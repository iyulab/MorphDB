using MorphDB.Npgsql.Infrastructure;

namespace MorphDB.Tests.Unit;

public class Sha256NameHasherTests
{
    private readonly Sha256NameHasher _hasher = new();

    [Fact]
    public void GenerateTableName_ShouldReturnValidFormat()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var logicalName = "Customers";

        // Act
        var result = _hasher.GenerateTableName(tenantId, logicalName);

        // Assert
        result.Should().StartWith("tbl_");
        result.Length.Should().BeLessOrEqualTo(63);
        _hasher.IsValidPhysicalName(result).Should().BeTrue();
    }

    [Fact]
    public void GenerateColumnName_ShouldReturnValidFormat()
    {
        // Arrange
        var tableId = Guid.NewGuid();
        var logicalName = "CustomerName";

        // Act
        var result = _hasher.GenerateColumnName(tableId, logicalName);

        // Assert
        result.Should().StartWith("col_");
        result.Length.Should().BeLessOrEqualTo(63);
    }

    [Fact]
    public void GenerateTableName_SameName_ShouldReturnDifferentHashForDifferentTenants()
    {
        // Arrange
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();
        var logicalName = "Orders";

        // Act
        var result1 = _hasher.GenerateTableName(tenant1, logicalName);
        var result2 = _hasher.GenerateTableName(tenant2, logicalName);

        // Assert
        result1.Should().NotBe(result2);
    }

    [Fact]
    public void GenerateTableName_SameInput_ShouldReturnConsistentHash()
    {
        // Arrange
        var tenantId = Guid.Parse("12345678-1234-1234-1234-123456789012");
        var logicalName = "Products";

        // Act
        var result1 = _hasher.GenerateTableName(tenantId, logicalName);
        var result2 = _hasher.GenerateTableName(tenantId, logicalName);

        // Assert
        result1.Should().Be(result2);
    }

    [Fact]
    public void GenerateIndexName_ShouldReturnValidFormat()
    {
        // Arrange
        var tableId = Guid.NewGuid();
        var logicalName = "idx_customer_email";

        // Act
        var result = _hasher.GenerateIndexName(tableId, logicalName);

        // Assert
        result.Should().StartWith("idx_");
        result.Length.Should().BeLessOrEqualTo(63);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void IsValidPhysicalName_EmptyOrNull_ShouldReturnFalse(string? name)
    {
        // Act
        var result = _hasher.IsValidPhysicalName(name!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsValidPhysicalName_TooLong_ShouldReturnFalse()
    {
        // Arrange
        var longName = new string('a', 64);

        // Act
        var result = _hasher.IsValidPhysicalName(longName);

        // Assert
        result.Should().BeFalse();
    }
}
