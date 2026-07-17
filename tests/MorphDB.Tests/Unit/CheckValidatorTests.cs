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
    [InlineData("email MATCHES '^[^@]+@[^@]+\\.[^@]+$'", "user@example.com", true)]
    [InlineData("email MATCHES '^[^@]+@[^@]+\\.[^@]+$'", "invalid-email", false)]
    [InlineData("code MATCHES '^[A-Z]{3}-\\d{4}$'", "ABC-1234", true)]
    [InlineData("code MATCHES '^[A-Z]{3}-\\d{4}$'", "abc-1234", false)]
    [InlineData("code MATCHES '^[A-Z]{3}-\\d{4}$'", "ABCD-123", false)]
    public void MatchesExpression_ShouldEvaluateCorrectly(string expression, string value, bool expected)
    {
        // Arrange
        var columnName = expression.Split(' ')[0];
        var context = CreateMockContextForString(columnName, expression, value);

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
    public void MatchesExpression_WithNullValue_ShouldBypassValidation()
    {
        // Arrange
        var context = CreateMockContextForString("email", "email MATCHES '^.+@.+$'", null);

        // Act
        _validator.ExecuteAsync(context);

        // Assert
        context.Errors.Should().BeEmpty();
    }

    [Fact]
    public void MatchesExpression_WithInvalidRegex_ShouldPassValidation()
    {
        // Arrange — invalid regex pattern (unmatched parenthesis)
        var context = CreateMockContextForString("code", "code MATCHES '([invalid'", "anything");

        // Act
        _validator.ExecuteAsync(context);

        // Assert — invalid regex patterns should not block data
        context.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("email MATCHES '^.+@.+$' AND email != ''", "user@test.com", true)]
    [InlineData("email MATCHES '^.+@.+$' AND email != ''", "invalid", false)]
    public void MatchesWithAndExpression_ShouldEvaluateCorrectly(string expression, string value, bool expected)
    {
        // Arrange
        var context = CreateMockContextForString("email", expression, value);

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

    // ---- REST JsonElement regression (issue rest-jsonelement-defects, audit finding #3) ----
    // Over REST the field value arrives as a System.Text.Json.JsonElement. CheckValidator's
    // typed comparisons (`left is string`, MATCHES `fieldValue is string`) did not match a
    // JsonElement, so string/regex CHECK constraints were *silently bypassed* (returned valid)
    // — a data-integrity defect. Numeric checks happened to work via ToString() parsing.

    [Theory]
    [InlineData("active", true)]    // satisfies the constraint
    [InlineData("pending", true)]
    [InlineData("banned", false)]   // violates it — MUST be rejected, was silently bypassed
    public void StringEquality_WithJsonElementValue_ShouldEnforceConstraint(string value, bool expectedValid)
    {
        // Arrange — value as JsonElement, exactly like REST model binding produces
        var jsonValue = System.Text.Json.JsonSerializer.SerializeToElement(value);
        var context = CreateMockContextForString("status", "status = 'active' OR status = 'pending'", jsonValue);

        // Act
        _validator.ExecuteAsync(context);

        // Assert
        if (expectedValid)
            context.Errors.Should().BeEmpty();
        else
            context.Errors.Should().NotBeEmpty("string CHECK must be enforced even when the value is a JsonElement");
    }

    [Theory]
    [InlineData("user@example.com", true)]
    [InlineData("invalid-email", false)]   // violates regex — MUST be rejected, was silently bypassed
    public void MatchesExpression_WithJsonElementValue_ShouldEnforceConstraint(string value, bool expectedValid)
    {
        // Arrange
        var jsonValue = System.Text.Json.JsonSerializer.SerializeToElement(value);
        var context = CreateMockContextForString("email", "email MATCHES '^[^@]+@[^@]+\\.[^@]+$'", jsonValue);

        // Act
        _validator.ExecuteAsync(context);

        // Assert
        if (expectedValid)
            context.Errors.Should().BeEmpty();
        else
            context.Errors.Should().NotBeEmpty("regex CHECK must be enforced even when the value is a JsonElement");
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(-5, false)]
    public void NumericExpression_WithJsonElementValue_ShouldEnforceConstraint(int value, bool expectedValid)
    {
        // Arrange — guards against a regression in the (previously accidental) numeric path
        var jsonValue = System.Text.Json.JsonSerializer.SerializeToElement(value);
        var context = CreateMockContext("price", "price > 0", jsonValue);

        // Act
        _validator.ExecuteAsync(context);

        // Assert
        if (expectedValid)
            context.Errors.Should().BeEmpty();
        else
            context.Errors.Should().NotBeEmpty();
    }

    private static WriteContext CreateMockContextForString(string columnName, string checkExpression, object? value)
    {
        var column = new ColumnMetadata
        {
            ColumnId = Guid.NewGuid(),
            TableId = Guid.NewGuid(),
            LogicalName = columnName,
            PhysicalName = $"c_{columnName}",
            DataType = MorphDataType.Text,
            NativeType = "TEXT",
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
