namespace MorphDB.Core.Security;

/// <summary>
/// Represents an API key type.
/// </summary>
public enum ApiKeyType
{
    /// <summary>
    /// Anonymous key for public/client-side use with RLS enforcement.
    /// </summary>
    Anon = 0,

    /// <summary>
    /// Service key for server-side use with full access.
    /// </summary>
    Service = 1
}

/// <summary>
/// Represents an API key for project authentication.
/// </summary>
public sealed class ApiKey
{
    /// <summary>
    /// Gets or sets the API key ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the project ID this key belongs to.
    /// </summary>
    public Guid ProjectId { get; set; }

    /// <summary>
    /// Gets or sets the key type (anon or service).
    /// </summary>
    public ApiKeyType KeyType { get; set; }

    /// <summary>
    /// Gets or sets the hashed key value.
    /// </summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the key prefix for identification (first 8 chars).
    /// </summary>
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name for this key.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a description for this key.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets whether the key is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets when the key was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets when the key expires (null for never).
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>
    /// Gets or sets when the key was last used.
    /// </summary>
    public DateTimeOffset? LastUsedAt { get; set; }
}

/// <summary>
/// Result of API key validation.
/// </summary>
public sealed class ApiKeyValidationResult
{
    /// <summary>
    /// Gets or sets whether the key is valid.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Gets or sets the validated API key info.
    /// </summary>
    public ApiKey? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the error message if validation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static ApiKeyValidationResult Success(ApiKey apiKey) =>
        new() { IsValid = true, ApiKey = apiKey };

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    public static ApiKeyValidationResult Failure(string message) =>
        new() { IsValid = false, ErrorMessage = message };
}
