using MorphDB.Core.Models;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Service.Models.Api;

namespace MorphDB.Tests.Unit;

/// <summary>
/// What the vocabulary tells a caller about itself. Two members of it are declared and not
/// implemented, and a caller who names one meets a refusal — so what that refusal says, and what the
/// list of alternatives contains, is the whole of what they have to go on.
/// </summary>
public class DataTypeVocabularyTests
{
    /// <summary>
    /// Asked of the store rather than listed here, so a type that gains storage leaves this set on
    /// its own. A hand-written pair would keep asserting yesterday's answer.
    /// </summary>
    private static IEnumerable<MorphDataType> WithoutStorage() =>
        Enum.GetValues<MorphDataType>().Where(type =>
        {
            try
            {
                TypeMapper.ToNativeType(type);
                return false;
            }
            catch (ArgumentOutOfRangeException)
            {
                return true;
            }
        });

    [Fact]
    public void A_declared_type_without_storage_is_not_reported_as_unknown()
    {
        var unimplemented = WithoutStorage().ToList();
        unimplemented.Should().NotBeEmpty(
            "if every declared type gained storage this test has nothing to guard and should go");

        foreach (var type in unimplemented)
        {
            var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => TypeMapper.ToNativeType(type));

            refusal.Message.Should().NotContain("Unknown",
                $"'{type}' is spelled correctly and parses — telling the caller it is unknown sends " +
                "them looking for a typo that is not there");
            refusal.Message.Should().Contain("unimplemented",
                "the caller's remedy is to choose another type, not to correct this one");
        }
    }

    /// <summary>
    /// The list a caller reads when their type name did not parse. It was written by hand and named
    /// fifteen of thirty, which is worse than naming none: a reader takes it for the whole answer.
    /// </summary>
    [Fact]
    public void The_list_of_supported_types_is_the_set_a_column_can_actually_be_created_with()
    {
        var refusal = Assert.Throws<ArgumentException>(() => ApiModelExtensions.ParseDataType("no-such-type"));

        foreach (var type in Enum.GetValues<MorphDataType>().Except(WithoutStorage()))
        {
            refusal.Message.Should().Contain(type.ToString().ToLowerInvariant(),
                "a type a column can be created with belongs in the list of what to send instead");
        }

        foreach (var type in WithoutStorage())
        {
            refusal.Message.Should().NotContain(type.ToString().ToLowerInvariant(),
                "offering a type that is refused at column creation sends the caller down a path " +
                "that cannot end");
        }
    }
}
