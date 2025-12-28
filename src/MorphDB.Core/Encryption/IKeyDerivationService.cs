namespace MorphDB.Core.Encryption;

/// <summary>
/// Service for cryptographic key derivation using HKDF (HMAC-based Key Derivation Function).
/// Derives unique keys per tenant and table from a single master key.
/// </summary>
public interface IKeyDerivationService
{
    /// <summary>
    /// Derives a tenant-specific key from the master key.
    /// </summary>
    /// <param name="tenantId">The tenant ID used as salt.</param>
    /// <returns>A 256-bit key unique to this tenant.</returns>
    byte[] DeriveTenantKey(Guid tenantId);

    /// <summary>
    /// Derives a table-specific key from the tenant key.
    /// </summary>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="tableName">The table name used as info parameter.</param>
    /// <returns>A 256-bit key unique to this tenant and table.</returns>
    byte[] DeriveTableKey(Guid tenantId, string tableName);

    /// <summary>
    /// Derives a column-specific key from the table key.
    /// Used for fine-grained column-level encryption.
    /// </summary>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column name.</param>
    /// <returns>A 256-bit key unique to this column.</returns>
    byte[] DeriveColumnKey(Guid tenantId, string tableName, string columnName);

    /// <summary>
    /// Gets the current key version for rotation support.
    /// </summary>
    int CurrentKeyVersion { get; }
}

/// <summary>
/// Result of key derivation containing the key and metadata.
/// </summary>
public sealed class DerivedKey
{
    /// <summary>
    /// The derived key bytes.
    /// </summary>
    public required byte[] Key { get; init; }

    /// <summary>
    /// The key version for rotation support.
    /// </summary>
    public required int Version { get; init; }

    /// <summary>
    /// The derivation context (for debugging/auditing).
    /// </summary>
    public required string Context { get; init; }
}
