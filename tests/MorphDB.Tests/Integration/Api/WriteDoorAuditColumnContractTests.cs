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
/// A table with audit fields on records who changed a row: <c>_created_by</c> once, <c>_updated_by</c>
/// at every change. The system columns are applied the same way through every door that changes a row
/// (docs/API.md, Write Options) — so a person's token that updates a row through any of them leaves
/// that person in <c>_updated_by</c>, and the creator's <c>_created_by</c> untouched. Run with a real
/// token, since an anonymous request has no one to record.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class WriteDoorAuditColumnContractTests
{
    private const string MasterSecret = "mdb_audit_test_master_secret";
    private const string Issuer = "https://issuer.example.test/realm";
    private const string Audience = "morphdb";

    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "issuer-key" };
    private static readonly string[] ByCode = ["code"];
    private static readonly string[] CodeIsK = ["code:eq:k"];

    private readonly ApiIntegrationFixture _fixture;

    public WriteDoorAuditColumnContractTests(ApiIntegrationFixture fixture) => _fixture = fixture;

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

    private delegate Task Door(HttpClient person, string table, Guid id);

    private static async Task Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        var said = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.Should().BeTrue(said);
        said.Should().NotContain("\"success\":false");
    }

    private static IEnumerable<(string Name, Door Send)> Doors() =>
    [
        ("rest patch", (p, t, id) => Ok(p.PatchAsJsonAsync($"/api/data/{t}/{id}", new { label = "changed" }, Ct))),
        ("update by filter", (p, t, id) => Ok(p.PatchAsJsonAsync($"/api/batch/data/{t}", new { data = new { label = "changed" }, filter = CodeIsK }, Ct))),
        ("rest put upsert", (p, t, id) => Ok(p.PutAsJsonAsync($"/api/batch/data/{t}", new { data = new { code = "k", label = "changed" }, keyColumns = ByCode }, Ct))),
        ("batch operations update", (p, t, id) => Ok(p.PostAsJsonAsync("/api/batch/data",
            new { operations = new[] { new { method = "UPDATE", table = t, id, data = new { label = "changed" } } } }, Ct))),
        ("batch operations upsert", (p, t, id) => Ok(p.PostAsJsonAsync("/api/batch/data",
            new { operations = new[] { new { method = "UPSERT", table = t, data = new { code = "k", label = "changed" }, keyColumns = ByCode } } }, Ct))),
        ("seed", (p, t, id) => Ok(p.PostAsJsonAsync($"/api/batch/data/{t}/seed",
            new { records = new[] { new { code = "k", label = "changed" } }, keyColumns = ByCode }, Ct))),
    ];

    [Fact]
    public async Task A_person_who_changes_a_row_through_any_door_is_the_one_recorded_as_having_changed_it()
    {
        var master = As(MasterSecret);
        var failures = new List<string>();
        foreach (var (door, send) in Doors())
        {
            var table = $"dooraud_{Guid.NewGuid():N}"[..30];
            (await master.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
            {
                Name = table,
                SystemColumns = new SystemColumnOptionsApiRequest { AuditFields = true },
                Columns =
                [
                    new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = false, Unique = true },
                    new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = false },
                ],
            }, Ct)).EnsureSuccessStatusCode();

            var creator = Guid.NewGuid();
            var editor = Guid.NewGuid();
            await Ok(As(Token(creator)).PostAsJsonAsync($"/api/data/{table}", new { code = "k", label = "orig" }, Ct));
            var id = (await ReadAsync(master, table)).Id;

            await send(As(Token(editor)), table, id);
            var after = await ReadAsync(master, table);

            if (after.Label != "changed")
                failures.Add($"{door}: the change was not written");
            if (after.UpdatedBy != editor)
                failures.Add($"{door}: _updated_by is {after.UpdatedBy?.ToString() ?? "null"}, expected the editor {editor}");
            if (after.CreatedBy != creator)
                failures.Add($"{door}: _created_by is {after.CreatedBy?.ToString() ?? "null"}, expected the creator {creator}");
        }

        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    /// <summary>
    /// A table with ownership on gives a row its creator as owner (<c>_owner_id</c>) and the owner does not
    /// change when someone else edits it — an upsert that lands on the row is an edit, not a new row.
    /// </summary>
    [Fact]
    public async Task The_person_who_created_a_row_stays_its_owner_when_another_person_changes_it_through_any_door()
    {
        var master = As(MasterSecret);
        var failures = new List<string>();
        foreach (var (door, send) in Doors())
        {
            var table = $"doorown_{Guid.NewGuid():N}"[..30];
            (await master.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
            {
                Name = table,
                SystemColumns = new SystemColumnOptionsApiRequest { Ownership = true },
                Columns =
                [
                    new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = false, Unique = true },
                    new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = false },
                ],
            }, Ct)).EnsureSuccessStatusCode();

            var creator = Guid.NewGuid();
            var editor = Guid.NewGuid();
            await Ok(As(Token(creator)).PostAsJsonAsync($"/api/data/{table}", new { code = "k", label = "orig" }, Ct));
            var created = await ReadOwnerAsync(master, table);
            if (created.Owner != creator)
                failures.Add($"{door}: the creator {creator} is not the owner after the insert ({created.Owner})");

            await send(As(Token(editor)), table, created.Id);
            var after = await ReadOwnerAsync(master, table);
            if (after.Label != "changed")
                failures.Add($"{door}: the change was not written");
            if (after.Owner != creator)
                failures.Add($"{door}: _owner_id is {after.Owner?.ToString() ?? "null"} after another person's change, expected the creator {creator}");
        }

        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    /// <summary>
    /// A delete on a table with soft delete and audit fields on marks the row and records who deleted it
    /// (<c>_deleted_by</c>) — the same person whichever door the delete came through.
    /// </summary>
    [Fact]
    public async Task A_person_who_deletes_a_row_through_any_door_is_the_one_recorded_as_having_deleted_it()
    {
        var master = As(MasterSecret);
        var deletes = new (string Name, Func<HttpClient, string, Guid, Task> Send)[]
        {
            ("rest delete", (p, t, id) => Ok(p.DeleteAsync($"/api/data/{t}/{id}", Ct))),
            ("delete by filter", (p, t, id) => Ok(p.DeleteAsync($"/api/batch/data/{t}?filter=code:eq:k", Ct))),
            ("batch operations delete", (p, t, id) => Ok(p.PostAsJsonAsync("/api/batch/data",
                new { operations = new[] { new { method = "DELETE", table = t, id } } }, Ct))),
        };

        var failures = new List<string>();
        foreach (var (door, send) in deletes)
        {
            var table = $"doordel_{Guid.NewGuid():N}"[..30];
            (await master.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
            {
                Name = table,
                SystemColumns = new SystemColumnOptionsApiRequest { AuditFields = true, SoftDelete = true },
                Columns =
                [
                    new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = false, Unique = true },
                    new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = false },
                ],
            }, Ct)).EnsureSuccessStatusCode();

            await Ok(master.PostAsJsonAsync($"/api/data/{table}", new { code = "k", label = "orig" }, Ct));
            var id = (await ReadAsync(master, table)).Id;
            var deleter = Guid.NewGuid();
            await send(As(Token(deleter)), table, id);

            using var doc = JsonDocument.Parse(await master.GetStringAsync($"/api/data/{table}?filter=code:eq:k&includeDeleted=true", Ct));
            var data = doc.RootElement.GetProperty("data");
            if (data.GetArrayLength() != 1)
            { failures.Add($"{door}: the row is not there with includeDeleted"); continue; }
            var row = data[0].GetProperty("data");
            if (!row.TryGetProperty("_deleted_at", out var at) || at.ValueKind == JsonValueKind.Null)
                failures.Add($"{door}: the row was not marked deleted");
            else if (!row.TryGetProperty("_deleted_by", out var by) || by.ValueKind != JsonValueKind.String || by.GetGuid() != deleter)
                failures.Add($"{door}: _deleted_by is {(row.TryGetProperty("_deleted_by", out var x) ? x.ToString() : "absent")}, expected the deleter {deleter}");
        }

        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    private sealed record OwnerRow(Guid Id, string Label, Guid? Owner);

    private static async Task<OwnerRow> ReadOwnerAsync(HttpClient client, string table)
    {
        using var doc = JsonDocument.Parse(await client.GetStringAsync($"/api/data/{table}?filter=code:eq:k", Ct));
        var d = doc.RootElement.GetProperty("data")[0].GetProperty("data");
        Guid? owner = d.TryGetProperty("_owner_id", out var v) && v.ValueKind == JsonValueKind.String ? v.GetGuid() : null;
        return new OwnerRow(d.GetProperty("_id").GetGuid(), d.GetProperty("label").GetString()!, owner);
    }

    private sealed record Row(Guid Id, string Label, Guid? CreatedBy, Guid? UpdatedBy);

    private static async Task<Row> ReadAsync(HttpClient client, string table)
    {
        using var doc = JsonDocument.Parse(await client.GetStringAsync($"/api/data/{table}?filter=code:eq:k", Ct));
        var d = doc.RootElement.GetProperty("data")[0].GetProperty("data");
        Guid? Guid_(string name) => d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetGuid() : null;
        return new Row(d.GetProperty("_id").GetGuid(), d.GetProperty("label").GetString()!, Guid_("_created_by"), Guid_("_updated_by"));
    }
}
