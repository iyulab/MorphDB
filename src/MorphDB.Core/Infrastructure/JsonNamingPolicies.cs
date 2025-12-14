using System.Text.Json;

namespace MorphDB.Core.Infrastructure;

/// <summary>
/// JSON naming policies using the NamingConvention converter.
/// Use these for consistent JSON serialization across the application.
/// </summary>
public static class JsonNamingPolicies
{
    /// <summary>
    /// camelCase policy for JSON API responses.
    /// Example: "FirstName" → "firstName"
    /// </summary>
    public static JsonNamingPolicy CamelCase { get; } = new CamelCaseNamingPolicy();

    /// <summary>
    /// snake_case policy for PostgreSQL-compatible JSON.
    /// Example: "FirstName" → "first_name"
    /// </summary>
    public static JsonNamingPolicy SnakeCase { get; } = new SnakeCaseNamingPolicy();

    /// <summary>
    /// kebab-case policy for URL-friendly JSON.
    /// Example: "FirstName" → "first-name"
    /// </summary>
    public static JsonNamingPolicy KebabCase { get; } = new KebabCaseNamingPolicy();

    /// <summary>
    /// Gets the default JSON serializer options with camelCase naming.
    /// </summary>
    public static JsonSerializerOptions DefaultApiOptions { get; } = new()
    {
        PropertyNamingPolicy = CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    /// <summary>
    /// Gets JSON serializer options for database-compatible JSON (snake_case).
    /// </summary>
    public static JsonSerializerOptions DatabaseOptions { get; } = new()
    {
        PropertyNamingPolicy = SnakeCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private sealed class CamelCaseNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name) => NamingConvention.ToCamelCase(name);
    }

    private sealed class SnakeCaseNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name) => NamingConvention.ToSnakeCase(name);
    }

    private sealed class KebabCaseNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name) => NamingConvention.ToKebabCase(name);
    }
}
