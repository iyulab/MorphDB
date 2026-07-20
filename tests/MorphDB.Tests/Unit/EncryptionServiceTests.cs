using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MorphDB.Core.Encryption;
using MorphDB.Npgsql.Encryption;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Unit tests for AesGcmDataEncryptionService and HkdfKeyDerivationService.
/// </summary>
[Trait("Category", "Unit")]
public class EncryptionServiceTests
{
    private const string ValidMasterKey = "dGhpcyBpcyBhIDMyIGJ5dGUga2V5IGZvciB0ZXN0cyE="; // 32 bytes base64
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProjectId2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private IKeyDerivationService CreateKeyDerivationService(int keyVersion = 1)
    {
        var options = Options.Create(new DataEncryptionOptions
        {
            Enabled = true,
            MasterKey = ValidMasterKey,
            KeyVersion = keyVersion
        });
        return new HkdfKeyDerivationService(options);
    }

    private AesGcmDataEncryptionService CreateEncryptionService(
        bool enabled = true,
        string? masterKey = ValidMasterKey,
        int keyVersion = 1)
    {
        var options = Options.Create(new DataEncryptionOptions
        {
            Enabled = enabled,
            MasterKey = masterKey,
            KeyVersion = keyVersion
        });
        var keyDerivation = new HkdfKeyDerivationService(options);
        var logger = new Mock<ILogger<AesGcmDataEncryptionService>>().Object;
        return new AesGcmDataEncryptionService(keyDerivation, options, logger);
    }

    #region Key Derivation Tests

    [Fact]
    public void DeriveProjectKey_SameProject_ShouldReturnConsistentKey()
    {
        // Arrange
        var service = CreateKeyDerivationService();

        // Act
        var key1 = service.DeriveProjectKey(ProjectId);
        var key2 = service.DeriveProjectKey(ProjectId);

        // Assert
        key1.Should().BeEquivalentTo(key2);
        key1.Should().HaveCount(32); // 256 bits
    }

    [Fact]
    public void DeriveProjectKey_DifferentProjects_ShouldReturnDifferentKeys()
    {
        // Arrange
        var service = CreateKeyDerivationService();

        // Act
        var key1 = service.DeriveProjectKey(ProjectId);
        var key2 = service.DeriveProjectKey(ProjectId2);

        // Assert
        key1.Should().NotBeEquivalentTo(key2);
    }

    [Fact]
    public void DeriveTableKey_SameTable_ShouldReturnConsistentKey()
    {
        // Arrange
        var service = CreateKeyDerivationService();

        // Act
        var key1 = service.DeriveTableKey(ProjectId, "customers");
        var key2 = service.DeriveTableKey(ProjectId, "customers");

        // Assert
        key1.Should().BeEquivalentTo(key2);
        key1.Should().HaveCount(32);
    }

    [Fact]
    public void DeriveTableKey_DifferentTables_ShouldReturnDifferentKeys()
    {
        // Arrange
        var service = CreateKeyDerivationService();

        // Act
        var key1 = service.DeriveTableKey(ProjectId, "customers");
        var key2 = service.DeriveTableKey(ProjectId, "orders");

        // Assert
        key1.Should().NotBeEquivalentTo(key2);
    }

    [Fact]
    public void DeriveTableKey_SameTableDifferentProjects_ShouldReturnDifferentKeys()
    {
        // Arrange
        var service = CreateKeyDerivationService();

        // Act
        var key1 = service.DeriveTableKey(ProjectId, "customers");
        var key2 = service.DeriveTableKey(ProjectId2, "customers");

        // Assert
        key1.Should().NotBeEquivalentTo(key2);
    }

    [Fact]
    public void DeriveColumnKey_DifferentColumns_ShouldReturnDifferentKeys()
    {
        // Arrange
        var service = CreateKeyDerivationService();

        // Act
        var key1 = service.DeriveColumnKey(ProjectId, "customers", "email");
        var key2 = service.DeriveColumnKey(ProjectId, "customers", "phone");

        // Assert
        key1.Should().NotBeEquivalentTo(key2);
    }

