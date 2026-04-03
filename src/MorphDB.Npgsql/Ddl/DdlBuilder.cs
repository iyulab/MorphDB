using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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
    /// Builds an ALTER TABLE ALTER COLUMN TYPE statement with USING cast.
    /// </summary>
    public static string BuildAlterColumnType(
        string tablePhysicalName,
        string columnPhysicalName,
        string newNativeType,
        string? schema = null)
    {
        var qualifiedName = QualifyName(tablePhysicalName, schema);
        var quotedColumn = QuoteIdentifier(columnPhysicalName);
        return $"ALTER TABLE {qualifiedName} ALTER COLUMN {quotedColumn} TYPE {newNativeType} USING {quotedColumn}::{newNativeType}";
    }

    /// <summary>
    /// Builds an ALTER TABLE DROP CONSTRAINT statement for removing a unique constraint.
    /// </summary>
    public static string BuildDropUniqueConstraint(
        string tablePhysicalName,
        string constraintName,
        string? schema = null)
    {
        var qualifiedName = QualifyName(tablePhysicalName, schema);
        return $"ALTER TABLE {qualifiedName} DROP CONSTRAINT IF EXISTS {QuoteIdentifier(constraintName)}";
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

    #region View Operations

    /// <summary>
    /// Builds a CREATE VIEW statement.
    /// </summary>
    public static string BuildCreateView(
        string physicalName,
        string selectStatement,
        string? schema = null)
    {
        var qualifiedName = QualifyName(physicalName, schema);
        return $"CREATE VIEW {qualifiedName} AS\n{selectStatement}";
    }

    /// <summary>
    /// Builds a CREATE OR REPLACE VIEW statement.
    /// </summary>
    public static string BuildCreateOrReplaceView(
        string physicalName,
        string selectStatement,
        string? schema = null)
    {
        var qualifiedName = QualifyName(physicalName, schema);
        return $"CREATE OR REPLACE VIEW {qualifiedName} AS\n{selectStatement}";
    }

    /// <summary>
    /// Builds a CREATE MATERIALIZED VIEW statement.
    /// </summary>
    public static string BuildCreateMaterializedView(
        string physicalName,
        string selectStatement,
        string? schema = null,
        bool withData = true)
    {
        var qualifiedName = QualifyName(physicalName, schema);
        var withDataClause = withData ? "WITH DATA" : "WITH NO DATA";
        return $"CREATE MATERIALIZED VIEW {qualifiedName} AS\n{selectStatement}\n{withDataClause}";
    }

    /// <summary>
    /// Builds a DROP VIEW statement.
    /// </summary>
    public static string BuildDropView(string physicalName, string? schema = null, bool cascade = false)
    {
        var qualifiedName = QualifyName(physicalName, schema);
        var cascadeClause = cascade ? " CASCADE" : "";
        return $"DROP VIEW IF EXISTS {qualifiedName}{cascadeClause}";
    }

    /// <summary>
    /// Builds a DROP MATERIALIZED VIEW statement.
    /// </summary>
    public static string BuildDropMaterializedView(string physicalName, string? schema = null, bool cascade = false)
    {
        var qualifiedName = QualifyName(physicalName, schema);
        var cascadeClause = cascade ? " CASCADE" : "";
        return $"DROP MATERIALIZED VIEW IF EXISTS {qualifiedName}{cascadeClause}";
    }

    /// <summary>
    /// Builds a REFRESH MATERIALIZED VIEW statement.
    /// </summary>
    public static string BuildRefreshMaterializedView(string physicalName, string? schema = null, bool concurrent = false)
    {
        var qualifiedName = QualifyName(physicalName, schema);
        var concurrentClause = concurrent ? "CONCURRENTLY " : "";
        return $"REFRESH MATERIALIZED VIEW {concurrentClause}{qualifiedName}";
    }

    /// <summary>
    /// Builds a CREATE UNIQUE INDEX statement for materialized view (required for CONCURRENTLY refresh).
    /// </summary>
    public static string BuildMaterializedViewUniqueIndex(
        string indexName,
        string viewPhysicalName,
        IReadOnlyList<string> columns,
        string? schema = null)
    {
        var qualifiedViewName = QualifyName(viewPhysicalName, schema);
        var columnList = string.Join(", ", columns.Select(QuoteIdentifier));
        return $"CREATE UNIQUE INDEX {QuoteIdentifier(indexName)} ON {qualifiedViewName} ({columnList})";
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
                "updated_at" TIMESTAMPTZ NOT NULL DEFAULT NOW()
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

        // _audit_logs: Audit trail for compliance and security
        sb.Append(CultureInfo.InvariantCulture, $"""
            CREATE TABLE {QuoteIdentifier(systemSchema)}."_audit_logs" (
                "id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                "category" SMALLINT NOT NULL,
                "action" VARCHAR(100) NOT NULL,
                "severity" SMALLINT NOT NULL DEFAULT 1,
                "actor_id" VARCHAR(100),
                "actor_type" VARCHAR(20),
                "resource_type" VARCHAR(50),
                "resource_id" VARCHAR(100),
                "http_method" VARCHAR(10),
                "request_path" VARCHAR(500),
                "status_code" INTEGER,
                "ip_address" VARCHAR(45),
                "user_agent" VARCHAR(500),
                "duration_ms" BIGINT,
                "metadata" JSONB,
                "error_message" TEXT,
                "timestamp" TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            """);

        // _views: View metadata
        sb.Append(CultureInfo.InvariantCulture, $"""
            CREATE TABLE {QuoteIdentifier(systemSchema)}."_views" (
                "view_id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                "tenant_id" UUID NOT NULL,
                "logical_name" VARCHAR(255) NOT NULL,
                "physical_name" VARCHAR(63) NOT NULL,
                "definition" JSONB NOT NULL,
                "is_materialized" BOOLEAN NOT NULL DEFAULT false,
                "refresh_policy" VARCHAR(20) NOT NULL DEFAULT 'OnDemand',
                "refresh_schedule" TEXT,
                "last_refreshed_at" TIMESTAMPTZ,
                "is_stale" BOOLEAN NOT NULL DEFAULT false,
                "descriptor" JSONB,
                "is_active" BOOLEAN NOT NULL DEFAULT true,
                "created_at" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                "updated_at" TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            """);

        // _view_columns: View column metadata
        sb.Append(CultureInfo.InvariantCulture, $"""
            CREATE TABLE {QuoteIdentifier(systemSchema)}."_view_columns" (
                "column_id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                "view_id" UUID NOT NULL REFERENCES {QuoteIdentifier(systemSchema)}."_views"("view_id") ON DELETE CASCADE,
                "logical_name" VARCHAR(255) NOT NULL,
                "data_type" VARCHAR(50) NOT NULL,
                "is_computed" BOOLEAN NOT NULL DEFAULT false,
                "expression" TEXT,
                "ordinal_position" INTEGER NOT NULL,
                "created_at" TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            """);

        // Indexes for performance
        sb.Append(CultureInfo.InvariantCulture, $"""
            CREATE UNIQUE INDEX "idx__tables_logical_name_active" ON {QuoteIdentifier(systemSchema)}."_tables"("logical_name") WHERE is_active = true;
            CREATE INDEX "idx__columns_table_id" ON {QuoteIdentifier(systemSchema)}."_columns"("table_id");
            CREATE INDEX "idx__indexes_table_id" ON {QuoteIdentifier(systemSchema)}."_indexes"("table_id");
            CREATE INDEX "idx__relations_source" ON {QuoteIdentifier(systemSchema)}."_relations"("source_table_id");
            CREATE INDEX "idx__relations_target" ON {QuoteIdentifier(systemSchema)}."_relations"("target_table_id");
            CREATE INDEX "idx__schema_changelog_table" ON {QuoteIdentifier(systemSchema)}."_schema_changelog"("table_id");
            CREATE INDEX "idx__schema_changelog_created" ON {QuoteIdentifier(systemSchema)}."_schema_changelog"("created_at" DESC);
            CREATE INDEX "idx__audit_logs_timestamp" ON {QuoteIdentifier(systemSchema)}."_audit_logs"("timestamp" DESC);
            CREATE INDEX "idx__audit_logs_category" ON {QuoteIdentifier(systemSchema)}."_audit_logs"("category", "timestamp" DESC);
            CREATE INDEX "idx__audit_logs_actor" ON {QuoteIdentifier(systemSchema)}."_audit_logs"("actor_id", "timestamp" DESC);
            CREATE UNIQUE INDEX "idx__views_logical_name_active" ON {QuoteIdentifier(systemSchema)}."_views"("tenant_id", "logical_name") WHERE is_active = true;
            CREATE INDEX "idx__view_columns_view_id" ON {QuoteIdentifier(systemSchema)}."_view_columns"("view_id");
            """);

        return sb.ToString();
    }

    /// <summary>
    /// Builds the DDL for the global morphdb schema and all system control-plane tables.
    /// Uses IF NOT EXISTS so it is safe to call on every startup.
    /// </summary>
    public static string BuildGlobalSystemSchemaDdl()
    {
        return """
            CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
            CREATE EXTENSION IF NOT EXISTS "pgcrypto";

            CREATE SCHEMA IF NOT EXISTS morphdb;

            CREATE TABLE IF NOT EXISTS morphdb._morph_tables (
                table_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
                tenant_id UUID NOT NULL,
                logical_name VARCHAR(255) NOT NULL,
                physical_name VARCHAR(63) NOT NULL UNIQUE,
                schema_version INTEGER NOT NULL DEFAULT 1,
                descriptor JSONB,
                is_active BOOLEAN NOT NULL DEFAULT true,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                UNIQUE (tenant_id, logical_name)
            );

            CREATE TABLE IF NOT EXISTS morphdb._morph_columns (
                column_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
                table_id UUID NOT NULL REFERENCES morphdb._morph_tables(table_id) ON DELETE CASCADE,
                logical_name VARCHAR(255) NOT NULL,
                physical_name VARCHAR(63) NOT NULL,
                data_type VARCHAR(50) NOT NULL,
                native_type VARCHAR(100) NOT NULL,
                is_nullable BOOLEAN NOT NULL DEFAULT true,
                is_unique BOOLEAN NOT NULL DEFAULT false,
                is_primary_key BOOLEAN NOT NULL DEFAULT false,
                is_indexed BOOLEAN NOT NULL DEFAULT false,
                is_encrypted BOOLEAN NOT NULL DEFAULT false,
                default_value TEXT,
                check_expr TEXT,
                ordinal_position INTEGER NOT NULL,
                descriptor JSONB,
                lookup_config JSONB,
                rollup_config JSONB,
                formula_config JSONB,
                computed_config JSONB,
                is_active BOOLEAN NOT NULL DEFAULT true,
                UNIQUE (table_id, logical_name),
                UNIQUE (table_id, physical_name)
            );

            CREATE TABLE IF NOT EXISTS morphdb._morph_relations (
                relation_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
                tenant_id UUID NOT NULL,
                logical_name VARCHAR(255) NOT NULL,
                source_table_id UUID NOT NULL REFERENCES morphdb._morph_tables(table_id),
                source_column_id UUID NOT NULL REFERENCES morphdb._morph_columns(column_id),
                target_table_id UUID NOT NULL REFERENCES morphdb._morph_tables(table_id),
                target_column_id UUID NOT NULL REFERENCES morphdb._morph_columns(column_id),
                relation_type VARCHAR(20) NOT NULL,
                on_delete VARCHAR(20) NOT NULL DEFAULT 'NO ACTION',
                on_update VARCHAR(20) NOT NULL DEFAULT 'NO ACTION',
                descriptor JSONB,
                is_active BOOLEAN NOT NULL DEFAULT true,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS morphdb._morph_indexes (
                index_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
                table_id UUID NOT NULL REFERENCES morphdb._morph_tables(table_id) ON DELETE CASCADE,
                logical_name VARCHAR(255) NOT NULL,
                physical_name VARCHAR(63) NOT NULL UNIQUE,
                columns JSONB NOT NULL,
                index_type VARCHAR(20) NOT NULL DEFAULT 'btree',
                is_unique BOOLEAN NOT NULL DEFAULT false,
                where_clause TEXT,
                descriptor JSONB,
                is_active BOOLEAN NOT NULL DEFAULT true,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS morphdb._morph_changelog (
                change_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
                table_id UUID NOT NULL,
                operation VARCHAR(50) NOT NULL,
                schema_version INTEGER NOT NULL,
                changes JSONB NOT NULL,
                performed_by VARCHAR(255),
                performed_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS morphdb._morph_api_keys (
                id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
                tenant_id UUID NOT NULL,
                key_type INTEGER NOT NULL DEFAULT 0,
                key_hash VARCHAR(255) NOT NULL,
                key_prefix VARCHAR(50) NOT NULL,
                name VARCHAR(100) NOT NULL,
                description TEXT,
                is_active BOOLEAN NOT NULL DEFAULT true,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                expires_at TIMESTAMPTZ,
                last_used_at TIMESTAMPTZ,
                UNIQUE (key_hash)
            );

            CREATE TABLE IF NOT EXISTS morphdb._morph_webhooks (
                webhook_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
                tenant_id UUID NOT NULL,
                table_id UUID NOT NULL REFERENCES morphdb._morph_tables(table_id) ON DELETE CASCADE,
                logical_name VARCHAR(255) NOT NULL,
                url VARCHAR(2048) NOT NULL,
                secret VARCHAR(64) NOT NULL,
                events VARCHAR(20)[] NOT NULL DEFAULT ARRAY['insert', 'update', 'delete'],
                filter JSONB,
                headers JSONB,
                is_active BOOLEAN NOT NULL DEFAULT true,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                UNIQUE (tenant_id, logical_name)
            );

            CREATE TABLE IF NOT EXISTS morphdb._morph_webhook_deliveries (
                delivery_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
                webhook_id UUID NOT NULL REFERENCES morphdb._morph_webhooks(webhook_id) ON DELETE CASCADE,
                record_id UUID,
                event VARCHAR(20) NOT NULL,
                payload JSONB NOT NULL,
                status VARCHAR(20) NOT NULL DEFAULT 'pending',
                attempt_count INTEGER NOT NULL DEFAULT 0,
                http_status_code INTEGER,
                response_body TEXT,
                error_message TEXT,
                next_retry_at TIMESTAMPTZ,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                delivered_at TIMESTAMPTZ
            );

            CREATE TABLE IF NOT EXISTS morphdb._morph_webhook_dlq (
                dlq_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
                delivery_id UUID NOT NULL,
                webhook_id UUID NOT NULL REFERENCES morphdb._morph_webhooks(webhook_id) ON DELETE CASCADE,
                tenant_id UUID NOT NULL,
                record_id UUID,
                event VARCHAR(20) NOT NULL,
                payload JSONB NOT NULL,
                reason VARCHAR(50) NOT NULL,
                attempt_count INTEGER NOT NULL DEFAULT 0,
                last_http_status_code INTEGER,
                last_error_message TEXT,
                status VARCHAR(20) NOT NULL DEFAULT 'pending_review',
                resolution_notes TEXT,
                dlq_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                resolved_at TIMESTAMPTZ,
                resolved_by UUID
            );

            CREATE TABLE IF NOT EXISTS morphdb._morph_import_jobs (
                job_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
                tenant_id UUID NOT NULL,
                table_id UUID NOT NULL REFERENCES morphdb._morph_tables(table_id) ON DELETE CASCADE,
                table_name VARCHAR(255) NOT NULL,
                format VARCHAR(20) NOT NULL,
                status VARCHAR(20) NOT NULL DEFAULT 'pending',
                total_rows BIGINT NOT NULL DEFAULT 0,
                processed_rows BIGINT NOT NULL DEFAULT 0,
                success_count BIGINT NOT NULL DEFAULT 0,
                error_count BIGINT NOT NULL DEFAULT 0,
                error_message TEXT,
                options JSONB,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                started_at TIMESTAMPTZ,
                completed_at TIMESTAMPTZ
            );

            CREATE TABLE IF NOT EXISTS morphdb._morph_export_jobs (
                job_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
                tenant_id UUID NOT NULL,
                table_id UUID NOT NULL REFERENCES morphdb._morph_tables(table_id) ON DELETE CASCADE,
                table_name VARCHAR(255) NOT NULL,
                format VARCHAR(20) NOT NULL,
                status VARCHAR(20) NOT NULL DEFAULT 'pending',
                total_rows BIGINT NOT NULL DEFAULT 0,
                processed_rows BIGINT NOT NULL DEFAULT 0,
                file_path VARCHAR(1024),
                file_size BIGINT,
                error_message TEXT,
                options JSONB,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                started_at TIMESTAMPTZ,
                completed_at TIMESTAMPTZ,
                expires_at TIMESTAMPTZ
            );

            CREATE TABLE IF NOT EXISTS morphdb._morph_import_data (
                job_id UUID PRIMARY KEY REFERENCES morphdb._morph_import_jobs(job_id) ON DELETE CASCADE,
                data BYTEA NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS morphdb._morph_export_data (
                job_id UUID PRIMARY KEY REFERENCES morphdb._morph_export_jobs(job_id) ON DELETE CASCADE,
                data BYTEA NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS morphdb._morph_organizations (
                org_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
                name VARCHAR(100) NOT NULL,
                slug VARCHAR(100) NOT NULL UNIQUE,
                owner_id UUID,
                settings JSONB,
                is_active BOOLEAN NOT NULL DEFAULT true,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS morphdb._morph_projects (
                project_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
                org_id UUID REFERENCES morphdb._morph_organizations(org_id) ON DELETE CASCADE,
                name VARCHAR(100) NOT NULL,
                slug VARCHAR(100) NOT NULL UNIQUE,
                system_schema VARCHAR(63) NOT NULL UNIQUE,
                data_schema VARCHAR(63) NOT NULL UNIQUE,
                settings JSONB,
                status INTEGER NOT NULL DEFAULT 0,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            -- Indexes (IF NOT EXISTS)
            CREATE INDEX IF NOT EXISTS idx_morph_tables_tenant ON morphdb._morph_tables(tenant_id);
            CREATE INDEX IF NOT EXISTS idx_morph_columns_table ON morphdb._morph_columns(table_id);
            CREATE INDEX IF NOT EXISTS idx_morph_relations_source ON morphdb._morph_relations(source_table_id);
            CREATE INDEX IF NOT EXISTS idx_morph_relations_target ON morphdb._morph_relations(target_table_id);
            CREATE INDEX IF NOT EXISTS idx_morph_changelog_table ON morphdb._morph_changelog(table_id);
            CREATE INDEX IF NOT EXISTS idx_morph_api_keys_tenant ON morphdb._morph_api_keys(tenant_id);
            CREATE INDEX IF NOT EXISTS idx_morph_api_keys_prefix ON morphdb._morph_api_keys(key_prefix);
            CREATE INDEX IF NOT EXISTS idx_morph_webhooks_tenant ON morphdb._morph_webhooks(tenant_id);
            CREATE INDEX IF NOT EXISTS idx_morph_webhooks_table ON morphdb._morph_webhooks(table_id);
            CREATE INDEX IF NOT EXISTS idx_morph_webhook_deliveries_webhook ON morphdb._morph_webhook_deliveries(webhook_id);
            CREATE INDEX IF NOT EXISTS idx_morph_webhook_deliveries_status ON morphdb._morph_webhook_deliveries(status) WHERE status IN ('pending', 'retrying');
            CREATE INDEX IF NOT EXISTS idx_morph_webhook_deliveries_next_retry ON morphdb._morph_webhook_deliveries(next_retry_at) WHERE next_retry_at IS NOT NULL;
            CREATE INDEX IF NOT EXISTS idx_morph_webhook_dlq_webhook_id ON morphdb._morph_webhook_dlq(webhook_id);
            CREATE INDEX IF NOT EXISTS idx_morph_webhook_dlq_tenant_id ON morphdb._morph_webhook_dlq(tenant_id);
            CREATE INDEX IF NOT EXISTS idx_morph_webhook_dlq_status ON morphdb._morph_webhook_dlq(status);
            CREATE INDEX IF NOT EXISTS idx_morph_webhook_dlq_dlq_at ON morphdb._morph_webhook_dlq(dlq_at);
            CREATE INDEX IF NOT EXISTS idx_morph_import_jobs_tenant ON morphdb._morph_import_jobs(tenant_id);
            CREATE INDEX IF NOT EXISTS idx_morph_import_jobs_status ON morphdb._morph_import_jobs(status) WHERE status IN ('pending', 'processing');
            CREATE INDEX IF NOT EXISTS idx_morph_export_jobs_tenant ON morphdb._morph_export_jobs(tenant_id);
            CREATE INDEX IF NOT EXISTS idx_morph_export_jobs_status ON morphdb._morph_export_jobs(status) WHERE status IN ('pending', 'processing');
            CREATE INDEX IF NOT EXISTS idx_morph_export_jobs_expires ON morphdb._morph_export_jobs(expires_at) WHERE expires_at IS NOT NULL;
            CREATE INDEX IF NOT EXISTS idx_morph_projects_org ON morphdb._morph_projects(org_id);
            CREATE INDEX IF NOT EXISTS idx_morph_projects_status ON morphdb._morph_projects(status);
            CREATE INDEX IF NOT EXISTS idx_morph_projects_slug ON morphdb._morph_projects(slug);
            CREATE INDEX IF NOT EXISTS idx_morph_organizations_slug ON morphdb._morph_organizations(slug);

            -- Functions
            CREATE OR REPLACE FUNCTION morphdb.notify_schema_change()
            RETURNS TRIGGER AS $$
            BEGIN
                PERFORM pg_notify('morphdb_schema', json_build_object(
                    'table_id', COALESCE(NEW.table_id, OLD.table_id),
                    'operation', TG_OP,
                    'schema_version', COALESCE(NEW.schema_version, OLD.schema_version)
                )::text);
                RETURN COALESCE(NEW, OLD);
            END;
            $$ LANGUAGE plpgsql;

            DROP TRIGGER IF EXISTS trg_schema_change ON morphdb._morph_tables;
            CREATE TRIGGER trg_schema_change
                AFTER INSERT OR UPDATE OR DELETE ON morphdb._morph_tables
                FOR EACH ROW
                EXECUTE FUNCTION morphdb.notify_schema_change();

            CREATE OR REPLACE FUNCTION morphdb.update_updated_at()
            RETURNS TRIGGER AS $$
            BEGIN
                NEW.updated_at = NOW();
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            DROP TRIGGER IF EXISTS trg_update_timestamp ON morphdb._morph_tables;
            CREATE TRIGGER trg_update_timestamp
                BEFORE UPDATE ON morphdb._morph_tables
                FOR EACH ROW
                EXECUTE FUNCTION morphdb.update_updated_at();
            """;
    }

    #endregion

    #region Check Expression Translation

    /// <summary>
    /// Translates logical column names in a CHECK expression to their quoted physical names.
    /// Uses word-boundary matching to avoid partial replacements.
    /// Also translates the MATCHES operator to PostgreSQL's native ~ (regex match) operator.
    /// </summary>
    public static string? TranslateCheckExpression(
        string? checkExpression,
        IReadOnlyDictionary<string, string> logicalToPhysicalMap)
    {
        if (string.IsNullOrEmpty(checkExpression))
            return checkExpression;

        var result = checkExpression;

        // Sort by length descending to replace longer names first (e.g., "unit_price" before "price")
        foreach (var (logicalName, physicalName) in logicalToPhysicalMap.OrderByDescending(kv => kv.Key.Length))
        {
            var pattern = $@"\b{Regex.Escape(logicalName)}\b";
            result = Regex.Replace(result, pattern, QuoteIdentifier(physicalName));
        }

        // Translate MATCHES operator to PostgreSQL's native ~ (regex match) operator
        result = Regex.Replace(result, @"\bMATCHES\b", "~", RegexOptions.IgnoreCase);

        return result;
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
