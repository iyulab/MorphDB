using MorphDB.Core.Exceptions;
using MorphDB.Core.Security;
using MorphDB.Npgsql.Security;

namespace MorphDB.Tests.Unit;

/// <summary>
/// A column may not take a name a stored policy already uses for something else — renamed to it or
/// added under it. Both paths ask the same question of the stored policies; the cases that decide
/// it are held here, without a database.
/// </summary>
public class PolicyNameUseTests
{
    private static NativePolicyProjector.StoredPolicy Policy(string name, string expression, bool isActive = true) =>
        new(Guid.NewGuid(), name, PolicyType.Select, expression, isActive);

    private static Action Ensure(string name, params NativePolicyProjector.StoredPolicy[] stored) =>
        () => NativePolicyProjector.EnsureNameUnused(stored, name, "Column 'x' cannot be added to table 't'");

    [Fact]
    public void A_name_a_policy_uses_as_a_function_is_refused_naming_the_policy()
    {
        var refusal = Ensure("lower", Policy("owner_only", "lower(owner) = {{user_id}}"))
            .Should().Throw<SchemaException>().Which;

        refusal.ErrorCode.Should().Be("COLUMN_USED_BY_POLICY");
        refusal.Message.Should().StartWith("Column 'x' cannot be added to table 't': ")
            .And.Contain("policy 'owner_only' already uses that name");
    }

    [Fact]
    public void Every_policy_using_the_name_is_named()
    {
        var refusal = Ensure(
                "lower",
                Policy("one", "lower(owner) = {{user_id}}"),
                Policy("two", "lower(title) <> ''"),
                Policy("other", "owner = {{user_id}}"))
            .Should().Throw<SchemaException>().Which;

        refusal.Message.Should().Contain("policies 'one', 'two' already use").And.NotContain("'other'");
    }

    [Fact]
    public void An_inactive_policy_counts_because_it_is_switched_on_as_it_stands() =>
        Ensure("lower", Policy("off", "lower(owner) = {{user_id}}", isActive: false))
            .Should().Throw<SchemaException>();

    [Theory]
    [InlineData("title = 'lower'", "a string literal is a value")]
    [InlineData("lowercase = 1", "a longer name is another name")]
    [InlineData("owner = {{lower}}", "a placeholder is the caller's value")]
    [InlineData("LOWER(owner) = 1", "a name is matched as written")]
    public void A_name_the_policy_does_not_use_as_an_identifier_is_free(string expression, string because) =>
        Ensure("lower", Policy("p", expression)).Should().NotThrow(because);

    [Fact]
    public void A_table_without_policies_holds_no_name() =>
        Ensure("lower").Should().NotThrow();
}
