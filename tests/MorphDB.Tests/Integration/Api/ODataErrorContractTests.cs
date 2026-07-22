using System.Net;
using System.Net.Http.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// Pins the error contract on the OData consumption surface — the last surface without one
/// (REST direct: ErrorSurfaceContractTests; C# SDK: ClientErrorEnvelopeTests; GraphQL:
/// GraphQlWriteContractTests). A caller mistake must answer 4xx with the standard
/// <c>{error, message, code}</c> envelope through this surface too, and a filter the handler
/// cannot parse must refuse loudly — answering 200 with the filter silently ignored hands the
/// caller every row and lets them believe it matched their predicate (live-probed on 2026-07-22:
/// <c>$filter=name eq</c> answered 200 unfiltered).
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ODataErrorContractTests
{
    private readonly HttpClient _client;

    public ODataErrorContractTests(ApiIntegrationFixture fixture)
    {
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

    private async Task<(HttpStatusCode Status, ErrorResponse Body)> GetErrorAsync(string url)
    {
        var response = await _client.GetAsync(url);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull("every OData error must carry the standard envelope");
        return (response.StatusCode, body!);
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
