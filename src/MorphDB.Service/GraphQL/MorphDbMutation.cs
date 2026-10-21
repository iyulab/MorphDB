using System.Text.Json;
using HotChocolate;
using HotChocolate.Types;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;
using MorphDB.Npgsql.Repositories;
using MorphDB.Service.Services;

namespace MorphDB.Service.GraphQL;

/// <summary>
/// The write half of the GraphQL surface: create, update, delete, upsert and batch insert of
/// records. Each operation names its table in an argument, so this class does not change when a
/// table does.
/// </summary>
[ExtendObjectType(typeof(Mutation))]
public sealed class MorphDbMutation
{
    /// <summary>
    /// Creates a new record in the specified table.
    /// </summary>
    [GraphQLDescription("Creates a new record in the specified table")]
    public async Task<MutationResult<RecordNode>> CreateRecord(
        string table,
        [GraphQLType(typeof(AnyType))] JsonElement data,
        [Service] IWritePipeline writePipeline,
        [Service] IMetadataRepository metadataRepository,
        [Service] IProjectContextAccessor projectAccessor,
        CancellationToken cancellationToken)
    {
        try
        {
            var projectId = projectAccessor.ProjectId;

            var tableMetadata = await metadataRepository.GetTableByNameAsync(
                projectId, table, includeColumns: true, cancellationToken);
            if (tableMetadata is null)
            {
                return new MutationResult<RecordNode>
                {
                    Success = false,
                    Error = $"Table '{table}' not found",
                    ErrorCode = "TABLE_NOT_FOUND"
                };
            }

            var normalizedData = GraphQlAny.ToRow(data);

            var writeResult = await writePipeline.InsertAsync(
                projectId, tableMetadata, normalizedData, cancellationToken: cancellationToken);

            if (!writeResult.Success)
            {
                return new MutationResult<RecordNode>
                {
                    Success = false,
                    Error = string.Join("; ", writeResult.Errors.Select(e => e.Message)),
                    ErrorCode = ErrorHandling.WriteFailure.CodeFor(writeResult)
                };
            }

            return new MutationResult<RecordNode>
            {
                Success = true,
                Data = CreateRecordNode(writeResult.Data ?? normalizedData)
            };
        }
        catch (Exception ex) when (IsCallersMistake(ex))
        {
            return Refused<RecordNode>(ex);
        }
    }

    /// <summary>
    /// Updates an existing record.
    /// </summary>
    [GraphQLDescription("Updates an existing record")]
    public async Task<MutationResult<RecordNode>> UpdateRecord(
        string table,
        Guid id,
        [GraphQLType(typeof(AnyType))] JsonElement data,
        [Service] IMorphDataService dataService,
        [Service] IWritePipeline writePipeline,
        [Service] IMetadataRepository metadataRepository,
        [Service] IProjectContextAccessor projectAccessor,
        CancellationToken cancellationToken)
    {
        try
        {
            var projectId = projectAccessor.ProjectId;

            var tableMetadata = await metadataRepository.GetTableByNameAsync(
                projectId, table, includeColumns: true, cancellationToken);
            if (tableMetadata is null)
            {
                return new MutationResult<RecordNode>
                {
                    Success = false,
                    Error = $"Table '{table}' not found",
                    ErrorCode = "TABLE_NOT_FOUND"
                };
            }

            // Verify record exists
            var existing = await dataService.GetByIdAsync(projectId, table, id, cancellationToken);
            if (existing is null)
            {
                return new MutationResult<RecordNode>
                {
                    Success = false,
                    Error = $"Record with ID '{id}' not found in table '{table}'",
                    ErrorCode = "RECORD_NOT_FOUND"
                };
            }

            var normalizedData = GraphQlAny.ToRow(data);
            var writeResult = await writePipeline.UpdateAsync(
                projectId, tableMetadata, id, normalizedData, existing, cancellationToken: cancellationToken);

            if (ErrorHandling.WriteFailure.IsNotFound(writeResult))
            {
                return new MutationResult<RecordNode>
                {
                    Success = false,
                    Error = $"Record with ID '{id}' not found in table '{table}'",
                    ErrorCode = "RECORD_NOT_FOUND"
                };
            }

            if (!writeResult.Success)
            {
                return new MutationResult<RecordNode>
                {
                    Success = false,
                    Error = string.Join("; ", writeResult.Errors.Select(e => e.Message)),
                    ErrorCode = ErrorHandling.WriteFailure.CodeFor(writeResult)
                };
            }

            return new MutationResult<RecordNode>
            {
                Success = true,
                Data = CreateRecordNodeWithId(writeResult.Data ?? normalizedData, id)
            };
        }
        catch (Exception ex) when (IsCallersMistake(ex))
        {
            return Refused<RecordNode>(ex);
        }
    }

