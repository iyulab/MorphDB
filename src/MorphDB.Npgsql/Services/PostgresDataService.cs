using System.Dynamic;
using Dapper;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
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
    private readonly string _primaryKeyLogicalName;

    /// <summary>
    /// Creates a new PostgresDataService.
    /// </summary>
    /// <param name="dataSource">NpgsqlDataSource for database connections.</param>
    /// <param name="metadataRepository">Metadata repository for schema lookups.</param>
    /// <param name="primaryKeyLogicalName">Logical name of the primary key column (default: "id").</param>
    public PostgresDataService(
        NpgsqlDataSource dataSource,
        IMetadataRepository metadataRepository,
        string primaryKeyLogicalName = "id")
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _metadataRepository = metadataRepository ?? throw new ArgumentNullException(nameof(metadataRepository));
        _primaryKeyLogicalName = primaryKeyLogicalName;
    }

    /// <inheritdoc />
    public IMorphQueryBuilder Query(Guid tenantId)
    {
        return new MorphQueryBuilder(_dataSource, _metadataRepository, tenantId);
    }

    /// <inheritdoc />
    public async Task<IDictionary<string, object?>?> GetByIdAsync(
        Guid tenantId,
        string tableName,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableWithColumnsAsync(tenantId, tableName, cancellationToken);
        var idColumn = GetPrimaryKeyColumn(table);

        var sql = DmlBuilder.BuildSelectById(table.PhysicalName, idColumn.PhysicalName);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await connection.QuerySingleOrDefaultAsync<dynamic>(
            new CommandDefinition(sql, new { id }, cancellationToken: cancellationToken));

        if (result is null)
            return null;

        return MapToLogicalDictionary(result, table.Columns);
    }

    /// <inheritdoc />
    public async Task<IDictionary<string, object?>> InsertAsync(
        Guid tenantId,
        string tableName,
        IDictionary<string, object?> data,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableWithColumnsAsync(tenantId, tableName, cancellationToken);

        // Ensure tenant_id is set
        var dataWithTenant = EnsureTenantId(data, tenantId);

        // Map logical names to physical and prepare parameters
        var (columns, parameters, values) = PrepareInsertParameters(dataWithTenant, table.Columns);

        var sql = DmlBuilder.BuildInsert(table.PhysicalName, columns, parameters);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await connection.QuerySingleAsync<dynamic>(
            new CommandDefinition(sql, values, cancellationToken: cancellationToken));

        return MapToLogicalDictionary(result, table.Columns);
    }

    /// <inheritdoc />
    public async Task<IDictionary<string, object?>> UpdateAsync(
        Guid tenantId,
        string tableName,
        Guid id,
        IDictionary<string, object?> data,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableWithColumnsAsync(tenantId, tableName, cancellationToken);
        var idColumn = GetPrimaryKeyColumn(table);

        // Map logical names to physical and prepare parameters
        var (setColumns, values) = PrepareUpdateParameters(data, table.Columns);
        values.id = id;

        var whereClause = DmlBuilder.BuildIdWhereClause(idColumn.PhysicalName);
        var sql = DmlBuilder.BuildUpdate(table.PhysicalName, setColumns, whereClause);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await connection.QuerySingleOrDefaultAsync<dynamic>(
            new CommandDefinition(sql, values, cancellationToken: cancellationToken));

        if (result is null)
            throw new NotFoundException($"Record with id '{id}' not found in table '{tableName}'");

        return MapToLogicalDictionary(result, table.Columns);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        Guid tenantId,
        string tableName,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableWithColumnsAsync(tenantId, tableName, cancellationToken);
        var idColumn = GetPrimaryKeyColumn(table);

        var whereClause = DmlBuilder.BuildIdWhereClause(idColumn.PhysicalName);
        var sql = DmlBuilder.BuildDelete(table.PhysicalName, whereClause);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var affectedRows = await connection.ExecuteAsync(
            new CommandDefinition(sql, new { id }, cancellationToken: cancellationToken));

        return affectedRows > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IDictionary<string, object?>>> InsertBatchAsync(
        Guid tenantId,
        string tableName,
        IReadOnlyList<IDictionary<string, object?>> records,
        CancellationToken cancellationToken = default)
    {
        if (records.Count == 0)
            return Array.Empty<IDictionary<string, object?>>();

        var table = await GetTableWithColumnsAsync(tenantId, tableName, cancellationToken);

        // For batch insert, use individual inserts in a transaction for simplicity
        // This could be optimized with COPY or multi-row VALUES in future
        var results = new List<IDictionary<string, object?>>(records.Count);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var record in records)
            {
                // Ensure tenant_id is set for each record
                var recordWithTenant = EnsureTenantId(record, tenantId);
                var (columns, parameters, values) = PrepareInsertParameters(recordWithTenant, table.Columns);
                var sql = DmlBuilder.BuildInsert(table.PhysicalName, columns, parameters);

                var result = await connection.QuerySingleAsync<dynamic>(
                    new CommandDefinition(sql, values, transaction: transaction, cancellationToken: cancellationToken));

                results.Add(MapToLogicalDictionary(result, table.Columns));
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
        Guid tenantId,
        string tableName,
        IDictionary<string, object?> data,
        IMorphQuery whereClause,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableWithColumnsAsync(tenantId, tableName, cancellationToken);

        // Map logical names to physical and prepare parameters
        var (setColumns, values) = PrepareUpdateParameters(data, table.Columns);

        // Get WHERE clause SQL from the query
        var whereSql = ExtractWhereClause(whereClause);
        var whereParams = whereClause.GetParameters();

        // Merge parameters
        var valuesDict = (IDictionary<string, object?>)values;
        foreach (var (key, value) in whereParams)
        {
            valuesDict[key] = value;
        }

        var sql = DmlBuilder.BuildBatchUpdate(table.PhysicalName, setColumns, whereSql);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var affectedRows = await connection.ExecuteAsync(
            new CommandDefinition(sql, values, cancellationToken: cancellationToken));

        return affectedRows;
    }

    /// <inheritdoc />
    public async Task<int> DeleteBatchAsync(
        Guid tenantId,
        string tableName,
        IMorphQuery whereClause,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableWithColumnsAsync(tenantId, tableName, cancellationToken);

        // Get WHERE clause SQL from the query
        var whereSql = ExtractWhereClause(whereClause);
        var whereParams = whereClause.GetParameters();

        var sql = $"DELETE FROM {table.PhysicalName} {whereSql}";

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

        return fullSql[whereIndex..endIndex].Trim();
    }

    /// <inheritdoc />
    public async Task<IDictionary<string, object?>> UpsertAsync(
        Guid tenantId,
        string tableName,
        IDictionary<string, object?> data,
        string[] keyColumns,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableWithColumnsAsync(tenantId, tableName, cancellationToken);

        // Validate key columns exist
        var columnMap = table.Columns.ToDictionary(c => c.LogicalName, c => c);
        foreach (var key in keyColumns)
        {
            if (!columnMap.ContainsKey(key))
                throw new ValidationException($"Key column '{key}' not found in table '{tableName}'");
        }

        // Map logical key columns to physical
        var physicalKeyColumns = keyColumns.Select(k => columnMap[k].PhysicalName).ToList();

        // Ensure tenant_id is set
        var dataWithTenant = EnsureTenantId(data, tenantId);

        // Prepare insert parameters
        var (columns, parameters, values) = PrepareInsertParameters(dataWithTenant, table.Columns);

        var sql = DmlBuilder.BuildUpsert(table.PhysicalName, columns, parameters, physicalKeyColumns);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await connection.QuerySingleAsync<dynamic>(
            new CommandDefinition(sql, values, cancellationToken: cancellationToken));

        return MapToLogicalDictionary(result, table.Columns);
    }

    #region Private Helper Methods

    private async Task<TableMetadata> GetTableWithColumnsAsync(
        Guid tenantId,
        string tableName,
        CancellationToken cancellationToken)
    {
        var table = await _metadataRepository.GetTableByNameAsync(tenantId, tableName, includeColumns: true, cancellationToken);

        if (table is null)
            throw new NotFoundException($"Table '{tableName}' not found for tenant '{tenantId}'");

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
    /// Ensures the tenant_id is included in the data dictionary.
    /// If not present, adds it; if present, verifies it matches.
    /// </summary>
    private static IDictionary<string, object?> EnsureTenantId(IDictionary<string, object?> data, Guid tenantId)
    {
        const string TenantIdColumn = "tenant_id";

        if (data.TryGetValue(TenantIdColumn, out var existingValue))
        {
            // If tenant_id is already in data, verify it matches
            if (existingValue is Guid existingGuid && existingGuid != tenantId)
            {
                throw new ValidationException($"Provided tenant_id '{existingGuid}' does not match the expected tenant_id '{tenantId}'");
            }
            return data;
        }

        // Create a new dictionary with tenant_id added
        var result = new Dictionary<string, object?>(data)
        {
            [TenantIdColumn] = tenantId
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
            parameterNames.Add(paramName);

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

            var paramName = $"@p{paramIndex}";
            setColumns.Add((column.PhysicalName, paramName));

            // Convert value to database type
            var dbValue = TypeMapper.ToDbValue(value, column.DataType);
            valuesDict[$"p{paramIndex}"] = dbValue;

            paramIndex++;
        }

        return (setColumns, values);
    }

    private static Dictionary<string, object?> MapToLogicalDictionary(
        dynamic row,
        IReadOnlyList<ColumnMetadata> columns)
    {
        var physicalToLogical = columns.ToDictionary(c => c.PhysicalName.ToLowerInvariant(), c => c);
        var result = new Dictionary<string, object?>();

        var rowDict = (IDictionary<string, object?>)row;

        foreach (var (key, value) in rowDict)
        {
            var normalizedKey = key.ToLowerInvariant();
            if (physicalToLogical.TryGetValue(normalizedKey, out var column))
            {
                // Convert from database type to .NET type
                var convertedValue = TypeMapper.FromDbValue(value, column.DataType);
                result[column.LogicalName] = convertedValue;
            }
            else
            {
                // Unknown column, keep as-is
                result[key] = value;
            }
        }

        return result;
    }

    #endregion
}
