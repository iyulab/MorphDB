using System.Text.RegularExpressions;
using MorphDB.Core.Abstractions;

namespace MorphDB.Npgsql.Schema;

/// <summary>
/// PostgreSQL implementation of schema naming.
/// Naming convention for a new project:
/// - System schema: p_{projectIdAs32HexDigits}_sys
/// - Data schema: p_{projectIdAs32HexDigits}_dat
/// - Global schema: morphdb
/// Projects created under the earlier rule keep their p_{first8HexDigits}_{sys|dat} schemas; their
/// names are recorded with the project, which is where every operation reads them from.
/// </summary>
public sealed partial class PostgresSchemaNameResolver : ISchemaNameResolver
{
    private const string GlobalSchemaName = "morphdb";
    private const string ProjectSchemaPrefix = "p_";
    private const string SystemSchemaSuffix = "_sys";
    private const string DataSchemaSuffix = "_dat";

    /// <inheritdoc/>
    public string GlobalSchema => GlobalSchemaName;

    /// <inheritdoc/>
    public SchemaNames NameSchemasForNewProject(Guid projectId)
    {
        var id = projectId.ToString("N");
        return new SchemaNames(
            $"{ProjectSchemaPrefix}{id}{SystemSchemaSuffix}",
            $"{ProjectSchemaPrefix}{id}{DataSchemaSuffix}");
    }

    /// <inheritdoc/>
    public bool TryParseSchemaName(string schemaName, out Guid projectId)
    {
        projectId = Guid.Empty;

        if (string.IsNullOrEmpty(schemaName))
            return false;

        var match = WholeIdSchemaNamePattern().Match(schemaName);
        return match.Success && Guid.TryParseExact(match.Groups["id"].Value, "N", out projectId);
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

    [GeneratedRegex(@"^p_(?<id>[a-f0-9]{32})_(sys|dat)$", RegexOptions.IgnoreCase)]
    private static partial Regex WholeIdSchemaNamePattern();

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*$")]
    private static partial Regex ValidSchemaCharacters();
}
