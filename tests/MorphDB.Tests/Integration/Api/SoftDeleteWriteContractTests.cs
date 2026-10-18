using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Controllers;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A row a soft delete has marked is not there to be written either: an update or a delete by id
/// answers <c>404</c> as a read of it does, and a bulk update does not touch it. Writes used to change
/// marked rows that no read would show. And an upsert that lands on a row updates it in place — it
/// used to give the row the id generated for the insert.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class SoftDeleteWriteContractTests
{
    private readonly HttpClient _client;

    public SoftDeleteWriteContractTests(ApiIntegrationFixture fixture)
    {
        _client = fixture.Api.Client;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> CreateTableAsync()
    {
        var table = $"softwrite_{Guid.NewGuid():N}"[..30];
        var created = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = false, Unique = true },
                new CreateColumnApiRequest { Name = "qty", Type = "integer" },
            ],
            SystemColumns = new SystemColumnOptionsApiRequest { SoftDelete = true },
        }, Ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        return table;
    }

    private async Task<Guid> InsertAsync(string table, string code, int qty)
    {
        var response = await _client.PostAsJsonAsync($"/api/data/{table}", new Dictionary<string, object?> { ["code"] = code, ["qty"] = qty }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<DataRecordResponse>(Ct))!.Id;
    }

    private async Task<Guid> InsertAndDeleteAsync(string table, string code, int qty)
    {
        var id = await InsertAsync(table, code, qty);
        (await _client.DeleteAsync($"/api/data/{table}/{id}", Ct)).IsSuccessStatusCode.Should().BeTrue();
        return id;
    }

    /// <summary>The stored qty of a row, deleted or not.</summary>
    private async Task<int> StoredQtyAsync(string table, Guid id)
    {
        var response = await _client.GetAsync($"/api/data/{table}?includeDeleted=true&filter=_id:eq:{id}", Ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return body.RootElement.GetProperty("data")[0].GetProperty("data").GetProperty("qty").GetInt32();
    }

    [Fact]
    public async Task An_update_by_id_of_a_deleted_row_is_not_found_and_changes_nothing()
    {
        var table = await CreateTableAsync();
        var gone = await InsertAndDeleteAsync(table, "gone", 1);

        var response = await _client.PatchAsJsonAsync($"/api/data/{table}/{gone}", new Dictionary<string, object?> { ["qty"] = 9 }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync(Ct));
        (await StoredQtyAsync(table, gone)).Should().Be(1);
    }

    [Fact]
    public async Task A_second_delete_by_id_is_not_found()
    {
        var table = await CreateTableAsync();
        var gone = await InsertAndDeleteAsync(table, "gone", 1);

        (await _client.DeleteAsync($"/api/data/{table}/{gone}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_bulk_update_leaves_deleted_rows_as_they_were()
    {
        var table = await CreateTableAsync();
        var kept = await InsertAsync(table, "kept", 1);
        var gone = await InsertAndDeleteAsync(table, "gone", 1);

        var response = await _client.PatchAsJsonAsync($"/api/batch/data/{table}", new BulkUpdateRequest
        {
            Data = new Dictionary<string, object?> { ["qty"] = 9 },
            Filter = ["qty:eq:1"],
        }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));

        (await response.Content.ReadFromJsonAsync<BatchResponse>(Ct))!.Results[0].AffectedRows.Should().Be(1);
        (await StoredQtyAsync(table, kept)).Should().Be(9);
        (await StoredQtyAsync(table, gone)).Should().Be(1);
    }

    [Fact]
    public async Task An_upsert_that_updates_a_row_keeps_its_id_and_creation_time()
    {
        var table = await CreateTableAsync();
        var id = await InsertAsync(table, "live", 1);
        var before = await _client.GetFromJsonAsync<DataRecordResponse>($"/api/data/{table}/{id}", Ct);

        var response = await _client.PutAsJsonAsync($"/api/batch/data/{table}", new UpsertRequest
        {
            Data = new Dictionary<string, object?> { ["code"] = "live", ["qty"] = 9 },
            KeyColumns = ["code"],
        }, Ct);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync(Ct));

        var after = (await response.Content.ReadFromJsonAsync<DataRecordResponse>(Ct))!;
        after.Id.Should().Be(id);
        after.Data["_created_at"]!.ToString().Should().Be(before!.Data["_created_at"]!.ToString());
        (await StoredQtyAsync(table, id)).Should().Be(9);
    }
}
