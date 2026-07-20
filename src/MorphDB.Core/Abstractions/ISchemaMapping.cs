using MorphDB.Core.Models;

namespace MorphDB.Core.Abstractions;

/// <summary>
/// Provides mapping between logical and physical names.
/// </summary>
public interface ISchemaMapping
{
    /// <summary>
    /// Gets the physical table name from a logical name.
    /// </summary>
    string GetPhysicalTableName(Guid projectId, string logicalName);

    /// <summary>
    /// Gets the physical column name from a logical name.
    /// </summary>
    string GetPhysicalColumnName(Guid projectId, string tableName, string logicalColumnName);

    /// <summary>
    /// Gets column metadata by logical name.
    /// </summary>
    ColumnMetadata? GetColumnMetadata(Guid projectId, string tableName, string logicalColumnName);

    /// <summary>
    /// Gets table metadata by logical name.
    /// </summary>
    TableMetadata? GetTableMetadata(Guid projectId, string logicalName);

    /// <summary>
    /// Checks if a table exists.
    /// </summary>
    bool TableExists(Guid projectId, string logicalName);

    /// <summary>
    /// Checks if a column exists in a table.
    /// </summary>
    bool ColumnExists(Guid projectId, string tableName, string logicalColumnName);

    /// <summary>
    /// Gets all columns for a table.
    /// </summary>
    IReadOnlyList<ColumnMetadata> GetTableColumns(Guid projectId, string tableName);

    /// <summary>
    /// Gets relations for a table.
    /// </summary>
    IReadOnlyList<RelationMetadata> GetTableRelations(Guid projectId, string tableName);
}

/// <summary>
/// Cache for schema mappings with invalidation support.
/// </summary>
public interface ISchemaMappingCache : ISchemaMapping
{
    /// <summary>
    /// Invalidates cached mapping for a table.
    /// </summary>
    void InvalidateTable(Guid projectId, Guid tableId);

    /// <summary>
    /// Invalidates all cached mappings for a project.
    /// </summary>
    void InvalidateProject(Guid projectId);

    /// <summary>
    /// Refreshes the cache from the database.
    /// </summary>
    Task RefreshAsync(Guid projectId, CancellationToken cancellationToken = default);
}
