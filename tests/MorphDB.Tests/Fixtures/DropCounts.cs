using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using MorphDB.Npgsql.Audit;

namespace MorphDB.Tests.Fixtures;

/// <summary>Totals of <c>morphdb.audit.dropped</c> by reason, from the moment it is created.</summary>
public sealed class DropCounts : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentDictionary<string, long> _byReason = new();

    public DropCounts()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == AuditMetrics.MeterName && instrument.Name == AuditMetrics.DroppedCounterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == AuditMetrics.ReasonTag && tag.Value is string reason)
                {
                    _byReason.AddOrUpdate(reason, measurement, (_, total) => total + measurement);
                }
            }
        });
        _listener.Start();
    }

    public long Of(string reason) => _byReason.GetValueOrDefault(reason);

    public void Dispose() => _listener.Dispose();
}
