using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Query;
using MorphDB.Npgsql.Repositories;

namespace MorphDB.Tests.Unit;

/// <summary>
/// A view's join condition and computed-column expression are caller-authored strings that the
/// builder splices into SQL verbatim (identifiers translated, everything else passed through). They
/// must go through the same inline-expression gate as an index predicate or a policy expression,
/// and they must be refused <em>before</em> the builder touches metadata -- so the repository here
/// is strict: any call into it fails the test, which is what proves the gate runs first.
/// </summary>
public class ViewQueryBuilderExpressionGateTests
{
    private static ViewQueryBuilder StrictBuilder() =>
        new(new Mock<IMetadataRepository>(MockBehavior.Strict).Object, Guid.NewGuid());

    [Theory]
    [InlineData("price * quantity; DROP TABLE orders")]
    [InlineData("price) UNION SELECT 1, 2, 3 FROM pg_user WHERE (1 = 1")]
    [InlineData("price -- * quantity")]
    [InlineData("price /* * */ quantity")]
    [InlineData("'unterminated")]
    public async Task ColumnExpression_ThatCouldEscapeItsClause_IsRefusedBeforeAnyMetadataRead(string expression)
    {
        var definition = new ViewDefinition
        {
            BaseTable = "orders",
            Columns = [new ViewColumnSpec { Expression = expression, Alias = "total" }],
        };

        var act = () => StrictBuilder().BuildSelectStatementAsync(definition);

        (await act.Should().ThrowAsync<SchemaException>())
            .Which.ErrorCode.Should().Be("INVALID_EXPRESSION");
    }

    [Theory]
    [InlineData("orders.customer_ref = customers._id; DELETE FROM customers")]
    [InlineData("orders.customer_ref = customers._id) OR (1 = 1")]
    [InlineData("orders.customer_ref = customers._id -- AND customers.active")]
    public async Task JoinCondition_ThatCouldEscapeItsClause_IsRefusedBeforeAnyMetadataRead(string condition)
    {
        var definition = new ViewDefinition
        {
            BaseTable = "orders",
            Joins = [new ViewJoinSpec { Table = "customers", JoinType = ViewJoinType.Inner, Condition = condition }],
            Columns = [new ViewColumnSpec { Source = "amount", Alias = "amount" }],
        };

        var act = () => StrictBuilder().BuildSelectStatementAsync(definition);

        (await act.Should().ThrowAsync<SchemaException>())
            .Which.ErrorCode.Should().Be("INVALID_EXPRESSION");
    }

    [Fact]
    public async Task WellFormedExpressionAndCondition_PassTheGateAndReachMetadata()
    {
        // A loose repository that knows no tables: passing the gate means the builder gets as far
        // as asking for the base table and reporting it missing -- not an INVALID_EXPRESSION.
        var repository = new Mock<IMetadataRepository>();
        var builder = new ViewQueryBuilder(repository.Object, Guid.NewGuid());
        var definition = new ViewDefinition
        {
            BaseTable = "orders",
            Joins = [new ViewJoinSpec { Table = "customers", JoinType = ViewJoinType.Inner, Condition = "orders.customer_ref = customers._id" }],
            Columns =
            [
                new ViewColumnSpec { Expression = "price * quantity", Alias = "total" },
                new ViewColumnSpec { Expression = "CONCAT(first_name, ' ', last_name)", Alias = "full_name" },
                new ViewColumnSpec { Expression = "status = 'a)b'", Alias = "odd_status" },
            ],
        };

        var act = () => builder.BuildSelectStatementAsync(definition);

        await act.Should().ThrowAsync<TableNotFoundException>();
        repository.Verify(
            r => r.GetTableByNameAsync(It.IsAny<Guid>(), "orders", true, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
