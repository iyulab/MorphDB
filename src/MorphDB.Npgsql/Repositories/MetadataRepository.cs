using System.Text.Json;
using Dapper;
using MorphDB.Core.Models;
using Npgsql;

namespace MorphDB.Npgsql.Repositories;

/// <summary>
/// Dapper-based implementation of metadata repository.
/// </summary>
public sealed class MetadataRepository : IMetadataRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public MetadataRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    #region Table Operations

    public async Task<TableMetadata> InsertTableAsync(
        TableMetadata table,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO morphdb._morph_tables
                (table_id, tenant_id, logical_name, physical_name, schema_version, descriptor)
            VALUES
                (@TableId, @TenantId, @LogicalName, @PhysicalName, @SchemaVersion, @Descriptor::jsonb)
            RETURNING table_id, tenant_id, logical_name, physical_name, schema_version,
                      descriptor, is_active, created_at, updated_at
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await connection.QuerySingleAsync<TableRow>(sql, new
        {
            table.TableId,
            table.TenantId,
            table.LogicalName,
            table.PhysicalName,
            table.SchemaVersion,
            Descriptor = table.Descriptor?.RootElement.GetRawText()
        });

        return MapToTableMetadata(result);
    }

    public async Task<TableMetadata?> GetTableByIdAsync(
        Guid tableId,
        bool includeColumns = false,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT table_id, tenant_id, logical_name, physical_name, schema_version,
                   descriptor, is_active, created_at, updated_at
            FROM morphdb._morph_tables
            WHERE table_id = @TableId AND is_active = true
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<TableRow>(sql, new { TableId = tableId });

        if (row is null) return null;

        var table = MapToTableMetadata(row);

        if (includeColumns)
        {
            var columns = await GetColumnsByTableIdAsync(tableId, cancellationToken);
            table = WithColumns(table, columns);
        }

        return table;
    }

    public async Task<TableMetadata?> GetTableByNameAsync(
        Guid tenantId,
        string logicalName,
        bool includeColumns = false,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT table_id, tenant_id, logical_name, physical_name, schema_version,
                   descriptor, is_active, created_at, updated_at
            FROM morphdb._morph_tables
            WHERE tenant_id = @TenantId AND logical_name = @LogicalName AND is_active = true
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<TableRow>(
            sql, new { TenantId = tenantId, LogicalName = logicalName });

        if (row is null) return null;

        var table = MapToTableMetadata(row);

        if (includeColumns)
        {
            var columns = await GetColumnsByTableIdAsync(table.TableId, cancellationToken);
            table = WithColumns(table, columns);
        }

        return table;
    }

    public async Task<IReadOnlyList<TableMetadata>> ListTablesAsync(
        Guid tenantId,
        bool includeColumns = false,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT table_id, tenant_id, logical_name, physical_name, schema_version,
                   descriptor, is_active, created_at, updated_at
            FROM morphdb._morph_tables
            WHERE tenant_id = @TenantId AND is_active = true
            ORDER BY created_at
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<TableRow>(sql, new { TenantId = tenantId });

        var tables = rows.Select(MapToTableMetadata).ToList();

        if (includeColumns)
        {
            for (var i = 0; i < tables.Count; i++)
            {
                var columns = await GetColumnsByTableIdAsync(tables[i].TableId, cancellationToken);
                tables[i] = WithColumns(tables[i], columns);
            }
        }

        return tables;
    }

    public async Task UpdateTableAsync(
        Guid tableId,
        string? logicalName,
        int newVersion,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE morphdb._morph_tables
            SET logical_name = COALESCE(@LogicalName, logical_name),
                schema_version = @NewVersion,
                updated_at = NOW()
            WHERE table_id = @TableId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { TableId = tableId, LogicalName = logicalName, NewVersion = newVersion });
    }

    public async Task SoftDeleteTableAsync(
        Guid tableId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE morphdb._morph_tables
            SET is_active = false, updated_at = NOW()
            WHERE table_id = @TableId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { TableId = tableId });
    }

    #endregion

    #region Column Operations

    public async Task<ColumnMetadata> InsertColumnAsync(
        ColumnMetadata column,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO morphdb._morph_columns
                (column_id, table_id, logical_name, physical_name, data_type, native_type,
                 is_nullable, is_unique, is_primary_key, is_indexed, is_encrypted,
                 default_value, check_expr, ordinal_position, descriptor)
            VALUES
                (@ColumnId, @TableId, @LogicalName, @PhysicalName, @DataType, @NativeType,
                 @IsNullable, @IsUnique, @IsPrimaryKey, @IsIndexed, @IsEncrypted,
                 @DefaultValue, @CheckExpression, @OrdinalPosition, @Descriptor::jsonb)
            RETURNING column_id, table_id, logical_name, physical_name, data_type, native_type,
                      is_nullable, is_unique, is_primary_key, is_indexed, is_encrypted,
                      default_value, check_expr, ordinal_position, descriptor, is_active
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await connection.QuerySingleAsync<ColumnRow>(sql, new
        {
            column.ColumnId,
            column.TableId,
            column.LogicalName,
            column.PhysicalName,
            DataType = column.DataType.ToString(),
            column.NativeType,
            column.IsNullable,
            column.IsUnique,
            column.IsPrimaryKey,
            column.IsIndexed,
            column.IsEncrypted,
            column.DefaultValue,
            column.CheckExpression,
            column.OrdinalPosition,
            Descriptor = column.Descriptor?.RootElement.GetRawText()
        });

        return MapToColumnMetadata(result);
    }

    public async Task<ColumnMetadata?> GetColumnByIdAsync(
        Guid columnId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT column_id, table_id, logical_name, physical_name, data_type, native_type,
                   is_nullable, is_unique, is_primary_key, is_indexed, is_encrypted,
                   default_value, check_expr, ordinal_position, descriptor, is_active
            FROM morphdb._morph_columns
            WHERE column_id = @ColumnId AND is_active = true
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ColumnRow>(sql, new { ColumnId = columnId });

        return row is null ? null : MapToColumnMetadata(row);
    }

    public async Task<IReadOnlyList<ColumnMetadata>> GetColumnsByTableIdAsync(
        Guid tableId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT column_id, table_id, logical_name, physical_name, data_type, native_type,
                   is_nullable, is_unique, is_primary_key, is_indexed, is_encrypted,
                   default_value, check_expr, ordinal_position, descriptor, is_active
            FROM morphdb._morph_columns
            WHERE table_id = @TableId AND is_active = true
            ORDER BY ordinal_position
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ColumnRow>(sql, new { TableId = tableId });

        return rows.Select(MapToColumnMetadata).ToList();
    }

    public async Task<int> GetNextOrdinalPositionAsync(
        Guid tableId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT COALESCE(MAX(ordinal_position), 0) + 1
            FROM morphdb._morph_columns
            WHERE table_id = @TableId AND is_active = true
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(sql, new { TableId = tableId });
    }

    public async Task UpdateColumnAsync(
        Guid columnId,
        string? logicalName,
        string? defaultValue,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE morphdb._morph_columns
            SET logical_name = COALESCE(@LogicalName, logical_name),
                default_value = COALESCE(@DefaultValue, default_value)
            WHERE column_id = @ColumnId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { ColumnId = columnId, LogicalName = logicalName, DefaultValue = defaultValue });
    }

    public async Task SoftDeleteColumnAsync(
        Guid columnId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE morphdb._morph_columns
            SET is_active = false
            WHERE column_id = @ColumnId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { ColumnId = columnId });
    }

    #endregion

    #region Index Operations

    public async Task<IndexMetadata> InsertIndexAsync(
        IndexMetadata index,
        CancellationToken cancellationToken = default)
    {
        var columnsJson = JsonSerializer.Serialize(index.Columns.Select(c => new
        {
            column_id = c.ColumnId,
            physical_name = c.PhysicalName,
            direction = c.Direction.ToString().ToLowerInvariant(),
            nulls_position = c.NullsPosition.ToString().ToLowerInvariant()
        }));

        const string sql = """
            INSERT INTO morphdb._morph_indexes
                (index_id, table_id, logical_name, physical_name, columns, index_type, is_unique, where_clause, descriptor)
            VALUES
                (@IndexId, @TableId, @LogicalName, @PhysicalName, @Columns::jsonb, @IndexType, @IsUnique, @WhereClause, @Descriptor::jsonb)
            RETURNING index_id, table_id, logical_name, physical_name, columns, index_type, is_unique, where_clause, descriptor, is_active, created_at
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await connection.QuerySingleAsync<IndexRow>(sql, new
        {
            index.IndexId,
            index.TableId,
            index.LogicalName,
            index.PhysicalName,
            Columns = columnsJson,
            IndexType = index.IndexType.ToString().ToLowerInvariant(),
            index.IsUnique,
            index.WhereClause,
            Descriptor = index.Descriptor?.RootElement.GetRawText()
        });

        return MapToIndexMetadata(result);
    }

    public async Task<IndexMetadata?> GetIndexByIdAsync(
        Guid indexId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT index_id, table_id, logical_name, physical_name, columns, index_type, is_unique, where_clause, descriptor, is_active, created_at
            FROM morphdb._morph_indexes
            WHERE index_id = @IndexId AND is_active = true
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<IndexRow>(sql, new { IndexId = indexId });

        return row is null ? null : MapToIndexMetadata(row);
    }

    public async Task<IReadOnlyList<IndexMetadata>> GetIndexesByTableIdAsync(
        Guid tableId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT index_id, table_id, logical_name, physical_name, columns, index_type, is_unique, where_clause, descriptor, is_active, created_at
            FROM morphdb._morph_indexes
            WHERE table_id = @TableId AND is_active = true
            ORDER BY created_at
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<IndexRow>(sql, new { TableId = tableId });

        return rows.Select(MapToIndexMetadata).ToList();
    }

    public async Task SoftDeleteIndexAsync(
        Guid indexId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE morphdb._morph_indexes
            SET is_active = false
            WHERE index_id = @IndexId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { IndexId = indexId });
    }

    #endregion

    #region Relation Operations

    public async Task<RelationMetadata> InsertRelationAsync(
        RelationMetadata relation,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO morphdb._morph_relations
                (relation_id, tenant_id, logical_name, source_table_id, source_column_id, target_table_id, target_column_id, relation_type, on_delete, on_update, descriptor)
            VALUES
                (@RelationId, @TenantId, @LogicalName, @SourceTableId, @SourceColumnId, @TargetTableId, @TargetColumnId, @RelationType, @OnDelete, @OnUpdate, @Descriptor::jsonb)
            RETURNING relation_id, tenant_id, logical_name, source_table_id, source_column_id, target_table_id, target_column_id, relation_type, on_delete, on_update, descriptor, is_active, created_at
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await connection.QuerySingleAsync<RelationRow>(sql, new
        {
            relation.RelationId,
            relation.TenantId,
            relation.LogicalName,
            relation.SourceTableId,
            relation.SourceColumnId,
            relation.TargetTableId,
            relation.TargetColumnId,
            RelationType = relation.RelationType.ToString(),
            OnDelete = MapOnDeleteAction(relation.OnDelete),
            OnUpdate = MapOnUpdateAction(relation.OnUpdate),
            Descriptor = relation.Descriptor?.RootElement.GetRawText()
        });

        return MapToRelationMetadata(result);
    }

    public async Task<RelationMetadata?> GetRelationByIdAsync(
        Guid relationId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT relation_id, tenant_id, logical_name, source_table_id, source_column_id, target_table_id, target_column_id, relation_type, on_delete, on_update, descriptor, is_active, created_at
            FROM morphdb._morph_relations
            WHERE relation_id = @RelationId AND is_active = true
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<RelationRow>(sql, new { RelationId = relationId });

        return row is null ? null : MapToRelationMetadata(row);
    }

    public async Task<IReadOnlyList<RelationMetadata>> GetRelationsByTableIdAsync(
        Guid tableId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT relation_id, tenant_id, logical_name, source_table_id, source_column_id, target_table_id, target_column_id, relation_type, on_delete, on_update, descriptor, is_active, created_at
            FROM morphdb._morph_relations
            WHERE (source_table_id = @TableId OR target_table_id = @TableId) AND is_active = true
            ORDER BY created_at
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<RelationRow>(sql, new { TableId = tableId });

        return rows.Select(MapToRelationMetadata).ToList();
    }

    public async Task SoftDeleteRelationAsync(
        Guid relationId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE morphdb._morph_relations
            SET is_active = false
            WHERE relation_id = @RelationId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { RelationId = relationId });
    }

    #endregion

    #region Version Operations

    public async Task<int> GetCurrentVersionAsync(
        Guid tableId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT schema_version
            FROM morphdb._morph_tables
            WHERE table_id = @TableId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(sql, new { TableId = tableId });
    }

    public async Task IncrementVersionAsync(
        Guid tableId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE morphdb._morph_tables
            SET schema_version = schema_version + 1, updated_at = NOW()
            WHERE table_id = @TableId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { TableId = tableId });
    }

    #endregion

    #region Mapping Helpers

    private static TableMetadata WithColumns(TableMetadata table, IReadOnlyList<ColumnMetadata> columns) => new()
    {
        TableId = table.TableId,
        TenantId = table.TenantId,
        LogicalName = table.LogicalName,
        PhysicalName = table.PhysicalName,
        SchemaVersion = table.SchemaVersion,
        Descriptor = table.Descriptor,
        IsActive = table.IsActive,
        CreatedAt = table.CreatedAt,
        UpdatedAt = table.UpdatedAt,
        Columns = columns,
        Relations = table.Relations,
        Indexes = table.Indexes
    };

    private static TableMetadata MapToTableMetadata(TableRow row) => new()
    {
        TableId = row.table_id,
        TenantId = row.tenant_id,
        LogicalName = row.logical_name,
        PhysicalName = row.physical_name,
        SchemaVersion = row.schema_version,
        Descriptor = row.descriptor is not null ? JsonDocument.Parse(row.descriptor) : null,
        IsActive = row.is_active,
        CreatedAt = row.created_at,
        UpdatedAt = row.updated_at
    };

    private static ColumnMetadata MapToColumnMetadata(ColumnRow row) => new()
    {
        ColumnId = row.column_id,
        TableId = row.table_id,
        LogicalName = row.logical_name,
        PhysicalName = row.physical_name,
        DataType = Enum.Parse<MorphDataType>(row.data_type, ignoreCase: true),
        NativeType = row.native_type,
        IsNullable = row.is_nullable,
        IsUnique = row.is_unique,
        IsPrimaryKey = row.is_primary_key,
        IsIndexed = row.is_indexed,
        IsEncrypted = row.is_encrypted,
        DefaultValue = row.default_value,
        CheckExpression = row.check_expr,
        OrdinalPosition = row.ordinal_position,
        Descriptor = row.descriptor is not null ? JsonDocument.Parse(row.descriptor) : null,
        IsActive = row.is_active
    };

    private static IndexMetadata MapToIndexMetadata(IndexRow row)
    {
        var columnsDoc = JsonDocument.Parse(row.columns);
        var columns = columnsDoc.RootElement.EnumerateArray()
            .Select(elem => new IndexColumnInfo
            {
                ColumnId = elem.GetProperty("column_id").GetGuid(),
                PhysicalName = elem.GetProperty("physical_name").GetString()!,
                Direction = Enum.Parse<SortDirection>(elem.GetProperty("direction").GetString()!, ignoreCase: true),
                NullsPosition = Enum.Parse<NullsPosition>(elem.GetProperty("nulls_position").GetString()!, ignoreCase: true)
            })
            .ToList();

        return new IndexMetadata
        {
            IndexId = row.index_id,
            TableId = row.table_id,
            LogicalName = row.logical_name,
            PhysicalName = row.physical_name,
            Columns = columns,
            IndexType = Enum.Parse<IndexType>(row.index_type, ignoreCase: true),
            IsUnique = row.is_unique,
            WhereClause = row.where_clause,
            Descriptor = row.descriptor is not null ? JsonDocument.Parse(row.descriptor) : null,
            IsActive = row.is_active
        };
    }

    private static RelationMetadata MapToRelationMetadata(RelationRow row) => new()
    {
        RelationId = row.relation_id,
        TenantId = row.tenant_id,
        LogicalName = row.logical_name,
        SourceTableId = row.source_table_id,
        SourceColumnId = row.source_column_id,
        TargetTableId = row.target_table_id,
        TargetColumnId = row.target_column_id,
        RelationType = Enum.Parse<RelationType>(row.relation_type, ignoreCase: true),
        OnDelete = ParseOnDeleteAction(row.on_delete),
        OnUpdate = ParseOnUpdateAction(row.on_update),
        Descriptor = row.descriptor is not null ? JsonDocument.Parse(row.descriptor) : null,
        IsActive = row.is_active
    };

    private static string MapOnDeleteAction(OnDeleteAction action) => action switch
    {
        OnDeleteAction.NoAction => "NO ACTION",
        OnDeleteAction.Cascade => "CASCADE",
        OnDeleteAction.SetNull => "SET NULL",
        OnDeleteAction.SetDefault => "SET DEFAULT",
        OnDeleteAction.Restrict => "RESTRICT",
        _ => "NO ACTION"
    };

    private static string MapOnUpdateAction(OnUpdateAction action) => action switch
    {
        OnUpdateAction.NoAction => "NO ACTION",
        OnUpdateAction.Cascade => "CASCADE",
        OnUpdateAction.SetNull => "SET NULL",
        OnUpdateAction.SetDefault => "SET DEFAULT",
        OnUpdateAction.Restrict => "RESTRICT",
        _ => "NO ACTION"
    };

    private static OnDeleteAction ParseOnDeleteAction(string value) => value.ToUpperInvariant() switch
    {
        "NO ACTION" => OnDeleteAction.NoAction,
        "CASCADE" => OnDeleteAction.Cascade,
        "SET NULL" => OnDeleteAction.SetNull,
        "SET DEFAULT" => OnDeleteAction.SetDefault,
        "RESTRICT" => OnDeleteAction.Restrict,
        _ => OnDeleteAction.NoAction
    };

    private static OnUpdateAction ParseOnUpdateAction(string value) => value.ToUpperInvariant() switch
    {
        "NO ACTION" => OnUpdateAction.NoAction,
        "CASCADE" => OnUpdateAction.Cascade,
        "SET NULL" => OnUpdateAction.SetNull,
        "SET DEFAULT" => OnUpdateAction.SetDefault,
        "RESTRICT" => OnUpdateAction.Restrict,
        _ => OnUpdateAction.NoAction
    };

    #endregion

    #region Row Types

    /// <summary>
    /// Dapper-compatible row type for table metadata.
    /// Uses parameterless constructor and public setters for Dapper materialization.
    /// </summary>
    private sealed class TableRow
    {
        public Guid table_id { get; set; }
        public Guid tenant_id { get; set; }
        public string logical_name { get; set; } = "";
        public string physical_name { get; set; } = "";
        public int schema_version { get; set; }
        public string? descriptor { get; set; }
        public bool is_active { get; set; }
        public DateTimeOffset created_at { get; set; }
        public DateTimeOffset updated_at { get; set; }
    }

    /// <summary>
    /// Dapper-compatible row type for column metadata.
    /// </summary>
    private sealed class ColumnRow
    {
        public Guid column_id { get; set; }
        public Guid table_id { get; set; }
        public string logical_name { get; set; } = "";
        public string physical_name { get; set; } = "";
        public string data_type { get; set; } = "";
        public string native_type { get; set; } = "";
        public bool is_nullable { get; set; }
        public bool is_unique { get; set; }
        public bool is_primary_key { get; set; }
        public bool is_indexed { get; set; }
        public bool is_encrypted { get; set; }
        public string? default_value { get; set; }
        public string? check_expr { get; set; }
        public int ordinal_position { get; set; }
        public string? descriptor { get; set; }
        public bool is_active { get; set; }
    }

    /// <summary>
    /// Dapper-compatible row type for index metadata.
    /// </summary>
    private sealed class IndexRow
    {
        public Guid index_id { get; set; }
        public Guid table_id { get; set; }
        public string logical_name { get; set; } = "";
        public string physical_name { get; set; } = "";
        public string columns { get; set; } = "";
        public string index_type { get; set; } = "";
        public bool is_unique { get; set; }
        public string? where_clause { get; set; }
        public string? descriptor { get; set; }
        public bool is_active { get; set; }
        public DateTimeOffset created_at { get; set; }
    }

    /// <summary>
    /// Dapper-compatible row type for relation metadata.
    /// </summary>
    private sealed class RelationRow
    {
        public Guid relation_id { get; set; }
        public Guid tenant_id { get; set; }
        public string logical_name { get; set; } = "";
        public Guid source_table_id { get; set; }
        public Guid source_column_id { get; set; }
        public Guid target_table_id { get; set; }
        public Guid target_column_id { get; set; }
        public string relation_type { get; set; } = "";
        public string on_delete { get; set; } = "";
        public string on_update { get; set; } = "";
        public string? descriptor { get; set; }
        public bool is_active { get; set; }
        public DateTimeOffset created_at { get; set; }
    }

    #endregion
}
