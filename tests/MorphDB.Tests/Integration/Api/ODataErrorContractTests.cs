using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Service.OData;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// Pins the error contract on the OData consumption surface. An error here is written the way OData
/// JSON writes one — <c>{"error": {"code", "message", "details"?}}</c>, the shape an OData client
/// parses — and carries the code the REST error table names for the same mistake (GraphQL does the
/// same in its own shape, under <c>extensions.code</c>). Every route of the surface answers in it:
/// a caller's mistake an action names, an exception it lets escape, a refused write, a missing or
/// deleted project, and a batch item's failure. A filter the handler cannot parse must refuse loudly
/// — answering 200 with the filter silently ignored hands the caller every row and lets them believe
/// it matched their predicate.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ODataErrorContractTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;

    public ODataErrorContractTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
    }

    private async Task<(string TableName, string EntitySet)> CreateTableAsync()
    {
        var tableName = $"odataerr_{Guid.NewGuid():N}"[..30];
        var response = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns = [new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false }],
        });
        response.EnsureSuccessStatusCode();
        var entitySet = string.Concat(tableName.Split('_').Select(p =>
            p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant() : p));
        return (tableName, entitySet);
    }

    private async Task<(HttpStatusCode Status, ODataErrorDetail Body)> GetErrorAsync(string url) =>
        await ReadErrorAsync(await _client.GetAsync(url));

    private static async Task<(HttpStatusCode Status, ODataErrorDetail Body)> ReadErrorAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        var root = JsonDocument.Parse(payload).RootElement;
        root.TryGetProperty("error", out var error).Should().BeTrue($"every OData error is an OData error object: {payload}");
        error.ValueKind.Should().Be(JsonValueKind.Object, $"`error` is an object with code and message, not a string: {payload}");
        return (response.StatusCode, JsonSerializer.Deserialize<ODataErrorDetail>(error.GetRawText())!);
    }

    [Fact]
    public async Task An_unknown_column_in_filter_answers_400_with_the_code()
    {
        var (tableName, entitySet) = await CreateTableAsync();

        var (status, body) = await GetErrorAsync($"/odata/{entitySet}?$filter=nmae eq 'x'");

        status.Should().Be(HttpStatusCode.BadRequest);
        body.Code.Should().Be("COLUMN_NOT_FOUND");
        body.Message.Should().Contain("nmae").And.Contain(tableName);
    }

    [Fact]
    public async Task An_unknown_column_in_orderby_answers_400_with_the_code()
    {
        var (_, entitySet) = await CreateTableAsync();

        var (status, body) = await GetErrorAsync($"/odata/{entitySet}?$orderby=nmae");

        status.Should().Be(HttpStatusCode.BadRequest);
        body.Code.Should().Be("COLUMN_NOT_FOUND");
    }

    [Fact]
    public async Task An_unknown_entity_set_answers_404_with_the_code()
    {
        var (status, body) = await GetErrorAsync("/odata/NoSuchTableZz?$top=1");

        status.Should().Be(HttpStatusCode.NotFound);
        body.Code.Should().Be("NOT_FOUND");
        body.Message.Should().Contain("NoSuchTableZz");
    }

    [Fact]
    public async Task A_key_the_set_does_not_hold_answers_404_with_the_code()
    {
        var (_, entitySet) = await CreateTableAsync();
        var missing = Guid.NewGuid();

        var (status, body) = await GetErrorAsync($"/odata/{entitySet}({missing})");
        status.Should().Be(HttpStatusCode.NotFound);
        body.Code.Should().Be("RECORD_NOT_FOUND");
        body.Message.Should().Contain(missing.ToString());

        var (deleted, deleteBody) = await ReadErrorAsync(await _client.DeleteAsync($"/odata/{entitySet}({missing})", TestContext.Current.CancellationToken));
        deleted.Should().Be(HttpStatusCode.NotFound);
        deleteBody.Code.Should().Be("RECORD_NOT_FOUND");
    }

    [Fact]
    public async Task A_refused_write_names_its_code_and_each_cause_as_a_detail()
    {
        var (_, entitySet) = await CreateTableAsync();

        var (status, body) = await ReadErrorAsync(await _client.PostAsJsonAsync($"/odata/{entitySet}", new Dictionary<string, object?> { ["name"] = null }, TestContext.Current.CancellationToken));

        status.Should().Be(HttpStatusCode.BadRequest);
        body.Code.Should().Be("VALIDATION_ERROR");
        body.Details.Should().ContainSingle().Which.Target.Should().Be("name");
    }

    [Theory]
    [InlineData("/odata/Anything")]
    [InlineData("/odata/$metadata")]
    public async Task A_request_without_a_project_is_refused_in_the_odata_shape(string route)
    {
        using var client = _fixture.Api.CreateClientWithProject(Guid.Empty);

        var (status, body) = await ReadErrorAsync(await client.GetAsync(route, TestContext.Current.CancellationToken));

        status.Should().Be(HttpStatusCode.BadRequest, "the XML-only $metadata must not turn the refusal into 406");
        body.Code.Should().Be("MISSING_PROJECT");
    }

    [Fact]
    public async Task A_batch_item_that_fails_carries_an_error_object()
    {
        var (_, entitySet) = await CreateTableAsync();
        var missing = Guid.NewGuid();

        var response = await _client.PostAsJsonAsync("/odata/$batch", new ODataBatchRequest
        {
            Requests =
            [
                new ODataBatchRequestItem { Id = "1", Method = "POST", EntitySet = entitySet, Body = JsonSerializer.SerializeToElement(new { name = (string?)null }) },
                new ODataBatchRequestItem { Id = "2", Method = "DELETE", EntitySet = entitySet, Key = missing },
            ],
        }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("responses");

        items[0].GetProperty("status").GetInt32().Should().Be(400);
        items[0].GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
        items[1].GetProperty("status").GetInt32().Should().Be(404);
        items[1].GetProperty("error").GetProperty("code").GetString().Should().Be("RECORD_NOT_FOUND");
    }

    [Fact]
    public async Task A_filter_the_handler_cannot_parse_is_refused_not_ignored()
    {
        var (_, entitySet) = await CreateTableAsync();

        var (status, body) = await GetErrorAsync($"/odata/{entitySet}?$filter=name eq");

        status.Should().Be(HttpStatusCode.BadRequest,
            "an unparseable filter silently ignored would answer every row as if it matched");
        body.Code.Should().Be("VALIDATION_ERROR");
        body.Message.Should().Contain("filter");
    }
}
