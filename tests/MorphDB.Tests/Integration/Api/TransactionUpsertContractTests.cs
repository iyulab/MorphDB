using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A transaction's <c>UPSERT</c> is an upsert: it writes the row when its key is new and updates the
/// row that holds the key when one does — as <c>PUT /api/batch/data/{table}</c> does. The transaction
/// door ran it as an insert, so an upsert on an existing key failed the whole transaction.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class TransactionUpsertContractTests
{
    private static readonly string[] KeyColumns = ["code"];

    private readonly HttpClient _client;

    public TransactionUpsertContractTests(ApiIntegrationFixture fixture)
    {
        _client = fixture.Api.Client;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> CreateTableAsync()
    {
        var table = $"txupsert_{Guid.NewGuid():N}"[..30];
        var created = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = false, Unique = true },
                new CreateColumnApiRequest { Name = "qty", Type = "integer" },
            ],
        }, Ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        return table;
    }

    private async Task<TransactionApiResponse> UpsertAsync(string table, string code, int qty)
    {
        var response = await _client.PostAsJsonAsync("/api/batch/transaction", new
        {
            operations = new[]
            {
                new { method = "UPSERT", table, keyColumns = KeyColumns, data = new Dictionary<string, object?> { ["code"] = code, ["qty"] = qty } },
            },
        }, Ct);
        return (await response.Content.ReadFromJsonAsync<TransactionApiResponse>(Ct))!;
    }

    private async Task<IReadOnlyList<(Guid Id, int Qty)>> RowsAsync(string table)
    {
        var response = await _client.GetAsync($"/api/data/{table}?filter=code:eq:k", Ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return [.. body.RootElement.GetProperty("data").EnumerateArray()
            .Select(r => (r.GetProperty("id").GetGuid(), r.GetProperty("data").GetProperty("qty").GetInt32()))];
    }

    [Fact]
    public async Task An_upsert_on_a_new_key_writes_the_row()
    {
        var table = await CreateTableAsync();

        var result = await UpsertAsync(table, "k", 1);

        result.Success.Should().BeTrue(result.Error);
        (await RowsAsync(table)).Select(r => r.Qty).Should().Equal(1);
    }

    [Fact]
    public async Task An_upsert_on_an_existing_key_updates_that_row()
    {
        var table = await CreateTableAsync();
        var first = await UpsertAsync(table, "k", 1);
        first.Success.Should().BeTrue(first.Error);

        var second = await UpsertAsync(table, "k", 2);

        second.Success.Should().BeTrue(second.Error ?? string.Join("; ", second.Results.Select(r => r.Error)));
        var rows = await RowsAsync(table);
        rows.Should().ContainSingle().Which.Qty.Should().Be(2);
        rows[0].Id.Should().Be(first.Results[0].Id!.Value, "the row keeps its id");
    }
}
