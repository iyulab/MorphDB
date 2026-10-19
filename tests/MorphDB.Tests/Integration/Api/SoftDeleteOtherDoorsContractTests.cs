using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A deleted row of a soft-delete table is gone for GraphQL and OData too, not only for the REST
/// door the soft-delete contract was first written against: a list, its count, a filter, an
/// aggregate and a read by id all leave it out. Both doors read through the same data service, so
/// the property holds by construction today; these tests are what keeps it holding if a door ever
/// grows its own query.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class SoftDeleteOtherDoorsContractTests
{
    private readonly HttpClient _client;

    public SoftDeleteOtherDoorsContractTests(ApiIntegrationFixture fixture)
    {
        _client = fixture.Api.Client;
    }

    private sealed record Rows(string Table, Guid Kept, Guid Gone);

    /// <summary>A soft-delete table with a row kept and a row deleted by id.</summary>
    private async Task<Rows> CreateRowsAsync(CancellationToken ct)
    {
        var table = $"softdoor_{Guid.NewGuid():N}"[..30];
        var created = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns = [new CreateColumnApiRequest { Name = "kind", Type = "text", Nullable = false }],
            SystemColumns = new SystemColumnOptionsApiRequest { SoftDelete = true },
        }, ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(ct));

        var kept = await InsertAsync(table, "kept", ct);
        var gone = await InsertAsync(table, "gone", ct);
        (await _client.DeleteAsync($"/api/data/{table}/{gone}", ct)).IsSuccessStatusCode.Should().BeTrue();
        return new Rows(table, kept, gone);
    }

    private async Task<Guid> InsertAsync(string table, string kind, CancellationToken ct)
    {
        var response = await _client.PostAsJsonAsync($"/api/data/{table}", new Dictionary<string, object?> { ["kind"] = kind }, ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<DataRecordResponse>(ct))!.Id;
    }

    private async Task<JsonElement> PostGraphQlAsync(string query, object variables, CancellationToken ct)
    {
        var response = await _client.PostAsJsonAsync("/graphql", new { query, variables }, ct);
        var payload = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, payload);
        var root = JsonDocument.Parse(payload).RootElement.Clone();
        root.TryGetProperty("errors", out var errors).Should().BeFalse($"the read must resolve: {errors}");
        return root.GetProperty("data");
    }

    private async Task<JsonElement> GetODataAsync(string url, CancellationToken ct)
    {
        var response = await _client.GetAsync(url, ct);
        var payload = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, payload);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    private static IReadOnlyList<Guid> ODataIds(JsonElement root) =>
        [.. root.GetProperty("value").EnumerateArray().Select(row => row.GetProperty("_id").GetGuid())];

    private static string EntitySet(string table) =>
        string.Concat(table.Split('_').Select(p => p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant() : p));

    [Fact]
    public async Task A_graphql_list_and_its_total_leave_out_a_deleted_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var r = await CreateRowsAsync(ct);

        var records = (await PostGraphQlAsync(
            """
            query($table: String!) {
              records(table: $table, first: 10) { totalCount edges { node { id } } }
            }
            """,
            new { table = r.Table }, ct)).GetProperty("records");

        records.GetProperty("totalCount").GetInt32().Should().Be(1);
        records.GetProperty("edges").EnumerateArray().Select(e => e.GetProperty("node").GetProperty("id").GetGuid())
            .Should().Equal(r.Kept);
    }

    [Fact]
    public async Task A_deleted_row_read_by_id_through_graphql_is_null()
    {
        var ct = TestContext.Current.CancellationToken;
        var r = await CreateRowsAsync(ct);
        const string query =
            """
            query($table: String!, $id: UUID!) {
              record(table: $table, id: $id) { id }
            }
            """;

        (await PostGraphQlAsync(query, new { table = r.Table, id = r.Gone }, ct)).GetProperty("record").ValueKind
            .Should().Be(JsonValueKind.Null);
        (await PostGraphQlAsync(query, new { table = r.Table, id = r.Kept }, ct)).GetProperty("record").GetProperty("id").GetGuid()
            .Should().Be(r.Kept);
    }

    [Fact]
    public async Task A_graphql_aggregate_does_not_count_a_deleted_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var r = await CreateRowsAsync(ct);

        var data = (await PostGraphQlAsync(
            """
            query($table: String!) {
              aggregate(table: $table, aggregations: [{ function: COUNT, alias: "n" }]) { data }
            }
            """,
            new { table = r.Table }, ct)).GetProperty("aggregate").GetProperty("data");

        // One group (no groupBy), its count.
        data.GetArrayLength().Should().Be(1);
        data[0].GetProperty("n").GetInt64().Should().Be(1);
    }

    [Fact]
    public async Task An_odata_list_and_its_count_leave_out_a_deleted_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var r = await CreateRowsAsync(ct);

        var root = await GetODataAsync($"/odata/{EntitySet(r.Table)}?$count=true", ct);

        ODataIds(root).Should().Equal(r.Kept);
        root.GetProperty("@odata.count").GetInt64().Should().Be(1);
    }

    [Fact]
    public async Task An_odata_filter_that_matches_only_a_deleted_row_finds_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var r = await CreateRowsAsync(ct);

        var root = await GetODataAsync($"/odata/{EntitySet(r.Table)}?$filter=kind eq 'gone'", ct);

        ODataIds(root).Should().BeEmpty();
    }

    [Fact]
    public async Task A_deleted_row_read_by_key_through_odata_is_not_found()
    {
        var ct = TestContext.Current.CancellationToken;
        var r = await CreateRowsAsync(ct);
        var set = EntitySet(r.Table);

        (await _client.GetAsync($"/odata/{set}({r.Gone})", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.GetAsync($"/odata/{set}({r.Kept})", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
