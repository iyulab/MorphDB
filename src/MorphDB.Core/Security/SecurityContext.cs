using System.Security.Claims;

namespace MorphDB.Core.Security;

/// <summary>
/// Represents the security context for the current request.
/// </summary>
public sealed class SecurityContext
{
    /// <summary>
    /// Gets or sets the tenant ID.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the authenticated user ID (from JWT sub claim).
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Gets or sets the user's email (from JWT email claim).
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Gets or sets the user's role (from JWT role claim).
    /// </summary>
    public string? Role { get; set; }

    /// <summary>
    /// Gets or sets the API key type used for authentication.
    /// </summary>
    public ApiKeyType? KeyType { get; set; }

    /// <summary>
    /// Gets or sets whether the request is authenticated.
    /// </summary>
    public bool IsAuthenticated { get; set; }

    /// <summary>
    /// Gets or sets whether RLS should be bypassed (service key).
    /// </summary>
    public bool BypassRls { get; set; }

    /// <summary>
    /// Gets or sets the claims principal from JWT.
    /// </summary>
    public ClaimsPrincipal? Principal { get; set; }

    /// <summary>
    /// Gets or sets additional custom claims.
    /// </summary>
    public Dictionary<string, string> Claims { get; set; } = [];

    /// <summary>
    /// Creates an anonymous security context.
    /// </summary>
    public static SecurityContext Anonymous(Guid tenantId) =>
        new()
        {
            TenantId = tenantId,
            IsAuthenticated = false,
            KeyType = ApiKeyType.Anon,
            BypassRls = false
        };

    /// <summary>
    /// Creates a service security context (bypasses RLS).
    /// </summary>
    public static SecurityContext Service(Guid tenantId) =>
        new()
        {
            TenantId = tenantId,
            IsAuthenticated = true,
            KeyType = ApiKeyType.Service,
            BypassRls = true,
            Role = "service"
        };

    /// <summary>
    /// Gets a claim value by name.
    /// </summary>
    public string? GetClaim(string claimType)
    {
        if (Claims.TryGetValue(claimType, out var value))
            return value;

        return Principal?.FindFirst(claimType)?.Value;
    }
}

/// <summary>
/// Provides access to the current security context.
/// </summary>
public interface ISecurityContextAccessor
{
    /// <summary>
    /// Gets the current security context.
    /// </summary>
    SecurityContext Context { get; }

    /// <summary>
    /// Gets the current security context or null if not available.
    /// </summary>
    SecurityContext? ContextOrNull { get; }

    /// <summary>
    /// Sets the security context for the current request.
    /// </summary>
    void SetContext(SecurityContext context);
}
