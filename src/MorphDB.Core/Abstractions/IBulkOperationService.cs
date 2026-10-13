using MorphDB.Core.Models;

namespace MorphDB.Core.Abstractions;

/// <summary>
/// Bulk import and export, as a caller sees it: requesting a job, and following one it requested.
/// </summary>
/// <remarks>
/// <para>Every member that reaches an existing job takes the project the caller is working in and
/// answers for the ambient <see cref="Security.SecurityContext"/>. A job is there for a caller only
/// when it belongs to that project and the caller is the one that requested it — the same user id,
/// two callers without one being the same caller — or bypasses row-level security. A job requested
/// by a caller that bypasses row-level security, and a job stored without a requester, is there for
/// a bypassing caller only; and where there is no ambient context, no job is there at all. A job
/// that is not there is answered exactly as an id that was never issued: <c>null</c>,
/// <c>false</c>, or absent from a list.</para>
/// <para>There is no member here that reaches a job by its id alone. Carrying jobs out, which is
/// not done for a caller, is <see cref="IBulkJobExecutor"/>.</para>
/// </remarks>
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

    #endregion

    #region Job Management

    /// <summary>
    /// Gets the current status of an import job, or <c>null</c> when there is no such job for the
    /// caller.
    /// </summary>
    Task<BulkImportJob?> GetImportJobAsync(Guid projectId, Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current status of an export job, or <c>null</c> when there is no such job for the
    /// caller.
    /// </summary>
    Task<BulkExportJob?> GetExportJobAsync(Guid projectId, Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the project's import jobs that are there for the caller, newest first.
    /// </summary>
    Task<IReadOnlyList<BulkImportJob>> ListImportJobsAsync(
        Guid projectId,
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the project's export jobs that are there for the caller, newest first.
    /// </summary>
    Task<IReadOnlyList<BulkExportJob>> ListExportJobsAsync(
        Guid projectId,
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a job that is pending or running. <c>false</c> when there is no such job for the
    /// caller, or it has already ended.
    /// </summary>
    Task<bool> CancelJobAsync(Guid projectId, Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the progress of an import or export job, or <c>null</c> when there is no such job for
    /// the caller.
    /// </summary>
    Task<BulkJobProgress?> GetJobProgressAsync(Guid projectId, Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the file of an export job, or answers <c>null</c> when there is no such job for the
    /// caller. The file is the one stored when the job was carried out; when none is stored, the
    /// table is read now, as the caller that requested the job — not as the caller opening it.
    /// </summary>
    /// <remarks>
    /// Whether the job has reached a state in which it has a file to give is the caller's question
    /// to ask first, of <see cref="GetExportJobAsync"/>.
    /// </remarks>
    Task<Stream?> OpenExportFileAsync(Guid projectId, Guid jobId, CancellationToken cancellationToken = default);

    #endregion
}
