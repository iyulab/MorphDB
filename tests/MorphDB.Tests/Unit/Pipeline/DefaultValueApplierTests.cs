using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;
using MorphDB.Npgsql.Pipeline.Transformers;

namespace MorphDB.Tests.Unit.Pipeline;

public class DefaultValueApplierTests
{
    private readonly DefaultValueApplier _sut = new();

    private static WriteContext CreateContext(
        WriteOperationType opType = WriteOperationType.Insert,
        bool applyDefaults = true,
        IReadOnlyList<ColumnMetadata>? columns = null)
    {
        return new WriteContext
        {
            ProjectId = Guid.NewGuid(),
            Table = new TableMetadata
            {
                LogicalName = "test_table",
                PhysicalName = "tbl_test",
                Columns = columns ?? []
            },
            OperationType = opType,
            Data = new Dictionary<string, object?>(),
            OriginalData = new Dictionary<string, object?>(),
            Options = new WriteOptions { ApplyDefaults = applyDefaults }
        };
    }

    private static ColumnMetadata CreateColumn(
        string name,
        MorphDataType dataType = MorphDataType.Text,
        DefaultValueType defaultType = DefaultValueType.Static,
        string? defaultValue = null)
    {
        return new ColumnMetadata
        {
            ColumnId = Guid.NewGuid(),
            LogicalName = name,
            PhysicalName = $"col_{name}",
            DataType = dataType,
            NativeType = "text",
            IsActive = true,
            DefaultType = defaultType,
            DefaultValue = defaultValue
        };
    }

    [Fact]
    public void ShouldExecute_InsertWithDefaults_ReturnsTrue()
    {
        var context = CreateContext(WriteOperationType.Insert);
        _sut.ShouldExecute(context).Should().BeTrue();
    }

    [Fact]
    public void ShouldExecute_UpdateOperation_ReturnsFalse()
    {
        var context = CreateContext(WriteOperationType.Update);
        _sut.ShouldExecute(context).Should().BeFalse();
    }

