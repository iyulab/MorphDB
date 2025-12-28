using System.Globalization;
using System.Text;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Infrastructure;

namespace MorphDB.Npgsql.Ddl;

/// <summary>
/// Builds DDL statements for PostgreSQL dynamic tables.
/// Supports schema-qualified names for multi-tenant isolation.
/// </summary>
public static class DdlBuilder
{
    #region Schema Operations

    /// <summary>
    /// Builds a CREATE SCHEMA statement.
    /// </summary>
    public static string BuildCreateSchema(string schemaName)
    {
        return $"CREATE SCHEMA IF NOT EXISTS {QuoteIdentifier(schemaName)}";
    }

    /// <summary>
    /// Builds a DROP SCHEMA statement.
    /// </summary>
    public static string BuildDropSchema(string schemaName, bool cascade = true)
    {
        var cascadeClause = cascade ? " CASCADE" : "";
        return $"DROP SCHEMA IF EXISTS {QuoteIdentifier(schemaName)}{cascadeClause}";
    }

    /// <summary>
    /// Builds an ALTER SCHEMA RENAME statement.
    /// </summary>
    public static string BuildRenameSchema(string oldName, string newName)
    {
        return $"ALTER SCHEMA {QuoteIdentifier(oldName)} RENAME TO {QuoteIdentifier(newName)}";
    }

    /// <summary>
    /// Builds a query to check if a schema exists.
    /// </summary>
    public static string BuildSchemaExistsQuery()
    {
        return "SELECT EXISTS(SELECT 1 FROM information_schema.schemata WHERE schema_name = @schemaName)";
    }

    /// <summary>
    /// Builds a query to get schema statistics.
    /// </summary>
    public static string BuildSchemaStatsQuery()
    {
        return """
            SELECT
                n.nspname AS schema_name,
                COUNT(DISTINCT c.relname) FILTER (WHERE c.relkind = 'r') AS table_count,
                COUNT(DISTINCT c.relname) FILTER (WHERE c.relkind = 'i') AS index_count,
                COALESCE(SUM(pg_total_relation_size(c.oid)) FILTER (WHERE c.relkind = 'r'), 0) AS total_size,
                COALESCE(SUM(pg_relation_size(c.oid)) FILTER (WHERE c.relkind = 'r'), 0) AS data_size,
                COALESCE(SUM(pg_indexes_size(c.oid)) FILTER (WHERE c.relkind = 'r'), 0) AS index_size
            FROM pg_namespace n
            LEFT JOIN pg_class c ON c.relnamespace = n.oid
            WHERE n.nspname = @schemaName
            GROUP BY n.nspname
            """;
    }

    #endregion

    #region Table Operations

    /// <summary>
    /// Builds a CREATE TABLE statement with optional schema qualification.
    /// </summary>
    public static string BuildCreateTable(
        string physicalName,
        IReadOnlyList<ColumnDefinition> columns,
        string? schema = null)
    {
        var sb = new StringBuilder();
        var qualifiedName = QualifyName(physicalName, schema);
        sb.AppendLine(CultureInfo.InvariantCulture, $"CREATE TABLE {qualifiedName} (");

        var columnDefs = new List<string>();
        var primaryKeyColumns = new List<string>();

        foreach (var col in columns)
        {
            var colDef = BuildColumnDefinition(col);
            columnDefs.Add($"    {colDef}");

            if (col.IsPrimaryKey)
            {
                primaryKeyColumns.Add(QuoteIdentifier(col.PhysicalName));
            }
        }

        sb.AppendLine(string.Join(",\n", columnDefs));

        if (primaryKeyColumns.Count > 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"    ,PRIMARY KEY ({string.Join(", ", primaryKeyColumns)})");
        }

