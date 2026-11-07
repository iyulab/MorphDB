using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MorphDB.Service.Controllers;
using MorphDB.Service.Filters;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A write door answers only the input it takes. A query parameter it does not declare, or a conditional
/// header it cannot honor, used to be dropped without a word — and the write went through. For
/// <c>?expectedVersion=3</c> or <c>If-Match</c> that is a lost update the caller asked to be protected
/// from: the precondition is not evaluated, the answer is still a success. The reads already refuse a
/// parameter they do not know (<c>fliter</c> narrows nothing); this holds the doors that change rows to the
/// same rule. The same call is sent through each REST door with an unknown parameter, then with each
/// conditional header, and the row is read back to show nothing was written.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class WriteDoorUnknownInputContractTests
{
    private readonly HttpClient _client;

    public WriteDoorUnknownInputContractTests(ApiIntegrationFixture fixture) => _client = fixture.Api.Client;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] ByCode = ["code"];

    private static readonly string[] CodeIsK = ["code:eq:k"];

    private async Task<(string Table, Guid Id)> TableWithOneRowAsync()
    {
        var table = $"doorin_{Guid.NewGuid():N}"[..30];
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = false, Unique = true },
                new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = false },
            ],
        }, Ct)).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync($"/api/data/{table}", new { code = "k", label = "v1" }, Ct)).EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await _client.GetStringAsync($"/api/data/{table}?filter=code:eq:k", Ct));
        return (table, doc.RootElement.GetProperty("data")[0].GetProperty("data").GetProperty("_id").GetGuid());
    }

    /// <summary>The table as the doors found it: the one row, at version 1, label unchanged.</summary>
    private async Task<string> SnapshotAsync(string table)
    {
        using var doc = JsonDocument.Parse(await _client.GetStringAsync($"/api/data/{table}?pageSize=100", Ct));
        var rows = doc.RootElement.GetProperty("data").EnumerateArray()
            .Select(r => r.GetProperty("data"))
            .Select(d => $"{d.GetProperty("code").GetString()}/{d.GetProperty("label").GetString()}/v{d.GetProperty("_version").GetInt64()}")
            .Order();
        return string.Join(" ", rows);
    }

    /// <summary>A call each door would answer with a success if it took no notice of the extra input.</summary>
    private static IEnumerable<(string Name, Func<string, Guid, string, HttpRequestMessage> Build)> Doors()
    {
        static HttpRequestMessage Json(HttpMethod method, string url, object body) =>
            new(method, url) { Content = JsonContent.Create(body) };

        static string With(string url, string extra) => extra.Length == 0 ? url : url + (url.Contains('?') ? "&" : "?") + extra;

        return
        [
            ("single insert", (t, _, q) => Json(HttpMethod.Post, With($"/api/data/{t}", q), new { code = "new", label = "n" })),
            ("rest patch", (t, id, q) => Json(HttpMethod.Patch, With($"/api/data/{t}/{id}", q), new { label = "changed" })),
            ("rest delete", (t, id, q) => new HttpRequestMessage(HttpMethod.Delete, With($"/api/data/{t}/{id}", q))),
            ("batch insert", (t, _, q) => Json(HttpMethod.Post, With($"/api/batch/data/{t}/insert", q), new[] { new { code = "new", label = "n" } })),
            ("batch operations", (t, _, q) => Json(HttpMethod.Post, With("/api/batch/data", q),
                new { operations = new[] { new { method = "INSERT", table = t, data = new { code = "new", label = "n" } } } })),
            ("rest put upsert", (t, _, q) => Json(HttpMethod.Put, With($"/api/batch/data/{t}", q),
                new { data = new { code = "k", label = "changed" }, keyColumns = ByCode })),
            ("seed", (t, _, q) => Json(HttpMethod.Post, With($"/api/batch/data/{t}/seed", q),
                new { records = new[] { new { code = "k", label = "changed" } }, keyColumns = ByCode })),
            ("update by filter", (t, _, q) => Json(HttpMethod.Patch, With($"/api/batch/data/{t}", q),
                new { data = new { label = "changed" }, filter = CodeIsK })),
            ("delete by filter", (t, _, q) => new HttpRequestMessage(HttpMethod.Delete, With($"/api/batch/data/{t}?filter=code:eq:k", q))),
            ("json import", (t, _, q) => new HttpRequestMessage(HttpMethod.Post, With($"/api/bulk/{t}/import/json", q))
            {
                Content = new StringContent("[{\"code\":\"new\",\"label\":\"n\"}]", Encoding.UTF8, "application/json"),
            }),
            ("ndjson import", (t, _, q) => new HttpRequestMessage(HttpMethod.Post, With($"/api/bulk/{t}/import/ndjson", q))
            {
                Content = new StringContent("{\"code\":\"new\",\"label\":\"n\"}\n", Encoding.UTF8, "application/x-ndjson"),
            }),
            ("csv import", (t, _, q) => new HttpRequestMessage(HttpMethod.Post, With($"/api/bulk/{t}/import/csv", q))
            {
                Content = new StringContent("code,label\nnew,n\n", Encoding.UTF8, "text/csv"),
            }),
        ];
    }

    [Fact]
    public async Task A_write_door_refuses_a_query_parameter_it_does_not_take_and_writes_nothing()
    {
        var failures = new List<string>();
        foreach (var (door, build) in Doors())
        {
            var (table, id) = await TableWithOneRowAsync();
            var before = await SnapshotAsync(table);

            using var response = await _client.SendAsync(build(table, id, "expectedVersion=1"), Ct);
            var said = await response.Content.ReadAsStringAsync(Ct);

            if (response.StatusCode != HttpStatusCode.BadRequest)
                failures.Add($"{door}: answered {(int)response.StatusCode}, expected 400 — {Trim(said)}");
            else if (!said.Contains("expectedVersion", StringComparison.Ordinal))
                failures.Add($"{door}: the refusal does not name the parameter — {Trim(said)}");

            var after = await SnapshotAsync(table);
            if (after != before)
                failures.Add($"{door}: the table changed anyway ({before} -> {after})");
        }

        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    [Theory]
    [InlineData("If-Match", "\"1\"")]
    [InlineData("If-None-Match", "*")]
    [InlineData("If-Unmodified-Since", "Sat, 07 Nov 2026 00:00:00 GMT")]
    public async Task A_write_door_refuses_a_conditional_header_it_cannot_honor_and_writes_nothing(string header, string value)
    {
        var failures = new List<string>();
        foreach (var (door, build) in Doors())
        {
            // The single-row update and delete evaluate If-Match against the row's entity tag — their contract is
            // RowEntityTagContractTests; every other door still has no precondition to evaluate.
            if (header == "If-Match" && door is "rest patch" or "rest delete")
                continue;

            var (table, id) = await TableWithOneRowAsync();
            var before = await SnapshotAsync(table);

            using var request = build(table, id, "");
            request.Headers.TryAddWithoutValidation(header, value);
            using var response = await _client.SendAsync(request, Ct);
            var said = await response.Content.ReadAsStringAsync(Ct);

            if (response.StatusCode != HttpStatusCode.BadRequest)
                failures.Add($"{door}: answered {(int)response.StatusCode}, expected 400 — {Trim(said)}");
            else if (!said.Contains(header, StringComparison.OrdinalIgnoreCase))
                failures.Add($"{door}: the refusal does not name the header — {Trim(said)}");

            var after = await SnapshotAsync(table);
            if (after != before)
                failures.Add($"{door}: the table changed anyway ({before} -> {after})");
        }

        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    [Fact]
    public async Task The_parameters_a_door_does_declare_still_pass()
    {
        var (table, id) = await TableWithOneRowAsync();

        // `ignoreUnknown` is declared on the insert and the update; `filter` on the delete by filter.
        using var insert = await _client.PostAsJsonAsync($"/api/data/{table}?ignoreUnknown=true", new { code = "n", label = "n", stray = 1 }, Ct);
        insert.EnsureSuccessStatusCode();
        using var update = await _client.PatchAsJsonAsync($"/api/data/{table}/{id}?ignoreUnknown=true", new { label = "changed", stray = 1 }, Ct);
        update.EnsureSuccessStatusCode();
        using var delete = await _client.DeleteAsync($"/api/batch/data/{table}?filter=code:eq:n", Ct);
        delete.EnsureSuccessStatusCode();

        (await SnapshotAsync(table)).Should().Be("k/changed/v2");
    }

    [Fact]
    public async Task A_record_declared_in_the_query_is_read_under_its_own_name_too()
    {
        var (table, _) = await TableWithOneRowAsync();

        // Model binding reads a record's members bare (`duplicateHandling`) and behind the parameter's name
        // (`options.duplicateHandling`); the second spelling binds, so it is not an unknown parameter.
        using var prefixed = await _client.PostAsync($"/api/bulk/{table}/import/json?options.duplicateHandling=insert",
            new StringContent("[{\"code\":\"new\",\"label\":\"n\"}]", Encoding.UTF8, "application/json"), Ct);
        prefixed.StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var stray = await _client.PostAsync($"/api/bulk/{table}/import/json?options.nope=1",
            new StringContent("[{\"code\":\"x\",\"label\":\"n\"}]", Encoding.UTF8, "application/json"), Ct);
        stray.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await stray.Content.ReadAsStringAsync(Ct)).Should().Contain("options.nope").And.Contain("duplicateHandling");
    }

    [Fact]
    public void The_controllers_that_change_rows_carry_the_filter_so_an_action_added_later_is_held_too()
    {
        // The matrix above sends through the routes that exist today. The filter sits on the controller, and this
        // keeps it there: a controller that lost it would silently take any parameter again. The transaction and
        // finalize routes are held by it without a row in the matrix; the OData routes answer their own system
        // options (ODataSystemOptions) and are not held by this filter.
        Type[] controllers = [typeof(DataController), typeof(BatchController), typeof(BulkController), typeof(TransactionController)];

        controllers.Where(c => c.GetCustomAttribute<RejectUnknownInputAttribute>() is null)
            .Should().BeEmpty("every controller that changes rows has to refuse the input it does not take");
    }

    private static string Trim(string said) => said.Length <= 200 ? said : said[..200] + "…";
}
