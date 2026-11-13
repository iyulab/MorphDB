using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A value the database computes while a request is answered can fail for the rows it is computed for -- a
/// formula column or a view expression that divides by a zero. That is the data's (and the definition's)
/// error, not a fault of this service, so it is answered as a data exception (<c>400 DATA_EXCEPTION</c>)
/// that names the kind of error and nothing of PostgreSQL's text, instead of an opaque <c>500</c>.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class DataExceptionContractTests
{
    private readonly HttpClient _client;

    public DataExceptionContractTests(ApiIntegrationFixture fixture) => _client = fixture.Api.Client;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_view_expression_that_divides_by_zero_answers_a_data_exception_when_read()
    {
        var table = await SeedTableAsync();
        var view = $"dz_{Guid.NewGuid():N}"[..20];
        (await _client.PostAsync("/api/views", new StringContent($$"""
            { "name": "{{view}}", "baseTable": "{{table}}", "columns": [ { "expression": "qty / 0", "alias": "x", "dataType": "integer" } ] }
            """, Encoding.UTF8, "application/json"), Ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        await AssertDataExceptionAsync(await _client.GetAsync($"/api/views/{view}/data", Ct));
    }

    [Fact]
    public async Task A_formula_column_that_divides_by_zero_for_a_row_makes_the_read_a_data_exception()
    {
        var table = await SeedTableAsync();
        (await _client.PostAsJsonAsync($"/api/schema/tables/{table}/columns", new AddColumnApiRequest
        {
            Name = "ratio",
            Type = "decimal",
            Formula = new FormulaConfigApiRequest { Formula = "{qty} / {divisor}", ReturnType = "decimal" },
        }, Ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        // One row whose divisor is zero: the answer says so, as a data error and not as the service failing.
        await AssertDataExceptionAsync(await _client.GetAsync($"/api/data/{table}", Ct));
    }

    private static async Task AssertDataExceptionAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, text);
        var body = JsonElement.Parse(text);
        body.GetProperty("code").GetString().Should().Be("DATA_EXCEPTION");
        body.GetProperty("message").GetString().Should().Contain("division by zero");
        PhysicalNameGuard.FindPhysicalNames(text).Should().BeEmpty("the answer must not carry a physical name");
    }

    private async Task<string> SeedTableAsync()
    {
        var table = $"dx_{Guid.NewGuid():N}"[..20];
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "qty", Type = "integer", Nullable = true },
                new CreateColumnApiRequest { Name = "divisor", Type = "integer", Nullable = true },
            ],
        }, Ct)).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync($"/api/data/{table}", new Dictionary<string, object?> { ["qty"] = 10, ["divisor"] = 0 }, Ct)).EnsureSuccessStatusCode();
        return table;
    }
}
