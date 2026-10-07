using System.Net;
using System.Net.Http.Json;
using MorphDB.Core.Security;
using MorphDB.Service.Controllers;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The security surface after the authentication sunset: the policy endpoints answer any caller
/// (the service has no identity to demand), and row-level security still binds over HTTP because
/// the pipeline supplies the ambient security context the query layer evaluates against.
/// <para>
/// The second half is the one that has to be measured. An absent context does not fail a query —
/// it skips policy evaluation entirely ("allow all"), so losing the context middleware would not
/// break a single existing test; it would silently stop enforcing every policy. The RLS test here
/// goes red if <c>UseSecurityContext()</c> leaves the pipeline.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class SecurityApiTests
{
    private readonly HttpClient _client;

    public SecurityApiTests(ApiIntegrationFixture fixture)
    {
        _client = fixture.Api.Client;
    }

    private async Task<string> SetupTestTableAsync()
    {
        var tableName = $"rls_test_{Guid.NewGuid():N}"[..30];
        var response = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns =
            [
                new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false }
            ]
        });
        response.EnsureSuccessStatusCode();
        return tableName;
    }

    [Fact]
    public async Task The_policy_endpoints_answer_a_caller_with_no_identity()
    {
        var tableName = await SetupTestTableAsync();

        var response = await _client.GetAsync($"/api/security/policies/{tableName}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the service is unauthenticated by design; there is no identity it could demand");
    }

    [Fact]
    public async Task A_select_policy_binds_a_plain_http_query()
    {
        var tableName = await SetupTestTableAsync();

        var insert = await _client.PostAsJsonAsync($"/api/data/{tableName}",
            new Dictionary<string, object?> { ["name"] = "visible before the policy" }, TestContext.Current.CancellationToken);
        insert.StatusCode.Should().Be(HttpStatusCode.Created);

        var before = await _client.GetFromJsonAsync<PagedResponse<DataRecordResponse>>(
            $"/api/data/{tableName}", TestContext.Current.CancellationToken);
        before!.Data.Should().HaveCount(1, "the row exists and no policy restricts it yet");

        var policy = await _client.PostAsJsonAsync("/api/security/policies", new CreateSecurityPolicyRequest
        {
            Name = "nobody_reads",
            TableName = tableName,
            PolicyType = PolicyType.Select,
            Expression = "1 = 0"
        }, TestContext.Current.CancellationToken);
        policy.StatusCode.Should().Be(HttpStatusCode.Created);

        var after = await _client.GetFromJsonAsync<PagedResponse<DataRecordResponse>>(
            $"/api/data/{tableName}", TestContext.Current.CancellationToken);
        after!.Data.Should().BeEmpty(
            "the policy filters every row; if this holds rows, policy evaluation was skipped — " +
            "the ambient security context is not reaching the query layer");
    }

    /// <summary>
    /// A policy written as the documentation writes one — in the table's own column names, with
    /// <c>"policyType": "Select"</c> — binds every read of the table, one record at a time included.
    /// <para>
    /// Three things failed here before. The documented request was refused (the type travelled
    /// only as a number). A policy naming a column failed every list and aggregate with a 500 (the
    /// logical name reached SQL, and the table's columns have physical names). And a single record
    /// read past the policy entirely — by REST, GraphQL and OData alike — because it was its own
    /// statement rather than the query narrowed to an id.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_policy_naming_a_column_binds_lists_aggregates_and_every_single_record_read()
    {
        var ct = TestContext.Current.CancellationToken;
        var tableName = await SetupTestTableAsync();
        var visible = await InsertAsync(tableName, "open");
        var hidden = await InsertAsync(tableName, "closed");

        var policy = await _client.PostAsJsonAsync("/api/security/policies", new
        {
            name = "open_only",
            tableName,
            policyType = "Select",
            expression = "name = 'open'"
        }, ct);
        policy.StatusCode.Should().Be(HttpStatusCode.Created, await policy.Content.ReadAsStringAsync(ct));
        (await policy.Content.ReadAsStringAsync(ct)).Should().Contain("\"policyType\":\"Select\"",
            "the type answers in the form it was asked in");

        var list = await _client.GetFromJsonAsync<PagedResponse<DataRecordResponse>>($"/api/data/{tableName}", ct);
        list!.Data.Select(r => r.Id).Should().Equal(visible);

        var count = await _client.PostAsJsonAsync($"/api/data/{tableName}/aggregate",
            new { aggregations = new[] { new { function = "count", alias = "n" } } }, ct);
        count.StatusCode.Should().Be(HttpStatusCode.OK);
        (await count.Content.ReadAsStringAsync(ct)).Should().Contain("\"n\":1");

        (await _client.GetAsync($"/api/data/{tableName}/{visible}", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.GetAsync($"/api/data/{tableName}/{hidden}", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a row the policy hides is not there for a single read either");

        var gql = await _client.PostAsJsonAsync("/graphql", new
        {
            query = "query($table: String!, $id: UUID!) { record(table: $table, id: $id) { id } }",
            variables = new { table = tableName, id = hidden },
        }, ct);
        using (var body = System.Text.Json.JsonDocument.Parse(await gql.Content.ReadAsStringAsync(ct)))
        {
            body.RootElement.GetProperty("data").GetProperty("record").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        }

        var entitySet = string.Concat(tableName.Split('_').Select(p => p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant() : p));
        (await _client.GetAsync($"/odata/{entitySet}({hidden})", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("Insert")]
    [InlineData("Update")]
    [InlineData("Delete")]
    [InlineData("All")]
    public async Task A_policy_for_writes_is_refused_while_no_write_enforces_one(string policyType)
    {
        var tableName = await SetupTestTableAsync();

        var response = await _client.PostAsJsonAsync("/api/security/policies", new
        {
            name = "writes",
            tableName,
            policyType,
            expression = "name IS NOT NULL"
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("VALIDATION_ERROR");
    }

    [Fact]
    public async Task A_policy_naming_a_column_the_table_lacks_is_refused_when_registered()
    {
        var tableName = await SetupTestTableAsync();

        var response = await _client.PostAsJsonAsync("/api/security/policies", new
        {
            name = "typo",
            tableName,
            policyType = "Select",
            expression = "nmae = 'open'"
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("INVALID_EXPRESSION");
        (await _client.GetAsync($"/api/data/{tableName}", TestContext.Current.CancellationToken)).StatusCode
            .Should().Be(HttpStatusCode.OK, "a refused policy was never stored, so the table still reads");
    }

    private async Task<Guid> InsertAsync(string tableName, string name)
    {
        var response = await _client.PostAsJsonAsync($"/api/data/{tableName}",
            new Dictionary<string, object?> { ["name"] = name }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<DataRecordResponse>(TestContext.Current.CancellationToken))!.Id;
    }
}
