using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;
using MorphDB.Core.Security;

namespace MorphDB.Npgsql.Pipeline;

/// <summary>
/// Orchestrates the write pipeline, executing validators and transformers in order.
/// </summary>
public sealed class WritePipeline : IWritePipeline
{
    private readonly IEnumerable<IValidator> _validators;
    private readonly IEnumerable<ITransformer> _transformers;
    private readonly ISecurityContextAccessor _securityContextAccessor;
    private readonly IWriteExecutor _writeExecutor;

    public WritePipeline(
        IEnumerable<IValidator> validators,
        IEnumerable<ITransformer> transformers,
        ISecurityContextAccessor securityContextAccessor,
        IWriteExecutor writeExecutor)
    {
        // Sort by Order for consistent execution
        _validators = validators.OrderBy(v => v.Order).ToList();
        _transformers = transformers.OrderBy(t => t.Order).ToList();
        _securityContextAccessor = securityContextAccessor;
        _writeExecutor = writeExecutor;
    }

    public async Task<WriteResult> InsertAsync(
        Guid tenantId,
        TableMetadata table,
        IDictionary<string, object?> data,
        WriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var context = CreateContext(tenantId, table, data, WriteOperationType.Insert, options, cancellationToken);
        return await ExecutePipelineAsync(context);
    }

    public async Task<WriteResult> UpdateAsync(
        Guid tenantId,
        TableMetadata table,
        Guid recordId,
        IDictionary<string, object?> data,
        IDictionary<string, object?>? existingData = null,
        WriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var context = CreateContext(tenantId, table, data, WriteOperationType.Update, options, cancellationToken, recordId, existingData);
        return await ExecutePipelineAsync(context);
    }

    public async Task<WriteResult> DeleteAsync(
        Guid tenantId,
        TableMetadata table,
        Guid recordId,
        IDictionary<string, object?>? existingData = null,
        WriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var context = CreateContext(tenantId, table, new Dictionary<string, object?>(), WriteOperationType.Delete, options, cancellationToken, recordId, existingData);

        // For delete, we only run transformers (soft delete) and skip validators
        foreach (var transformer in _transformers)
        {
            if (!context.ShouldContinue)
                break;
            if (transformer.ShouldExecute(context))
            {
                await transformer.ExecuteAsync(context);
            }
        }

        if (context.Errors.Count > 0)
        {
            return WriteResult.Failed(context.Errors.ToArray());
        }

        return await _writeExecutor.ExecuteDeleteAsync(context);
    }

    public async Task<WriteResult> ValidateAsync(
        Guid tenantId,
        TableMetadata table,
        IDictionary<string, object?> data,
        WriteOperationType operationType,
        WriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var context = CreateContext(tenantId, table, data, operationType, options, cancellationToken);

        // Run transformers first (to apply defaults, etc.)
        foreach (var transformer in _transformers)
        {
            if (!context.ShouldContinue)
                break;
            if (transformer.ShouldExecute(context))
            {
                await transformer.ExecuteAsync(context);
            }
        }

        // Then run validators
        foreach (var validator in _validators)
        {
            if (!context.ShouldContinue)
                break;
            if (validator.ShouldExecute(context))
            {
                await validator.ExecuteAsync(context);
            }
        }

        if (context.Errors.Count > 0)
        {
            return WriteResult.Failed(context.Errors.ToArray());
        }

        // Don't actually write, just return the transformed data
        return WriteResult.Ok(context.Data);
    }

    public async Task<IReadOnlyList<WriteResult>> ValidateBatchAsync(
        Guid tenantId,
        TableMetadata table,
        IReadOnlyList<IDictionary<string, object?>> records,
        WriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<WriteResult>(records.Count);

        foreach (var record in records)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
            var result = await ValidateAsync(tenantId, table, record, WriteOperationType.Insert, options, cancellationToken);
            results.Add(result);
        }

        return results;
    }

    private async Task<WriteResult> ExecutePipelineAsync(WriteContext context)
    {
        // Phase 1: Run transformers (apply defaults, timestamps, etc.)
        foreach (var transformer in _transformers)
        {
            if (!context.ShouldContinue)
                break;
            if (transformer.ShouldExecute(context))
            {
                await transformer.ExecuteAsync(context);
            }
        }

        if (!context.ShouldContinue || context.Errors.Count > 0)
        {
            return WriteResult.Failed(context.Errors.ToArray());
        }

        // Phase 2: Run validators
        foreach (var validator in _validators)
        {
            if (!context.ShouldContinue)
                break;
            if (validator.ShouldExecute(context))
            {
                await validator.ExecuteAsync(context);
            }
        }

        if (context.Errors.Count > 0)
        {
            return WriteResult.Failed(context.Errors.ToArray());
        }

        // Phase 3: Execute the actual write
        return await _writeExecutor.ExecuteAsync(context);
    }

    private WriteContext CreateContext(
        Guid tenantId,
        TableMetadata table,
        IDictionary<string, object?> data,
        WriteOperationType operationType,
        WriteOptions? options,
        CancellationToken cancellationToken,
        Guid? recordId = null,
        IDictionary<string, object?>? existingData = null)
    {
        return new WriteContext
        {
            TenantId = tenantId,
            Table = table,
            OperationType = operationType,
            RecordId = recordId,
            Data = new Dictionary<string, object?>(data),
            OriginalData = new Dictionary<string, object?>(data),
            ExistingData = existingData,
            Options = options ?? WriteOptions.Default,
            SecurityContext = _securityContextAccessor.ContextOrNull,
            CancellationToken = cancellationToken
        };
    }
}

/// <summary>
/// Executes the actual database write after pipeline validation.
/// </summary>
public interface IWriteExecutor
{
    Task<WriteResult> ExecuteAsync(IWriteContext context);
    Task<WriteResult> ExecuteDeleteAsync(IWriteContext context);
}
