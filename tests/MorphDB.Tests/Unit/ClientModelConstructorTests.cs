using MorphDB.Client.Models;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Holds the parameterized constructors on client models usable.
/// <para>
/// These models pair <c>required</c> members with a convenience constructor. C# does not infer that a
/// constructor satisfies those members — without <c>[SetsRequiredMembers]</c> the compiler still demands
/// an object initializer (CS9035), so the constructor cannot be called at all and is dead API. Because
/// the failure is a compile error in *consuming* code, no runtime assertion can catch it: these tests
/// call each constructor, and dropping the attribute stops this project from building.
/// </para>
/// </summary>
public class ClientModelConstructorTests
{
    [Fact]
    public void Filter_constructor_sets_its_required_members()
    {
        var filter = new Filter("status", FilterOperator.Equal, "open");

        filter.Column.Should().Be("status");
        filter.Operator.Should().Be(FilterOperator.Equal);
        filter.Value.Should().Be("open");
    }

    [Fact]
    public void OrderBy_constructor_sets_its_required_members()
    {
        var ascending = new OrderBy("created_at");
        var descending = new OrderBy("created_at", ascending: false);

        ascending.Column.Should().Be("created_at");
        ascending.Ascending.Should().BeTrue("the parameter defaults to ascending");
        descending.Ascending.Should().BeFalse();
    }

    [Fact]
    public void AggregationFilter_constructor_sets_its_required_members()
    {
        var filter = new AggregationFilter("amount", FilterOperator.GreaterThan, 100);

        filter.Column.Should().Be("amount");
        filter.Operator.Should().Be(FilterOperator.GreaterThan);
        filter.Value.Should().Be(100);
    }

    [Fact]
    public void HavingCondition_constructor_sets_its_required_members()
    {
        var having = new HavingCondition("total", FilterOperator.GreaterThanOrEqual, 10);

        having.Alias.Should().Be("total");
        having.Operator.Should().Be(FilterOperator.GreaterThanOrEqual);
        having.Value.Should().Be(10);
    }

    [Fact]
    public void AggregationOrderBy_constructor_sets_its_required_members()
    {
        var ascending = new AggregationOrderBy("total");
        var descending = new AggregationOrderBy("total", descending: true);

        ascending.Column.Should().Be("total");
        ascending.Descending.Should().BeFalse("the parameter defaults to ascending");
        descending.Descending.Should().BeTrue();
    }
}
