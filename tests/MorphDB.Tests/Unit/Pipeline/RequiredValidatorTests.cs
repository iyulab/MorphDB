using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;
using MorphDB.Npgsql.Pipeline.Validators;

namespace MorphDB.Tests.Unit.Pipeline;

public class RequiredValidatorTests
{
    private readonly RequiredValidator _sut = new();

    private static WriteContext CreateContext(
        WriteOperationType opType = WriteOperationType.Insert,
        bool validateRequired = true,
        IReadOnlyList<ColumnMetadata>? columns = null,
        IDictionary<string, object?>? data = null)
    {
        return new WriteContext
        {
            TenantId = Guid.NewGuid(),
            Table = new TableMetadata
            {
                LogicalName = "test_table",
                PhysicalName = "tbl_test",
                Columns = columns ?? []
            },
            OperationType = opType,
            Data = data ?? new Dictionary<string, object?>(),
            OriginalData = new Dictionary<string, object?>(),
            Options = new WriteOptions { ValidateRequired = validateRequired }
        };
    }

    private static ColumnMetadata CreateColumn(
        string name,
        bool isRequired = true,
        bool isPrimaryKey = false,
        DefaultValueType defaultType = DefaultValueType.None,
        string? defaultValue = null)
    {
        return new ColumnMetadata
        {
            ColumnId = Guid.NewGuid(),
            LogicalName = name,
            PhysicalName = $"col_{name}",
            DataType = MorphDataType.Text,
            NativeType = "text",
            IsActive = true,
            IsRequired = isRequired,
            IsPrimaryKey = isPrimaryKey,
            DefaultType = defaultType,
            DefaultValue = defaultValue
        };
    }

    [Fact]
    public void ShouldExecute_InsertWithValidation_ReturnsTrue()
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
    public void ShouldExecute_ValidateRequiredFalse_ReturnsFalse()
    {
        var context = CreateContext(validateRequired: false);
        _sut.ShouldExecute(context).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_RequiredFieldProvided_NoErrors()
    {
        var columns = new List<ColumnMetadata> { CreateColumn("name") };
        var data = new Dictionary<string, object?> { ["name"] = "John" };
        var context = CreateContext(columns: columns, data: data);

        await _sut.ExecuteAsync(context);

        context.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_RequiredFieldMissing_AddsError()
    {
        var columns = new List<ColumnMetadata> { CreateColumn("name") };
        var context = CreateContext(columns: columns);

        await _sut.ExecuteAsync(context);

        context.Errors.Should().HaveCount(1);
        context.Errors[0].Field.Should().Be("name");
        context.Errors[0].Code.Should().Be(ValidationErrorCodes.Required);
    }

    [Fact]
    public async Task ExecuteAsync_RequiredFieldNull_AddsError()
    {
        var columns = new List<ColumnMetadata> { CreateColumn("name") };
        var data = new Dictionary<string, object?> { ["name"] = null };
        var context = CreateContext(columns: columns, data: data);

        await _sut.ExecuteAsync(context);

        context.Errors.Should().HaveCount(1);
        context.Errors[0].Code.Should().Be(ValidationErrorCodes.Required);
    }

    [Fact]
    public async Task ExecuteAsync_RequiredFieldWithDefault_NoError()
    {
        var columns = new List<ColumnMetadata>
        {
            CreateColumn("status", defaultType: DefaultValueType.Static, defaultValue: "active")
        };
        var context = CreateContext(columns: columns);

        await _sut.ExecuteAsync(context);

        context.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_PrimaryKeyColumn_Skipped()
    {
        var columns = new List<ColumnMetadata>
        {
            CreateColumn("_id", isPrimaryKey: true)
        };
        var context = CreateContext(columns: columns);

        await _sut.ExecuteAsync(context);

        context.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_Update_FieldNotInPayload_Skipped()
    {
        var columns = new List<ColumnMetadata> { CreateColumn("name") };
        var data = new Dictionary<string, object?> { ["email"] = "test@example.com" };
        var context = CreateContext(WriteOperationType.Update, columns: columns, data: data);

        await _sut.ExecuteAsync(context);

        context.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_Update_FieldInPayloadAsNull_AddsError()
    {
        var columns = new List<ColumnMetadata> { CreateColumn("name") };
        var data = new Dictionary<string, object?> { ["name"] = null };
        var context = CreateContext(WriteOperationType.Update, columns: columns, data: data);

        await _sut.ExecuteAsync(context);

        context.Errors.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleRequiredFields_ReportsAllErrors()
    {
        var columns = new List<ColumnMetadata>
        {
            CreateColumn("name"),
            CreateColumn("email"),
            CreateColumn("phone")
        };
        var context = CreateContext(columns: columns);

        await _sut.ExecuteAsync(context);

        context.Errors.Should().HaveCount(3);
    }

    [Fact]
    public async Task ExecuteAsync_OptionalField_NoError()
    {
        var columns = new List<ColumnMetadata>
        {
            CreateColumn("notes", isRequired: false)
        };
        var context = CreateContext(columns: columns);

        await _sut.ExecuteAsync(context);

        context.Errors.Should().BeEmpty();
    }
}
