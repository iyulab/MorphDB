using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// An input a read does not understand is refused, not read as no input. The narrowing ones are the dangerous
/// kind: a parameter spelled <c>fliter</c> or an order direction written <c>sideways</c> changes nothing about
/// the answer, so the caller gets every row, in some order, with a <c>200</c> and no way to tell. A query body already
/// refuses a member it does not know; the same sentence holds for the list and the view read and for OData's
/// <c>$orderby</c>.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ReadInputsContractTests
{
    private readonly HttpClient _client;

    public ReadInputsContractTests(ApiIntegrationFixture fixture) => _client = fixture.Api.Client;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("fliter=code:eq:c1", "fliter")]
    [InlineData("filters=code:eq:c1", "filters")]
    [InlineData("pagesize=2&limit=1", "limit")]
    public async Task A_row_list_refuses_a_parameter_it_does_not_have_naming_it(string query, string unknown)
    {
        var (table, _) = await SeedAsync();

        var response = await _client.GetAsync($"/api/data/{table}?{query}", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, text);
        var error = JsonDocument.Parse(text).RootElement;
        error.GetProperty("code").GetString().Should().Be("INVALID_ARGUMENT");
        error.GetProperty("message").GetString().Should().Contain(unknown).And.Contain("pageSize", "it lists what the list does take");
    }

    [Fact]
    public async Task Every_parameter_a_row_list_takes_is_accepted_together()
    {
        var (table, _) = await SeedAsync();

        var response = await _client.GetAsync(
            $"/api/data/{table}?filter={Uri.EscapeDataString("qty:gte:1")}&orderBy=qty:desc&select=code,qty&search=c&state=all&includeDeleted=true&page=1&pageSize=3&offset=0&count=true", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_view_read_refuses_a_parameter_it_does_not_have_and_a_negative_window()
    {
        var (_, view) = await SeedAsync();

        foreach (var query in new[] { "fliter=code:eq:c1", "limit=1", "take=-1", "skip=-1" })
        {
            var response = await _client.GetAsync($"/api/views/{view}/data?{query}", Ct);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, query);
            JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("code").GetString()
                .Should().Be("INVALID_ARGUMENT", query);
        }

        var zero = await _client.GetAsync($"/api/views/{view}/data?take=0&skip=0&select=code&orderBy=code:desc&filter={Uri.EscapeDataString("qty:gt:0")}", Ct);
        zero.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonDocument.Parse(await zero.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("data").GetArrayLength().Should().Be(0, "take=0 is no rows");
    }

    [Theory]
    [InlineData("code sideways")]
    [InlineData("code asc desc")]
    public async Task An_odata_orderby_direction_it_cannot_read_is_refused(string orderBy)
    {
        var (table, _) = await SeedAsync();

        var response = await _client.GetAsync($"/odata/{EntitySet(table)}?$orderby={Uri.EscapeDataString(orderBy)}", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, text);
        JsonDocument.Parse(text).RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("INVALID_ARGUMENT");
    }

    [Fact]
    public async Task An_odata_orderby_of_several_columns_with_directions_is_read()
    {
        var (table, _) = await SeedAsync();

        var response = await _client.GetAsync($"/odata/{EntitySet(table)}?$orderby={Uri.EscapeDataString("qty desc, code asc")}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("value");
        rows[0].GetProperty("qty").GetInt32().Should().Be(6);
    }

    private static string EntitySet(string table) =>
        string.Concat(table.Split('_').Select(p => p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant() : p));

    private async Task<(string Table, string View)> SeedAsync()
    {
        var table = $"rdin_{Guid.NewGuid():N}"[..30];
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = true },
                new CreateColumnApiRequest { Name = "qty", Type = "integer", Nullable = true },
            ],
        }, Ct)).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync($"/api/batch/data/{table}/insert",
            Enumerable.Range(1, 6).Select(i => new Dictionary<string, object?> { ["code"] = $"c{i}", ["qty"] = i }).ToArray(), Ct)).EnsureSuccessStatusCode();
        var view = $"v_{table}"[..30];
        (await _client.PostAsJsonAsync("/api/views", new CreateViewApiRequest { Name = view, BaseTable = table }, Ct)).EnsureSuccessStatusCode();
        return (table, view);
    }
}
