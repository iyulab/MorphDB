namespace MorphDB.Core.Abstractions;

using MorphDB.Core.Models;

/// <summary>
/// Resolves formula fields by evaluating expressions at query time.
/// Formula fields compute values from other columns in the same row using
/// expressions like arithmetic operations, string functions, and conditionals.
/// </summary>
public interface IFormulaResolver
{
    /// <summary>
    /// Builds SQL expressions for formula columns to include in a query SELECT.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="sourceTable">The source table metadata.</param>
    /// <param name="formulaColumns">Formula columns to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>SQL expressions for formula columns.</returns>
    Task<FormulaQueryExpansion> BuildFormulaExpansionAsync(
        Guid projectId,
        TableMetadata sourceTable,
        IReadOnlyList<FormulaColumnInfo> formulaColumns,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses a formula expression into an AST for validation and analysis.
    /// </summary>
    /// <param name="formula">The formula string to parse.</param>
    /// <returns>Parse result with AST or errors.</returns>
    FormulaParseResult ParseFormula(string formula);

    /// <summary>
    /// Validates a formula column configuration.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="sourceTable">The source table containing the formula column.</param>
    /// <param name="config">The formula configuration to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result with any errors.</returns>
    Task<FormulaValidationResult> ValidateFormulaConfigAsync(
        Guid projectId,
        TableMetadata sourceTable,
        FormulaColumnConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Infers the return type of a formula expression.
    /// </summary>
    /// <param name="formula">The formula expression.</param>
    /// <param name="columnTypes">Map of column names to their data types.</param>
    /// <returns>The inferred return type.</returns>
    MorphDataType InferReturnType(
        string formula,
        IReadOnlyDictionary<string, MorphDataType> columnTypes);
}

/// <summary>
/// Information about a formula column to resolve.
/// </summary>
public sealed class FormulaColumnInfo
{
    /// <summary>
    /// The logical name of the formula column.
    /// </summary>
    public required string ColumnName { get; init; }

    /// <summary>
    /// The formula configuration.
    /// </summary>
    public required FormulaColumnConfig Config { get; init; }

    /// <summary>
    /// The data type of the formula result.
    /// </summary>
    public MorphDataType? DataType { get; init; }
}

/// <summary>
/// SQL expansion for formula columns in a query.
/// </summary>
public sealed class FormulaQueryExpansion
{
    /// <summary>
    /// SQL expressions for formula values.
    /// Key: logical column name, Value: SQL expression.
    /// </summary>
    public IReadOnlyDictionary<string, string> Expressions { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Structured expression information for query builder integration.
    /// </summary>
    public IReadOnlyList<FormulaExpressionInfo> FormulaExpressions { get; init; } = [];

    /// <summary>
    /// Whether any formula expansion was generated.
    /// </summary>
    public bool HasExpansion => Expressions.Count > 0 || FormulaExpressions.Count > 0;
}

/// <summary>
/// Structured information for a formula expression.
/// </summary>
public sealed class FormulaExpressionInfo
{
    /// <summary>
    /// The logical column name.
    /// </summary>
    public required string ColumnName { get; init; }

    /// <summary>
    /// The original formula expression.
    /// </summary>
    public required string OriginalFormula { get; init; }

    /// <summary>
    /// The SQL expression (translated from formula syntax).
    /// </summary>
    public required string SqlExpression { get; init; }

    /// <summary>
    /// The expected return type.
    /// </summary>
    public required MorphDataType ReturnType { get; init; }

    /// <summary>
    /// Whether the formula contains volatile functions (NOW(), etc.).
    /// </summary>
    public bool IsVolatile { get; init; }

    /// <summary>
    /// Physical column names referenced by this formula.
    /// </summary>
    public IReadOnlyList<string> Dependencies { get; init; } = [];
}

/// <summary>
/// Result of parsing a formula expression.
/// </summary>
public sealed class FormulaParseResult
{
    /// <summary>
    /// Whether parsing was successful.
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Parse errors, if any.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// The parsed AST (as JSON for storage).
    /// </summary>
    public string? AstJson { get; init; }

    /// <summary>
    /// Column references found in the formula.
    /// </summary>
    public IReadOnlyList<string> ColumnReferences { get; init; } = [];

    /// <summary>
    /// Function calls found in the formula.
    /// </summary>
    public IReadOnlyList<string> FunctionCalls { get; init; } = [];

    /// <summary>
    /// Whether the formula contains volatile functions.
    /// </summary>
    public bool IsVolatile { get; init; }

    /// <summary>
    /// Inferred return type of the formula.
    /// </summary>
    public MorphDataType? InferredType { get; init; }

    public static FormulaParseResult Success(
        string astJson,
        IReadOnlyList<string> columnReferences,
        IReadOnlyList<string> functionCalls,
        bool isVolatile,
        MorphDataType? inferredType = null) => new()
        {
            IsSuccess = true,
            AstJson = astJson,
            ColumnReferences = columnReferences,
            FunctionCalls = functionCalls,
            IsVolatile = isVolatile,
            InferredType = inferredType
        };

    public static FormulaParseResult Failure(params string[] errors) => new()
    {
        IsSuccess = false,
        Errors = errors
    };
}

/// <summary>
/// Result of formula configuration validation.
/// </summary>
public sealed class FormulaValidationResult
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
    /// Resolved column dependencies with their physical names.
    /// </summary>
    public IReadOnlyDictionary<string, string> ResolvedDependencies { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// The inferred return type.
    /// </summary>
    public MorphDataType? InferredType { get; init; }

    public static FormulaValidationResult Valid(
        IReadOnlyDictionary<string, string> resolvedDependencies,
        MorphDataType? inferredType = null) => new()
        {
            IsValid = true,
            ResolvedDependencies = resolvedDependencies,
            InferredType = inferredType
        };

    public static FormulaValidationResult Invalid(params string[] errors) => new()
    {
        IsValid = false,
        Errors = errors
    };
}