        sb.Append(')');
        return sb.ToString();
    }

    /// <summary>
    /// Builds an ALTER TABLE ADD COLUMN statement with optional schema qualification.
    /// </summary>
    public static string BuildAddColumn(
        string tablePhysicalName,
        ColumnDefinition column,
        string? schema = null)
    {
        var colDef = BuildColumnDefinition(column);
        var qualifiedName = QualifyName(tablePhysicalName, schema);
        return $"ALTER TABLE {qualifiedName} ADD COLUMN {colDef}";
    }

    /// <summary>
    /// Builds an ALTER TABLE DROP COLUMN statement with optional schema qualification.
    /// </summary>
    public static string BuildDropColumn(
        string tablePhysicalName,
        string columnPhysicalName,
        string? schema = null)
    {
        var qualifiedName = QualifyName(tablePhysicalName, schema);
        return $"ALTER TABLE {qualifiedName} DROP COLUMN IF EXISTS {QuoteIdentifier(columnPhysicalName)}";
    }

    /// <summary>
    /// Builds a DROP TABLE statement with optional schema qualification.
    /// </summary>
    public static string BuildDropTable(string physicalName, string? schema = null)
    {
        var qualifiedName = QualifyName(physicalName, schema);
        return $"DROP TABLE IF EXISTS {qualifiedName}";
    }

    /// <summary>
    /// Builds a TRUNCATE TABLE statement with optional schema qualification.
    /// </summary>
    public static string BuildTruncateTable(
        string physicalName,
        string? schema = null,
        bool cascade = false)
    {
        var qualifiedName = QualifyName(physicalName, schema);
        var cascadeClause = cascade ? " CASCADE" : "";
        return $"TRUNCATE TABLE {qualifiedName}{cascadeClause}";
    }

    #endregion

    #region Index Operations

    /// <summary>
    /// Builds a CREATE INDEX statement with optional schema qualification.
    /// </summary>
    public static string BuildCreateIndex(IndexDefinition index, string? schema = null)
    {
        var sb = new StringBuilder("CREATE ");

        if (index.IsUnique)
        {
            sb.Append("UNIQUE ");
        }

        sb.Append("INDEX ");
        // Index names are not schema-qualified in PostgreSQL, they inherit from table
        sb.Append(QuoteIdentifier(index.PhysicalName));
        sb.Append(" ON ");
        sb.Append(QualifyName(index.TablePhysicalName, schema));

        // Add USING clause for non-btree indexes
        if (index.IndexType != IndexType.BTree)
        {
            sb.Append(CultureInfo.InvariantCulture, $" USING {index.IndexType.ToString().ToLowerInvariant()}");
        }

        sb.Append(" (");

        var columnSpecs = index.Columns.Select(c =>
        {
            var spec = QuoteIdentifier(c.PhysicalName);
            if (c.Direction == SortDirection.Descending)
            {
                spec += " DESC";
            }
            if (c.NullsPosition == NullsPosition.First)
            {
                spec += " NULLS FIRST";
            }
            return spec;
        });

        sb.Append(string.Join(", ", columnSpecs));
        sb.Append(')');

        if (!string.IsNullOrWhiteSpace(index.WhereClause))
        {
            sb.Append(CultureInfo.InvariantCulture, $" WHERE {index.WhereClause}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds a DROP INDEX statement with optional schema qualification.
    /// </summary>
    public static string BuildDropIndex(string indexPhysicalName, string? schema = null)
    {
        var qualifiedName = QualifyName(indexPhysicalName, schema);
        return $"DROP INDEX IF EXISTS {qualifiedName}";
    }

    #endregion

    #region Foreign Key Operations

    /// <summary>
    /// Builds an ALTER TABLE ADD CONSTRAINT for foreign key with optional schema qualification.
    /// </summary>
    public static string BuildAddForeignKey(ForeignKeyDefinition fk, string? schema = null)
    {
        var onDelete = MapReferentialAction(fk.OnDelete);
        var onUpdate = MapReferentialAction(fk.OnUpdate);
        var sourceTable = QualifyName(fk.SourceTablePhysicalName, schema);
        var targetTable = QualifyName(fk.TargetTablePhysicalName, fk.TargetSchema ?? schema);

        return $"""
            ALTER TABLE {sourceTable}
            ADD CONSTRAINT {QuoteIdentifier(fk.ConstraintName)}
            FOREIGN KEY ({QuoteIdentifier(fk.SourceColumnPhysicalName)})
            REFERENCES {targetTable} ({QuoteIdentifier(fk.TargetColumnPhysicalName)})
            ON DELETE {onDelete}
            ON UPDATE {onUpdate}
            """;
    }

    /// <summary>
    /// Builds an ALTER TABLE DROP CONSTRAINT statement with optional schema qualification.
    /// </summary>
    public static string BuildDropForeignKey(
        string tablePhysicalName,
        string constraintName,
        string? schema = null)
    {
        var qualifiedName = QualifyName(tablePhysicalName, schema);
        return $"ALTER TABLE {qualifiedName} DROP CONSTRAINT IF EXISTS {QuoteIdentifier(constraintName)}";
    }

    #endregion

    #region Column Modification Operations

    /// <summary>
    /// Builds an ALTER TABLE ALTER COLUMN SET NOT NULL statement.
    /// </summary>
    public static string BuildSetNotNull(
        string tablePhysicalName,
        string columnPhysicalName,
        string? schema = null)
    {
        var qualifiedName = QualifyName(tablePhysicalName, schema);
        return $"ALTER TABLE {qualifiedName} ALTER COLUMN {QuoteIdentifier(columnPhysicalName)} SET NOT NULL";
    }

    /// <summary>
    /// Builds an ALTER TABLE ALTER COLUMN DROP NOT NULL statement.
    /// </summary>
    public static string BuildDropNotNull(
        string tablePhysicalName,
        string columnPhysicalName,
        string? schema = null)
    {
        var qualifiedName = QualifyName(tablePhysicalName, schema);
        return $"ALTER TABLE {qualifiedName} ALTER COLUMN {QuoteIdentifier(columnPhysicalName)} DROP NOT NULL";
    }

    /// <summary>
    /// Builds an ALTER TABLE ALTER COLUMN SET DEFAULT statement.
    /// </summary>
    public static string BuildSetDefault(
        string tablePhysicalName,
        string columnPhysicalName,
        string defaultExpression,
        string? schema = null)
    {
        var qualifiedName = QualifyName(tablePhysicalName, schema);
        return $"ALTER TABLE {qualifiedName} ALTER COLUMN {QuoteIdentifier(columnPhysicalName)} SET DEFAULT {defaultExpression}";
    }

    /// <summary>
    /// Builds an ALTER TABLE ALTER COLUMN DROP DEFAULT statement.
    /// </summary>
    public static string BuildDropDefault(
        string tablePhysicalName,
        string columnPhysicalName,
        string? schema = null)
    {
        var qualifiedName = QualifyName(tablePhysicalName, schema);
        return $"ALTER TABLE {qualifiedName} ALTER COLUMN {QuoteIdentifier(columnPhysicalName)} DROP DEFAULT";
    }

    /// <summary>
    /// Builds an ALTER TABLE ADD CONSTRAINT UNIQUE statement.
    /// </summary>
    public static string BuildAddUniqueConstraint(
        string tablePhysicalName,
        string constraintName,
        string columnPhysicalName,
        string? schema = null)
    {
        var qualifiedName = QualifyName(tablePhysicalName, schema);
        return $"ALTER TABLE {qualifiedName} ADD CONSTRAINT {QuoteIdentifier(constraintName)} UNIQUE ({QuoteIdentifier(columnPhysicalName)})";
    }

    /// <summary>
    /// Builds an ALTER TABLE RENAME COLUMN statement.
    /// </summary>
    public static string BuildRenameColumn(
        string tablePhysicalName,
        string oldPhysicalName,
        string newPhysicalName,
        string? schema = null)
    {
        var qualifiedName = QualifyName(tablePhysicalName, schema);
        return $"ALTER TABLE {qualifiedName} RENAME COLUMN {QuoteIdentifier(oldPhysicalName)} TO {QuoteIdentifier(newPhysicalName)}";
    }

    /// <summary>
    /// Builds an ALTER TABLE RENAME TO statement.
    /// Note: This only changes the table name within the same schema.
    /// </summary>
    public static string BuildRenameTable(
        string oldPhysicalName,
        string newPhysicalName,
        string? schema = null)
    {
        var qualifiedName = QualifyName(oldPhysicalName, schema);
        return $"ALTER TABLE {qualifiedName} RENAME TO {QuoteIdentifier(newPhysicalName)}";
    }

    /// <summary>
    /// Builds an ALTER TABLE SET SCHEMA statement to move a table between schemas.
    /// </summary>
    public static string BuildMoveTableToSchema(
        string tableName,
        string sourceSchema,
        string targetSchema)
    {
        var qualifiedName = QualifyName(tableName, sourceSchema);
        return $"ALTER TABLE {qualifiedName} SET SCHEMA {QuoteIdentifier(targetSchema)}";
    }

    #endregion

    #region System Tables DDL

    /// <summary>
    /// Builds DDL for creating all system tables in a project's system schema.
    /// These tables store metadata about user-defined tables in the data schema.
    /// </summary>
    public static string BuildSystemTablesDdl(string systemSchema)
    {
        var sb = new StringBuilder();

        // _tables: Metadata about user-defined tables
        sb.Append(CultureInfo.InvariantCulture, $"""
            CREATE TABLE {QuoteIdentifier(systemSchema)}."_tables" (
                "table_id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                "logical_name" VARCHAR(255) NOT NULL,
                "physical_name" VARCHAR(63) NOT NULL,
                "schema_version" INTEGER NOT NULL DEFAULT 1,
                "descriptor" JSONB,
                "is_active" BOOLEAN NOT NULL DEFAULT true,
                "created_at" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                "updated_at" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                UNIQUE("logical_name") WHERE is_active = true
            );

            """);

        // _columns: Column definitions for user tables
        sb.Append(CultureInfo.InvariantCulture, $"""
            CREATE TABLE {QuoteIdentifier(systemSchema)}."_columns" (
                "column_id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                "table_id" UUID NOT NULL REFERENCES {QuoteIdentifier(systemSchema)}."_tables"("table_id") ON DELETE CASCADE,
                "logical_name" VARCHAR(255) NOT NULL,
                "physical_name" VARCHAR(63) NOT NULL,
                "data_type" VARCHAR(50) NOT NULL,
                "native_type" VARCHAR(100) NOT NULL,
                "is_nullable" BOOLEAN NOT NULL DEFAULT true,
                "is_unique" BOOLEAN NOT NULL DEFAULT false,
                "is_primary_key" BOOLEAN NOT NULL DEFAULT false,
                "is_indexed" BOOLEAN NOT NULL DEFAULT false,
                "default_value" TEXT,
                "check_expression" TEXT,
                "ordinal_position" INTEGER NOT NULL,
                "is_active" BOOLEAN NOT NULL DEFAULT true,
                "created_at" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                "updated_at" TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            """);

        // _indexes: Custom indexes on user tables
        sb.Append(CultureInfo.InvariantCulture, $"""
            CREATE TABLE {QuoteIdentifier(systemSchema)}."_indexes" (
                "index_id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                "table_id" UUID NOT NULL REFERENCES {QuoteIdentifier(systemSchema)}."_tables"("table_id") ON DELETE CASCADE,
                "logical_name" VARCHAR(255) NOT NULL,
                "physical_name" VARCHAR(63) NOT NULL,
                "columns" JSONB NOT NULL,
                "index_type" VARCHAR(20) NOT NULL DEFAULT 'btree',
                "is_unique" BOOLEAN NOT NULL DEFAULT false,
                "where_clause" TEXT,
                "is_active" BOOLEAN NOT NULL DEFAULT true,
                "created_at" TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            """);

        // _relations: Foreign key relationships
        sb.Append(CultureInfo.InvariantCulture, $"""
            CREATE TABLE {QuoteIdentifier(systemSchema)}."_relations" (
                "relation_id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                "logical_name" VARCHAR(255) NOT NULL,
                "source_table_id" UUID NOT NULL REFERENCES {QuoteIdentifier(systemSchema)}."_tables"("table_id") ON DELETE CASCADE,
                "source_column_id" UUID NOT NULL,
                "target_table_id" UUID NOT NULL REFERENCES {QuoteIdentifier(systemSchema)}."_tables"("table_id") ON DELETE CASCADE,
                "target_column_id" UUID NOT NULL,
                "relation_type" VARCHAR(20) NOT NULL,
                "on_delete" VARCHAR(20) NOT NULL DEFAULT 'NO ACTION',
                "is_active" BOOLEAN NOT NULL DEFAULT true,
                "created_at" TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            """);

        // _schema_changelog: Schema modification history
        sb.Append(CultureInfo.InvariantCulture, $"""
            CREATE TABLE {QuoteIdentifier(systemSchema)}."_schema_changelog" (
                "change_id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                "table_id" UUID,
                "operation" VARCHAR(50) NOT NULL,
                "schema_version" INTEGER,
                "changes" JSONB NOT NULL,
                "created_at" TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            """);

        // _migrations: Migration version tracking
        sb.Append(CultureInfo.InvariantCulture, $"""
            CREATE TABLE {QuoteIdentifier(systemSchema)}."_migrations" (
                "id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                "version" INTEGER NOT NULL UNIQUE,
                "name" VARCHAR(255) NOT NULL,
                "checksum" VARCHAR(64),
                "applied_at" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                "duration_ms" BIGINT NOT NULL DEFAULT 0,
                "is_rolled_back" BOOLEAN NOT NULL DEFAULT false,
                "rolled_back_at" TIMESTAMPTZ
            );

            """);

        // Indexes for performance
        sb.Append(CultureInfo.InvariantCulture, $"""
            CREATE INDEX "idx__columns_table_id" ON {QuoteIdentifier(systemSchema)}."_columns"("table_id");
            CREATE INDEX "idx__indexes_table_id" ON {QuoteIdentifier(systemSchema)}."_indexes"("table_id");
            CREATE INDEX "idx__relations_source" ON {QuoteIdentifier(systemSchema)}."_relations"("source_table_id");
            CREATE INDEX "idx__relations_target" ON {QuoteIdentifier(systemSchema)}."_relations"("target_table_id");
            CREATE INDEX "idx__schema_changelog_table" ON {QuoteIdentifier(systemSchema)}."_schema_changelog"("table_id");
            CREATE INDEX "idx__schema_changelog_created" ON {QuoteIdentifier(systemSchema)}."_schema_changelog"("created_at" DESC);
            """);

        return sb.ToString();
    }

    #endregion

    #region Private Helpers

    private static string BuildColumnDefinition(ColumnDefinition col)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{QuoteIdentifier(col.PhysicalName)} {col.NativeType}");

        if (!col.IsNullable && !col.IsPrimaryKey)
        {
            sb.Append(" NOT NULL");
        }

        if (col.DefaultExpression is not null)
        {
            sb.Append(CultureInfo.InvariantCulture, $" DEFAULT {col.DefaultExpression}");
        }

        if (col.CheckExpression is not null)
        {
            sb.Append(CultureInfo.InvariantCulture, $" CHECK ({col.CheckExpression})");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Quotes a PostgreSQL identifier (table, column, schema name).
    /// </summary>
    public static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }

    /// <summary>
    /// Creates a schema-qualified name if schema is provided.
    /// </summary>
    public static string QualifyName(string objectName, string? schema)
    {
        if (string.IsNullOrEmpty(schema))
        {
            return QuoteIdentifier(objectName);
        }
        return $"{QuoteIdentifier(schema)}.{QuoteIdentifier(objectName)}";
    }

    private static string MapReferentialAction(OnDeleteAction action) => action switch
    {
        OnDeleteAction.NoAction => "NO ACTION",
        OnDeleteAction.Cascade => "CASCADE",
        OnDeleteAction.SetNull => "SET NULL",
        OnDeleteAction.SetDefault => "SET DEFAULT",
        OnDeleteAction.Restrict => "RESTRICT",
        _ => "NO ACTION"
    };

    private static string MapReferentialAction(OnUpdateAction action) => action switch
    {
        OnUpdateAction.NoAction => "NO ACTION",
        OnUpdateAction.Cascade => "CASCADE",
        OnUpdateAction.SetNull => "SET NULL",
        OnUpdateAction.SetDefault => "SET DEFAULT",
        OnUpdateAction.Restrict => "RESTRICT",
        _ => "NO ACTION"
    };

    #endregion
}

/// <summary>
/// Column definition for DDL generation.
/// </summary>
public sealed record ColumnDefinition
{
    public required string PhysicalName { get; init; }
    public required string NativeType { get; init; }
    public bool IsNullable { get; init; } = true;
    public bool IsPrimaryKey { get; init; }
    public bool IsUnique { get; init; }
    public string? DefaultExpression { get; init; }
    public string? CheckExpression { get; init; }

    /// <summary>
    /// Creates a ColumnDefinition from column metadata.
    /// </summary>
    public static ColumnDefinition FromMetadata(ColumnMetadata metadata)
    {
        var defaultExpr = FormatDefaultExpression(metadata.DefaultValue, metadata.DataType);
        if (string.IsNullOrEmpty(defaultExpr))
        {
            defaultExpr = TypeMapper.GetDefaultExpression(metadata.DataType);
        }

        return new ColumnDefinition
        {
            PhysicalName = metadata.PhysicalName,
            NativeType = metadata.NativeType,
            IsNullable = metadata.IsNullable,
            IsPrimaryKey = metadata.IsPrimaryKey,
            IsUnique = metadata.IsUnique,
            DefaultExpression = defaultExpr,
            CheckExpression = metadata.CheckExpression
        };
    }

    /// <summary>
    /// Formats a default value as a valid PostgreSQL expression.
    /// </summary>
    private static string? FormatDefaultExpression(string? value, MorphDataType dataType)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        // Already a SQL expression (function call like now(), uuid_generate_v4())
        if (value.Contains('(') && value.Contains(')'))
            return value;

        // Numeric types - return as-is
        if (dataType is MorphDataType.Integer or MorphDataType.BigInteger or MorphDataType.Decimal)
        {
            return value;
        }

        // Boolean type
        if (dataType is MorphDataType.Boolean)
        {
            return value.ToLowerInvariant() switch
            {
                "true" or "1" => "true",
                "false" or "0" => "false",
                _ => value
            };
        }

        // String-based types - wrap in single quotes
        return $"'{value.Replace("'", "''")}'";
    }
}

/// <summary>
/// Index definition for DDL generation.
/// </summary>
public sealed record IndexDefinition
{
    public required string PhysicalName { get; init; }
    public required string TablePhysicalName { get; init; }
    public required IReadOnlyList<IndexColumnInfo> Columns { get; init; }
    public IndexType IndexType { get; init; } = IndexType.BTree;
    public bool IsUnique { get; init; }
    public string? WhereClause { get; init; }
}

/// <summary>
/// Foreign key definition for DDL generation.
/// </summary>
public sealed record ForeignKeyDefinition
{
    public required string ConstraintName { get; init; }
    public required string SourceTablePhysicalName { get; init; }
    public required string SourceColumnPhysicalName { get; init; }
    public required string TargetTablePhysicalName { get; init; }
    public required string TargetColumnPhysicalName { get; init; }
    /// <summary>
    /// Target table schema if different from source (for cross-schema references).
    /// </summary>
    public string? TargetSchema { get; init; }
    public OnDeleteAction OnDelete { get; init; } = OnDeleteAction.NoAction;
    public OnUpdateAction OnUpdate { get; init; } = OnUpdateAction.NoAction;
}
