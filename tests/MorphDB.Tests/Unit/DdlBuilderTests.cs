using MorphDB.Core.Models;
using MorphDB.Npgsql.Ddl;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Unit tests for DdlBuilder.
/// </summary>
public class DdlBuilderTests
{
    [Fact]
    public void BuildCreateTable_ShouldGenerateValidSql()
    {
        // Arrange
        var columns = new List<ColumnDefinition>
        {
            new()
            {
                PhysicalName = "id",
                NativeType = "UUID",
                IsNullable = false,
                IsPrimaryKey = true
            },
            new()
            {
                PhysicalName = "name",
                NativeType = "TEXT",
                IsNullable = false,
                IsPrimaryKey = false
            },
            new()
            {
                PhysicalName = "created_at",
                NativeType = "TIMESTAMPTZ",
                IsNullable = false,
                IsPrimaryKey = false,
                DefaultExpression = "NOW()"
            }
        };

        // Act
        var sql = DdlBuilder.BuildCreateTable("t_customers", columns);

        // Assert
        sql.Should().Contain("CREATE TABLE \"t_customers\"");
        sql.Should().Contain("\"id\" UUID");
        sql.Should().Contain("\"name\" TEXT NOT NULL");
        sql.Should().Contain("\"created_at\" TIMESTAMPTZ NOT NULL DEFAULT NOW()");
        sql.Should().Contain("PRIMARY KEY (\"id\")");
    }

    [Fact]
    public void BuildCreateTable_WithCompositePrimaryKey_ShouldGenerateValidSql()
    {
        // Arrange
        var columns = new List<ColumnDefinition>
        {
            new()
            {
                PhysicalName = "tenant_id",
                NativeType = "UUID",
                IsNullable = false,
                IsPrimaryKey = true
            },
            new()
            {
                PhysicalName = "user_id",
                NativeType = "UUID",
                IsNullable = false,
                IsPrimaryKey = true
            },
            new()
            {
                PhysicalName = "role",
                NativeType = "TEXT",
                IsNullable = false,
                IsPrimaryKey = false
            }
        };

        // Act
        var sql = DdlBuilder.BuildCreateTable("t_tenant_users", columns);

        // Assert
        sql.Should().Contain("PRIMARY KEY (\"tenant_id\", \"user_id\")");
    }

    [Fact]
    public void BuildCreateTable_WithCheckConstraint_ShouldGenerateValidSql()
    {
        // Arrange
        var columns = new List<ColumnDefinition>
        {
            new()
            {
                PhysicalName = "age",
                NativeType = "INTEGER",
                IsNullable = false,
                CheckExpression = "age >= 0 AND age <= 150"
            }
        };

        // Act
        var sql = DdlBuilder.BuildCreateTable("t_people", columns);

        // Assert
        sql.Should().Contain("CHECK (age >= 0 AND age <= 150)");
    }

    [Fact]
    public void BuildAddColumn_ShouldGenerateValidSql()
    {
        // Arrange
        var column = new ColumnDefinition
        {
            PhysicalName = "email",
            NativeType = "TEXT",
            IsNullable = false,
            DefaultExpression = "''"
        };

        // Act
        var sql = DdlBuilder.BuildAddColumn("t_users", column);

        // Assert
        sql.Should().Be("ALTER TABLE \"t_users\" ADD COLUMN \"email\" TEXT NOT NULL DEFAULT ''");
    }

    [Fact]
    public void BuildDropColumn_ShouldGenerateValidSql()
    {
        // Act
        var sql = DdlBuilder.BuildDropColumn("t_users", "c_obsolete");

        // Assert
        sql.Should().Be("ALTER TABLE \"t_users\" DROP COLUMN IF EXISTS \"c_obsolete\"");
    }

    [Fact]
    public void BuildDropTable_ShouldGenerateValidSql()
    {
        // Act
        var sql = DdlBuilder.BuildDropTable("t_old_table");

        // Assert
        sql.Should().Be("DROP TABLE IF EXISTS \"t_old_table\"");
    }

