using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Client;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;
using ClientModels = MorphDB.Client.Models;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A lookup matches the relation column's value against the target column the declaration names —
/// by default the target column of a relation declared on that column, else the target's
/// <c>_id</c> — and reads one target row per row: the first in the declared order when several match.
/// <para>
/// A lookup used to join on the target's <c>_id</c> only, so a value that names a row by a code or
/// a key could not be looked up, and its <c>onDelete</c> and <c>allowMultiple</c> settings were
/// stored and never read.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public sealed class LookupMatchTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;

    public LookupMatchTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
    }

    /// <summary>
    /// versions(doc_key, version, title): two versions of "D-1", one of "D-2" — a key that is not
    /// unique in its table, as a record key is across its corrections.
    /// </summary>
    private async Task<(string Versions, string Notes)> SetupAsync(string? matchColumn, string? orderBy)
    {
        var ct = TestContext.Current.CancellationToken;
        var s = Guid.NewGuid().ToString("N")[..8];
        var versions = $"lm_ver_{s}";
        var notes = $"lm_note_{s}";

        await CreatedAsync(_client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = versions,
            Columns =
            [
                new CreateColumnApiRequest { Name = "doc_key", Type = "text" },
                new CreateColumnApiRequest { Name = "version", Type = "integer" },
                new CreateColumnApiRequest { Name = "title", Type = "text" },
            ],
        }, ct));
        await InsertAsync(versions, new { doc_key = "D-1", version = 1, title = "first draft" });
        await InsertAsync(versions, new { doc_key = "D-1", version = 2, title = "revised" });
        await InsertAsync(versions, new { doc_key = "D-2", version = 1, title = "other" });

        await CreatedAsync(_client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = notes,
            Columns =
            [
                new CreateColumnApiRequest { Name = "about", Type = "text" },
                new CreateColumnApiRequest
                {
                    Name = "about_title", Type = "text",
                    Lookup = new LookupConfigApiRequest
                    {
                        RelationColumn = "about", TargetTable = versions, TargetColumn = "title",
                        MatchColumn = matchColumn, OrderBy = orderBy,
                    },
                },
            ],
        }, ct));
        await InsertAsync(notes, new { about = "D-1" });
        await InsertAsync(notes, new { about = "D-2" });
        await InsertAsync(notes, new { about = "D-9" });

        return (versions, notes);
    }

    [Fact]
    public async Task A_lookup_matches_a_named_column_and_reads_the_first_match_in_its_order()
    {
        var (_, notes) = await SetupAsync(matchColumn: "doc_key", orderBy: "version desc");

        var rows = await ListAsync(notes, "about");
        rows.Should().HaveCount(3, "several matches choose one row; they do not repeat the row");
        rows.Select(r => r.GetProperty("about_title").ToString()).Should().Equal("revised", "other", "");
        rows[2].GetProperty("about_title").ValueKind.Should().Be(JsonValueKind.Null, "a value that matches nothing reads null");

        (await ListAsync($"{notes}?filter=about_title:eq:revised", "about")).Should().ContainSingle();
    }

    [Fact]
    public async Task Without_an_order_the_earliest_row_matches()
    {
        var (_, notes) = await SetupAsync(matchColumn: "doc_key", orderBy: null);

        (await ListAsync(notes, "about")).Select(r => r.GetProperty("about_title").ToString())
            .Should().Equal("first draft", "other", "");
    }

    [Fact]
    public async Task Without_a_match_column_a_declared_relation_names_the_target_column()
    {
        var ct = TestContext.Current.CancellationToken;
        var s = Guid.NewGuid().ToString("N")[..8];
        var codes = $"lm_code_{s}";
        var refs = $"lm_ref_{s}";
        await CreatedAsync(_client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = codes,
            Columns = [new CreateColumnApiRequest { Name = "code", Type = "text", Unique = true }, new CreateColumnApiRequest { Name = "label", Type = "text" }],
        }, ct));
        await CreatedAsync(_client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = refs,
            Columns = [new CreateColumnApiRequest { Name = "code_ref", Type = "text" }],
        }, ct));
        await CreatedAsync(_client.PostAsJsonAsync("/api/schema/relations", new CreateRelationApiRequest
        {
            Name = $"ref_code_{s}", SourceTable = refs, SourceColumn = "code_ref", TargetTable = codes, TargetColumn = "code",
            Type = "many-to-one",
        }, ct));
        await CreatedAsync(_client.PostAsJsonAsync($"/api/schema/tables/{refs}/columns", new AddColumnApiRequest
        {
            Name = "code_label", Type = "text",
            Lookup = new LookupConfigApiRequest { RelationColumn = "code_ref", TargetTable = codes, TargetColumn = "label" },
        }, ct));

        await InsertAsync(codes, new { code = "K", label = "kilo" });
        await InsertAsync(refs, new { code_ref = "K" });

        (await ListAsync(refs, "code_ref")).Single().GetProperty("code_label").GetString().Should().Be("kilo");
    }

    [Theory]
    [InlineData("no_such", null, "a match column the target does not have")]
    [InlineData("version", null, "a text value matched against an integer column")]
    [InlineData("doc_key", "version sideways", "an order that is not a column list")]
    public async Task A_lookup_that_cannot_match_is_refused_when_declared(string matchColumn, string? orderBy, string why)
    {
        var ct = TestContext.Current.CancellationToken;
        var (versions, notes) = await SetupAsync(matchColumn: "doc_key", orderBy: null);

        var response = await _client.PostAsJsonAsync($"/api/schema/tables/{notes}/columns", new AddColumnApiRequest
        {
            Name = "bad_lookup", Type = "text",
            Lookup = new LookupConfigApiRequest { RelationColumn = "about", TargetTable = versions, TargetColumn = "title", MatchColumn = matchColumn, OrderBy = orderBy },
        }, ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, why);
        (await response.Content.ReadAsStringAsync(ct)).Should().Contain("INVALID_EXPRESSION");
    }

    [Fact]
    public async Task The_client_declares_a_lookup_and_reads_its_declaration_back()
    {
        var ct = TestContext.Current.CancellationToken;
        var (versions, notes) = await SetupAsync(matchColumn: "doc_key", orderBy: null);
        var http = new HttpClient(_fixture.Api.CreateHandler()) { BaseAddress = _fixture.Api.BaseAddress };
        await using var client = new MorphDBClient(http);
        client.SetProjectId(_fixture.Api.ProjectId);

        var added = await client.Schema.AddColumnAsync(notes, new ClientModels.AddColumnRequest
        {
            Name = "latest_title",
            Type = "text",
            Lookup = new ClientModels.LookupConfig
            {
                RelationColumn = "about", TargetTable = versions, TargetColumn = "title", MatchColumn = "doc_key", OrderBy = "version desc",
            },
        }, ct);

        added.IsDerived.Should().BeTrue();
        added.Lookup.Should().NotBeNull();
        added.Lookup!.MatchColumn.Should().Be("doc_key");
        added.Lookup.OrderBy.Should().Be("version desc");

        var table = await client.Schema.GetTableAsync(notes, ct);
        table!.Columns.Single(c => c.Name == "latest_title").Lookup!.TargetTable.Should().Be(versions);
        (await ListAsync(notes, "about"))[0].GetProperty("latest_title").GetString().Should().Be("revised");
    }

    private static async Task CreatedAsync(Task<HttpResponseMessage> request)
    {
        var response = await request;
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private async Task InsertAsync(string table, object row) =>
        (await _client.PostAsJsonAsync($"/api/data/{table}", row, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Created);

    private async Task<IReadOnlyList<JsonElement>> ListAsync(string tableAndQuery, string orderBy)
    {
        var separator = tableAndQuery.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var response = await _client.GetAsync($"/api/data/{tableAndQuery}{separator}orderBy={orderBy}:asc", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data")
            .EnumerateArray().Select(r => r.GetProperty("data").Clone()).ToList();
    }
}