    /// <summary>
    /// Deletes a record.
    /// </summary>
    [GraphQLDescription("Deletes a record")]
    public async Task<MutationResult<bool>> DeleteRecord(
        string table,
        Guid id,
        [Service] IWritePipeline writePipeline,
        [Service] IMetadataRepository metadataRepository,
        [Service] IProjectContextAccessor projectAccessor,
        CancellationToken cancellationToken)
    {
        try
        {
            var projectId = projectAccessor.ProjectId;

            var tableMetadata = await metadataRepository.GetTableByNameAsync(
                projectId, table, includeColumns: true, cancellationToken);
            if (tableMetadata is null)
            {
                return new MutationResult<bool>
                {
                    Success = false,
                    Error = $"Table '{table}' not found",
                    ErrorCode = "TABLE_NOT_FOUND"
                };
            }

            var writeResult = await writePipeline.DeleteAsync(
                projectId, tableMetadata, id, cancellationToken: cancellationToken);

            if (ErrorHandling.WriteFailure.IsNotFound(writeResult))
            {
                return new MutationResult<bool>
                {
                    Success = false,
                    Error = $"Record with ID '{id}' not found in table '{table}'",
                    ErrorCode = "RECORD_NOT_FOUND"
                };
            }

            if (!writeResult.Success)
            {
                return new MutationResult<bool>
                {
                    Success = false,
                    Error = string.Join("; ", writeResult.Errors.Select(e => e.Message)),
                    ErrorCode = ErrorHandling.WriteFailure.CodeFor(writeResult)
                };
            }

            return new MutationResult<bool>
            {
                Success = true,
                Data = true
            };
        }
        catch (Exception ex) when (IsCallersMistake(ex))
        {
            return Refused<bool>(ex);
        }
    }

    /// <summary>
    /// Upserts a record (insert or update based on key columns).
    /// </summary>
    // Reaches the pipeline via IMorphDataService, whose write methods now delegate to
    // IWritePipeline — the same validators and transformers as every other door.
    [GraphQLDescription("Upserts a record (insert or update based on key columns)")]
    public async Task<MutationResult<RecordNode>> UpsertRecord(
        string table,
        [GraphQLType(typeof(AnyType))] JsonElement data,
        string[] keyColumns,
        [Service] IMorphDataService dataService,
        [Service] IProjectContextAccessor projectAccessor,
        CancellationToken cancellationToken)
    {
        try
        {
            var projectId = projectAccessor.ProjectId;

            var normalizedData = GraphQlAny.ToRow(data);
            var result = await dataService.UpsertAsync(projectId, table, normalizedData, keyColumns, cancellationToken);

            return new MutationResult<RecordNode>
            {
                Success = true,
                Data = CreateRecordNode(result.Record)
            };
        }
        catch (Exception ex) when (IsCallersMistake(ex))
        {
            return Refused<RecordNode>(ex);
        }
    }

    /// <summary>
    /// Inserts multiple records in a batch.
    /// </summary>
    // Reaches the pipeline via IMorphDataService.InsertBatchAsync (pipeline per row, one
    // transaction via the ambient connection scope).
    [GraphQLDescription("Inserts multiple records in a batch")]
    public async Task<MutationResult<IReadOnlyList<RecordNode>>> CreateRecords(
        string table,
        [GraphQLType(typeof(ListType<AnyType>))] IReadOnlyList<JsonElement> records,
        [Service] IMorphDataService dataService,
        [Service] IProjectContextAccessor projectAccessor,
        CancellationToken cancellationToken)
    {
        try
        {
            var projectId = projectAccessor.ProjectId;

            var normalizedRecords = records.Select(GraphQlAny.ToRow).ToList();
            var results = await dataService.InsertBatchAsync(projectId, table, normalizedRecords, cancellationToken);

            var nodes = results.Select(CreateRecordNode).ToList();

            return new MutationResult<IReadOnlyList<RecordNode>>
            {
                Success = true,
                Data = nodes
            };
        }
        catch (Exception ex) when (IsCallersMistake(ex))
        {
            return Refused<IReadOnlyList<RecordNode>>(ex);
        }
    }

    private static RecordNode CreateRecordNode(IDictionary<string, object?> r)
    {
        return new RecordNode
        {
            Id = SystemColumns.RequireRecordId(r),
            Data = GraphQlAny.FromRow(r),
            CreatedAt = r.TryGetValue("_created_at", out var createdAt) && createdAt is DateTimeOffset ca ? ca : null,
            UpdatedAt = r.TryGetValue("_updated_at", out var updatedAt) && updatedAt is DateTimeOffset ua ? ua : null
        };
    }

    /// <summary>
    /// Whether an exception is the caller's mistake rather than ours — the REST door's classification.
    /// A mutation answers such a mistake in its result (<c>success: false</c>, the REST code as
    /// <c>errorCode</c>); anything else escapes to the error filter and stays unexplained.
    /// </summary>
    private static bool IsCallersMistake(Exception exception) =>
        exception is not OperationCanceledException
        && ErrorHandling.ErrorClassification.Classify(exception).Status < StatusCodes.Status500InternalServerError;

    private static MutationResult<T> Refused<T>(Exception exception) => new()
    {
        Success = false,
        Error = exception.Message,
        ErrorCode = ErrorHandling.ErrorClassification.Classify(exception).Code,
    };

    private static RecordNode CreateRecordNodeWithId(IDictionary<string, object?> r, Guid id)
    {
        return new RecordNode
        {
            Id = id,
            Data = GraphQlAny.FromRow(r),
            CreatedAt = r.TryGetValue("_created_at", out var createdAt) && createdAt is DateTimeOffset ca ? ca : null,
            UpdatedAt = r.TryGetValue("_updated_at", out var updatedAt) && updatedAt is DateTimeOffset ua ? ua : null
        };
    }
}

/// <summary>
/// Result of a mutation operation.
/// </summary>
public sealed class MutationResult<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }
}
