using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// Deleting a row another row still references — through an enforcing relation, whose physical
/// constraint the database holds — is refused on every door with a message that says so: the row is
/// still referenced, and what the caller can do about it. It used to say the opposite, «a value in this
/// request references a row that does not exist», the message for a child written against a missing
/// parent, because both are the same PostgreSQL error code.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public sealed class ReferencedRowDeleteContractTests
{
    private readonly HttpClient _client;

    public ReferencedRowDeleteContractTests(ApiIntegrationFixture fixture) => _client = fixture.Api.Client;

    private sealed record Family(string Parents, string Children, Guid Parent, Guid Child);

    private async Task<Family> SetupAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var s = Guid.NewGuid().ToString("N")[..8];
        var parents = $"rr_parent_{s}";
        var children = $"rr_child_{s}";
        await CreatedAsync(_client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = parents,
            Columns = [new CreateColumnApiRequest { Name = "label", Type = "text" }],
        }, ct));
        await CreatedAsync(_client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = children,
            Columns = [new CreateColumnApiRequest { Name = "parent_id", Type = "uuid" }],
        }, ct));
        await CreatedAsync(_client.PostAsJsonAsync("/api/schema/relations", new CreateRelationApiRequest
        {
            Name = $"rr_rel_{s}",
            SourceTable = children,
            SourceColumn = "parent_id",
            TargetTable = parents,
            TargetColumn = "_id",
        }, ct));

        var parent = await InsertAsync(parents, new Dictionary<string, object?> { ["label"] = "p" });
        var child = await InsertAsync(children, new Dictionary<string, object?> { ["parent_id"] = parent });
        return new Family(parents, children, parent, child);
    }

    [Fact]
    public async Task A_rest_delete_of_a_referenced_row_says_it_is_still_referenced()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = await SetupAsync();

        var response = await _client.DeleteAsync($"/api/data/{f.Parents}/{f.Parent}", ct);
        var payload = await response.Content.ReadAsStringAsync(ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, payload);
        var error = JsonSerializer.Deserialize<ErrorResponse>(payload, JsonSerializerOptions.Web)!;
        error.Code.Should().Be("ROW_REFERENCED");
        error.Message.Should().Contain("still referenced").And.NotContain("does not exist");
        error.Message.Should().NotContain("rr_child", "a physical table name is not contract — and the caller did not name the child");
        (await _client.GetAsync($"/api/data/{f.Parents}/{f.Parent}", ct)).StatusCode.Should().Be(HttpStatusCode.OK, "nothing was deleted");

        // With the child gone, the parent deletes.
        (await _client.DeleteAsync($"/api/data/{f.Children}/{f.Child}", ct)).IsSuccessStatusCode.Should().BeTrue();
        (await _client.DeleteAsync($"/api/data/{f.Parents}/{f.Parent}", ct)).IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task An_odata_and_a_graphql_delete_of_a_referenced_row_say_the_same()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = await SetupAsync();
        var entitySet = string.Concat(f.Parents.Split('_').Select(p => p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant() : p));

        var odata = await _client.DeleteAsync($"/odata/{entitySet}({f.Parent})", ct);
        var odataPayload = await odata.Content.ReadAsStringAsync(ct);
        odata.StatusCode.Should().Be(HttpStatusCode.Conflict, odataPayload);
        JsonDocument.Parse(odataPayload).RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("ROW_REFERENCED");

        var graphql = await _client.PostAsJsonAsync("/graphql", new
        {
            query = "mutation($table: String!, $id: UUID!) { deleteRecord(table: $table, id: $id) { success errorCode error } }",
            variables = new { table = f.Parents, id = f.Parent },
        }, ct);
        var result = (await graphql.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("data").GetProperty("deleteRecord");
        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("ROW_REFERENCED");
        result.GetProperty("error").GetString().Should().Contain("still referenced");
    }

    [Fact]
    public async Task A_bulk_delete_that_reaches_a_referenced_row_says_so_and_deletes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = await SetupAsync();
        await InsertAsync(f.Parents, new Dictionary<string, object?> { ["label"] = "p" });

        var response = await _client.DeleteAsync($"/api/batch/data/{f.Parents}?filter=label:eq:p", ct);
        var payload = await response.Content.ReadAsStringAsync(ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, payload);
        payload.Should().Contain("ROW_REFERENCED");
        var left = await _client.GetFromJsonAsync<PagedResponse<DataRecordResponse>>($"/api/data/{f.Parents}", ct);
        left!.Data.Should().HaveCount(2, "the delete is one statement: the unreferenced row stays too");
    }

    [Fact]
    public async Task A_child_written_against_a_missing_parent_still_says_the_parent_does_not_exist()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = await SetupAsync();

        var response = await _client.PostAsJsonAsync($"/api/data/{f.Children}", new Dictionary<string, object?> { ["parent_id"] = Guid.NewGuid() }, ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(ct)).Should().NotContain("still referenced");
    }

    [Fact]
    public async Task An_unknown_on_delete_action_is_refused_not_read_as_no_action()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = await SetupAsync();

        var response = await _client.PostAsJsonAsync("/api/schema/relations", new CreateRelationApiRequest
        {
            Name = $"rr_typo_{Guid.NewGuid():N}"[..20],
            SourceTable = f.Children,
            SourceColumn = "parent_id",
            TargetTable = f.Parents,
            TargetColumn = "_id",
            OnDelete = "cascades",
        }, ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(ct)).Should().Contain("Unknown onDelete action 'cascades'");
    }

    private static async Task CreatedAsync(Task<HttpResponseMessage> request)
    {
        var response = await request;
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> InsertAsync(string table, Dictionary<string, object?> row)
    {
        var response = await _client.PostAsJsonAsync($"/api/data/{table}", row, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<DataRecordResponse>(TestContext.Current.CancellationToken))!.Id;
    }
}
