using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Dapper;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MorphDB.Npgsql.Schema;
using MorphDB.Service.Models.Api;
using MorphDB.Service.Security;
using MorphDB.Tests.Fixtures;
using Npgsql;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The detection tests for people's access tokens: a request that presents a token from the issuer
/// the deployment trusts runs under row-level security as the person the token names.
/// <para>
/// Each check refuses one way a token could be wrong — signed by someone else, issued by someone
/// else, meant for some other service, expired, or claiming a role that bypasses row-level security —
/// and the last two follow a good token through to the policy PostgreSQL evaluates, which is the
/// proposition that matters: a subject that never reaches <c>{{user_id}}</c> would read as a working
/// boundary and enforce nothing.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class TokenAuthenticationTests
{
    private const string MasterSecret = "mdb_token_test_master_secret";
    private const string Issuer = "https://issuer.example.test/realm";
    private const string Audience = "morphdb";

    private static readonly RsaSecurityKey SigningKey = NewKey("issuer-key");
    private static readonly RsaSecurityKey StrangerKey = NewKey("stranger-key");

    private readonly ApiIntegrationFixture _fixture;

    public TokenAuthenticationTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    private static RsaSecurityKey NewKey(string keyId) => new(RSA.Create(2048)) { KeyId = keyId };

    private HttpClient Client(string? bearer)
    {
        var client = _fixture.Api
            .WithTokens(MasterSecret, new TokenOptions { Issuer = Issuer, Audience = Audience }, SigningKey)
            .CreateClient();
        client.DefaultRequestHeaders.Add("X-Project-Id", _fixture.Api.ProjectId.ToString());
        if (bearer is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return client;
    }

    private static string Token(
        string subject,
        IDictionary<string, object>? claims = null,
        SecurityKey? key = null,
        string issuer = Issuer,
        string audience = Audience,
        DateTime? expires = null)
    {
        var now = DateTime.UtcNow;
        var payload = new Dictionary<string, object>(claims ?? new Dictionary<string, object>())
        {
            [JwtRegisteredClaimNames.Sub] = subject
        };

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = payload,
            IssuedAt = now.AddMinutes(-10),
            NotBefore = now.AddMinutes(-10),
            Expires = expires ?? now.AddMinutes(10),
            SigningCredentials = new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.RsaSha256)
        });
    }

    [Fact]
    public async Task A_token_from_the_trusted_issuer_is_accepted()
    {
        var response = await Client(Token("alice")).GetAsync("/api/schema/tables", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    public static TheoryData<string, string> Refused => new()
    {
        { "signed by another key", Token("alice", key: StrangerKey) },
        { "from another issuer", Token("alice", issuer: "https://elsewhere.example.test") },
        { "for another audience", Token("alice", audience: "some-other-service") },
        { "expired", Token("alice", expires: DateTime.UtcNow.AddMinutes(-5)) },
    };

    // Delete the deny branch in CredentialAuthenticationMiddleware.AuthenticateTokenAsync and each row
    // goes green on a 200 — or, read as a secret instead, on the same 401 for the wrong reason, which
    // is why the message is asserted too.
    [Theory]
    [MemberData(nameof(Refused))]
    public async Task A_token_that_fails_any_check_is_refused(string why, string token)
    {
        var response = await Client(token).GetAsync("/api/schema/tables", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, why);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        error!.Message.Should().Be("This token is not valid.", why);
    }

    [Theory]
    [InlineData("master")]
    [InlineData("service")]
    [InlineData("MASTER")]
    public async Task A_token_never_carries_a_role_that_bypasses_row_level_security(string role)
    {
        var token = Token("mallory", new Dictionary<string, object> { ["role"] = role });

        var response = await Client(token).GetAsync("/api/schema/tables", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Secrets_still_work_beside_tokens()
    {
        var response = await Client(MasterSecret).GetAsync("/api/schema/tables", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client(null).GetAsync("/api/schema/tables", TestContext.Current.CancellationToken)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "tokens add a way in; they do not lift enforcement");
    }

    // The subject reaches {{user_id}} and a claim reaches {{claims.x}} through a real request — the
    // same path a person's query takes.
    [Fact]
    public async Task The_subject_and_claims_of_a_token_reach_row_level_security()
    {
        var ct = TestContext.Current.CancellationToken;
        var master = Client(MasterSecret);
        var tableName = $"tok_rls_{Guid.NewGuid():N}"[..28];

        (await master.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns =
            [
                new CreateColumnApiRequest { Name = "owner", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "team", Type = "text", Nullable = false }
            ]
        }, ct)).EnsureSuccessStatusCode();

        foreach (var (owner, team) in new[] { ("alice", "red"), ("bob", "red"), ("carol", "blue") })
        {
            (await master.PostAsJsonAsync($"/api/data/{tableName}",
                new Dictionary<string, object?> { ["owner"] = owner, ["team"] = team }, ct)).StatusCode
                .Should().Be(HttpStatusCode.Created);
        }

        var policy = await master.PostAsJsonAsync("/api/security/policies", new
        {
            name = "own_rows_or_own_team",
            tableName,
            policyType = "Select",
            expression = "owner = {{user_id}} OR team = {{claims.team}}"
        }, ct);
        policy.StatusCode.Should().Be(HttpStatusCode.Created, await policy.Content.ReadAsStringAsync(ct));

        async Task<string[]> OwnersSeenBy(string token)
        {
            var page = await Client(token).GetFromJsonAsync<PagedResponse<DataRecordResponse>>($"/api/data/{tableName}", ct);
            return page!.Data.Select(r => r.Data["owner"]!.ToString()!).Order().ToArray();
        }

        (await OwnersSeenBy(Token("alice"))).Should().Equal("alice");
        (await OwnersSeenBy(Token("dave", new Dictionary<string, object> { ["team"] = "red" }))).Should().Equal("alice", "bob");
        (await OwnersSeenBy(Token("erin"))).Should().BeEmpty("a token is no master: the policy holds for it");
    }

    /// <summary>
    /// A token obtained by exchange names, in its <c>act</c> claim, the party it was issued to: a service
    /// acting for the person. The audit row keeps both — the person as the actor, the service as who acted
    /// for them — so a request an agent made on someone's behalf is not read as theirs alone.
    /// </summary>
    [Fact]
    public async Task Who_acted_for_a_tokens_subject_reaches_the_audit_trail()
    {
        var ct = TestContext.Current.CancellationToken;
        var person = $"person-{Guid.NewGuid():N}"[..20];
        var exchanged = Token(person, new Dictionary<string, object> { ["act"] = new Dictionary<string, object> { ["sub"] = "agent-7" } });
        var own = Token($"{person}-own");

        (await Client(exchanged).GetAsync("/api/schema/tables", ct)).EnsureSuccessStatusCode();
        (await Client(own).GetAsync("/api/schema/tables", ct)).EnsureSuccessStatusCode();

        (await ActedByAsync(person, ct)).Should().Be("agent-7");
        (await ActedByAsync($"{person}-own", ct)).Should().BeNull("a person acting for themselves has no one acting for them");
    }

    /// <summary>
    /// A project created by an earlier version has an audit table without the column; the bootstrap that
    /// runs on every start adds it, and running it again changes nothing.
    /// </summary>
    [Fact]
    public async Task An_audit_trail_from_an_earlier_version_gains_the_column_and_keeps_its_rows()
    {
        var ct = TestContext.Current.CancellationToken;
        (await Client(Token($"before-{Guid.NewGuid():N}"[..20])).GetAsync("/api/schema/tables", ct)).EnsureSuccessStatusCode();
        await using var connection = new NpgsqlConnection(_fixture.Postgres.ConnectionString);
        await connection.OpenAsync(ct);
        var schemas = (await connection.QueryAsync<string>(
            "SELECT table_schema FROM information_schema.columns WHERE table_name = '_audit_logs' AND column_name = 'acted_by'")).ToList();
        schemas.Should().NotBeEmpty();
        var rows = await connection.ExecuteScalarAsync<long>($"""SELECT count(*) FROM "{schemas[0]}"."_audit_logs" """);
        await connection.ExecuteAsync($"""ALTER TABLE "{schemas[0]}"."_audit_logs" DROP COLUMN acted_by""");

        await connection.ExecuteAsync(GlobalSchemaMigrations.BuildPreBootstrapDdl());
        await connection.ExecuteAsync(GlobalSchemaMigrations.BuildPreBootstrapDdl());

        (await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = @schema AND table_name = '_audit_logs' AND column_name = 'acted_by'",
            new { schema = schemas[0] })).Should().Be(1);
        (await connection.ExecuteScalarAsync<long>($"""SELECT count(*) FROM "{schemas[0]}"."_audit_logs" """)).Should().BeGreaterThanOrEqualTo(rows);
    }

    [Theory]
    [InlineData("""{"sub":"agent-7","act":{"sub":"saem"}}""", "agent-7")]
    [InlineData("""{"client_id":"x"}""", null)]
    [InlineData("\"agent-7\"", null)]
    [InlineData("not json", null)]
    public void The_party_acting_now_is_the_outermost_act(string act, string? expected) =>
        CredentialAuthenticationMiddleware.ActedByOf(new Dictionary<string, string> { ["act"] = act }).Should().Be(expected);

    private async Task<string?> ActedByAsync(string actor, CancellationToken ct)
    {
        // The audit trail is written in batches behind the request; the row arrives shortly after.
        var master = Client(MasterSecret);
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var page = await master.GetFromJsonAsync<System.Text.Json.JsonElement>(
                $"/api/projects/{_fixture.Api.ProjectId}/audit/logs?actorId={Uri.EscapeDataString(actor)}", ct);
            var items = page.GetProperty("items");
            if (items.GetArrayLength() > 0)
            {
                return items[0].TryGetProperty("actedBy", out var actedBy) && actedBy.ValueKind == System.Text.Json.JsonValueKind.String
                    ? actedBy.GetString()
                    : null;
            }

            await Task.Delay(200, ct);
        }

        throw new TimeoutException($"No audit row for '{actor}' arrived.");
    }
}
