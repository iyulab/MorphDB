using System.Diagnostics.Metrics;

namespace MorphDB.Npgsql.Audit;

/// <summary>
/// What the audit log could not keep. The log is written from a bounded queue after the response
/// is sent, so an entry can be lost without any request failing; this counter is where that loss
/// shows. A process that dies takes its queue with it and cannot count what it lost — the counter
/// covers every loss the running process can see.
/// </summary>
public static class AuditMetrics
{
    /// <summary>The meter every MorphDB instrument is created on; add it to the metrics pipeline to scrape them.</summary>
    public const string MeterName = "MorphDB";

    /// <summary>The instrument name of the loss counter.</summary>
    public const string DroppedCounterName = "morphdb.audit.dropped";

    /// <summary>The tag that says why entries were lost.</summary>
    public const string ReasonTag = "reason";

    /// <summary>The queue was full, so the oldest queued entries were discarded to make room.</summary>
    public const string QueueOverflow = "queue_overflow";

    /// <summary>The database refused or could not take a batch, and it was discarded after logging the failure.</summary>
    public const string WriteFailed = "write_failed";

    /// <summary>The service was already shutting down, so the entry was never queued.</summary>
    public const string ServiceStopped = "service_stopped";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Dropped = Meter.CreateCounter<long>(
        DroppedCounterName,
        unit: "{entry}",
        description: "Audit log entries that were not recorded, by reason.");

    internal static void RecordDropped(long count, string reason) =>
        Dropped.Add(count, new KeyValuePair<string, object?>(ReasonTag, reason));
}
