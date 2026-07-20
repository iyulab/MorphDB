using System.Security.Claims;

namespace MorphDB.Core.Security;

/// <summary>
/// JWT token validation options.
/// </summary>
public sealed class JwtOptions
{
    /// <summary>
    /// Gets or sets the JWT secret key.
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the issuer.
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>
    /// Gets or sets the audience.
    /// </summary>
    public string? Audience { get; set; }

    /// <summary>
    /// Gets or sets whether to validate the issuer.
    /// </summary>
    public bool ValidateIssuer { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to validate the audience.
    /// </summary>
    public bool ValidateAudience { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to validate the lifetime.
    /// </summary>
    public bool ValidateLifetime { get; set; } = true;

    /// <summary>
    /// Gets or sets the clock skew tolerance.
    /// </summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Result of JWT token validation.
/// </summary>
public sealed class JwtValidationResult
{
    /// <summary>
    /// Gets or sets whether the token is valid.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Gets or sets the claims principal from the validated token.
    /// </summary>
    public ClaimsPrincipal? Principal { get; set; }

    /// <summary>
    /// Gets or sets the error message if validation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static JwtValidationResult Success(ClaimsPrincipal principal) =>
        new() { IsValid = true, Principal = principal };

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    public static JwtValidationResult Failure(string message) =>
        new() { IsValid = false, ErrorMessage = message };
}

/// <summary>
/// Service for JWT token operations.
/// </summary>
public interface IJwtService
{
    /// <summary>
    /// Validates a JWT token.
    /// </summary>
    /// <param name="token">The JWT token to validate.</param>
    /// <returns>The validation result.</returns>
    JwtValidationResult ValidateToken(string token);

    /// <summary>
    /// Generates a JWT token for the given claims.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="userId">The user ID (sub claim).</param>
    /// <param name="email">Optional email claim.</param>
    /// <param name="role">Optional role claim.</param>
    /// <param name="additionalClaims">Additional claims to include.</param>
    /// <param name="expiresIn">Token expiration time (default: 1 hour).</param>
    /// <returns>The generated JWT token.</returns>
    string GenerateToken(
        Guid projectId,
        string userId,
        string? email = null,
        string? role = null,
        IDictionary<string, string>? additionalClaims = null,
        TimeSpan? expiresIn = null);

    /// <summary>
    /// Extracts claims from a JWT token without full validation.
    /// </summary>
    /// <param name="token">The JWT token.</param>
    /// <returns>The claims principal or null if token is invalid.</returns>
    ClaimsPrincipal? ExtractClaims(string token);
}