    [Fact]
    public void BuildCreateIndex_BTree_ShouldGenerateValidSql()
    {
        // Arrange
        var index = new IndexDefinition
        {
            PhysicalName = "idx_users_email",
            TablePhysicalName = "t_users",
            Columns =
            [
                new IndexColumnInfo
                {
                    ColumnId = Guid.NewGuid(),
                    PhysicalName = "email",
                    Direction = SortDirection.Ascending,
                    NullsPosition = NullsPosition.Last
                }
            ],
            IndexType = IndexType.BTree,
            IsUnique = true
        };

        // Act
        var sql = DdlBuilder.BuildCreateIndex(index);

        // Assert
        sql.Should().Contain("CREATE UNIQUE INDEX");
        sql.Should().Contain("\"idx_users_email\"");
        sql.Should().Contain("ON \"t_users\"");
        sql.Should().Contain("(\"email\")");
        sql.Should().NotContain("USING"); // BTree is default
    }

    [Fact]
    public void BuildCreateIndex_Hash_ShouldGenerateValidSql()
    {
        // Arrange
        var index = new IndexDefinition
        {
            PhysicalName = "idx_users_id_hash",
            TablePhysicalName = "t_users",
            Columns =
            [
                new IndexColumnInfo
                {
                    ColumnId = Guid.NewGuid(),
                    PhysicalName = "id",
                    Direction = SortDirection.Ascending,
                    NullsPosition = NullsPosition.Last
                }
            ],
            IndexType = IndexType.Hash,
            IsUnique = false
        };

        // Act
        var sql = DdlBuilder.BuildCreateIndex(index);

        // Assert
        sql.Should().Contain("CREATE INDEX");
        sql.Should().Contain("USING hash");
    }

    [Fact]
    public void BuildCreateIndex_WithDescAndNullsFirst_ShouldGenerateValidSql()
    {
        // Arrange
        var index = new IndexDefinition
        {
            PhysicalName = "idx_orders_date",
            TablePhysicalName = "t_orders",
            Columns =
            [
                new IndexColumnInfo
                {
                    ColumnId = Guid.NewGuid(),
                    PhysicalName = "order_date",
                    Direction = SortDirection.Descending,
                    NullsPosition = NullsPosition.First
                }
            ],
            IndexType = IndexType.BTree,
            IsUnique = false
        };

        // Act
        var sql = DdlBuilder.BuildCreateIndex(index);

        // Assert
        sql.Should().Contain("\"order_date\" DESC NULLS FIRST");
    }

    [Fact]
    public void BuildCreateIndex_WithWhereClause_ShouldGenerateValidSql()
    {
        // Arrange
        var index = new IndexDefinition
        {
            PhysicalName = "idx_active_users",
            TablePhysicalName = "t_users",
            Columns =
            [
                new IndexColumnInfo
                {
                    ColumnId = Guid.NewGuid(),
                    PhysicalName = "email",
                    Direction = SortDirection.Ascending,
                    NullsPosition = NullsPosition.Last
                }
            ],
            IndexType = IndexType.BTree,
            IsUnique = false,
            WhereClause = "is_active = true"
        };

        // Act
        var sql = DdlBuilder.BuildCreateIndex(index);

        // Assert
        sql.Should().Contain("WHERE is_active = true");
    }

    [Fact]
    public void BuildDropIndex_ShouldGenerateValidSql()
    {
        // Act
        var sql = DdlBuilder.BuildDropIndex("idx_old");

        // Assert
        sql.Should().Be("DROP INDEX IF EXISTS \"idx_old\"");
    }

