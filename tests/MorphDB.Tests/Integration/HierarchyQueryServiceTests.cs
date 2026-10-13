using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration;

/// <summary>
/// A hierarchy query walks a table's parent column with a recursive query that computes each row's
/// depth. A request may name the columns it wants back or leave them out, and leaving them out —
/// the default — has to answer rows too.
/// </summary>
[Collection("PostgreSQL")]
public class HierarchyQueryServiceTests
{
    private readonly RowLevelSecurityHarness _harness;

    public HierarchyQueryServiceTests(PostgresFixture fixture)
    {
        _harness = new RowLevelSecurityHarness(fixture);
    }

    private sealed record Tree(TableMetadata Table, Guid Root, Guid Child, Guid Grandchild)
    {
        /// <param name="columns">Comma-separated; <c>null</c> for a request that names none.</param>
        public HierarchyQueryRequest Request(Guid recordId, string? columns) => new()
        {
            ProjectId = Table.ProjectId,
            TableName = Table.LogicalName,
            ParentColumn = SystemColumns.ParentId,
            RecordId = recordId,
            Columns = columns?.Split(',', StringSplitOptions.RemoveEmptyEntries)
        };
    }

    private async Task<Tree> CreateTreeAsync()
    {
        var table = await _harness.CreateDocumentsAsync(Guid.NewGuid(), "tree_docs");
        Dictionary<string, object?> Row(string title, Guid? parent) =>
            new() { ["title"] = title, [SystemColumns.ParentId] = parent };

        var root = await _harness.SeedAsync(table, Row("root", null));
        var child = await _harness.SeedAsync(table, Row("child", root));
        var grandchild = await _harness.SeedAsync(table, Row("grandchild", child));
        return new Tree(table, root, child, grandchild);
    }

    private static IEnumerable<(Guid Id, int Depth)> IdsAndDepths(HierarchyQueryResult result) =>
        result.Records.Select(r => ((Guid)r.Data[SystemColumns.Id]!, r.Depth));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("_id")]
    public async Task Descendants_are_returned_with_their_depth_whether_or_not_columns_are_named(string? columns)
    {
        var tree = await CreateTreeAsync();

        var result = await _harness.Hierarchy.GetDescendantsAsync(
            tree.Request(tree.Root, columns), TestContext.Current.CancellationToken);

        IdsAndDepths(result).Should().Equal((tree.Child, 1), (tree.Grandchild, 2));
        result.MaxDepth.Should().Be(2);
        result.Records.Should().OnlyContain(r => !r.Data.ContainsKey("depth"), "depth is the record's own property");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("_id")]
    public async Task Ancestors_are_returned_with_their_depth_whether_or_not_columns_are_named(string? columns)
    {
        var tree = await CreateTreeAsync();

        var result = await _harness.Hierarchy.GetAncestorsAsync(
            tree.Request(tree.Grandchild, columns), TestContext.Current.CancellationToken);

        IdsAndDepths(result).Should().Equal((tree.Child, 1), (tree.Root, 2));
        result.Records.Should().OnlyContain(r => !r.Data.ContainsKey("depth"));
    }

    /// <summary>The path carries a second computed column, <c>path</c>, built the same way.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("_id")]
    public async Task The_path_to_the_root_is_returned_whether_or_not_columns_are_named(string? columns)
    {
        var tree = await CreateTreeAsync();

        var result = await _harness.Hierarchy.GetPathToRootAsync(
            tree.Request(tree.Grandchild, columns), TestContext.Current.CancellationToken);

        IdsAndDepths(result).Should().Equal((tree.Root, 2), (tree.Child, 1), (tree.Grandchild, 0));
        result.Records[0].Path.Should().Equal(tree.Grandchild, tree.Child, tree.Root);
        result.Records.Should().OnlyContain(r => !r.Data.ContainsKey("depth") && !r.Data.ContainsKey("path"));
    }

    /// <summary>
    /// With no columns named a record carries the whole row — in the names the table was declared
    /// with, like a row read any other way: no physical column name, and no internal column.
    /// </summary>
    [Fact]
    public async Task Without_named_columns_a_record_carries_the_whole_row_in_logical_names()
    {
        var tree = await CreateTreeAsync();
        var request = tree.Request(tree.Child, columns: null);
        var ct = TestContext.Current.CancellationToken;

        var descendant = (await _harness.Hierarchy.GetDescendantsAsync(request, ct)).Records.Should().ContainSingle().Subject;
        descendant.Data[SystemColumns.Id].Should().Be(tree.Grandchild);
        descendant.Data[SystemColumns.ParentId].Should().Be(tree.Child);
        descendant.Data["title"].Should().Be("grandchild");

        var sibling = (await _harness.Hierarchy.GetSiblingsAsync(request with { IncludeSelf = true }, ct))
            .Records.Should().ContainSingle().Subject;
        sibling.Data["title"].Should().Be("child");

        var records = new[]
        {
            descendant,
            sibling,
            (await _harness.Hierarchy.GetAncestorsAsync(request, ct)).Records.Should().ContainSingle().Subject,
            (await _harness.Hierarchy.GetPathToRootAsync(request, ct)).Records[0]
        };
        foreach (var record in records)
        {
            record.Data.Keys.Should().NotContain(key => PhysicalNameGuard.IsPhysicalName(key));
            record.Data.Keys.Should().NotContain(SystemColumns.ProjectId);
            record.Data.Keys.Should().Contain(["title", "owner", "department"]);
        }
    }
    [Fact]
    public async Task Columns_and_order_are_named_as_the_table_declares_them()
    {
        var tree = await CreateTreeAsync();
        var sibling = await _harness.SeedAsync(tree.Table, new Dictionary<string, object?> { ["title"] = "another child", [SystemColumns.ParentId] = tree.Root });
        var ct = TestContext.Current.CancellationToken;

        var result = await _harness.Hierarchy.GetDescendantsAsync(
            tree.Request(tree.Root, "_id,title") with { OrderBy = "title desc", MaxDepth = 1 }, ct);

        result.Records.Select(r => r.Data["title"]).Should().Equal("child", "another child");
        result.Records.Select(r => (Guid)r.Data[SystemColumns.Id]!).Should().Equal(tree.Child, sibling);
    }

    [Theory]
    [InlineData("title; DROP TABLE x")]
    [InlineData("title, (SELECT 1)")]
    [InlineData("1")]
    [InlineData("no_such_column")]
    [InlineData("title sideways")]
    public async Task An_order_that_is_not_columns_of_the_table_is_refused_before_it_reaches_PostgreSQL(string orderBy)
    {
        var tree = await CreateTreeAsync();

        var act = () => _harness.Hierarchy.GetDescendantsAsync(
            tree.Request(tree.Root, columns: null) with { OrderBy = orderBy }, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain("'orderBy'");
    }

    [Fact]
    public async Task A_column_the_table_does_not_store_is_refused_by_name()
    {
        var tree = await CreateTreeAsync();
        var ct = TestContext.Current.CancellationToken;

        var columns = () => _harness.Hierarchy.GetAncestorsAsync(tree.Request(tree.Child, "title,no_such_column"), ct);
        (await columns.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain("no_such_column");

        var parent = () => _harness.Hierarchy.GetDescendantsAsync(
            tree.Request(tree.Root, columns: null) with { ParentColumn = "no_such_parent" }, ct);
        (await parent.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain("no_such_parent");
    }
}
