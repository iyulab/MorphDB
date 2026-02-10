using System.Dynamic;
using Dapper;
using Microsoft.Extensions.Options;
using MorphDB.Core.Diagnostics;
using MorphDB.Core.Encryption;
using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;
using MorphDB.Npgsql.Diagnostics;
using MorphDB.Npgsql.Dml;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Pipeline.Transformers;
using Npgsql;

namespace MorphDB.Npgsql.Pipeline;

/// <summary>
/// PostgreSQL implementation of IWriteExecutor.
/// Executes the actual database write after pipeline validation.
/// </summary>
public sealed class PostgresWriteExecutor : IWriteExecutor
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IQueryDiagnostics _queryDiagnostics;
    private readonly IDataEncryptionService? _encryptionService;
    private readonly DataEncryptionOptions _encryptionOptions;
    private readonly string _primaryKeyLogicalName;

    public PostgresWriteExecutor(
        NpgsqlDataSource dataSource,
        IQueryDiagnostics queryDiagnostics,
        IDataEncryptionService? encryptionService = null,
        IOptions<DataEncryptionOptions>? encryptionOptions = null,
        string primaryKeyLogicalName = "id")
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _queryDiagnostics = queryDiagnostics ?? throw new ArgumentNullException(nameof(queryDiagnostics));
        _encryptionService = encryptionService;
        _encryptionOptions = encryptionOptions?.Value ?? new DataEncryptionOptions();
        _primaryKeyLogicalName = primaryKeyLogicalName;
    }

    public async Task<WriteResult> ExecuteAsync(IWriteContext context)
    {
        return context.OperationType switch
        {
            WriteOperationType.Insert => await ExecuteInsertAsync(context),
            WriteOperationType.Update => await ExecuteUpdateAsync(context),
            WriteOperationType.Upsert => await ExecuteUpsertAsync(context),
            _ => throw new InvalidOperationException($"Unsupported operation type: {context.OperationType}")
        };
    }

    public async Task<WriteResult> ExecuteDeleteAsync(IWriteContext context)
    {
        var table = context.Table;

        // Check if this is a soft delete (SoftDeleteApplier sets _deleted_at)
        if (context.Data.ContainsKey("_deleted_at"))
        {
            // Perform soft delete as UPDATE
            return await ExecuteSoftDeleteAsync(context);
        }

        // Hard delete
        var idColumn = GetPrimaryKeyColumn(table);
        if (!context.RecordId.HasValue)
        {
            return WriteResult.Failed(new ValidationError
            {
                Field = "id",
                Code = ValidationErrorCodes.Required,
                Message = "Record ID is required for delete operation"
            });
        }

        using var scope = new QueryExecutionScope(
            _queryDiagnostics,
            context.TenantId,
            table.LogicalName,
            QueryOperationType.Delete,
            "WritePipeline");

        try
        {
            var whereClause = DmlBuilder.BuildIdWhereClause(idColumn.PhysicalName);
            var sql = DmlBuilder.BuildDelete(table.PhysicalName, whereClause);

            await using var conn = await GetScopedConnectionAsync(context.CancellationToken);
            var affectedRows = await conn.Connection.ExecuteAsync(
                new CommandDefinition(sql, new { id = context.RecordId.Value }, transaction: conn.Transaction, cancellationToken: context.CancellationToken));

            if (affectedRows == 0)
            {
                return WriteResult.Failed(new ValidationError
                {
                    Field = "id",
                    Code = ValidationErrorCodes.NotFound,
                    Message = $"Record with id '{context.RecordId}' not found"
                });
            }

            scope.SetRowCount(affectedRows);
            return WriteResult.Ok(new Dictionary<string, object?> { ["deleted"] = true, ["id"] = context.RecordId });
        }
        catch (Exception ex)
        {
            scope.SetError(ex.Message);
            throw;
        }
    }

    private async Task<WriteResult> ExecuteInsertAsync(IWriteContext context)
    {
        var table = context.Table;
        using var scope = new QueryExecutionScope(
            _queryDiagnostics,
            context.TenantId,
            table.LogicalName,
            QueryOperationType.Insert,
            "WritePipeline");

        try
        {
            // Ensure tenant_id is set
            var dataWithTenant = EnsureTenantId(context.Data, context.TenantId);

            // Encrypt data before storing
            var encryptedData = EncryptRowData(context.TenantId, table.LogicalName, dataWithTenant, table.Columns);

            // Map logical names to physical and prepare parameters
            var (columns, parameters, values) = PrepareInsertParameters(encryptedData, table.Columns);

            var sql = DmlBuilder.BuildInsert(table.PhysicalName, columns, parameters);

            await using var conn = await GetScopedConnectionAsync(context.CancellationToken);
            var result = await conn.Connection.QuerySingleAsync<dynamic>(
                new CommandDefinition(sql, values, transaction: conn.Transaction, cancellationToken: context.CancellationToken));

            var mapped = MapToLogicalDictionary(result, table.Columns);

            // Return decrypted data to the caller
            var decrypted = DecryptRowData(context.TenantId, table.LogicalName, mapped, table.Columns);
            scope.SetRowCount(1);
            return WriteResult.Ok(decrypted);
        }
        catch (Exception ex)
        {
            scope.SetError(ex.Message);
            throw;
        }
    }

    private async Task<WriteResult> ExecuteUpdateAsync(IWriteContext context)
    {
        var table = context.Table;
        var idColumn = GetPrimaryKeyColumn(table);

        if (!context.RecordId.HasValue)
        {
            return WriteResult.Failed(new ValidationError
            {
                Field = "id",
                Code = ValidationErrorCodes.Required,
                Message = "Record ID is required for update operation"
            });
        }

        using var scope = new QueryExecutionScope(
            _queryDiagnostics,
            context.TenantId,
            table.LogicalName,
            QueryOperationType.Update,
            "WritePipeline");

        try
        {
            // Encrypt data before storing
            var encryptedData = EncryptRowData(context.TenantId, table.LogicalName, context.Data, table.Columns);

            // Handle version increment specially
            var (setColumns, values) = PrepareUpdateParameters(encryptedData, table.Columns);
            ((IDictionary<string, object?>)values)["id"] = context.RecordId.Value;

            var whereClause = DmlBuilder.BuildIdWhereClause(idColumn.PhysicalName);
            var sql = DmlBuilder.BuildUpdate(table.PhysicalName, setColumns, whereClause);

            await using var conn = await GetScopedConnectionAsync(context.CancellationToken);
            var result = await conn.Connection.QuerySingleOrDefaultAsync<dynamic>(
                new CommandDefinition(sql, values, transaction: conn.Transaction, cancellationToken: context.CancellationToken));

            if (result is null)
            {
                return WriteResult.Failed(new ValidationError
                {
                    Field = "id",
                    Code = ValidationErrorCodes.NotFound,
                    Message = $"Record with id '{context.RecordId}' not found"
                });
            }

            var mapped = MapToLogicalDictionary(result, table.Columns);
            var decrypted = DecryptRowData(context.TenantId, table.LogicalName, mapped, table.Columns);
            scope.SetRowCount(1);
            return WriteResult.Ok(decrypted);
        }
        catch (Exception ex)
        {
            scope.SetError(ex.Message);
            throw;
        }
    }

    private async Task<WriteResult> ExecuteUpsertAsync(IWriteContext context)
    {
        // For upsert, we need key columns - use primary key
        var table = context.Table;
        var pkColumn = GetPrimaryKeyColumn(table);

        using var scope = new QueryExecutionScope(
            _queryDiagnostics,
            context.TenantId,
            table.LogicalName,
            QueryOperationType.Upsert,
            "WritePipeline");

        try
        {
            // Ensure tenant_id is set
            var dataWithTenant = EnsureTenantId(context.Data, context.TenantId);

            // Encrypt data before storing
            var encryptedData = EncryptRowData(context.TenantId, table.LogicalName, dataWithTenant, table.Columns);

            // Prepare insert parameters
            var (columns, parameters, values) = PrepareInsertParameters(encryptedData, table.Columns);

            var sql = DmlBuilder.BuildUpsert(table.PhysicalName, columns, parameters, [pkColumn.PhysicalName]);

            await using var conn = await GetScopedConnectionAsync(context.CancellationToken);
            var result = await conn.Connection.QuerySingleAsync<dynamic>(
                new CommandDefinition(sql, values, transaction: conn.Transaction, cancellationToken: context.CancellationToken));

            var mapped = MapToLogicalDictionary(result, table.Columns);
            var decrypted = DecryptRowData(context.TenantId, table.LogicalName, mapped, table.Columns);
            scope.SetRowCount(1);
            return WriteResult.Ok(decrypted);
        }
        catch (Exception ex)
        {
            scope.SetError(ex.Message);
            throw;
        }
    }

    private async Task<WriteResult> ExecuteSoftDeleteAsync(IWriteContext context)
    {
        var table = context.Table;
        var idColumn = GetPrimaryKeyColumn(table);

        if (!context.RecordId.HasValue)
        {
            return WriteResult.Failed(new ValidationError
            {
                Field = "id",
                Code = ValidationErrorCodes.Required,
                Message = "Record ID is required for delete operation"
            });
        }

        using var scope = new QueryExecutionScope(
            _queryDiagnostics,
            context.TenantId,
            table.LogicalName,
            QueryOperationType.Delete,
            "WritePipeline.SoftDelete");

        try
        {
            // Build update for soft delete columns
            var (setColumns, values) = PrepareUpdateParameters(context.Data, table.Columns);
            ((IDictionary<string, object?>)values)["id"] = context.RecordId.Value;

            var whereClause = DmlBuilder.BuildIdWhereClause(idColumn.PhysicalName);
            var sql = DmlBuilder.BuildUpdate(table.PhysicalName, setColumns, whereClause);

            await using var conn = await GetScopedConnectionAsync(context.CancellationToken);
            var result = await conn.Connection.QuerySingleOrDefaultAsync<dynamic>(
                new CommandDefinition(sql, values, transaction: conn.Transaction, cancellationToken: context.CancellationToken));

            if (result is null)
            {
                return WriteResult.Failed(new ValidationError
                {
                    Field = "id",
                    Code = ValidationErrorCodes.NotFound,
                    Message = $"Record with id '{context.RecordId}' not found"
                });
            }

            var mapped = MapToLogicalDictionary(result, table.Columns);
            scope.SetRowCount(1);
            return WriteResult.Ok(mapped);
        }
        catch (Exception ex)
        {
            scope.SetError(ex.Message);
            throw;
        }
    }

    #region Private Helpers

    /// <summary>
    /// Returns a scoped connection wrapper. If a ConnectionScope is active
    /// (e.g. inside a transaction), uses the shared connection/transaction.
    /// Otherwise opens a new connection from the data source.
    /// </summary>
    private async ValueTask<ScopedConnection> GetScopedConnectionAsync(CancellationToken cancellationToken)
    {
        if (ConnectionScope.HasScope)
        {
            return new ScopedConnection(ConnectionScope.CurrentConnection!, ConnectionScope.CurrentTransaction, ownsConnection: false);
        }

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        return new ScopedConnection(connection, null, ownsConnection: true);
    }

    private ColumnMetadata GetPrimaryKeyColumn(TableMetadata table)
    {
        var pkColumn = table.Columns.FirstOrDefault(c => c.IsPrimaryKey)
            ?? table.Columns.FirstOrDefault(c => c.LogicalName == _primaryKeyLogicalName);

        if (pkColumn is null)
            throw new InvalidOperationException($"Table '{table.LogicalName}' has no primary key column");

        return pkColumn;
    }

    private static IDictionary<string, object?> EnsureTenantId(IDictionary<string, object?> data, Guid tenantId)
    {
        const string TenantIdColumn = "tenant_id";

        if (data.TryGetValue(TenantIdColumn, out var existingValue))
        {
            if (existingValue is Guid existingGuid && existingGuid != tenantId)
            {
                throw new InvalidOperationException($"Provided tenant_id '{existingGuid}' does not match expected '{tenantId}'");
            }
            return data;
        }

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
            // Skip special marker values
            if (value is VersionIncrement)
            {
                // For insert, version starts at 1
                if (!columnMap.TryGetValue(logicalName, out var versionColumn))
                    continue;

                physicalColumns.Add(versionColumn.PhysicalName);
                var paramName = $"@p{paramIndex}";
                parameterNames.Add(paramName);
                valuesDict[$"p{paramIndex}"] = 1;
                paramIndex++;
                continue;
            }

            if (!columnMap.TryGetValue(logicalName, out var column))
            {
                // Skip unknown columns for resilience
                continue;
            }

            physicalColumns.Add(column.PhysicalName);
            var pName = $"@p{paramIndex}";
            parameterNames.Add(pName);

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
            // Handle version increment specially
            if (value is VersionIncrement)
            {
                if (!columnMap.TryGetValue(logicalName, out var versionColumn))
                    continue;

                // Use SQL expression for increment: _version = _version + 1
                // This requires special handling in SQL builder
                setColumns.Add((versionColumn.PhysicalName, $"{versionColumn.PhysicalName} + 1"));
                continue;
            }

            if (!columnMap.TryGetValue(logicalName, out var column))
            {
                // Skip unknown columns
                continue;
            }

            // Skip primary key columns in SET clause
            if (column.IsPrimaryKey)
                continue;

            var paramName = $"@p{paramIndex}";
            setColumns.Add((column.PhysicalName, paramName));

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
                var convertedValue = TypeMapper.FromDbValue(value, column.DataType);
                result[column.LogicalName] = convertedValue;
            }
            else
            {
                result[key] = value;
            }
        }

        return result;
    }

    private IDictionary<string, object?> EncryptRowData(
        Guid tenantId,
        string tableName,
        IDictionary<string, object?> data,
        IReadOnlyList<ColumnMetadata> columns)
    {
        if (_encryptionService is null || !_encryptionService.IsEnabled)
            return data;

        var encryptedColumnNames = GetEncryptedColumnNames(columns);

        if (encryptedColumnNames.Count == 0)
            return data;

        return _encryptionService.EncryptRow(tenantId, tableName, data, encryptedColumnNames);
    }

    private IDictionary<string, object?> DecryptRowData(
        Guid tenantId,
        string tableName,
        IDictionary<string, object?> data,
        IReadOnlyList<ColumnMetadata> columns)
    {
        if (_encryptionService is null || !_encryptionService.IsEnabled)
            return data;

        var encryptedColumnNames = GetEncryptedColumnNames(columns);

        if (encryptedColumnNames.Count == 0)
            return data;

        return _encryptionService.DecryptRow(tenantId, tableName, data, encryptedColumnNames);
    }

    private HashSet<string> GetEncryptedColumnNames(IReadOnlyList<ColumnMetadata> columns)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in columns)
        {
            if (_encryptionOptions.ExcludedColumns.Contains(column.LogicalName))
                continue;

            if (column.IsEncrypted)
            {
                result.Add(column.LogicalName);
                continue;
            }

            if (_encryptionOptions.EncryptAllByDefault && IsEncryptableDataType(column.DataType))
            {
                result.Add(column.LogicalName);
            }
        }

        return result;
    }

    private static bool IsEncryptableDataType(MorphDataType dataType)
    {
        return dataType switch
        {
            MorphDataType.Text => true,
            MorphDataType.LongText => true,
            MorphDataType.Email => true,
            MorphDataType.Phone => true,
            MorphDataType.Url => true,
            MorphDataType.Json => true,
            MorphDataType.Integer => true,
            MorphDataType.BigInteger => true,
            MorphDataType.Decimal => true,
            _ => false
        };
    }

    #endregion
}

