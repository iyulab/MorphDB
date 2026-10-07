namespace MorphDB.Core.Abstractions;

using MorphDB.Core.Models;

/// <summary>
/// Resolves lookup fields by retrieving data from related tables.
/// Lookup fields are virtual columns that reference data in other tables via relations.
/// </summary>
public interface ILookupResolver
{
    /// <summary>
    /// Builds SQL JOIN clauses for lookup columns to include in a query.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="sourceTable">The source table metadata.</param>
    /// <param name="lookupColumns">Lookup columns to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>SQL fragments for JOINs and SELECT columns.</returns>
    Task<LookupQueryExpansion> BuildLookupExpansionAsync(
        Guid projectId,
        TableMetadata sourceTable,
        IReadOnlyList<LookupColumnInfo> lookupColumns,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a lookup column configuration.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="sourceTable">The source table containing the lookup column.</param>
    /// <param name="config">The lookup configuration to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result with any errors.</returns>
    Task<LookupValidationResult> ValidateLookupConfigAsync(
        Guid projectId,
        TableMetadata sourceTable,
        LookupColumnConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the metadata for lookup target columns.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="config">The lookup configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Target column metadata.</returns>
    Task<ColumnMetadata?> GetTargetColumnMetadataAsync(
        Guid projectId,
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
    /// The SQL expression of each lookup column, a correlated subquery over the table read as
    /// <c>base_table</c>. Key: logical column name, Value: SQL expression.
    /// </summary>
    public IReadOnlyDictionary<string, string> SelectExpressions { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Whether any lookup expansion was generated.
    /// </summary>
    public bool HasExpansion => SelectExpressions.Count > 0;
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

    /// <summary>
    /// Physical name of the target column the relation column's value is matched against.
    /// </summary>
    public string? MatchColumnPhysical { get; init; }

    /// <summary>
    /// Whether the target table does not exist and the lookup reads null meanwhile
    /// (<see cref="LookupTargetMissing.Null"/>). Only <see cref="RelationColumn"/> is set then.
    /// </summary>
    public bool IsTargetAbsent { get; init; }

    public static LookupValidationResult Valid(
        TableMetadata targetTable,
        ColumnMetadata targetColumn,
        ColumnMetadata relationColumn,
        string matchColumnPhysical) => new()
        {
            IsValid = true,
            TargetTable = targetTable,
            TargetColumn = targetColumn,
            RelationColumn = relationColumn,
            MatchColumnPhysical = matchColumnPhysical
        };

    public static LookupValidationResult TargetAbsent(ColumnMetadata relationColumn) => new()
    {
        IsValid = true,
        IsTargetAbsent = true,
        RelationColumn = relationColumn
    };

    public static LookupValidationResult Invalid(params string[] errors) => new()
    {
        IsValid = false,
        Errors = errors
    };
}
