using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Core.Abstractions;
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
/// The second half is the one that has to be measured, and it is measured over HTTP on purpose: a
/// policy is enforced by PostgreSQL against whatever caller the pipeline wrote to the connection,
/// so these tests go red if a route stops reaching the table as its caller.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class SecurityApiTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;

    public SecurityApiTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
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

    /// <summary>
    /// A policy on reads binds writes too: a hidden row is not there to be changed, and a write
    /// whose result its caller could not read is refused rather than answered with a row the
    /// policy hides.
    /// </summary>
    [Fact]
    public async Task A_policy_binds_updates_deletes_and_inserts_over_http()
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

        var update = await _client.PatchAsJsonAsync($"/api/data/{tableName}/{hidden}",
            new Dictionary<string, object?> { ["name"] = "open" }, ct);
        update.StatusCode.Should().Be(HttpStatusCode.NotFound, "a hidden row is not there to be updated");

        (await _client.DeleteAsync($"/api/data/{tableName}/{hidden}", ct)).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "nor to be deleted");

        var insertHidden = await _client.PostAsJsonAsync($"/api/data/{tableName}",
            new Dictionary<string, object?> { ["name"] = "closed" }, ct);
        insertHidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await insertHidden.Content.ReadAsStringAsync(ct)).Should().Contain("FORBIDDEN");

        var hide = await _client.PatchAsJsonAsync($"/api/data/{tableName}/{visible}",
            new Dictionary<string, object?> { ["name"] = "closed" }, ct);
        hide.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the update would hide the row from its own caller");

        // A write the caller can read back is unaffected.
        await InsertAsync(tableName, "open");
        var list = await _client.GetFromJsonAsync<PagedResponse<DataRecordResponse>>($"/api/data/{tableName}", ct);
        list!.Data.Should().HaveCount(2);
    }

    /// <summary>
    /// A policy for writes binds the write it is for, and only that one: an insert of a row it does
    /// not admit is refused, an update or delete of such a row finds no row, and an update that
    /// would leave such a row is refused. Reads are restricted by <c>All</c> alone.
    /// </summary>
    [Theory]
    [InlineData("Insert")]
    [InlineData("Update")]
    [InlineData("Delete")]
    [InlineData("All")]
    public async Task A_policy_for_writes_binds_the_write_it_is_for_over_http(string policyType)
    {
        var ct = TestContext.Current.CancellationToken;
        var tableName = await SetupTestTableAsync();
        var open = await InsertAsync(tableName, "open");
        var closed = await InsertAsync(tableName, "closed");

        var policy = await _client.PostAsJsonAsync("/api/security/policies", new
        {
            name = "open_only",
            tableName,
            policyType,
            expression = "name = 'open'"
        }, ct);
        policy.StatusCode.Should().Be(HttpStatusCode.Created, await policy.Content.ReadAsStringAsync(ct));
        (await policy.Content.ReadAsStringAsync(ct)).Should().Contain($"\"policyType\":\"{policyType}\"");

        var all = policyType == "All";
        var inserts = all || policyType == "Insert";
        var updates = all || policyType == "Update";
        var deletes = all || policyType == "Delete";

        var list = await _client.GetFromJsonAsync<PagedResponse<DataRecordResponse>>($"/api/data/{tableName}", ct);
        list!.Data.Should().HaveCount(all ? 1 : 2, "only a policy for every command restricts reading");

        var insertClosed = await _client.PostAsJsonAsync($"/api/data/{tableName}",
            new Dictionary<string, object?> { ["name"] = "closed" }, ct);
        insertClosed.StatusCode.Should().Be(inserts ? HttpStatusCode.Forbidden : HttpStatusCode.Created);

        var updateClosed = await _client.PatchAsJsonAsync($"/api/data/{tableName}/{closed}",
            new Dictionary<string, object?> { ["name"] = "closed" }, ct);
        updateClosed.StatusCode.Should().Be(updates ? HttpStatusCode.NotFound : HttpStatusCode.OK);

        // The row can be read, so every route that updates by id reads it and then finds no row to
        // update. Each answers what it answers for a record that is not there.
        if (policyType == "Update")
        {
            (await updateClosed.Content.ReadAsStringAsync(ct)).Should().Contain("RECORD_NOT_FOUND");

            var gql = await _client.PostAsJsonAsync("/graphql", new
            {
                query = "mutation($table: String!, $id: UUID!, $data: Any!) " +
                        "{ updateRecord(table: $table, id: $id, data: $data) { success errorCode } }",
                variables = new { table = tableName, id = closed, data = new { name = "closed" } },
            }, ct);
            using (var body = System.Text.Json.JsonDocument.Parse(await gql.Content.ReadAsStringAsync(ct)))
            {
                var result = body.RootElement.GetProperty("data").GetProperty("updateRecord");
                result.GetProperty("success").GetBoolean().Should().BeFalse();
                result.GetProperty("errorCode").GetString().Should().Be("NOT_FOUND");
            }

            var entitySet = string.Concat(tableName.Split('_').Select(p => p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant() : p));
            using var patch = new HttpRequestMessage(HttpMethod.Patch, $"/odata/{entitySet}({closed})")
            {
                Content = JsonContent.Create(new Dictionary<string, object?> { ["name"] = "closed" })
            };
            (await _client.SendAsync(patch, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

            // And what it answers for an entity that was never there.
            using var patchNothing = new HttpRequestMessage(HttpMethod.Patch, $"/odata/{entitySet}({Guid.NewGuid()})")
            {
                Content = JsonContent.Create(new Dictionary<string, object?> { ["name"] = "closed" })
            };
            (await _client.SendAsync(patchNothing, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        var close = await _client.PatchAsJsonAsync($"/api/data/{tableName}/{open}",
            new Dictionary<string, object?> { ["name"] = "closed" }, ct);
        close.StatusCode.Should().Be(updates ? HttpStatusCode.Forbidden : HttpStatusCode.OK);
        if (updates)
        {
            (await close.Content.ReadAsStringAsync(ct)).Should().Contain("FORBIDDEN");
        }

        (await _client.DeleteAsync($"/api/data/{tableName}/{closed}", ct)).StatusCode
            .Should().Be(deletes ? HttpStatusCode.NotFound : HttpStatusCode.NoContent);
    }

    /// <summary>
    /// A bulk job is requested by an HTTP request and carried out after that request is gone. The
    /// request's caller travels with the job: here the policy admits a row only to a caller working
    /// in the table's project, which the request is and whatever carries the job out is not.
    /// <para>
    /// The test host does not run the job processor, so the job is carried out here, through the
    /// service method the processor calls — from a test, which like the processor has no caller.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_bulk_export_requested_over_http_is_carried_out_as_the_request_that_asked_for_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var tableName = await SetupTestTableAsync();
        await InsertAsync(tableName, "one");
        await InsertAsync(tableName, "two");

        var policy = await _client.PostAsJsonAsync("/api/security/policies", new
        {
            name = "this_project_only",
            tableName,
            policyType = "Select",
            expression = "{{project_id}} = project_id"
        }, ct);
        policy.StatusCode.Should().Be(HttpStatusCode.Created, await policy.Content.ReadAsStringAsync(ct));

        var requested = await _client.PostAsJsonAsync(
            $"/api/bulk/{tableName}/export/csv", new CsvExportApiRequest { Columns = ["name"] }, ct);
        requested.StatusCode.Should().Be(HttpStatusCode.Accepted, await requested.Content.ReadAsStringAsync(ct));
        var job = (await requested.Content.ReadFromJsonAsync<ExportJobApiResponse>(ct))!;
        job.TotalRows.Should().Be(2);

        var bulk = _fixture.Api.Services.GetRequiredService<IBulkOperationService>();
        using var file = new MemoryStream();
        await bulk.StreamExportAsync(job.JobId, file, ct);

        Encoding.UTF8.GetString(file.ToArray()).Should().Contain("one").And.Contain("two");
        (await requested.Content.ReadAsStringAsync(ct)).Should().NotContain("requestedBy").And.NotContain("requested_by");
    }

    [Fact]
    public async Task A_policy_of_an_unknown_type_is_refused_when_registered()
    {
        var tableName = await SetupTestTableAsync();

        var response = await _client.PostAsJsonAsync("/api/security/policies", new
        {
            name = "no_such_type",
            tableName,
            policyType = 9,
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
