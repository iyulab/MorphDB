using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The read door of the GraphQL surface, held to the same rows the REST door reports.
/// <para>
/// A row's shape is not in the schema — that is the product — so every read carries it through the
/// <c>Any</c> scalar, and how <c>Any</c> maps to CLR values is a property of the GraphQL library,
/// not of this code. The write door had tests and the read door did not, so a library change that
/// broke reads would have left the suite green: the failure is a coercion error inside the
/// response payload, which is a `200 OK` on the wire.
/// </para>
/// <para>
/// Rows are compared against the REST reply rather than against literals, because the claim is not
/// "GraphQL returns this JSON" but "both doors report the same row".
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class GraphQlReadContractTests
{
    private readonly HttpClient _client;

    public GraphQlReadContractTests(ApiIntegrationFixture fixture)
    {
        _client = fixture.Api.Client;
    }

    [Fact]
    public async Task A_record_read_through_graphql_carries_the_same_fields_as_the_rest_reply()
    {
        var (table, id) = await SeedRowAsync("read-me", 7);

        var rest = await _client.GetFromJsonAsync<DataRecordResponse>($"/api/data/{table}/{id}");
        var gql = await PostGraphQlAsync(
            """
            query($table: String!, $id: UUID!) {
              record(table: $table, id: $id) { id data }
            }
            """,
            new { table, id });

        var data = gql.GetProperty("data").GetProperty("record");
        data.GetProperty("id").GetGuid().Should().Be(id);

        var row = data.GetProperty("data");
        FieldNames(row).Should().BeEquivalentTo(rest!.Data.Keys,
            "a row is the same row whichever door reports it — the Any scalar must not drop or " +
            "rename the caller's own columns");
        row.GetProperty("label").GetString().Should().Be("read-me");

        // Names agreeing is not values agreeing. A date and a decimal are where two serializers
        // diverge if they are going to: one writes an offset the other omits, one quotes a number
        // the other does not. Comparing against the REST reply keeps the claim as wide as stated.
        foreach (var field in new[] { "score", "amount", "at" })
        {
            row.GetProperty(field).GetRawText().Should().Be(
                JsonSerializer.Serialize(rest.Data[field]),
                $"both doors must report `{field}` the same way, not merely report it — the raw " +
                "JSON is compared so a number quoted on one door does not read as a match");
        }
    }

    [Fact]
    public async Task A_record_list_read_through_graphql_carries_its_rows()
    {
        var (table, id) = await SeedRowAsync("listed", 3);

        var gql = await PostGraphQlAsync(
            """
            query($table: String!) {
              records(table: $table, first: 10) {
                totalCount
                edges { node { id data } }
              }
            }
            """,
            new { table });

        var records = gql.GetProperty("data").GetProperty("records");
        records.GetProperty("totalCount").GetInt32().Should().Be(1);

        var node = records.GetProperty("edges")[0].GetProperty("node");
        node.GetProperty("id").GetGuid().Should().Be(id);
        node.GetProperty("data").GetProperty("label").GetString().Should().Be("listed");
    }

    /// <summary>
    /// Aggregation carries a row shape out through <c>Any</c> and a comparison value in through it,
    /// so it is the one read that exercises the boundary in both directions.
    /// </summary>
    [Fact]
    public async Task An_aggregate_read_carries_grouped_rows_and_accepts_a_filter_value()
    {
        var (table, _) = await SeedRowAsync("grouped", 5);
        await InsertAsync(table, "grouped", 9);
        await InsertAsync(table, "other", 1);

        var gql = await PostGraphQlAsync(
            """
            query($table: String!) {
              aggregate(
                table: $table,
                aggregations: [{ function: SUM, column: "score", alias: "total" }],
                groupBy: ["label"],
                filter: [{ column: "label", operator: "eq", value: "grouped" }]
              ) { data }
            }
            """,
            new { table });

        var rows = gql.GetProperty("data").GetProperty("aggregate").GetProperty("data");
        rows.GetArrayLength().Should().Be(1,
            "the filter value crosses the Any boundary too — a value that failed to convert would " +
            "either widen the result or fail the request");
        rows[0].GetProperty("total").GetInt32().Should().Be(14);
    }

    /// <summary>
    /// A filter value is optional in the schema — <c>isNull</c> and <c>isNotNull</c> have nothing
    /// to compare against. The CLR type behind the field decides that nullability, so a change of
    /// representation can quietly turn an optional input into a required one; nothing else here
    /// notices, because every other query supplies a value.
    /// </summary>
    [Fact]
    public async Task A_filter_may_omit_its_value()
    {
        var (table, _) = await SeedRowAsync("present", 1);

        var gql = await PostGraphQlAsync(
            """
            query($table: String!) {
              aggregate(
                table: $table,
                aggregations: [{ function: COUNT, alias: "n" }],
                filter: [{ column: "label", operator: "isnotnull" }]
              ) { data }
            }
            """,
            new { table });

        gql.GetProperty("data").GetProperty("aggregate").GetProperty("data")
            .GetArrayLength().Should().Be(1);
    }

    /// <summary>
    /// The schema is the contract a published image carries, and CLR nullability is what decides
    /// an input field's. Asking the server for the type directly is the only reading that cannot be
    /// satisfied by a query that happens to supply the field.
    /// </summary>
    [Fact]
    public async Task An_optional_input_field_stays_optional_in_the_schema()
    {
        var gql = await PostGraphQlAsync(
            """
            query {
              __type(name: "FilterInput") {
                inputFields { name type { kind name ofType { kind name } } }
              }
            }
            """,
            new { });

        var value = gql.GetProperty("data").GetProperty("__type").GetProperty("inputFields")
            .EnumerateArray().Single(f => f.GetProperty("name").GetString() == "value");

        value.GetProperty("type").GetProperty("kind").GetString().Should().NotBe("NON_NULL",
            "a filter value is optional — isNull and isNotNull have nothing to compare against, and " +
            "making it required would break every stored query that leaves it out");
    }

    private async Task<JsonElement> PostGraphQlAsync(string query, object variables)
    {
        var response = await _client.PostAsJsonAsync("/graphql", new { query, variables });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, payload);

        var root = JsonDocument.Parse(payload).RootElement.Clone();

        // A coercion failure is reported inside a 200 payload, so the transport status says
        // nothing. Read it here or every assertion below dies on a null `data`.
        root.TryGetProperty("errors", out var errors).Should().BeFalse(
            $"the read must resolve to say anything about the row: {errors}");

        return root;
    }

    private static IEnumerable<string> FieldNames(JsonElement row) =>
        row.EnumerateObject().Select(p => p.Name);

    private async Task<(string Table, Guid Id)> SeedRowAsync(string label, int score)
    {
        var table = $"gqlread_{Guid.NewGuid():N}"[..28];
        var created = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = true },
                new CreateColumnApiRequest { Name = "score", Type = "integer", Nullable = true },
                new CreateColumnApiRequest { Name = "amount", Type = "decimal", Nullable = true },
                new CreateColumnApiRequest { Name = "at", Type = "datetime", Nullable = true }
            ]
        });
        created.EnsureSuccessStatusCode();

        return (table, await InsertAsync(table, label, score));
    }

    private async Task<Guid> InsertAsync(string table, string label, int score)
    {
        var response = await _client.PostAsJsonAsync($"/api/data/{table}", new Dictionary<string, object?>
        {
            ["label"] = label,
            ["score"] = score,
            ["amount"] = 12.75m,
            ["at"] = "2026-08-04T09:30:00Z"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<DataRecordResponse>())!.Id;
    }
}
