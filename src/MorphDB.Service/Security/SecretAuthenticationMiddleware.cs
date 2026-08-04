using MorphDB.Core.Security;
using MorphDB.Service.Models.Api;

namespace MorphDB.Service.Security;

/// <summary>
/// Enforces connection secrets, when a deployment has injected a master secret.
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
public sealed class SecretAuthenticationMiddleware
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

    public SecretAuthenticationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        SecretOptions options,
        ISecretService secrets,
        ISecurityContextAccessor securityContextAccessor)
    {
        if (!options.IsEnforced || IsExempt(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var presented = ExtractSecret(context.Request);
        if (presented is null)
        {
            await DenyAsync(context, StatusCodes.Status401Unauthorized, "UNAUTHENTICATED",
                "This request presented no secret. Send an Authorization: Bearer <secret> header.");
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

        var requestedProject = securityContextAccessor.ContextOrNull?.ProjectId ?? Guid.Empty;

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
            UserId = secret.SecretId == Guid.Empty ? null : secret.SecretId.ToString()
        });

        await _next(context);
    }

    private static bool IsExempt(PathString path) =>
        ExemptPathPrefixes.Any(prefix =>
            path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    private static string? ExtractSecret(HttpRequest request)
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
        return context.Response.WriteAsJsonAsync(new ErrorResponse
        {
            Error = status == StatusCodes.Status401Unauthorized ? "Unauthenticated" : "Forbidden",
            Message = message,
            Code = code
        });
    }
}

/// <summary>
/// Pipeline registration for <see cref="SecretAuthenticationMiddleware"/>.
/// </summary>
public static class SecretAuthenticationMiddlewareExtensions
{
    public static IApplicationBuilder UseSecretAuthentication(this IApplicationBuilder app)
    {
        return app.UseMiddleware<SecretAuthenticationMiddleware>();
    }
}
