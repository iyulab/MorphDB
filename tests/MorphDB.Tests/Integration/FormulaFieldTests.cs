using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Services;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration;

/// <summary>
/// Integration tests for Formula Field functionality.
/// Formula fields are virtual columns that compute values from expressions.
/// </summary>
[Collection("PostgreSQL")]
public class FormulaFieldTests
{
    private readonly PostgresFixture _fixture;
    private readonly PostgresSchemaManager _schemaManager;
    private readonly MetadataRepository _metadataRepository;

    public FormulaFieldTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _metadataRepository = new MetadataRepository(fixture.DataSource);

        var nameHasher = new Sha256NameHasher();
        var lockOptions = new AdvisoryLockOptions();
        var lockManager = new PostgresAdvisoryLockManager(fixture.DataSource, lockOptions);
        var changeLogger = new ChangeLogger(fixture.DataSource);
        var schemaOptions = new SchemaManagerOptions();

        _schemaManager = new PostgresSchemaManager(
            fixture.DataSource,
            _metadataRepository,
            lockManager,
            nameHasher,
            changeLogger,
            schemaOptions);
    }

    [Fact]
    public async Task CreateTableAsync_WithFormulaColumn_ShouldCreateVirtualColumn()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

        // Create table with formula column
        var table = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            TenantId = tenantId,
            LogicalName = "formula_products_" + uniqueSuffix,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "name",
                    DataType = MorphDataType.Text,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "price",
                    DataType = MorphDataType.Decimal,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "quantity",
                    DataType = MorphDataType.Integer,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "total",
                    DataType = MorphDataType.Decimal,
                    IsNullable = true,
                    FormulaConfig = new FormulaColumnConfig
                    {
                        Formula = "{price} * {quantity}",
                        ReturnType = MorphDataType.Decimal
                    }
                }
            ]
        });

        // Assert
        table.Should().NotBeNull();
        table.Columns.Should().Contain(c => c.LogicalName == "name");
        table.Columns.Should().Contain(c => c.LogicalName == "price");
        table.Columns.Should().Contain(c => c.LogicalName == "quantity");
        table.Columns.Should().Contain(c => c.LogicalName == "total");

        var formulaColumn = table.Columns.First(c => c.LogicalName == "total");
        formulaColumn.IsDerived.Should().BeTrue();
        formulaColumn.FormulaConfig.Should().NotBeNull();
        formulaColumn.FormulaConfig!.Formula.Should().Be("{price} * {quantity}");
        formulaColumn.FormulaConfig.ReturnType.Should().Be(MorphDataType.Decimal);

        // Virtual column should have special physical name and native type
        formulaColumn.PhysicalName.Should().StartWith("virtual_");
        formulaColumn.NativeType.Should().Be("virtual");
    }

    [Fact]
    public async Task CreateTableAsync_WithFormulaColumn_ShouldPersistFormulaConfig()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

        // Create table with formula
        var table = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            TenantId = tenantId,
            LogicalName = "formula_persist_" + uniqueSuffix,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "first_name",
                    DataType = MorphDataType.Text,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "last_name",
                    DataType = MorphDataType.Text,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "full_name",
                    DataType = MorphDataType.Text,
                    IsNullable = true,
                    FormulaConfig = new FormulaColumnConfig
                    {
                        Formula = "CONCAT({first_name}, ' ', {last_name})",
                        ReturnType = MorphDataType.Text,
                        OutputFormat = "title-case"
                    }
                }
            ]
        });

        // Act - Retrieve the table to verify persistence
        var storedTable = await _metadataRepository.GetTableByIdAsync(table.TableId, includeColumns: true);

        // Assert
        storedTable.Should().NotBeNull();
        var formulaColumn = storedTable!.Columns.First(c => c.LogicalName == "full_name");
        formulaColumn.FormulaConfig.Should().NotBeNull();
        formulaColumn.FormulaConfig!.Formula.Should().Be("CONCAT({first_name}, ' ', {last_name})");
        formulaColumn.FormulaConfig.ReturnType.Should().Be(MorphDataType.Text);
        formulaColumn.FormulaConfig.OutputFormat.Should().Be("title-case");
    }

    [Fact]
    public async Task AddColumnAsync_WithFormulaConfig_ShouldAddVirtualColumn()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

        // Create table without formula initially
        var table = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            TenantId = tenantId,
            LogicalName = "addformula_" + uniqueSuffix,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "start_date",
                    DataType = MorphDataType.DateTime,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "end_date",
                    DataType = MorphDataType.DateTime,
                    IsNullable = false
                }
            ]
        });

        var addColumnRequest = new AddColumnRequest
        {
            TableId = table.TableId,
            LogicalName = "duration_days",
            DataType = MorphDataType.Integer,
            IsNullable = true,
            ExpectedVersion = table.SchemaVersion,
            FormulaConfig = new FormulaColumnConfig
            {
                Formula = "DATEDIFF({start_date}, {end_date})",
                ReturnType = MorphDataType.Integer
            }
        };

        // Act
        var newColumn = await _schemaManager.AddColumnAsync(addColumnRequest);

        // Assert
        newColumn.Should().NotBeNull();
        newColumn.LogicalName.Should().Be("duration_days");
        newColumn.IsDerived.Should().BeTrue();
        newColumn.FormulaConfig.Should().NotBeNull();
        newColumn.FormulaConfig!.Formula.Should().Be("DATEDIFF({start_date}, {end_date})");
        newColumn.FormulaConfig.ReturnType.Should().Be(MorphDataType.Integer);
        newColumn.PhysicalName.Should().StartWith("virtual_");
        newColumn.NativeType.Should().Be("virtual");
    }

    [Fact]
    public async Task CreateTableAsync_WithMultipleFormulaColumns_ShouldCreateAllVirtuals()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

        // Create table with multiple formula columns
        var table = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            TenantId = tenantId,
            LogicalName = "multiformula_" + uniqueSuffix,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "base_price",
                    DataType = MorphDataType.Decimal,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "discount_percent",
                    DataType = MorphDataType.Decimal,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "tax_rate",
                    DataType = MorphDataType.Decimal,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "discounted_price",
                    DataType = MorphDataType.Decimal,
                    IsNullable = true,
                    FormulaConfig = new FormulaColumnConfig
                    {
                        Formula = "{base_price} * (1 - {discount_percent} / 100)",
                        ReturnType = MorphDataType.Decimal
                    }
                },
                new CreateColumnRequest
                {
                    LogicalName = "tax_amount",
                    DataType = MorphDataType.Decimal,
                    IsNullable = true,
                    FormulaConfig = new FormulaColumnConfig
                    {
                        Formula = "{base_price} * {tax_rate} / 100",
                        ReturnType = MorphDataType.Decimal
                    }
                },
                new CreateColumnRequest
                {
                    LogicalName = "final_price",
                    DataType = MorphDataType.Decimal,
                    IsNullable = true,
                    FormulaConfig = new FormulaColumnConfig
                    {
                        Formula = "{base_price} * (1 - {discount_percent} / 100) * (1 + {tax_rate} / 100)",
                        ReturnType = MorphDataType.Decimal
                    }
                }
            ]
        });

        // Assert
        table.Should().NotBeNull();

        var formulaColumns = table.Columns.Where(c => c.FormulaConfig != null).ToList();
        formulaColumns.Should().HaveCount(3);

        foreach (var col in formulaColumns)
        {
            col.IsDerived.Should().BeTrue();
            col.PhysicalName.Should().StartWith("virtual_");
            col.NativeType.Should().Be("virtual");
        }

        // Verify each formula
        formulaColumns.First(c => c.LogicalName == "discounted_price").FormulaConfig!.Formula
            .Should().Contain("discount_percent");
        formulaColumns.First(c => c.LogicalName == "tax_amount").FormulaConfig!.Formula
            .Should().Contain("tax_rate");
        formulaColumns.First(c => c.LogicalName == "final_price").FormulaConfig!.Formula
            .Should().Contain("discount_percent").And.Contain("tax_rate");
    }

    [Fact]
    public async Task CreateTableAsync_FormulaColumn_ShouldNotCreatePhysicalColumn()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

        // Create table with formula
        var table = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            TenantId = tenantId,
            LogicalName = "physical_formula_" + uniqueSuffix,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "email",
                    DataType = MorphDataType.Text,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "email_domain",
                    DataType = MorphDataType.Text,
                    IsNullable = true,
                    FormulaConfig = new FormulaColumnConfig
                    {
                        Formula = "SUBSTRING({email}, '@', 999)",
                        ReturnType = MorphDataType.Text
                    }
                }
            ]
        });

        // Act - Query PostgreSQL to check actual physical columns
        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT column_name
            FROM information_schema.columns
            WHERE table_name = '{table.PhysicalName}'
            ORDER BY ordinal_position
            """;

        var physicalColumns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            physicalColumns.Add(reader.GetString(0));
        }

        // Assert
        // The formula column "email_domain" should NOT exist in the physical schema
        var emailColumn = table.Columns.First(c => c.LogicalName == "email");
        var formulaColumn = table.Columns.First(c => c.LogicalName == "email_domain");

        // The regular column should have a physical column
        physicalColumns.Should().Contain(emailColumn.PhysicalName);

        // The virtual formula column should NOT have a physical column
        physicalColumns.Should().NotContain(formulaColumn.PhysicalName);
        physicalColumns.Should().NotContain("email_domain");

        // Verify the virtual column has the expected physical name format
        formulaColumn.PhysicalName.Should().StartWith("virtual_");
    }

    [Fact]
    public async Task CreateTableAsync_WithConditionalFormula_ShouldStoreCorrectly()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

        // Create table with conditional formula
        var table = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            TenantId = tenantId,
            LogicalName = "conditional_formula_" + uniqueSuffix,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "score",
                    DataType = MorphDataType.Integer,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "grade",
                    DataType = MorphDataType.Text,
                    IsNullable = true,
                    FormulaConfig = new FormulaColumnConfig
                    {
                        Formula = "IF({score} >= 90, 'A', IF({score} >= 80, 'B', IF({score} >= 70, 'C', 'F')))",
                        ReturnType = MorphDataType.Text
                    }
                }
            ]
        });

        // Assert
        var formulaColumn = table.Columns.First(c => c.LogicalName == "grade");
        formulaColumn.FormulaConfig.Should().NotBeNull();
        formulaColumn.FormulaConfig!.Formula.Should().Contain("IF");
        formulaColumn.FormulaConfig.ReturnType.Should().Be(MorphDataType.Text);
    }

    [Fact]
    public async Task CreateTableAsync_WithDateFormula_ShouldStoreCorrectly()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

        // Create table with date formula
        var table = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            TenantId = tenantId,
            LogicalName = "date_formula_" + uniqueSuffix,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "birth_date",
                    DataType = MorphDataType.Date,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "age_days",
                    DataType = MorphDataType.Integer,
                    IsNullable = true,
                    FormulaConfig = new FormulaColumnConfig
                    {
                        Formula = "DATEDIFF({birth_date}, NOW())",
                        ReturnType = MorphDataType.Integer,
                        IsVolatile = true
                    }
                }
            ]
        });

        // Assert
        var formulaColumn = table.Columns.First(c => c.LogicalName == "age_days");
        formulaColumn.FormulaConfig.Should().NotBeNull();
        formulaColumn.FormulaConfig!.Formula.Should().Contain("NOW()");
        formulaColumn.FormulaConfig.IsVolatile.Should().BeTrue();
    }

    [Fact]
    public async Task CreateTableAsync_MixedDerivedColumns_ShouldHandleAllTypes()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

        // Create parent table with lookup, rollup, and formula columns
        var table = await _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            TenantId = tenantId,
            LogicalName = "mixed_derived_" + uniqueSuffix,
            Columns =
            [
                new CreateColumnRequest
                {
                    LogicalName = "name",
                    DataType = MorphDataType.Text,
                    IsNullable = false
                },
                new CreateColumnRequest
                {
                    LogicalName = "category_id",
                    DataType = MorphDataType.Uuid,
                    IsNullable = true
                },
                new CreateColumnRequest
                {
                    LogicalName = "category_name",
                    DataType = MorphDataType.Text,
                    IsNullable = true,
                    LookupConfig = new LookupColumnConfig
                    {
                        RelationColumn = "category_id",
                        TargetTable = "categories_" + uniqueSuffix,
                        TargetColumn = "name"
                    }
                },
                new CreateColumnRequest
                {
                    LogicalName = "total_sales",
                    DataType = MorphDataType.Decimal,
                    IsNullable = true,
                    RollupConfig = new RollupColumnConfig
                    {
                        Relation = "product_sales",
                        TargetTable = "sales_" + uniqueSuffix,
                        ForeignKeyColumn = "product_id",
                        SourceColumn = "amount",
                        Aggregation = RollupAggregation.Sum
                    }
                },
                new CreateColumnRequest
                {
                    LogicalName = "display_name",
                    DataType = MorphDataType.Text,
                    IsNullable = true,
                    FormulaConfig = new FormulaColumnConfig
                    {
                        Formula = "UPPER({name})",
                        ReturnType = MorphDataType.Text
                    }
                }
            ]
        });

        // Assert
        table.Should().NotBeNull();

        // Check lookup column
        var lookupColumn = table.Columns.First(c => c.LogicalName == "category_name");
        lookupColumn.IsDerived.Should().BeTrue();
        lookupColumn.LookupConfig.Should().NotBeNull();

        // Check rollup column
        var rollupColumn = table.Columns.First(c => c.LogicalName == "total_sales");
        rollupColumn.IsDerived.Should().BeTrue();
        rollupColumn.RollupConfig.Should().NotBeNull();

        // Check formula column
        var formulaColumn = table.Columns.First(c => c.LogicalName == "display_name");
        formulaColumn.IsDerived.Should().BeTrue();
        formulaColumn.FormulaConfig.Should().NotBeNull();

        // All derived columns should be virtual
        var derivedColumns = table.Columns.Where(c => c.IsDerived).ToList();
        derivedColumns.Should().HaveCount(3);
        foreach (var col in derivedColumns)
        {
            col.PhysicalName.Should().StartWith("virtual_");
            col.NativeType.Should().Be("virtual");
        }
    }
}
