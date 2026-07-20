using MorphDB.Core.Abstractions;
using MorphDB.Core.Formula;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Repositories;

namespace MorphDB.Npgsql.Services;

/// <summary>
/// PostgreSQL implementation of formula field resolution.
/// Translates formula expressions to PostgreSQL SQL for query-time evaluation.
/// </summary>
public sealed class PostgresFormulaResolver : IFormulaResolver
{
    private readonly IMetadataRepository _metadataRepository;
    private readonly FormulaParser _parser;

    public PostgresFormulaResolver(IMetadataRepository metadataRepository)
    {
        _metadataRepository = metadataRepository;
        _parser = new FormulaParser();
    }

    public async Task<FormulaQueryExpansion> BuildFormulaExpansionAsync(
        Guid projectId,
        TableMetadata sourceTable,
        IReadOnlyList<FormulaColumnInfo> formulaColumns,
        CancellationToken cancellationToken = default)
    {
        if (formulaColumns.Count == 0)
        {
            return new FormulaQueryExpansion();
        }

        var expressions = new Dictionary<string, string>();
        var formulaExpressions = new List<FormulaExpressionInfo>();

        // Build column logical-to-physical mapping
        var columnMappings = sourceTable.Columns
            .Where(c => !string.IsNullOrEmpty(c.PhysicalName) && c.PhysicalName != "virtual")
            .ToDictionary(c => c.LogicalName, c => c.PhysicalName);

        // Also add _id if present (common primary key)
        var idColumn = sourceTable.Columns.FirstOrDefault(c => c.LogicalName == "_id");
        if (idColumn != null && !columnMappings.ContainsKey("_id"))
        {
            columnMappings["_id"] = idColumn.PhysicalName;
        }

        var translator = new FormulaSqlTranslator(columnMappings);

        foreach (var formula in formulaColumns)
        {
            // Parse the formula if not already parsed
            var parseResult = _parser.Parse(formula.Config.Formula);
            if (!parseResult.IsSuccess)
            {
                // Generate an error-indicating expression
                expressions[formula.ColumnName] = $"NULL /* formula error: {string.Join(", ", parseResult.Errors)} */";
                continue;
            }

            // Translate to SQL
            var (sql, errors) = translator.Translate(parseResult.AstJson!, "t");

            if (errors.Count > 0)
            {
                expressions[formula.ColumnName] = $"NULL /* translation error: {string.Join(", ", errors)} */";
                continue;
            }

            expressions[formula.ColumnName] = sql;

            // Collect resolved dependencies
            var resolvedDeps = new List<string>();
            foreach (var colRef in parseResult.ColumnReferences)
            {
                if (columnMappings.TryGetValue(colRef, out var physicalName))
                {
                    resolvedDeps.Add(physicalName);
                }
            }

            formulaExpressions.Add(new FormulaExpressionInfo
            {
                ColumnName = formula.ColumnName,
                OriginalFormula = formula.Config.Formula,
                SqlExpression = sql,
                ReturnType = formula.Config.ReturnType,
                IsVolatile = parseResult.IsVolatile,
                Dependencies = resolvedDeps
            });
        }

        return new FormulaQueryExpansion
        {
            Expressions = expressions,
            FormulaExpressions = formulaExpressions
        };
    }

    public FormulaParseResult ParseFormula(string formula)
    {
        return _parser.Parse(formula);
    }

    public async Task<FormulaValidationResult> ValidateFormulaConfigAsync(
        Guid projectId,
        TableMetadata sourceTable,
        FormulaColumnConfig config,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(config.Formula))
        {
            return FormulaValidationResult.Invalid("Formula cannot be empty");
        }

        // Parse the formula
        var parseResult = _parser.Parse(config.Formula);
        if (!parseResult.IsSuccess)
        {
            return FormulaValidationResult.Invalid(parseResult.Errors.ToArray());
        }

        // Validate column references
        var errors = new List<string>();
        var resolvedDeps = new Dictionary<string, string>();

        foreach (var colRef in parseResult.ColumnReferences)
        {
            var column = sourceTable.Columns.FirstOrDefault(c =>
                c.LogicalName.Equals(colRef, StringComparison.OrdinalIgnoreCase));

            if (column == null)
            {
                errors.Add($"Unknown column referenced in formula: {colRef}");
            }
            else if (!string.IsNullOrEmpty(column.PhysicalName) && column.PhysicalName != "virtual")
            {
                resolvedDeps[colRef] = column.PhysicalName;
            }
            else
            {
                // Referencing another virtual column - this is allowed but the
                // order of virtual column expansion matters
                resolvedDeps[colRef] = $"virtual_{colRef}";
            }
        }

        if (errors.Count > 0)
        {
            return FormulaValidationResult.Invalid(errors.ToArray());
        }

        return FormulaValidationResult.Valid(resolvedDeps, parseResult.InferredType);
    }

    public MorphDataType InferReturnType(
        string formula,
        IReadOnlyDictionary<string, MorphDataType> columnTypes)
    {
        var parseResult = _parser.Parse(formula);
        if (!parseResult.IsSuccess)
        {
            return MorphDataType.Text; // Default to text on parse failure
        }

        // Simple type inference based on function calls and operators
        var functions = parseResult.FunctionCalls.Select(f => f.ToUpperInvariant()).ToHashSet();

        // Date functions return DateTime
        if (functions.Contains("NOW") || functions.Contains("TODAY") ||
            functions.Contains("DATEADD") || functions.Contains("DATE"))
        {
            return MorphDataType.DateTime;
        }

        // String functions return text
        if (functions.Contains("CONCAT") || functions.Contains("UPPER") ||
            functions.Contains("LOWER") || functions.Contains("TRIM") ||
            functions.Contains("LEFT") || functions.Contains("RIGHT") ||
            functions.Contains("SUBSTRING") || functions.Contains("REPLACE"))
        {
            return MorphDataType.Text;
        }

        // Numeric functions return numeric
        if (functions.Contains("SUM") || functions.Contains("AVG") ||
            functions.Contains("ABS") || functions.Contains("ROUND") ||
            functions.Contains("FLOOR") || functions.Contains("CEIL") ||
            functions.Contains("POWER") || functions.Contains("SQRT"))
        {
            return MorphDataType.Decimal;
        }

        // Boolean functions
        if (functions.Contains("AND") || functions.Contains("OR") || functions.Contains("NOT"))
        {
            return MorphDataType.Boolean;
        }

        // If referencing columns, try to infer from column types
        if (parseResult.ColumnReferences.Count > 0 && columnTypes.Count > 0)
        {
            // For arithmetic operations, if any column is decimal, result is decimal
            // For comparison operations, result is boolean
            var referencedTypes = parseResult.ColumnReferences
                .Where(columnTypes.ContainsKey)
                .Select(c => columnTypes[c])
                .ToList();

            if (referencedTypes.Any(t => t == MorphDataType.Decimal || t == MorphDataType.Integer || t == MorphDataType.BigInteger))
            {
                return MorphDataType.Decimal;
            }

            if (referencedTypes.Count == 1)
            {
                return referencedTypes[0];
            }
        }

        // Default to text
        return MorphDataType.Text;
    }
}
