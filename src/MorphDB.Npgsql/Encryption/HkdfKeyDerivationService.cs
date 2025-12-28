using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MorphDB.Core.Encryption;

namespace MorphDB.Npgsql.Encryption;

/// <summary>
/// HKDF-based key derivation service for envelope encryption.
/// Derives unique encryption keys per tenant/table/column from a master key.
/// </summary>
/// <remarks>
/// Key Hierarchy:
///   Master Key (MK)
///     └── Tenant Key (TK) = HKDF(MK, salt=TenantId)
///           └── Table Key (TEK) = HKDF(TK, info=TableName)
///                 └── Column Key (CEK) = HKDF(TEK, info=ColumnName)
///
/// This hierarchy ensures:
/// - Each tenant has isolated encryption keys
/// - Each table within a tenant has a unique key
/// - Column-level granularity for sensitive data
/// - Key rotation can be done at any level
/// </remarks>
public sealed class HkdfKeyDerivationService : IKeyDerivationService
{
    private const int KeySizeBytes = 32; // 256 bits for AES-256
    private readonly byte[] _masterKey;
    private readonly int _keyVersion;

    public HkdfKeyDerivationService(IOptions<DataEncryptionOptions> options)
    {
        var config = options.Value;

        if (string.IsNullOrEmpty(config.MasterKey))
        {
            throw new InvalidOperationException(
                "Encryption master key is not configured. " +
                "Set 'Encryption:MasterKey' in configuration or environment variable.");
        }

        _masterKey = Convert.FromBase64String(config.MasterKey);

        if (_masterKey.Length < KeySizeBytes)
        {
            throw new InvalidOperationException(
                $"Master key must be at least {KeySizeBytes} bytes (256 bits). " +
                $"Current key is {_masterKey.Length} bytes.");
        }

        _keyVersion = config.KeyVersion;
    }

    /// <inheritdoc />
    public int CurrentKeyVersion => _keyVersion;

    /// <inheritdoc />
    public byte[] DeriveTenantKey(Guid tenantId)
    {
        // Use tenant ID bytes as salt for tenant-level key derivation
        var salt = tenantId.ToByteArray();
        var info = Encoding.UTF8.GetBytes($"morphdb:tenant:v{_keyVersion}");

        return DeriveKey(_masterKey, salt, info);
    }

    /// <inheritdoc />
    public byte[] DeriveTableKey(Guid tenantId, string tableName)
    {
        var tenantKey = DeriveTenantKey(tenantId);
        var salt = Encoding.UTF8.GetBytes($"table:{tableName}");
        var info = Encoding.UTF8.GetBytes($"morphdb:table:v{_keyVersion}");

        try
        {
            return DeriveKey(tenantKey, salt, info);
        }
        finally
        {
            // Clear tenant key from memory
            CryptographicOperations.ZeroMemory(tenantKey);
        }
    }

    /// <inheritdoc />
    public byte[] DeriveColumnKey(Guid tenantId, string tableName, string columnName)
    {
        var tableKey = DeriveTableKey(tenantId, tableName);
        var salt = Encoding.UTF8.GetBytes($"column:{columnName}");
        var info = Encoding.UTF8.GetBytes($"morphdb:column:v{_keyVersion}");

        try
        {
            return DeriveKey(tableKey, salt, info);
        }
        finally
        {
            // Clear table key from memory
            CryptographicOperations.ZeroMemory(tableKey);
        }
    }

    /// <summary>
    /// Derives a key using HKDF (HMAC-based Key Derivation Function).
    /// </summary>
    private static byte[] DeriveKey(byte[] inputKeyMaterial, byte[] salt, byte[] info)
    {
        // HKDF-SHA256 extract-then-expand
        return HKDF.DeriveKey(
            hashAlgorithmName: HashAlgorithmName.SHA256,
            ikm: inputKeyMaterial,
            outputLength: KeySizeBytes,
            salt: salt,
            info: info);
    }
}
