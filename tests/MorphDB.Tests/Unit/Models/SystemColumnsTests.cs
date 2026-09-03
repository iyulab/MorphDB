using MorphDB.Core.Models;

namespace MorphDB.Tests.Unit.Models;

/// <summary>
/// <see cref="SystemColumns.GetRecordId"/> is the single canonical read of a row's id, replacing
/// three independent reimplementations (transaction service, GraphQL mutation, GraphQL query) that
/// had drifted apart. These cases pin the contract those call sites now share.
/// </summary>
public class SystemColumnsTests
{
    [Fact]
    public void Reads_a_Guid_typed_id_column()
    {
        var id = Guid.NewGuid();
        var row = new Dictionary<string, object?> { ["_id"] = id, ["name"] = "row" };

        SystemColumns.GetRecordId(row).Should().Be(id);
    }

    [Fact]
    public void Parses_a_string_typed_id_column()
    {
        var id = Guid.NewGuid();
        var row = new Dictionary<string, object?> { ["_id"] = id.ToString() };

        SystemColumns.GetRecordId(row).Should().Be(id);
    }

    [Fact]
    public void Returns_null_when_the_id_column_is_absent()
    {
        var row = new Dictionary<string, object?> { ["name"] = "row" };

        SystemColumns.GetRecordId(row).Should().BeNull();
    }

    [Fact]
    public void Does_not_fall_back_to_an_unprefixed_id_key()
    {
        // The unprefixed "id" key is not a second valid spelling of the record id — a row that
        // carries only it (instead of "_id") is itself the defect this contract exists to surface.
        var row = new Dictionary<string, object?> { ["id"] = Guid.NewGuid() };

        SystemColumns.GetRecordId(row).Should().BeNull();
    }

    [Fact]
    public void Returns_null_for_an_unparsable_id_value()
    {
        var row = new Dictionary<string, object?> { ["_id"] = "not-a-guid" };

        SystemColumns.GetRecordId(row).Should().BeNull();
    }

    [Fact]
    public void Returns_null_for_a_null_dictionary()
    {
        SystemColumns.GetRecordId(null).Should().BeNull();
    }
}
