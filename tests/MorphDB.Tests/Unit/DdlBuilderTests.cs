using MorphDB.Core.Exceptions;
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
                PhysicalName = "project_id",
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
        var sql = DdlBuilder.BuildCreateTable("t_project_users", columns);

        // Assert
        sql.Should().Contain("PRIMARY KEY (\"project_id\", \"user_id\")");
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

        // Assert - CHECK is a virtual constraint: the expression must never reach DDL. The
        // app-layer evaluator (CheckGrammar/CheckValidator) is its only enforcement.
        sql.Should().NotContain("CHECK");
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
                    LogicalName = "email",
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
                    LogicalName = "id",
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
                    LogicalName = "order_date",
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
                    LogicalName = "email",
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






    [Fact]
    public void BuildCreateTable_WithCheckMetadata_EmitsNoCheck()
    {
        // CHECK is virtual: even a column definition carrying an expression must leave DDL clean.
        var columns = new List<ColumnDefinition>
        {
            new()
            {
                PhysicalName = "col_a1b2c3d4e5f6",
                NativeType = "INTEGER",
                IsNullable = false,
                CheckExpression = "\"col_a1b2c3d4e5f6\" >= 0"
            }
        };

        // Act
        var sql = DdlBuilder.BuildCreateTable("t_test", columns);

        // Assert
        sql.Should().NotContain("CHECK");
    }

    #region ALTER COLUMN TYPE Tests

    [Fact]
    public void BuildAlterColumnType_ShouldGenerateValidSql()
    {
        // Act
        var sql = DdlBuilder.BuildAlterColumnType("tbl_abc", "col_def", "bigint");

        // Assert
        sql.Should().Be("ALTER TABLE \"tbl_abc\" ALTER COLUMN \"col_def\" TYPE bigint USING \"col_def\"::bigint");
    }

    [Fact]
    public void BuildAlterColumnType_WithSchema_ShouldQualifyTableName()
    {
        // Act
        var sql = DdlBuilder.BuildAlterColumnType("tbl_abc", "col_def", "text", "project_schema");

        // Assert
        sql.Should().Be("ALTER TABLE \"project_schema\".\"tbl_abc\" ALTER COLUMN \"col_def\" TYPE text USING \"col_def\"::text");
    }

    [Fact]
    public void BuildDropUniqueConstraint_ShouldGenerateValidSql()
    {
        // Act
        var sql = DdlBuilder.BuildDropUniqueConstraint("tbl_abc", "uq_tbl_abc_col_def");

        // Assert
        sql.Should().Be("ALTER TABLE \"tbl_abc\" DROP CONSTRAINT IF EXISTS \"uq_tbl_abc_col_def\"");
    }

    #endregion

    #region Default value safety

    private static ColumnMetadata ColumnWithDefault(string defaultValue) => new()
    {
        LogicalName = "col",
        PhysicalName = "col",
        DataType = MorphDataType.Text,
        NativeType = "TEXT",
        DefaultValue = defaultValue
    };

    [Theory]
    [InlineData("gen_random_uuid()", "gen_random_uuid()")]
    [InlineData("NOW()", "now()")]
    [InlineData("  now()  ", "now()")]
    [InlineData("clock_timestamp()", "clock_timestamp()")]
    [InlineData("statement_timestamp()", "statement_timestamp()")]
    [InlineData("transaction_timestamp()", "transaction_timestamp()")]
    public void FromMetadata_ShouldEmitSupportedFunctionDefaultsUnquoted(string declared, string expected)
    {
        // Act
        var definition = ColumnDefinition.FromMetadata(ColumnWithDefault(declared));

        // Assert
        definition.DefaultExpression.Should().Be(expected);
    }

    [Theory]
    // Closing the DEFAULT expression escapes into arbitrary DDL, executed by a role privileged
    // enough to create extensions — a project user must never reach it.
    [InlineData("'x'), extra TEXT DEFAULT ('y")]
    [InlineData("(SELECT current_setting('is_superuser'))")]
    // Needs the uuid-ossp extension, which managed PostgreSQL does not grant.
    [InlineData("uuid_generate_v4()")]
    public void FromMetadata_ShouldRejectUnsupportedFunctionDefaults(string declared)
    {
        // Act
        var act = () => ColumnDefinition.FromMetadata(ColumnWithDefault(declared));

        // Assert
        act.Should().Throw<SchemaException>()
            .Which.ErrorCode.Should().Be("INVALID_DEFAULT");
    }

    [Fact]
    public void FromMetadata_ShouldQuoteLiteralDefaults()
    {
        // Act
        var definition = ColumnDefinition.FromMetadata(ColumnWithDefault("O'Brien"));

        // Assert — quoting, not rejection: a literal apostrophe is ordinary data.
        definition.DefaultExpression.Should().Be("'O''Brien'");
    }

    private static ColumnMetadata TemporalColumnWithDefault(string defaultValue, MorphDataType type) => new()
    {
        LogicalName = "col",
        PhysicalName = "col",
        DataType = type,
        NativeType = "TIMESTAMPTZ",
        DefaultValue = defaultValue
    };

    /// <summary>
    /// SQL's clock keywords take no parentheses, so they used to fall through to the literal path
    /// and come out quoted — <c>DEFAULT 'CURRENT_TIMESTAMP'</c> — which no temporal column can cast,
    /// so the CREATE TABLE failed at execution time.
    /// </summary>
    [Theory]
    [InlineData("CURRENT_TIMESTAMP", MorphDataType.DateTime, "CURRENT_TIMESTAMP")]
    [InlineData("current_timestamp", MorphDataType.DateTime, "CURRENT_TIMESTAMP")]
    [InlineData("  CURRENT_TIMESTAMP  ", MorphDataType.DateTime, "CURRENT_TIMESTAMP")]
    [InlineData("CURRENT_DATE", MorphDataType.Date, "CURRENT_DATE")]
    [InlineData("CURRENT_TIME", MorphDataType.Time, "CURRENT_TIME")]
    [InlineData("LOCALTIMESTAMP", MorphDataType.DateTime, "LOCALTIMESTAMP")]
    public void FromMetadata_ShouldEmitClockKeywordDefaultsUnquotedOnTemporalColumns(
        string declared, MorphDataType type, string expected)
    {
        // Act
        var definition = ColumnDefinition.FromMetadata(TemporalColumnWithDefault(declared, type));

        // Assert
        definition.DefaultExpression.Should().Be(expected);
    }

    /// <summary>
    /// On a text column the same word is ordinary data. The keyword handling is scoped to temporal
    /// columns precisely so that no literal meaning is lost.
    /// </summary>
    [Fact]
    public void FromMetadata_ShouldKeepClockKeywordsAsLiteralsOnTextColumns()
    {
        // Act
        var definition = ColumnDefinition.FromMetadata(ColumnWithDefault("CURRENT_TIMESTAMP"));

        // Assert
        definition.DefaultExpression.Should().Be("'CURRENT_TIMESTAMP'");
    }

    #endregion

    #region Check expression safety

    private static List<ColumnDefinition> ColumnWithCheck(string check) =>
    [
        new()
        {
            PhysicalName = "age",
            NativeType = "INTEGER",
            CheckExpression = check
        }
    ];

    [Theory]
    [InlineData("age >= 0 AND age <= 150")]
    [InlineData("(age > 1 OR age < 200)")]
    [InlineData("\"c_age\" >= 0")]
    [InlineData("status = 'a)b'")]
    [InlineData("name ~ '^[a-z]+$'")]
    public void BuildCreateTable_NeverEmitsCheck_WhateverTheExpression(string check)
    {
        // Act
        var sql = DdlBuilder.BuildCreateTable("t_people", ColumnWithCheck(check));

        // Assert - virtual constraint: enforcement lives in the app evaluator, not in DDL.
        sql.Should().NotContain("CHECK");
    }

    [Theory]
    // Verified against a real PostgreSQL before this guard existed: this payload created a second
    // column named "extra" on the table, i.e. it escaped CHECK (...) into arbitrary DDL.
    [InlineData("1=1), extra TEXT DEFAULT ('injected'")]
    [InlineData("age > 0; DROP TABLE t_people")]
    [InlineData("age > 0 -- ")]
    [InlineData("age > 'unterminated")]
    [InlineData("(age > 0")]
    public void An_escaping_check_expression_is_outside_the_grammar(string check)
    {
        // CHECK no longer reaches DDL, so escape is impossible by construction; the declaration
        // gate refuses these earlier because the evaluator cannot enforce them.
        MorphDB.Npgsql.Infrastructure.CheckGrammar.IsSupported(check).Should().BeFalse();
    }

    [Fact]
    public void BuildCreateIndex_ShouldRejectEscapingPredicate()
    {
        // Arrange — an index predicate sits at the end of its statement, so a separator is enough.
        var index = new IndexDefinition
        {
            PhysicalName = "idx_users_email",
            TablePhysicalName = "t_users",
            Columns =
            [
                new IndexColumnInfo
                {
                    ColumnId = Guid.NewGuid(),
                    LogicalName = "email",
                    PhysicalName = "email",
                    Direction = SortDirection.Ascending,
                    NullsPosition = NullsPosition.Last
                }
            ],
            IndexType = IndexType.BTree,
            WhereClause = "1=1; DROP TABLE t_users"
        };

        // Act
        var act = () => DdlBuilder.BuildCreateIndex(index);

        // Assert
        act.Should().Throw<SchemaException>()
            .Which.ErrorCode.Should().Be("INVALID_EXPRESSION");
    }

    #endregion

    #region Extension independence

    [Fact]
    public void BuildGlobalSystemSchemaDdl_ShouldNotRequireAnyExtension()
    {
        // Act
        var sql = DdlBuilder.BuildGlobalSystemSchemaDdl();

        // Assert — managed PostgreSQL gates CREATE EXTENSION behind a server-parameter allow-list,
        // so a bootstrap that creates one cannot start there. ExtensionFreeBootstrapTests proves
        // this against a real database; this guard fails fast even where Docker is unavailable.
        sql.Should().NotContain("CREATE EXTENSION");
        sql.Should().NotContain("uuid_generate_v4", "that function needs uuid-ossp; gen_random_uuid() is built in since PostgreSQL 13");
        sql.Should().Contain("gen_random_uuid()");
    }

    #endregion
}
