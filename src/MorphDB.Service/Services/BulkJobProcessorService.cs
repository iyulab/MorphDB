using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;

namespace MorphDB.Service.Services;

/// <summary>
/// Background service that processes pending bulk import/export jobs.
/// </summary>
public sealed partial class BulkJobProcessorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BulkJobProcessorService> _logger;
    private readonly BulkJobProcessorOptions _options;

    public BulkJobProcessorService(
        IServiceScopeFactory scopeFactory,
        ILogger<BulkJobProcessorService> logger,
        BulkJobProcessorOptions? options = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options ?? new BulkJobProcessorOptions();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogProcessorStarted(_logger);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingJobsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogProcessorError(_logger, ex);
            }

            await Task.Delay(_options.PollingInterval, stoppingToken);
        }

        LogProcessorStopped(_logger);
    }

    private async Task ProcessPendingJobsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var bulkService = scope.ServiceProvider.GetRequiredService<IBulkOperationService>();

        // Process pending export jobs
        var pendingExports = await bulkService.GetPendingExportJobsAsync(_options.BatchSize, cancellationToken);
        foreach (var job in pendingExports)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await ProcessExportJobAsync(bulkService, job, cancellationToken);
        }

        // Process pending import jobs
        var pendingImports = await bulkService.GetPendingImportJobsAsync(_options.BatchSize, cancellationToken);
        foreach (var job in pendingImports)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await ProcessImportJobAsync(bulkService, job, cancellationToken);
        }
    }

    private async Task ProcessExportJobAsync(
        IBulkOperationService bulkService,
        BulkExportJob job,
        CancellationToken cancellationToken)
    {
        LogExportJobStarted(_logger, job.JobId, job.TableName, job.Format.ToString());

        try
        {
            // Create a memory stream to hold the export data
            using var memoryStream = new MemoryStream();

            // Stream the export data
            await bulkService.StreamExportAsync(job.JobId, memoryStream, cancellationToken);

            // Reset position for storing
            memoryStream.Position = 0;

            // Store the export data for later download
            await bulkService.StoreExportDataAsync(job.JobId, memoryStream, cancellationToken);

            LogExportJobCompleted(_logger, job.JobId, memoryStream.Length);
        }
        catch (Exception ex)
        {
            LogExportJobFailed(_logger, job.JobId, ex);
        }
    }

    private async Task ProcessImportJobAsync(
        IBulkOperationService bulkService,
        BulkImportJob job,
        CancellationToken cancellationToken)
    {
        LogImportJobStarted(_logger, job.JobId, job.TableName, job.Format.ToString());

        try
        {
            var successCount = 0L;
            var errorCount = 0L;

            await foreach (var result in bulkService.ProcessImportAsync(job.JobId, cancellationToken))
            {
                if (result.Success)
                    successCount++;
                else
                    errorCount++;
            }

            LogImportJobCompleted(_logger, job.JobId, successCount, errorCount);
        }
        catch (Exception ex)
        {
            LogImportJobFailed(_logger, job.JobId, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Bulk job processor started")]
    private static partial void LogProcessorStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Bulk job processor stopped")]
    private static partial void LogProcessorStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error in bulk job processor")]
    private static partial void LogProcessorError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting export job {JobId} for table {TableName} (format: {Format})")]
    private static partial void LogExportJobStarted(ILogger logger, Guid jobId, string tableName, string format);

    [LoggerMessage(Level = LogLevel.Information, Message = "Export job {JobId} completed, file size: {FileSize} bytes")]
    private static partial void LogExportJobCompleted(ILogger logger, Guid jobId, long fileSize);

    [LoggerMessage(Level = LogLevel.Error, Message = "Export job {JobId} failed")]
    private static partial void LogExportJobFailed(ILogger logger, Guid jobId, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting import job {JobId} for table {TableName} (format: {Format})")]
    private static partial void LogImportJobStarted(ILogger logger, Guid jobId, string tableName, string format);

    [LoggerMessage(Level = LogLevel.Information, Message = "Import job {JobId} completed: {SuccessCount} succeeded, {ErrorCount} failed")]
    private static partial void LogImportJobCompleted(ILogger logger, Guid jobId, long successCount, long errorCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Import job {JobId} failed")]
    private static partial void LogImportJobFailed(ILogger logger, Guid jobId, Exception exception);
}

/// <summary>
/// Options for the bulk job processor.
/// </summary>
public sealed class BulkJobProcessorOptions
{
    /// <summary>
    /// How often to poll for pending jobs.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum number of jobs to process per batch.
    /// </summary>
    public int BatchSize { get; set; } = 5;
}
