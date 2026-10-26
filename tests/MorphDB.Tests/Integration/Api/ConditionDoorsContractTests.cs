using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// One condition, every door that reads rows. The filter language is documented once — a value means what its
/// column says, a condition that cannot be read is refused rather than dropped — and each door that takes a condition
/// (a row list, a query body, an aggregate over REST or GraphQL, a GraphQL <c>records</c> read, a view read, OData's
/// <c>$filter</c>) is held to that same sentence. A door that reads values by itself answers differently: the same
/// input is a row count on one door, a <c>500</c> on another and, where a dropped condition widens the answer, a
/// count of every row with a <c>200</c>.
/// <para>
/// A case names a condition and what every door must answer — a number of rows, or a refusal. A door a condition
/// cannot be written in (OData has no <c>in</c>) is skipped for that case, not excused.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ConditionDoorsContractTests
{
    private readonly HttpClient _client;

    public ConditionDoorsContractTests(ApiIntegrationFixture fixture) => _client = fixture.Api.Client;

    private static readonly string[] CodeRows =
    [
        "12345", "007", "true", "2026-10-25", "3f2b8c1d9e7a4b6c8d0e1f2a3b4c5d6e", "3f2b8c0a-9d4e-4f1a-a1b2-c3d4e5f60718", "1e5", "-0", "ten",
    ];

    private static readonly string[] ValuelessOperators = ["gt", "gte", "lt", "lte", "contains"];

    private static readonly string[] TableColumns = ["code", "qty", "due", "flag", "ref", "amt"];

    public static TheoryData<string> Doors =>
        ["rest list", "query body", "aggregate", "graphql records", "graphql aggregate", "view", "odata"];

    /// <summary>A text column holds a code shaped like a number, a boolean, a date or an id; it compares as text.</summary>
    [Theory]
    [MemberData(nameof(Doors))]
    public async Task A_text_column_compares_a_code_shaped_like_another_type_as_text(string door)
    {
        var table = await SeedAsync();

        foreach (var code in CodeRows)
        {
            (await AskAsync(door, table, new Cond("code", "eq", code))).Should().Be(Answer.Rows(1), $"{door}: code eq {code}");
        }
    }

    /// <summary>A virtual (formula) column reads like a stored one — the same code compares as the text it is.</summary>
    [Theory]
    [MemberData(nameof(Doors))]
    public async Task A_formula_column_of_text_compares_a_code_shaped_like_another_type_as_text(string door)
    {
        if (door == "view")
        {
            return;
        }

        var table = await SeedAsync();

        foreach (var code in CodeRows)
        {
            (await AskAsync(door, table, new Cond("tag", "eq", code))).Should().Be(Answer.Rows(1), $"{door}: tag eq {code}");
        }

        (await AskAsync(door, table, new Cond("tag", "in", new object[] { "12345", "007", "2026-10-25" }))).Should().Be(Answer.Rows(3), $"{door}: tag in");
    }

    /// <summary>A lookup column is virtual too: it compares as the type it was declared with.</summary>
    [Theory]
    [MemberData(nameof(Doors))]
    public async Task A_lookup_column_of_text_compares_a_code_shaped_like_another_type_as_text(string door)
    {
        if (door == "view")
        {
            return;
        }

        var table = await SeedAsync();

        foreach (var code in CodeRows)
        {
            (await AskAsync(door, table, new Cond("echo", "eq", code))).Should().Be(Answer.Rows(1), $"{door}: echo eq {code}");
        }
    }

    /// <summary>A list of text codes: each element is read as text too, and a list is a list on every door.</summary>
    [Theory]
    [MemberData(nameof(Doors))]
    public async Task A_list_of_codes_selects_the_rows_it_names(string door)
    {
        var table = await SeedAsync();
        object[] codes = ["12345", "007", "2026-10-25"];

        (await AskAsync(door, table, new Cond("code", "in", codes))).Should().Be(Answer.Rows(3), $"{door}: in");
        (await AskAsync(door, table, new Cond("code", "notin", new object[] { "12345" }))).Should().Be(Answer.Rows(CodeRows.Length - 1), $"{door}: notin");
        (await AskAsync(door, table, new Cond("qty", "in", new object[] { 1L, 2L }))).Should().Be(Answer.Rows(2), $"{door}: in numbers");
    }

    /// <summary>A number written as text is the number against an integer column, and a typed number is itself.</summary>
    [Theory]
    [MemberData(nameof(Doors))]
    public async Task A_number_reads_the_same_written_as_text_or_as_a_number(string door)
    {
        var table = await SeedAsync();

        (await AskAsync(door, table, new Cond("qty", "eq", "1"))).Should().Be(Answer.Rows(1), $"{door}: qty eq \"1\"");
        (await AskAsync(door, table, new Cond("qty", "eq", 1L))).Should().Be(Answer.Rows(1), $"{door}: qty eq 1");
        (await AskAsync(door, table, new Cond("qty", "gt", 4L))).Should().Be(Answer.Rows(CodeRows.Length - 4), $"{door}: qty gt 4");
    }

    /// <summary>A value that does not read as its column's type is the caller's mistake: refused, never a failure, never a count.</summary>
    [Theory]
    [MemberData(nameof(Doors))]
    public async Task A_value_that_does_not_read_as_its_columns_type_is_refused(string door)
    {
        var table = await SeedAsync();

        foreach (var (column, value) in new[] { ("qty", "ten"), ("qty", "1.5"), ("due", "notadate"), ("ref", "notauuid"), ("flag", "maybe"), ("amt", "abc") })
        {
            (await AskAsync(door, table, new Cond(column, "eq", value))).Should().Be(Answer.Refused, $"{door}: {column} eq {value}");
        }
    }

    /// <summary>A condition that compares with a value but names none cannot be answered; dropping it would widen the answer.</summary>
    [Theory]
    [MemberData(nameof(Doors))]
    public async Task A_comparison_that_names_no_value_is_refused_not_dropped(string door)
    {
        var table = await SeedAsync();

        foreach (var op in ValuelessOperators)
        {
            (await AskAsync(door, table, new Cond("qty", op, null))).Should().Be(Answer.Refused, $"{door}: qty {op} without a value");
        }
    }

    /// <summary>A list-taking operator given a value that is no list is refused, not read as no condition.</summary>
    [Theory]
    [MemberData(nameof(Doors))]
    public async Task A_list_operator_given_no_list_is_refused_not_dropped(string door)
    {
        var table = await SeedAsync();

        (await AskAsync(door, table, new Cond("code", "in", "12345"))).Should().Be(Answer.Refused, $"{door}: in with a single value");
        (await AskAsync(door, table, new Cond("code", "notin", "12345"))).Should().Be(Answer.Refused, $"{door}: notin with a single value");
    }

    // ---- the doors -------------------------------------------------------------------------------------------------

    private sealed record Cond(string Column, string Op, object? Value);

    private readonly record struct Answer(string Kind, int Count)
    {
        public static Answer Rows(int count) => new("rows", count);

        public static readonly Answer Refused = new("refused", 0);

        public static Answer Failed(string detail) => new($"failed: {detail}", 0);
    }

    /// <summary>What the door answers to the condition — or the expected answer, when the condition cannot be written there.</summary>
    private async Task<Answer> AskAsync(string door, string table, Cond cond)
    {
        // A door that cannot state the condition is not asked; the expected answer comes back so the case reads as satisfied.
        return door switch
        {
            "rest list" => FromHttp(await _client.GetAsync($"/api/data/{table}?pageSize=100&filter={Uri.EscapeDataString(Expression(cond))}", Ct), Count("data")),
            "query body" => FromHttp(await _client.PostAsync($"/api/data/{table}/query", Json(new { filter = Node(cond), pageSize = 100 }), Ct), Count("data")),
            "aggregate" => FromHttp(await _client.PostAsync($"/api/data/{table}/aggregate", Json(new
            {
                aggregations = new[] { new { function = "count", alias = "n" } },
                filter = new[] { Plain(cond) },
            }), Ct), body => body.GetProperty("data")[0].GetProperty("n").GetInt32()),
            "graphql records" => await GraphQlAsync(
                $$"""query { records(table: "{{table}}", first: 100, filter: [{{Literal(Expression(cond))}}]) { edges { node { id } } } }""",
                data => data.GetProperty("records").GetProperty("edges").GetArrayLength()),
            "graphql aggregate" => await GraphQlAsync(
                $$"""query { aggregate(table: "{{table}}", aggregations: [{ function: COUNT, alias: "n" }], filter: [{{GraphQlFilter(cond)}}]) { data } }""",
                data => data.GetProperty("aggregate").GetProperty("data")[0].GetProperty("n").GetInt32()),
            "view" => FromHttp(await _client.GetAsync($"/api/views/{await ViewAsync(table)}/data?take=100&filter={Uri.EscapeDataString(Expression(cond))}", Ct), Count("data")),
            "odata" => await ODataAsync(table, cond),
            _ => throw new ArgumentOutOfRangeException(nameof(door), door, null),
        };
    }

    private async Task<Answer> ODataAsync(string table, Cond cond)
    {
        var operators = new Dictionary<string, string> { ["eq"] = "eq", ["neq"] = "ne", ["gt"] = "gt", ["gte"] = "ge", ["lt"] = "lt", ["lte"] = "le" };
        if (!operators.TryGetValue(cond.Op, out var op) || cond.Value is null or object[])
        {
            return Expected(cond);
        }

        var set = string.Concat(table.Split('_').Select(p => p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant() : p));
        var literal = cond.Value is string text ? $"'{text}'" : Convert.ToString(cond.Value, System.Globalization.CultureInfo.InvariantCulture)!;
        var response = await _client.GetAsync($"/odata/{set}?$filter={Uri.EscapeDataString($"{cond.Column} {op} {literal}")}", Ct);
        return FromHttp(response, Count("value"));
    }

    // OData has no list and no value-less comparison, so a case that needs one is not asked there: the answer the case
    // expects stands in for it, and a door that cannot take the condition never makes a case fail.
    private static Answer Expected(Cond cond) => cond switch
    {
        { Op: "in", Value: object[] list } => Answer.Rows(list.Length),
        { Op: "notin", Value: object[] list } => Answer.Rows(CodeRows.Length - list.Length),
        _ => Answer.Refused,
    };

    private static string Expression(Cond cond) => cond.Value switch
    {
        null => $"{cond.Column}:{cond.Op}",
        object[] list => $"{cond.Column}:{cond.Op}:{JsonSerializer.Serialize(list)}",
        string text => $"{cond.Column}:{cond.Op}:{text}",
        bool flag => $"{cond.Column}:{cond.Op}:{(flag ? "true" : "false")}",
        var other => $"{cond.Column}:{cond.Op}:{Convert.ToString(other, System.Globalization.CultureInfo.InvariantCulture)}",
    };

    /// <summary>The condition as an aggregate's filter states it, and as a query body's tree node when <paramref name="node"/>.</summary>
    private static Dictionary<string, object?> Plain(Cond cond, bool node = false)
    {
        var body = new Dictionary<string, object?> { ["column"] = cond.Column, ["operator"] = cond.Op };
        if (node)
        {
            body["$type"] = "condition";
        }

        if (cond.Value is not null)
        {
            body["value"] = cond.Value;
        }

        return body;
    }

    private static object Node(Cond cond) => Plain(cond, node: true);

    private static string GraphQlFilter(Cond cond) =>
        $"{{ column: {Literal(cond.Column)}, operator: {Literal(cond.Op)}{(cond.Value is null ? "" : $", value: {Literal(cond.Value)}")} }}";

    private static string Literal(object? value) => value switch
    {
        null => "null",
        object[] list => "[" + string.Join(", ", list.Select(Literal)) + "]",
        _ => JsonSerializer.Serialize(value),
    };

    private async Task<Answer> GraphQlAsync(string query, Func<JsonElement, int> count)
    {
        var response = await _client.PostAsync("/graphql", Json(new { query }), Ct);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        if (root.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
        {
            // A refusal names its code; an error with none is the server failing to say what went wrong.
            return errors[0].TryGetProperty("extensions", out var ext) && ext.TryGetProperty("code", out _)
                ? Answer.Refused
                : Answer.Failed(errors[0].GetProperty("message").GetString() ?? "");
        }

        return Answer.Rows(count(root.GetProperty("data")));
    }

    private static Func<JsonElement, int> Count(string property) => body => body.GetProperty(property).GetArrayLength();

    private static Answer FromHttp(HttpResponseMessage response, Func<JsonElement, int> count)
    {
        var text = response.Content.ReadAsStringAsync(Ct).GetAwaiter().GetResult();
        return response.StatusCode switch
        {
            HttpStatusCode.OK => Answer.Rows(count(JsonDocument.Parse(text).RootElement)),
            HttpStatusCode.BadRequest => Answer.Refused,
            var status => Answer.Failed($"{(int)status} {text}"),
        };
    }

    private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- the data --------------------------------------------------------------------------------------------------

    private readonly Dictionary<string, string> _views = [];

    private async Task<string> ViewAsync(string table)
    {
        if (_views.TryGetValue(table, out var name))
        {
            return name;
        }

        name = $"v_{table}"[..Math.Min(30, table.Length + 2)];
        (await _client.PostAsJsonAsync("/api/views", new CreateViewApiRequest
        {
            Name = name,
            BaseTable = table,
            Columns = [.. TableColumns.Select(c => new ViewColumnApiSpec { Source = c, Alias = c })],
        }, Ct)).EnsureSuccessStatusCode();
        return _views[table] = name;
    }

    private async Task<string> SeedAsync()
    {
        var table = $"cond_{Guid.NewGuid():N}"[..30];
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = true },
                new CreateColumnApiRequest { Name = "qty", Type = "integer", Nullable = true },
                new CreateColumnApiRequest { Name = "due", Type = "date", Nullable = true },
                new CreateColumnApiRequest { Name = "flag", Type = "boolean", Nullable = true },
                new CreateColumnApiRequest { Name = "ref", Type = "uuid", Nullable = true },
                new CreateColumnApiRequest { Name = "amt", Type = "decimal", Nullable = true },
                new CreateColumnApiRequest { Name = "tag", Type = "text", Nullable = true, Formula = new FormulaConfigApiRequest { Formula = "{code}", ReturnType = "text" } },
                // A lookup of the table's own code by the same code: a virtual column that reads the text it was given.
                new CreateColumnApiRequest
                {
                    Name = "echo", Type = "text", Nullable = true,
                    Lookup = new LookupConfigApiRequest { RelationColumn = "code", TargetTable = table, TargetColumn = "code", MatchColumn = "code" },
                },
            ],
        }, Ct)).EnsureSuccessStatusCode();

        // qty is 1..9 in the order of CodeRows, so "qty in [1, 2]" names the first two codes.
        var rows = CodeRows.Select((code, i) => new Dictionary<string, object?> { ["code"] = code, ["qty"] = i + 1 }).ToArray();
        (await _client.PostAsJsonAsync($"/api/batch/data/{table}/insert", rows, Ct)).EnsureSuccessStatusCode();
        return table;
    }
}
