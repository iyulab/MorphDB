using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MorphDB.Service.Health;

/// <summary>
/// Writes a <see cref="HealthReport"/> as the JSON document the health endpoints have always
/// returned: overall <c>status</c>, <c>totalDuration</c>, and one entry per check keyed by its
/// name, each with <c>status</c>, <c>duration</c>, <c>tags</c>, its <c>data</c>, and — when a
/// check set them — <c>description</c> and the failing <c>exception</c>'s message. Statuses are
/// their names, durations are <see cref="TimeSpan"/> text, and members that would be null are
/// omitted. The shape is a wire contract: an orchestrator or dashboard that learned it from an
/// earlier version keeps reading it.
/// </summary>
internal static class HealthReportJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters = { new JsonStringEnumConverter() }
    };

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(Serialize(report), context.RequestAborted);
    }

    public static string Serialize(HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var (name, entry) in report.Entries)
        {
            entries[name] = new Entry(
                entry.Data,
                entry.Description,
                entry.Duration,
                entry.Exception?.Message,
                entry.Status,
                entry.Tags);
        }

        return JsonSerializer.Serialize(new Document(report.Status, report.TotalDuration, entries), SerializerOptions);
    }

    private sealed record Document(HealthStatus Status, TimeSpan TotalDuration, IReadOnlyDictionary<string, Entry> Entries);

    private sealed record Entry(
        IReadOnlyDictionary<string, object> Data,
        string? Description,
        TimeSpan Duration,
        string? Exception,
        HealthStatus Status,
        IEnumerable<string> Tags);
}
