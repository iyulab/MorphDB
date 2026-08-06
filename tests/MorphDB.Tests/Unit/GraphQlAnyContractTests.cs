using System.Text.Json;
using MorphDB.Service.GraphQL;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Holds the <c>Any</c> boundary to the values it hands the write pipeline.
/// <para>
/// A row's shape is not in the schema — that is the product — so every field a client writes over
/// GraphQL crosses here, and what it becomes is decided by this mapping alone. That makes the
/// mapping a published contract even though no type in the schema names it: the schema says
/// <c>Any</c> either way, so a change in here is invisible to a schema diff and to introspection,
/// and it lands in what a consumer's rows actually store.
/// </para>
/// <para>
/// It had no test. The boundary was rewritten when the GraphQL server moved a major version — the
/// coercion used to be the server's and is now this type's — and a full suite stayed green across
/// that move, which is exactly what a surface with no gate looks like.
/// </para>
/// </summary>
public class GraphQlAnyContractTests
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    [Fact]
    public void A_number_narrows_to_the_smallest_integer_that_holds_it_then_to_decimal()
    {
        GraphQlAny.ToRuntimeValue(Json("7")).Should().BeOfType<int>().And.Be(7);
        GraphQlAny.ToRuntimeValue(Json("2147483648")).Should().BeOfType<long>().And.Be(2147483648L);
        GraphQlAny.ToRuntimeValue(Json("1.5")).Should().BeOfType<decimal>().And.Be(1.5m);
    }

    [Fact]
    public void A_string_that_reads_as_a_guid_or_a_timestamp_becomes_one()
    {
        GraphQlAny.ToRuntimeValue(Json("\"3f2504e0-4f89-11d3-9a0c-0305e82c3301\""))
            .Should().BeOfType<Guid>();
        GraphQlAny.ToRuntimeValue(Json("\"2026-07-01T09:30:00Z\""))
            .Should().BeOfType<DateTimeOffset>();
        GraphQlAny.ToRuntimeValue(Json("\"plain\"")).Should().BeOfType<string>().And.Be("plain");
    }

    [Fact]
    public void Booleans_and_null_cross_as_themselves()
    {
        GraphQlAny.ToRuntimeValue(Json("true")).Should().Be(true);
        GraphQlAny.ToRuntimeValue(Json("false")).Should().Be(false);
        GraphQlAny.ToRuntimeValue(Json("null")).Should().BeNull();
    }

    [Fact]
    public void Objects_and_arrays_stay_json_text_rather_than_becoming_nested_structures()
    {
        // A jsonb column holds what the caller sent; re-materialising nested values here would only
        // have to be serialised again on the way to the database.
        GraphQlAny.ToRuntimeValue(Json("""{"a":1}""")).Should().BeOfType<string>().And.Be("""{"a":1}""");
        GraphQlAny.ToRuntimeValue(Json("[1,2]")).Should().BeOfType<string>().And.Be("[1,2]");
    }

    [Fact]
    public void A_row_reads_every_field_and_matches_names_without_regard_to_case()
    {
        var row = GraphQlAny.ToRow(Json("""{"Lot":"L-1","qty":2}"""));

        row.Should().HaveCount(2);
        row["lot"].Should().Be("L-1", "a caller's own logical names are matched as they mean them");
        row["QTY"].Should().Be(2);
    }

    [Fact]
    public void A_value_that_is_not_an_object_carries_no_fields()
    {
        // Answering with an empty row lets the write pipeline refuse it with a code the caller can
        // branch on, rather than throwing out of the resolver.
        GraphQlAny.ToRow(Json("[1,2]")).Should().BeEmpty();
        GraphQlAny.ToRow(Json("\"nope\"")).Should().BeEmpty();
    }

    [Fact]
    public void A_row_written_back_out_keeps_the_names_it_was_given()
    {
        var element = GraphQlAny.FromRow(new Dictionary<string, object?>
        {
            ["lot_label"] = "L-1",
            ["QtyOnHand"] = 3,
        });

        // No naming policy: applying one would rewrite the caller's schema on the way out.
        element.GetProperty("lot_label").GetString().Should().Be("L-1");
        element.GetProperty("QtyOnHand").GetInt32().Should().Be(3);
    }
}
