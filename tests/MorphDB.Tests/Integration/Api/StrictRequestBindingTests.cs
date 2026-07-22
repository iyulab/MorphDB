using System.Net;
using System.Net.Http.Json;
using System.Text;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The request-envelope half of fail-loud writes (issue complex-query-silently-ignores-unknown-body,
/// adopted as HANDOFF §3.10-B1). A JSON member a request DTO does not declare answered 200 with the
/// member silently dropped — live-probed: <c>{"filters": …}</c> against <c>/query</c> returned
/// every row with the filter ignored, a confidently wrong answer. It must be a 400 naming the
/// member and listing the supported ones, in the standard error envelope (not ProblemDetails).
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class StrictRequestBindingTests
{
    private readonly HttpClient _client;

    public StrictRequestBindingTests(ApiIntegrationFixture fixture)
    {
        _client = fixture.Api.Client;
    }

    private async Task<string> CreateTableAsync()
    {
        var tableName = $"strict_{Guid.NewGuid():N}"[..25];
        var response = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns = [new CreateColumnApiRequest { Name = "grade", Type = "text", Nullable = true }],
        });
        response.EnsureSuccessStatusCode();
        return tableName;
    }

    private async Task<(HttpStatusCode Status, ErrorResponse? Body)> PostAsync(string url, string json)
    {
        var response = await _client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
        ErrorResponse? body = null;
        if (!response.IsSuccessStatusCode)
        {
            body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        }

        return (response.StatusCode, body);
    }

    [Fact]
    public async Task The_issue_repro_a_plural_filters_typo_is_refused_not_ignored()
    {
        var tableName = await CreateTableAsync();

        var (status, body) = await PostAsync($"/api/data/{tableName}/query",
            """{"filters":[{"column":"grade","operator":"eq","value":"vip"}]}""");

        status.Should().Be(HttpStatusCode.BadRequest,
            "a dropped member turns a caller's typo into a confidently wrong 200");
        body!.Code.Should().Be("INVALID_ARGUMENT");
        body.Message.Should().Contain("filters");
        body.Message.Should().Contain("Supported members").And.Contain("filter");
    }

    [Fact]
    public async Task An_unknown_member_anywhere_in_the_body_is_refused()
    {
        var tableName = await CreateTableAsync();

        var (status, body) = await PostAsync($"/api/data/{tableName}/query", """{"zzz":true}""");

        status.Should().Be(HttpStatusCode.BadRequest);
        body!.Code.Should().Be("INVALID_ARGUMENT");
        body.Message.Should().Contain("zzz");
    }

    [Fact]
    public async Task A_typo_inside_a_nested_filter_node_is_refused()
    {
        var tableName = await CreateTableAsync();

        var (status, body) = await PostAsync($"/api/data/{tableName}/query",
            """{"filter":{"$type":"condition","colunm":"grade","operator":"eq","value":"vip"}}""");

        status.Should().Be(HttpStatusCode.BadRequest, "strictness must reach nested nodes, not just the root");
        body!.Code.Should().Be("INVALID_ARGUMENT");
    }

    [Fact]
    public async Task A_schema_request_with_an_unknown_member_is_refused()
    {
        var (status, body) = await PostAsync("/api/schema/tables",
            $$"""{"name":"strict_{{Guid.NewGuid():N}}","colums":[{"name":"a","type":"text"}]}""");

        status.Should().Be(HttpStatusCode.BadRequest,
            "a table created with its columns member typo'd would answer 201 with zero columns");
        body!.Code.Should().Be("INVALID_ARGUMENT");
        body.Message.Should().Contain("colums");
    }

    [Fact]
    public async Task A_row_write_dictionary_body_is_not_affected()
    {
        // Row bodies are dictionaries — every member maps by definition; their unknown-field policy
        // is the write pipeline's UNKNOWN_COLUMN, not request binding.
        var tableName = await CreateTableAsync();

        var response = await _client.PostAsJsonAsync($"/api/data/{tableName}",
            new Dictionary<string, object?> { ["grade"] = "vip" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_valid_complex_query_still_answers()
    {
        var tableName = await CreateTableAsync();

        var response = await _client.PostAsync($"/api/data/{tableName}/query",
            new StringContent(
                """{"filter":{"$type":"condition","column":"grade","operator":"eq","value":"vip"},"pageSize":5}""",
                Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
