using System.Globalization;
using System.Net.Http.Json;
using MorphDB.Client;
using MorphDB.Client.Models;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Client;

/// <summary>
/// The client's aggregation over the real service: what it sends is what the service reads, and a
/// collected array comes back as a list of values.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class AggregationClientTests(ApiIntegrationFixture fixture)
{
    [Fact]
    public async Task An_array_aggregate_comes_back_beside_the_count_in_ascending_order_up_to_its_limit()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = $"agg_cli_{Guid.NewGuid():N}"[..30];
        var setup = fixture.Api.Client;
        (await setup.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "category", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "ref", Type = "text", Nullable = false },
            ],
        }, ct)).EnsureSuccessStatusCode();
        foreach (var (category, reference) in new[] { ("a", "r3"), ("a", "r1"), ("a", "r2"), ("b", "r9") })
        {
            (await setup.PostAsJsonAsync($"/api/data/{table}", new Dictionary<string, object?> { ["category"] = category, ["ref"] = reference }, ct)).EnsureSuccessStatusCode();
        }

        var http = new HttpClient(fixture.Api.CreateHandler()) { BaseAddress = fixture.Api.BaseAddress };
        await using var client = new MorphDBClient(http);
        client.SetProjectId(fixture.Api.ProjectId);

        var response = await client.Data.AggregateAsync(table, new AggregationRequest
        {
            Aggregations = [AggregationColumn.Count("n"), AggregationColumn.ArrayAgg("ref", "refs", limit: 2)],
            GroupBy = ["category"],
        }, ct);

        var a = response.Data.Single(row => Equals(row["category"], "a"));
        Assert.Equal(3L, Convert.ToInt64(a["n"], CultureInfo.InvariantCulture));
        Assert.Equal(["r1", "r2"], ((IEnumerable<object?>)a["refs"]!).Select(v => v?.ToString()));
        var b = response.Data.Single(row => Equals(row["category"], "b"));
        Assert.Equal(["r9"], ((IEnumerable<object?>)b["refs"]!).Select(v => v?.ToString()));
    }
}
