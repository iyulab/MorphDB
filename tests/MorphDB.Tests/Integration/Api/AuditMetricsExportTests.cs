using MorphDB.Npgsql.Audit;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// <see cref="AuditMetrics"/> is only useful if a scraper can read it: the counter has to be on a
/// meter the service's metrics pipeline listens to, and it has to come out of <c>/metrics</c> under a
/// name an operator can alert on. The unit tests see the instrument; this sees the wire.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class AuditMetricsExportTests
{
    private readonly HttpClient _client;

    public AuditMetricsExportTests(ApiIntegrationFixture fixture)
    {
        _client = fixture.Api.Client;
    }

    [Fact]
    public async Task Lost_audit_entries_are_exported_on_the_metrics_endpoint_by_reason()
    {
        // The service runs in this process, so the instrument it exports is the one written here.
        AuditMetrics.RecordDropped(3, AuditMetrics.QueueOverflow);
        AuditMetrics.RecordDropped(2, AuditMetrics.WriteFailed);

        var response = await _client.GetAsync("/metrics", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.Should().BeTrue("the scrape endpoint answers without a credential");
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // The name an operator alerts on: the instrument's dots become underscores and a counter ends in _total.
        var dropped = lines.Where(l => l.StartsWith("morphdb_audit_dropped_total{", StringComparison.Ordinal)).ToList();

        dropped.Should().Contain(l => l.Contains("reason=\"queue_overflow\"", StringComparison.Ordinal),
            "an operator alerts on the reason that says the queue overflowed; scrape output was:\n{0}", body);
        dropped.Should().Contain(l => l.Contains("reason=\"write_failed\"", StringComparison.Ordinal));
    }
}
