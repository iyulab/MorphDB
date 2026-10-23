using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MorphDB.Core.Security;
using MorphDB.Service.Models.Api;

namespace MorphDB.Service.Security;

/// <summary>
/// Authenticates the credential a request presents: a connection secret, enforced when a deployment
/// has injected a master secret, or a person's access token from the issuer it trusts
/// (<see cref="TokenOptions"/>).
/// <para>
/// Both arrive as <c>Authorization: Bearer</c>. A value that parses as a JSON Web Token is a token
/// when tokens are accepted, and anything else is a secret. A token is checked by the framework's
/// JWT bearer handler and runs as the person it names — subject, role and claims fill the policy
/// placeholders, and it never bypasses row-level security. A token that fails is refused outright,
/// never read as anonymous: a caller that meant to be someone must not silently become no one.
/// </para>
/// <para>
/// This is a middleware of its own rather than a branch inside
/// <see cref="SecurityContextMiddleware"/>, and the distinction is load-bearing: that middleware
/// only acts when <c>X-Project-Id</c> is present and parses, so an enforcement check placed inside
/// it could be skipped by simply omitting the header. This one runs on every request that is not
/// explicitly exempt, whatever headers it carries.
/// </para>
/// <para>
/// It also runs before rate limiting and audit logging, so a rejected request is still counted and
/// still recorded — a denial nobody can see is not much of a boundary.
/// </para>
/// </summary>
public sealed class CredentialAuthenticationMiddleware
{
    /// <summary>
    /// The header carrying the secret.
    /// </summary>
    public const string HeaderName = "Authorization";

    private const string BearerPrefix = "Bearer ";

    /// <summary>
    /// Paths that answer without a secret even while enforcement is on.
    /// <para>
    /// Both are machine surfaces that must work before any credential is distributed: an
    /// orchestrator decides whether to route traffic to this container by polling health, and a
    /// scraper collects metrics on a schedule. Neither reads or writes project data. This list is
    /// the service's entire unauthenticated attack surface, so it is asserted by a test and is not
    /// extended without one.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> ExemptPathPrefixes =
    [
        "/health",
        "/metrics"
    ];

    private readonly RequestDelegate _next;

    public CredentialAuthenticationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        SecretOptions options,
        TokenOptions tokens,
        ISecretService secrets,
        ISecurityContextAccessor securityContextAccessor)
    {
        if (IsExempt(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var presented = ExtractBearer(context.Request);

        if (tokens.IsEnabled && presented is not null && LooksLikeToken(presented))
        {
            await AuthenticateTokenAsync(context, presented, tokens, securityContextAccessor);
            return;
        }

        if (!options.IsEnforced)
        {
            await _next(context);
            return;
        }

        if (presented is null)
        {
            await DenyAsync(context, StatusCodes.Status401Unauthorized, "UNAUTHENTICATED",
                "This request presented no credential. Send an Authorization: Bearer <secret or token> header.");
            return;
        }

        var secret = await secrets.AuthenticateAsync(presented, context.RequestAborted);
        if (secret is null)
        {
            // The same answer for an unknown secret and a revoked one: distinguishing them would
            // tell an attacker which of their guesses once existed.
            await DenyAsync(context, StatusCodes.Status401Unauthorized, "UNAUTHENTICATED",
                "This secret is not recognized.");
            return;
        }

        // A confined secret reaches one project only, so a request that names none asks for that one —
        // the same answer as naming it in X-Project-Id. Without this default such a request would be
        // refused as addressing "a different project" when it addressed none.
        var requestedProject = securityContextAccessor.ContextOrNull?.ProjectId ?? secret.ProjectId ?? Guid.Empty;

        // A secret confined to a project is confined by this check and nowhere else. Storing the
        // column without enforcing it would be a boundary that exists only in the schema.
        if (secret.ProjectId is { } confinedTo && requestedProject != confinedTo)
        {
            await DenyAsync(context, StatusCodes.Status403Forbidden, "FORBIDDEN",
                "This secret is confined to a different project.");
            return;
        }

        securityContextAccessor.SetContext(new SecurityContext
        {
            ProjectId = requestedProject,
            IsAuthenticated = true,
            // Only the injected master secret bypasses row-level security. An issued secret is
            // subject to the same policies an anonymous caller is, with {{role}} now filled in.
            BypassRls = string.Equals(secret.Role, SecretRoles.Master, StringComparison.Ordinal),
            Role = secret.Role,
            ConfinedToProjectId = secret.ProjectId,
            UserId = secret.SecretId == Guid.Empty ? null : secret.SecretId.ToString()
        });

        await _next(context);
    }

    private static bool IsExempt(PathString path) =>
        ExemptPathPrefixes.Any(prefix =>
            path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    private async Task AuthenticateTokenAsync(
        HttpContext context,
        string token,
        TokenOptions tokens,
        ISecurityContextAccessor securityContextAccessor)
    {
        var result = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        if (!result.Succeeded || result.Principal is null)
        {
            // Why it failed (signature, issuer, audience, expiry) is logged by the handler; the
            // caller learns only that the token was not accepted.
            await DenyAsync(context, StatusCodes.Status401Unauthorized, "UNAUTHENTICATED",
                "This token is not valid.");
            return;
        }

        var principal = result.Principal;
        var role = principal.FindFirst(tokens.RoleClaim)?.Value;
        if (role is not null && SecretRoles.Reserved.Contains(role))
        {
            await DenyAsync(context, StatusCodes.Status403Forbidden, "FORBIDDEN",
                $"A token may not carry the reserved role '{role}'.");
            return;
        }

        securityContextAccessor.SetContext(new SecurityContext
        {
            ProjectId = securityContextAccessor.ContextOrNull?.ProjectId ?? Guid.Empty,
            IsAuthenticated = true,
            BypassRls = false,
            Role = role,
            UserId = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
            Email = principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value,
            Principal = principal,
            Claims = ClaimsOf(token)
        });

        await _next(context);
    }

    /// <summary>
    /// The token's payload as <c>{{claims.x}}</c> sees it: each top-level claim by its name, strings
    /// as they are and anything else (numbers, booleans, arrays, objects) as its JSON text. Read from
    /// the token itself rather than the principal, which splits arrays into repeated claims.
    /// </summary>
    private static Dictionary<string, string> ClaimsOf(string token)
    {
        var payload = Base64UrlEncoder.DecodeBytes(new JsonWebToken(token).EncodedPayload);
        using var document = JsonDocument.Parse(payload);

        var claims = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            claims[property.Name] = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()!
                : property.Value.GetRawText();
        }

        return claims;
    }

    private static bool LooksLikeToken(string presented) =>
        TokenHandler.CanReadToken(presented);

    private static readonly JsonWebTokenHandler TokenHandler = new();

    private static string? ExtractBearer(HttpRequest request)
    {
        if (!request.Headers.TryGetValue(HeaderName, out var values))
        {
            return null;
        }

        var header = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(header) ||
            !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var presented = header[BearerPrefix.Length..].Trim();
        return string.IsNullOrEmpty(presented) ? null : presented;
    }

    private static Task DenyAsync(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(OData.ErrorWire.Body(context.Request, new ErrorResponse
        {
            Error = status == StatusCodes.Status401Unauthorized ? "Unauthenticated" : "Forbidden",
            Message = message,
            Code = code
        }));
    }
}

/// <summary>
/// Pipeline registration for <see cref="CredentialAuthenticationMiddleware"/>.
/// </summary>
public static class CredentialAuthenticationMiddlewareExtensions
{
    public static IApplicationBuilder UseCredentialAuthentication(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CredentialAuthenticationMiddleware>();
    }
}
