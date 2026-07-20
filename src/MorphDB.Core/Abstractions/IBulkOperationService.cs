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
        Guid projectId,
        string tableName,
        Stream dataStream,
        CsvImportOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a streaming import from JSON array.
    /// </summary>
    Task<BulkImportJob> StartJsonImportAsync(
        Guid projectId,
        string tableName,
        Stream dataStream,
        JsonImportOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a streaming import from NDJSON (newline-delimited JSON).
    /// </summary>
    Task<BulkImportJob> StartNdjsonImportAsync(
        Guid projectId,
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
        Guid projectId,
        string tableName,
        CsvExportOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a JSON export.
    /// </summary>
    Task<BulkExportJob> StartJsonExportAsync(
        Guid projectId,
        string tableName,
        JsonExportOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts an XLSX export.
    /// </summary>
    Task<BulkExportJob> StartXlsxExportAsync(
        Guid projectId,
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
    /// Lists import jobs for a project.
    /// </summary>
    Task<IReadOnlyList<BulkImportJob>> ListImportJobsAsync(
        Guid projectId,
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists export jobs for a project.
    /// </summary>
    Task<IReadOnlyList<BulkExportJob>> ListExportJobsAsync(
        Guid projectId,
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

    /// <summary>
    /// Gets pending import jobs for background processing.
    /// </summary>
    Task<IReadOnlyList<BulkImportJob>> GetPendingImportJobsAsync(
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets pending export jobs for background processing.
    /// </summary>
    Task<IReadOnlyList<BulkExportJob>> GetPendingExportJobsAsync(
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores export data to file system or storage.
    /// </summary>
    Task StoreExportDataAsync(
        Guid jobId,
        Stream dataStream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets stored export data stream.
    /// </summary>
    Task<Stream?> GetStoredExportDataAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    #endregion
}
