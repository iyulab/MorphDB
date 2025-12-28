using MorphDB.Core.Models;

namespace MorphDB.Core.Abstractions;

/// <summary>
/// Resolves PostgreSQL schema names for projects following the naming convention:
/// - System schema: p_{projectId8char}_sys
/// - Data schema: p_{projectId8char}_dat
///
/// Example: For project ID "a1b2c3d4-e5f6-7890-abcd-ef1234567890"
/// - System: p_a1b2c3d4_sys
/// - Data: p_a1b2c3d4_dat
/// </summary>
public interface ISchemaNameResolver
{
    /// <summary>
    /// Gets the system schema name for a project.
    /// System schema contains metadata tables: _tables, _columns, _indexes, etc.
    /// </summary>
    string GetSystemSchema(Guid projectId);

    /// <summary>
    /// Gets the data schema name for a project.
    /// Data schema contains user-defined data tables with logical names directly.
    /// </summary>
    string GetDataSchema(Guid projectId);

    /// <summary>
    /// Gets both schema names for a project.
    /// </summary>
    SchemaNames GetSchemaNames(Guid projectId);

    /// <summary>
    /// Parses a schema name and extracts the project ID if it's a valid MorphDB schema.
    /// </summary>
    /// <param name="schemaName">The schema name to parse.</param>
    /// <param name="projectId">The extracted project ID if parsing succeeds.</param>
    /// <returns>True if the schema name is a valid MorphDB schema.</returns>
    bool TryParseSchemaName(string schemaName, out Guid projectId);

    /// <summary>
    /// Determines the schema type from a schema name.
    /// </summary>
    SchemaType GetSchemaType(string schemaName);

    /// <summary>
    /// Generates a fully qualified object name (schema.object).
    /// </summary>
    string QualifyName(string schemaName, string objectName);

    /// <summary>
    /// Generates a fully qualified table name for data tables.
    /// </summary>
    string QualifyDataTable(Guid projectId, string tableName);

    /// <summary>
    /// Generates a fully qualified table name for system tables.
    /// </summary>
    string QualifySystemTable(Guid projectId, string tableName);

    /// <summary>
    /// Validates that a schema name follows MorphDB naming conventions.
    /// </summary>
    bool IsValidSchemaName(string schemaName);

    /// <summary>
    /// Gets the global control plane schema name (morphdb).
    /// </summary>
    string GlobalSchema { get; }
}

/// <summary>
/// Contains both schema names for a project.
/// </summary>
public readonly record struct SchemaNames(string SystemSchema, string DataSchema);

/// <summary>
/// Types of PostgreSQL schemas managed by MorphDB.
/// </summary>
public enum SchemaType
{
    /// <summary>
    /// Unknown or unrecognized schema.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Global control plane schema (morphdb).
    /// </summary>
    Global = 1,

    /// <summary>
    /// Project system schema (p_{id}_sys).
    /// </summary>
    ProjectSystem = 2,

    /// <summary>
    /// Project data schema (p_{id}_dat).
    /// </summary>
    ProjectData = 3,

    /// <summary>
    /// PostgreSQL public schema.
    /// </summary>
    Public = 4
}
