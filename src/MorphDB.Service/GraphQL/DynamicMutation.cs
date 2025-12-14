using System.Text.Json;
using HotChocolate;
using HotChocolate.Types;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Service.Services;

namespace MorphDB.Service.GraphQL;

/// <summary>
/// Dynamic GraphQL mutation type for MorphDB tables.
/// </summary>
[ExtendObjectType(typeof(Mutation))]
public sealed class DynamicMutation
{
    /// <summary>
    /// Creates a new record in the specified table.
    /// </summary>
    [GraphQLDescription("Creates a new record in the specified table")]
    public async Task<MutationResult<RecordNode>> CreateRecord(
        string table,
        [GraphQLType(typeof(AnyType))] IDictionary<string, object?> data,
        [Service] IMorphDataService dataService,
        [Service] ITenantContextAccessor tenantAccessor,
        CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = tenantAccessor.TenantId;

            // Convert data from JsonElement if necessary
            var normalizedData = NormalizeData(data);

            var result = await dataService.InsertAsync(tenantId, table, normalizedData, cancellationToken);

            return new MutationResult<RecordNode>
            {
                Success = true,
                Data = CreateRecordNode(result)
            };
        }
        catch (ValidationException ex)
        {
            return new MutationResult<RecordNode>
            {
                Success = false,
                Error = ex.Message,
                ErrorCode = ex.ErrorCode
            };
        }
        catch (NotFoundException ex)
        {
            return new MutationResult<RecordNode>
            {
                Success = false,
                Error = ex.Message,
                ErrorCode = ex.ErrorCode
            };
        }
    }

    /// <summary>
    /// Updates an existing record.
    /// </summary>
    [GraphQLDescription("Updates an existing record")]
    public async Task<MutationResult<RecordNode>> UpdateRecord(
        string table,
        Guid id,
        [GraphQLType(typeof(AnyType))] IDictionary<string, object?> data,
        [Service] IMorphDataService dataService,
        [Service] ITenantContextAccessor tenantAccessor,
        CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = tenantAccessor.TenantId;

            // Verify record exists
            var existing = await dataService.GetByIdAsync(tenantId, table, id, cancellationToken);
            if (existing is null)
            {
                return new MutationResult<RecordNode>
                {
                    Success = false,
                    Error = $"Record with ID '{id}' not found in table '{table}'",
                    ErrorCode = "NOT_FOUND"
                };
            }

            var normalizedData = NormalizeData(data);
            var result = await dataService.UpdateAsync(tenantId, table, id, normalizedData, cancellationToken);

            return new MutationResult<RecordNode>
            {
                Success = true,
                Data = CreateRecordNodeWithId(result, id)
            };
        }
        catch (ValidationException ex)
        {
            return new MutationResult<RecordNode>
            {
                Success = false,
                Error = ex.Message,
                ErrorCode = ex.ErrorCode
            };
        }
        catch (NotFoundException ex)
        {
            return new MutationResult<RecordNode>
            {
                Success = false,
                Error = ex.Message,
                ErrorCode = ex.ErrorCode
            };
        }
    }

    /// <summary>
    /// Deletes a record.
    /// </summary>
    [GraphQLDescription("Deletes a record")]
    public async Task<MutationResult<bool>> DeleteRecord(
        string table,
        Guid id,
        [Service] IMorphDataService dataService,
        [Service] ITenantContextAccessor tenantAccessor,
        CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = tenantAccessor.TenantId;

            var deleted = await dataService.DeleteAsync(tenantId, table, id, cancellationToken);

            if (!deleted)
            {
                return new MutationResult<bool>
                {
                    Success = false,
                    Error = $"Record with ID '{id}' not found in table '{table}'",
                    ErrorCode = "NOT_FOUND"
                };
            }

            return new MutationResult<bool>
            {
                Success = true,
                Data = true
            };
        }
        catch (NotFoundException ex)
        {
            return new MutationResult<bool>
            {
                Success = false,
                Error = ex.Message,
                ErrorCode = ex.ErrorCode
            };
        }
    }

    /// <summary>
    /// Upserts a record (insert or update based on key columns).
    /// </summary>
    [GraphQLDescription("Upserts a record (insert or update based on key columns)")]
    public async Task<MutationResult<RecordNode>> UpsertRecord(
        string table,
        [GraphQLType(typeof(AnyType))] IDictionary<string, object?> data,
        string[] keyColumns,
        [Service] IMorphDataService dataService,
        [Service] ITenantContextAccessor tenantAccessor,
        CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = tenantAccessor.TenantId;

            var normalizedData = NormalizeData(data);
            var result = await dataService.UpsertAsync(tenantId, table, normalizedData, keyColumns, cancellationToken);

            return new MutationResult<RecordNode>
            {
                Success = true,
                Data = CreateRecordNode(result)
            };
        }
        catch (ValidationException ex)
        {
            return new MutationResult<RecordNode>
            {
                Success = false,
                Error = ex.Message,
                ErrorCode = ex.ErrorCode
            };
        }
        catch (NotFoundException ex)
        {
            return new MutationResult<RecordNode>
            {
                Success = false,
                Error = ex.Message,
                ErrorCode = ex.ErrorCode
            };
        }
    }

    /// <summary>
    /// Inserts multiple records in a batch.
    /// </summary>
    [GraphQLDescription("Inserts multiple records in a batch")]
    public async Task<MutationResult<IReadOnlyList<RecordNode>>> CreateRecords(
        string table,
        [GraphQLType(typeof(ListType<AnyType>))] IReadOnlyList<IDictionary<string, object?>> records,
        [Service] IMorphDataService dataService,
        [Service] ITenantContextAccessor tenantAccessor,
        CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = tenantAccessor.TenantId;

            var normalizedRecords = records.Select(NormalizeData).ToList();
            var results = await dataService.InsertBatchAsync(tenantId, table, normalizedRecords, cancellationToken);

            var nodes = results.Select(CreateRecordNode).ToList();

            return new MutationResult<IReadOnlyList<RecordNode>>
            {
                Success = true,
                Data = nodes
            };
        }
        catch (ValidationException ex)
        {
            return new MutationResult<IReadOnlyList<RecordNode>>
            {
                Success = false,
                Error = ex.Message,
                ErrorCode = ex.ErrorCode
            };
        }
    }

    private static IDictionary<string, object?> NormalizeData(IDictionary<string, object?> data)
    {
        var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in data)
        {
            normalized[kvp.Key] = NormalizeValue(kvp.Value);
        }

        return normalized;
    }

    private static object? NormalizeValue(object? value)
    {
        if (value is null)
            return null;

        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when element.TryGetInt32(out var intVal) => intVal,
                JsonValueKind.Number when element.TryGetInt64(out var longVal) => longVal,
                JsonValueKind.Number when element.TryGetDecimal(out var decVal) => decVal,
                JsonValueKind.String when element.TryGetGuid(out var guidVal) => guidVal,
                JsonValueKind.String when element.TryGetDateTimeOffset(out var dateVal) => dateVal,
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Object => JsonSerializer.Serialize(element),
                JsonValueKind.Array => JsonSerializer.Serialize(element),
                _ => element.ToString()
            };
        }

        return value;
    }

    private static RecordNode CreateRecordNode(IDictionary<string, object?> r)
    {
        return new RecordNode
        {
            Id = GetRecordId(r),
            Data = r,
            CreatedAt = r.TryGetValue("created_at", out var createdAt) && createdAt is DateTimeOffset ca ? ca : null,
            UpdatedAt = r.TryGetValue("updated_at", out var updatedAt) && updatedAt is DateTimeOffset ua ? ua : null
        };
    }

    private static RecordNode CreateRecordNodeWithId(IDictionary<string, object?> r, Guid id)
    {
        return new RecordNode
        {
            Id = id,
            Data = r,
            CreatedAt = r.TryGetValue("created_at", out var createdAt) && createdAt is DateTimeOffset ca ? ca : null,
            UpdatedAt = r.TryGetValue("updated_at", out var updatedAt) && updatedAt is DateTimeOffset ua ? ua : null
        };
    }

    private static Guid GetRecordId(IDictionary<string, object?> r)
    {
        return r.TryGetValue("id", out var idValue) && idValue is Guid id ? id : Guid.Empty;
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
