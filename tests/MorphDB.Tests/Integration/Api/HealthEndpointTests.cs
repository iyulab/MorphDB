using System.Net;
using System.Text.Json;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The three health endpoints as an orchestrator sees them: which one carries the dependency
/// checks, which one carries none, and the document each answers with. The database check must
/// probe the database the host is actually wired to — the fixture swaps the data source, and a
/// check bound to some other connection string would report a machine this host never talks to.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class HealthEndpointTests(ApiIntegrationFixture fixture)
{
    [Fact]
    public async Task Health_reports_the_database_check_healthy_against_the_wired_data_source()
    {
        using var response = await fixture.Api.Client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = document.RootElement;
        Assert.Equal("Healthy", root.GetProperty("status").GetString());
        Assert.True(TimeSpan.TryParse(root.GetProperty("totalDuration").GetString(), System.Globalization.CultureInfo.InvariantCulture, out _));

        var postgres = root.GetProperty("entries").GetProperty("postgresql");
        Assert.Equal("Healthy", postgres.GetProperty("status").GetString());
        Assert.Equal(["db", "ready"], postgres.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToArray());
        Assert.False(root.GetProperty("entries").TryGetProperty("redis", out _), "no Redis is configured, so no Redis check may be registered");
    }

    [Fact]
    public async Task Readiness_carries_only_the_checks_tagged_ready()
    {
        using var response = await fixture.Api.Client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var names = document.RootElement.GetProperty("entries").EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(["postgresql"], names);
    }

    [Fact]
    public async Task Liveness_carries_no_dependency_checks()
    {
        using var response = await fixture.Api.Client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Healthy", document.RootElement.GetProperty("status").GetString());
        Assert.Empty(document.RootElement.GetProperty("entries").EnumerateObject());
    }
}
