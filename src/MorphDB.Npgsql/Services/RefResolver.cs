using System.Text.Json;
using System.Text.RegularExpressions;
using MorphDB.Core.Models;

namespace MorphDB.Npgsql.Services;

/// <summary>
/// Resolves $ref references in transaction operations.
/// References follow the pattern: $refName.propertyPath (e.g., "$order._id", "$item.quantity").
/// </summary>
public sealed partial class RefResolver : IRefResolver
{
    private readonly Dictionary<string, TransactionOperationResult> _store = new(StringComparer.OrdinalIgnoreCase);

    // Pattern: $refName or $refName.property or $refName.nested.property
    [GeneratedRegex(@"^\$([a-zA-Z_][a-zA-Z0-9_]*)(?:\.([a-zA-Z0-9_\.]+))?$", RegexOptions.Compiled)]
    private static partial Regex RefPattern();

    /// <inheritdoc />
    public void Store(string refName, TransactionOperationResult result)
    {
        ArgumentNullException.ThrowIfNull(refName);
        _store[refName] = result;
    }

    /// <inheritdoc />
    public object? Resolve(string refExpression)
    {
        if (string.IsNullOrEmpty(refExpression) || !refExpression.StartsWith('$'))
        {
            return null;
        }

        var match = RefPattern().Match(refExpression);
        if (!match.Success)
        {
            return null;
        }

        var refName = match.Groups[1].Value;
        var propertyPath = match.Groups[2].Success ? match.Groups[2].Value : null;

        if (!_store.TryGetValue(refName, out var result))
        {
            return null;
        }

        // If no property path, return the entire result data
        if (string.IsNullOrEmpty(propertyPath))
        {
            return result.Data;
        }

        // Handle special properties
        if (propertyPath.Equals("_id", StringComparison.OrdinalIgnoreCase))
        {
            return result.Id;
        }

        // Navigate the property path in the result data
        return NavigatePropertyPath(result.Data, propertyPath);
    }

    /// <inheritdoc />
    public IDictionary<string, object?> ResolveData(IDictionary<string, object?> data)
    {
        var resolved = new Dictionary<string, object?>(data.Count);

        foreach (var kvp in data)
        {
            resolved[kvp.Key] = ResolveValue(kvp.Value);
        }

        return resolved;
    }

    /// <summary>
    /// Resolves a single value, handling $ref strings and nested objects/arrays.
    /// </summary>
    private object? ResolveValue(object? value)
    {
        return value switch
        {
            string str when str.StartsWith('$') => Resolve(str) ?? value,
            JsonElement json => ResolveJsonElement(json),
            IDictionary<string, object?> dict => ResolveData(dict),
            IList<object?> list => list.Select(ResolveValue).ToList(),
            _ => value
        };
    }

    /// <summary>
    /// Resolves values within a JsonElement.
    /// </summary>
    private object? ResolveJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String when element.GetString() is { } str && str.StartsWith('$')
                => Resolve(str) ?? str,
            JsonValueKind.Object => ResolveJsonObject(element),
            JsonValueKind.Array => element.EnumerateArray().Select(e => ResolveJsonElement(e)).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    /// <summary>
    /// Resolves values within a JsonElement object.
    /// </summary>
    private Dictionary<string, object?> ResolveJsonObject(JsonElement element)
    {
        var result = new Dictionary<string, object?>();
        foreach (var prop in element.EnumerateObject())
        {
            result[prop.Name] = ResolveJsonElement(prop.Value);
        }
        return result;
    }

    /// <summary>
    /// Navigates a dot-separated property path in the data dictionary.
    /// </summary>
    private static object? NavigatePropertyPath(IDictionary<string, object?>? data, string path)
    {
        if (data == null)
        {
            return null;
        }

        var parts = path.Split('.');
        object? current = data;

        foreach (var part in parts)
        {
            current = current switch
            {
                IDictionary<string, object?> dict when dict.TryGetValue(part, out var val) => val,
                JsonElement json when json.ValueKind == JsonValueKind.Object
                    => json.TryGetProperty(part, out var prop) ? GetJsonValue(prop) : null,
                _ => null
            };

            if (current == null)
            {
                return null;
            }
        }

        return current;
    }

    /// <summary>
    /// Extracts a CLR value from a JsonElement.
    /// </summary>
    private static object? GetJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element // Keep as JsonElement for complex types
        };
    }

    /// <summary>
    /// Checks if a value is a $ref expression.
    /// </summary>
    public static bool IsRefExpression(object? value)
    {
        return value is string str && str.StartsWith('$') && RefPattern().IsMatch(str);
    }

    /// <summary>
    /// Resolves an ID value that may be a GUID or a $ref expression.
    /// </summary>
    public Guid? ResolveId(object? id)
    {
        return id switch
        {
            Guid g => g,
            string str when Guid.TryParse(str, out var g) => g,
            string str when str.StartsWith('$') => Resolve(str) switch
            {
                Guid g => g,
                string s when Guid.TryParse(s, out var g) => g,
                _ => null
            },
            _ => null
        };
    }
}
