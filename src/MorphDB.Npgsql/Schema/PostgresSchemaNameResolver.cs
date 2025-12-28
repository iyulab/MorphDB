using System.Text.RegularExpressions;
using MorphDB.Core.Abstractions;

namespace MorphDB.Npgsql.Schema;

/// <summary>
/// PostgreSQL implementation of schema name resolution.
/// Naming convention:
/// - System schema: p_{first8charsOfProjectId}_sys
/// - Data schema: p_{first8charsOfProjectId}_dat
/// - Global schema: morphdb
/// </summary>
public sealed partial class PostgresSchemaNameResolver : ISchemaNameResolver
{
    private const string GlobalSchemaName = "morphdb";
    private const string ProjectSchemaPrefix = "p_";
    private const string SystemSchemaSuffix = "_sys";
    private const string DataSchemaSuffix = "_dat";
    private const int ProjectIdPrefixLength = 8;

    /// <inheritdoc/>
    public string GlobalSchema => GlobalSchemaName;

    /// <inheritdoc/>
    public string GetSystemSchema(Guid projectId)
    {
        var idPrefix = GetProjectIdPrefix(projectId);
        return $"{ProjectSchemaPrefix}{idPrefix}{SystemSchemaSuffix}";
    }

    /// <inheritdoc/>
    public string GetDataSchema(Guid projectId)
    {
        var idPrefix = GetProjectIdPrefix(projectId);
        return $"{ProjectSchemaPrefix}{idPrefix}{DataSchemaSuffix}";
    }

    /// <inheritdoc/>
    public SchemaNames GetSchemaNames(Guid projectId)
    {
        return new SchemaNames(GetSystemSchema(projectId), GetDataSchema(projectId));
    }

    /// <inheritdoc/>
    public bool TryParseSchemaName(string schemaName, out Guid projectId)
    {
        projectId = Guid.Empty;

        if (string.IsNullOrEmpty(schemaName))
            return false;

        // Match pattern: p_{8chars}_{sys|dat}
        var match = SchemaNamePattern().Match(schemaName);
        if (!match.Success)
            return false;

        var idPrefix = match.Groups["id"].Value;

        // We can only recover a partial GUID - store as a deterministic GUID
        // In practice, you'd look up the full project ID from the database
        // Here we create a reproducible GUID from the prefix for consistency
        projectId = CreateDeterministicGuid(idPrefix);
        return true;
    }

    /// <inheritdoc/>
    public SchemaType GetSchemaType(string schemaName)
    {
        if (string.IsNullOrEmpty(schemaName))
            return SchemaType.Unknown;

        if (schemaName.Equals(GlobalSchemaName, StringComparison.OrdinalIgnoreCase))
            return SchemaType.Global;

        if (schemaName.Equals("public", StringComparison.OrdinalIgnoreCase))
            return SchemaType.Public;

        if (schemaName.StartsWith(ProjectSchemaPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (schemaName.EndsWith(SystemSchemaSuffix, StringComparison.OrdinalIgnoreCase))
                return SchemaType.ProjectSystem;

            if (schemaName.EndsWith(DataSchemaSuffix, StringComparison.OrdinalIgnoreCase))
                return SchemaType.ProjectData;
        }

        return SchemaType.Unknown;
    }

    /// <inheritdoc/>
    public string QualifyName(string schemaName, string objectName)
    {
        return $"\"{schemaName}\".\"{objectName}\"";
    }

    /// <inheritdoc/>
    public string QualifyDataTable(Guid projectId, string tableName)
    {
        var dataSchema = GetDataSchema(projectId);
        return QualifyName(dataSchema, tableName);
    }

    /// <inheritdoc/>
    public string QualifySystemTable(Guid projectId, string tableName)
    {
        var systemSchema = GetSystemSchema(projectId);
        return QualifyName(systemSchema, tableName);
    }

    /// <inheritdoc/>
    public bool IsValidSchemaName(string schemaName)
    {
        if (string.IsNullOrEmpty(schemaName))
            return false;

        // PostgreSQL identifier limit
        if (schemaName.Length > 63)
            return false;

        // Must be alphanumeric with underscores
        if (!ValidSchemaCharacters().IsMatch(schemaName))
            return false;

        // Check if it follows MorphDB conventions
        var schemaType = GetSchemaType(schemaName);
        return schemaType != SchemaType.Unknown;
    }

    /// <summary>
    /// Extracts the first 8 characters of a project ID for schema naming.
    /// </summary>
    private static string GetProjectIdPrefix(Guid projectId)
    {
        // Use the first 8 characters of the GUID (without dashes)
        var idString = projectId.ToString("N");
        return idString[..ProjectIdPrefixLength].ToLowerInvariant();
    }

    /// <summary>
    /// Creates a deterministic GUID from a prefix string.
    /// This is used when parsing schema names back to project IDs.
    /// </summary>
    private static Guid CreateDeterministicGuid(string prefix)
    {
        // Pad the prefix to create a valid GUID string
        var paddedId = prefix.PadRight(32, '0');
        return Guid.Parse(paddedId);
    }

    [GeneratedRegex(@"^p_(?<id>[a-f0-9]{8})_(sys|dat)$", RegexOptions.IgnoreCase)]
    private static partial Regex SchemaNamePattern();

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*$")]
    private static partial Regex ValidSchemaCharacters();
}
