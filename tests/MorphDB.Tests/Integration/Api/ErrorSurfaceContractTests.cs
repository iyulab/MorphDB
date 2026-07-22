using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// Pins the error surface a caller actually experiences, per the acceptance table of
/// issue <c>caller-errors-surface-as-500</c>: a caller's mistake must answer 4xx with a code and a
/// hint, never a 500 — and no path may ever answer a 5xx with an empty body. Each test here was a
/// live-probed defect on 0.7.1 (500 INTERNAL_ERROR, one with no body at all).
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ErrorSurfaceContractTests
{
    private readonly HttpClient _client;

    public ErrorSurfaceContractTests(ApiIntegrationFixture fixture)
    {
        _client = fixture.Api.Client;
    }

    private async Task<string> CreateTableAsync(params CreateColumnApiRequest[] columns)
    {
        var tableName = $"errsurf_{Guid.NewGuid():N}"[..30];
        var response = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns = columns
        });
        response.EnsureSuccessStatusCode();
        return tableName;
    }

    /// <summary>
    /// Live probe #5: an unknown column type answered a 500 with an empty body — the worst possible
    /// reply. SchemaController's catch chain never named ArgumentException and no global handler
    /// existed, so the framework's bodyless default answered.
    /// </summary>
    [Fact]
    public async Task CreateTable_WithUnknownColumnType_IsA400_ListingTheSupportedTypes()
    {
        var response = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = $"errsurf_{Guid.NewGuid():N}"[..30],
            Columns = [new CreateColumnApiRequest { Name = "a", Type = "varchar2" }]
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Code.Should().Be("INVALID_ARGUMENT");
        body.Message.Should().Contain("varchar2").And.Contain("Supported types",
            "the caller should learn what is possible, not just that they failed");
    }

    /// <summary>
    /// Live probe #4: an explicit null into a nullable:false column reached PostgreSQL and its
    /// 23502 surfaced as INTERNAL_ERROR. The physical violation is still the caller's mistake —
    /// it now translates to the same ValidationError the app-layer validators produce, naming the
    /// logical column.
    /// </summary>
    [Fact]
    public async Task Insert_ExplicitNullIntoNotNullableColumn_IsA400_NamingTheColumn()
    {
        var table = await CreateTableAsync(
            new CreateColumnApiRequest { Name = "title", Type = "text", Nullable = false });

        var response = await _client.PostAsJsonAsync($"/api/data/{table}",
            new Dictionary<string, object?> { ["title"] = null });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Error.Should().NotBe("InternalError", "a caller's null is not our defect");
        (body.Message ?? "").Should().Contain("title", "the caller must learn which column rejected the null");
    }

    /// <summary>
    /// Live probe #3: an unknown filter operator used to fall back to Equals silently — a typo
    /// became a different query. It is now a 400 listing what the API supports.
    /// </summary>
    [Fact]
    public async Task Query_WithUnknownFilterOperator_IsA400_ListingTheSupportedOperators()
    {
        var table = await CreateTableAsync(
            new CreateColumnApiRequest { Name = "email", Type = "text", Nullable = true });

        var response = await _client.GetAsync($"/api/data/{table}?filter=email:zz:1");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Message.Should().Contain("zz").And.Contain("Supported operators");
    }

    /// <summary>
    /// The bulk routes used to recognise a missing table by sniffing "not found" in an
    /// ArgumentException message. The service layer now throws the domain type, so the answer is
    /// the documented 404 — by type, not by wording.
    /// </summary>
    [Fact]
    public async Task BulkImport_IntoMissingTable_IsA404_WithTheDocumentedCode()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/bulk/no_such_table/import/json",
            new[] { new Dictionary<string, object?> { ["a"] = 1 } });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Code.Should().Be("TABLE_NOT_FOUND");
    }

    /// <summary>
    /// Live probe #2: a well-formed filter naming a column the table does not have flowed straight
    /// into SQL, and PostgreSQL's 42703 surfaced as a 500. The builder now validates the name before
    /// it can reach physical SQL — the caller's typo is a 400 that names the column.
    /// </summary>
    [Fact]
    public async Task Query_WithUnknownFilterColumn_IsA400_NamingTheColumn()
    {
        var table = await CreateTableAsync(
            new CreateColumnApiRequest { Name = "email", Type = "text", Nullable = true });

        var response = await _client.GetAsync($"/api/data/{table}?filter=nosuch:eq:1");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Code.Should().Be("COLUMN_NOT_FOUND");
        body.Message.Should().Contain("nosuch");
    }

    /// <summary>
    /// Live probe #1: a syntactically valid X-Project-Id that no project bears answered a 500.
    /// A project that does not exist has no tables, so the request lands on the documented 404 —
    /// and the message must not echo the project GUID (hidden-layer sweep).
    /// </summary>
    [Fact]
    public async Task Query_AgainstNonexistentProject_IsA404_WithoutEchoingTheGuid()
    {
        var ghostProject = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/data/customers");
        request.Headers.Add("X-Project-Id", ghostProject.ToString());

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Code.Should().Be("TABLE_NOT_FOUND");
        (body.Message ?? "").Should().NotContain(ghostProject.ToString(),
            "internal identifiers do not belong in error text");
    }

    /// <summary>
    /// A typo'd field used to answer 201 while its value silently vanished — data loss dressed as
    /// success, discovered only when a later query came back empty. The write boundary now fails
    /// loudly, naming the field, unless the caller explicitly opts into dropping.
    /// </summary>
    [Fact]
    public async Task Insert_WithUnknownField_IsA400_NamingTheField()
    {
        var table = await CreateTableAsync(
            new CreateColumnApiRequest { Name = "email", Type = "text", Nullable = true });

        var response = await _client.PostAsJsonAsync($"/api/data/{table}",
            new Dictionary<string, object?> { ["email"] = "bob@example.com", ["emial_typo"] = "lost?" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        (body!.Message ?? "").Should().Contain("emial_typo");
        body.Code.Should().Be("UNKNOWN_COLUMN",
            "docs/API.md promises exactly this code for a write naming an undeclared column");
    }

    [Fact]
    public async Task Insert_WithUnknownField_AndExplicitOptIn_Succeeds_KeepingDeclaredFields()
    {
        var table = await CreateTableAsync(
            new CreateColumnApiRequest { Name = "email", Type = "text", Nullable = true });

        var response = await _client.PostAsJsonAsync($"/api/data/{table}?ignoreUnknown=true",
            new Dictionary<string, object?> { ["email"] = "bob@example.com", ["emial_typo"] = "dropped-by-consent" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<DataRecordResponse>();
        body!.Data.Should().ContainKey("email");
        body.Data.Should().NotContainKey("emial_typo");
    }

    [Fact]
    public async Task Update_WithUnknownField_IsA400()
    {
        var table = await CreateTableAsync(
            new CreateColumnApiRequest { Name = "email", Type = "text", Nullable = true });
        var created = await _client.PostAsJsonAsync($"/api/data/{table}",
            new Dictionary<string, object?> { ["email"] = "a@example.com" });
        var record = await created.Content.ReadFromJsonAsync<DataRecordResponse>();

        var response = await _client.PatchAsJsonAsync($"/api/data/{table}/{record!.Id}",
            new Dictionary<string, object?> { ["emial_typo"] = "lost?" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Protocol parity: the batch upsert door used to bypass the write pipeline entirely
    /// (IMorphDataService built its own SQL), so the same typo that insert rejected was silently
    /// dropped here. Every door now goes through the same validators — this pins the second door.
    /// </summary>
    [Fact]
    public async Task BatchUpsert_WithUnknownField_IsA400_LikeEveryOtherDoor()
    {
        var table = await CreateTableAsync(
            new CreateColumnApiRequest { Name = "email", Type = "text", Nullable = true, Unique = true });

        var response = await _client.PutAsJsonAsync($"/api/batch/data/{table}", new
        {
            data = new Dictionary<string, object?> { ["email"] = "b@example.com", ["emial_typo"] = "lost?" },
            keyColumns = (string[])["email"]
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        (body!.Message ?? "").Should().Contain("emial_typo");
    }

    /// <summary>
    /// Hidden-layer sweep: a rejected CHECK expression must quote what the caller wrote, not the
    /// physically-renamed form — the physical column names (`col_*`) are internals the API user
    /// must never need to know exist.
    /// </summary>
    [Fact]
    public async Task RejectedCheckExpression_QuotesTheCallersText_NotPhysicalNames()
    {
        var response = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = $"errsurf_{Guid.NewGuid():N}"[..30],
            Columns = [new CreateColumnApiRequest { Name = "a", Type = "integer", Check = "a > (1" }]
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        (body!.Message ?? "").Should().Contain("a > (1", "the caller must recognise their own expression");
        (body.Message ?? "").Should().NotContain("col_", "physical column names are not contract");
    }

    /// <summary>
    /// The bulk UPDATE door stays a single SQL statement (not per-row pipeline). Its parameter
    /// preparation has always rejected unknown columns — this pins that the contract holds there
    /// too, so it cannot regress toward the executor's historical silent skip.
    /// </summary>
    [Fact]
    public async Task BatchUpdate_WithUnknownField_IsA400()
    {
        var table = await CreateTableAsync(
            new CreateColumnApiRequest { Name = "score", Type = "integer", Nullable = true });
        await _client.PostAsJsonAsync($"/api/data/{table}",
            new Dictionary<string, object?> { ["score"] = 1 });

        var response = await _client.PatchAsJsonAsync($"/api/batch/data/{table}", new
        {
            data = new Dictionary<string, object?> { ["scoer_typo"] = 100 },
            filter = "score:lt:50"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        (body!.Message ?? "").Should().Contain("scoer_typo");
    }

    /// <summary>
    /// The floor under everything above: even a genuine defect must answer with the INTERNAL_ERROR
    /// envelope — an unknown filter column still reaches PostgreSQL today (validation moves ahead
    /// of the builder in a later cycle), so it is exactly the path that used to depend on the
    /// framework default. The status may improve to 400 later; the envelope must never be empty.
    /// </summary>
    [Fact]
    public async Task NoErrorPath_AnswersA5xx_WithAnEmptyBody()
    {
        var table = await CreateTableAsync(
            new CreateColumnApiRequest { Name = "email", Type = "text", Nullable = true });

        var response = await _client.GetAsync($"/api/data/{table}?filter=nosuch:eq:1");

        if ((int)response.StatusCode >= 500)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            body.Should().NotBeNull("a 5xx with no body gives the caller nothing to branch on");
            body!.Code.Should().Be("INTERNAL_ERROR");
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }
}
