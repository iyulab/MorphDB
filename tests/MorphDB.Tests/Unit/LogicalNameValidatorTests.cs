using MorphDB.Core.Exceptions;
using MorphDB.Npgsql.Services;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Unit tests for LogicalNameValidator.
/// Verifies that entity names allow underscore prefix (except _morph_),
/// while column names block all underscore prefixes.
/// </summary>
public class LogicalNameValidatorTests
{
    #region ValidateEntityName

    [Theory]
    [InlineData("customers")]
    [InlineData("order_items")]
    [InlineData("_custom_table")]
    [InlineData("_archive_logs")]
    [InlineData("_formbase_entities")]
    public void ValidateEntityName_ValidNames_ShouldNotThrow(string name)
    {
        var act = () => LogicalNameValidator.ValidateEntityName(name, "Table");
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateEntityName_MorphPrefix_ShouldThrowReservedName()
    {
        var act = () => LogicalNameValidator.ValidateEntityName("_morph_system", "Table");
        act.Should().Throw<SchemaException>()
            .Where(e => e.ErrorCode == "RESERVED_NAME");
    }

    [Fact]
    public void ValidateEntityName_MorphPrefixCaseInsensitive_ShouldThrowReservedName()
    {
        var act = () => LogicalNameValidator.ValidateEntityName("_MORPH_tables", "Table");
        act.Should().Throw<SchemaException>()
            .Where(e => e.ErrorCode == "RESERVED_NAME");
    }

    [Fact]
    public void ValidateEntityName_Empty_ShouldThrowInvalidName()
    {
        var act = () => LogicalNameValidator.ValidateEntityName("", "Table");
        act.Should().Throw<SchemaException>()
            .Where(e => e.ErrorCode == "INVALID_NAME");
    }

    [Fact]
    public void ValidateEntityName_Whitespace_ShouldThrowInvalidName()
    {
        var act = () => LogicalNameValidator.ValidateEntityName("   ", "View");
        act.Should().Throw<SchemaException>()
            .Where(e => e.ErrorCode == "INVALID_NAME");
    }

    [Fact]
    public void ValidateEntityName_TooLong_ShouldThrowInvalidName()
    {
        var longName = new string('a', 256);
        var act = () => LogicalNameValidator.ValidateEntityName(longName, "Index");
        act.Should().Throw<SchemaException>()
            .Where(e => e.ErrorCode == "INVALID_NAME");
    }

    [Fact]
    public void ValidateEntityName_MaxLength_ShouldNotThrow()
    {
        var maxName = new string('a', 255);
        var act = () => LogicalNameValidator.ValidateEntityName(maxName, "Table");
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Table")]
    [InlineData("View")]
    [InlineData("Index")]
    [InlineData("Relation")]
    public void ValidateEntityName_EntityTypeInErrorMessage(string entityType)
    {
        var act = () => LogicalNameValidator.ValidateEntityName("", entityType);
        act.Should().Throw<SchemaException>()
            .WithMessage($"*{entityType}*");
    }

    #endregion

    #region ValidateColumnName

    [Theory]
    [InlineData("email")]
    [InlineData("customer_name")]
    [InlineData("order123")]
    public void ValidateColumnName_ValidNames_ShouldNotThrow(string name)
    {
        var act = () => LogicalNameValidator.ValidateColumnName(name);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("_custom_col")]
    [InlineData("_id")]
    [InlineData("_anything")]
    public void ValidateColumnName_UnderscorePrefix_ShouldThrowInvalidName(string name)
    {
        var act = () => LogicalNameValidator.ValidateColumnName(name);
        act.Should().Throw<SchemaException>()
            .Where(e => e.ErrorCode == "INVALID_NAME");
    }

    [Fact]
    public void ValidateColumnName_Empty_ShouldThrowInvalidName()
    {
        var act = () => LogicalNameValidator.ValidateColumnName("");
        act.Should().Throw<SchemaException>()
            .Where(e => e.ErrorCode == "INVALID_NAME");
    }

    [Fact]
    public void ValidateColumnName_TooLong_ShouldThrowInvalidName()
    {
        var longName = new string('a', 256);
        var act = () => LogicalNameValidator.ValidateColumnName(longName);
        act.Should().Throw<SchemaException>()
            .Where(e => e.ErrorCode == "INVALID_NAME");
    }

    #endregion
}
