namespace MorphDB.Core.Abstractions;

using MorphDB.Core.Models;

/// <summary>
/// Resolves rollup fields by aggregating data from related tables.
/// Rollup fields compute aggregate values (COUNT, SUM, AVG, etc.) from child records.
/// </summary>
public interface IRollupResolver
{
    /// <summary>
    /// Builds SQL subquery for rollup columns to include in a query.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="sourceTable">The source table metadata.</param>
    /// <param name="rollupColumns">Rollup columns to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>SQL fragments for rollup subqueries.</returns>
    Task<RollupQueryExpansion> BuildRollupExpansionAsync(
        Guid projectId,
        TableMetadata sourceTable,
        IReadOnlyList<RollupColumnInfo> rollupColumns,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a rollup column configuration.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="sourceTable">The source table containing the rollup column.</param>
    /// <param name="config">The rollup configuration to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result with any errors.</returns>
    Task<RollupValidationResult> ValidateRollupConfigAsync(
        Guid projectId,
        TableMetadata sourceTable,
        RollupColumnConfig config,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Information about a rollup column to resolve.
/// </summary>
public sealed class RollupColumnInfo
{
    /// <summary>
    /// The logical name of the rollup column.
    /// </summary>
    public required string ColumnName { get; init; }

    /// <summary>
    /// The rollup configuration.
    /// </summary>
    public required RollupColumnConfig Config { get; init; }

    /// <summary>
    /// The data type of the rollup result.
    /// </summary>
    public MorphDataType? DataType { get; init; }
}

/// <summary>
/// SQL expansion for rollup columns in a query.
/// </summary>
public sealed class RollupQueryExpansion
{
    /// <summary>
    /// Correlated subquery expressions for rollup values.
    /// Key: logical column name, Value: SQL subquery expression.
    /// </summary>
    public IReadOnlyDictionary<string, string> SubqueryExpressions { get; init; } =
        new Dictionary<string, string>();


    /// <summary>
    /// Whether any rollup expansion was generated.
    /// </summary>
    public bool HasExpansion => SubqueryExpressions.Count > 0;
}

/// <summary>
/// Result of rollup configuration validation.
/// </summary>
public sealed class RollupValidationResult
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
    /// Source column metadata (column to aggregate, if found).
    /// </summary>
    public ColumnMetadata? SourceColumn { get; init; }

    /// <summary>
    /// Foreign key column metadata (if found).
    /// </summary>
    public ColumnMetadata? ForeignKeyColumn { get; init; }

    public static RollupValidationResult Valid(
        TableMetadata targetTable,
        ColumnMetadata? sourceColumn,
        ColumnMetadata foreignKeyColumn) => new()
        {
            IsValid = true,
            TargetTable = targetTable,
            SourceColumn = sourceColumn,
            ForeignKeyColumn = foreignKeyColumn
        };

    public static RollupValidationResult Invalid(params string[] errors) => new()
    {
        IsValid = false,
        Errors = errors
    };
}
