namespace MorphDB.Core.Abstractions;

using MorphDB.Core.Models;

/// <summary>
/// Resolves lookup fields by retrieving data from related tables.
/// Lookup fields are virtual columns that reference data in other tables via relations.
/// </summary>
public interface ILookupResolver
{
    /// <summary>
    /// Resolves lookup values for a set of records.
    /// </summary>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="records">Records containing the relation key values.</param>
    /// <param name="lookupColumns">Lookup column configurations to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Records enriched with resolved lookup values.</returns>
    Task<IReadOnlyList<IDictionary<string, object?>>> ResolveLookupValuesAsync(
        Guid tenantId,
        IReadOnlyList<IDictionary<string, object?>> records,
        IReadOnlyList<LookupColumnInfo> lookupColumns,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds SQL JOIN clauses for lookup columns to include in a query.
    /// </summary>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="sourceTable">The source table metadata.</param>
    /// <param name="lookupColumns">Lookup columns to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>SQL fragments for JOINs and SELECT columns.</returns>
    Task<LookupQueryExpansion> BuildLookupExpansionAsync(
        Guid tenantId,
        TableMetadata sourceTable,
        IReadOnlyList<LookupColumnInfo> lookupColumns,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a lookup column configuration.
    /// </summary>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="sourceTable">The source table containing the lookup column.</param>
    /// <param name="config">The lookup configuration to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result with any errors.</returns>
    Task<LookupValidationResult> ValidateLookupConfigAsync(
        Guid tenantId,
        TableMetadata sourceTable,
        LookupColumnConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the metadata for lookup target columns.
    /// </summary>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="config">The lookup configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Target column metadata.</returns>
    Task<ColumnMetadata?> GetTargetColumnMetadataAsync(
        Guid tenantId,
        LookupColumnConfig config,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Information about a lookup column to resolve.
/// </summary>
public sealed class LookupColumnInfo
{
    /// <summary>
    /// The logical name of the lookup column.
    /// </summary>
    public required string ColumnName { get; init; }

    /// <summary>
    /// The lookup configuration.
    /// </summary>
    public required LookupColumnConfig Config { get; init; }

    /// <summary>
    /// The data type of the lookup result.
    /// </summary>
    public MorphDataType? DataType { get; init; }
}

/// <summary>
/// SQL expansion for lookup columns in a query.
/// </summary>
public sealed class LookupQueryExpansion
{
    /// <summary>
    /// SQL JOIN clauses to add to the query (raw SQL format).
    /// </summary>
    public IReadOnlyList<string> JoinClauses { get; init; } = [];

    /// <summary>
    /// Structured JOIN information for query builder integration.
    /// </summary>
    public IReadOnlyList<LookupJoinInfo> Joins { get; init; } = [];

    /// <summary>
    /// SELECT column expressions for lookup values.
    /// Key: logical column name, Value: SQL expression.
    /// </summary>
    public IReadOnlyDictionary<string, string> SelectExpressions { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Table aliases used in the expansion.
    /// Key: target table name, Value: alias.
    /// </summary>
    public IReadOnlyDictionary<string, string> TableAliases { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Whether any lookup expansion was generated.
    /// </summary>
    public bool HasExpansion => JoinClauses.Count > 0 || Joins.Count > 0;
}

/// <summary>
/// Structured information for a lookup JOIN.
/// </summary>
public sealed class LookupJoinInfo
{
    /// <summary>
    /// Physical name of the target table.
    /// </summary>
    public required string TargetTablePhysical { get; init; }

    /// <summary>
    /// Alias for the target table in the query.
    /// </summary>
    public required string TargetTableAlias { get; init; }

    /// <summary>
    /// Physical name of the source column (the relation/FK column in base table).
    /// </summary>
    public required string SourceColumnPhysical { get; init; }

    /// <summary>
    /// Physical name of the target column (usually the PK in target table).
    /// </summary>
    public required string TargetColumnPhysical { get; init; }
}

/// <summary>
/// Result of lookup configuration validation.
/// </summary>
public sealed class LookupValidationResult
{
    /// <summary>
    /// Whether the configuration is valid.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Validation errors, if any.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// Target table metadata (if found).
    /// </summary>
    public TableMetadata? TargetTable { get; init; }

    /// <summary>
    /// Target column metadata (if found).
    /// </summary>
    public ColumnMetadata? TargetColumn { get; init; }

    /// <summary>
    /// Relation column metadata (if found).
    /// </summary>
    public ColumnMetadata? RelationColumn { get; init; }

    public static LookupValidationResult Valid(
        TableMetadata targetTable,
        ColumnMetadata targetColumn,
        ColumnMetadata relationColumn) => new()
        {
            IsValid = true,
            TargetTable = targetTable,
            TargetColumn = targetColumn,
            RelationColumn = relationColumn
        };

    public static LookupValidationResult Invalid(params string[] errors) => new()
    {
        IsValid = false,
        Errors = errors
    };
}
