using System.Security.Claims;

namespace MorphDB.Core.Security;

/// <summary>
/// Represents the security context for the current request.
/// </summary>
public sealed class SecurityContext
{
    /// <summary>
    /// Gets or sets the project ID.
    /// </summary>
    public Guid ProjectId { get; set; }

    /// <summary>
    /// Gets or sets the end-user ID, when the caller supplies one.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Gets or sets the end-user's email, when the caller supplies one.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Gets or sets the end-user's role, when the caller supplies one.
    /// </summary>
    public string? Role { get; set; }

    /// <summary>
    /// Gets or sets whether the context carries an identified end-user.
    /// </summary>
    public bool IsAuthenticated { get; set; }

    /// <summary>
    /// Gets or sets whether RLS should be bypassed (trusted in-process callers).
    /// </summary>
    public bool BypassRls { get; set; }

    /// <summary>
    /// Gets or sets the claims principal, when the caller supplies one.
    /// </summary>
    public ClaimsPrincipal? Principal { get; set; }

    /// <summary>
    /// Gets or sets additional custom claims.
    /// </summary>
    public Dictionary<string, string> Claims { get; set; } = [];

    /// <summary>
    /// Creates an anonymous security context.
    /// </summary>
    public static SecurityContext Anonymous(Guid projectId) =>
        new()
        {
            ProjectId = projectId,
            IsAuthenticated = false,
            BypassRls = false
        };

    /// <summary>
    /// Creates a service security context (bypasses RLS) for trusted in-process callers.
    /// </summary>
    public static SecurityContext Service(Guid projectId) =>
        new()
        {
            ProjectId = projectId,
            IsAuthenticated = true,
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
