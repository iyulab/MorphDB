using System.Text;
using System.Text.RegularExpressions;

namespace MorphDB.Core.Infrastructure;

/// <summary>
/// Naming convention types for cross-language/context transformations.
/// </summary>
public enum NamingStyle
{
    /// <summary>PascalCase - C# classes, methods, properties (e.g., "UserAccount")</summary>
    PascalCase,

    /// <summary>camelCase - JSON, JavaScript, C# local variables (e.g., "userAccount")</summary>
    CamelCase,

    /// <summary>snake_case - PostgreSQL, Python, Rust (e.g., "user_account")</summary>
    SnakeCase,

    /// <summary>kebab-case - REST API paths, CSS (e.g., "user-account")</summary>
    KebabCase,

    /// <summary>SCREAMING_SNAKE_CASE - Environment variables (e.g., "USER_ACCOUNT")</summary>
    ScreamingSnakeCase
}

/// <summary>
/// Provides naming convention conversion utilities for cross-language transformations.
/// Handles the flow: Database (snake_case) ↔ C# (PascalCase) ↔ JSON API (camelCase)
/// </summary>
public static partial class NamingConvention
{
    /// <summary>
    /// Converts a string to the specified naming style.
    /// </summary>
    /// <param name="input">The input string in any naming convention.</param>
    /// <param name="style">The target naming style.</param>
    /// <returns>The converted string.</returns>
    public static string Convert(string input, NamingStyle style)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        return style switch
        {
            NamingStyle.PascalCase => ToPascalCase(input),
            NamingStyle.CamelCase => ToCamelCase(input),
            NamingStyle.SnakeCase => ToSnakeCase(input),
            NamingStyle.KebabCase => ToKebabCase(input),
            NamingStyle.ScreamingSnakeCase => ToScreamingSnakeCase(input),
            _ => input
        };
    }

    /// <summary>
    /// Converts to PascalCase (C# classes, methods, properties).
    /// Examples: "user_account" → "UserAccount", "userId" → "UserId"
    /// </summary>
    public static string ToPascalCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        var words = SplitIntoWords(input);
        var result = new StringBuilder();

        foreach (var word in words)
        {
            if (word.Length > 0)
            {
                result.Append(char.ToUpperInvariant(word[0]));
                if (word.Length > 1)
                    result.Append(word[1..].ToLowerInvariant());
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Converts to camelCase (JSON API, JavaScript, C# local variables).
    /// Examples: "user_account" → "userAccount", "UserId" → "userId"
    /// </summary>
    public static string ToCamelCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        var pascal = ToPascalCase(input);
        if (pascal.Length == 0)
            return pascal;

        // Find the end of leading uppercase sequence
        var firstLowerIndex = 0;
        for (var i = 0; i < pascal.Length; i++)
        {
            if (char.IsLower(pascal[i]))
            {
                firstLowerIndex = i;
                break;
            }
        }

        // Handle acronyms: "XMLParser" → "xmlParser", "ID" → "id"
        if (firstLowerIndex > 1)
        {
            return pascal[..(firstLowerIndex - 1)].ToLowerInvariant() + pascal[(firstLowerIndex - 1)..];
        }

        return char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    /// <summary>
    /// Converts to snake_case (PostgreSQL, Python, Rust).
    /// Examples: "UserAccount" → "user_account", "userId" → "user_id"
    /// </summary>
    public static string ToSnakeCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        var words = SplitIntoWords(input);
        return string.Join("_", words.Select(w => w.ToLowerInvariant()));
    }

    /// <summary>
    /// Converts to kebab-case (REST API paths, CSS classes).
    /// Examples: "UserAccount" → "user-account", "userId" → "user-id"
    /// </summary>
    public static string ToKebabCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        var words = SplitIntoWords(input);
        return string.Join("-", words.Select(w => w.ToLowerInvariant()));
    }

    /// <summary>
    /// Converts to SCREAMING_SNAKE_CASE (Environment variables).
    /// Examples: "UserAccount" → "USER_ACCOUNT", "apiKey" → "API_KEY"
    /// </summary>
    public static string ToScreamingSnakeCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        var words = SplitIntoWords(input);
        return string.Join("_", words.Select(w => w.ToUpperInvariant()));
    }

    /// <summary>
    /// Splits a string in any naming convention into individual words.
    /// Handles: PascalCase, camelCase, snake_case, kebab-case, SCREAMING_SNAKE_CASE
    /// </summary>
    private static List<string> SplitIntoWords(string input)
    {
        if (string.IsNullOrEmpty(input))
            return [];

        // First, split by delimiters (underscore, hyphen, space)
        var parts = DelimiterSplitRegex().Split(input);
        var words = new List<string>();

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part))
                continue;

            // Then split by case changes for camelCase/PascalCase
            var caseWords = SplitByCaseChange(part);
            words.AddRange(caseWords);
        }

        return words;
    }

    /// <summary>
    /// Splits a string by case changes (for PascalCase/camelCase).
    /// Examples: "UserID" → ["User", "ID"], "XMLParser" → ["XML", "Parser"]
    /// </summary>
    private static List<string> SplitByCaseChange(string input)
    {
        if (string.IsNullOrEmpty(input))
            return [];

        var words = new List<string>();
        var currentWord = new StringBuilder();

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            var isUpper = char.IsUpper(c);
            var isLower = char.IsLower(c);
            var isDigit = char.IsDigit(c);

            if (currentWord.Length == 0)
            {
                currentWord.Append(c);
                continue;
            }

            var prevChar = currentWord[^1];
            var prevIsUpper = char.IsUpper(prevChar);
            var prevIsLower = char.IsLower(prevChar);

            // Start new word on:
            // 1. Lower to Upper transition: "userId" → "user" | "Id"
            // 2. Upper to Lower (with preceding uppers): "XMLParser" → "XML" | "Parser"
            var startNewWord = false;

            if (prevIsLower && isUpper)
            {
                // userID → user | ID
                startNewWord = true;
            }
            else if (prevIsUpper && isLower && currentWord.Length > 1 && char.IsUpper(currentWord[^2]))
            {
                // XMLParser: when we hit 'a', we need to break before 'P'
                // So we need to move the last upper to the new word
                var lastChar = currentWord[^1];
                currentWord.Length--;
                if (currentWord.Length > 0)
                {
                    words.Add(currentWord.ToString());
                    currentWord.Clear();
                }
                currentWord.Append(lastChar);
            }

            if (startNewWord)
            {
                if (currentWord.Length > 0)
                {
                    words.Add(currentWord.ToString());
                    currentWord.Clear();
                }
            }

            currentWord.Append(c);
        }

        if (currentWord.Length > 0)
        {
            words.Add(currentWord.ToString());
        }

        return words;
    }

    [GeneratedRegex(@"[_\-\s]+")]
    private static partial Regex DelimiterSplitRegex();
}

/// <summary>
/// Extension methods for easy naming convention conversions.
/// </summary>
public static class NamingConventionExtensions
{
    /// <summary>Converts to PascalCase.</summary>
    public static string ToPascalCase(this string input) => NamingConvention.ToPascalCase(input);

    /// <summary>Converts to camelCase.</summary>
    public static string ToCamelCase(this string input) => NamingConvention.ToCamelCase(input);

    /// <summary>Converts to snake_case.</summary>
    public static string ToSnakeCase(this string input) => NamingConvention.ToSnakeCase(input);

    /// <summary>Converts to kebab-case.</summary>
    public static string ToKebabCase(this string input) => NamingConvention.ToKebabCase(input);

    /// <summary>Converts to SCREAMING_SNAKE_CASE.</summary>
    public static string ToScreamingSnakeCase(this string input) => NamingConvention.ToScreamingSnakeCase(input);

    /// <summary>Converts to the specified naming style.</summary>
    public static string ToNamingStyle(this string input, NamingStyle style) => NamingConvention.Convert(input, style);
}