    [Fact]
    public void DeriveKey_DifferentKeyVersions_ShouldReturnDifferentKeys()
    {
        // Arrange
        var serviceV1 = CreateKeyDerivationService(keyVersion: 1);
        var serviceV2 = CreateKeyDerivationService(keyVersion: 2);

        // Act
        var key1 = serviceV1.DeriveTableKey(ProjectId, "customers");
        var key2 = serviceV2.DeriveTableKey(ProjectId, "customers");

        // Assert
        key1.Should().NotBeEquivalentTo(key2);
    }

    [Fact]
    public void CurrentKeyVersion_ShouldReturnConfiguredVersion()
    {
        // Arrange
        var service = CreateKeyDerivationService(keyVersion: 5);

        // Act & Assert
        service.CurrentKeyVersion.Should().Be(5);
    }

    [Fact]
    public void Constructor_WithShortMasterKey_ShouldThrowException()
    {
        // Arrange
        var shortKey = Convert.ToBase64String(new byte[16]); // Only 16 bytes
        var options = Options.Create(new DataEncryptionOptions
        {
            MasterKey = shortKey
        });

        // Act
        var action = () => new HkdfKeyDerivationService(options);

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*at least 32 bytes*");
    }

    [Fact]
    public void Constructor_WithEmptyMasterKey_ShouldThrowException()
    {
        // Arrange
        var options = Options.Create(new DataEncryptionOptions
        {
            MasterKey = ""
        });

        // Act
        var action = () => new HkdfKeyDerivationService(options);

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*not configured*");
    }

    #endregion

    #region Encryption/Decryption Tests

    [Fact]
    public void Encrypt_ShouldReturnPrefixedString()
    {
        // Arrange
        var service = CreateEncryptionService();
        var plaintext = "Hello, World!";

        // Act
        var encrypted = service.Encrypt(ProjectId, "customers", "email", plaintext);

        // Assert
        encrypted.Should().StartWith("$MORPH$v1$");
        encrypted.Should().NotContain(plaintext);
    }

