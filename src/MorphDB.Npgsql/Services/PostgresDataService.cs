using System.Dynamic;
using Dapper;
using Microsoft.Extensions.Options;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;
using MorphDB.Core.Security;
using MorphDB.Npgsql.Dml;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Query;
using MorphDB.Npgsql.Repositories;
using Npgsql;

namespace MorphDB.Npgsql.Services;

/// <summary>
/// PostgreSQL implementation of IMorphDataService.
/// Handles CRUD operations with logical-to-physical name translation.
/// </summary>
public sealed class PostgresDataService : IMorphDataService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IMetadataRepository _metadataRepository;
    private readonly ISecurityPolicyService _securityPolicyService;
    private readonly ISecurityContextAccessor _securityContextAccessor;
    private readonly IWritePipeline _writePipeline;
    private readonly ILookupResolver? _lookupResolver;
    private readonly IRollupResolver? _rollupResolver;
    private readonly IFormulaResolver? _formulaResolver;
    private readonly string _primaryKeyLogicalName;

    /// <summary>
    /// Creates a new PostgresDataService.
    /// </summary>
    public PostgresDataService(
        NpgsqlDataSource dataSource,
        IMetadataRepository metadataRepository,
        ISecurityPolicyService securityPolicyService,
        ISecurityContextAccessor securityContextAccessor,
        IWritePipeline writePipeline,
        ILookupResolver? lookupResolver = null,
        IRollupResolver? rollupResolver = null,
        IFormulaResolver? formulaResolver = null,
        string primaryKeyLogicalName = "id")
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _metadataRepository = metadataRepository ?? throw new ArgumentNullException(nameof(metadataRepository));
        _securityPolicyService = securityPolicyService ?? throw new ArgumentNullException(nameof(securityPolicyService));
        _securityContextAccessor = securityContextAccessor ?? throw new ArgumentNullException(nameof(securityContextAccessor));
        _writePipeline = writePipeline ?? throw new ArgumentNullException(nameof(writePipeline));
        _lookupResolver = lookupResolver;
        _rollupResolver = rollupResolver;
        _formulaResolver = formulaResolver;
        _primaryKeyLogicalName = primaryKeyLogicalName;
    }

    /// <inheritdoc />
    public IMorphQueryBuilder Query(Guid projectId)
    {
        return new MorphQueryBuilder(
            _dataSource,
            _metadataRepository,
            _securityPolicyService,
            _securityContextAccessor,
            projectId,
            _lookupResolver,
            _rollupResolver,
            _formulaResolver);
    }

    /// <inheritdoc />
    public async Task<IDictionary<string, object?>?> GetByIdAsync(
        Guid projectId,
        string tableName,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableWithColumnsAsync(projectId, tableName, cancellationToken);
        var idColumn = GetPrimaryKeyColumn(table);

        // One record is a query narrowed to its id, not a statement of its own: a separate path is
        // how a single read came to skip row-level security and leave out every lookup, rollup and
        // formula value the same row carries when it is listed.
        var row = await Query(projectId)
            .From(tableName)
            .SelectAll()
            .Where(idColumn.LogicalName, FilterOperator.Equals, id)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            return null;

        return row;
    }

    /// <inheritdoc />
    public async Task<IDictionary<string, object?>> InsertAsync(
        Guid projectId,
        string tableName,
        IDictionary<string, object?> data,
        CancellationToken cancellationToken = default)
    {
        // Every write goes through the pipeline. This service used to build its own SQL, which made
        // it a second door past every validator and transformer: batch, seed, transaction and
        // GraphQL callers got no virtual constraints and silent unknown-field drops. One door now.
        var table = await GetTableWithColumnsAsync(projectId, tableName, cancellationToken);
        var result = await _writePipeline.InsertAsync(projectId, table, data, null, cancellationToken);
        return UnwrapOrThrow(result, tableName);
    }

    /// <inheritdoc />
    public async Task<IDictionary<string, object?>> UpdateAsync(
        Guid projectId,
        string tableName,
        Guid id,
        IDictionary<string, object?> data,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableWithColumnsAsync(projectId, tableName, cancellationToken);
        var result = await _writePipeline.UpdateAsync(projectId, table, id, data, null, null, cancellationToken);
        if (!result.Success && result.Errors.Any(e => e.Code == ValidationErrorCodes.NotFound))
            throw new NotFoundException($"Record with id '{id}' not found in table '{tableName}'");
        return UnwrapOrThrow(result, tableName);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        Guid projectId,
        string tableName,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableWithColumnsAsync(projectId, tableName, cancellationToken);
        var result = await _writePipeline.DeleteAsync(projectId, table, id, null, null, cancellationToken);
        // The historical contract: false for a record that was not there, not an exception.
        return result.Success;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IDictionary<string, object?>>> InsertBatchAsync(
        Guid projectId,
        string tableName,
        IReadOnlyList<IDictionary<string, object?>> records,
        CancellationToken cancellationToken = default)
    {
        if (records.Count == 0)
            return Array.Empty<IDictionary<string, object?>>();

        var table = await GetTableWithColumnsAsync(projectId, tableName, cancellationToken);

        // Row-by-row through the pipeline (this path always inserted row-by-row), atomic via the
        // ambient connection scope: the executor picks up this transaction instead of opening its own.
        var results = new List<IDictionary<string, object?>>(records.Count);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        using var scope = ConnectionScope.Begin(connection, transaction);

        try
        {
            foreach (var record in records)
            {
                var result = await _writePipeline.InsertAsync(projectId, table, record, null, cancellationToken);
                results.Add(UnwrapOrThrow(result, tableName));
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<int> UpdateBatchAsync(
        Guid projectId,
        string tableName,
        IDictionary<string, object?> data,
        IMorphQuery whereClause,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableWithColumnsAsync(projectId, tableName, cancellationToken);

        // Map logical names to physical and prepare parameters. Unlike the pipeline executor's
        // historical skip, this service's Prepare* rejects unknown columns outright — the bulk
        // UPDATE door never silently dropped fields (pinned by ErrorSurfaceContractTests).
        var (setColumns, values) = PrepareUpdateParameters(data, table.Columns);

        // Get physical WHERE clause SQL from the query
        var (whereSql, whereParams) = await whereClause.GetPhysicalWhereClauseAsync(cancellationToken);

        // Merge parameters. The SET values are named set0, set1, … and the WHERE bindings p0, p1, … —
        // when both used p{n}, the filter's value overwrote the one being written, so a bulk update set
        // its rows to the value it had filtered on. A collision is a defect, not something to resolve.
        var valuesDict = (IDictionary<string, object?>)values;
        foreach (var (key, value) in whereParams)
        {
            if (!valuesDict.TryAdd(key, value))
            {
                throw new InvalidOperationException($"Bulk update parameter '{key}' is bound twice.");
            }
        }

        var sql = DmlBuilder.BuildBatchUpdate(table.PhysicalName, setColumns, whereSql);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var affectedRows = await connection.ExecuteAsync(
            new CommandDefinition(sql, values, cancellationToken: cancellationToken));

        return affectedRows;
    }

    /// <inheritdoc />
    public async Task<int> DeleteBatchAsync(
        Guid projectId,
        string tableName,
        IMorphQuery whereClause,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableWithColumnsAsync(projectId, tableName, cancellationToken);

        // Get physical WHERE clause SQL from the query
        var (whereSql, whereParams) = await whereClause.GetPhysicalWhereClauseAsync(cancellationToken);

        // Build DELETE statement with WHERE clause if present
        var sql = string.IsNullOrEmpty(whereSql)
            ? $"DELETE FROM {table.PhysicalName}"
            : $"DELETE FROM {table.PhysicalName} WHERE {whereSql}";

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var affectedRows = await connection.ExecuteAsync(
            new CommandDefinition(sql, whereParams, cancellationToken: cancellationToken));

        return affectedRows;
    }

    private static string ExtractWhereClause(IMorphQuery query)
    {
        // Get full SQL and extract WHERE clause
        var fullSql = query.ToSql();
        var whereIndex = fullSql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);
        if (whereIndex < 0)
            return "";

        // Find the end of WHERE clause (before ORDER BY, LIMIT, etc.)
        var endKeywords = new[] { "ORDER BY", "GROUP BY", "HAVING", "LIMIT", "OFFSET" };
        var endIndex = fullSql.Length;

        foreach (var keyword in endKeywords)
        {
            var keywordIndex = fullSql.IndexOf(keyword, whereIndex, StringComparison.OrdinalIgnoreCase);
            if (keywordIndex > 0 && keywordIndex < endIndex)
            {
                endIndex = keywordIndex;
            }
        }

        // Extract the WHERE clause and strip the "WHERE" keyword since BuildBatchUpdate adds it
        var whereClause = fullSql[whereIndex..endIndex].Trim();
        if (whereClause.StartsWith("WHERE", StringComparison.OrdinalIgnoreCase))
        {
            whereClause = whereClause[5..].TrimStart(); // Remove "WHERE" and leading whitespace
        }

        return whereClause;
    }

    /// <inheritdoc />
    public async Task<IDictionary<string, object?>> UpsertAsync(
        Guid projectId,
        string tableName,
        IDictionary<string, object?> data,
        string[] keyColumns,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableWithColumnsAsync(projectId, tableName, cancellationToken);
        var result = await _writePipeline.UpsertAsync(projectId, table, data, keyColumns, null, cancellationToken);
        return UnwrapOrThrow(result, tableName);
    }

    #region Private Helper Methods

    /// <summary>
    /// This interface reports failure by exception; the pipeline reports it as data. The seam
    /// translates: pipeline validation errors become the ValidationException callers of this
    /// service have always received.
    /// </summary>
    private static IDictionary<string, object?> UnwrapOrThrow(WriteResult result, string tableName)
    {
        if (result.Success)
        {
            return result.Data ?? new Dictionary<string, object?>();
        }

        var message = result.Errors.Count == 1
            ? result.Errors[0].Message
            : string.Join("; ", result.Errors.Select(e => e.Message));
        throw new ValidationException($"Write to table '{tableName}' failed: {message}");
    }

    private async Task<TableMetadata> GetTableWithColumnsAsync(
        Guid projectId,
        string tableName,
        CancellationToken cancellationToken)
    {
        var table = await _metadataRepository.GetTableByNameAsync(projectId, tableName, includeColumns: true, cancellationToken);

        if (table is null)
            throw new TableNotFoundException(tableName);

        if (table.Columns.Count == 0)
            throw new InvalidOperationException($"Table '{tableName}' has no columns");

        return table;
    }

    private ColumnMetadata GetPrimaryKeyColumn(TableMetadata table)
    {
        var pkColumn = table.Columns.FirstOrDefault(c => c.IsPrimaryKey)
            ?? table.Columns.FirstOrDefault(c => c.LogicalName == _primaryKeyLogicalName);

        if (pkColumn is null)
            throw new InvalidOperationException($"Table '{table.LogicalName}' has no primary key column");

        return pkColumn;
    }

    /// <summary>
    /// Ensures the project_id is included in the data dictionary.
    /// If not present, adds it; if present, verifies it matches.
    /// </summary>
    private static IDictionary<string, object?> EnsureProjectId(IDictionary<string, object?> data, Guid projectId)
    {
        const string ProjectIdColumn = "project_id";

        if (data.TryGetValue(ProjectIdColumn, out var existingValue))
        {
            // If project_id is already in data, verify it matches
            if (existingValue is Guid existingGuid && existingGuid != projectId)
            {
                throw new ValidationException($"Provided project_id '{existingGuid}' does not match the expected project_id '{projectId}'");
            }
            return data;
        }

        // Create a new dictionary with project_id added
        var result = new Dictionary<string, object?>(data)
        {
            [ProjectIdColumn] = projectId
        };
        return result;
    }

    private static (List<string> Columns, List<string> Parameters, dynamic Values) PrepareInsertParameters(
        IDictionary<string, object?> data,
        IReadOnlyList<ColumnMetadata> columns)
    {
        var columnMap = columns.ToDictionary(c => c.LogicalName, c => c);
        var physicalColumns = new List<string>();
        var parameterNames = new List<string>();
        dynamic values = new ExpandoObject();
        var valuesDict = (IDictionary<string, object?>)values;

        int paramIndex = 0;
        foreach (var (logicalName, value) in data)
        {
            if (!columnMap.TryGetValue(logicalName, out var column))
            {
                throw new ValidationException($"Column '{logicalName}' not found in table metadata");
            }

            physicalColumns.Add(column.PhysicalName);
            var paramName = $"@p{paramIndex}";
            parameterNames.Add(TypeMapper.IsJsonbType(column.DataType) ? $"{paramName}::jsonb" : paramName);

            // Convert value to database type
            var dbValue = TypeMapper.ToDbValue(value, column.DataType);
            valuesDict[$"p{paramIndex}"] = dbValue;

            paramIndex++;
        }

        return (physicalColumns, parameterNames, values);
    }

    private static (List<(string ColumnName, string ParameterName)> SetColumns, dynamic Values) PrepareUpdateParameters(
        IDictionary<string, object?> data,
        IReadOnlyList<ColumnMetadata> columns)
    {
        var columnMap = columns.ToDictionary(c => c.LogicalName, c => c);
        var setColumns = new List<(string ColumnName, string ParameterName)>();
        dynamic values = new ExpandoObject();
        var valuesDict = (IDictionary<string, object?>)values;

        int paramIndex = 0;
        foreach (var (logicalName, value) in data)
        {
            if (!columnMap.TryGetValue(logicalName, out var column))
            {
                throw new ValidationException($"Column '{logicalName}' not found in table metadata");
            }

            // Skip primary key columns in SET clause
            if (column.IsPrimaryKey)
                continue;

            var paramName = $"@set{paramIndex}";
            var paramExpr = TypeMapper.IsJsonbType(column.DataType) ? $"{paramName}::jsonb" : paramName;
            setColumns.Add((column.PhysicalName, paramExpr));

            // Convert value to database type
            var dbValue = TypeMapper.ToDbValue(value, column.DataType);
            valuesDict[$"set{paramIndex}"] = dbValue;

            paramIndex++;
        }

        return (setColumns, values);
    }


    #endregion
}
