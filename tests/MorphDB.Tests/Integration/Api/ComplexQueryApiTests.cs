using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// Pins the wire shape of <c>POST /api/data/{table}/query</c> — the endpoint docs/API.md points
/// filter-tree consumers at, which until now had no coverage and no body documentation. The JSON
/// bodies here are the exact examples the docs carry ("문서 예제 실행 계약 테스트"): if the wire
/// contract drifts, this suite and the docs go stale together, loudly.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ComplexQueryApiTests
{
    private readonly HttpClient _client;

    public ComplexQueryApiTests(ApiIntegrationFixture fixture)
    {
        _client = fixture.Api.Client;
    }

    private async Task<string> SetupAsync()
    {
        var tableName = $"cq_test_{Guid.NewGuid():N}"[..25];
        var create = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns =
            [
                new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "grade", Type = "text", Nullable = true },
                new CreateColumnApiRequest { Name = "amount", Type = "integer", Nullable = true },
            ],
        });
        create.EnsureSuccessStatusCode();

        foreach (var (name, grade, amount) in new[]
                 { ("Acme", "vip", 80), ("Globex", "vip", 30), ("Initech", "basic", 90) })
        {
            var insert = await _client.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
            {
                ["name"] = name,
                ["grade"] = grade,
                ["amount"] = amount,
            });
            insert.EnsureSuccessStatusCode();
        }

        return tableName;
    }

    private async Task<JsonElement> QueryAsync(string tableName, string body)
    {
        var response = await _client.PostAsync($"/api/data/{tableName}/query",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task A_single_condition_filters_the_rows()
    {
        var tableName = await SetupAsync();

        var result = await QueryAsync(tableName,
            """{"filter":{"$type":"condition","column":"grade","operator":"eq","value":"vip"},"pageSize":10}""");

        result.GetProperty("data").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task An_and_group_with_orderby_select_and_paging_answers_the_documented_shape()
    {
        var tableName = await SetupAsync();

        // The exact example docs/API.md carries for this endpoint.
        var result = await QueryAsync(tableName,
            """
            {
              "filter": {
                "$type": "group",
                "logic": "and",
                "filters": [
                  { "$type": "condition", "column": "grade", "operator": "eq", "value": "vip" },
                  { "$type": "condition", "column": "amount", "operator": "gte", "value": 50 }
                ]
              },
              "select": ["name", "amount"],
              "orderBy": ["amount:desc"],
              "page": 1,
              "pageSize": 10
            }
            """);

        var rows = result.GetProperty("data");
        rows.GetArrayLength().Should().Be(1);
        rows[0].GetProperty("data").GetProperty("name").GetString().Should().Be("Acme");
        result.GetProperty("pagination").GetProperty("totalCount").GetInt64().Should().Be(1);
    }

    [Fact]
    public async Task An_or_group_widens_the_match()
    {
        var tableName = await SetupAsync();

        var result = await QueryAsync(tableName,
            """
            {
              "filter": {
                "$type": "group",
                "logic": "or",
                "filters": [
                  { "$type": "condition", "column": "grade", "operator": "eq", "value": "basic" },
                  { "$type": "condition", "column": "amount", "operator": "gte", "value": 50 }
                ]
              },
              "pageSize": 10
            }
            """);

        result.GetProperty("data").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task An_unknown_operator_is_refused_with_the_supported_set()
    {
        var tableName = await SetupAsync();

        var response = await _client.PostAsync($"/api/data/{tableName}/query",
            new StringContent(
                """{"filter":{"$type":"condition","column":"grade","operator":"in","value":["vip"]},"pageSize":10}""",
                System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "'in' is not part of the accepted operator vocabulary on any surface — the docs must not promise it");
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Message.Should().Contain("Supported operators");
    }
}