    [Fact]
    public void ShouldExecute_ApplyDefaultsFalse_ReturnsFalse()
    {
        var context = CreateContext(applyDefaults: false);
        _sut.ShouldExecute(context).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_StaticDefault_Text_AppliesValue()
    {
        var columns = new List<ColumnMetadata>
        {
            CreateColumn("status", MorphDataType.Text, DefaultValueType.Static, "active")
        };
        var context = CreateContext(columns: columns);

        await _sut.ExecuteAsync(context);

        context.Data["status"].Should().Be("active");
    }

    [Fact]
    public async Task ExecuteAsync_StaticDefault_Integer_AppliesValue()
    {
        var columns = new List<ColumnMetadata>
        {
            CreateColumn("count", MorphDataType.Integer, DefaultValueType.Static, "0")
        };
        var context = CreateContext(columns: columns);

        await _sut.ExecuteAsync(context);

        context.Data["count"].Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_StaticDefault_Boolean_AppliesValue()
    {
        var columns = new List<ColumnMetadata>
        {
            CreateColumn("is_active", MorphDataType.Boolean, DefaultValueType.Static, "true")
        };
        var context = CreateContext(columns: columns);

        await _sut.ExecuteAsync(context);

        context.Data["is_active"].Should().Be(true);
    }

    [Fact]
    public async Task ExecuteAsync_ExistingValue_DoesNotOverride()
    {
        var columns = new List<ColumnMetadata>
        {
            CreateColumn("status", MorphDataType.Text, DefaultValueType.Static, "active")
        };
        var context = CreateContext(columns: columns);
        context.Data["status"] = "inactive";

        await _sut.ExecuteAsync(context);

        context.Data["status"].Should().Be("inactive");
    }

    [Fact]
    public async Task ExecuteAsync_ContextBased_Now_AppliesCurrentTime()
    {
        var columns = new List<ColumnMetadata>
        {
            CreateColumn("due_date", MorphDataType.DateTime, DefaultValueType.ContextBased, "{{now}}")
        };
        var context = CreateContext(columns: columns);
        var before = DateTimeOffset.UtcNow;

        await _sut.ExecuteAsync(context);

        context.Data.Should().ContainKey("due_date");
        var value = (DateTimeOffset)context.Data["due_date"]!;
        value.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task ExecuteAsync_ContextBased_Uuid_AppliesNewGuid()
    {
        var columns = new List<ColumnMetadata>
        {
            CreateColumn("ref_id", MorphDataType.Uuid, DefaultValueType.ContextBased, "{{uuid}}")
        };
        var context = CreateContext(columns: columns);

        await _sut.ExecuteAsync(context);

        context.Data.Should().ContainKey("ref_id");
        context.Data["ref_id"].Should().BeOfType<Guid>();
        ((Guid)context.Data["ref_id"]!).Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_NoDefault_DoesNotAddField()
    {
        var columns = new List<ColumnMetadata>
        {
            CreateColumn("optional", MorphDataType.Text, DefaultValueType.None, null)
        };
        var context = CreateContext(columns: columns);

        await _sut.ExecuteAsync(context);

        context.Data.Should().NotContainKey("optional");
    }

    [Fact]
    public async Task ExecuteAsync_Computed_BareFieldReference_CopiesTheReferencedValue()
    {
        var columns = new List<ColumnMetadata>
        {
            CreateColumn("total", MorphDataType.Decimal, DefaultValueType.Computed, "field1")
        };
        var context = CreateContext(columns: columns);
        context.Data["field1"] = 7m;

        await _sut.ExecuteAsync(context);

        context.Data["total"].Should().Be(7m);
    }

    [Fact]
    public async Task ExecuteAsync_Computed_SumOfTwoFields_ComputesTheArithmeticExpression()
    {
        // This is the P2-o regression: the pre-fix implementation only did the bare-field-reference
        // case above and returned null for anything with an operator in it.
        var columns = new List<ColumnMetadata>
        {
            CreateColumn("total", MorphDataType.Decimal, DefaultValueType.Computed, "field1 + field2")
        };
        var context = CreateContext(columns: columns);
        context.Data["field1"] = 3m;
        context.Data["field2"] = 4m;

        await _sut.ExecuteAsync(context);

        context.Data["total"].Should().Be(7m);
    }

    [Fact]
    public async Task ExecuteAsync_Computed_FieldTimesLiteral_ComputesAndConvertsToColumnType()
    {
        var columns = new List<ColumnMetadata>
        {
            CreateColumn("bonus", MorphDataType.Integer, DefaultValueType.Computed, "field1 * 0.1")
        };
        var context = CreateContext(columns: columns);
        context.Data["field1"] = 50;

        await _sut.ExecuteAsync(context);

        context.Data["bonus"].Should().Be(5);
    }

    [Fact]
    public async Task ExecuteAsync_Computed_DivisionByZero_ReturnsNullInsteadOfThrowing()
    {
        var columns = new List<ColumnMetadata>
        {
            CreateColumn("ratio", MorphDataType.Decimal, DefaultValueType.Computed, "field1 / field2")
        };
        var context = CreateContext(columns: columns);
        context.Data["field1"] = 10m;
        context.Data["field2"] = 0m;

        await _sut.ExecuteAsync(context);

        context.Data.Should().NotContainKey("ratio");
    }

    [Fact]
    public async Task ExecuteAsync_Computed_UnresolvableOperand_StaysSilentLikeAnUnparsableStaticDefault()
    {
        var columns = new List<ColumnMetadata>
        {
            CreateColumn("total", MorphDataType.Decimal, DefaultValueType.Computed, "field1 + missing_field")
        };
        var context = CreateContext(columns: columns);
        context.Data["field1"] = 3m;

        await _sut.ExecuteAsync(context);

        context.Data.Should().NotContainKey("total");
    }
}
