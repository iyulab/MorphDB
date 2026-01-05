using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MorphDB.Core.Audit;

namespace MorphDB.Npgsql.Audit;

/// <summary>
/// Default implementation of PII masking service.
/// Provides consistent masking for sensitive data in audit logs.
/// </summary>
public sealed partial class PiiMaskingService : IPiiMaskingService
{
    private readonly PiiMaskingOptions _options;
    private readonly HashSet<string> _allPiiPatterns;

    public PiiMaskingService(IOptions<PiiMaskingOptions>? options = null)
    {
        _options = options?.Value ?? new PiiMaskingOptions();

        // Build combined pattern set for quick lookup
        _allPiiPatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _allPiiPatterns.UnionWith(_options.EmailPatterns);
        _allPiiPatterns.UnionWith(_options.PhonePatterns);
        _allPiiPatterns.UnionWith(_options.RedactedPatterns);
        _allPiiPatterns.UnionWith(_options.NamePatterns);
        _allPiiPatterns.UnionWith(_options.AddressPatterns);
    }

    /// <inheritdoc/>
    public Dictionary<string, object?>? MaskMetadata(Dictionary<string, object?>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
            return metadata;

        return MaskDictionary(metadata, 0);
    }

    /// <inheritdoc/>
    public string? MaskValue(string fieldName, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var normalizedField = NormalizeFieldName(fieldName);

        // Check for completely redacted fields
        if (MatchesPattern(normalizedField, _options.RedactedPatterns))
        {
            return _options.RedactedText;
        }

        // Check for email patterns
        if (MatchesPattern(normalizedField, _options.EmailPatterns) ||
            EmailRegex().IsMatch(value))
        {
            return MaskEmail(value);
        }

        // Check for phone patterns
        if (MatchesPattern(normalizedField, _options.PhonePatterns) ||
            PhoneRegex().IsMatch(value))
        {
            return MaskPhone(value);
        }

        // Check for name patterns
        if (MatchesPattern(normalizedField, _options.NamePatterns))
        {
            return MaskName(value);
        }

        // Check for address patterns
        if (MatchesPattern(normalizedField, _options.AddressPatterns))
        {
            return MaskAddress(value);
        }

        return value;
    }

    /// <inheritdoc/>
    public bool IsPiiField(string fieldName)
    {
        var normalizedField = NormalizeFieldName(fieldName);
        return MatchesPattern(normalizedField, _allPiiPatterns);
    }

    private Dictionary<string, object?> MaskDictionary(Dictionary<string, object?> dict, int depth)
    {
        if (depth >= _options.MaxRecursionDepth)
            return dict;

        var result = new Dictionary<string, object?>(dict.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in dict)
        {
            result[kvp.Key] = MaskObject(kvp.Key, kvp.Value, depth);
        }

        return result;
    }

    private object? MaskObject(string fieldName, object? value, int depth)
    {
        if (value is null)
            return null;

        // Handle string values
        if (value is string strValue)
        {
            return MaskValue(fieldName, strValue);
        }

        // Handle nested dictionaries
        if (_options.EnableRecursiveMasking && value is Dictionary<string, object?> nestedDict)
        {
            return MaskDictionary(nestedDict, depth + 1);
        }

        // Handle JsonElement (common when deserializing JSON)
        if (value is JsonElement jsonElement)
        {
            return MaskJsonElement(fieldName, jsonElement, depth);
        }

        // Handle collections
        if (_options.EnableRecursiveMasking && value is IEnumerable<object?> collection)
        {
            return collection.Select((item, i) => MaskObject($"{fieldName}[{i}]", item, depth + 1)).ToList();
        }

        // Return non-PII values as-is
        return value;
    }