    [Fact]
    public void BuildAddForeignKey_ShouldGenerateValidSql()
    {
        // Arrange
        var fk = new ForeignKeyDefinition
        {
            ConstraintName = "fk_orders_customer",
            SourceTablePhysicalName = "t_orders",
            SourceColumnPhysicalName = "customer_id",
            TargetTablePhysicalName = "t_customers",
            TargetColumnPhysicalName = "id",
            OnDelete = OnDeleteAction.Cascade,
            OnUpdate = OnUpdateAction.NoAction
        };

        // Act
        var sql = DdlBuilder.BuildAddForeignKey(fk);

        // Assert
        sql.Should().Contain("ALTER TABLE \"t_orders\"");
        sql.Should().Contain("ADD CONSTRAINT \"fk_orders_customer\"");
        sql.Should().Contain("FOREIGN KEY (\"customer_id\")");
        sql.Should().Contain("REFERENCES \"t_customers\" (\"id\")");
        sql.Should().Contain("ON DELETE CASCADE");
        sql.Should().Contain("ON UPDATE NO ACTION");
    }

    [Fact]
    public void BuildDropForeignKey_ShouldGenerateValidSql()
    {
        // Act
        var sql = DdlBuilder.BuildDropForeignKey("t_orders", "fk_orders_customer");

        // Assert
        sql.Should().Be("ALTER TABLE \"t_orders\" DROP CONSTRAINT IF EXISTS \"fk_orders_customer\"");
    }

    [Fact]
    public void BuildSetNotNull_ShouldGenerateValidSql()
    {
        // Act
        var sql = DdlBuilder.BuildSetNotNull("t_users", "email");

        // Assert
        sql.Should().Be("ALTER TABLE \"t_users\" ALTER COLUMN \"email\" SET NOT NULL");
    }

    [Fact]
    public void BuildDropNotNull_ShouldGenerateValidSql()
    {
        // Act
        var sql = DdlBuilder.BuildDropNotNull("t_users", "phone");

        // Assert
        sql.Should().Be("ALTER TABLE \"t_users\" ALTER COLUMN \"phone\" DROP NOT NULL");
    }

    [Fact]
    public void BuildSetDefault_ShouldGenerateValidSql()
    {
        // Act
        var sql = DdlBuilder.BuildSetDefault("t_users", "status", "'active'");

        // Assert
        sql.Should().Be("ALTER TABLE \"t_users\" ALTER COLUMN \"status\" SET DEFAULT 'active'");
    }

    [Fact]
    public void BuildDropDefault_ShouldGenerateValidSql()
    {
        // Act
        var sql = DdlBuilder.BuildDropDefault("t_users", "status");

        // Assert
        sql.Should().Be("ALTER TABLE \"t_users\" ALTER COLUMN \"status\" DROP DEFAULT");
    }

    [Fact]
    public void BuildAddUniqueConstraint_ShouldGenerateValidSql()
    {
        // Act
        var sql = DdlBuilder.BuildAddUniqueConstraint("t_users", "uq_email", "email");

        // Assert
        sql.Should().Be("ALTER TABLE \"t_users\" ADD CONSTRAINT \"uq_email\" UNIQUE (\"email\")");
    }

    [Fact]
    public void BuildRenameColumn_ShouldGenerateValidSql()
    {
        // Act
        var sql = DdlBuilder.BuildRenameColumn("t_users", "old_name", "new_name");

        // Assert
        sql.Should().Be("ALTER TABLE \"t_users\" RENAME COLUMN \"old_name\" TO \"new_name\"");
    }

    [Fact]
    public void BuildRenameTable_ShouldGenerateValidSql()
    {
        // Act
        var sql = DdlBuilder.BuildRenameTable("old_table", "new_table");

        // Assert
        sql.Should().Be("ALTER TABLE \"old_table\" RENAME TO \"new_table\"");
    }

    [Fact]
    public void QuoteIdentifier_ShouldEscapeDoubleQuotes()
    {
        // Arrange - table name with double quote
        var columns = new List<ColumnDefinition>
        {
            new()
            {
                PhysicalName = "col\"name",
                NativeType = "TEXT",
                IsNullable = true
            }
        };

        // Act
        var sql = DdlBuilder.BuildCreateTable("table\"name", columns);

        // Assert
        sql.Should().Contain("\"table\"\"name\"");
        sql.Should().Contain("\"col\"\"name\"");
    }
}
