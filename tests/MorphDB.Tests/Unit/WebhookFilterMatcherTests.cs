using System.Text.Json;
using MorphDB.Service.Realtime;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Unit tests for WebhookFilterMatcher — the flat, AND-combined scalar-literal equality
/// evaluator. There is deliberately no operator syntax and no nesting.
/// </summary>
[Trait("Category", "Unit")]
public class WebhookFilterMatcherTests
{
    // A row deserialized from JSON — object? values land as JsonElement. The listener no longer
    // produces this shape (it reads the row back as .NET values, see the last test), but a
    // JsonElement value must keep matching the same way, since both are the same value on the wire.
    private static IDictionary<string, object?> Data(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;

    private static JsonDocument Filter(string json) => JsonDocument.Parse(json);

    [Fact]
    public void Matches_NoFilter_AdmitsAnyRow()
    {
        WebhookFilterMatcher.Matches(null, Data("""{"status":"completed"}""")).Should().BeTrue();
    }

    [Fact]
    public void Matches_NoFilter_AdmitsDeleteWithNoData()
    {
        WebhookFilterMatcher.Matches(null, null).Should().BeTrue();
    }

    [Fact]
    public void Matches_FilterOnDeleteWithNoData_NeverMatches()
    {
        WebhookFilterMatcher.Matches(Filter("""{"status":"completed"}"""), null).Should().BeFalse();
    }

    [Fact]
    public void Matches_SingleKeyStringEqual_ReturnsTrue()
    {
        WebhookFilterMatcher.Matches(
            Filter("""{"status":"completed"}"""),
            Data("""{"status":"completed","id":"1"}""")).Should().BeTrue();
    }

    [Fact]
    public void Matches_SingleKeyStringMismatch_ReturnsFalse()
    {
        WebhookFilterMatcher.Matches(
            Filter("""{"status":"completed"}"""),
            Data("""{"status":"pending"}""")).Should().BeFalse();
    }

    [Fact]
    public void Matches_MultipleKeysAllMatch_ReturnsTrue()
    {
        WebhookFilterMatcher.Matches(
            Filter("""{"status":"completed","priority":"high"}"""),
            Data("""{"status":"completed","priority":"high","id":"1"}""")).Should().BeTrue();
    }

    [Fact]
    public void Matches_MultipleKeysOneMismatches_ReturnsFalse()
    {
        WebhookFilterMatcher.Matches(
            Filter("""{"status":"completed","priority":"high"}"""),
            Data("""{"status":"completed","priority":"low"}""")).Should().BeFalse();
    }

    [Fact]
    public void Matches_FilterKeyAbsentFromData_ReturnsFalse()
    {
        WebhookFilterMatcher.Matches(
            Filter("""{"missingColumn":"x"}"""),
            Data("""{"status":"completed"}""")).Should().BeFalse();
    }

    [Fact]
    public void Matches_NumberEqual_ReturnsTrue()
    {
        WebhookFilterMatcher.Matches(
            Filter("""{"qty":10}"""),
            Data("""{"qty":10}""")).Should().BeTrue();
    }

    [Fact]
    public void Matches_NumberMismatch_ReturnsFalse()
    {
        WebhookFilterMatcher.Matches(
            Filter("""{"qty":10}"""),
            Data("""{"qty":11}""")).Should().BeFalse();
    }

    [Fact]
    public void Matches_BoolEqual_ReturnsTrue()
    {
        WebhookFilterMatcher.Matches(
            Filter("""{"shipped":true}"""),
            Data("""{"shipped":true}""")).Should().BeTrue();
    }

    [Fact]
    public void Matches_BoolMismatch_ReturnsFalse()
    {
        WebhookFilterMatcher.Matches(
            Filter("""{"shipped":true}"""),
            Data("""{"shipped":false}""")).Should().BeFalse();
    }

    [Fact]
    public void Matches_NullFilterValueAgainstNullData_ReturnsTrue()
    {
        WebhookFilterMatcher.Matches(
            Filter("""{"cancelledAt":null}"""),
            Data("""{"cancelledAt":null}""")).Should().BeTrue();
    }

    [Fact]
    public void Matches_NullFilterValueAgainstNonNullData_ReturnsFalse()
    {
        WebhookFilterMatcher.Matches(
            Filter("""{"cancelledAt":null}"""),
            Data("""{"cancelledAt":"2026-08-21"}""")).Should().BeFalse();
    }

    [Fact]
    public void Matches_CrossTypeComparisonIsStrict_ReturnsFalse()
    {
        // Filter "10" (string) must not match data 10 (number) — no coercion.
        WebhookFilterMatcher.Matches(
            Filter("""{"qty":"10"}"""),
            Data("""{"qty":10}""")).Should().BeFalse();
    }

    /// <summary>
    /// Regression. The listener used to hand the matcher the NOTIFY payload's row, whose values
    /// were <see cref="JsonElement"/>s, and the matcher matched nothing else — so once the row
    /// was read back through the data service (whose values are the CLR values a REST response is
    /// serialized from) every filter stopped matching and no filtered webhook fired.
    /// </summary>
    [Fact]
    public void Matches_RowReadBackAsClrValues_ComparesAsItWouldOnTheWire()
    {
        var row = new Dictionary<string, object?>
        {
            ["name"] = "vip",
            ["count"] = 42,
            ["active"] = true,
            ["price"] = 9.5m,
            ["note"] = null,
        };

        WebhookFilterMatcher.Matches(Filter("""{"name":"vip","count":42,"active":true,"price":9.5,"note":null}"""), row).Should().BeTrue();
        WebhookFilterMatcher.Matches(Filter("""{"name":"VIP"}"""), row).Should().BeFalse();
        WebhookFilterMatcher.Matches(Filter("""{"count":"42"}"""), row).Should().BeFalse("a string literal never equals a number, whatever the CLR type underneath");
        WebhookFilterMatcher.Matches(Filter("""{"active":"true"}"""), row).Should().BeFalse();
    }

    [Fact]
    public void IsSupported_NullFilter_ReturnsTrue()
    {
        WebhookFilterMatcher.IsSupported(null).Should().BeTrue();
    }

    [Fact]
    public void IsSupported_JsonNullFilter_ReturnsTrue()
    {
        // ASP.NET Core model binding turns a request body's "filter": null into a non-null
        // JsonDocument whose root is JsonValueKind.Null, not a C# null — JsonDocument.Parse("null")
        // is itself valid JSON. A webhook created with no filter must not be rejected as unsupported.
        WebhookFilterMatcher.IsSupported(JsonDocument.Parse("null")).Should().BeTrue();
    }

    [Fact]
    public void Matches_JsonNullFilter_AdmitsAnyRow()
    {
        WebhookFilterMatcher.Matches(JsonDocument.Parse("null"), Data("""{"status":"completed"}"""))
            .Should().BeTrue();
    }

    [Fact]
    public void Matches_JsonNullFilter_AdmitsDeleteWithNoData()
    {
        WebhookFilterMatcher.Matches(JsonDocument.Parse("null"), null).Should().BeTrue();
    }

    [Fact]
    public void IsSupported_FlatScalarFilter_ReturnsTrue()
    {
        WebhookFilterMatcher.IsSupported(Filter("""{"status":"completed","qty":10,"shipped":true,"note":null}"""))
            .Should().BeTrue();
    }

    [Fact]
    public void IsSupported_NestedObjectValue_ReturnsFalse()
    {
        WebhookFilterMatcher.IsSupported(Filter("""{"status":{"$gt":10}}""")).Should().BeFalse();
    }

    [Fact]
    public void IsSupported_ArrayValue_ReturnsFalse()
    {
        WebhookFilterMatcher.IsSupported(Filter("""{"status":["a","b"]}""")).Should().BeFalse();
    }

    [Fact]
    public void IsSupported_TopLevelArray_ReturnsFalse()
    {
        WebhookFilterMatcher.IsSupported(Filter("""["a","b"]""")).Should().BeFalse();
    }
}
