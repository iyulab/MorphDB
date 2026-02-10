using Dapper;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Ddl;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
using Npgsql;

namespace MorphDB.Npgsql.Services;

/// <summary>
/// PostgreSQL implementation of schema management with advisory lock protection.
/// </summary>
public sealed class PostgresSchemaManager : ISchemaManager
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IMetadataRepository _repository;
    private readonly IAdvisoryLockManager _lockManager;
    private readonly INameHasher _nameHasher;
    private readonly IChangeLogger _changeLogger;
    private readonly SchemaManagerOptions _options;

    public PostgresSchemaManager(
        NpgsqlDataSource dataSource,
        IMetadataRepository repository,
        IAdvisoryLockManager lockManager,
        INameHasher nameHasher,
        IChangeLogger changeLogger,
        SchemaManagerOptions? options = null)
    {
        _dataSource = dataSource;
        _repository = repository;
        _lockManager = lockManager;
        _nameHasher = nameHasher;
        _changeLogger = changeLogger;
        _options = options ?? new SchemaManagerOptions();
    }

    #region Table Operations

    public async Task<TableMetadata> CreateTableAsync(
        CreateTableRequest request,
        CancellationToken cancellationToken = default)
    {
        LogicalNameValidator.ValidateEntityName(request.LogicalName, "Table");

        // Check if table already exists
        var existing = await _repository.GetTableByNameAsync(
            request.TenantId, request.LogicalName, cancellationToken: cancellationToken);
        if (existing is not null)
        {
            throw new DuplicateNameException("Table", request.LogicalName);
        }

        var tableId = Guid.NewGuid();
        var physicalTableName = _nameHasher.GenerateTableName(request.TenantId, request.LogicalName);

        // Acquire advisory lock for DDL
        await using var lockHandle = await _lockManager.AcquireDdlLockAsync(
            $"table:{tableId}",
            _options.LockTimeout,
            cancellationToken);

        // Build column metadata
        var columns = new List<ColumnMetadata>();
        var columnDefinitions = new List<ColumnDefinition>();
        var ordinal = 1;

        // Get system column options (defaults if not specified)
        var sysOpts = request.SystemColumns ?? new SystemColumnOptions();

        // Add Core system columns (always present, cannot be disabled)
        // _id: UUID v7 primary key (generated in application, not DB)
        var idColumn = CreateSystemColumn(tableId, SystemColumns.Id, MorphDataType.Uuid, ordinal++, isPrimaryKey: true);
        columns.Add(idColumn);
        columnDefinitions.Add(ColumnDefinition.FromMetadata(idColumn));

        // tenant_id: Internal column for multi-tenancy (not exposed to API)
        var tenantColumn = CreateSystemColumn(tableId, SystemColumns.TenantId, MorphDataType.Uuid, ordinal++);
        columns.Add(tenantColumn);
        columnDefinitions.Add(ColumnDefinition.FromMetadata(tenantColumn));

        // _created_at: Immutable creation timestamp
        var createdAtColumn = CreateSystemColumn(tableId, SystemColumns.CreatedAt, MorphDataType.CreatedTime, ordinal++);
        columns.Add(createdAtColumn);
        columnDefinitions.Add(ColumnDefinition.FromMetadata(createdAtColumn) with { DefaultExpression = "CURRENT_TIMESTAMP" });

        // _updated_at: Auto-updated modification timestamp
        var updatedAtColumn = CreateSystemColumn(tableId, SystemColumns.UpdatedAt, MorphDataType.ModifiedTime, ordinal++);
        columns.Add(updatedAtColumn);
        columnDefinitions.Add(ColumnDefinition.FromMetadata(updatedAtColumn) with { DefaultExpression = "CURRENT_TIMESTAMP" });

        // Add Standard columns based on options
        if (sysOpts.VersioningEnabled)
        {
            var versionColumn = CreateSystemColumn(tableId, SystemColumns.Version, MorphDataType.Integer, ordinal++);
            columns.Add(versionColumn);
            columnDefinitions.Add(ColumnDefinition.FromMetadata(versionColumn) with { DefaultExpression = "1" });
        }

        if (sysOpts.AuditFieldsEnabled)
        {
            var createdByColumn = CreateSystemColumn(tableId, SystemColumns.CreatedBy, MorphDataType.Uuid, ordinal++, isNullable: true);
            columns.Add(createdByColumn);
            columnDefinitions.Add(ColumnDefinition.FromMetadata(createdByColumn));

            var updatedByColumn = CreateSystemColumn(tableId, SystemColumns.UpdatedBy, MorphDataType.Uuid, ordinal++, isNullable: true);
            columns.Add(updatedByColumn);
            columnDefinitions.Add(ColumnDefinition.FromMetadata(updatedByColumn));
        }

        // Add Optional columns based on options
        if (sysOpts.SoftDeleteEnabled)
        {
            var deletedAtColumn = CreateSystemColumn(tableId, SystemColumns.DeletedAt, MorphDataType.DateTime, ordinal++, isNullable: true);
            columns.Add(deletedAtColumn);
            columnDefinitions.Add(ColumnDefinition.FromMetadata(deletedAtColumn));

            var deletedByColumn = CreateSystemColumn(tableId, SystemColumns.DeletedBy, MorphDataType.Uuid, ordinal++, isNullable: true);
            columns.Add(deletedByColumn);
            columnDefinitions.Add(ColumnDefinition.FromMetadata(deletedByColumn));
        }

        if (sysOpts.OwnershipEnabled)
        {
            var ownerColumn = CreateSystemColumn(tableId, SystemColumns.OwnerId, MorphDataType.Uuid, ordinal++, isNullable: true);
            columns.Add(ownerColumn);
            columnDefinitions.Add(ColumnDefinition.FromMetadata(ownerColumn));
        }

        if (sysOpts.HierarchyEnabled)
        {
            var parentColumn = CreateSystemColumn(tableId, SystemColumns.ParentId, MorphDataType.Uuid, ordinal++, isNullable: true);
            columns.Add(parentColumn);
            columnDefinitions.Add(ColumnDefinition.FromMetadata(parentColumn));

            var sortOrderColumn = CreateSystemColumn(tableId, SystemColumns.SortOrder, MorphDataType.Integer, ordinal++, isNullable: true);
            columns.Add(sortOrderColumn);
            columnDefinitions.Add(ColumnDefinition.FromMetadata(sortOrderColumn) with { DefaultExpression = "0" });
        }

        if (sysOpts.SourceTrackingEnabled)
        {
            var sourceIdColumn = CreateSystemColumn(tableId, SystemColumns.SourceId, MorphDataType.Text, ordinal++, isNullable: true);
            columns.Add(sourceIdColumn);
            columnDefinitions.Add(ColumnDefinition.FromMetadata(sourceIdColumn));
        }

        // Add user-defined columns
        foreach (var colReq in request.Columns)
        {
            LogicalNameValidator.ValidateColumnName(colReq.LogicalName);

            var columnId = Guid.NewGuid();

            // Check if this is a virtual column (lookup, rollup, formula)
            var isVirtualColumn = colReq.LookupConfig != null || colReq.RollupConfig != null || colReq.FormulaConfig != null;

            // Virtual columns don't have a physical column in the database
            var physicalColName = isVirtualColumn
                ? $"virtual_{colReq.LogicalName}"
                : _nameHasher.GenerateColumnName(tableId, colReq.LogicalName);

            var nativeType = isVirtualColumn
                ? "virtual"
                : TypeMapper.ToNativeType(colReq.DataType);

            var column = new ColumnMetadata
            {
                ColumnId = columnId,
                TableId = tableId,
                LogicalName = colReq.LogicalName,
                PhysicalName = physicalColName,
                DataType = colReq.DataType,
                NativeType = nativeType,
                IsNullable = colReq.IsNullable,
                IsUnique = colReq.IsUnique,
                IsPrimaryKey = colReq.IsPrimaryKey,
                IsIndexed = colReq.IsIndexed,
                DefaultValue = colReq.DefaultValue,
                OrdinalPosition = ordinal++,
                IsActive = true,
                LookupConfig = colReq.LookupConfig,
                RollupConfig = colReq.RollupConfig,
                FormulaConfig = colReq.FormulaConfig
            };

            columns.Add(column);

            // Only add physical columns to DDL
            if (!isVirtualColumn)
            {
                columnDefinitions.Add(ColumnDefinition.FromMetadata(column));
            }
        }

        // Create table metadata with system column options
        var tableMetadata = new TableMetadata
        {
            TableId = tableId,
            TenantId = request.TenantId,
            LogicalName = request.LogicalName,
            PhysicalName = physicalTableName,
            SchemaVersion = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
            Columns = columns,
            // Standard column options
            TimestampsEnabled = true, // Always enabled (Core)
            VersioningEnabled = sysOpts.VersioningEnabled,
            AuditFieldsEnabled = sysOpts.AuditFieldsEnabled,
            // Optional column options
            SoftDeleteEnabled = sysOpts.SoftDeleteEnabled,
            OwnershipEnabled = sysOpts.OwnershipEnabled,
            HierarchyEnabled = sysOpts.HierarchyEnabled,
            SourceTrackingEnabled = sysOpts.SourceTrackingEnabled
        };

        // Execute DDL and insert metadata in a transaction
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Create physical table
            var createTableSql = DdlBuilder.BuildCreateTable(physicalTableName, columnDefinitions);
            await connection.ExecuteAsync(createTableSql, transaction: transaction);

            // Create tenant_id index for RLS performance
            var tenantIndexSql = DdlBuilder.BuildCreateIndex(new IndexDefinition
            {
                PhysicalName = $"idx_{physicalTableName}_tenant",
                TablePhysicalName = physicalTableName,
                Columns = [new IndexColumnInfo { ColumnId = tenantColumn.ColumnId, LogicalName = tenantColumn.LogicalName, PhysicalName = tenantColumn.PhysicalName }],
                IndexType = IndexType.BTree
            });
            await connection.ExecuteAsync(tenantIndexSql, transaction: transaction);

            // Create unique/indexed columns
            foreach (var col in columns.Where(c => c.IsUnique && !c.IsPrimaryKey))
            {
                var uniqueConstraintSql = DdlBuilder.BuildAddUniqueConstraint(
                    physicalTableName,
                    $"uq_{physicalTableName}_{col.PhysicalName}",
                    col.PhysicalName);
                await connection.ExecuteAsync(uniqueConstraintSql, transaction: transaction);
            }

            foreach (var col in columns.Where(c => c.IsIndexed && !c.IsPrimaryKey && !c.IsUnique))
            {
                var indexSql = DdlBuilder.BuildCreateIndex(new IndexDefinition
                {
                    PhysicalName = $"idx_{physicalTableName}_{col.PhysicalName}",
                    TablePhysicalName = physicalTableName,
                    Columns = [new IndexColumnInfo { ColumnId = col.ColumnId, LogicalName = col.LogicalName, PhysicalName = col.PhysicalName }],
                    IndexType = TypeMapper.GetRecommendedIndexType(col.DataType)
                });
                await connection.ExecuteAsync(indexSql, transaction: transaction);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        // Insert metadata (after DDL success)
        var insertedTable = await _repository.InsertTableAsync(tableMetadata, cancellationToken);
        foreach (var column in columns)
        {
            await _repository.InsertColumnAsync(column, cancellationToken);
        }

        // Log change
        await _changeLogger.LogChangeAsync(new SchemaChangeEntry
        {
            TableId = tableId,
            Operation = SchemaOperation.CreateTable,
            SchemaVersion = 1,
            Changes = new
            {
                LogicalName = request.LogicalName,
                PhysicalName = physicalTableName,
                ColumnCount = columns.Count
            }
        }, cancellationToken);

        return new TableMetadata
        {
            TableId = insertedTable.TableId,
            TenantId = insertedTable.TenantId,
            LogicalName = insertedTable.LogicalName,
            PhysicalName = insertedTable.PhysicalName,
            SchemaVersion = insertedTable.SchemaVersion,
            Descriptor = insertedTable.Descriptor,
            CreatedAt = insertedTable.CreatedAt,
            UpdatedAt = insertedTable.UpdatedAt,
            IsActive = insertedTable.IsActive,
            Columns = columns,
            // System column options
            TimestampsEnabled = true,
            VersioningEnabled = sysOpts.VersioningEnabled,
            AuditFieldsEnabled = sysOpts.AuditFieldsEnabled,
            SoftDeleteEnabled = sysOpts.SoftDeleteEnabled,
            OwnershipEnabled = sysOpts.OwnershipEnabled,
            HierarchyEnabled = sysOpts.HierarchyEnabled,
            SourceTrackingEnabled = sysOpts.SourceTrackingEnabled
        };
    }

    public async Task<TableMetadata?> GetTableAsync(
        Guid tenantId,
        string logicalName,
        CancellationToken cancellationToken = default)
    {
        return await _repository.GetTableByNameAsync(tenantId, logicalName, includeColumns: true, cancellationToken);
    }

    public async Task<TableMetadata?> GetTableByIdAsync(
        Guid tableId,
        CancellationToken cancellationToken = default)
    {
        return await _repository.GetTableByIdAsync(tableId, includeColumns: true, cancellationToken);
    }

    public async Task<IReadOnlyList<TableMetadata>> ListTablesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return await _repository.ListTablesAsync(tenantId, includeColumns: true, cancellationToken);
    }

    public async Task<TableMetadata> UpdateTableAsync(
        UpdateTableRequest request,
        CancellationToken cancellationToken = default)
    {
        var table = await _repository.GetTableByIdAsync(request.TableId, cancellationToken: cancellationToken)
            ?? throw new TableNotFoundException(request.TableId.ToString());

        // Optimistic concurrency check
        var currentVersion = await _repository.GetCurrentVersionAsync(request.TableId, cancellationToken);
        if (currentVersion != request.ExpectedVersion)
        {
            throw new SchemaVersionConflictException(request.ExpectedVersion, currentVersion);
        }

        // Validate new name if provided
        if (request.LogicalName is not null)
        {
            LogicalNameValidator.ValidateEntityName(request.LogicalName, "Table");

            // Check for duplicate name
            var existing = await _repository.GetTableByNameAsync(table.TenantId, request.LogicalName, cancellationToken: cancellationToken);
            if (existing is not null && existing.TableId != request.TableId)
            {
                throw new DuplicateNameException("Table", request.LogicalName);
            }
        }

        var newVersion = currentVersion + 1;
        await _repository.UpdateTableAsync(request.TableId, request.LogicalName, newVersion, cancellationToken);

        // Log change
        await _changeLogger.LogChangeAsync(new SchemaChangeEntry
        {
            TableId = request.TableId,
            Operation = SchemaOperation.UpdateTable,
            SchemaVersion = newVersion,
            Changes = new
            {
                OldLogicalName = table.LogicalName,
                NewLogicalName = request.LogicalName ?? table.LogicalName
            }
        }, cancellationToken);

        return (await _repository.GetTableByIdAsync(request.TableId, includeColumns: true, cancellationToken))!;
    }

    public async Task DeleteTableAsync(
        Guid tableId,
        CancellationToken cancellationToken = default)
    {
        var table = await _repository.GetTableByIdAsync(tableId, cancellationToken: cancellationToken)
            ?? throw new TableNotFoundException(tableId.ToString());

        // Acquire advisory lock
        await using var lockHandle = await _lockManager.AcquireDdlLockAsync(
            $"table:{tableId}",
            _options.LockTimeout,
            cancellationToken);

        var currentVersion = await _repository.GetCurrentVersionAsync(tableId, cancellationToken);

        // Execute DDL
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var dropTableSql = DdlBuilder.BuildDropTable(table.PhysicalName);
        await connection.ExecuteAsync(dropTableSql);

        // Soft delete metadata
        await _repository.SoftDeleteTableAsync(tableId, cancellationToken);

        // Log change
        await _changeLogger.LogChangeAsync(new SchemaChangeEntry
        {
            TableId = tableId,
            Operation = SchemaOperation.DeleteTable,
            SchemaVersion = currentVersion,
            Changes = new
            {
                LogicalName = table.LogicalName,
                PhysicalName = table.PhysicalName
            }
        }, cancellationToken);
    }

    #endregion

    #region Column Operations

    public async Task<ColumnMetadata> AddColumnAsync(
        AddColumnRequest request,
        CancellationToken cancellationToken = default)
    {
        LogicalNameValidator.ValidateColumnName(request.LogicalName);

        var table = await _repository.GetTableByIdAsync(request.TableId, includeColumns: true, cancellationToken)
            ?? throw new TableNotFoundException(request.TableId.ToString());

        // Check for duplicate column name
        if (table.Columns.Any(c => c.LogicalName.Equals(request.LogicalName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DuplicateNameException("Column", request.LogicalName);
        }

        // Optimistic concurrency check
        var currentVersion = await _repository.GetCurrentVersionAsync(request.TableId, cancellationToken);
        if (currentVersion != request.ExpectedVersion)
        {
            throw new SchemaVersionConflictException(request.ExpectedVersion, currentVersion);
        }

        // Acquire advisory lock
        await using var lockHandle = await _lockManager.AcquireDdlLockAsync(
            $"table:{request.TableId}",
            _options.LockTimeout,
            cancellationToken);

        var columnId = Guid.NewGuid();
        var ordinalPosition = await _repository.GetNextOrdinalPositionAsync(request.TableId, cancellationToken);

        // Check if this is a virtual column (lookup, rollup, formula)
        var isVirtualColumn = request.LookupConfig != null || request.RollupConfig != null || request.FormulaConfig != null;

        // Virtual columns don't have a physical column in the database
        var physicalColName = isVirtualColumn
            ? $"virtual_{request.LogicalName}"
            : _nameHasher.GenerateColumnName(request.TableId, request.LogicalName);

        var nativeType = isVirtualColumn
            ? "virtual"
            : TypeMapper.ToNativeType(request.DataType);

        var column = new ColumnMetadata
        {
            ColumnId = columnId,
            TableId = request.TableId,
            LogicalName = request.LogicalName,
            PhysicalName = physicalColName,
            DataType = request.DataType,
            NativeType = nativeType,
            IsNullable = request.IsNullable,
            IsUnique = request.IsUnique,
            IsIndexed = request.IsIndexed,
            DefaultValue = request.DefaultValue,
            OrdinalPosition = ordinalPosition,
            IsActive = true,
            LookupConfig = request.LookupConfig,
            RollupConfig = request.RollupConfig,
            FormulaConfig = request.FormulaConfig
        };

        // Only execute DDL for physical columns
        if (!isVirtualColumn)
        {
            var columnDef = ColumnDefinition.FromMetadata(column);

            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                var addColumnSql = DdlBuilder.BuildAddColumn(table.PhysicalName, columnDef);
                await connection.ExecuteAsync(addColumnSql, transaction: transaction);

                if (request.IsUnique)
                {
                    var uniqueSql = DdlBuilder.BuildAddUniqueConstraint(
                        table.PhysicalName,
                        $"uq_{table.PhysicalName}_{physicalColName}",
                        physicalColName);
                    await connection.ExecuteAsync(uniqueSql, transaction: transaction);
                }

                if (request.IsIndexed && !request.IsUnique)
                {
                    var indexSql = DdlBuilder.BuildCreateIndex(new IndexDefinition
                    {
                        PhysicalName = $"idx_{table.PhysicalName}_{physicalColName}",
                        TablePhysicalName = table.PhysicalName,
                        Columns = [new IndexColumnInfo { ColumnId = columnId, LogicalName = request.LogicalName, PhysicalName = physicalColName }],
                        IndexType = TypeMapper.GetRecommendedIndexType(request.DataType)
                    });
                    await connection.ExecuteAsync(indexSql, transaction: transaction);
                }

                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        // Insert metadata and increment version
        var insertedColumn = await _repository.InsertColumnAsync(column, cancellationToken);
        await _repository.IncrementVersionAsync(request.TableId, cancellationToken);

        // Log change
        await _changeLogger.LogChangeAsync(new SchemaChangeEntry
        {
            TableId = request.TableId,
            Operation = SchemaOperation.AddColumn,
            SchemaVersion = currentVersion + 1,
            Changes = new
            {
                column.LogicalName,
                column.PhysicalName,
                DataType = request.DataType.ToString(),
                IsVirtual = isVirtualColumn
            }
        }, cancellationToken);

        return insertedColumn;
    }

    public async Task<ColumnMetadata> UpdateColumnAsync(
        UpdateColumnRequest request,
        CancellationToken cancellationToken = default)
    {
        var column = await _repository.GetColumnByIdAsync(request.ColumnId, cancellationToken)
            ?? throw new ColumnNotFoundException("unknown", request.ColumnId.ToString());

        // Validate new name if provided
        if (request.LogicalName is not null)
        {
            LogicalNameValidator.ValidateColumnName(request.LogicalName);

            // Check for duplicate name in same table
            var columns = await _repository.GetColumnsByTableIdAsync(column.TableId, cancellationToken);
            if (columns.Any(c => c.ColumnId != request.ColumnId &&
                c.LogicalName.Equals(request.LogicalName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new DuplicateNameException("Column", request.LogicalName);
            }
        }

        // Optimistic concurrency check
        var currentVersion = await _repository.GetCurrentVersionAsync(column.TableId, cancellationToken);
        if (currentVersion != request.ExpectedVersion)
        {
            throw new SchemaVersionConflictException(request.ExpectedVersion, currentVersion);
        }

        // Update metadata only (no DDL for logical name change)
        await _repository.UpdateColumnAsync(request.ColumnId, request.LogicalName, request.DefaultValue, cancellationToken);
        await _repository.IncrementVersionAsync(column.TableId, cancellationToken);

        // Log change
        await _changeLogger.LogChangeAsync(new SchemaChangeEntry
        {
            TableId = column.TableId,
            Operation = SchemaOperation.UpdateColumn,
            SchemaVersion = currentVersion + 1,
            Changes = new
            {
                ColumnId = request.ColumnId,
                OldLogicalName = column.LogicalName,
                NewLogicalName = request.LogicalName ?? column.LogicalName
            }
        }, cancellationToken);

        return (await _repository.GetColumnByIdAsync(request.ColumnId, cancellationToken))!;
    }

    public async Task DeleteColumnAsync(
        Guid columnId,
        CancellationToken cancellationToken = default)
    {
        var column = await _repository.GetColumnByIdAsync(columnId, cancellationToken)
            ?? throw new ColumnNotFoundException("unknown", columnId.ToString());

        var table = await _repository.GetTableByIdAsync(column.TableId, cancellationToken: cancellationToken)
            ?? throw new TableNotFoundException(column.TableId.ToString());

        // Acquire advisory lock
        await using var lockHandle = await _lockManager.AcquireDdlLockAsync(
            $"table:{column.TableId}",
            _options.LockTimeout,
            cancellationToken);

        var currentVersion = await _repository.GetCurrentVersionAsync(column.TableId, cancellationToken);

        // Execute DDL
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var dropColumnSql = DdlBuilder.BuildDropColumn(table.PhysicalName, column.PhysicalName);
        await connection.ExecuteAsync(dropColumnSql);

        // Soft delete metadata
        await _repository.SoftDeleteColumnAsync(columnId, cancellationToken);
        await _repository.IncrementVersionAsync(column.TableId, cancellationToken);

        // Log change
        await _changeLogger.LogChangeAsync(new SchemaChangeEntry
        {
            TableId = column.TableId,
            Operation = SchemaOperation.DeleteColumn,
            SchemaVersion = currentVersion + 1,
            Changes = new
            {
                column.LogicalName,
                column.PhysicalName
            }
        }, cancellationToken);
    }

    #endregion

    #region Index Operations

    public async Task<IndexMetadata> CreateIndexAsync(
        CreateIndexRequest request,
        CancellationToken cancellationToken = default)
    {
        LogicalNameValidator.ValidateEntityName(request.LogicalName, "Index");

        var table = await _repository.GetTableByIdAsync(request.TableId, includeColumns: true, cancellationToken)
            ?? throw new TableNotFoundException(request.TableId.ToString());

        // Validate columns exist
        var indexColumns = new List<IndexColumnInfo>();
        foreach (var colId in request.ColumnIds)
        {
            var column = table.Columns.FirstOrDefault(c => c.ColumnId == colId)
                ?? throw new ColumnNotFoundException(table.LogicalName, colId.ToString());

            indexColumns.Add(new IndexColumnInfo
            {
                ColumnId = colId,
                LogicalName = column.LogicalName,
                PhysicalName = column.PhysicalName
            });
        }

        // Acquire advisory lock
        await using var lockHandle = await _lockManager.AcquireDdlLockAsync(
            $"table:{request.TableId}",
            _options.LockTimeout,
            cancellationToken);

        var indexId = Guid.NewGuid();
        var physicalIndexName = _nameHasher.GenerateIndexName(request.TableId, request.LogicalName);

        var index = new IndexMetadata
        {
            IndexId = indexId,
            TableId = request.TableId,
            LogicalName = request.LogicalName,
            PhysicalName = physicalIndexName,
            Columns = indexColumns,
            IndexType = request.IndexType,
            IsUnique = request.IsUnique,
            WhereClause = request.WhereClause,
            IsActive = true
        };

        // Execute DDL
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var createIndexSql = DdlBuilder.BuildCreateIndex(new IndexDefinition
        {
            PhysicalName = physicalIndexName,
            TablePhysicalName = table.PhysicalName,
            Columns = indexColumns,
            IndexType = request.IndexType,
            IsUnique = request.IsUnique,
            WhereClause = request.WhereClause
        });
        await connection.ExecuteAsync(createIndexSql);

        // Insert metadata and increment version
        var insertedIndex = await _repository.InsertIndexAsync(index, cancellationToken);
        await _repository.IncrementVersionAsync(request.TableId, cancellationToken);

        var newVersion = await _repository.GetCurrentVersionAsync(request.TableId, cancellationToken);

        // Log change
        await _changeLogger.LogChangeAsync(new SchemaChangeEntry
        {
            TableId = request.TableId,
            Operation = SchemaOperation.CreateIndex,
            SchemaVersion = newVersion,
            Changes = new
            {
                index.LogicalName,
                index.PhysicalName,
                ColumnCount = request.ColumnIds.Count
            }
        }, cancellationToken);

        return insertedIndex;
    }

    public async Task DeleteIndexAsync(
        Guid indexId,
        CancellationToken cancellationToken = default)
    {
        var index = await _repository.GetIndexByIdAsync(indexId, cancellationToken)
            ?? throw new SchemaException("INDEX_NOT_FOUND", $"Index with ID '{indexId}' not found.");

        // Acquire advisory lock
        await using var lockHandle = await _lockManager.AcquireDdlLockAsync(
            $"table:{index.TableId}",
            _options.LockTimeout,
            cancellationToken);

        var currentVersion = await _repository.GetCurrentVersionAsync(index.TableId, cancellationToken);

        // Execute DDL
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var dropIndexSql = DdlBuilder.BuildDropIndex(index.PhysicalName);
        await connection.ExecuteAsync(dropIndexSql);

        // Soft delete metadata
        await _repository.SoftDeleteIndexAsync(indexId, cancellationToken);
        await _repository.IncrementVersionAsync(index.TableId, cancellationToken);

        // Log change
        await _changeLogger.LogChangeAsync(new SchemaChangeEntry
        {
            TableId = index.TableId,
            Operation = SchemaOperation.DeleteIndex,
            SchemaVersion = currentVersion + 1,
            Changes = new
            {
                index.LogicalName,
                index.PhysicalName
            }
        }, cancellationToken);
    }

    #endregion

    #region Relation Operations

    public async Task<RelationMetadata> CreateRelationAsync(
        CreateRelationRequest request,
        CancellationToken cancellationToken = default)
    {
        LogicalNameValidator.ValidateEntityName(request.LogicalName, "Relation");

        // Validate source table and column
        var sourceTable = await _repository.GetTableByIdAsync(request.SourceTableId, includeColumns: true, cancellationToken)
            ?? throw new TableNotFoundException(request.SourceTableId.ToString());

        var sourceColumn = sourceTable.Columns.FirstOrDefault(c => c.ColumnId == request.SourceColumnId)
            ?? throw new ColumnNotFoundException(sourceTable.LogicalName, request.SourceColumnId.ToString());

        // Validate target table and column
        var targetTable = await _repository.GetTableByIdAsync(request.TargetTableId, includeColumns: true, cancellationToken)
            ?? throw new TableNotFoundException(request.TargetTableId.ToString());

        var targetColumn = targetTable.Columns.FirstOrDefault(c => c.ColumnId == request.TargetColumnId)
            ?? throw new ColumnNotFoundException(targetTable.LogicalName, request.TargetColumnId.ToString());

        // Acquire advisory lock for both tables
        await using var sourceLock = await _lockManager.AcquireDdlLockAsync(
            $"table:{request.SourceTableId}",
            _options.LockTimeout,
            cancellationToken);

        await using var targetLock = await _lockManager.AcquireDdlLockAsync(
            $"table:{request.TargetTableId}",
            _options.LockTimeout,
            cancellationToken);

        var relationId = Guid.NewGuid();
        Guid? junctionTableId = null;
        string? junctionTableName = null;

        // For ManyToMany relations, auto-generate junction table
        if (request.RelationType == RelationType.ManyToMany)
        {
            junctionTableName = request.JunctionTableName
                ?? $"{sourceTable.LogicalName}_{targetTable.LogicalName}";

            var junctionTable = await CreateJunctionTableAsync(
                request.TenantId,
                junctionTableName,
                sourceTable,
                targetTable,
                cancellationToken);

            junctionTableId = junctionTable.TableId;
        }

        var relation = new RelationMetadata
        {
            RelationId = relationId,
            TenantId = request.TenantId,
            LogicalName = request.LogicalName,
            SourceTableId = request.SourceTableId,
            SourceColumnId = request.SourceColumnId,
            TargetTableId = request.TargetTableId,
            TargetColumnId = request.TargetColumnId,
            SourceTableName = sourceTable.LogicalName,
            SourceColumnName = sourceColumn.LogicalName,
            TargetTableName = targetTable.LogicalName,
            TargetColumnName = targetColumn.LogicalName,
            RelationType = request.RelationType,
            OnDelete = request.OnDelete,
            MaxHierarchyDepth = request.MaxHierarchyDepth,
            JunctionTableId = junctionTableId,
            JunctionTableName = junctionTableName,
            // Virtual FK by default - no physical constraint created
            EnforceOnWrite = true,
            VirtualCascade = true,
            IsActive = true
        };

        // For non-ManyToMany, optionally create physical FK (configurable)
        // Note: Virtual Constraint philosophy recommends NOT creating physical FKs
        // Physical FKs are kept for backward compatibility but can be disabled
        if (request.RelationType != RelationType.ManyToMany && _options.CreatePhysicalForeignKeys)
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            var constraintName = $"fk_{sourceTable.PhysicalName}_{sourceColumn.PhysicalName}";
            var addFkSql = DdlBuilder.BuildAddForeignKey(new ForeignKeyDefinition
            {
                ConstraintName = constraintName,
                SourceTablePhysicalName = sourceTable.PhysicalName,
                SourceColumnPhysicalName = sourceColumn.PhysicalName,
                TargetTablePhysicalName = targetTable.PhysicalName,
                TargetColumnPhysicalName = targetColumn.PhysicalName,
                OnDelete = request.OnDelete
            });
            await connection.ExecuteAsync(addFkSql);
        }

        // Insert metadata and increment version
        var insertedRelation = await _repository.InsertRelationAsync(relation, cancellationToken);
        await _repository.IncrementVersionAsync(request.SourceTableId, cancellationToken);

        var newVersion = await _repository.GetCurrentVersionAsync(request.SourceTableId, cancellationToken);

        // Log change
        await _changeLogger.LogChangeAsync(new SchemaChangeEntry
        {
            TableId = request.SourceTableId,
            Operation = SchemaOperation.CreateRelation,
            SchemaVersion = newVersion,
            Changes = new
            {
                relation.LogicalName,
                SourceTable = sourceTable.LogicalName,
                TargetTable = targetTable.LogicalName,
                JunctionTable = junctionTableName,
                IsSelfReferential = relation.IsSelfReferential
            }
        }, cancellationToken);

        return insertedRelation;
    }

    /// <summary>
    /// Creates a junction table for ManyToMany relations.
    /// The junction table contains source_id and target_id columns.
    /// </summary>
    private async Task<TableMetadata> CreateJunctionTableAsync(
        Guid tenantId,
        string junctionTableName,
        TableMetadata sourceTable,
        TableMetadata targetTable,
        CancellationToken cancellationToken)
    {
        // Find the _id columns from source and target tables
        var sourceIdColumn = sourceTable.Columns.FirstOrDefault(c => c.LogicalName == "_id")
            ?? throw new InvalidOperationException($"Source table '{sourceTable.LogicalName}' must have _id column for M:N relation");
        var targetIdColumn = targetTable.Columns.FirstOrDefault(c => c.LogicalName == "_id")
            ?? throw new InvalidOperationException($"Target table '{targetTable.LogicalName}' must have _id column for M:N relation");

        // Create junction table columns
        var junctionColumns = new List<CreateColumnRequest>
        {
            new()
            {
                LogicalName = $"{sourceTable.LogicalName}_id",
                DataType = sourceIdColumn.DataType,
                IsNullable = false,
                IsIndexed = true
            },
            new()
            {
                LogicalName = $"{targetTable.LogicalName}_id",
                DataType = targetIdColumn.DataType,
                IsNullable = false,
                IsIndexed = true
            }
        };

        // Create the junction table
        var junctionTableRequest = new CreateTableRequest
        {
            TenantId = tenantId,
            LogicalName = junctionTableName,
            Columns = junctionColumns,
            SystemColumns = new SystemColumnOptions
            {
                VersioningEnabled = true,
                AuditFieldsEnabled = false,
                SoftDeleteEnabled = false
            }
        };

        var junctionTable = await CreateTableAsync(junctionTableRequest, cancellationToken);

        // Create unique composite index on (source_id, target_id) to prevent duplicates
        var junctionSourceCol = junctionTable.Columns.First(c => c.LogicalName == $"{sourceTable.LogicalName}_id");
        var junctionTargetCol = junctionTable.Columns.First(c => c.LogicalName == $"{targetTable.LogicalName}_id");

        await CreateIndexAsync(new CreateIndexRequest
        {
            TableId = junctionTable.TableId,
            LogicalName = $"ux_{junctionTableName}_pair",
            ColumnIds = [junctionSourceCol.ColumnId, junctionTargetCol.ColumnId],
            IsUnique = true,
            IndexType = IndexType.BTree
        }, cancellationToken);

        return junctionTable;
    }

    public async Task DeleteRelationAsync(
        Guid relationId,
        CancellationToken cancellationToken = default)
    {
        var relation = await _repository.GetRelationByIdAsync(relationId, cancellationToken)
            ?? throw new SchemaException("RELATION_NOT_FOUND", $"Relation with ID '{relationId}' not found.");

        var sourceTable = await _repository.GetTableByIdAsync(relation.SourceTableId, includeColumns: true, cancellationToken)
            ?? throw new TableNotFoundException(relation.SourceTableId.ToString());

        var sourceColumn = sourceTable.Columns.FirstOrDefault(c => c.ColumnId == relation.SourceColumnId)
            ?? throw new ColumnNotFoundException(sourceTable.LogicalName, relation.SourceColumnId.ToString());

        // Acquire advisory lock
        await using var lockHandle = await _lockManager.AcquireDdlLockAsync(
            $"table:{relation.SourceTableId}",
            _options.LockTimeout,
            cancellationToken);

        var currentVersion = await _repository.GetCurrentVersionAsync(relation.SourceTableId, cancellationToken);

        // Execute DDL
        var constraintName = $"fk_{sourceTable.PhysicalName}_{sourceColumn.PhysicalName}";
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var dropFkSql = DdlBuilder.BuildDropForeignKey(sourceTable.PhysicalName, constraintName);
        await connection.ExecuteAsync(dropFkSql);

        // Soft delete metadata
        await _repository.SoftDeleteRelationAsync(relationId, cancellationToken);
        await _repository.IncrementVersionAsync(relation.SourceTableId, cancellationToken);

        // Log change
        await _changeLogger.LogChangeAsync(new SchemaChangeEntry
        {
            TableId = relation.SourceTableId,
            Operation = SchemaOperation.DeleteRelation,
            SchemaVersion = currentVersion + 1,
            Changes = new
            {
                relation.LogicalName,
                RelationId = relationId
            }
        }, cancellationToken);
    }

    #endregion

    #region Private Helpers

    private static ColumnMetadata CreateSystemColumn(
        Guid tableId,
        string logicalName,
        MorphDataType dataType,
        int ordinalPosition,
        bool isPrimaryKey = false,
        bool isNullable = false)
    {
        var columnId = Guid.NewGuid();
        // System columns are NOT hashed - physical name equals logical name
        var physicalName = logicalName;
        var nativeType = TypeMapper.ToNativeType(dataType);

        return new ColumnMetadata
        {
            ColumnId = columnId,
            TableId = tableId,
            LogicalName = logicalName,
            PhysicalName = physicalName,
            DataType = dataType,
            NativeType = nativeType,
            IsNullable = isNullable,
            IsPrimaryKey = isPrimaryKey,
            IsSystemColumn = true,
            OrdinalPosition = ordinalPosition,
            IsActive = true
        };
    }

    #endregion
}

/// <summary>
/// Options for PostgresSchemaManager.
/// </summary>
public sealed class SchemaManagerOptions
{
    /// <summary>
    /// Timeout for acquiring advisory locks.
    /// </summary>
    public TimeSpan LockTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// When true, creates physical FK constraints in the database.
    /// When false (recommended), uses Virtual FK enforcement at application layer.
    /// Default: true for backward compatibility, but Virtual FK is recommended.
    /// </summary>
    public bool CreatePhysicalForeignKeys { get; init; } = true;
}
