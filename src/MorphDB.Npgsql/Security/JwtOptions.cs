namespace MorphDB.Npgsql.Security;

/// <summary>
/// Options for JWT token generation and validation.
/// </summary>
public sealed class JwtOptions
{
    /// <summary>
    /// Secret key for signing tokens (minimum 32 characters for HMAC-SHA256).
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Token issuer.
    /// </summary>
    public string Issuer { get; set; } = "MorphDB";

    /// <summary>
    /// Token audience.
    /// </summary>
    public string Audience { get; set; } = "MorphDB";

    /// <summary>
    /// Access token expiration time in minutes (for config binding).
    /// </summary>
    public int AccessTokenExpirationMinutes { get; set; } = 60;

    /// <summary>
    /// Access token expiration time.
    /// </summary>
    public TimeSpan AccessTokenExpiration => TimeSpan.FromMinutes(AccessTokenExpirationMinutes);

    /// <summary>
    /// Refresh token expiration time in days (for config binding).
    /// </summary>
    public int RefreshTokenExpirationDays { get; set; } = 7;

    /// <summary>
    /// Refresh token expiration time.
    /// </summary>
    public TimeSpan RefreshTokenExpiration => TimeSpan.FromDays(RefreshTokenExpirationDays);

    /// <summary>
    /// Whether to validate the issuer.
    /// </summary>
    public bool ValidateIssuer { get; set; } = true;

    /// <summary>
    /// Whether to validate the audience.
    /// </summary>
    public bool ValidateAudience { get; set; } = true;

    /// <summary>
    /// Whether to validate the token lifetime.
    /// </summary>
    public bool ValidateLifetime { get; set; } = true;

    /// <summary>
    /// Clock skew tolerance in minutes (for config binding).
    /// </summary>
    public int ClockSkewMinutes { get; set; } = 5;

    /// <summary>
    /// Clock skew tolerance for token validation.
    /// </summary>
    public TimeSpan ClockSkew => TimeSpan.FromMinutes(ClockSkewMinutes);
}
