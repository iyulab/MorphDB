using MorphDB.Npgsql.Infrastructure;

namespace MorphDB.Tests.Unit;

/// <summary>
/// The CHECK grammar has one definition (§3.10-C1): what <c>CheckGrammar</c> accepts is exactly
/// what the evaluator can enforce. Accepting more would store constraints that constrain nothing.
/// </summary>
public class CheckGrammarTests
{
    [Theory]
    [InlineData("age > 0")]
    [InlineData("age >= 0")]
    [InlineData("price <= 10000")]
    [InlineData("status = 'active'")]
    [InlineData("status != 'banned'")]
    [InlineData("status <> 'banned'")]
    [InlineData("end_date > start_date")]
    [InlineData("email MATCHES '^[^@]+@[^@]+$'")]
    [InlineData("code matches '^[A-Z]+$'")]
    [InlineData("age >= 0 AND age <= 150")]
    [InlineData("status = 'active' OR status = 'pending'")]
    [InlineData("(age > 0 AND age < 100) OR status = 'exempt'")]
    [InlineData("status = 'a)b'")]
    public void The_evaluatable_forms_are_accepted(string expression) =>
        CheckGrammar.IsSupported(expression).Should().BeTrue();

    [Theory]
    [InlineData("name ~ '^[a-z]+$'")]            // PostgreSQL regex operator — use MATCHES
    [InlineData("status IN ('a', 'b')")]
    [InlineData("length(name) > 3")]
    [InlineData("age BETWEEN 0 AND 150")]
    [InlineData("name IS NOT NULL")]
    [InlineData("age > 0; DROP TABLE t_people")]
    [InlineData("age > 0 -- ")]
    [InlineData("(age > 0")]
    [InlineData("")]
    public void What_the_evaluator_cannot_enforce_is_refused(string expression) =>
        CheckGrammar.IsSupported(expression).Should().BeFalse();

    [Fact]
    public void The_rejection_names_the_expression_and_the_supported_forms()
    {
        var act = () => CheckGrammar.EnsureSupported("length(name) > 3");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*length(name) > 3*")
            .WithMessage("*Supported CHECK forms*");
    }
}
