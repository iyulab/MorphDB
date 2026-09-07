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

    private async Task<(HttpStatusCode Status, ErrorResponse? Body)> PostAsync(string url, string json, HttpMethod? method = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var response = await _client.SendAsync(request);
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
        body.Message.Should().NotContain("MorphDB.Service").And.NotContain(".NET",
            "the error is the caller's to act on, and an implementation identifier is the one part of it they cannot");
    }

    [Fact]
    public async Task A_value_of_the_wrong_kind_is_named_without_a_clr_type()
    {
        var tableName = await CreateTableAsync();

        var (status, body) = await PostAsync($"/api/data/{tableName}/query", """{"page":"first"}""");

        status.Should().Be(HttpStatusCode.BadRequest);
        body!.Code.Should().Be("INVALID_ARGUMENT");
        body.Message.Should().Contain("not an integer").And.Contain("$.page");
        body.Message.Should().NotContain("System.");
    }

    [Fact]
    public async Task A_schema_update_without_a_version_is_refused_at_binding_not_answered_as_a_conflict()
    {
        // `version` is documented as the one required field of a schema update. Bound as a plain int
        // it defaulted to 0, so an omitted version was compared against the table and answered 409
        // SCHEMA_VERSION_CONFLICT -- the same code as a real lost race, for a request that never said
        // which version it had read.
        var tableName = await CreateTableAsync();

        var (status, body) = await PostAsync($"/api/schema/tables/{tableName}", """{"name":"renamed"}""", HttpMethod.Patch);

        status.Should().Be(HttpStatusCode.BadRequest, "an omitted version is a malformed request, not a conflict");
        body!.Code.Should().Be("INVALID_ARGUMENT");
        body.Message.Should().Contain("version").And.NotContain("MorphDB.Service");
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
            new Dictionary<string, object?> { ["grade"] = "vip" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_valid_complex_query_still_answers()
    {
        var tableName = await CreateTableAsync();

        var response = await _client.PostAsync($"/api/data/{tableName}/query",
            new StringContent(
                """{"filter":{"$type":"condition","column":"grade","operator":"eq","value":"vip"},"pageSize":5}""",
                Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
