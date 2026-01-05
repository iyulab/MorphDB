namespace MorphDB.Core.Audit;

/// <summary>
/// Service for masking Personally Identifiable Information (PII) in audit logs.
/// Implements the MorphDB philosophy of "Blocked Access = Security" by ensuring
/// sensitive data is never exposed in audit trails.
/// </summary>
public interface IPiiMaskingService
{
    /// <summary>
    /// Masks PII in a dictionary of metadata values.
    /// </summary>
    /// <param name="metadata">The metadata dictionary to process.</param>
    /// <returns>A new dictionary with PII masked.</returns>
    Dictionary<string, object?>? MaskMetadata(Dictionary<string, object?>? metadata);

    /// <summary>
    /// Masks PII in a single string value based on its field name.
    /// </summary>
    /// <param name="fieldName">The name of the field (used to detect PII type).</param>
    /// <param name="value">The value to mask.</param>
    /// <returns>The masked value.</returns>
    string? MaskValue(string fieldName, string? value);

    /// <summary>
    /// Checks if a field name indicates it contains PII.
    /// </summary>
    /// <param name="fieldName">The field name to check.</param>
    /// <returns>True if the field likely contains PII.</returns>
    bool IsPiiField(string fieldName);
}

/// <summary>
/// Configuration options for PII masking behavior.
/// </summary>
public sealed class PiiMaskingOptions
{
    /// <summary>
    /// Field name patterns that indicate email addresses.
    /// Default: email, e_mail, email_address, user_email, contact_email
    /// </summary>
    public HashSet<string> EmailPatterns { get; init; } =
    [
        "email", "e_mail", "email_address", "user_email", "contact_email",
        "primary_email", "secondary_email", "work_email", "personal_email"
    ];

    /// <summary>
    /// Field name patterns that indicate phone numbers.
    /// Default: phone, telephone, mobile, cell, fax
    /// </summary>
    public HashSet<string> PhonePatterns { get; init; } =
    [
        "phone", "telephone", "mobile", "cell", "fax", "phone_number",
        "mobile_number", "cell_number", "contact_number", "work_phone", "home_phone"
    ];

    /// <summary>
    /// Field name patterns that should be completely redacted.
    /// Default: password, secret, token, api_key, private_key
    /// </summary>
    public HashSet<string> RedactedPatterns { get; init; } =
    [
        "password", "passwd", "pwd", "secret", "token", "api_key", "apikey",
        "private_key", "privatekey", "access_token", "refresh_token", "auth_token",
        "credential", "credit_card", "creditcard", "card_number", "cvv", "cvc",
        "ssn", "social_security", "national_id", "passport", "license_number"
    ];

    /// <summary>
    /// Field name patterns that indicate names (partial masking).
    /// Default: name, first_name, last_name, full_name
    /// </summary>
    public HashSet<string> NamePatterns { get; init; } =
    [
        "name", "first_name", "last_name", "full_name", "given_name",
        "family_name", "middle_name", "display_name", "user_name", "username"
    ];

    /// <summary>
    /// Field name patterns that indicate addresses.
    /// Default: address, street, city, postal_code, zip_code
    /// </summary>
    public HashSet<string> AddressPatterns { get; init; } =
    [
        "address", "street", "street_address", "home_address", "work_address",
        "postal_code", "zip_code", "zipcode"
    ];

    /// <summary>
    /// Whether to enable recursive masking in nested objects.
    /// Default: true
    /// </summary>
    public bool EnableRecursiveMasking { get; init; } = true;

    /// <summary>
    /// Maximum recursion depth for nested object masking.
    /// Default: 5
    /// </summary>
    public int MaxRecursionDepth { get; init; } = 5;

    /// <summary>
    /// The character used for masking.
    /// Default: *
    /// </summary>
    public char MaskCharacter { get; init; } = '*';

    /// <summary>
    /// Text displayed for completely redacted values.
    /// Default: [REDACTED]
    /// </summary>
    public string RedactedText { get; init; } = "[REDACTED]";
}
