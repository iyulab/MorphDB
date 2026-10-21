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
using MorphDB.Npgsql.Security;
using Npgsql;

namespace MorphDB.Npgsql.Services;

/// <summary>
/// PostgreSQL implementation of IMorphDataService.
/// Handles CRUD operations with logical-to-physical name translation.
/// </summary>
public sealed class PostgresDataService : IMorphDataService
{
    private readonly MorphConnectionFactory _connections;
    private readonly PostgresUnitOfWorkFactory _units;
    private readonly IMetadataRepository _metadataRepository;
    private readonly IWritePipeline _writePipeline;
    private readonly ILookupResolver? _lookupResolver;
    private readonly IRollupResolver? _rollupResolver;
    private readonly IFormulaResolver? _formulaResolver;
    private readonly string _primaryKeyLogicalName;
    private readonly ISecurityContextAccessor? _securityContext;

    /// <summary>
    /// Creates a new PostgresDataService.
    /// </summary>
    public PostgresDataService(
        MorphConnectionFactory connections,
        IMetadataRepository metadataRepository,
        IWritePipeline writePipeline,
        ILookupResolver? lookupResolver = null,
        IRollupResolver? rollupResolver = null,
        IFormulaResolver? formulaResolver = null,
        string primaryKeyLogicalName = "id",
        ISecurityContextAccessor? securityContext = null)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _units = new PostgresUnitOfWorkFactory(connections);
        _metadataRepository = metadataRepository ?? throw new ArgumentNullException(nameof(metadataRepository));
        _writePipeline = writePipeline ?? throw new ArgumentNullException(nameof(writePipeline));
        _lookupResolver = lookupResolver;
        _rollupResolver = rollupResolver;
        _formulaResolver = formulaResolver;
        _primaryKeyLogicalName = primaryKeyLogicalName;
        _securityContext = securityContext;
    }

    /// <inheritdoc />
    public IMorphQueryBuilder Query(Guid projectId) => QueryBuilder(projectId);

    private MorphQueryBuilder QueryBuilder(Guid projectId)
    {
        return new MorphQueryBuilder(
            _connections,
            _metadataRepository,
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
        var table = await OpenTableAsync(projectId, tableName, cancellationToken);
        return await table.GetByIdAsync(id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ITableRecordReader> OpenTableAsync(
        Guid projectId,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableWithColumnsAsync(projectId, tableName, cancellationToken);
        return new TableRecordReader(this, projectId, tableName, table, GetPrimaryKeyColumn(table));
    }

    private sealed class TableRecordReader(PostgresDataService service, Guid projectId, string tableName, TableMetadata table, ColumnMetadata idColumn) : ITableRecordReader
    {
        public string TableName => tableName;

        // One record is a query narrowed to its id, not a statement of its own: a separate path is
        // how a single read came to skip row-level security and leave out every lookup, rollup and
        // formula value the same row carries when it is listed.
        public Task<IDictionary<string, object?>?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            service.QueryBuilder(projectId)
                .From(tableName, table)
                .SelectAll()
                .Where(idColumn.LogicalName, FilterOperator.Equals, id)
                .FirstOrDefaultAsync(cancellationToken);
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

        // Through the pipeline as one batch, atomic as one unit of work — or as part of the one the
        // caller is already in. The first failed row ends it, and the unit undoes the rows before it.
        await using var unit = _units.Begin();
        var results = await _writePipeline.InsertBatchAsync(projectId, table, records, null, cancellationToken);
        var rows = results.Select(result => UnwrapOrThrow(result, tableName)).ToList();

        await unit.CommitAsync(cancellationToken);
        return rows;
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

        // On a soft-delete table a bulk update writes the rows a list shows, not the marked ones.
        if (table.SoftDeleteEnabled)
        {
            whereSql = $"({whereSql}) AND {SystemColumns.DeletedAt} IS NULL";
        }

        var sql = DmlBuilder.BuildBatchUpdate(table.PhysicalName, setColumns, whereSql);

        await using var lease = await _connections.LeaseAsync(cancellationToken);

        var connection = lease.Connection;
        try
        {
            return await connection.ExecuteAsync(
                new CommandDefinition(sql, values, cancellationToken: cancellationToken));
        }
        catch (PostgresException pg) when (Pipeline.PostgresWriteRefusal.IsCallersMistake(pg))
        {
            // Rows a policy keeps from the caller are simply not matched; a policy refusal here is the
            // other case — the update would turn a row the caller may update into one a policy does
            // not admit. A physical constraint (a NULL into a required column, a duplicate, a key a
            // reference needs) is the caller's mistake as on a single write.
            throw Pipeline.PostgresWriteRefusal.Translate(pg, table);
        }
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

        var where = string.IsNullOrEmpty(whereSql) ? null : $"({whereSql})";
        string sql;
        var parameters = new DynamicParameters(whereParams);
        if (table.SoftDeleteEnabled)
        {
            // A soft-delete table marks rows instead of removing them -- the same meaning a delete by
            // id has (SoftDeleteApplier). Rows already marked are not marked again, so the count is
            // of rows this request deleted. _deleted_by is set as the single delete sets it: with
            // audit fields on and a known caller.
            var set = $"{SystemColumns.DeletedAt} = now()";
            var actor = _securityContext?.ContextOrNull?.UserId;
            var deletedBy = table.Columns.FirstOrDefault(c => c.LogicalName == SystemColumns.DeletedBy);
            if (table.AuditFieldsEnabled && actor is not null && deletedBy is not null)
            {
                set += $", {deletedBy.PhysicalName} = @__deletedBy";
                parameters.Add("__deletedBy", TypeMapper.ToDbValue(actor, deletedBy.DataType));
            }

            var notYet = $"{SystemColumns.DeletedAt} IS NULL";
            sql = $"UPDATE {table.PhysicalName} SET {set} WHERE {(where is null ? notYet : $"{where} AND {notYet}")}";
        }
        else
        {
            sql = where is null ? $"DELETE FROM {table.PhysicalName}" : $"DELETE FROM {table.PhysicalName} WHERE {where}";
        }

        await using var lease = await _connections.LeaseAsync(cancellationToken);

        var connection = lease.Connection;
        try
        {
            return await connection.ExecuteAsync(
                new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        }
        catch (PostgresException pg) when (Pipeline.PostgresWriteRefusal.IsCallersMistake(pg))
        {
            // One statement: a row still referenced refuses the whole delete, and nothing is deleted.
            throw Pipeline.PostgresWriteRefusal.Translate(pg, table);
        }
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
    public async Task<UpsertResult> UpsertAsync(
        Guid projectId,
        string tableName,
        IDictionary<string, object?> data,
        string[] keyColumns,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableWithColumnsAsync(projectId, tableName, cancellationToken);
        var result = await _writePipeline.UpsertAsync(projectId, table, data, keyColumns, null, cancellationToken);
        return new UpsertResult(UnwrapOrThrow(result, tableName), result.Inserted ?? false);
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
