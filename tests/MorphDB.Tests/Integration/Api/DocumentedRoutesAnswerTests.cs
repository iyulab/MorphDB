using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;
using MorphDB.Tests.Unit;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// <see cref="DocsRouteParityTests"/> holds that every documented route exists; this holds that each
/// documented read route <em>answers</em>. The two changelog routes were documented, routed, and had
/// answered <c>500</c> on every call since they were added — existence was checked and nobody ever
/// called them.
/// <para>
/// Every documented <c>GET</c> is called once, with its parameters filled from a table and a row made
/// for the purpose. The answer may be anything a caller can act on — a <c>404</c> for an example name
/// the docs use, a <c>503</c> the docs describe — but not an unhandled failure. Write routes need a
/// body shaped to each one, and their contracts are held by their own tests.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class DocumentedRoutesAnswerTests
{
    private readonly ApiIntegrationFixture _fixture;

    public DocumentedRoutesAnswerTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Every_documented_read_route_answers_without_an_unhandled_failure()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _fixture.Api.Client;
        var table = $"route_{Guid.NewGuid():N}"[..24];
        (await client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns = [new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = true }]
        }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        var insert = await client.PostAsJsonAsync($"/api/data/{table}", new Dictionary<string, object?> { ["label"] = "a" }, ct);
        insert.StatusCode.Should().Be(HttpStatusCode.Created);
        var rowId = JsonDocument.Parse(await insert.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("id").GetString()!;

        var reads = DocsRouteParityTests.DocumentedRoutes()
            .Where(r => r.StartsWith("GET ", StringComparison.Ordinal))
            .Select(r => r["GET ".Length..])
            .ToList();
        reads.Should().Contain("/api/schema/changelog", "the gate must at least reach the routes that prompted it");

        var failures = new List<string>();
        foreach (var route in reads)
        {
            var path = route
                .Replace("{table}", table, StringComparison.Ordinal)
                .Replace("{tableName}", table, StringComparison.Ordinal)
                .Replace("{name}", table, StringComparison.Ordinal)
                .Replace("{id}", rowId, StringComparison.Ordinal)
                .Replace("{jobId}", Guid.NewGuid().ToString(), StringComparison.Ordinal);

            var response = await client.GetAsync(path, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode == HttpStatusCode.InternalServerError || body.Contains("\"INTERNAL_ERROR\"", StringComparison.Ordinal))
            {
                failures.Add($"GET {path} -> {(int)response.StatusCode} {body[..Math.Min(200, body.Length)]}");
            }
        }

        // Joined so one run names every route that fails, not the first.
        string.Join(Environment.NewLine, failures).Should().BeEmpty(
            "a documented route that answers with an unhandled failure is a contract nobody can use");
    }
}
