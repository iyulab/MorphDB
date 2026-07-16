using System.Text.Json;
using MorphDB.Npgsql.Infrastructure;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Tests for the shared JsonElement -> CLR unwrap used by the write-pipeline validators and the
/// view SQL builders (issue rest-jsonelement-defects).
/// </summary>
public class JsonValueConverterTests
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    [Fact]
    public void ToClrValue_String_ReturnsString()
        => JsonValueConverter.ToClrValue(Json("\"hello\"")).Should().Be("hello");

    [Fact]
    public void ToClrValue_Integer_ReturnsLong()
        => JsonValueConverter.ToClrValue(Json("42")).Should().Be(42L);

    [Fact]
    public void ToClrValue_Decimal_ReturnsDouble()
        => JsonValueConverter.ToClrValue(Json("3.5")).Should().Be(3.5d);

    [Fact]
    public void ToClrValue_True_ReturnsBool()
        => JsonValueConverter.ToClrValue(Json("true")).Should().Be(true);

    [Fact]
    public void ToClrValue_Null_ReturnsNull()
        => JsonValueConverter.ToClrValue(Json("null")).Should().BeNull();

    [Fact]
    public void ToClrValue_NonJsonElement_PassesThrough()
    {
        var guid = Guid.NewGuid();
        JsonValueConverter.ToClrValue(guid).Should().Be(guid);
        JsonValueConverter.ToClrValue("plain").Should().Be("plain");
        JsonValueConverter.ToClrValue(null).Should().BeNull();
    }

    [Fact]
    public void ToClrValue_ObjectOrArray_ReturnsRawJson()
    {
        JsonValueConverter.ToClrValue(Json("{\"a\":1}")).Should().Be("{\"a\":1}");
        JsonValueConverter.ToClrValue(Json("[1,2]")).Should().Be("[1,2]");
    }
}
