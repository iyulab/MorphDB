using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The write contract must be one contract, however a row arrives. Run 8 converged every write
/// door onto the pipeline; these tests measure that the convergence holds *as observed on the
/// wire* — the same bad row is refused through REST and through GraphQL for the same reason, and
/// a row accepted through GraphQL carries the same pipeline-applied system columns as one
/// accepted through REST. Per-door unit coverage cannot catch the doors drifting apart, which is
/// exactly how the pre-Run-8 defects (silent drops on one door, enforcement on another) lived.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class GraphQlWriteContractTests
{
    private readonly HttpClient _client;

    public GraphQlWriteContractTests(ApiIntegrationFixture fixture)
    {
        _client = fixture.Api.Client;
    }

    private async Task<string> CreateTableAsync(params CreateColumnApiRequest[] columns)
    {
        var tableName = $"gqlwrite_{Guid.NewGuid():N}"[..30];
        var response = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns = columns
        });
        response.EnsureSuccessStatusCode();
        return tableName;
    }

    private async Task<JsonElement> PostGraphQlAsync(string query, object variables)
    {
        var response = await _client.PostAsJsonAsync("/graphql", new { query, variables });
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "mutation-level failures are reported in the payload, not as transport errors");

        var payload = await response.Content.ReadAsStringAsync();
        var document = JsonDocument.Parse(payload);
        var root = document.RootElement.Clone();

        // A request-level failure (a schema or coercion error) leaves `data` null, and every
        // assertion below then dies reading a property off null — which says nothing about what
        // went wrong. Report what the server said instead.
        root.TryGetProperty("errors", out var errors).Should().BeFalse(
            $"the mutation must reach the resolver to say anything about the write: {errors}");

        return root;
    }

    private const string CreateRecordMutation = """
        mutation($table: String!, $data: Any!) {
          createRecord(table: $table, data: $data) {
            success
            error
            errorCode
            data { id data }
          }
        }
        """;

    [Fact]
    public async Task An_unknown_field_is_refused_through_both_doors_with_the_same_code()
    {
        var table = await CreateTableAsync(
            new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false });
        var row = new Dictionary<string, object?> { ["name"] = "ok", ["ghost"] = 1 };

        var rest = await _client.PostAsJsonAsync($"/api/data/{table}", row);
        rest.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var restBody = await rest.Content.ReadFromJsonAsync<ErrorResponse>();
        restBody!.Message.Should().Contain("ghost");

        var gql = await PostGraphQlAsync(CreateRecordMutation, new { table, data = row });
        var result = gql.GetProperty("data").GetProperty("createRecord");
        result.GetProperty("success").GetBoolean().Should().BeFalse(
            "GraphQL must not accept a row REST refuses");
        result.GetProperty("error").GetString().Should().Contain("ghost");

        result.GetProperty("errorCode").GetString().Should().Be(restBody.Code,
            "the two doors must refuse the same row for the same stated reason");
    }

    [Fact]
    public async Task An_explicit_null_into_a_required_column_is_refused_through_both_doors()
    {
        var table = await CreateTableAsync(
            new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false });
        var row = new Dictionary<string, object?> { ["name"] = null };

        var rest = await _client.PostAsJsonAsync($"/api/data/{table}", row);
        rest.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var restBody = await rest.Content.ReadFromJsonAsync<ErrorResponse>();
        restBody!.Message.Should().Contain("name");

        var gql = await PostGraphQlAsync(CreateRecordMutation, new { table, data = row });
        var result = gql.GetProperty("data").GetProperty("createRecord");
        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("error").GetString().Should().Contain("name");
        result.GetProperty("errorCode").GetString().Should().Be(restBody.Code);
    }

    [Fact]
    public async Task A_row_created_through_graphql_carries_the_same_system_columns_as_one_created_through_rest()
    {
        var table = await CreateTableAsync(
            new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false });

        var restInsert = await _client.PostAsJsonAsync($"/api/data/{table}",
            new Dictionary<string, object?> { ["name"] = "via rest" });
        restInsert.StatusCode.Should().Be(HttpStatusCode.Created);
        var restRow = (await restInsert.Content.ReadFromJsonAsync<DataRecordResponse>())!;

        var gql = await PostGraphQlAsync(CreateRecordMutation,
            new { table, data = new Dictionary<string, object?> { ["name"] = "via graphql" } });
        var result = gql.GetProperty("data").GetProperty("createRecord");
        result.GetProperty("success").GetBoolean().Should().BeTrue(
            result.TryGetProperty("error", out var err) ? err.ToString() : null);
        var gqlId = result.GetProperty("data").GetProperty("id").GetGuid();
        gqlId.Should().NotBeEmpty("the pipeline, not the database, issues the id on every door");

        // Read both rows back through the same (REST) door and compare shapes there, so the
        // comparison cannot be confused by per-door serialization.
        var restReadBack = await _client.GetFromJsonAsync<DataRecordResponse>(
            $"/api/data/{table}/{restRow.Id}");
        var gqlReadBack = await _client.GetFromJsonAsync<DataRecordResponse>(
            $"/api/data/{table}/{gqlId}");

        gqlReadBack!.Data.Keys.Should().BeEquivalentTo(restReadBack!.Data.Keys,
            "a row is the same row whichever door admitted it — same declared and system columns");
    }
}
