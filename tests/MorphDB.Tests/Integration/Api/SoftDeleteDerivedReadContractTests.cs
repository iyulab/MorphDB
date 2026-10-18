using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A row a soft delete has marked is not there for the reads that reach it through another table
/// either: a lookup does not read it, a rollup does not count it, and a view does not show it. Each of
/// those builds its own SQL beside the table's own read, and each read marked rows as live.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class SoftDeleteDerivedReadContractTests
{
    private readonly HttpClient _client;

    public SoftDeleteDerivedReadContractTests(ApiIntegrationFixture fixture)
    {
        _client = fixture.Api.Client;
    }

    private async Task CreateTableAsync(string name, IReadOnlyList<CreateColumnApiRequest> columns, bool softDelete, CancellationToken ct)
    {
        var created = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = name,
            Columns = columns,
            SystemColumns = new SystemColumnOptionsApiRequest { SoftDelete = softDelete },
        }, ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(ct));
    }

    private async Task<Guid> InsertAsync(string table, Dictionary<string, object?> row, CancellationToken ct)
    {
        var response = await _client.PostAsJsonAsync($"/api/data/{table}", row, ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<DataRecordResponse>(ct))!.Id;
    }

    private async Task DeleteAsync(string table, Guid id, CancellationToken ct) =>
        (await _client.DeleteAsync($"/api/data/{table}/{id}", ct)).IsSuccessStatusCode.Should().BeTrue();

    private async Task AddColumnAsync(string table, AddColumnApiRequest column, CancellationToken ct)
    {
        var response = await _client.PostAsJsonAsync($"/api/schema/tables/{table}/columns", column, ct);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync(ct));
    }

    private async Task<JsonElement> ReadAsync(string table, Guid id, CancellationToken ct)
    {
        var response = await _client.GetAsync($"/api/data/{table}/{id}", ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return body.RootElement.GetProperty("data").Clone();
    }

    /// <summary>A soft-delete parent table and a child table pointing at it.</summary>
    private async Task<(string Parents, string Children)> CreatePairAsync(bool childrenSoftDelete, CancellationToken ct)
    {
        var s = Guid.NewGuid().ToString("N")[..8];
        var parents = $"sdp_{s}";
        var children = $"sdc_{s}";
        await CreateTableAsync(parents, [new CreateColumnApiRequest { Name = "code", Type = "text" }], softDelete: true, ct);
        await CreateTableAsync(children,
        [
            new CreateColumnApiRequest { Name = "parent_id", Type = "uuid" },
            new CreateColumnApiRequest { Name = "amount", Type = "integer" },
        ], softDelete: childrenSoftDelete, ct);
        return (parents, children);
    }

    [Fact]
    public async Task A_lookup_whose_target_row_is_deleted_reads_null()
    {
        var ct = TestContext.Current.CancellationToken;
        var (parents, children) = await CreatePairAsync(childrenSoftDelete: false, ct);
        var kept = await InsertAsync(parents, new() { ["code"] = "P-KEPT" }, ct);
        var gone = await InsertAsync(parents, new() { ["code"] = "P-GONE" }, ct);
        var toKept = await InsertAsync(children, new() { ["parent_id"] = kept, ["amount"] = 1 }, ct);
        var toGone = await InsertAsync(children, new() { ["parent_id"] = gone, ["amount"] = 1 }, ct);
        await AddColumnAsync(children, new AddColumnApiRequest
        {
            Name = "parent_code",
            Type = "text",
            Lookup = new LookupConfigApiRequest { RelationColumn = "parent_id", TargetTable = parents, TargetColumn = "code" },
        }, ct);

        await DeleteAsync(parents, gone, ct);

        (await ReadAsync(children, toKept, ct)).GetProperty("parent_code").GetString().Should().Be("P-KEPT");
        (await ReadAsync(children, toGone, ct)).GetProperty("parent_code").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_rollup_does_not_count_a_deleted_child()
    {
        var ct = TestContext.Current.CancellationToken;
        var (parents, children) = await CreatePairAsync(childrenSoftDelete: true, ct);
        var parent = await InsertAsync(parents, new() { ["code"] = "P" }, ct);
        await InsertAsync(children, new() { ["parent_id"] = parent, ["amount"] = 5 }, ct);
        var gone = await InsertAsync(children, new() { ["parent_id"] = parent, ["amount"] = 7 }, ct);
        await AddColumnAsync(parents, new AddColumnApiRequest
        {
            Name = "total",
            Type = "integer",
            Rollup = new RollupConfigApiRequest { Relation = children, TargetTable = children, ForeignKeyColumn = "parent_id", SourceColumn = "amount", Aggregation = "sum" },
        }, ct);

        await DeleteAsync(children, gone, ct);

        (await ReadAsync(parents, parent, ct)).GetProperty("total").GetDecimal().Should().Be(5);
    }

    [Fact]
    public async Task A_view_does_not_show_a_deleted_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var (parents, _) = await CreatePairAsync(childrenSoftDelete: false, ct);
        await InsertAsync(parents, new() { ["code"] = "kept" }, ct);
        await DeleteAsync(parents, await InsertAsync(parents, new() { ["code"] = "gone" }, ct), ct);

        var view = $"vw_sd_{Guid.NewGuid():N}"[..30];
        var created = await _client.PostAsJsonAsync("/api/views", new CreateViewApiRequest
        {
            Name = view,
            BaseTable = parents,
            Columns = [new ViewColumnApiSpec { Source = "code", Alias = "code" }],
        }, ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(ct));

        var rows = await ViewRowsAsync(view, ct);

        rows.Select(r => r.GetProperty("code").GetString()).Should().Equal("kept");
    }

    [Fact]
    public async Task A_view_with_two_filters_joined_by_or_reads_both()
    {
        var ct = TestContext.Current.CancellationToken;
        var (parents, _) = await CreatePairAsync(childrenSoftDelete: false, ct);
        await InsertAsync(parents, new() { ["code"] = "a" }, ct);
        await InsertAsync(parents, new() { ["code"] = "b" }, ct);
        await InsertAsync(parents, new() { ["code"] = "c" }, ct);
        await DeleteAsync(parents, await InsertAsync(parents, new() { ["code"] = "a" }, ct), ct);

        var view = $"vw_or_{Guid.NewGuid():N}"[..30];
        var created = await _client.PostAsJsonAsync("/api/views", new CreateViewApiRequest
        {
            Name = view,
            BaseTable = parents,
            Columns = [new ViewColumnApiSpec { Source = "code", Alias = "code" }],
            Filters =
            [
                new ViewFilterApiSpec { Field = "code", Operator = "eq", Value = "a" },
                new ViewFilterApiSpec { Field = "code", Operator = "eq", Value = "b", LogicalOp = "Or" },
            ],
        }, ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(ct));

        var rows = await ViewRowsAsync(view, ct);

        rows.Select(r => r.GetProperty("code").GetString()).Should().BeEquivalentTo(["a", "b"],
            "the OR is between the declared filters only — the deleted 'a' stays out");
    }

    private async Task<IReadOnlyList<JsonElement>> ViewRowsAsync(string view, CancellationToken ct)
    {
        var response = await _client.GetAsync($"/api/views/{view}/data", ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return [.. body.RootElement.GetProperty("data").EnumerateArray().Select(e => e.Clone())];
    }
}
