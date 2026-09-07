using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MorphDB.Service.Health;

namespace MorphDB.Tests.Unit;

/// <summary>
/// The health endpoints' JSON is a wire contract. These pin the document shape independently of
/// which checks are registered: member names and casing, how a status and a duration are written,
/// which members disappear when they are null, and that a failing check's message reaches the
/// reader.
/// </summary>
public class HealthReportJsonTests
{
    [Fact]
    public void Writes_status_total_duration_and_one_entry_per_check()
    {
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["postgresql"] = new(HealthStatus.Healthy, description: null, TimeSpan.FromMilliseconds(12), exception: null, data: null, tags: ["db", "ready"])
            },
            TimeSpan.FromMilliseconds(15));

        using var document = JsonDocument.Parse(HealthReportJson.Serialize(report));
        var root = document.RootElement;

        Assert.Equal("Healthy", root.GetProperty("status").GetString());
        Assert.Equal(TimeSpan.FromMilliseconds(15), TimeSpan.Parse(root.GetProperty("totalDuration").GetString()!, System.Globalization.CultureInfo.InvariantCulture));

        var entry = root.GetProperty("entries").GetProperty("postgresql");
        Assert.Equal("Healthy", entry.GetProperty("status").GetString());
        Assert.Equal(TimeSpan.FromMilliseconds(12), TimeSpan.Parse(entry.GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(["db", "ready"], entry.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToArray());
        Assert.Equal(JsonValueKind.Object, entry.GetProperty("data").ValueKind);
        Assert.False(entry.TryGetProperty("description", out _), "a null description is omitted, not written as null");
        Assert.False(entry.TryGetProperty("exception", out _), "a healthy entry carries no exception member");
    }

    [Fact]
    public void A_failing_check_reports_its_status_description_message_and_data()
    {
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["redis"] = new(
                    HealthStatus.Unhealthy,
                    "cache unreachable",
                    TimeSpan.FromSeconds(5),
                    new TimeoutException("no reply within 5s"),
                    new Dictionary<string, object> { ["latencyMs"] = 5000.0 },
                    tags: ["cache"])
            },
            TimeSpan.FromSeconds(5));

        using var document = JsonDocument.Parse(HealthReportJson.Serialize(report));
        var entry = document.RootElement.GetProperty("entries").GetProperty("redis");

        Assert.Equal("Unhealthy", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("Unhealthy", entry.GetProperty("status").GetString());
        Assert.Equal("cache unreachable", entry.GetProperty("description").GetString());
        Assert.Equal("no reply within 5s", entry.GetProperty("exception").GetString());
        Assert.Equal(5000.0, entry.GetProperty("data").GetProperty("latencyMs").GetDouble());
    }

    [Fact]
    public void A_report_with_no_entries_still_carries_an_empty_entries_object()
    {
        var report = new HealthReport(new Dictionary<string, HealthReportEntry>(), TimeSpan.Zero);

        using var document = JsonDocument.Parse(HealthReportJson.Serialize(report));

        Assert.Equal("Healthy", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Object, document.RootElement.GetProperty("entries").ValueKind);
        Assert.Empty(document.RootElement.GetProperty("entries").EnumerateObject());
    }
}
