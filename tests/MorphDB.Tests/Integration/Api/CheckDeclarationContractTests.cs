using System.Net;
using System.Net.Http.Json;
using Dapper;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;
using Npgsql;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// CHECK canonicalization on the wire (§3.10-C1): the app-layer grammar is the single definition —
/// a declaration outside it is refused at declaration time (previously it was stored, silently
/// skipped by the evaluator, and enforced only by a physical CHECK the ALTER path could never
/// update), and no physical CHECK constraint is emitted at all.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class CheckDeclarationContractTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;

    public CheckDeclarationContractTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
    }

    private Task<HttpResponseMessage> CreateTableAsync(string tableName, string check) =>
        _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns =
            [
                new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false, Check = check },
            ],
        });

    [Fact]
    public async Task A_check_the_evaluator_cannot_enforce_is_refused_at_declaration()
    {
        var response = await CreateTableAsync($"chkgram_{Guid.NewGuid():N}"[..25], "length(name) > 3");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a stored-but-unenforceable CHECK would constrain nothing, silently");
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Code.Should().Be("INVALID_ARGUMENT");
        body.Message.Should().Contain("Supported CHECK forms");
    }

    [Fact]
    public async Task The_postgres_regex_operator_is_refused_toward_matches()
    {
        var response = await CreateTableAsync($"chkgram_{Guid.NewGuid():N}"[..25], "name ~ '^[a-z]+$'");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.Message
            .Should().Contain("MATCHES", "the grammar's regex form is MATCHES, not the SQL operator");
    }

    [Fact]
    public async Task A_supported_check_is_accepted_and_emits_no_physical_constraint()
    {
        var tableName = $"chkgram_{Guid.NewGuid():N}"[..25];
        var response = await CreateTableAsync(tableName, "name != 'forbidden'");
        response.EnsureSuccessStatusCode();
        var table = await response.Content.ReadFromJsonAsync<TableApiResponse>();
        var physicalName = await GetPhysicalNameAsync(table!.Id);

        await using var connection = new NpgsqlConnection(_fixture.Postgres.ConnectionString);
        var checkConstraints = await connection.QueryAsync<string>(
            """
            SELECT con.conname FROM pg_constraint con
            JOIN pg_class rel ON rel.oid = con.conrelid
            WHERE rel.relname = @physicalName AND con.contype = 'c' AND con.conname NOT LIKE '%not_null%'
            """,
            new { physicalName });

        checkConstraints.Should().BeEmpty(
            "CHECK is virtual: enforcement is the app evaluator's alone, so DDL must carry nothing");
    }

    [Fact]
    public async Task The_declared_check_still_constrains_writes()
    {
        var tableName = $"chkgram_{Guid.NewGuid():N}"[..25];
        (await CreateTableAsync(tableName, "name != 'forbidden'")).EnsureSuccessStatusCode();

        var refused = await _client.PostAsJsonAsync($"/api/data/{tableName}",
            new Dictionary<string, object?> { ["name"] = "forbidden" });
        var accepted = await _client.PostAsJsonAsync($"/api/data/{tableName}",
            new Dictionary<string, object?> { ["name"] = "fine" });

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "removing the physical CHECK must not remove the constraint itself");
        accepted.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Adding_a_column_with_an_unenforceable_check_is_refused()
    {
        var tableName = $"chkgram_{Guid.NewGuid():N}"[..25];
        (await CreateTableAsync(tableName, "name != 'x'")).EnsureSuccessStatusCode();

        var response = await _client.PostAsJsonAsync($"/api/schema/tables/{tableName}/columns",
            new AddColumnApiRequest { Name = "score", Type = "integer", Check = "score IN (1,2,3)" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.Message
            .Should().Contain("Supported CHECK forms");
    }

    private async Task<string> GetPhysicalNameAsync(Guid tableId)
    {
        await using var connection = new NpgsqlConnection(_fixture.Postgres.ConnectionString);
        return await connection.QuerySingleAsync<string>(
            "SELECT physical_name FROM morphdb._morph_tables WHERE table_id = @tableId",
            new { tableId });
    }
}
