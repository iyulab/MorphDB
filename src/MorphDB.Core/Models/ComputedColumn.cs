using System.Text.Json;
using MorphDB.Core.Abstractions;

namespace MorphDB.Core.Models;

/// <summary>
/// Configuration for a computed (virtual or stored) column.
/// Computed columns derive their values from expressions rather than direct storage.
/// </summary>
public sealed class ComputedColumnConfig
{
    /// <summary>
    /// The expression that computes the column value.
    /// Can reference other columns in the same table.
    /// Examples: "first_name || ' ' || last_name", "price * quantity", "UPPER(email)"
    /// </summary>
    public required string Expression { get; init; }

    /// <summary>
    /// When true, the computed value is stored in the database (GENERATED ALWAYS AS ... STORED).
    /// When false, the value is computed on read (virtual).
    /// Stored computed columns can be indexed.
    /// </summary>
    public bool IsStored { get; init; }

    /// <summary>
    /// Column references used in the expression (for dependency tracking).
    /// </summary>
    public IReadOnlyList<string> Dependencies { get; init; } = [];

    /// <summary>
    /// The expected return type of the expression.
    /// </summary>
    public MorphDataType ReturnType { get; init; }
}

/// <summary>
/// Configuration for a lookup field that references data from a related table.
/// </summary>
public sealed class LookupColumnConfig
{
    /// <summary>
    /// The column of this table whose value names the target row.
    /// </summary>
    public required string RelationColumn { get; init; }

    /// <summary>
    /// The target table to look up from.
    /// </summary>
    public required string TargetTable { get; init; }

    /// <summary>
    /// The column to retrieve from the target table.
    /// </summary>
    public required string TargetColumn { get; init; }

    /// <summary>
    /// The target column the relation column's value is matched against. When absent: the target
    /// column of the relation declared on the relation column, else the target's <c>_id</c>.
    /// </summary>
    public string? MatchColumn { get; init; }

    /// <summary>
    /// Which target row is read when several match: the first in this order, comma-separated
    /// <c>column [asc|desc]</c> terms over the target's columns. When absent: <c>_id</c> ascending.
    /// </summary>
    public string? OrderBy { get; init; }

    /// <summary>
    /// What a read does while the target cannot answer this declaration — the table absent, or without
    /// the read, matched or ordering column. Default <see cref="LookupTargetMissing.Fail"/>.
    /// </summary>
    public LookupTargetMissing WhenTargetMissing { get; init; }
}

/// <summary>
/// What a lookup does while its target cannot answer the declaration: the target table does not
/// exist, or exists without the read, matched or ordering column.
/// </summary>
public enum LookupTargetMissing
{
    /// <summary>
    /// The lookup is refused when declared and fails every read that needs it, naming what is missing.
    /// </summary>
    Fail = 0,

    /// <summary>
    /// The lookup is accepted and reads null — no target row matches — until the target answers the
    /// declaration, from then on reading it as usual. For a writer that builds tables in an order it
    /// does not control, or rebuilds a target by dropping and recreating it: a target not yet built
    /// and one redeclared but not yet rebuilt are the same passing state. What is wrong on the
    /// declaring side — a relation column this table lacks, an order that is not a column list —
    /// still fails. A misspelt target column also reads null rather than failing, so a writer that
    /// opts in checks its declarations against the targets it declares.
    /// </summary>
    Null = 1
}

/// <summary>
/// Configuration for a rollup field that aggregates data from related records.
/// </summary>
public sealed class RollupColumnConfig
{
    /// <summary>
    /// The relation that connects to the records to roll up.
    /// </summary>
    public required string Relation { get; init; }

    /// <summary>
    /// The table containing the records to roll up.
    /// </summary>
    public required string TargetTable { get; init; }

    /// <summary>
    /// The foreign key column in the target table that references this table.
    /// </summary>
    public required string ForeignKeyColumn { get; init; }

    /// <summary>
    /// The column in the target table to aggregate.
    /// Use "*" for COUNT operations.
    /// </summary>
    public required string SourceColumn { get; init; }

    /// <summary>
    /// The aggregation function to apply.
    /// </summary>
    public required RollupAggregation Aggregation { get; init; }

