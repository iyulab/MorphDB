namespace MorphDB.Core.Encryption;

/// <summary>
/// Service for transparent data encryption and decryption.
/// Uses envelope encryption with per-project, per-table key derivation.
/// </summary>
public interface IDataEncryptionService
{
    /// <summary>
    /// Gets whether encryption is enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Encrypts a value for storage.
    /// </summary>
    /// <param name="projectId">The project ID for key derivation.</param>
    /// <param name="tableName">The table name for key derivation.</param>
    /// <param name="columnName">The column name for additional context.</param>
    /// <param name="plaintext">The value to encrypt.</param>
    /// <returns>The encrypted value as a base64 string with metadata prefix.</returns>
    string Encrypt(Guid projectId, string tableName, string columnName, string plaintext);

    /// <summary>
    /// Decrypts a value from storage.
    /// </summary>
    /// <param name="projectId">The project ID for key derivation.</param>
    /// <param name="tableName">The table name for key derivation.</param>
    /// <param name="columnName">The column name for additional context.</param>
    /// <param name="ciphertext">The encrypted value.</param>
    /// <returns>The decrypted plaintext.</returns>
    string Decrypt(Guid projectId, string tableName, string columnName, string ciphertext);

    /// <summary>
    /// Encrypts binary data for storage.
    /// </summary>
    byte[] EncryptBytes(Guid projectId, string tableName, string columnName, byte[] plaintext);

    /// <summary>
    /// Decrypts binary data from storage.
    /// </summary>
    byte[] DecryptBytes(Guid projectId, string tableName, string columnName, byte[] ciphertext);

    /// <summary>
    /// Encrypts a dictionary of values (for row-level encryption).
    /// Only encrypts values for columns marked as encrypted in metadata.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="data">The data to encrypt.</param>
    /// <param name="encryptedColumns">Column names that should be encrypted.</param>
    /// <returns>The data with encrypted values.</returns>
    IDictionary<string, object?> EncryptRow(
        Guid projectId,
        string tableName,
        IDictionary<string, object?> data,
        IReadOnlySet<string> encryptedColumns);

    /// <summary>
    /// Decrypts a dictionary of values (for row-level decryption).
    /// </summary>
    IDictionary<string, object?> DecryptRow(
        Guid projectId,
        string tableName,
        IDictionary<string, object?> data,
        IReadOnlySet<string> encryptedColumns);
}

/// <summary>
/// Configuration options for data encryption.
/// </summary>
public class DataEncryptionOptions
{
    /// <summary>
    /// Gets or sets whether encryption is enabled.
    /// Default: true when MasterKey is configured.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the master encryption key (base64 encoded, 32 bytes for AES-256).
    /// This should be stored securely (environment variable, vault, etc.).
    /// </summary>
    public string? MasterKey { get; set; }

    /// <summary>
    /// Gets or sets the key version for key rotation support.
    /// </summary>
    public int KeyVersion { get; set; } = 1;

    /// <summary>
    /// Gets or sets the algorithm identifier for forward compatibility.
    /// </summary>
    public string Algorithm { get; set; } = "AES-256-GCM";

    /// <summary>
    /// Gets or sets whether to encrypt all columns by default.
    /// If false, only columns explicitly marked as encrypted will be encrypted.
    /// </summary>
    public bool EncryptAllByDefault { get; set; } = true;

    /// <summary>
    /// Gets or sets columns that should never be encrypted (e.g., id, project_id, timestamps).
    /// </summary>
    public HashSet<string> ExcludedColumns { get; set; } =
    [
        "id",
        "project_id",
        "created_at",
        "updated_at",
        "created_by",
        "updated_by"
    ];
}
