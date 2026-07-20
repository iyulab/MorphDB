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
}