    /// <summary>
    /// Optional filter to apply before aggregation.
    /// </summary>
    public RollupFilter? Filter { get; init; }

    /// <summary>
    /// For StringAgg: the delimiter to use.
    /// </summary>
    public string? Delimiter { get; init; }

    /// <summary>
    /// For StringAgg/ArrayAgg: the ordering.
    /// </summary>
    public string? OrderBy { get; init; }
}

/// <summary>
/// Filter configuration for rollup operations.
/// </summary>
public sealed class RollupFilter
{
    public required string Field { get; init; }
    public required FilterOperator Operator { get; init; }
    public object? Value { get; init; }
}

/// <summary>
/// Configuration for a formula field with expression evaluation.
/// </summary>
public sealed class FormulaColumnConfig
{
    /// <summary>
    /// The formula expression in MorphDB formula syntax.
    /// Supports functions, operators, and references to other fields.
    /// </summary>
    public required string Formula { get; init; }

    /// <summary>
    /// The parsed AST of the formula (cached for performance).
    /// </summary>
    public JsonDocument? ParsedAst { get; init; }

    /// <summary>
    /// The return type of the formula (inferred or explicit).
    /// </summary>
    public MorphDataType ReturnType { get; init; }

    /// <summary>
    /// Fields referenced by this formula (for dependency tracking and invalidation).
    /// </summary>
    public IReadOnlyList<string> Dependencies { get; init; } = [];

    /// <summary>
    /// Whether the formula contains volatile functions (NOW(), TODAY(), etc.).
    /// Volatile formulas are recomputed on every read.
    /// </summary>
    public bool IsVolatile { get; init; }

    /// <summary>
    /// Format string for output (e.g., currency, percentage).
    /// </summary>
    public string? OutputFormat { get; init; }
}

/// <summary>
/// Aggregation types for rollup fields.
/// </summary>
public enum RollupAggregation
{
    /// <summary>Count of related records.</summary>
    Count,

    /// <summary>Count of non-null values in the source column.</summary>
    CountValues,

    /// <summary>Count of empty/null values in the source column.</summary>
    CountEmpty,

    /// <summary>Sum of numeric values.</summary>
    Sum,

    /// <summary>Average of numeric values.</summary>
    Average,

    /// <summary>Minimum value.</summary>
    Min,

    /// <summary>Maximum value.</summary>
    Max,

    /// <summary>Concatenate text values.</summary>
    StringConcat,

    /// <summary>Collect values into an array.</summary>
    ArrayValues,

    /// <summary>Percentage of checkboxes that are checked.</summary>
    PercentChecked,

    /// <summary>Percentage of checkboxes that are unchecked.</summary>
    PercentUnchecked,

    /// <summary>Earliest date.</summary>
    EarliestDate,

    /// <summary>Latest date.</summary>
    LatestDate,

    /// <summary>Range between earliest and latest dates.</summary>
    DateRange,

    /// <summary>Check if all values are truthy.</summary>
    AllTrue,

    /// <summary>Check if any value is truthy.</summary>
    AnyTrue
}

/// <summary>
/// Extended column metadata including computed, lookup, rollup, and formula configurations.
/// </summary>
public sealed class ExtendedColumnMetadata
{
    public required ColumnMetadata BaseMetadata { get; init; }

    /// <summary>
    /// Configuration for computed columns (PostgreSQL GENERATED columns).
    /// </summary>
    public ComputedColumnConfig? ComputedConfig { get; init; }

    /// <summary>
    /// Configuration for lookup fields.
    /// </summary>
    public LookupColumnConfig? LookupConfig { get; init; }

    /// <summary>
    /// Configuration for rollup fields.
    /// </summary>
    public RollupColumnConfig? RollupConfig { get; init; }

    /// <summary>
    /// Configuration for formula fields.
    /// </summary>
    public FormulaColumnConfig? FormulaConfig { get; init; }

    /// <summary>
    /// Whether this column is a derived type (computed, lookup, rollup, or formula).
    /// </summary>
    public bool IsDerived =>
        ComputedConfig != null ||
        LookupConfig != null ||
        RollupConfig != null ||
        FormulaConfig != null;
}
