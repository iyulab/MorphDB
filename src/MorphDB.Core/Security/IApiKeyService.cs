namespace MorphDB.Core.Security;

/// <summary>
/// Service for managing API keys.
/// </summary>
public interface IApiKeyService
{
    /// <summary>
    /// Creates a new API key for a project.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="keyType">The key type (anon or service).</param>
    /// <param name="name">Display name for the key.</param>
    /// <param name="description">Optional description.</param>
    /// <param name="expiresAt">Optional expiration date.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created API key with the raw key value (only shown once).</returns>
    Task<(ApiKey Key, string RawKey)> CreateKeyAsync(
        Guid projectId,
        ApiKeyType keyType,
        string name,
        string? description = null,
        DateTimeOffset? expiresAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an API key.
    /// </summary>
    /// <param name="rawKey">The raw API key to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validation result.</returns>
    Task<ApiKeyValidationResult> ValidateKeyAsync(
        string rawKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all API keys for a project.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of API keys (without raw key values).</returns>
    Task<IReadOnlyList<ApiKey>> GetKeysAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes (deactivates) an API key.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="keyId">The key ID to revoke.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RevokeKeyAsync(
        Guid projectId,
        Guid keyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotates an API key (creates new key, optionally revokes old).
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="keyId">The key ID to rotate.</param>
    /// <param name="revokeOld">Whether to revoke the old key immediately.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new API key with the raw key value.</returns>
    Task<(ApiKey Key, string RawKey)> RotateKeyAsync(
        Guid projectId,
        Guid keyId,
        bool revokeOld = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the last used timestamp for a key.
    /// </summary>
    /// <param name="keyId">The key ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateLastUsedAsync(
        Guid keyId,
        CancellationToken cancellationToken = default);
}
