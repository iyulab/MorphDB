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

        var rest = await _client.GetFromJsonAsync<DataRecordResponse>($"/api/data/{table}/{id}", TestContext.Current.CancellationToken);
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

    [Fact]
    public async Task A_cursor_the_server_did_not_issue_is_refused_not_read_as_the_first_page()
    {
        // The cursor used to decode to Guid.Empty on any parse failure, so a malformed `after`
        // silently restarted the list from the beginning -- a well-formed answer to a malformed
        // request. It is a GraphQL error now, with a code the caller can branch on.
        var (table, _) = await SeedRowAsync("cursored", 2);

        var response = await _client.PostAsJsonAsync("/graphql", new
        {
            query = """
                query($table: String!) {
                  records(table: $table, first: 10, after: "not-a-cursor") { totalCount }
                }
                """,
            variables = new { table }
        }, TestContext.Current.CancellationToken);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;

        root.TryGetProperty("errors", out var errors).Should().BeTrue("a cursor this server never issued must not resolve");
        errors[0].GetProperty("extensions").GetProperty("code").GetString().Should().Be("INVALID_CURSOR");
        errors[0].GetProperty("message").GetString().Should().Contain("not-a-cursor");
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

    private const string Page =
        """
        query($table: String!, $first: Int, $after: String, $orderBy: String) {
          records(table: $table, first: $first, after: $after, orderBy: $orderBy) {
            totalCount
            pageInfo { hasNextPage endCursor }
            edges { node { id data } }
          }
        }
        """;

    /// <summary>
    /// Following <c>endCursor</c> from the first page to the last reads every row once, in the order
    /// asked for — the default (newest first), an ascending and a descending column order, and an
    /// order with ties — and <c>hasNextPage</c> is false exactly on the last page, also when the rows
    /// fill it to the brim.
    /// </summary>
    [Theory]
    [InlineData(null, 7, 3)]
    [InlineData("score:asc", 7, 3)]
    [InlineData("score:desc", 7, 3)]
    [InlineData("label:asc", 6, 3)]
    [InlineData("label:desc,score:asc", 6, 2)]
    public async Task Following_end_cursors_reads_every_row_once_in_order(string? orderBy, int rows, int pageSize)
    {
        var (table, first) = await SeedRowAsync("p0", 0);
        var inserted = new List<Guid> { first };
        for (var i = 1; i < rows; i++)
        {
            // Labels tie in pairs, so an order by label alone is not total.
            inserted.Add(await InsertAsync(table, $"p{i / 2}", i * 10 % 7));
        }

        // GraphQL's default is newest first; the REST list is asked for the same order explicitly.
        var expected = await RestOrderAsync(table, orderBy ?? "_created_at:desc,_id:desc");
        var seen = new List<Guid>();
        string? after = null;
        for (var page = 0; page < rows + 1; page++)
        {
            var records = (await PostGraphQlAsync(Page, new { table, first = pageSize, after, orderBy }))
                .GetProperty("data").GetProperty("records");
            records.GetProperty("totalCount").GetInt32().Should().Be(rows);
            var ids = records.GetProperty("edges").EnumerateArray().Select(e => e.GetProperty("node").GetProperty("id").GetGuid()).ToList();
            seen.AddRange(ids);

            var info = records.GetProperty("pageInfo");
            var more = info.GetProperty("hasNextPage").GetBoolean();
            more.Should().Be(seen.Count < rows, $"page {page} ends at row {seen.Count} of {rows}");
            if (!more)
            {
                break;
            }

            after = info.GetProperty("endCursor").GetString();
        }

        seen.Should().Equal(expected, "every row once, in the order the REST list reads them");
        seen.Should().BeEquivalentTo(inserted);
    }

    /// <summary>The rows' ids in the order the REST list answers for the same ordering (one page).</summary>
    private async Task<List<Guid>> RestOrderAsync(string table, string? orderBy)
    {
        var url = $"/api/data/{table}?pageSize=100" + (orderBy is null ? "" : $"&orderBy={orderBy}");
        var page = await _client.GetFromJsonAsync<JsonElement>(url, TestContext.Current.CancellationToken);
        return [.. page.GetProperty("data").EnumerateArray().Select(r => r.GetProperty("id").GetGuid())];
    }

    private const string FilteredList =
        """
        query($table: String!, $filter: [String!], $orderBy: String) {
          records(table: $table, first: 10, filter: $filter, orderBy: $orderBy) { totalCount edges { node { data } } }
        }
        """;

    /// <summary>
    /// <c>records</c> reads its filter with the REST list's code: one condition per entry, every
    /// condition applies, and only the first two colons separate — so a value may hold a comma. It
    /// used to split one string on commas, which cut such a value in two and dropped the halves.
    /// </summary>
    [Fact]
    public async Task Records_reads_one_condition_per_filter_entry_and_a_value_may_hold_a_comma()
    {
        var (table, _) = await SeedRowAsync("a,b", 3);
        await InsertAsync(table, "a", 3);
        await InsertAsync(table, "a,b", 8);

        var both = (await PostGraphQlAsync(FilteredList, new { table, filter = Conditions("label:eq:a,b", "score:lt:5") }))
            .GetProperty("data").GetProperty("records");
        both.GetProperty("totalCount").GetInt32().Should().Be(1, "both conditions apply, and the comma is part of the value");

        // A single string is a list of one (GraphQL input coercion) — a query written against the old
        // single-string argument still reads, as one condition.
        var single = await PostGraphQlAsync(
            $$"""query { records(table: "{{table}}", filter: "label:eq:a,b") { totalCount } }""", new { });
        single.GetProperty("data").GetProperty("records").GetProperty("totalCount").GetInt32().Should().Be(2);

        var isnull = (await PostGraphQlAsync(FilteredList, new { table, filter = Conditions("label:isnotnull") }))
            .GetProperty("data").GetProperty("records");
        isnull.GetProperty("totalCount").GetInt32().Should().Be(3, "the operators are the REST set, isnull and isnotnull included");
    }

    /// <summary>
    /// A condition the server cannot read is refused, not skipped: a skipped condition answers a wider
    /// question than the one asked. An unknown operator used to read as <c>eq</c> and a malformed
    /// condition used to vanish; an unknown order direction used to read as ascending.
    /// </summary>
    [Theory]
    [InlineData("""filter: ["label"]""", "Invalid filter format")]
    [InlineData("""filter: ["label:between:1"]""", "Unknown filter operator")]
    [InlineData("""filter: ["label:isnull:x"]""", "takes no value")]
    [InlineData("""orderBy: "score:sideways" """, "the direction is 'asc' or 'desc'")]
    public async Task Records_refuses_a_condition_or_order_it_cannot_read(string arguments, string message)
    {
        var (table, _) = await SeedRowAsync("refused", 1);

        var error = await GraphQlErrorAsync($$"""query { records(table: "{{table}}", {{arguments}}) { totalCount } }""");

        error.GetProperty("extensions").GetProperty("code").GetString().Should().Be("INVALID_FILTER");
        error.GetProperty("message").GetString().Should().Contain(message);
    }

    /// <summary>
    /// The structured filter, having and order of <c>aggregate</c> read operators and directions with the
    /// REST parser too, and a refusal carries REST's code rather than an unexplained execution error.
    /// </summary>
    [Theory]
    [InlineData("""filter: [{ column: "label", operator: "between", value: 1 }]""", "Unknown filter operator")]
    [InlineData("""having: [{ alias: "n", operator: "around", value: 1 }]""", "Unknown filter operator")]
    [InlineData("""orderBy: [{ column: "label", direction: "up" }]""", "the direction is 'asc' or 'desc'")]
    public async Task Aggregate_refuses_an_operator_or_direction_it_cannot_read(string arguments, string message)
    {
        var (table, _) = await SeedRowAsync("refused", 1);

        var error = await GraphQlErrorAsync(
            $$"""query { aggregate(table: "{{table}}", aggregations: [{ function: COUNT, alias: "n" }], groupBy: ["label"], {{arguments}}) { data } }""");

        error.GetProperty("extensions").GetProperty("code").GetString().Should().Be("INVALID_ARGUMENT");
        error.GetProperty("message").GetString().Should().Contain(message);
    }

    [Fact]
    public async Task A_caller_mistake_inside_a_resolver_carries_the_rest_code()
    {
        var (table, _) = await SeedRowAsync("named", 1);

        var error = await GraphQlErrorAsync(
            $$"""query { aggregate(table: "{{table}}", aggregations: [{ function: SUM, column: "no_such", alias: "s" }]) { data } }""");

        error.GetProperty("extensions").GetProperty("code").GetString().Should().Be("COLUMN_NOT_FOUND");
    }

    private async Task<JsonElement> GraphQlErrorAsync(string query)
    {
        var response = await _client.PostAsJsonAsync("/graphql", new { query }, TestContext.Current.CancellationToken);
        var payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var root = JsonDocument.Parse(payload).RootElement.Clone();
        root.TryGetProperty("errors", out var errors).Should().BeTrue($"the request must be refused: {payload}");
        return errors[0];
    }

    private static string[] Conditions(params string[] conditions) => conditions;

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
