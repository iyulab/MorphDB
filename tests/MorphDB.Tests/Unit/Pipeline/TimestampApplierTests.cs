using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;
using MorphDB.Npgsql.Pipeline.Transformers;

namespace MorphDB.Tests.Unit.Pipeline;

public class TimestampApplierTests
{
    private readonly TimestampApplier _sut = new();

    private static WriteContext CreateContext(
        WriteOperationType opType = WriteOperationType.Insert,
        bool timestampsEnabled = true,
        bool applyTimestamps = true)
    {
        return new WriteContext
        {
            ProjectId = Guid.NewGuid(),
            Table = new TableMetadata
            {
                LogicalName = "test_table",
                PhysicalName = "tbl_test",
                TimestampsEnabled = timestampsEnabled,
                Columns = []
            },
            OperationType = opType,
            Data = new Dictionary<string, object?>(),
            OriginalData = new Dictionary<string, object?>(),
            Options = new WriteOptions { ApplyTimestamps = applyTimestamps }
        };
    }

    [Fact]
    public void ShouldExecute_InsertWithTimestamps_ReturnsTrue()
    {
        var context = CreateContext(WriteOperationType.Insert);
        _sut.ShouldExecute(context).Should().BeTrue();
    }

    [Fact]
    public void ShouldExecute_DeleteOperation_ReturnsFalse()
    {
        var context = CreateContext(WriteOperationType.Delete);
        _sut.ShouldExecute(context).Should().BeFalse();
    }

    [Fact]
    public void ShouldExecute_TimestampsDisabled_ReturnsFalse()
    {
        var context = CreateContext(timestampsEnabled: false);
        _sut.ShouldExecute(context).Should().BeFalse();
    }

    [Fact]
    public void ShouldExecute_ApplyTimestampsFalse_ReturnsFalse()
    {
        var context = CreateContext(applyTimestamps: false);
        _sut.ShouldExecute(context).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_Insert_SetsCreatedAtAndUpdatedAt()
    {
        var context = CreateContext(WriteOperationType.Insert);
        var before = DateTimeOffset.UtcNow;

        await _sut.ExecuteAsync(context);

        context.Data.Should().ContainKey("_created_at");
        context.Data.Should().ContainKey("_updated_at");

        var createdAt = (DateTimeOffset)context.Data["_created_at"]!;
        var updatedAt = (DateTimeOffset)context.Data["_updated_at"]!;
        createdAt.Should().BeOnOrAfter(before);
        updatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task ExecuteAsync_Update_SetsUpdatedAtOnly()
    {
        var context = CreateContext(WriteOperationType.Update);

        await _sut.ExecuteAsync(context);

        context.Data.Should().NotContainKey("_created_at");
        context.Data.Should().ContainKey("_updated_at");
    }

    [Fact]
    public async Task ExecuteAsync_Insert_DoesNotOverrideExistingCreatedAt()
    {
        var existingTime = DateTimeOffset.UtcNow.AddDays(-1);
        var context = CreateContext(WriteOperationType.Insert);
        context.Data["_created_at"] = existingTime;

        await _sut.ExecuteAsync(context);

        context.Data["_created_at"].Should().Be(existingTime);
    }
}
