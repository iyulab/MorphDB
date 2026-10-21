using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Client;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// On a table created with soft delete, a deleted row is marked, and every read of the table sees
/// only the rows that are not — a list, its total, a filter, a single read, an aggregate and a
/// hierarchy walk alike. The reads used to return marked rows as if nothing had happened: the delete
/// marked them and nothing read the mark.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class SoftDeleteReadContractTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;

    public SoftDeleteReadContractTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
    }

    private sealed record Rows(string Table, Guid Kept, Guid Gone);

    /// <summary>A soft-delete table with a row kept and a row deleted by id.</summary>
    private async Task<Rows> CreateRowsAsync(CancellationToken ct, bool hierarchy = false)
    {
        var table = $"softread_{Guid.NewGuid():N}"[..30];
        var created = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns = [new CreateColumnApiRequest { Name = "kind", Type = "text", Nullable = false }],
            SystemColumns = new SystemColumnOptionsApiRequest { SoftDelete = true, Hierarchy = hierarchy },
        }, ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(ct));

        var kept = await InsertAsync(table, "kept", parent: null, ct);
        var gone = await InsertAsync(table, "gone", parent: hierarchy ? kept : null, ct);
        (await _client.DeleteAsync($"/api/data/{table}/{gone}", ct)).IsSuccessStatusCode.Should().BeTrue();
        return new Rows(table, kept, gone);
    }

    private async Task<Guid> InsertAsync(string table, string kind, Guid? parent, CancellationToken ct)
    {
        var row = new Dictionary<string, object?> { ["kind"] = kind };
        if (parent is not null)
        {
            row["_parent_id"] = parent;
        }

        var response = await _client.PostAsJsonAsync($"/api/data/{table}", row, ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<DataRecordResponse>(ct))!.Id;
    }

    private async Task<(IReadOnlyList<Guid> Ids, long Total)> ListAsync(string url, CancellationToken ct)
    {
        var response = await _client.GetAsync(url + (url.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "count=true", ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        var page = (await response.Content.ReadFromJsonAsync<PagedResponse<DataRecordResponse>>(ct))!;
        return ([.. page.Data.Select(r => r.Id)], page.Pagination.TotalCount!.Value);
    }

    [Fact]
    public async Task A_list_and_its_total_leave_out_a_deleted_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var r = await CreateRowsAsync(ct);

        var (ids, total) = await ListAsync($"/api/data/{r.Table}", ct);

        ids.Should().Equal(r.Kept);
        total.Should().Be(1);
    }

    [Fact]
    public async Task A_filter_that_matches_only_a_deleted_row_finds_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var r = await CreateRowsAsync(ct);

        var (ids, total) = await ListAsync($"/api/data/{r.Table}?filter=kind:eq:gone", ct);

        ids.Should().BeEmpty();
        total.Should().Be(0);
    }

    [Fact]
    public async Task A_deleted_row_read_by_id_is_not_found()
    {
        var ct = TestContext.Current.CancellationToken;
        var r = await CreateRowsAsync(ct);

        (await _client.GetAsync($"/api/data/{r.Table}/{r.Gone}", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.GetAsync($"/api/data/{r.Table}/{r.Kept}", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_complex_query_leaves_out_a_deleted_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var r = await CreateRowsAsync(ct);

        var response = await _client.PostAsJsonAsync($"/api/data/{r.Table}/query", new ComplexQueryApiRequest(), ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        var page = (await response.Content.ReadFromJsonAsync<PagedResponse<DataRecordResponse>>(ct))!;

        page.Data.Select(d => d.Id).Should().Equal(r.Kept);
    }

    [Fact]
    public async Task Asking_for_deleted_rows_returns_them_with_their_mark()
    {
        var ct = TestContext.Current.CancellationToken;
        var r = await CreateRowsAsync(ct);

        var response = await _client.GetAsync($"/api/data/{r.Table}?includeDeleted=true&count=true", ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var marks = body.RootElement.GetProperty("data").EnumerateArray()
            .ToDictionary(
                row => row.GetProperty("id").GetGuid(),
                row => row.GetProperty("data").GetProperty("_deleted_at").ValueKind != JsonValueKind.Null);

        marks.Should().BeEquivalentTo(new Dictionary<Guid, bool> { [r.Kept] = false, [r.Gone] = true });
        body.RootElement.GetProperty("pagination").GetProperty("totalCount").GetInt64().Should().Be(2);

        var query = await _client.PostAsJsonAsync($"/api/data/{r.Table}/query", new ComplexQueryApiRequest { IncludeDeleted = true }, ct);
        query.StatusCode.Should().Be(HttpStatusCode.OK, await query.Content.ReadAsStringAsync(ct));
        (await query.Content.ReadFromJsonAsync<PagedResponse<DataRecordResponse>>(ct))!.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task An_aggregate_counts_only_the_rows_that_exist()
    {
        var ct = TestContext.Current.CancellationToken;
        var r = await CreateRowsAsync(ct);

        var response = await _client.PostAsJsonAsync($"/api/data/{r.Table}/aggregate", new AggregationApiRequest
        {
            Aggregations = [new AggregationColumnApiRequest { Function = "count", Alias = "total" }],
        }, ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

        body.RootElement.GetProperty("data")[0].GetProperty("total").GetInt64().Should().Be(1);
    }

    [Fact]
    public async Task A_hierarchy_walk_does_not_reach_a_deleted_child()
    {
        var ct = TestContext.Current.CancellationToken;
        var r = await CreateRowsAsync(ct, hierarchy: true);

        var response = await _client.GetAsync($"/api/hierarchy/{r.Table}/{r.Kept}/descendants", ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

        body.RootElement.GetProperty("records").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task The_client_reads_only_the_rows_that_exist_unless_it_asks_for_deleted_ones()
    {
        var ct = TestContext.Current.CancellationToken;
        var r = await CreateRowsAsync(ct);
        var http = new HttpClient(_fixture.Api.CreateHandler()) { BaseAddress = _fixture.Api.BaseAddress };
        await using var client = new MorphDBClient(http);
        client.SetProjectId(_fixture.Api.ProjectId);

        var live = await client.Data.QueryAsync(r.Table, new MorphDB.Client.Models.QueryRequest(), ct);
        var all = await client.Data.QueryAsync(r.Table, new MorphDB.Client.Models.QueryRequest { IncludeDeleted = true }, ct);

        live.Data.Select(d => d.Id).Should().Equal(r.Kept);
        all.Data.Select(d => d.Id).Should().BeEquivalentTo([r.Kept, r.Gone]);
    }
}
