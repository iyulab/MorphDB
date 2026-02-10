using MorphDB.Service.Services;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Unit tests for WebhookSignatureService.
/// </summary>
[Trait("Category", "Unit")]
public class WebhookSignatureServiceTests
{
    private readonly WebhookSignatureService _sut = new();

    [Fact]
    public void ComputeSignature_WithValidInputs_ShouldReturnVersionedSignature()
    {
        // Arrange
        var secret = "test_secret_key";
        var timestamp = 1699999999L;
        var payload = "{\"event\":\"insert\",\"data\":{}}";

        // Act
        var signature = _sut.ComputeSignature(secret, timestamp, payload);

        // Assert
        signature.Should().StartWith("v1=");
        signature.Should().HaveLength(67); // "v1=" (3) + hex hash (64)
    }

    [Fact]
    public void ComputeSignature_WithSameInputs_ShouldReturnSameSignature()
    {
        // Arrange
        var secret = "consistent_secret";
        var timestamp = 1700000000L;
        var payload = "{\"test\":\"data\"}";

        // Act
        var signature1 = _sut.ComputeSignature(secret, timestamp, payload);
        var signature2 = _sut.ComputeSignature(secret, timestamp, payload);

        // Assert
        signature1.Should().Be(signature2);
    }

    [Fact]
    public void ComputeSignature_WithDifferentSecrets_ShouldReturnDifferentSignatures()
    {
        // Arrange
        var timestamp = 1700000000L;
        var payload = "{\"test\":\"data\"}";

        // Act
        var signature1 = _sut.ComputeSignature("secret1", timestamp, payload);
        var signature2 = _sut.ComputeSignature("secret2", timestamp, payload);

        // Assert
        signature1.Should().NotBe(signature2);
    }

    [Fact]
    public void ComputeSignature_WithDifferentTimestamps_ShouldReturnDifferentSignatures()
    {
        // Arrange
        var secret = "same_secret";
        var payload = "{\"test\":\"data\"}";

        // Act
        var signature1 = _sut.ComputeSignature(secret, 1700000000L, payload);
        var signature2 = _sut.ComputeSignature(secret, 1700000001L, payload);

        // Assert
        signature1.Should().NotBe(signature2);
    }

    [Fact]
    public void ComputeSignature_WithDifferentPayloads_ShouldReturnDifferentSignatures()
    {
        // Arrange
        var secret = "same_secret";
        var timestamp = 1700000000L;

        // Act
        var signature1 = _sut.ComputeSignature(secret, timestamp, "{\"a\":1}");
        var signature2 = _sut.ComputeSignature(secret, timestamp, "{\"a\":2}");

        // Assert
        signature1.Should().NotBe(signature2);
    }

    [Fact]
    public void VerifySignature_WithValidSignature_ShouldReturnTrue()
    {
        // Arrange
        var secret = "verification_secret";
        var timestamp = _sut.GetCurrentTimestamp();
        var payload = "{\"event\":\"insert\"}";
        var signature = _sut.ComputeSignature(secret, timestamp, payload);

        // Act
        var isValid = _sut.VerifySignature(secret, signature, timestamp, payload);

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public void VerifySignature_WithInvalidSignature_ShouldReturnFalse()
    {
        // Arrange
        var secret = "verification_secret";
        var timestamp = _sut.GetCurrentTimestamp();
        var payload = "{\"event\":\"insert\"}";
        var invalidSignature = "v1=0000000000000000000000000000000000000000000000000000000000000000";

        // Act
        var isValid = _sut.VerifySignature(secret, invalidSignature, timestamp, payload);

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_WithExpiredTimestamp_ShouldReturnFalse()
    {
        // Arrange
        var secret = "verification_secret";
        var expiredTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds(); // 10 minutes ago
        var payload = "{\"event\":\"insert\"}";
        var signature = _sut.ComputeSignature(secret, expiredTimestamp, payload);

        // Act
        var isValid = _sut.VerifySignature(secret, signature, expiredTimestamp, payload);

        // Assert - Should fail because timestamp is too old (default tolerance is 5 minutes)
        isValid.Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_WithFutureTimestamp_ShouldReturnFalse()
    {
        // Arrange
        var secret = "verification_secret";
        var futureTimestamp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds(); // 10 minutes from now
        var payload = "{\"event\":\"insert\"}";
        var signature = _sut.ComputeSignature(secret, futureTimestamp, payload);

        // Act
        var isValid = _sut.VerifySignature(secret, signature, futureTimestamp, payload);

        // Assert - Should fail because timestamp is in the future (beyond tolerance)
        isValid.Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_WithCustomTolerance_ShouldRespectTolerance()
    {
        // Arrange
        var secret = "verification_secret";
        var oldTimestamp = DateTimeOffset.UtcNow.AddSeconds(-400).ToUnixTimeSeconds(); // 400 seconds ago
        var payload = "{\"event\":\"insert\"}";
        var signature = _sut.ComputeSignature(secret, oldTimestamp, payload);

        // Act
        var isValidWith300 = _sut.VerifySignature(secret, signature, oldTimestamp, payload, toleranceSeconds: 300);
        var isValidWith600 = _sut.VerifySignature(secret, signature, oldTimestamp, payload, toleranceSeconds: 600);

        // Assert
        isValidWith300.Should().BeFalse(); // 400 > 300
        isValidWith600.Should().BeTrue();  // 400 < 600
    }

    [Fact]
    public void VerifySignature_WithWrongSecret_ShouldReturnFalse()
    {
        // Arrange
        var originalSecret = "original_secret";
        var wrongSecret = "wrong_secret";
        var timestamp = _sut.GetCurrentTimestamp();
        var payload = "{\"event\":\"insert\"}";
        var signature = _sut.ComputeSignature(originalSecret, timestamp, payload);

        // Act
        var isValid = _sut.VerifySignature(wrongSecret, signature, timestamp, payload);

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_WithModifiedPayload_ShouldReturnFalse()
    {
        // Arrange
        var secret = "verification_secret";
        var timestamp = _sut.GetCurrentTimestamp();
        var originalPayload = "{\"event\":\"insert\"}";
        var modifiedPayload = "{\"event\":\"delete\"}";
        var signature = _sut.ComputeSignature(secret, timestamp, originalPayload);

        // Act
        var isValid = _sut.VerifySignature(secret, signature, timestamp, modifiedPayload);

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public void GetCurrentTimestamp_ShouldReturnReasonableValue()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Act
        var timestamp = _sut.GetCurrentTimestamp();
        var after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Assert
        timestamp.Should().BeGreaterThanOrEqualTo(before);
        timestamp.Should().BeLessThanOrEqualTo(after);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    [InlineData("{\"nested\":{\"deep\":{\"value\":123}}}")]
    [InlineData("[1,2,3,4,5]")]
    public void ComputeSignature_WithVariousPayloads_ShouldNotThrow(string payload)
    {
        // Arrange
        var secret = "test_secret";
        var timestamp = 1700000000L;

        // Act
        var action = () => _sut.ComputeSignature(secret, timestamp, payload);

        // Assert
        action.Should().NotThrow();
    }
}
