using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Service.Models.Api;
using MorphDB.Service.Security;
using MorphDB.Tests.Fixtures;
using Npgsql;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A person's token names its subject (<c>sub</c>) as an opaque string: a UUID for some issuers, an e-mail address, a
/// number or <c>provider|id</c> for others, and row-level security's <c>{{user_id}}</c> is the same string. The columns
/// that record a person — <c>_created_by</c>, <c>_updated_by</c>, <c>_deleted_by</c>, <c>_owner_id</c> — are therefore
/// text, so every issuer's subject can be recorded as it is and compared with <c>{{user_id}}</c>; a column that only
/// held a UUID refused the writes of every other subject.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class AuditColumnsTakeAnySubjectTests
{
    private const string MasterSecret = "mdb_audit_subject_master_secret";
    private const string Issuer = "https://issuer.example.test/realm";
    private const string Audience = "morphdb";

    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "issuer-key" };

    private readonly ApiIntegrationFixture _fixture;

    public AuditColumnsTakeAnySubjectTests(ApiIntegrationFixture fixture) => _fixture = fixture;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient As(string bearer)
    {
        var client = _fixture.Api
            .WithTokens(MasterSecret, new TokenOptions { Issuer = Issuer, Audience = Audience }, SigningKey)
            .CreateClient();
        client.DefaultRequestHeaders.Add("X-Project-Id", _fixture.Api.ProjectId.ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    private static string Token(string subject)
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Claims = new Dictionary<string, object> { [JwtRegisteredClaimNames.Sub] = subject },
            IssuedAt = now.AddMinutes(-10),
            NotBefore = now.AddMinutes(-10),
            Expires = now.AddMinutes(10),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
        });
    }

    public static TheoryData<string> Subjects => new()
    {
        "auth0|5f7c8ec7c33c6c004bbafe82",
        "ada.lovelace@example.com",
        "10293847",
        "9b3a5c2e-0a5e-4f61-8d1a-3b1f4c2d9e00",
    };

    private async Task<string> CreateTableAsync(HttpClient master, SystemColumnOptionsApiRequest options)
    {
        var table = $"subj_{Guid.NewGuid():N}"[..30];
        (await master.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            SystemColumns = options,
            Columns =
            [
                new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = false, Unique = true },
                new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = false },
            ],
        }, Ct)).EnsureSuccessStatusCode();
        return table;
    }

    private static async Task<JsonElement> RowAsync(HttpClient client, string table, bool includeDeleted = false)
    {
        var suffix = includeDeleted ? "&includeDeleted=true" : string.Empty;
        using var doc = JsonDocument.Parse(await client.GetStringAsync($"/api/data/{table}?filter=code:eq:k{suffix}", Ct));
        return doc.RootElement.GetProperty("data")[0].GetProperty("data").Clone();
    }

    [Theory]
    [MemberData(nameof(Subjects))]
    public async Task Whoever_a_token_names_is_recorded_as_it_names_them(string subject)
    {
        var master = As(MasterSecret);
        var table = await CreateTableAsync(master, new SystemColumnOptionsApiRequest { AuditFields = true, Ownership = true, SoftDelete = true });
        var editor = "someone-else";

        (await As(Token(subject)).PostAsJsonAsync($"/api/data/{table}", new { code = "k", label = "orig" }, Ct)).EnsureSuccessStatusCode();
        var created = await RowAsync(master, table);
        created.GetProperty("_created_by").GetString().Should().Be(subject);
        created.GetProperty("_updated_by").GetString().Should().Be(subject);
        created.GetProperty("_owner_id").GetString().Should().Be(subject);

        var id = created.GetProperty("_id").GetGuid();
        (await As(Token(editor)).PatchAsJsonAsync($"/api/data/{table}/{id}", new { label = "changed" }, Ct)).EnsureSuccessStatusCode();
        var edited = await RowAsync(master, table);
        edited.GetProperty("_updated_by").GetString().Should().Be(editor);
        edited.GetProperty("_created_by").GetString().Should().Be(subject, "the creator does not change");
        edited.GetProperty("_owner_id").GetString().Should().Be(subject, "the owner does not change when someone else edits");

        (await As(Token(subject)).DeleteAsync($"/api/data/{table}/{id}", Ct)).EnsureSuccessStatusCode();
        var deleted = await RowAsync(master, table, includeDeleted: true);
        deleted.GetProperty("_deleted_by").GetString().Should().Be(subject);
    }

    // The point of recording the subject as it is: row-level security compares it with {{user_id}}, the same string.
    [Theory]
    [MemberData(nameof(Subjects))]
    public async Task A_policy_that_compares_the_owner_with_the_caller_works_for_any_subject(string subject)
    {
        var master = As(MasterSecret);
        var table = await CreateTableAsync(master, new SystemColumnOptionsApiRequest { Ownership = true });
        (await master.PostAsJsonAsync("/api/security/policies",
            new { name = "own_rows", tableName = table, policyType = "Select", expression = "_owner_id = {{user_id}}" }, Ct)).EnsureSuccessStatusCode();

        (await As(Token(subject)).PostAsJsonAsync($"/api/data/{table}", new { code = "k", label = "mine" }, Ct)).EnsureSuccessStatusCode();
        (await As(Token("another-person")).PostAsJsonAsync($"/api/data/{table}", new { code = "other", label = "theirs" }, Ct)).EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await As(Token(subject)).GetStringAsync($"/api/data/{table}", Ct));
        var labels = doc.RootElement.GetProperty("data").EnumerateArray()
            .Select(r => r.GetProperty("data").GetProperty("label").GetString()).ToList();

        labels.Should().Equal("mine");
    }

    // A table made when these columns held a uuid keeps them until it catches up. The shape an earlier version built is
    // made here directly (the service no longer builds it); catching up is the ordinary column update, for these
    // columns and this change only.
    private async Task<(string Table, Guid TableId)> TableAsBuiltBeforeAsync(HttpClient master)
    {
        var table = await CreateTableAsync(master, new SystemColumnOptionsApiRequest { AuditFields = true, Ownership = true });
        await using var connection = new NpgsqlConnection(_fixture.Postgres.ConnectionString);
        var (tableId, schema, physical) = await connection.QuerySingleAsync<(Guid, string, string)>(
            """
            SELECT t.table_id, n.nspname, t.physical_name FROM morphdb._morph_tables t
            JOIN pg_class c ON c.relname = t.physical_name AND c.relkind = 'r'
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE t.project_id = @projectId AND t.logical_name = @table
            """, new { projectId = _fixture.Api.ProjectId, table });
        foreach (var column in new[] { "_created_by", "_updated_by", "_owner_id" })
        {
            await connection.ExecuteAsync($"""ALTER TABLE "{schema}"."{physical}" ALTER COLUMN "{column}" TYPE uuid USING NULL""");
            await connection.ExecuteAsync(
                "UPDATE morphdb._morph_columns SET data_type = 'Uuid', native_type = 'uuid' WHERE table_id = @tableId AND logical_name = @column",
                new { tableId, column });
        }

        _fixture.Api.Services.GetRequiredService<MetadataCache>().Invalidate(_fixture.Api.ProjectId);
        return (table, tableId);
    }

    [Fact]
    public async Task A_table_built_before_refuses_a_subject_that_is_not_a_uuid_until_its_columns_catch_up()
    {
        var master = As(MasterSecret);
        var (table, _) = await TableAsBuiltBeforeAsync(master);
        const string subject = "auth0|5f7c8ec7c33c6c004bbafe82";

        var before = await As(Token(subject)).PostAsJsonAsync($"/api/data/{table}", new { code = "k", label = "orig" }, Ct);
        before.IsSuccessStatusCode.Should().BeFalse("the columns still hold a uuid");

        foreach (var column in new[] { "_created_by", "_updated_by", "_owner_id" })
        {
            var version = await VersionAsync(master, table);
            var update = await master.PatchAsJsonAsync($"/api/schema/tables/{table}/columns/{column}", new { type = "text", version }, Ct);
            update.IsSuccessStatusCode.Should().BeTrue($"{column}: {await update.Content.ReadAsStringAsync(Ct)}");
        }

        (await As(Token(subject)).PostAsJsonAsync($"/api/data/{table}", new { code = "k", label = "orig" }, Ct)).EnsureSuccessStatusCode();
        var row = await RowAsync(master, table);
        row.GetProperty("_created_by").GetString().Should().Be(subject);
        row.GetProperty("_owner_id").GetString().Should().Be(subject);
    }

    [Fact]
    public async Task Catching_up_keeps_the_ids_already_recorded()
    {
        var master = As(MasterSecret);
        var (table, _) = await TableAsBuiltBeforeAsync(master);
        var creator = Guid.NewGuid();
        (await As(Token(creator.ToString())).PostAsJsonAsync($"/api/data/{table}", new { code = "k", label = "orig" }, Ct)).EnsureSuccessStatusCode();

        foreach (var column in new[] { "_created_by", "_updated_by", "_owner_id" })
        {
            (await master.PatchAsJsonAsync($"/api/schema/tables/{table}/columns/{column}", new { type = "text", version = await VersionAsync(master, table) }, Ct))
                .EnsureSuccessStatusCode();
        }

        var row = await RowAsync(master, table);
        row.GetProperty("_created_by").GetString().Should().Be(creator.ToString());
        row.GetProperty("_owner_id").GetString().Should().Be(creator.ToString());
    }

    // The one thing a table made before cannot do on its own: PostgreSQL will not retype a column a policy reads. The
    // update is refused naming the policy, nothing moves, and once the policy is dropped the column catches up and the
    // policy is written again around it (docs/SYSTEM_COLUMNS.md).
    [Fact]
    public async Task A_policy_that_reads_the_owner_column_holds_it_as_it_is_until_the_policy_is_dropped()
    {
        var master = As(MasterSecret);
        var (table, _) = await TableAsBuiltBeforeAsync(master);
        var policy = await master.PostAsJsonAsync("/api/security/policies",
            new { name = "own_rows", tableName = table, policyType = "Select", expression = "_owner_id = {{user_id}}" }, Ct);
        policy.IsSuccessStatusCode.Should().BeTrue(await policy.Content.ReadAsStringAsync(Ct));
        var policyId = JsonElement.Parse(await policy.Content.ReadAsStringAsync(Ct)).GetProperty("id").GetGuid();

        var refused = await master.PatchAsJsonAsync($"/api/schema/tables/{table}/columns/_owner_id", new { type = "text", version = await VersionAsync(master, table) }, Ct);

        refused.IsSuccessStatusCode.Should().BeFalse("a policy reads the column");
        var body = await refused.Content.ReadAsStringAsync(Ct);
        body.Should().Contain("COLUMN_USED_BY_POLICY").And.Contain("own_rows");

        (await master.DeleteAsync($"/api/security/policies/{policyId}", Ct)).EnsureSuccessStatusCode();
        var caughtUp = await master.PatchAsJsonAsync($"/api/schema/tables/{table}/columns/_owner_id", new { type = "text", version = await VersionAsync(master, table) }, Ct);
        caughtUp.IsSuccessStatusCode.Should().BeTrue(await caughtUp.Content.ReadAsStringAsync(Ct));
    }

    [Theory]
    [InlineData("_id", "text")]
    [InlineData("_version", "text")]
    [InlineData("_created_by", "integer")]
    public async Task No_other_change_to_a_system_column_is_taken(string column, string type)
    {
        var master = As(MasterSecret);
        var table = await CreateTableAsync(master, new SystemColumnOptionsApiRequest { AuditFields = true });

        var response = await master.PatchAsJsonAsync($"/api/schema/tables/{table}/columns/{column}", new { type, version = await VersionAsync(master, table) }, Ct);

        response.IsSuccessStatusCode.Should().BeFalse();
    }

    private static async Task<int> VersionAsync(HttpClient client, string table)
    {
        using var doc = JsonDocument.Parse(await client.GetStringAsync($"/api/schema/tables/{table}", Ct));
        return doc.RootElement.GetProperty("version").GetInt32();
    }
}
