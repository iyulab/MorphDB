using System.Security.Cryptography;
using System.Text;

namespace MorphDB.Service.Services;

/// <summary>
/// Service for computing HMAC-SHA256 signatures for webhook payloads.
/// </summary>
public sealed class WebhookSignatureService
{
    private const string SignatureVersion = "v1";

    /// <summary>
    /// Computes the HMAC-SHA256 signature for a webhook payload.
    /// </summary>
    /// <param name="secret">The webhook secret key.</param>
    /// <param name="timestamp">Unix timestamp of the delivery attempt.</param>
    /// <param name="payload">The JSON payload being sent.</param>
    /// <returns>The signature in format "v1=hex_signature".</returns>
    public string ComputeSignature(string secret, long timestamp, string payload)
    {
        // Create the signed payload: timestamp.payload
        var signedPayload = $"{timestamp}.{payload}";

        // Compute HMAC-SHA256
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(signedPayload);

        using var hmac = new HMACSHA256(secretBytes);
        var hash = hmac.ComputeHash(payloadBytes);

        // Return signature in versioned format
        return $"{SignatureVersion}={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    /// <summary>
    /// Verifies a webhook signature.
    /// </summary>
    /// <param name="secret">The webhook secret key.</param>
    /// <param name="signature">The signature header value.</param>
    /// <param name="timestamp">The timestamp header value.</param>
    /// <param name="payload">The raw request body.</param>
    /// <param name="toleranceSeconds">Maximum age of the signature in seconds (default: 300 = 5 minutes).</param>
    /// <returns>True if the signature is valid and not expired.</returns>
    public bool VerifySignature(
        string secret,
        string signature,
        long timestamp,
        string payload,
        int toleranceSeconds = 300)
    {
        // Check timestamp is within tolerance (prevent replay attacks)
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - timestamp) > toleranceSeconds)
        {
            return false;
        }

        // Compute expected signature
        var expectedSignature = ComputeSignature(secret, timestamp, payload);

        // Constant-time comparison to prevent timing attacks
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(signature),
            Encoding.UTF8.GetBytes(expectedSignature));
    }

    /// <summary>
    /// Gets the current Unix timestamp.
    /// </summary>
    public long GetCurrentTimestamp() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
