using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using MorphDB.Core.Security;

namespace MorphDB.Service.Security;

/// <summary>
/// Options for MorphDB authentication.
/// </summary>
public sealed class MorphDBAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// The header name for API key.
    /// </summary>
    public string ApiKeyHeaderName { get; set; } = "X-API-Key";

    /// <summary>
    /// The header name for tenant ID.
    /// </summary>
    public string TenantIdHeaderName { get; set; } = "X-Tenant-Id";
}

/// <summary>
/// Authentication handler for MorphDB API key and JWT.
/// </summary>
public sealed class MorphDBAuthenticationHandler : AuthenticationHandler<MorphDBAuthenticationOptions>
{
    private readonly IApiKeyService _apiKeyService;
    private readonly IJwtService _jwtService;
    private readonly ISecurityContextAccessor _securityContextAccessor;

    public MorphDBAuthenticationHandler(
        IOptionsMonitor<MorphDBAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiKeyService apiKeyService,
        IJwtService jwtService,
        ISecurityContextAccessor securityContextAccessor)
        : base(options, logger, encoder)
    {
        _apiKeyService = apiKeyService;
        _jwtService = jwtService;
        _securityContextAccessor = securityContextAccessor;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Get tenant ID from header
        if (!Request.Headers.TryGetValue(Options.TenantIdHeaderName, out var tenantIdHeader) ||
            !Guid.TryParse(tenantIdHeader.FirstOrDefault(), out var tenantId))
        {
            // Tenant ID is optional for some endpoints
            tenantId = Guid.Empty;
        }

        // Try JWT first (from Authorization header)
        var authorizationHeader = Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrEmpty(authorizationHeader) && authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authorizationHeader["Bearer ".Length..].Trim();
            var jwtResult = _jwtService.ValidateToken(token);

            if (jwtResult.IsValid && jwtResult.Principal != null)
            {
                var securityContext = CreateSecurityContextFromJwt(tenantId, jwtResult.Principal);
                _securityContextAccessor.SetContext(securityContext);

                var ticket = new AuthenticationTicket(jwtResult.Principal, Scheme.Name);
                return AuthenticateResult.Success(ticket);
            }

            return AuthenticateResult.Fail(jwtResult.ErrorMessage ?? "Invalid JWT token");
        }

        // Try API Key
        if (Request.Headers.TryGetValue(Options.ApiKeyHeaderName, out var apiKeyHeader))
        {
            var apiKey = apiKeyHeader.FirstOrDefault();
            if (!string.IsNullOrEmpty(apiKey))
            {
                var result = await _apiKeyService.ValidateKeyAsync(apiKey);

                if (result.IsValid && result.ApiKey != null)
                {
                    // Verify tenant ID matches (if provided)
                    if (tenantId != Guid.Empty && result.ApiKey.TenantId != tenantId)
                    {
                        return AuthenticateResult.Fail("API key does not match tenant ID");
                    }

                    var securityContext = CreateSecurityContextFromApiKey(result.ApiKey);
                    _securityContextAccessor.SetContext(securityContext);

                    var claims = new List<Claim>
                    {
                        new(ClaimTypes.NameIdentifier, result.ApiKey.Id.ToString()),
                        new("tenant_id", result.ApiKey.TenantId.ToString()),
                        new("key_type", result.ApiKey.KeyType.ToString())
                    };

                    if (result.ApiKey.KeyType == ApiKeyType.Service)
                    {
                        claims.Add(new Claim(ClaimTypes.Role, "service"));
                    }

                    var identity = new ClaimsIdentity(claims, Scheme.Name);
                    var principal = new ClaimsPrincipal(identity);
                    var ticket = new AuthenticationTicket(principal, Scheme.Name);

                    return AuthenticateResult.Success(ticket);
                }

                return AuthenticateResult.Fail(result.ErrorMessage ?? "Invalid API key");
            }
        }

        // No credentials provided - allow anonymous for now (may be restricted at authorization level)
        if (tenantId != Guid.Empty)
        {
            var securityContext = SecurityContext.Anonymous(tenantId);
            _securityContextAccessor.SetContext(securityContext);
        }

        return AuthenticateResult.NoResult();
    }

    private static SecurityContext CreateSecurityContextFromJwt(Guid tenantId, ClaimsPrincipal principal)
    {
        var claims = new Dictionary<string, string>();
        foreach (var claim in principal.Claims)
        {
            claims[claim.Type] = claim.Value;
        }

        // Try to get tenant ID from claims if not provided in header
        if (tenantId == Guid.Empty)
        {
            var tenantClaim = principal.FindFirst("tenant_id");
            if (tenantClaim != null && Guid.TryParse(tenantClaim.Value, out var claimTenantId))
            {
                tenantId = claimTenantId;
            }
        }

        return new SecurityContext
        {
            TenantId = tenantId,
            UserId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                     principal.FindFirst("sub")?.Value,
            Email = principal.FindFirst(ClaimTypes.Email)?.Value ??
                    principal.FindFirst("email")?.Value,
            Role = principal.FindFirst(ClaimTypes.Role)?.Value ??
                   principal.FindFirst("role")?.Value,
            IsAuthenticated = true,
            BypassRls = principal.IsInRole("service") || principal.IsInRole("admin"),
            Principal = principal,
            Claims = claims
        };
    }

    private static SecurityContext CreateSecurityContextFromApiKey(ApiKey apiKey)
    {
        return apiKey.KeyType == ApiKeyType.Service
            ? SecurityContext.Service(apiKey.TenantId)
            : SecurityContext.Anonymous(apiKey.TenantId);
    }
}

/// <summary>
/// Extension methods for MorphDB authentication.
/// </summary>
public static class MorphDBAuthenticationExtensions
{
    public const string SchemeName = "MorphDB";

    /// <summary>
    /// Adds MorphDB authentication to the authentication builder.
    /// </summary>
    public static AuthenticationBuilder AddMorphDB(
        this AuthenticationBuilder builder,
        Action<MorphDBAuthenticationOptions>? configure = null)
    {
        return builder.AddScheme<MorphDBAuthenticationOptions, MorphDBAuthenticationHandler>(
            SchemeName,
            configure ?? (_ => { }));
    }
}
