namespace MorphDB.Core.Exceptions;

/// <summary>
/// A bulk job was cancelled while it was being carried out — or before it started. Whatever is
/// carrying it out stops where it is and leaves the job cancelled: a cancelled job is not completed,
/// failed, or given a file afterwards.
/// </summary>
public sealed class BulkJobCancelledException : OperationCanceledException
{
    public BulkJobCancelledException(Guid jobId)
        : base($"Bulk job '{jobId}' was cancelled.")
    {
        JobId = jobId;
    }

    public BulkJobCancelledException()
    {
    }

    public BulkJobCancelledException(string message)
        : base(message)
    {
    }

    public BulkJobCancelledException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The job that was cancelled.</summary>
    public Guid JobId { get; }
}
