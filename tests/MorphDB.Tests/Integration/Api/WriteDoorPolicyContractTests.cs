using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MorphDB.Service.Models.Api;
using MorphDB.Service.Security;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A row-level policy on a table says whose rows a person may read or change. It holds through every door that
/// changes a row — the single update, the update by filter, the batch update, and the three upserts, whose
/// statement lands on an existing row by a conflict and not by the row's id. A person the policy does not let
/// change the row leaves it as it was whichever door they use; the person it lets change it can use every door.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class WriteDoorPolicyContractTests
{
    private const string MasterSecret = "mdb_policy_test_master_secret";
    private const string Issuer = "https://issuer.example.test/realm";
    private const string Audience = "morphdb";

    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "issuer-key" };
    private static readonly string[] ByCode = ["code"];
    private static readonly string[] CodeIsK = ["code:eq:k"];

    private readonly ApiIntegrationFixture _fixture;

    public WriteDoorPolicyContractTests(ApiIntegrationFixture fixture) => _fixture = fixture;

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

    private static string Token(Guid subject)
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Claims = new Dictionary<string, object> { [JwtRegisteredClaimNames.Sub] = subject.ToString() },
            IssuedAt = now.AddMinutes(-10),
            NotBefore = now.AddMinutes(-10),
            Expires = now.AddMinutes(10),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
        });
    }

    // `owner` is the one an upsert names for the row it would create: the insert half of an upsert has to be a valid row of its own.
    private delegate Task<HttpResponseMessage> Door(HttpClient person, string table, Guid id, string label, string owner);

    private static IEnumerable<(string Name, Door Send)> Doors() =>
    [
        ("rest patch", (p, t, id, label, owner) => p.PatchAsJsonAsync($"/api/data/{t}/{id}", new { label }, Ct)),
        ("update by filter", (p, t, id, label, owner) => p.PatchAsJsonAsync($"/api/batch/data/{t}", new { data = new { label }, filter = CodeIsK }, Ct)),
        ("batch operations update", (p, t, id, label, owner) => p.PostAsJsonAsync("/api/batch/data",
            new { operations = new[] { new { method = "UPDATE", table = t, id, data = new { label } } } }, Ct)),
        ("rest put upsert", (p, t, id, label, owner) => p.PutAsJsonAsync($"/api/batch/data/{t}", new { data = new { code = "k", label, owner }, keyColumns = ByCode }, Ct)),
        ("batch operations upsert", (p, t, id, label, owner) => p.PostAsJsonAsync("/api/batch/data",
            new { operations = new[] { new { method = "UPSERT", table = t, data = new { code = "k", label, owner }, keyColumns = ByCode } } }, Ct)),
        ("seed", (p, t, id, label, owner) => p.PostAsJsonAsync($"/api/batch/data/{t}/seed",
            new { records = new[] { new { code = "k", label, owner } }, keyColumns = ByCode }, Ct)),
    ];

    /// <summary>The kinds of policy that say whose rows a person may change; each reads the row's own <c>owner</c>.</summary>
    public static IEnumerable<object[]> Policies() =>
    [
        ["Update"],
        ["All"],
    ];

    private async Task<(HttpClient Master, string Table)> TableWithPolicyAsync(string policyType)
    {
        var master = As(MasterSecret);
        var table = $"doorpol_{Guid.NewGuid():N}"[..30];
        (await master.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = false, Unique = true },
                new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "owner", Type = "text", Nullable = false },
            ],
        }, Ct)).EnsureSuccessStatusCode();
        (await master.PostAsJsonAsync("/api/security/policies",
            new { name = "own_rows", tableName = table, policyType, expression = "owner = {{user_id}}" }, Ct)).EnsureSuccessStatusCode();
        return (master, table);
    }

    private static async Task<(Guid Id, string Label)> ReadAsync(HttpClient master, string table)
    {
        using var doc = JsonDocument.Parse(await master.GetStringAsync($"/api/data/{table}?filter=code:eq:k", Ct));
        var d = doc.RootElement.GetProperty("data")[0].GetProperty("data");
        return (d.GetProperty("_id").GetGuid(), d.GetProperty("label").GetString()!);
    }

    [Theory]
    [MemberData(nameof(Policies))]
    public async Task A_person_the_policy_does_not_let_change_a_row_leaves_it_as_it_was_through_every_door(string policyType)
    {
        var failures = new List<string>();
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        foreach (var (door, send) in Doors())
        {
            var (master, table) = await TableWithPolicyAsync(policyType);
            (await master.PostAsJsonAsync($"/api/data/{table}", new { code = "k", label = "orig", owner = owner.ToString() }, Ct)).EnsureSuccessStatusCode();
            var id = (await ReadAsync(master, table)).Id;

            var response = await send(As(Token(stranger)), table, id, "changed-by-a-stranger", stranger.ToString());
            var said = await response.Content.ReadAsStringAsync(Ct);
            var after = await ReadAsync(master, table);

            if (after.Label != "orig")
                failures.Add($"{door}: the stranger changed the row to '{after.Label}' ({(int)response.StatusCode})");
            if ((int)response.StatusCode >= 500)
                failures.Add($"{door}: refusing the stranger answered {(int)response.StatusCode}: {said}");
            if (said.Contains("unexpected error", StringComparison.OrdinalIgnoreCase))
                failures.Add($"{door}: a refusal the caller can act on is reported as an unexpected error: {said}");
        }

        failures.Should().BeEmpty($"under a {policyType} policy:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(Policies))]
    public async Task The_person_the_policy_lets_change_a_row_can_through_every_door(string policyType)
    {
        var failures = new List<string>();
        var owner = Guid.NewGuid();
        foreach (var (door, send) in Doors())
        {
            var (master, table) = await TableWithPolicyAsync(policyType);
            (await master.PostAsJsonAsync($"/api/data/{table}", new { code = "k", label = "orig", owner = owner.ToString() }, Ct)).EnsureSuccessStatusCode();
            var id = (await ReadAsync(master, table)).Id;

            var response = await send(As(Token(owner)), table, id, "changed-by-the-owner", owner.ToString());
            var said = await response.Content.ReadAsStringAsync(Ct);
            var after = await ReadAsync(master, table);

            if (after.Label != "changed-by-the-owner")
                failures.Add($"{door}: the owner's change was not written ({(int)response.StatusCode}: {said})");
        }

        failures.Should().BeEmpty($"under a {policyType} policy:\n" + string.Join("\n", failures));
    }
}
