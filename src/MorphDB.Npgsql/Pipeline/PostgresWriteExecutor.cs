using System.Dynamic;
using System.Globalization;
using Dapper;
using Microsoft.Extensions.Options;
using MorphDB.Core.Diagnostics;
using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;
using MorphDB.Npgsql.Diagnostics;
using MorphDB.Npgsql.Dml;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Pipeline.Transformers;
using MorphDB.Npgsql.Security;
using Npgsql;

namespace MorphDB.Npgsql.Pipeline;

/// <summary>
/// PostgreSQL implementation of IWriteExecutor.
/// Executes the actual database write after pipeline validation.
/// </summary>
public sealed class PostgresWriteExecutor : IWriteExecutor
{
    private readonly MorphConnectionFactory _connections;
    private readonly IQueryDiagnostics _queryDiagnostics;
    private readonly string _primaryKeyLogicalName;

    public PostgresWriteExecutor(
        MorphConnectionFactory connections,
        IQueryDiagnostics queryDiagnostics,
        string primaryKeyLogicalName = "id")
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _queryDiagnostics = queryDiagnostics ?? throw new ArgumentNullException(nameof(queryDiagnostics));
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

            await using var conn = await _connections.LeaseAsync(context.CancellationToken);
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
        catch (PostgresException pg) when (PostgresWriteRefusal.IsCallersMistake(pg))
        {
            // A physical constraint the app-layer validators did not intercept — or a row-level
            // security policy, which no validator reads — is still the caller's mistake, not our
            // defect: translate it before it can surface as a 500.
            var translated = PostgresWriteRefusal.Translate(pg, context.Table);
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

            // Map logical names to physical and prepare parameters
            var (columns, parameters, values) = PrepareInsertParameters(dataWithProject, table.Columns);

            var sql = DmlBuilder.BuildInsert(table.PhysicalName, columns, parameters);

            await using var conn = await _connections.LeaseAsync(context.CancellationToken);
            var result = await conn.Connection.QuerySingleAsync<dynamic>(
                new CommandDefinition(sql, values, transaction: conn.Transaction, cancellationToken: context.CancellationToken));

            var mapped = Infrastructure.RowMapper.MapToLogicalDictionary(result, table.Columns);

            scope.SetRowCount(1);
            return WriteResult.Ok(mapped);
        }
        catch (PostgresException pg) when (PostgresWriteRefusal.IsCallersMistake(pg))
        {
            // A physical constraint the app-layer validators did not intercept — or a row-level
            // security policy, which no validator reads — is still the caller's mistake, not our
            // defect: translate it before it can surface as a 500.
            var translated = PostgresWriteRefusal.Translate(pg, context.Table);
            scope.SetError(translated.Message);
            throw translated;
        }
        catch (Exception ex)
        {
            scope.SetError(ex.Message);
            throw;
        }
    }

    // PostgreSQL's limit on the parameters of one statement.
    private const int MaxParametersPerStatement = 65_535;

    public async Task<IReadOnlyList<WriteResult>> ExecuteInsertBatchAsync(IReadOnlyList<IWriteContext> contexts, int firstIndex = 0)
    {
        if (contexts.Count == 0)
            return [];

        var first = contexts[0];
        var table = first.Table;
        var cancellationToken = first.CancellationToken;
        using var scope = new QueryExecutionScope(
            _queryDiagnostics,
            first.ProjectId,
            table.LogicalName,
            QueryOperationType.Insert,
            "WritePipeline");

        var statementRows = new List<int>();
        var reading = 0;
        try
        {
            await using var conn = await _connections.LeaseAsync(cancellationToken);

            // Neighbouring rows that write the same columns go into one INSERT … VALUES (…), (…) RETURNING *;
            // all the statements go in one round trip. Row-level security checks and row triggers act on
            // every row as they do on a single insert.
            var rows = contexts.Select(c => InsertValues(EnsureProjectId(c.Data, c.ProjectId), table.Columns)).ToList();
            await using var batch = new NpgsqlBatch(conn.Connection, conn.Transaction);
            for (var start = 0; start < rows.Count;)
            {
                var shape = rows[start];
                var rowsPerStatement = Math.Max(1, MaxParametersPerStatement / shape.Count);
                var end = start + 1;
                while (end < rows.Count && end - start < rowsPerStatement && SameColumns(rows[end], shape))
                {
                    end++;
                }

                batch.BatchCommands.Add(InsertRowsCommand(table, rows, start, end));
                statementRows.Add(end - start);
                start = end;
            }

            var results = new List<WriteResult>(contexts.Count);
            await using (var reader = await batch.ExecuteReaderAsync(cancellationToken))
            {
                // A refused statement surfaces when the reader reaches its result: `reading` is that statement.
                foreach (var expected in statementRows)
                {
                    for (var i = 0; i < expected; i++)
                    {
                        if (!await reader.ReadAsync(cancellationToken))
                            throw new InvalidOperationException("An INSERT … RETURNING statement returned fewer rows than it inserted.");

                        var row = new Dictionary<string, object?>(reader.FieldCount);
                        for (var f = 0; f < reader.FieldCount; f++)
                        {
                            row[reader.GetName(f)] = reader.IsDBNull(f) ? null : reader.GetValue(f);
                        }

                        var mapped = Infrastructure.RowMapper.MapToLogicalDictionary(row, table.Columns);

                        // PostgreSQL returns the rows of INSERT … VALUES in the order of the list, which the
                        // results rely on; the id each row was given says so, or the batch stops here.
                        var sent = rows[results.Count].FirstOrDefault(v => v.Column.LogicalName == SystemColumns.Id).Value;
                        if (sent is not null && mapped.TryGetValue(SystemColumns.Id, out var returned)
                            && !string.Equals(
                                Convert.ToString(sent, CultureInfo.InvariantCulture),
                                Convert.ToString(returned, CultureInfo.InvariantCulture),
                                StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException("An INSERT … RETURNING statement returned its rows out of order.");
                        }

                        results.Add(WriteResult.Ok(mapped));
                    }

                    reading++;
                    await reader.NextResultAsync(cancellationToken);
                }
            }

            scope.SetRowCount(results.Count);
            return results;
        }
        catch (PostgresException pg) when (PostgresWriteRefusal.IsCallersMistake(pg))
        {
            // As on a single insert, and named by the rows the refused statement carried — one row, or the
            // range of a multi-row statement, whose refusal PostgreSQL does not pin to a row.
            var translated = PostgresWriteRefusal.Translate(pg, table, RowsOf(reading, statementRows, firstIndex));
            scope.SetError(translated.Message);
            throw translated;
        }
        catch (Exception ex)
        {
            scope.SetError(ex.Message);
            throw;
        }
    }

    private static string? RowsOf(int statement, List<int> statementRows, int firstIndex)
    {
        if (statement >= statementRows.Count)
            return null;

        var first = firstIndex + statementRows.Take(statement).Sum();
        var last = first + statementRows[statement] - 1;
        return first == last ? $"[{first}]" : $"[{first}]–[{last}]";
    }

    private static bool SameColumns(
        List<(ColumnMetadata Column, object? Value)> row,
        List<(ColumnMetadata Column, object? Value)> shape) =>
        row.Count == shape.Count && row.Select(v => v.Column).SequenceEqual(shape.Select(v => v.Column));

    private static NpgsqlBatchCommand InsertRowsCommand(
        TableMetadata table,
        List<List<(ColumnMetadata Column, object? Value)>> rows,
        int start,
        int end)
    {
        var command = new NpgsqlBatchCommand();
        var placeholders = new List<IReadOnlyList<string>>(end - start);
        for (var r = start; r < end; r++)
        {
            var row = new List<string>(rows[r].Count);
            foreach (var (column, value) in rows[r])
            {
                command.Parameters.Add(new NpgsqlParameter { Value = value ?? DBNull.Value });
                row.Add(Placeholder($"${command.Parameters.Count}", column));
            }

            placeholders.Add(row);
        }

        command.CommandText = DmlBuilder.BuildInsertRows(
            table.PhysicalName, [.. rows[start].Select(v => v.Column.PhysicalName)], placeholders);
        return command;
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
            // Handle version increment specially
            var (setColumns, values) = PrepareUpdateParameters(context.Data, table.Columns);
            ((IDictionary<string, object?>)values)["id"] = context.RecordId.Value;

            var whereClause = ActiveRowById(table, idColumn);
            var sql = DmlBuilder.BuildUpdate(table.PhysicalName, setColumns, whereClause);

            await using var conn = await _connections.LeaseAsync(context.CancellationToken);
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
        catch (PostgresException pg) when (PostgresWriteRefusal.IsCallersMistake(pg))
        {
            // A physical constraint the app-layer validators did not intercept — or a row-level
            // security policy, which no validator reads — is still the caller's mistake, not our
            // defect: translate it before it can surface as a 500.
            var translated = PostgresWriteRefusal.Translate(pg, context.Table);
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

            // Prepare insert parameters
            var (columns, parameters, values) = PrepareInsertParameters(dataWithProject, table.Columns);

            // On a soft-delete table uniqueness covers the unmarked rows only, so the conflict target
            // is that partial index: a value a marked row held is written as a new row.
            var sql = DmlBuilder.BuildUpsert(table.PhysicalName, columns, parameters, conflictColumns,
                table.SoftDeleteEnabled ? Ddl.DdlBuilder.ActiveRowsPredicate : null);

            await using var conn = await _connections.LeaseAsync(context.CancellationToken);
            var result = await conn.Connection.QuerySingleAsync<dynamic>(
                new CommandDefinition(sql, values, transaction: conn.Transaction, cancellationToken: context.CancellationToken));

            var row = (IDictionary<string, object?>)result;
            var inserted = row[DmlBuilder.UpsertInsertedColumn] is true;
            row.Remove(DmlBuilder.UpsertInsertedColumn);
            Dictionary<string, object?> mapped = Infrastructure.RowMapper.MapToLogicalDictionary(row, table.Columns);
            scope.SetRowCount(1);
            return WriteResult.Ok(mapped) with { Inserted = inserted };
        }
        catch (PostgresException pg) when (pg.SqlState == PostgresErrorCodes.InvalidColumnReference)
        {
            // ON CONFLICT names columns no unique constraint covers: the caller keyed the upsert on
            // columns that do not identify a row. Their mistake, not a server fault.
            var keys = context.KeyColumns is { Count: > 0 } named ? named : [GetPrimaryKeyColumn(table).LogicalName];
            var refusal = new MorphDB.Core.Exceptions.ValidationException(
                "keyColumns",
                $"no unique constraint covers {string.Join(", ", keys.Select(k => $"'{k}'"))}; an upsert matches rows on columns that are unique together");
            scope.SetError(refusal.Message);
            throw refusal;
        }
        catch (PostgresException pg) when (PostgresWriteRefusal.IsCallersMistake(pg))
        {
            // A physical constraint the app-layer validators did not intercept — or a row-level
            // security policy, which no validator reads — is still the caller's mistake, not our
            // defect: translate it before it can surface as a 500.
            var translated = PostgresWriteRefusal.Translate(pg, context.Table);
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

            var whereClause = ActiveRowById(table, idColumn);
            var sql = DmlBuilder.BuildUpdate(table.PhysicalName, setColumns, whereClause);

            await using var conn = await _connections.LeaseAsync(context.CancellationToken);
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
        catch (PostgresException pg) when (PostgresWriteRefusal.IsCallersMistake(pg))
        {
            // A physical constraint the app-layer validators did not intercept — or a row-level
            // security policy, which no validator reads — is still the caller's mistake, not our
            // defect: translate it before it can surface as a 500.
            var translated = PostgresWriteRefusal.Translate(pg, context.Table);
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
        var physicalColumns = new List<string>();
        var parameterNames = new List<string>();
        dynamic values = new ExpandoObject();
        var valuesDict = (IDictionary<string, object?>)values;

        var paramIndex = 0;
        foreach (var (column, dbValue) in InsertValues(data, columns))
        {
            physicalColumns.Add(column.PhysicalName);
            parameterNames.Add(Placeholder($"@p{paramIndex}", column));
            valuesDict[$"p{paramIndex}"] = dbValue;
            paramIndex++;
        }

        return (physicalColumns, parameterNames, values);
    }

    /// <summary>
    /// The columns an insert writes and the database value of each, in the data's order — what every
    /// insert statement, one row or many, is built from.
    /// </summary>
    private static List<(ColumnMetadata Column, object? Value)> InsertValues(
        IDictionary<string, object?> data,
        IReadOnlyList<ColumnMetadata> columns)
    {
        var columnMap = columns.ToDictionary(c => c.LogicalName, c => c);
        var values = new List<(ColumnMetadata, object?)>(data.Count);

        foreach (var (logicalName, value) in data)
        {
            // Skip unknown columns for resilience
            if (!columnMap.TryGetValue(logicalName, out var column))
                continue;

            // For insert, version starts at 1
            values.Add((column, value is VersionIncrement ? 1 : TypeMapper.ToDbValue(value, column.DataType)));
        }

        return values;
    }

    private static string Placeholder(string parameter, ColumnMetadata column) =>
        TypeMapper.IsJsonbType(column.DataType) ? $"{parameter}::jsonb" : parameter;

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


    /// <summary>
    /// The row an update or a soft delete by id writes: the one with that id -- and, on a table
    /// created with soft delete, only while no delete has marked it. A marked row is not there to be
    /// written, as it is not there to be read, so the write answers not found.
    /// </summary>
    private static string ActiveRowById(TableMetadata table, ColumnMetadata idColumn) =>
        table.SoftDeleteEnabled
            ? $"{DmlBuilder.BuildIdWhereClause(idColumn.PhysicalName)} AND {SystemColumns.DeletedAt} IS NULL"
            : DmlBuilder.BuildIdWhereClause(idColumn.PhysicalName);

    #endregion
}
