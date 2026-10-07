namespace MorphDB.Core.Abstractions;

using MorphDB.Core.Formula;
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
    /// Whether any formula expansion was generated.
    /// </summary>
    public bool HasExpansion => Expressions.Count > 0;
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
    /// The parsed syntax tree; null when parsing failed.
    /// </summary>
    public FormulaNode? Ast { get; init; }

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
        FormulaNode ast,
        IReadOnlyList<string> columnReferences,
        IReadOnlyList<string> functionCalls,
        bool isVolatile,
        MorphDataType? inferredType = null) => new()
        {
            IsSuccess = true,
            Ast = ast,
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
