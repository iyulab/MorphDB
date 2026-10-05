using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A table with a lookup column is read through the data routes, each row carrying the looked-up value.
/// <para>
/// Every read of such a table used to fail: the lookup JOIN names the queried table <c>base_table</c>,
/// and the query never gave it that alias in FROM (SqlKata's <c>Query.As</c> names a subquery, it does
/// not alias a table), so PostgreSQL refused the statement as a missing FROM-clause entry and the route
/// answered <c>500</c>. Declaring one lookup column made the whole table unreadable.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public sealed class LookupReadTests
{
    private readonly ApiIntegrationFixture _fixture;

    public LookupReadTests(ApiIntegrationFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_table_with_a_lookup_column_reads_and_carries_the_looked_up_value()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _fixture.Api.Client;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var customers = $"lk_cust_{suffix}";
        var orders = $"lk_ord_{suffix}";

        (await client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = customers,
            Columns =
            [
                new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "grade", Type = "text", Nullable = true },
            ],
        }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        (await client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = orders,
            Columns =
            [
                new CreateColumnApiRequest { Name = "customer_id", Type = "uuid", Nullable = true },
                new CreateColumnApiRequest { Name = "status", Type = "text", Nullable = true },
                new CreateColumnApiRequest
                {
                    Name = "customer_grade",
                    Type = "text",
                    Nullable = true,
                    Lookup = new LookupConfigApiRequest { RelationColumn = "customer_id", TargetTable = customers, TargetColumn = "grade" },
                },
            ],
        }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        var customer = await client.PostAsJsonAsync($"/api/data/{customers}", new Dictionary<string, object?> { ["code"] = "C-1", ["grade"] = "A" }, ct);
        var customerId = (await customer.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString();

        (await client.PostAsJsonAsync($"/api/data/{orders}", new Dictionary<string, object?> { ["customer_id"] = customerId, ["status"] = "open" }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.PostAsJsonAsync($"/api/data/{orders}", new Dictionary<string, object?> { ["status"] = "closed" }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var read = await client.GetAsync($"/api/data/{orders}?orderBy=status:asc", ct);
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        using var page = JsonDocument.Parse(await read.Content.ReadAsStringAsync(ct));
        var rows = page.RootElement.GetProperty("data");
        rows.GetArrayLength().Should().Be(2);

        // "closed" sorts first: the order with no customer reads the lookup as null, not as a failure.
        rows[0].GetProperty("data").GetProperty("customer_grade").ValueKind.Should().Be(JsonValueKind.Null);
        rows[1].GetProperty("data").GetProperty("customer_grade").GetString().Should().Be("A");

        // A filter on an ordinary column of the same table goes through the aliased statement too.
        using var filtered = JsonDocument.Parse(await client.GetStringAsync($"/api/data/{orders}?filter=status:eq:open", ct));
        filtered.RootElement.GetProperty("data").GetArrayLength().Should().Be(1);
        filtered.RootElement.GetProperty("data")[0].GetProperty("data").GetProperty("customer_grade").GetString().Should().Be("A");
    }
}
