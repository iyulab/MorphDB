using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// On a table created with soft delete, a delete marks the row instead of removing it — and that has
/// to hold for the bulk delete by filter as it holds for the delete by id. The bulk route used to
/// remove the rows outright, so the same table lost rows one way and kept them the other.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class SoftDeleteBatchContractTests
{
    private readonly HttpClient _client;

    public SoftDeleteBatchContractTests(ApiIntegrationFixture fixture)
    {
        _client = fixture.Api.Client;
    }

    private async Task<string> CreateTableAsync(bool softDelete, CancellationToken ct)
    {
        var table = $"softdel_{Guid.NewGuid():N}"[..30];
        var created = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns = [new CreateColumnApiRequest { Name = "kind", Type = "text", Nullable = false }],
            SystemColumns = new SystemColumnOptionsApiRequest { SoftDelete = softDelete },
        }, ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(ct));
        return table;
    }

    private async Task<Guid> InsertAsync(string table, string kind, CancellationToken ct)
    {
        var response = await _client.PostAsJsonAsync($"/api/data/{table}", new Dictionary<string, object?> { ["kind"] = kind }, ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<DataRecordResponse>(ct))!.Id;
    }

    private async Task<int> BulkDeleteAsync(string table, string filter, CancellationToken ct)
    {
        var response = await _client.DeleteAsync($"/api/batch/data/{table}?filter={filter}", ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<BatchResponse>(ct))!.Results[0].AffectedRows ?? 0;
    }

    /// <summary>What a later read of the row says: its status, and whether it carries a deletion mark.</summary>
    private async Task<(HttpStatusCode Status, bool Marked)> ReadBackAsync(string table, Guid id, CancellationToken ct)
    {
        var response = await _client.GetAsync($"/api/data/{table}/{id}", ct);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return (response.StatusCode, false);
        }

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var data = body.RootElement.GetProperty("data");
        var marked = data.TryGetProperty("_deleted_at", out var at) && at.ValueKind != JsonValueKind.Null;
        return (response.StatusCode, marked);
    }

    [Fact]
    public async Task A_bulk_delete_on_a_soft_delete_table_leaves_rows_as_a_delete_by_id_does()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await CreateTableAsync(softDelete: true, ct);
        var byId = await InsertAsync(table, "gone", ct);
        var byFilter = await InsertAsync(table, "gone", ct);
        var kept = await InsertAsync(table, "kept", ct);

        (await _client.DeleteAsync($"/api/data/{table}/{byId}", ct)).IsSuccessStatusCode.Should().BeTrue();
        var single = await ReadBackAsync(table, byId, ct);

        (await BulkDeleteAsync(table, "kind:eq:gone", ct)).Should().Be(1, "the row deleted by id is already marked and is not deleted again");

        (await ReadBackAsync(table, byFilter, ct)).Should().Be(single, "a row deleted by filter ends as a row deleted by id does");
        (await ReadBackAsync(table, kept, ct)).Should().Be((HttpStatusCode.OK, false));
    }

    [Fact]
    public async Task A_second_bulk_delete_of_the_same_rows_deletes_none()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await CreateTableAsync(softDelete: true, ct);
        await InsertAsync(table, "gone", ct);
        await InsertAsync(table, "gone", ct);

        (await BulkDeleteAsync(table, "kind:eq:gone", ct)).Should().Be(2);
        (await BulkDeleteAsync(table, "kind:eq:gone", ct)).Should().Be(0);
    }

    [Fact]
    public async Task A_bulk_delete_on_a_table_without_soft_delete_still_removes_the_rows()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await CreateTableAsync(softDelete: false, ct);
        var gone = await InsertAsync(table, "gone", ct);

        (await BulkDeleteAsync(table, "kind:eq:gone", ct)).Should().Be(1);
        (await ReadBackAsync(table, gone, ct)).Status.Should().Be(HttpStatusCode.NotFound);
    }
}
