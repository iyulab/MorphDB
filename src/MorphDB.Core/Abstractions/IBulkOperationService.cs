using MorphDB.Core.Models;

namespace MorphDB.Core.Abstractions;

/// <summary>
/// Service for bulk import/export operations.
/// </summary>
public interface IBulkOperationService
{
    #region Import Operations

    /// <summary>
    /// Starts a streaming import from CSV.
    /// </summary>
    Task<BulkImportJob> StartCsvImportAsync(
        Guid tenantId,
        string tableName,
        Stream dataStream,
        CsvImportOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a streaming import from JSON array.
    /// </summary>
    Task<BulkImportJob> StartJsonImportAsync(
        Guid tenantId,
        string tableName,
        Stream dataStream,
        JsonImportOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a streaming import from NDJSON (newline-delimited JSON).
    /// </summary>
    Task<BulkImportJob> StartNdjsonImportAsync(
        Guid tenantId,
        string tableName,
        Stream dataStream,
        JsonImportOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes import data asynchronously, yielding results as they complete.
    /// </summary>
    IAsyncEnumerable<ImportRowResult> ProcessImportAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    #endregion

    #region Export Operations

    /// <summary>
    /// Starts a CSV export.
    /// </summary>
    Task<BulkExportJob> StartCsvExportAsync(
        Guid tenantId,
        string tableName,
        CsvExportOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a JSON export.
    /// </summary>
    Task<BulkExportJob> StartJsonExportAsync(
        Guid tenantId,
        string tableName,
        JsonExportOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts an XLSX export.
    /// </summary>
    Task<BulkExportJob> StartXlsxExportAsync(
        Guid tenantId,
        string tableName,
        XlsxExportOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams export data to the provided stream.
    /// </summary>
    Task StreamExportAsync(
        Guid jobId,
        Stream outputStream,
        CancellationToken cancellationToken = default);

    #endregion

    #region Job Management

    /// <summary>
    /// Gets the current status of an import job.
    /// </summary>
    Task<BulkImportJob?> GetImportJobAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current status of an export job.
    /// </summary>
    Task<BulkExportJob?> GetExportJobAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists import jobs for a tenant.
    /// </summary>
    Task<IReadOnlyList<BulkImportJob>> ListImportJobsAsync(
        Guid tenantId,
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists export jobs for a tenant.
    /// </summary>
    Task<IReadOnlyList<BulkExportJob>> ListExportJobsAsync(
        Guid tenantId,
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a running job.
    /// </summary>
    Task<bool> CancelJobAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets progress updates for a job.
    /// </summary>
    Task<BulkJobProgress?> GetJobProgressAsync(Guid jobId, CancellationToken cancellationToken = default);

    #endregion
}
