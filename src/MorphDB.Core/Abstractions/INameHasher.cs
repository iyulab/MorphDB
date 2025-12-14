namespace MorphDB.Core.Abstractions;

/// <summary>
/// Generates hash-based physical names for tables and columns.
/// </summary>
public interface INameHasher
{
    /// <summary>
    /// Generates a physical table name from a logical name.
    /// Format: tbl_{hash}
    /// </summary>
    string GenerateTableName(Guid tenantId, string logicalName);

    /// <summary>
    /// Generates a physical column name from a logical name.
    /// Format: col_{hash}
    /// </summary>
    string GenerateColumnName(Guid tableId, string logicalName);

    /// <summary>
    /// Generates a physical index name.
    /// Format: idx_{hash}
    /// </summary>
    string GenerateIndexName(Guid tableId, string logicalName);

    /// <summary>
    /// Generates a physical constraint name.
    /// Format: chk_{hash} or fk_{hash}
    /// </summary>
    string GenerateConstraintName(string prefix, Guid tableId, string logicalName);

    /// <summary>
    /// Validates that a physical name adheres to PostgreSQL limits (63 chars).
    /// </summary>
    bool IsValidPhysicalName(string name);
}