    private object? MaskJsonElement(string fieldName, JsonElement element, int depth)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => MaskValue(fieldName, element.GetString()),
            JsonValueKind.Object => MaskJsonObject(element, depth),
            JsonValueKind.Array => MaskJsonArray(fieldName, element, depth),
            JsonValueKind.Number => element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }

    private Dictionary<string, object?> MaskJsonObject(JsonElement element, int depth)
    {
        if (depth >= _options.MaxRecursionDepth)
        {
            // Convert to dictionary without masking at max depth
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(element.GetRawText()) ?? [];
        }

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = MaskJsonElement(property.Name, property.Value, depth + 1);
        }
        return result;
    }

    private List<object?> MaskJsonArray(string fieldName, JsonElement element, int depth)
    {
        var result = new List<object?>();
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            result.Add(MaskJsonElement($"{fieldName}[{index}]", item, depth + 1));
            index++;
        }
        return result;
    }

    private string MaskEmail(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex <= 0)
            return new string(_options.MaskCharacter, email.Length);

        var localPart = email[..atIndex];
        var domainPart = email[(atIndex + 1)..];

        // Keep first character of local part, mask the rest
        var maskedLocal = localPart.Length > 1
            ? $"{localPart[0]}{new string(_options.MaskCharacter, Math.Min(localPart.Length - 1, 5))}"
            : new string(_options.MaskCharacter, 3);

        // Keep first and last 2 chars of domain, mask middle
        var dotIndex = domainPart.LastIndexOf('.');
        if (dotIndex > 2)
        {
            var domainName = domainPart[..dotIndex];
            var tld = domainPart[dotIndex..];
            var maskedDomain = domainName.Length > 3
                ? $"{domainName[0]}{new string(_options.MaskCharacter, Math.Min(domainName.Length - 2, 5))}{domainName[^1]}{tld}"
                : $"{new string(_options.MaskCharacter, 3)}{tld}";
            return $"{maskedLocal}@{maskedDomain}";
        }

        return $"{maskedLocal}@{new string(_options.MaskCharacter, 5)}.***";
    }

    private string MaskPhone(string phone)
    {
        // Extract only digits
        var digits = new string(phone.Where(char.IsDigit).ToArray());

        if (digits.Length < 4)
            return new string(_options.MaskCharacter, phone.Length);

        // Keep last 4 digits visible
        var lastFour = digits[^4..];
        var maskedPart = new string(_options.MaskCharacter, digits.Length - 4);

        // Try to maintain original formatting
        var digitIndex = 0;
        var maskedBuilder = new char[phone.Length];

        for (var i = 0; i < phone.Length; i++)
        {
            if (char.IsDigit(phone[i]))
            {
                if (digitIndex < digits.Length - 4)
                {
                    maskedBuilder[i] = _options.MaskCharacter;
                }
                else
                {
                    maskedBuilder[i] = phone[i];
                }
                digitIndex++;
            }
            else
            {
                maskedBuilder[i] = phone[i];
            }
        }

        return new string(maskedBuilder);
    }

    private string MaskName(string name)
    {
        if (name.Length <= 1)
            return new string(_options.MaskCharacter, 3);

        // Keep first letter, mask the rest
        return $"{name[0]}{new string(_options.MaskCharacter, Math.Min(name.Length - 1, 8))}";
    }

    private string MaskAddress(string address)
    {
        if (address.Length <= 3)
            return new string(_options.MaskCharacter, 10);

        // Keep first 3 characters, mask the rest (but show length indication)
        var maskedLength = Math.Min(address.Length - 3, 15);
        return $"{address[..3]}{new string(_options.MaskCharacter, maskedLength)}";
    }

    private static string NormalizeFieldName(string fieldName)
    {
        // Convert to lowercase and handle common separators
        return fieldName
            .ToLowerInvariant()
            .Replace("-", "_")
            .Replace(" ", "_");
    }

    private static bool MatchesPattern(string fieldName, HashSet<string> patterns)
    {
        // Direct match
        if (patterns.Contains(fieldName))
            return true;

        // Check if field contains any pattern
        foreach (var pattern in patterns)
        {
            if (fieldName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    // Regex patterns for auto-detection
    [GeneratedRegex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$", RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^[\+]?[(]?[0-9]{1,4}[)]?[-\s\.]?[0-9]{1,4}[-\s\.]?[0-9]{1,9}$", RegexOptions.Compiled)]
    private static partial Regex PhoneRegex();
}
