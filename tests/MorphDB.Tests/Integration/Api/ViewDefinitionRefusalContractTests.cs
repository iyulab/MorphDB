using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A view definition PostgreSQL cannot resolve is the caller's mistake and is answered as one: the join
/// condition or a column expression names something the view does not read (a column no table has, a
/// quoted identifier the translator does not read), so the request is refused with
/// <c>400 INVALID_EXPRESSION</c> -- not the opaque <c>500</c> the database error used to become. The
/// answer says what kind of refusal it was and carries none of PostgreSQL's text or a physical name.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ViewDefinitionRefusalContractTests
{
    private readonly HttpClient _client;

    public ViewDefinitionRefusalContractTests(ApiIntegrationFixture fixture) => _client = fixture.Api.Client;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("{o}.no_such_column - {c}.at", "an expression naming a column no table has")]
    [InlineData("{c}.\"no_such_column\" - {o}.\"at\"", "a table-qualified quoted column no table has")]
    public async Task A_view_expression_that_names_what_the_view_does_not_read_is_a_400_not_a_500(string expressionTemplate, string because)
    {
        var (orders, customers) = await SeedTablesAsync();
        var expression = expressionTemplate.Replace("{o}", orders, StringComparison.Ordinal).Replace("{c}", customers, StringComparison.Ordinal);

        var response = await CreateViewAsync($"bad_{Guid.NewGuid():N}"[..20], orders, customers, expression);
        var text = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"{because}: {text}");
        JsonDocument.Parse(text).RootElement.GetProperty("code").GetString().Should().Be("INVALID_EXPRESSION");
        PhysicalNameGuard.FindPhysicalNames(text).Should().BeEmpty("the refusal must not carry a physical name");
    }

    [Fact]
    public async Task A_definition_that_replaces_a_working_one_with_a_bad_expression_is_refused_and_the_view_keeps_working()
    {
        var (orders, customers) = await SeedTablesAsync();
        var view = $"keep_{Guid.NewGuid():N}"[..20];
        (await CreateViewAsync(view, orders, customers, $"{customers}.at - {orders}.at")).StatusCode.Should().Be(HttpStatusCode.Created);

        var patch = await _client.PatchAsync($"/api/views/{view}", new StringContent($$"""
            {
              "columns": [ { "expression": "{{orders}}.no_such_column", "alias": "x", "dataType": "decimal" } ],
              "joins": [ { "table": "{{customers}}", "condition": "{{orders}}.ref = {{customers}}.ref" } ]
            }
            """, Encoding.UTF8, "application/json"), Ct);
        var text = await patch.Content.ReadAsStringAsync(Ct);

        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest, text);
        JsonDocument.Parse(text).RootElement.GetProperty("code").GetString().Should().Be("INVALID_EXPRESSION");
        (await _client.GetAsync($"/api/views/{view}/data", Ct)).StatusCode.Should().Be(HttpStatusCode.OK,
            "a refused replacement leaves the view as it was");
    }

    /// <summary>
    /// Two more definition mistakes that used to pass for fine or surface as a 500: a filter value that is
    /// not of its column's type (PostgreSQL refuses it when the view is created), and a negative limit
    /// (PostgreSQL creates the view and fails every read of it).
    /// </summary>
    [Theory]
    [InlineData("\"filters\": [ { \"field\": \"at\", \"operator\": \"eq\", \"value\": \"abc\" } ]", "a filter value that is not of its column's type")]
    [InlineData("\"limit\": -5", "a negative limit")]
    public async Task A_view_definition_with_a_filter_value_or_limit_it_cannot_honour_is_a_400(string clause, string because)
    {
        var (orders, _) = await SeedTablesAsync();

        var response = await _client.PostAsync("/api/views", new StringContent($$"""
            { "name": "{{$"bad_{Guid.NewGuid():N}"[..20]}}", "baseTable": "{{orders}}", "columns": [ { "source": "at", "alias": "at" } ], {{clause}} }
            """, Encoding.UTF8, "application/json"), Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"{because}: {text}");
        PhysicalNameGuard.FindPhysicalNames(text).Should().BeEmpty("the refusal must not carry a physical name");
    }

    private Task<HttpResponseMessage> CreateViewAsync(string view, string orders, string customers, string expression) =>
        _client.PostAsync("/api/views", new StringContent($$"""
            {
              "name": "{{view}}",
              "baseTable": "{{orders}}",
              "columns": [
                { "source": "ref", "alias": "ref" },
                { "expression": "{{expression.Replace("\"", "\\\"", StringComparison.Ordinal)}}", "alias": "delta", "dataType": "decimal" }
              ],
              "joins": [ { "table": "{{customers}}", "condition": "{{orders}}.ref = {{customers}}.ref" } ]
            }
            """, Encoding.UTF8, "application/json"), Ct);

    private async Task<(string Orders, string Customers)> SeedTablesAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (orders, customers) = ($"vo_{suffix}", $"vc_{suffix}");
        foreach (var table in new[] { orders, customers })
        {
            (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
            {
                Name = table,
                Columns =
                [
                    new CreateColumnApiRequest { Name = "ref", Type = "text", Nullable = true },
                    new CreateColumnApiRequest { Name = "at", Type = "integer", Nullable = true },
                ],
            }, Ct)).EnsureSuccessStatusCode();
            (await _client.PostAsJsonAsync($"/api/data/{table}", new Dictionary<string, object?> { ["ref"] = "r1", ["at"] = 5 }, Ct)).EnsureSuccessStatusCode();
        }

        return (orders, customers);
    }
}
