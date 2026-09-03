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
            context.ProjectId,
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
            return WriteResult.Ok(new Dictionary<string, object?> { ["deleted"] = true, [SystemColumns.Id] = context.RecordId });
        }
        catch (PostgresException pg) when (pg.SqlState is PostgresErrorCodes.NotNullViolation or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation)
        {
            // A physical constraint the app-layer validators did not intercept is still the
            // caller's mistake, not our defect — translate it before it can surface as a 500.
            var translated = TranslateConstraintViolation(pg, context.Table);
            scope.SetError(translated.Message);
            throw translated;
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
            context.ProjectId,
            table.LogicalName,
            QueryOperationType.Insert,
            "WritePipeline");

        try
        {
            // Ensure project_id is set
            var dataWithProject = EnsureProjectId(context.Data, context.ProjectId);

            // Encrypt data before storing
            var encryptedData = EncryptRowData(context.ProjectId, table.LogicalName, dataWithProject, table.Columns);

            // Map logical names to physical and prepare parameters
            var (columns, parameters, values) = PrepareInsertParameters(encryptedData, table.Columns);

            var sql = DmlBuilder.BuildInsert(table.PhysicalName, columns, parameters);

            await using var conn = await GetScopedConnectionAsync(context.CancellationToken);
            var result = await conn.Connection.QuerySingleAsync<dynamic>(
                new CommandDefinition(sql, values, transaction: conn.Transaction, cancellationToken: context.CancellationToken));

            var mapped = Infrastructure.RowMapper.MapToLogicalDictionary(result, table.Columns);

            // Return decrypted data to the caller
            var decrypted = DecryptRowData(context.ProjectId, table.LogicalName, mapped, table.Columns);
            scope.SetRowCount(1);
            return WriteResult.Ok(decrypted);
        }
        catch (PostgresException pg) when (pg.SqlState is PostgresErrorCodes.NotNullViolation or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation)
        {
            // A physical constraint the app-layer validators did not intercept is still the
            // caller's mistake, not our defect — translate it before it can surface as a 500.
            var translated = TranslateConstraintViolation(pg, context.Table);
            scope.SetError(translated.Message);
            throw translated;
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
            context.ProjectId,
            table.LogicalName,
            QueryOperationType.Update,
            "WritePipeline");

        try
        {
            // Encrypt data before storing
            var encryptedData = EncryptRowData(context.ProjectId, table.LogicalName, context.Data, table.Columns);

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

            var mapped = Infrastructure.RowMapper.MapToLogicalDictionary(result, table.Columns);
            var decrypted = DecryptRowData(context.ProjectId, table.LogicalName, mapped, table.Columns);
            scope.SetRowCount(1);
            return WriteResult.Ok(decrypted);
        }
        catch (PostgresException pg) when (pg.SqlState is PostgresErrorCodes.NotNullViolation or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation)
        {
            // A physical constraint the app-layer validators did not intercept is still the
            // caller's mistake, not our defect — translate it before it can surface as a 500.
            var translated = TranslateConstraintViolation(pg, context.Table);
            scope.SetError(translated.Message);
            throw translated;
        }
        catch (Exception ex)
        {
            scope.SetError(ex.Message);
            throw;
        }
    }

    private async Task<WriteResult> ExecuteUpsertAsync(IWriteContext context)
    {
        var table = context.Table;

        // Conflict resolution: the caller's key columns (logical → physical, validated), or the
        // primary key when none were named.
        List<string> conflictColumns;
        if (context.KeyColumns is { Count: > 0 })
        {
            var columnMap = table.Columns.ToDictionary(c => c.LogicalName, c => c);
            conflictColumns = new List<string>(context.KeyColumns.Count);
            foreach (var key in context.KeyColumns)
            {
                if (!columnMap.TryGetValue(key, out var keyColumn))
                {
                    return WriteResult.Failed(new ValidationError
                    {
                        Field = key,
                        Code = ValidationErrorCodes.UnknownColumn,
                        Message = $"Key column '{key}' not found in table '{table.LogicalName}'."
                    });
                }

                conflictColumns.Add(keyColumn.PhysicalName);
            }
        }
        else
        {
            conflictColumns = [GetPrimaryKeyColumn(table).PhysicalName];
        }

        using var scope = new QueryExecutionScope(
            _queryDiagnostics,
            context.ProjectId,
            table.LogicalName,
            QueryOperationType.Upsert,
            "WritePipeline");

        try
        {
            // Ensure project_id is set
            var dataWithProject = EnsureProjectId(context.Data, context.ProjectId);

            // Encrypt data before storing
            var encryptedData = EncryptRowData(context.ProjectId, table.LogicalName, dataWithProject, table.Columns);

            // Prepare insert parameters
            var (columns, parameters, values) = PrepareInsertParameters(encryptedData, table.Columns);

            var sql = DmlBuilder.BuildUpsert(table.PhysicalName, columns, parameters, conflictColumns);

            await using var conn = await GetScopedConnectionAsync(context.CancellationToken);
            var result = await conn.Connection.QuerySingleAsync<dynamic>(
                new CommandDefinition(sql, values, transaction: conn.Transaction, cancellationToken: context.CancellationToken));

            var mapped = Infrastructure.RowMapper.MapToLogicalDictionary(result, table.Columns);
            var decrypted = DecryptRowData(context.ProjectId, table.LogicalName, mapped, table.Columns);
            scope.SetRowCount(1);
            return WriteResult.Ok(decrypted);
        }
        catch (PostgresException pg) when (pg.SqlState is PostgresErrorCodes.NotNullViolation or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation)
        {
            // A physical constraint the app-layer validators did not intercept is still the
            // caller's mistake, not our defect — translate it before it can surface as a 500.
            var translated = TranslateConstraintViolation(pg, context.Table);
            scope.SetError(translated.Message);
            throw translated;
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
            context.ProjectId,
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

            var mapped = Infrastructure.RowMapper.MapToLogicalDictionary(result, table.Columns);
            scope.SetRowCount(1);
            return WriteResult.Ok(mapped);
        }
        catch (PostgresException pg) when (pg.SqlState is PostgresErrorCodes.NotNullViolation or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation)
        {
            // A physical constraint the app-layer validators did not intercept is still the
            // caller's mistake, not our defect — translate it before it can surface as a 500.
            var translated = TranslateConstraintViolation(pg, context.Table);
            scope.SetError(translated.Message);
            throw translated;
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

    private static IDictionary<string, object?> EnsureProjectId(IDictionary<string, object?> data, Guid projectId)
    {
        const string ProjectIdColumn = "project_id";

        if (data.TryGetValue(ProjectIdColumn, out var existingValue))
        {
            if (existingValue is Guid existingGuid && existingGuid != projectId)
            {
                throw new InvalidOperationException($"Provided project_id '{existingGuid}' does not match expected '{projectId}'");
            }
            return data;
        }

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
            parameterNames.Add(TypeMapper.IsJsonbType(column.DataType) ? $"{pName}::jsonb" : pName);

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
            var paramExpr = TypeMapper.IsJsonbType(column.DataType) ? $"{paramName}::jsonb" : paramName;
            setColumns.Add((column.PhysicalName, paramExpr));

            var dbValue = TypeMapper.ToDbValue(value, column.DataType);
            valuesDict[$"p{paramIndex}"] = dbValue;

            paramIndex++;
        }

        return (setColumns, values);
    }


    private IDictionary<string, object?> EncryptRowData(
        Guid projectId,
        string tableName,
        IDictionary<string, object?> data,
        IReadOnlyList<ColumnMetadata> columns)
    {
        if (_encryptionService is null || !_encryptionService.IsEnabled)
            return data;

        var encryptedColumnNames = GetEncryptedColumnNames(columns);

        if (encryptedColumnNames.Count == 0)
            return data;

        return _encryptionService.EncryptRow(projectId, tableName, data, encryptedColumnNames);
    }

    private IDictionary<string, object?> DecryptRowData(
        Guid projectId,
        string tableName,
        IDictionary<string, object?> data,
        IReadOnlyList<ColumnMetadata> columns)
    {
        if (_encryptionService is null || !_encryptionService.IsEnabled)
            return data;

        var encryptedColumnNames = GetEncryptedColumnNames(columns);

        if (encryptedColumnNames.Count == 0)
            return data;

        return _encryptionService.DecryptRow(projectId, tableName, data, encryptedColumnNames);
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

    /// <summary>
    /// Turns a physical NOT NULL / UNIQUE violation into the same kind of error the app-layer
    /// validators produce, naming the logical column — never the physical one — because internal
    /// names are not contract (hidden-layer principle).
    /// </summary>
    private static MorphDB.Core.Exceptions.ValidationException TranslateConstraintViolation(
        PostgresException pg, TableMetadata table)
    {
        if (pg.SqlState == PostgresErrorCodes.NotNullViolation)
        {
            var logical = table.Columns.FirstOrDefault(c => c.PhysicalName == pg.ColumnName)?.LogicalName;
            return logical is null
                ? new MorphDB.Core.Exceptions.ValidationException("A required column does not allow null values.")
                : new MorphDB.Core.Exceptions.ValidationException(logical, "a value is required and cannot be null");
        }

        if (pg.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            // Reached when the app-layer foreign-key validator did not answer first -- the physical
            // constraint is the backstop, and a backstop that surfaces as an opaque 500 tells the
            // caller their own mistake is our defect.
            return new MorphDB.Core.Exceptions.ValidationException(
                "A value in this request references a row that does not exist.");
        }

        return new MorphDB.Core.Exceptions.ValidationException(
            "A value in this request conflicts with an existing row: it violates a unique constraint.");
    }

    #endregion
}
