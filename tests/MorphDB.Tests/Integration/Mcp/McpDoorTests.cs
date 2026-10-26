using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using MorphDB.Service.Models.Api;
using MorphDB.Service.Security;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Mcp;

/// <summary>
/// The detection tests for the read-only MCP door: it opens only when asked, it takes no
/// credential the REST API does not, it runs as the person the token names so row-level security holds, and
/// it has no tool that writes. Each says what it would stop detecting if the door were rebuilt to do
/// otherwise.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class McpDoorTests
{
    private const string MasterSecret = "mdb_mcp_test_master_secret";
    private const string Issuer = "https://issuer.example.test/realm";
    private const string Audience = "morphdb";

    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "mcp-issuer-key" };

    private readonly ApiIntegrationFixture _fixture;

    public McpDoorTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    private static readonly string[] BadFilter = ["owner:nonsense:x"];

    private static TokenOptions Tokens => new() { Issuer = Issuer, Audience = Audience };

    private static string Token(string subject, IDictionary<string, object>? claims = null) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Claims = new Dictionary<string, object>(claims ?? new Dictionary<string, object>()) { [JwtRegisteredClaimNames.Sub] = subject },
            IssuedAt = DateTime.UtcNow.AddMinutes(-10),
            NotBefore = DateTime.UtcNow.AddMinutes(-10),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
        });

    private HttpClient RestAs(string bearer, int maxPageSize = 100)
    {
        var client = _fixture.Api.WithMcp(MasterSecret, Tokens, SigningKey, maxPageSize).CreateClient();
        client.DefaultRequestHeaders.Add("X-Project-Id", _fixture.Api.ProjectId.ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    private async Task<McpClient> McpAs(string? bearer, Guid? project = null, int maxPageSize = 100)
    {
        var http = _fixture.Api.WithMcp(MasterSecret, Tokens, SigningKey, maxPageSize).CreateClient();
        var headers = new Dictionary<string, string>();
        if (bearer is not null)
        {
            headers["Authorization"] = $"Bearer {bearer}";
        }

        if ((project ?? _fixture.Api.ProjectId) is { } id && id != Guid.Empty)
        {
            headers["X-Project-Id"] = id.ToString();
        }

        return await McpClient.CreateAsync(
            new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint = new Uri(http.BaseAddress!, "/mcp"),
                    TransportMode = HttpTransportMode.StreamableHttp,
                    AdditionalHeaders = headers,
                },
                http,
                ownsHttpClient: true),
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private async Task<string> RowLevelTableAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var master = RestAs(MasterSecret);
        var table = $"mcp_rls_{Guid.NewGuid():N}"[..28];
        (await master.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns = [new CreateColumnApiRequest { Name = "owner", Type = "text", Nullable = false }, new CreateColumnApiRequest { Name = "note", Type = "text", Nullable = true }],
        }, ct)).EnsureSuccessStatusCode();
        foreach (var owner in new[] { "alice", "bob", "alice" })
        {
            (await master.PostAsJsonAsync($"/api/data/{table}", new Dictionary<string, object?> { ["owner"] = owner, ["note"] = $"{owner}'s" }, ct)).EnsureSuccessStatusCode();
        }

        (await master.PostAsJsonAsync("/api/security/policies", new { name = "own_rows", tableName = table, policyType = "Select", expression = "owner = {{user_id}}" }, ct))
            .EnsureSuccessStatusCode();
        return table;
    }

    private static string[] Owners(CallToolResult result)
    {
        result.IsError.Should().NotBe(true, string.Join(" ", result.Content.OfType<TextContentBlock>().Select(c => c.Text)));
        var rows = result.StructuredContent!.Value.GetProperty("rows");
        return [.. rows.EnumerateArray().Select(r => r.GetProperty("owner").GetString()!).Order()];
    }

    // Delete the door's registration and the rest of this class has nothing to talk to; this is the one that goes
    // red if the door is on without being asked — the default deployment's surface must not grow.
    [Fact]
    public async Task The_door_is_closed_unless_it_is_switched_on()
    {
        // A credential first: with one injected, every path but health and metrics asks for it before routing.
        var http = _fixture.Api.WithMasterSecret(MasterSecret).CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MasterSecret);
        var response = await http.PostAsync("/mcp", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Delete the credential check in front of the door and this goes green on a tool list for anyone.
    [Fact]
    public async Task A_request_with_no_credential_is_refused_as_on_the_rest_api()
    {
        var http = _fixture.Api.WithMcp(MasterSecret, Tokens, SigningKey).CreateClient();

        var response = await http.PostAsync("/mcp", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Add a tool and this goes red until the list is changed here, on purpose; a write tool cannot arrive unseen.
    [Fact]
    public async Task The_door_offers_only_read_tools_and_every_one_says_it_is_read_only()
    {
        await using var client = await McpAs(Token("alice"));

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        tools.Select(t => t.Name).Order().Should().Equal("describe_table", "describe_tables", "get_record", "query_records");
        tools.Should().OnlyContain(t => t.ProtocolTool.Annotations!.ReadOnlyHint == true && t.ProtocolTool.Annotations.DestructiveHint != true);
    }

    // The proposition that matters: a person asking through the door sees what the same person sees through REST.
    [Fact]
    public async Task A_tool_call_runs_as_the_person_the_token_names_so_row_level_security_holds()
    {
        var table = await RowLevelTableAsync();
        await using var alice = await McpAs(Token("alice"));
        await using var bob = await McpAs(Token("bob"));
        await using var nobody = await McpAs(Token("erin"));
        var ct = TestContext.Current.CancellationToken;
        var call = new Dictionary<string, object?> { ["table"] = table };

        Owners(await alice.CallToolAsync("query_records", call, cancellationToken: ct)).Should().Equal("alice", "alice");
        Owners(await bob.CallToolAsync("query_records", call, cancellationToken: ct)).Should().Equal("bob");
        Owners(await nobody.CallToolAsync("query_records", call, cancellationToken: ct)).Should().BeEmpty("a token is no master");

        // …and the same rows the REST API gives alice.
        var viaRest = await RestAs(Token("alice")).GetFromJsonAsync<PagedResponse<DataRecordResponse>>($"/api/data/{table}", ct);
        viaRest!.Data.Should().HaveCount(2);
    }

    // The id a person holds is no key to a row they may not see: bob asking for alice's record is told it is not there.
    [Fact]
    public async Task A_record_by_id_is_read_under_the_callers_rules_and_never_leaks_across_them()
    {
        var table = await RowLevelTableAsync();
        var ct = TestContext.Current.CancellationToken;
        await using var alice = await McpAs(Token("alice"));
        await using var bob = await McpAs(Token("bob"));
        var rows = (await alice.CallToolAsync("query_records", new Dictionary<string, object?> { ["table"] = table }, cancellationToken: ct))
            .StructuredContent!.Value.GetProperty("rows");
        var alicesId = rows.EnumerateArray().First().GetProperty("_id").GetString()!;

        var own = await alice.CallToolAsync("get_record", new Dictionary<string, object?> { ["table"] = table, ["id"] = alicesId }, cancellationToken: ct);
        var theirs = await bob.CallToolAsync("get_record", new Dictionary<string, object?> { ["table"] = table, ["id"] = alicesId }, cancellationToken: ct);
        var notAnId = await alice.CallToolAsync("get_record", new Dictionary<string, object?> { ["table"] = table, ["id"] = "nope" }, cancellationToken: ct);

        own.IsError.Should().NotBe(true);
        own.StructuredContent!.Value.GetProperty("record").GetProperty("owner").GetString().Should().Be("alice");
        theirs.IsError.Should().BeTrue();
        string.Join(" ", theirs.Content.OfType<TextContentBlock>().Select(c => c.Text)).Should().NotContain("alice's");
        notAnId.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task The_tables_and_their_columns_are_described_from_the_projects_schema()
    {
        var table = await RowLevelTableAsync();
        await using var client = await McpAs(Token("alice"));
        var ct = TestContext.Current.CancellationToken;

        var listed = await client.CallToolAsync("describe_tables", cancellationToken: ct);
        var described = await client.CallToolAsync("describe_table", new Dictionary<string, object?> { ["table"] = table }, cancellationToken: ct);

        listed.StructuredContent!.Value.GetProperty("tables").EnumerateArray().Select(t => t.GetProperty("name").GetString()).Should().Contain(table);
        var columns = described.StructuredContent!.Value.GetProperty("columns").EnumerateArray().ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("required").GetBoolean());
        columns["owner"].Should().BeTrue();
        columns["note"].Should().BeFalse();
    }

    [Fact]
    public async Task A_page_is_capped_by_the_door_and_says_when_more_follow()
    {
        var table = await RowLevelTableAsync();
        var ct = TestContext.Current.CancellationToken;
        await using var alice = await McpAs(Token("alice"), maxPageSize: 1);

        var result = await alice.CallToolAsync("query_records", new Dictionary<string, object?> { ["table"] = table, ["pageSize"] = 50 }, cancellationToken: ct);

        var page = result.StructuredContent!.Value;
        page.GetProperty("rows").GetArrayLength().Should().Be(1);
        page.GetProperty("pageSize").GetInt32().Should().Be(1);
        page.GetProperty("hasNext").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task A_request_with_no_project_is_refused_in_the_words_the_rest_api_uses()
    {
        await using var client = await McpAs(Token("alice"), project: Guid.Empty);

        var result = await client.CallToolAsync("describe_tables", cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.Should().BeTrue();
        string.Join(" ", result.Content.OfType<TextContentBlock>().Select(c => c.Text)).Should().Contain("X-Project-Id");
    }

    [Fact]
    public async Task A_filter_the_rules_do_not_accept_is_the_tools_error_and_not_a_server_fault()
    {
        var table = await RowLevelTableAsync();
        await using var client = await McpAs(Token("alice"));

        var result = await client.CallToolAsync(
            "query_records",
            new Dictionary<string, object?> { ["table"] = table, ["filter"] = BadFilter },
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.Should().BeTrue();
    }
}
