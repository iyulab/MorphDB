using MorphDB.Npgsql.Infrastructure;

namespace MorphDB.Tests.Unit;

/// <summary>
/// The rewriter decides what in a caller-authored fragment is a column reference. Everything that
/// reads or rewrites a policy or a view expression stands on that decision, so the cases that are
/// not column references are held here: a string literal, part of a longer name, and a placeholder.
/// </summary>
public class SqlIdentifierRewriterTests
{
    private static string Rename(string text, string from, string to) =>
        SqlIdentifierRewriter.Rewrite(text, token => token == from ? to : null);

    [Theory]
    [InlineData("owner = 1", "holder = 1", "a bare identifier")]
    [InlineData("\"owner\" = 1", "holder = 1", "a quoted identifier names the same column")]
    [InlineData("title = 'owner'", "title = 'owner'", "a string literal is a value")]
    [InlineData("title = 'it''s owner'", "title = 'it''s owner'", "a doubled quote does not end the literal")]
    [InlineData("owner_id IS NULL AND co_owner = 1", "owner_id IS NULL AND co_owner = 1", "a longer name is another name")]
    [InlineData("t.owner = 1", "t.owner = 1", "a dotted reference is offered whole")]
    [InlineData("lower(owner) = owner", "lower(holder) = holder", "every occurrence")]
    public void A_name_is_rewritten_only_where_it_is_an_identifier(string text, string expected, string why)
    {
        Rename(text, "owner", "holder").Should().Be(expected, why);
    }

    /// <summary>
    /// <c>{{email}}</c> is the caller's value. A table may well have a column called <c>email</c>,
    /// and the two must not be confused.
    /// </summary>
    [Theory]
    [InlineData("email = {{email}}", "contact = {{email}}")]
    [InlineData("{{email}} = email", "{{email}} = contact")]
    [InlineData("email = {{claims.email}}", "contact = {{claims.email}}")]
    [InlineData("note = '{{email}}' AND email IS NOT NULL", "note = '{{email}}' AND contact IS NOT NULL")]
    public void A_placeholder_is_not_an_identifier(string text, string expected)
    {
        Rename(text, "email", "contact").Should().Be(expected);
        SqlIdentifierRewriter.Identifiers(text).Should().NotContain(i => i.Contains('{'));
        SqlIdentifierRewriter.Identifiers("role = {{role}}").Should().Equal("role");
    }
}
