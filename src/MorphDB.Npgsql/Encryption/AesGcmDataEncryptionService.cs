using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MorphDB.Core.Encryption;

namespace MorphDB.Npgsql.Encryption;

/// <summary>
/// AES-256-GCM based data encryption service with automatic key derivation.
/// Provides authenticated encryption ensuring both confidentiality and integrity.
/// </summary>
/// <remarks>
/// Encrypted format (binary):
///   [Version: 1 byte][KeyVersion: 2 bytes][Nonce: 12 bytes][Tag: 16 bytes][Ciphertext: N bytes]
///
/// Encrypted format (string):
///   $MORPH$v1$<base64(binary)>
///
/// Features:
/// - AES-256-GCM authenticated encryption (confidentiality + integrity)
/// - Per-table unique encryption keys via HKDF
/// - Key version tracking for rotation support
/// - Automatic nonce generation (12 bytes, cryptographically random)
/// - Base64 encoding for string storage
/// </remarks>
public sealed partial class AesGcmDataEncryptionService : IDataEncryptionService
{
    private const byte FormatVersion = 1;
    private const int NonceSizeBytes = 12; // 96 bits, recommended for GCM
    private const int TagSizeBytes = 16;   // 128 bits authentication tag
    private const string EncryptedPrefix = "$MORPH$v1$";

    private readonly IKeyDerivationService _keyDerivation;
    private readonly DataEncryptionOptions _options;
    private readonly ILogger<AesGcmDataEncryptionService> _logger;

