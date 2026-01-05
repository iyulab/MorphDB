using FluentAssertions;
using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;
using MorphDB.Npgsql.Pipeline;
using MorphDB.Npgsql.Pipeline.Validators;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Tests for CheckValidator, particularly AND/OR compound expression support.
/// </summary>
public class CheckValidatorTests
{
    private readonly CheckValidator _validator;

    public CheckValidatorTests()
    {
        _validator = new CheckValidator();
    }

    [Theory]
    [InlineData("price > 0", 10, true)]
    [InlineData("price > 0", -5, false)]
    [InlineData("price >= 0", 0, true)]
    [InlineData("price < 100", 50, true)]
    [InlineData("price <= 100", 100, true)]
    public void SimpleExpression_ShouldEvaluateCorrectly(string expression, object value, bool expected)
    {
        // Arrange
        var context = CreateMockContext("price", expression, value);

        // Act
        _validator.ExecuteAsync(context);

        // Assert
        if (expected)
        {
            context.Errors.Should().BeEmpty();
        }
        else
        {
            context.Errors.Should().NotBeEmpty();
        }
    }

    [Theory]
    [InlineData("price > 0 AND quantity >= 1", 10, 5, true)]
    [InlineData("price > 0 AND quantity >= 1", 10, 0, false)]
    [InlineData("price > 0 AND quantity >= 1", -5, 5, false)]
    [InlineData("price > 0 AND quantity >= 1", -5, 0, false)]
    public void AndExpression_ShouldEvaluateCorrectly(string expression, object priceValue, object quantityValue, bool expected)
    {
        // Arrange
        var context = CreateMockContextWithMultipleFields("price", expression, priceValue,
            new Dictionary<string, object?> { ["price"] = priceValue, ["quantity"] = quantityValue });

        // Act
        _validator.ExecuteAsync(context);

        // Assert
        if (expected)
        {
            context.Errors.Should().BeEmpty();
        }
        else
        {
            context.Errors.Should().NotBeEmpty();
        }
    }

    [Theory]
    [InlineData("status = 'active' OR status = 'pending'", "active", true)]
    [InlineData("status = 'active' OR status = 'pending'", "pending", true)]
    [InlineData("status = 'active' OR status = 'pending'", "inactive", false)]
    public void OrExpression_ShouldEvaluateCorrectly(string expression, string value, bool expected)
    {
        // Arrange
        var context = CreateMockContext("status", expression, value);

        // Act
        _validator.ExecuteAsync(context);

        // Assert
        if (expected)
        {
            context.Errors.Should().BeEmpty();
        }
        else
        {
            context.Errors.Should().NotBeEmpty();
        }
    }

    [Theory]
    [InlineData("(price > 0 AND price < 100) OR quantity = 0", 50, 5, true)]  // price valid
    [InlineData("(price > 0 AND price < 100) OR quantity = 0", 150, 0, true)] // quantity = 0
    [InlineData("(price > 0 AND price < 100) OR quantity = 0", -10, 5, false)] // neither valid
    public void NestedExpression_ShouldEvaluateCorrectly(string expression, object priceValue, object quantityValue, bool expected)
    {
        // Arrange
        var context = CreateMockContextWithMultipleFields("price", expression, priceValue,
            new Dictionary<string, object?> { ["price"] = priceValue, ["quantity"] = quantityValue });

        // Act
        _validator.ExecuteAsync(context);

        // Assert
        if (expected)
        {
            context.Errors.Should().BeEmpty();
        }
        else
        {
            context.Errors.Should().NotBeEmpty();
        }
    }

    [Fact]
    public void NullValue_ShouldBypassCheckConstraint()
    {
        // Arrange
        var context = CreateMockContext("price", "price > 0", null);

        // Act
        _validator.ExecuteAsync(context);

        // Assert
        context.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("end_date > start_date")]
    public void CrossFieldExpression_ShouldEvaluateCorrectly(string expression)
    {
        // Arrange
        var startDate = DateTimeOffset.UtcNow.AddDays(-1);
        var endDate = DateTimeOffset.UtcNow.AddDays(1);

        var context = CreateMockContextWithMultipleFields("end_date", expression, endDate,
            new Dictionary<string, object?> { ["end_date"] = endDate, ["start_date"] = startDate });

        // Act
        _validator.ExecuteAsync(context);

        // Assert
        context.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("age >= 18 AND age <= 65", 30, true)]
    [InlineData("age >= 18 AND age <= 65", 17, false)]
    [InlineData("age >= 18 AND age <= 65", 66, false)]
    public void RangeExpression_ShouldEvaluateCorrectly(string expression, int age, bool expected)
    {
        // Arrange
        var context = CreateMockContext("age", expression, age);

        // Act
        _validator.ExecuteAsync(context);

        // Assert
        if (expected)
        {
            context.Errors.Should().BeEmpty();
        }
        else
        {
            context.Errors.Should().NotBeEmpty();
        }
    }

    private static WriteContext CreateMockContext(string columnName, string checkExpression, object? value)
    {
        var column = new ColumnMetadata
        {
            ColumnId = Guid.NewGuid(),
            TableId = Guid.NewGuid(),
            LogicalName = columnName,
            PhysicalName = $"c_{columnName}",
            DataType = MorphDataType.Integer,
            NativeType = "INTEGER",
            OrdinalPosition = 1,
            CheckExpression = checkExpression,
            IsActive = true
        };

        var table = new TableMetadata
        {
            TableId = column.TableId,
            TenantId = Guid.NewGuid(),
            LogicalName = "test_table",
            PhysicalName = "t_test",
            Columns = [column],
            SchemaVersion = 1
        };

        return new WriteContext
        {
            TenantId = table.TenantId,
            Table = table,
            OperationType = WriteOperationType.Insert,
            Data = new Dictionary<string, object?> { [columnName] = value },
            Options = new WriteOptions { ValidateCheck = true }
        };
    }

    private static WriteContext CreateMockContextWithMultipleFields(
        string primaryColumn,
        string checkExpression,
        object? primaryValue,
        Dictionary<string, object?> allData)
    {
        var tableId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var columns = allData.Keys.Select((name, index) => new ColumnMetadata
        {
            ColumnId = Guid.NewGuid(),
            TableId = tableId,
            LogicalName = name,
            PhysicalName = $"c_{name}",
            DataType = MorphDataType.Integer,
            NativeType = "INTEGER",
            OrdinalPosition = index + 1,
            CheckExpression = name == primaryColumn ? checkExpression : null,
            IsActive = true
        }).ToList();

        var table = new TableMetadata
        {
            TableId = tableId,
            TenantId = tenantId,
            LogicalName = "test_table",
            PhysicalName = "t_test",
            Columns = columns,
            SchemaVersion = 1
        };

        return new WriteContext
        {
            TenantId = tenantId,
            Table = table,
            OperationType = WriteOperationType.Insert,
            Data = allData,
            Options = new WriteOptions { ValidateCheck = true }
        };
    }
}
