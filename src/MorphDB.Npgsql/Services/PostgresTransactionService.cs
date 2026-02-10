using System.Text.Json;
using Dapper;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
using Npgsql;

namespace MorphDB.Npgsql.Services;

/// <summary>
/// PostgreSQL implementation of ITransactionService.
/// Handles cross-entity transactional operations with $ref resolution.
/// </summary>
public sealed class PostgresTransactionService : ITransactionService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IMetadataRepository _metadataRepository;
    private readonly IWritePipeline _writePipeline;
    private readonly int _defaultTimeoutMs = 30000;

    public PostgresTransactionService(
        NpgsqlDataSource dataSource,
        IMetadataRepository metadataRepository,
        IWritePipeline writePipeline)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _metadataRepository = metadataRepository ?? throw new ArgumentNullException(nameof(metadataRepository));
        _writePipeline = writePipeline ?? throw new ArgumentNullException(nameof(writePipeline));
    }

    /// <inheritdoc />
    public async Task<TransactionResult> ExecuteAsync(
        Guid tenantId,
        TransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Operations.Count == 0)
        {
            return TransactionResult.Failed("Transaction must contain at least one operation");
        }

        var timeout = request.TimeoutMs ?? _defaultTimeoutMs;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        var refResolver = new RefResolver();
        var results = new List<TransactionOperationResult>(request.Operations.Count);

        await using var connection = await _dataSource.OpenConnectionAsync(cts.Token);
        await using var transaction = await connection.BeginTransactionAsync(cts.Token);

        try
        {
            using var scope = ConnectionScope.Begin(connection, transaction);
            for (var i = 0; i < request.Operations.Count; i++)
            {
                var operation = request.Operations[i];
                var result = await ExecuteOperationAsync(
                    connection,
                    transaction,
                    tenantId,
                    operation,
                    refResolver,
                    i,
                    request.ReturnFullRecords,
                    cts.Token);

                if (!result.Success)
                {
                    await transaction.RollbackAsync(cts.Token);
                    return TransactionResult.PartialFailed(results, result.Error ?? "Operation failed", i);
                }

                // Store result for $ref resolution
                if (!string.IsNullOrEmpty(operation.Ref))
                {
                    refResolver.Store(operation.Ref, result);
                }

                results.Add(result);
            }

            await transaction.CommitAsync(cts.Token);
            return TransactionResult.Ok(results);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return TransactionResult.Failed($"Transaction timed out after {timeout}ms");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return TransactionResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<FinalizeResult> FinalizeAsync(
        Guid tenantId,
        string tableName,
        Guid recordId,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableWithColumnsAsync(tenantId, tableName, cancellationToken);

        if (!table.RowStateEnabled)
        {
            return FinalizeResult.Invalid(recordId, [
                new RowValidationError
                {
                    Column = "_row_state",
                    Error = "row_state_not_enabled",
                    Message = $"Row state is not enabled for table '{tableName}'"
                }
            ]);
        }

        // Get the existing record
        var record = await GetRecordByIdAsync(table, recordId, cancellationToken);
        if (record is null)
        {
            return FinalizeResult.NotFound(recordId);
        }

        // Check current row state
        var currentState = GetRowState(record);
        if (currentState != RowStateValue.Draft)
        {
            // Already finalized, return current state
            return currentState == RowStateValue.Valid
                ? FinalizeResult.Valid(recordId, record)
                : FinalizeResult.Invalid(recordId, GetRowErrors(record), record);
        }

        // Validate the record
        var validationOptions = new WriteOptions
        {
            ValidateRequired = true,
            ValidateForeignKeys = true,
            ValidateUnique = true,
            ValidateCheck = true,
            ApplyDefaults = false,
            ApplyTimestamps = false,
            ApplyVersion = false,
            ApplyAuditFields = false
        };

        var validationResult = await _writePipeline.ValidateAsync(
            tenantId, table, record, WriteOperationType.Update, validationOptions, cancellationToken);

        // Update row state based on validation
        var newState = validationResult.Success ? RowStateValue.Valid : RowStateValue.Error;
        var errors = validationResult.Errors
            .Select(e => new RowValidationError
            {
                Column = e.Field,
                Error = e.Code,
                Message = e.Message,
                AttemptedValue = e.AttemptedValue
            })
            .ToList();

        // Update the record's row state
        var updatedData = await UpdateRowStateAsync(table, recordId, newState, errors, cancellationToken);

        return validationResult.Success
            ? FinalizeResult.Valid(recordId, updatedData)
            : FinalizeResult.Invalid(recordId, errors, updatedData);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FinalizeResult>> FinalizeBatchAsync(
        Guid tenantId,
        string tableName,
        IReadOnlyList<Guid> recordIds,
        CancellationToken cancellationToken = default)
    {
        var results = new List<FinalizeResult>(recordIds.Count);

        foreach (var recordId in recordIds)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var result = await FinalizeAsync(tenantId, tableName, recordId, cancellationToken);
            results.Add(result);
        }

        return results;
    }

    #region Private Methods

    private async Task<TransactionOperationResult> ExecuteOperationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        TransactionOperation operation,
        RefResolver refResolver,
        int index,
        bool returnFullRecords,
        CancellationToken cancellationToken)
    {
        try
        {
            var table = await GetTableWithColumnsAsync(tenantId, operation.Table, cancellationToken);

            return operation.Method.ToUpperInvariant() switch
            {
                "INSERT" => await ExecuteInsertAsync(connection, transaction, tenantId, table, operation, refResolver, index, returnFullRecords, cancellationToken),
                "UPDATE" => await ExecuteUpdateAsync(connection, transaction, tenantId, table, operation, refResolver, index, returnFullRecords, cancellationToken),
                "DELETE" => await ExecuteDeleteAsync(connection, transaction, tenantId, table, operation, refResolver, index, cancellationToken),
                "UPSERT" => await ExecuteUpsertAsync(connection, transaction, tenantId, table, operation, refResolver, index, returnFullRecords, cancellationToken),
                _ => TransactionOperationResult(index, operation.Ref, false, error: $"Unknown method: {operation.Method}")
            };
        }
        catch (NotFoundException ex)
        {
            return TransactionOperationResult(index, operation.Ref, false, error: ex.Message);
        }
        catch (ValidationException ex)
        {
            return TransactionOperationResult(index, operation.Ref, false, error: ex.Message);
        }
        catch (Exception ex)
        {
            return TransactionOperationResult(index, operation.Ref, false, error: ex.Message);
        }
    }

    private async Task<TransactionOperationResult> ExecuteInsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        TableMetadata table,
        TransactionOperation operation,
        RefResolver refResolver,
        int index,
        bool returnFullRecords,
        CancellationToken cancellationToken)
    {
        if (operation.Data is null)
        {
            return TransactionOperationResult(index, operation.Ref, false, error: "INSERT requires data");
        }

        // Resolve $ref values in data
        var resolvedData = refResolver.ResolveData(operation.Data);

        // Apply tenant_id
        resolvedData["tenant_id"] = tenantId;

        // Generate ID if not provided
        if (!resolvedData.ContainsKey("_id") && !resolvedData.ContainsKey("id"))
        {
            resolvedData["_id"] = Guid.CreateVersion7();
        }

        var options = operation.Options ?? WriteOptions.Default;
        var result = await _writePipeline.InsertAsync(tenantId, table, resolvedData, options, cancellationToken);

        if (!result.Success)
        {
            return TransactionOperationResult(index, operation.Ref, false,
                validationErrors: result.Errors,
                error: "Validation failed");
        }

        var recordId = GetRecordId(result.Data);
        return TransactionOperationResult(index, operation.Ref, true,
            id: recordId,
            data: returnFullRecords ? result.Data : null,
            affectedRows: 1);
    }

    private async Task<TransactionOperationResult> ExecuteUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        TableMetadata table,
        TransactionOperation operation,
        RefResolver refResolver,
        int index,
        bool returnFullRecords,
        CancellationToken cancellationToken)
    {
        if (operation.Data is null)
        {
            return TransactionOperationResult(index, operation.Ref, false, error: "UPDATE requires data");
        }

        // Resolve the record ID (could be a $ref)
        var recordId = refResolver.ResolveId(operation.Id);
        if (!recordId.HasValue)
        {
            return TransactionOperationResult(index, operation.Ref, false, error: "UPDATE requires a valid record ID");
        }

        // Resolve $ref values in data
        var resolvedData = refResolver.ResolveData(operation.Data);

        var options = operation.Options ?? WriteOptions.Default;
        var result = await _writePipeline.UpdateAsync(tenantId, table, recordId.Value, resolvedData, null, options, cancellationToken);

        if (!result.Success)
        {
            return TransactionOperationResult(index, operation.Ref, false,
                validationErrors: result.Errors,
                error: "Validation failed");
        }

        return TransactionOperationResult(index, operation.Ref, true,
            id: recordId.Value,
            data: returnFullRecords ? result.Data : null,
            affectedRows: 1);
    }

    private async Task<TransactionOperationResult> ExecuteDeleteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        TableMetadata table,
        TransactionOperation operation,
        RefResolver refResolver,
        int index,
        CancellationToken cancellationToken)
    {
        // Resolve the record ID (could be a $ref)
        var recordId = refResolver.ResolveId(operation.Id);
        if (!recordId.HasValue)
        {
            return TransactionOperationResult(index, operation.Ref, false, error: "DELETE requires a valid record ID");
        }

        var options = operation.Options ?? WriteOptions.Default;
        var result = await _writePipeline.DeleteAsync(tenantId, table, recordId.Value, null, options, cancellationToken);

        if (!result.Success)
        {
            return TransactionOperationResult(index, operation.Ref, false,
                validationErrors: result.Errors,
                error: "Delete failed");
        }

        return TransactionOperationResult(index, operation.Ref, true,
            id: recordId.Value,
            affectedRows: 1);
    }

    private async Task<TransactionOperationResult> ExecuteUpsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        TableMetadata table,
        TransactionOperation operation,
        RefResolver refResolver,
        int index,
        bool returnFullRecords,
        CancellationToken cancellationToken)
    {
        if (operation.Data is null)
        {
            return TransactionOperationResult(index, operation.Ref, false, error: "UPSERT requires data");
        }

        if (operation.KeyColumns is null || operation.KeyColumns.Count == 0)
        {
            return TransactionOperationResult(index, operation.Ref, false, error: "UPSERT requires keyColumns");
        }

        // Resolve $ref values in data
        var resolvedData = refResolver.ResolveData(operation.Data);

        // Apply tenant_id
        resolvedData["tenant_id"] = tenantId;

        // Generate ID if not provided
        if (!resolvedData.ContainsKey("_id") && !resolvedData.ContainsKey("id"))
        {
            resolvedData["_id"] = Guid.CreateVersion7();
        }

        // For UPSERT, we need to build a custom SQL with ON CONFLICT
        var columnMap = table.Columns.ToDictionary(c => c.LogicalName, c => c);
        var keyColumnPhysical = operation.KeyColumns
            .Where(k => columnMap.ContainsKey(k))
            .Select(k => columnMap[k].PhysicalName)
            .ToList();

        if (keyColumnPhysical.Count != operation.KeyColumns.Count)
        {
            return TransactionOperationResult(index, operation.Ref, false,
                error: "One or more key columns not found in table");
        }

        // Use write pipeline for upsert (it handles UPSERT operation type)
        var writeContext = new WriteContext
        {
            TenantId = tenantId,
            Table = table,
            OperationType = WriteOperationType.Upsert,
            Data = new Dictionary<string, object?>(resolvedData),
            OriginalData = new Dictionary<string, object?>(resolvedData),
            Options = operation.Options ?? WriteOptions.Default,
            CancellationToken = cancellationToken
        };

        // Execute as insert with conflict handling
        var options = operation.Options ?? WriteOptions.Default;
        var result = await _writePipeline.InsertAsync(tenantId, table, resolvedData, options, cancellationToken);

        if (!result.Success)
        {
            return TransactionOperationResult(index, operation.Ref, false,
                validationErrors: result.Errors,
                error: "Validation failed");
        }

        var recordId = GetRecordId(result.Data);
        return TransactionOperationResult(index, operation.Ref, true,
            id: recordId,
            data: returnFullRecords ? result.Data : null,
            affectedRows: 1);
    }

    private static TransactionOperationResult TransactionOperationResult(
        int index,
        string? refName,
        bool success,
        Guid? id = null,
        IDictionary<string, object?>? data = null,
        int affectedRows = 0,
        string? error = null,
        IReadOnlyList<Core.Models.ValidationError>? validationErrors = null)
    {
        return new TransactionOperationResult
        {
            Index = index,
            Ref = refName,
            Success = success,
            Id = id,
            Data = data,
            AffectedRows = affectedRows,
            Error = error,
            ValidationErrors = validationErrors
        };
    }

    private static Guid? GetRecordId(IDictionary<string, object?>? data)
    {
        if (data is null)
            return null;

        if (data.TryGetValue("_id", out var id))
        {
            return id switch
            {
                Guid g => g,
                string s when Guid.TryParse(s, out var parsed) => parsed,
                _ => null
            };
        }

        if (data.TryGetValue("id", out var legacyId))
        {
            return legacyId switch
            {
                Guid g => g,
                string s when Guid.TryParse(s, out var parsed) => parsed,
                _ => null
            };
        }

        return null;
    }

    private async Task<TableMetadata> GetTableWithColumnsAsync(
        Guid tenantId,
        string tableName,
        CancellationToken cancellationToken)
    {
        var table = await _metadataRepository.GetTableByNameAsync(
            tenantId, tableName, includeColumns: true, cancellationToken);

        if (table is null)
            throw new NotFoundException($"Table '{tableName}' not found");

        if (table.Columns.Count == 0)
            throw new InvalidOperationException($"Table '{tableName}' has no columns");

        return table;
    }

    private async Task<IDictionary<string, object?>?> GetRecordByIdAsync(
        TableMetadata table,
        Guid recordId,
        CancellationToken cancellationToken)
    {
        var idColumn = table.Columns.FirstOrDefault(c => c.IsPrimaryKey)
            ?? table.Columns.FirstOrDefault(c => c.LogicalName == "_id" || c.LogicalName == "id");

        if (idColumn is null)
            return null;

        var sql = $"SELECT * FROM {table.PhysicalName} WHERE {idColumn.PhysicalName} = @id";

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await connection.QuerySingleOrDefaultAsync<dynamic>(
            new CommandDefinition(sql, new { id = recordId }, cancellationToken: cancellationToken));

        if (result is null)
            return null;

        return MapToLogicalDictionary(result, table.Columns);
    }

    private static RowStateValue GetRowState(IDictionary<string, object?> data)
    {
        if (!data.TryGetValue(SystemColumns.RowState, out var stateValue))
            return RowStateValue.Valid;

        return stateValue?.ToString()?.ToLowerInvariant() switch
        {
            "draft" => RowStateValue.Draft,
            "valid" => RowStateValue.Valid,
            "error" => RowStateValue.Error,
            _ => RowStateValue.Valid
        };
    }

    private static List<RowValidationError> GetRowErrors(IDictionary<string, object?> data)
    {
        if (!data.TryGetValue(SystemColumns.RowErrors, out var errorsValue))
            return [];

        try
        {
            return errorsValue switch
            {
                string json when !string.IsNullOrEmpty(json) =>
                    JsonSerializer.Deserialize<List<RowValidationError>>(json) ?? [],
                JsonDocument doc =>
                    doc.Deserialize<List<RowValidationError>>() ?? [],
                _ => []
            };
        }
        catch
        {
            return [];
        }
    }

    private async Task<IDictionary<string, object?>> UpdateRowStateAsync(
        TableMetadata table,
        Guid recordId,
        RowStateValue newState,
        List<RowValidationError> errors,
        CancellationToken cancellationToken)
    {
        var stateColumn = table.Columns.FirstOrDefault(c => c.LogicalName == SystemColumns.RowState);
        var errorsColumn = table.Columns.FirstOrDefault(c => c.LogicalName == SystemColumns.RowErrors);
        var idColumn = table.Columns.FirstOrDefault(c => c.IsPrimaryKey);

        if (stateColumn is null || idColumn is null)
        {
            throw new InvalidOperationException("Table does not have required row state columns");
        }

        var stateString = newState.ToString().ToLowerInvariant();
        var errorsJson = errors.Count > 0
            ? JsonSerializer.Serialize(errors)
            : null;

        var setClauses = new List<string>
        {
            $"{stateColumn.PhysicalName} = @state"
        };

        if (errorsColumn is not null)
        {
            setClauses.Add($"{errorsColumn.PhysicalName} = @errors::jsonb");
        }

        var sql = $"""
            UPDATE {table.PhysicalName}
            SET {string.Join(", ", setClauses)}
            WHERE {idColumn.PhysicalName} = @id
            RETURNING *
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await connection.QuerySingleAsync<dynamic>(
            new CommandDefinition(sql, new { id = recordId, state = stateString, errors = errorsJson },
                cancellationToken: cancellationToken));

        return MapToLogicalDictionary(result, table.Columns);
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

    #endregion
}
