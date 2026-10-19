using System.Net;
using System.Net.Http.Json;
using System.Text;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A request that names one of a fixed set of choices — an index type, a relation type, a join type,
/// how a bulk import treats duplicates, which row state to list — and names something not in the set
/// is refused, naming what the set is. Each of these used to read an unknown value as its default: a
/// misspelt <c>many-to-many</c> declared a one-to-many relation with a physical foreign key, a misspelt
/// <c>skip</c> imported the duplicates it was meant to skip, and an unknown row state listed every row.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public sealed class UnknownChoiceContractTests
{
    private readonly HttpClient _client;

    public UnknownChoiceContractTests(ApiIntegrationFixture fixture) => _client = fixture.Api.Client;

    private async Task<(string Table, string Other)> TablesAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var s = Guid.NewGuid().ToString("N")[..8];
        var table = $"uc_a_{s}";
        var other = $"uc_b_{s}";
        foreach (var name in new[] { table, other })
        {
            (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
            {
                Name = name,
                Columns = [new CreateColumnApiRequest { Name = "label", Type = "text" }, new CreateColumnApiRequest { Name = "ref_id", Type = "uuid" }],
                SystemColumns = new SystemColumnOptionsApiRequest { RowState = true },
            }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        return (table, other);
    }

    private static async Task RefusedAsync(HttpResponseMessage response, string names)
    {
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, payload);
        payload.Should().Contain(names);
    }

    [Fact]
    public async Task An_unknown_index_type_is_refused()
    {
        var (table, _) = await TablesAsync();

        await RefusedAsync(await _client.PostAsJsonAsync($"/api/schema/tables/{table}/indexes",
            new CreateIndexApiRequest { Name = $"ix_{table}", Columns = ["label"], Type = "hsah" }, TestContext.Current.CancellationToken),
            "Unknown index type 'hsah'");
    }

    [Fact]
    public async Task An_unknown_relation_type_is_refused_and_nothing_is_declared()
    {
        var (table, other) = await TablesAsync();

        await RefusedAsync(await _client.PostAsJsonAsync("/api/schema/relations", new CreateRelationApiRequest
        {
            Name = $"rel_{table}",
            SourceTable = table,
            SourceColumn = "ref_id",
            TargetTable = other,
            TargetColumn = "_id",
            Type = "many-many",
        }, TestContext.Current.CancellationToken), "Unknown relation type 'many-many'");
    }

    [Fact]
    public async Task An_unknown_join_type_is_refused()
    {
        var (table, other) = await TablesAsync();

        await RefusedAsync(await _client.PostAsJsonAsync("/api/views", new CreateViewApiRequest
        {
            Name = $"v_{table}",
            BaseTable = table,
            Joins = [new ViewJoinApiSpec { Table = other, Condition = $"{table}.ref_id = {other}._id", JoinType = "outer" }],
        }, TestContext.Current.CancellationToken), "Unknown join type 'outer'");
    }

    [Fact]
    public async Task An_unknown_duplicate_handling_is_refused_before_anything_is_imported()
    {
        var ct = TestContext.Current.CancellationToken;
        var (table, _) = await TablesAsync();

        await RefusedAsync(await _client.PostAsync($"/api/bulk/{table}/import/json?duplicateHandling=skp",
            new StringContent("""[{"label":"x"}]""", Encoding.UTF8, "application/json"), ct), "Unknown duplicate handling 'skp'");

        var rows = await _client.GetFromJsonAsync<PagedResponse<DataRecordResponse>>($"/api/data/{table}", ct);
        rows!.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_row_state_is_refused_not_read_as_every_row()
    {
        var (table, _) = await TablesAsync();

        await RefusedAsync(await _client.GetAsync($"/api/data/{table}?state=drafts", TestContext.Current.CancellationToken), "Unknown row state 'drafts'");
    }
}
