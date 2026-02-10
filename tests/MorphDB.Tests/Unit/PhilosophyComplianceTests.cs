using System.Text.Json;
using FluentAssertions;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Service.Models.Api;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Tests for MorphDB Philosophy Compliance.
///
/// Core Philosophy: "Virtual DOM for Database"
/// - Logical-Physical Separation: Users work with logical names only
/// - Physical names (tbl_*, col_*, i_*, r_*) are internal implementation details
/// - API responses must NEVER expose physical names
/// </summary>
public class PhilosophyComplianceTests
{
    /// <summary>
    /// Validates that physical name patterns are detected correctly.
    /// Physical name patterns: tbl_*, col_*, idx_* (hash-based identifiers)
    /// </summary>
    [Theory]
    [InlineData("tbl_a7f3b2c1d4e5", true)]
    [InlineData("col_e9d8c7b6a5f4", true)]
    [InlineData("idx_1234567890ab", true)]
    [InlineData("fk_fedcba098765", true)]
    [InlineData("customers", false)]
    [InlineData("email", false)]
    [InlineData("created_at", false)]
    [InlineData("user_email_idx", false)]
    public void PhysicalNamePattern_ShouldBeDetectedCorrectly(string name, bool isPhysical)
    {
        // Physical names are hash-based: tbl_[hash], col_[hash], i_[hash], r_[hash]
        var isPhysicalName = IsPhysicalName(name);
        isPhysicalName.Should().Be(isPhysical, $"'{name}' physical detection should be {isPhysical}");
    }

    /// <summary>
    /// TableApiResponse must only expose LogicalName, never PhysicalName.
    /// </summary>
    [Fact]
    public void TableApiResponse_ShouldNotExposePhysicalName()
    {
        // Arrange
        var table = CreateTestTable();

        // Act
        var response = TableApiResponse.FromMetadata(table);

        // Assert - Response should use logical name
        response.Name.Should().Be("customers");

        // Verify response doesn't contain physical patterns via JSON serialization
        var json = JsonSerializer.Serialize(response);
        json.Should().NotContain("tbl_", "TableApiResponse should not expose physical table name");
        json.Should().NotContain("col_", "TableApiResponse should not expose physical column names");
    }

    /// <summary>
    /// ColumnApiResponse must expose LogicalName, not PhysicalName.
    /// </summary>
    [Fact]
    public void ColumnApiResponse_ShouldUseLogicalName()
    {
        // Arrange
        var column = new ColumnMetadata
        {
            ColumnId = Guid.NewGuid(),
            TableId = Guid.NewGuid(),
            LogicalName = "email",
            PhysicalName = "col_e9d8c7b6",
            DataType = MorphDataType.Text,
            NativeType = "TEXT",
            OrdinalPosition = 1
        };

        // Act
        var response = ColumnApiResponse.FromMetadata(column);

        // Assert
        response.Name.Should().Be("email");

        var json = JsonSerializer.Serialize(response);
        json.Should().NotContain("col_", "ColumnApiResponse should not expose physical column name");
    }

    /// <summary>
    /// IndexApiResponse columns should use LogicalName, not PhysicalName.
    /// </summary>
    [Fact]
    public void IndexApiResponse_ShouldUseLogicalColumnNames()
    {
        // Arrange
        var index = new IndexMetadata
        {
            IndexId = Guid.NewGuid(),
            TableId = Guid.NewGuid(),
            LogicalName = "idx_users_email",
            PhysicalName = "i_1234567890abcdef",
            Columns = new List<IndexColumnInfo>
            {
                new()
                {
                    ColumnId = Guid.NewGuid(),
                    LogicalName = "email",
                    PhysicalName = "col_e9d8c7b6",
                    Direction = SortDirection.Ascending,
                    NullsPosition = NullsPosition.Last
                }
            },
            IndexType = IndexType.BTree,
            IsUnique = true
        };

        // Act
        var response = IndexApiResponse.FromMetadata(index);

        // Assert
        response.Columns.Should().Contain("email");
        response.Columns.Should().NotContain("col_e9d8c7b6");

        var json = JsonSerializer.Serialize(response);
        json.Should().NotContain("col_", "IndexApiResponse should not expose physical column names");
    }

