using MorphDB.Core.Models;

namespace MorphDB.Core.Abstractions;

/// <summary>
/// Carries bulk jobs out: what the background job processor calls. Nothing here is done for a
/// caller, so nothing here asks who is calling — a job is found by its id alone, whatever project
/// it belongs to and whoever requested it.
/// </summary>
/// <remarks>
/// Not for answering a request. A request follows its own jobs through
/// <see cref="IBulkOperationService"/>, which holds a job to its project and its requester; handing
/// what these members produce to whoever asks for it would give one caller another's rows.
/// </remarks>
public interface IBulkJobExecutor
{
    /// <summary>
    /// Gets pending import jobs, oldest first, of every project and every requester.
    /// </summary>
    Task<IReadOnlyList<BulkImportJob>> GetPendingImportJobsAsync(
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets pending export jobs, oldest first, of every project and every requester.
    /// </summary>
    Task<IReadOnlyList<BulkExportJob>> GetPendingExportJobsAsync(
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Carries out an import job, yielding each row's result as it is written. The rows are written
    /// as the caller that requested the job.
    /// </summary>
    IAsyncEnumerable<ImportRowResult> ProcessImportAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Carries out an export job, writing its file to the provided stream. The table is read as the
    /// caller that requested the job.
    /// </summary>
    Task StreamExportAsync(
        Guid jobId,
        Stream outputStream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the file an export job produced, for its requester to download.
    /// </summary>
    Task StoreExportDataAsync(
        Guid jobId,
        Stream dataStream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the stored file of every export job whose <c>ExpiresAt</c> is at or before
    /// <paramref name="now"/>, and returns how many it removed. The job stays, so its status can still
    /// be read; its download answers that it has expired.
    /// </summary>
    Task<int> DeleteExpiredExportFilesAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