    [Fact]
    public void EncryptDecrypt_ShouldRoundTrip()
    {
        // Arrange
        var service = CreateEncryptionService();
        var plaintext = "Sensitive data here!";

        // Act
        var encrypted = service.Encrypt(ProjectId, "customers", "email", plaintext);
        var decrypted = service.Decrypt(ProjectId, "customers", "email", encrypted);

        // Assert
        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void Encrypt_SameInput_ShouldReturnDifferentCiphertexts()
    {
        // Arrange (due to random nonce)
        var service = CreateEncryptionService();
        var plaintext = "Same text";

        // Act
        var encrypted1 = service.Encrypt(ProjectId, "customers", "email", plaintext);
        var encrypted2 = service.Encrypt(ProjectId, "customers", "email", plaintext);

        // Assert
        encrypted1.Should().NotBe(encrypted2); // Different nonces
    }

    [Fact]
    public void Decrypt_WithWrongProject_ShouldThrowException()
    {
        // Arrange
        var service = CreateEncryptionService();
        var encrypted = service.Encrypt(ProjectId, "customers", "email", "secret");

        // Act
        var action = () => service.Decrypt(ProjectId2, "customers", "email", encrypted);

        // Assert
        action.Should().Throw<System.Security.Cryptography.CryptographicException>();
    }

    [Fact]
    public void Decrypt_WithWrongTable_ShouldThrowException()
    {
        // Arrange
        var service = CreateEncryptionService();
        var encrypted = service.Encrypt(ProjectId, "customers", "email", "secret");

        // Act
        var action = () => service.Decrypt(ProjectId, "orders", "email", encrypted);

        // Assert
        action.Should().Throw<System.Security.Cryptography.CryptographicException>();
    }

    [Fact]
    public void Decrypt_UnencryptedData_ShouldReturnAsIs()
    {
        // Arrange (backward compatibility)
        var service = CreateEncryptionService();
        var plaintext = "Not encrypted data";

        // Act
        var result = service.Decrypt(ProjectId, "customers", "email", plaintext);

        // Assert
        result.Should().Be(plaintext);
    }

    [Fact]
    public void Encrypt_EmptyString_ShouldReturnEmptyString()
    {
        // Arrange
        var service = CreateEncryptionService();

        // Act
        var encrypted = service.Encrypt(ProjectId, "customers", "email", "");

        // Assert
        encrypted.Should().BeEmpty();
    }

    [Fact]
    public void Encrypt_Null_ShouldReturnNull()
    {
        // Arrange
        var service = CreateEncryptionService();

        // Act
        var encrypted = service.Encrypt(ProjectId, "customers", "email", null!);

        // Assert
        encrypted.Should().BeNull();
    }

    [Fact]
    public void Encrypt_WhenDisabled_ShouldReturnPlaintext()
    {
        // Arrange
        var service = CreateEncryptionService(enabled: false);
        var plaintext = "Should not encrypt";

        // Act
        var result = service.Encrypt(ProjectId, "customers", "email", plaintext);

        // Assert
        result.Should().Be(plaintext);
    }

    [Fact]
    public void IsEnabled_WhenEnabled_ShouldReturnTrue()
    {
        // Arrange
        var service = CreateEncryptionService(enabled: true);

        // Assert
        service.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void IsEnabled_WhenDisabled_ShouldReturnFalse()
    {
        // Arrange
        var service = CreateEncryptionService(enabled: false);

        // Assert
        service.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_WithNoMasterKey_ShouldReturnFalse()
    {
        // Arrange
        var options = Options.Create(new DataEncryptionOptions
        {
            Enabled = true,
            MasterKey = null
        });
        var keyDerivation = new Mock<IKeyDerivationService>().Object;
        var logger = new Mock<ILogger<AesGcmDataEncryptionService>>().Object;
        var service = new AesGcmDataEncryptionService(keyDerivation, options, logger);

        // Assert
        service.IsEnabled.Should().BeFalse();
    }

    #endregion

    #region Byte Encryption Tests

    [Fact]
    public void EncryptBytes_ShouldReturnCorrectFormat()
    {
        // Arrange
        var service = CreateEncryptionService();
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Test data");

        // Act
        var encrypted = service.EncryptBytes(ProjectId, "customers", "email", plaintext);

        // Assert
        encrypted.Should().HaveCountGreaterThan(31); // 1 + 2 + 12 + 16 = 31 bytes header
        encrypted[0].Should().Be(1); // Format version
    }

    [Fact]
    public void EncryptDecryptBytes_ShouldRoundTrip()
    {
        // Arrange
        var service = CreateEncryptionService();
        var plaintext = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };

        // Act
        var encrypted = service.EncryptBytes(ProjectId, "customers", "data", plaintext);
        var decrypted = service.DecryptBytes(ProjectId, "customers", "data", encrypted);

        // Assert
        decrypted.Should().BeEquivalentTo(plaintext);
    }

    [Fact]
    public void DecryptBytes_WithTamperedData_ShouldThrowException()
    {
        // Arrange
        var service = CreateEncryptionService();
        var encrypted = service.EncryptBytes(ProjectId, "customers", "email", new byte[] { 1, 2, 3 });

        // Tamper with ciphertext (last byte)
        encrypted[^1] ^= 0xFF;

        // Act
        var action = () => service.DecryptBytes(ProjectId, "customers", "email", encrypted);

        // Assert
        action.Should().Throw<System.Security.Cryptography.CryptographicException>()
            .WithMessage("*integrity*");
    }

    [Fact]
    public void DecryptBytes_WithTooShortData_ShouldThrowException()
    {
        // Arrange
        var service = CreateEncryptionService();
        var shortData = new byte[10]; // Less than 31 bytes header

        // Act
        var action = () => service.DecryptBytes(ProjectId, "customers", "email", shortData);

        // Assert
        action.Should().Throw<System.Security.Cryptography.CryptographicException>()
            .WithMessage("*too short*");
    }

    [Fact]
    public void DecryptBytes_WithUnsupportedVersion_ShouldThrowException()
    {
        // Arrange
        var service = CreateEncryptionService();
        var encrypted = service.EncryptBytes(ProjectId, "customers", "email", new byte[] { 1, 2, 3 });

        // Change version byte to unsupported version
        encrypted[0] = 99;

        // Act
        var action = () => service.DecryptBytes(ProjectId, "customers", "email", encrypted);

        // Assert
        action.Should().Throw<System.Security.Cryptography.CryptographicException>()
            .WithMessage("*version*");
    }

    #endregion

    #region Row Encryption Tests

    [Fact]
    public void EncryptRow_ShouldEncryptSpecifiedColumns()
    {
        // Arrange
        var service = CreateEncryptionService();
        var data = new Dictionary<string, object?>
        {
            ["_id"] = Guid.NewGuid(),
            ["email"] = "test@example.com",
            ["phone"] = "123-456-7890",
            ["name"] = "John Doe"
        };
        var encryptedColumns = new HashSet<string> { "email", "phone" };

        // Act
        var result = service.EncryptRow(ProjectId, "customers", data, encryptedColumns);

        // Assert
        result["_id"].Should().Be(data["_id"]); // Not encrypted
        result["name"].Should().Be(data["name"]); // Not encrypted
        result["email"].Should().BeOfType<string>();
        ((string)result["email"]!).Should().StartWith("$MORPH$v1$");
        ((string)result["phone"]!).Should().StartWith("$MORPH$v1$");
    }

    [Fact]
    public void DecryptRow_ShouldDecryptEncryptedColumns()
    {
        // Arrange
        var service = CreateEncryptionService();
        var encryptedColumns = new HashSet<string> { "email" };

        var encryptedEmail = service.Encrypt(ProjectId, "customers", "email", "test@example.com");
        var data = new Dictionary<string, object?>
        {
            ["_id"] = Guid.NewGuid(),
            ["email"] = encryptedEmail,
            ["name"] = "John Doe"
        };

        // Act
        var result = service.DecryptRow(ProjectId, "customers", data, encryptedColumns);

        // Assert
        result["email"].Should().Be("test@example.com");
        result["_id"].Should().Be(data["_id"]);
        result["name"].Should().Be(data["name"]);
    }

    [Fact]
    public void EncryptDecryptRow_ShouldRoundTrip()
    {
        // Arrange
        var service = CreateEncryptionService();
        var originalData = new Dictionary<string, object?>
        {
            ["_id"] = Guid.NewGuid(),
            ["email"] = "secret@example.com",
            ["phone"] = "555-1234",
            ["public_info"] = "visible"
        };
        var encryptedColumns = new HashSet<string> { "email", "phone" };

        // Act
        var encrypted = service.EncryptRow(ProjectId, "users", originalData, encryptedColumns);
        var decrypted = service.DecryptRow(ProjectId, "users", encrypted, encryptedColumns);

        // Assert
        decrypted["email"].Should().Be("secret@example.com");
        decrypted["phone"].Should().Be("555-1234");
        decrypted["public_info"].Should().Be("visible");
        decrypted["_id"].Should().Be(originalData["_id"]);
    }

    [Fact]
    public void EncryptRow_WithNullValues_ShouldPreserveNulls()
    {
        // Arrange
        var service = CreateEncryptionService();
        var data = new Dictionary<string, object?>
        {
            ["email"] = null,
            ["phone"] = "123"
        };
        var encryptedColumns = new HashSet<string> { "email", "phone" };

        // Act
        var result = service.EncryptRow(ProjectId, "customers", data, encryptedColumns);

        // Assert
        result["email"].Should().BeNull();
        ((string)result["phone"]!).Should().StartWith("$MORPH$v1$");
    }

    [Fact]
    public void EncryptRow_WhenDisabled_ShouldReturnOriginalData()
    {
        // Arrange
        var service = CreateEncryptionService(enabled: false);
        var data = new Dictionary<string, object?>
        {
            ["email"] = "test@example.com"
        };
        var encryptedColumns = new HashSet<string> { "email" };

        // Act
        var result = service.EncryptRow(ProjectId, "customers", data, encryptedColumns);

        // Assert
        result.Should().BeSameAs(data);
    }

    [Fact]
    public void EncryptRow_WithEmptyEncryptedColumns_ShouldReturnOriginalData()
    {
        // Arrange
        var service = CreateEncryptionService();
        var data = new Dictionary<string, object?>
        {
            ["email"] = "test@example.com"
        };
        var encryptedColumns = new HashSet<string>();

        // Act
        var result = service.EncryptRow(ProjectId, "customers", data, encryptedColumns);

        // Assert
        result.Should().BeSameAs(data);
    }

    #endregion

    #region Type Conversion Tests

    [Fact]
    public void EncryptRow_WithDateTime_ShouldPreserveValue()
    {
        // Arrange
        var service = CreateEncryptionService();
        var dateValue = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var data = new Dictionary<string, object?>
        {
            ["created_at"] = dateValue
        };
        var encryptedColumns = new HashSet<string> { "created_at" };

        // Act
        var encrypted = service.EncryptRow(ProjectId, "events", data, encryptedColumns);
        var decrypted = service.DecryptRow(ProjectId, "events", encrypted, encryptedColumns);

        // Assert - DateTime is stored as ISO 8601 string
        var decryptedValue = (string)decrypted["created_at"]!;
        decryptedValue.Should().Contain("2024-01-15");
    }

    [Fact]
    public void EncryptRow_WithGuid_ShouldPreserveValue()
    {
        // Arrange
        var service = CreateEncryptionService();
        var guidValue = Guid.NewGuid();
        var data = new Dictionary<string, object?>
        {
            ["reference_id"] = guidValue
        };
        var encryptedColumns = new HashSet<string> { "reference_id" };

        // Act
        var encrypted = service.EncryptRow(ProjectId, "refs", data, encryptedColumns);
        var decrypted = service.DecryptRow(ProjectId, "refs", encrypted, encryptedColumns);

        // Assert
        var decryptedValue = (string)decrypted["reference_id"]!;
        decryptedValue.Should().Be(guidValue.ToString());
    }

    [Fact]
    public void EncryptRow_WithByteArray_ShouldPreserveValue()
    {
        // Arrange
        var service = CreateEncryptionService();
        var binaryData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var data = new Dictionary<string, object?>
        {
            ["binary_field"] = binaryData
        };
        var encryptedColumns = new HashSet<string> { "binary_field" };

        // Act
        var encrypted = service.EncryptRow(ProjectId, "files", data, encryptedColumns);
        var decrypted = service.DecryptRow(ProjectId, "files", encrypted, encryptedColumns);

        // Assert - byte[] is stored as base64
        var decryptedValue = (string)decrypted["binary_field"]!;
        decryptedValue.Should().Be(Convert.ToBase64String(binaryData));
    }

    #endregion

    #region Large Data Tests

    [Fact]
    public void EncryptDecrypt_LargeData_ShouldRoundTrip()
    {
        // Arrange
        var service = CreateEncryptionService();
        var largeText = new string('X', 100_000); // 100KB of text

        // Act
        var encrypted = service.Encrypt(ProjectId, "documents", "content", largeText);
        var decrypted = service.Decrypt(ProjectId, "documents", "content", encrypted);

        // Assert
        decrypted.Should().Be(largeText);
    }

    [Fact]
    public void EncryptDecrypt_UnicodeData_ShouldRoundTrip()
    {
        // Arrange
        var service = CreateEncryptionService();
        var unicodeText = "Hello 你好 مرحبا שלום 🔐🎉";

        // Act
        var encrypted = service.Encrypt(ProjectId, "messages", "content", unicodeText);
        var decrypted = service.Decrypt(ProjectId, "messages", "content", encrypted);

        // Assert
        decrypted.Should().Be(unicodeText);
    }

    #endregion
}
