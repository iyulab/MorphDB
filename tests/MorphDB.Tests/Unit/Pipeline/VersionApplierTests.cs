using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;
using MorphDB.Npgsql.Pipeline.Transformers;

namespace MorphDB.Tests.Unit.Pipeline;

public class VersionApplierTests
{
    private readonly VersionApplier _sut = new();

    private static WriteContext CreateContext(
        WriteOperationType opType = WriteOperationType.Insert,
        bool versioningEnabled = true,
        bool applyVersion = true,
        int? expectedVersion = null,
        IDictionary<string, object?>? existingData = null)
    {
        return new WriteContext
        {
            TenantId = Guid.NewGuid(),
            Table = new TableMetadata
            {
                LogicalName = "test_table",
                PhysicalName = "tbl_test",
                VersioningEnabled = versioningEnabled,
                Columns = []
            },
            OperationType = opType,
            Data = new Dictionary<string, object?>(),
            OriginalData = new Dictionary<string, object?>(),
            ExistingData = existingData,
            Options = new WriteOptions { ApplyVersion = applyVersion, ExpectedVersion = expectedVersion }
        };
    }

    [Fact]
    public void ShouldExecute_InsertWithVersioning_ReturnsTrue()
    {
        var context = CreateContext(WriteOperationType.Insert);
        _sut.ShouldExecute(context).Should().BeTrue();
    }

    [Fact]
    public void ShouldExecute_VersioningDisabled_ReturnsFalse()
    {
        var context = CreateContext(versioningEnabled: false);
        _sut.ShouldExecute(context).Should().BeFalse();
    }

    [Fact]
    public void ShouldExecute_ApplyVersionFalse_ReturnsFalse()
    {
        var context = CreateContext(applyVersion: false);
        _sut.ShouldExecute(context).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_Insert_SetsVersionTo1()
    {
        var context = CreateContext(WriteOperationType.Insert);

        await _sut.ExecuteAsync(context);

        context.Data["_version"].Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_Update_SetsVersionIncrement()
    {
        var context = CreateContext(WriteOperationType.Update);

        await _sut.ExecuteAsync(context);

        context.Data["_version"].Should().BeOfType<VersionIncrement>();
    }

    [Fact]
    public async Task ExecuteAsync_UpdateWithExpectedVersion_MatchesCurrent_Succeeds()
    {
        var existingData = new Dictionary<string, object?> { ["_version"] = 3 };
        var context = CreateContext(WriteOperationType.Update, expectedVersion: 3, existingData: existingData);

        await _sut.ExecuteAsync(context);

        context.Errors.Should().BeEmpty();
        context.ShouldContinue.Should().BeTrue();
        context.Data["_version"].Should().BeOfType<VersionIncrement>();
    }

    [Fact]
    public async Task ExecuteAsync_UpdateWithExpectedVersion_Mismatch_AddsError()
    {
        var existingData = new Dictionary<string, object?> { ["_version"] = 5 };
        var context = CreateContext(WriteOperationType.Update, expectedVersion: 3, existingData: existingData);

        await _sut.ExecuteAsync(context);

        context.Errors.Should().HaveCount(1);
        context.Errors[0].Code.Should().Be(ValidationErrorCodes.VersionConflict);
        context.ShouldContinue.Should().BeFalse();
    }
}