    public AesGcmDataEncryptionService(
        IKeyDerivationService keyDerivation,
        IOptions<DataEncryptionOptions> options,
        ILogger<AesGcmDataEncryptionService> logger)
    {
        _keyDerivation = keyDerivation;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsEnabled => _options.Enabled && !string.IsNullOrEmpty(_options.MasterKey);

    /// <inheritdoc />
    public string Encrypt(Guid projectId, string tableName, string columnName, string plaintext)
    {
        if (!IsEnabled)
            return plaintext;

        if (string.IsNullOrEmpty(plaintext))
            return plaintext;

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var encryptedBytes = EncryptBytes(projectId, tableName, columnName, plaintextBytes);

        return EncryptedPrefix + Convert.ToBase64String(encryptedBytes);
    }

    /// <inheritdoc />
    public string Decrypt(Guid projectId, string tableName, string columnName, string ciphertext)
    {
        if (!IsEnabled)
            return ciphertext;

        if (string.IsNullOrEmpty(ciphertext))
            return ciphertext;

        // Check if the value is encrypted
        if (!ciphertext.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
        {
            // Not encrypted, return as-is (backward compatibility)
            return ciphertext;
        }

        var base64Part = ciphertext[EncryptedPrefix.Length..];
        var encryptedBytes = Convert.FromBase64String(base64Part);
        var decryptedBytes = DecryptBytes(projectId, tableName, columnName, encryptedBytes);

        return Encoding.UTF8.GetString(decryptedBytes);
    }

    /// <inheritdoc />
    public byte[] EncryptBytes(Guid projectId, string tableName, string columnName, byte[] plaintext)
    {
        if (!IsEnabled)
            return plaintext;

        // Derive the encryption key for this table/column
        var key = _keyDerivation.DeriveTableKey(projectId, tableName);

        try
        {
            // Generate random nonce
            var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);

            // Prepare output buffer: version(1) + keyVersion(2) + nonce(12) + tag(16) + ciphertext(N)
            var outputLength = 1 + 2 + NonceSizeBytes + TagSizeBytes + plaintext.Length;
            var output = new byte[outputLength];

            // Write header
            output[0] = FormatVersion;
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(1, 2), (ushort)_keyDerivation.CurrentKeyVersion);
            nonce.CopyTo(output.AsSpan(3, NonceSizeBytes));

            // Encrypt using AES-GCM
            using var aesGcm = new AesGcm(key, TagSizeBytes);

            var tagSpan = output.AsSpan(3 + NonceSizeBytes, TagSizeBytes);
            var ciphertextSpan = output.AsSpan(3 + NonceSizeBytes + TagSizeBytes);

            aesGcm.Encrypt(
                nonce: nonce,
                plaintext: plaintext,
                ciphertext: ciphertextSpan,
                tag: tagSpan);

            return output;
        }
        finally
        {
            // Clear key from memory
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <inheritdoc />
    public byte[] DecryptBytes(Guid projectId, string tableName, string columnName, byte[] ciphertext)
    {
        if (!IsEnabled)
            return ciphertext;

        // Parse header
        if (ciphertext.Length < 1 + 2 + NonceSizeBytes + TagSizeBytes)
        {
            throw new CryptographicException("Invalid encrypted data format: too short");
        }

        var version = ciphertext[0];
        if (version != FormatVersion)
        {
            throw new CryptographicException($"Unsupported encryption format version: {version}");
        }

        var keyVersion = BinaryPrimitives.ReadUInt16BigEndian(ciphertext.AsSpan(1, 2));
        var nonce = ciphertext.AsSpan(3, NonceSizeBytes);
        var tag = ciphertext.AsSpan(3 + NonceSizeBytes, TagSizeBytes);
        var encryptedData = ciphertext.AsSpan(3 + NonceSizeBytes + TagSizeBytes);

        // Derive the decryption key
        // Note: For key rotation, we might need to look up the old key based on keyVersion
        var key = _keyDerivation.DeriveTableKey(projectId, tableName);

        try
        {
            var plaintext = new byte[encryptedData.Length];

            using var aesGcm = new AesGcm(key, TagSizeBytes);
            aesGcm.Decrypt(
                nonce: nonce,
                ciphertext: encryptedData,
                tag: tag,
                plaintext: plaintext);

            return plaintext;
        }
        catch (AuthenticationTagMismatchException ex)
        {
            LogDecryptionTagMismatch(tableName, ex);
            throw new CryptographicException("Decryption failed: data integrity check failed", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <inheritdoc />
    public IDictionary<string, object?> EncryptRow(
        Guid projectId,
        string tableName,
        IDictionary<string, object?> data,
        IReadOnlySet<string> encryptedColumns)
    {
        if (!IsEnabled || encryptedColumns.Count == 0)
            return data;

        var result = new Dictionary<string, object?>(data.Count);

        foreach (var (key, value) in data)
        {
            if (value == null || !encryptedColumns.Contains(key))
            {
                result[key] = value;
                continue;
            }

            // Convert value to string for encryption
            var stringValue = ConvertToString(value);
            result[key] = Encrypt(projectId, tableName, key, stringValue);
        }

        return result;
    }

    /// <inheritdoc />
    public IDictionary<string, object?> DecryptRow(
        Guid projectId,
        string tableName,
        IDictionary<string, object?> data,
        IReadOnlySet<string> encryptedColumns)
    {
        if (!IsEnabled || encryptedColumns.Count == 0)
            return data;

        var result = new Dictionary<string, object?>(data.Count);

        foreach (var (key, value) in data)
        {
            if (value == null || !encryptedColumns.Contains(key))
            {
                result[key] = value;
                continue;
            }

            if (value is string stringValue && stringValue.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
            {
                result[key] = Decrypt(projectId, tableName, key, stringValue);
            }
            else
            {
                result[key] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// Converts a value to its string representation for encryption.
    /// </summary>
    private static string ConvertToString(object value)
    {
        return value switch
        {
            string s => s,
            DateTime dt => dt.ToString("O"),
            DateTimeOffset dto => dto.ToString("O"),
            Guid g => g.ToString(),
            byte[] bytes => Convert.ToBase64String(bytes),
            _ when value.GetType().IsPrimitive => value.ToString() ?? string.Empty,
            _ => JsonSerializer.Serialize(value)
        };
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Decryption failed: authentication tag mismatch for table {TableName}")]
    private partial void LogDecryptionTagMismatch(string tableName, Exception ex);
}