    /// <summary>
    /// Sha256NameHasher produces physical names with correct prefixes.
    /// </summary>
    [Fact]
    public void NameHasher_ShouldProducePhysicalNamePatterns()
    {
        // Arrange
        var hasher = new Sha256NameHasher();
        var tenantId = Guid.NewGuid();
        var tableId = Guid.NewGuid();

        // Act
        var tablePhysical = hasher.GenerateTableName(tenantId, "customers");
        var columnPhysical = hasher.GenerateColumnName(tableId, "email");
        var indexPhysical = hasher.GenerateIndexName(tableId, "idx_email");
        var constraintPhysical = hasher.GenerateConstraintName("fk", tableId, "orders_customer");

        // Assert - Physical names follow prefix patterns
        tablePhysical.Should().StartWith("tbl_");
        columnPhysical.Should().StartWith("col_");
        indexPhysical.Should().StartWith("idx_");
        constraintPhysical.Should().StartWith("fk_");

        // Physical names should be consistent (deterministic)
        var tablePhysical2 = hasher.GenerateTableName(tenantId, "customers");
        tablePhysical.Should().Be(tablePhysical2);
    }

    /// <summary>
    /// Data records returned from queries should use logical column names as keys.
    /// </summary>
    [Fact]
    public void DataRecords_ShouldUseLogicalColumnNames()
    {
        // This test validates the principle that data returned to users
        // must use logical column names, not physical names.

        // Simulate a record with physical column names
        var physicalRecord = new Dictionary<string, object?>
        {
            ["col_e9d8c7b6"] = "john@example.com",
            ["col_f1g2h3i4"] = "John Doe"
        };

        // Column mapping (physical -> logical)
        var columnMapping = new Dictionary<string, string>
        {
            ["col_e9d8c7b6"] = "email",
            ["col_f1g2h3i4"] = "name"
        };

        // Act - Transform to logical names (simulating MapToLogicalDictionary)
        var logicalRecord = new Dictionary<string, object?>();
        foreach (var kvp in physicalRecord)
        {
            if (columnMapping.TryGetValue(kvp.Key, out var logicalName))
            {
                logicalRecord[logicalName] = kvp.Value;
            }
        }

        // Assert
        logicalRecord.Keys.Should().Contain("email");
        logicalRecord.Keys.Should().Contain("name");
        logicalRecord.Keys.Should().NotContain("col_e9d8c7b6");
        logicalRecord.Keys.Should().NotContain("col_f1g2h3i4");
    }

    /// <summary>
    /// System columns should use underscore prefix pattern.
    /// </summary>
    [Theory]
    [InlineData("_id")]
    [InlineData("_created_at")]
    [InlineData("_updated_at")]
    [InlineData("_version")]
    [InlineData("_created_by")]
    [InlineData("_updated_by")]
    [InlineData("_owner_id")]
    [InlineData("_tenant_id")]
    public void SystemColumns_ShouldUseUnderscorePrefix(string columnName)
    {
        // System columns are user-facing (logical names with underscore prefix)
        // They should NOT be confused with physical names
        var isPhysical = IsPhysicalName(columnName);
        isPhysical.Should().BeFalse($"System column '{columnName}' should not be detected as physical name");
    }

    /// <summary>
    /// Helper method to detect physical name patterns.
    /// Physical names follow: prefix_[hexadecimal hash]
    /// Prefixes: tbl_, col_, idx_, fk_, pk_, uq_, chk_, view_
    /// </summary>
    private static bool IsPhysicalName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        // Physical name patterns with hash suffixes
        var prefixes = new[] { "tbl_", "col_", "idx_", "fk_", "pk_", "uq_", "chk_", "view_" };

        foreach (var prefix in prefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var suffix = name[prefix.Length..];
                // Check if suffix is hexadecimal (at least 8 chars)
                return suffix.Length >= 8 && suffix.All(c =>
                    (c >= '0' && c <= '9') ||
                    (c >= 'a' && c <= 'f') ||
                    (c >= 'A' && c <= 'F'));
            }
        }

        return false;
    }

    private static TableMetadata CreateTestTable()
    {
        return new TableMetadata
        {
            TableId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            LogicalName = "customers",
            PhysicalName = "tbl_a7f3b2c1",
            SchemaVersion = 1,
            Columns = new List<ColumnMetadata>
            {
                new()
                {
                    ColumnId = Guid.NewGuid(),
                    TableId = Guid.NewGuid(),
                    LogicalName = "email",
                    PhysicalName = "col_e9d8c7b6",
                    DataType = MorphDataType.Text,
                    NativeType = "TEXT",
                    OrdinalPosition = 1
                },
                new()
                {
                    ColumnId = Guid.NewGuid(),
                    TableId = Guid.NewGuid(),
                    LogicalName = "name",
                    PhysicalName = "col_f1g2h3i4",
                    DataType = MorphDataType.Text,
                    NativeType = "TEXT",
                    OrdinalPosition = 2
                }
            }
        };
    }
}
