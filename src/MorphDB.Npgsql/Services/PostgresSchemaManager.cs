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
        ValidateLogicalName(request.LogicalName);

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

        // Add system columns first
        var idColumn = CreateSystemColumn(tableId, "id", MorphDataType.Uuid, ordinal++, isPrimaryKey: true);
        columns.Add(idColumn);
        columnDefinitions.Add(ColumnDefinition.FromMetadata(idColumn) with { DefaultExpression = "uuid_generate_v4()" });

        var tenantColumn = CreateSystemColumn(tableId, "tenant_id", MorphDataType.Uuid, ordinal++);
        columns.Add(tenantColumn);
        columnDefinitions.Add(ColumnDefinition.FromMetadata(tenantColumn));

        var createdAtColumn = CreateSystemColumn(tableId, "created_at", MorphDataType.CreatedTime, ordinal++);
        columns.Add(createdAtColumn);
        columnDefinitions.Add(ColumnDefinition.FromMetadata(createdAtColumn));

        var updatedAtColumn = CreateSystemColumn(tableId, "updated_at", MorphDataType.ModifiedTime, ordinal++);
        columns.Add(updatedAtColumn);
        columnDefinitions.Add(ColumnDefinition.FromMetadata(updatedAtColumn));

        // Add user-defined columns
        foreach (var colReq in request.Columns)
        {
            ValidateLogicalName(colReq.LogicalName);

            var columnId = Guid.NewGuid();
            var physicalColName = _nameHasher.GenerateColumnName(tableId, colReq.LogicalName);
            var nativeType = TypeMapper.ToNativeType(colReq.DataType);

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
                IsActive = true
            };

            columns.Add(column);
            columnDefinitions.Add(ColumnDefinition.FromMetadata(column));
        }

        // Create table metadata
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
            Columns = columns
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
                Columns = [new IndexColumnInfo { ColumnId = tenantColumn.ColumnId, PhysicalName = tenantColumn.PhysicalName }],
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
                    Columns = [new IndexColumnInfo { ColumnId = col.ColumnId, PhysicalName = col.PhysicalName }],
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
            Columns = columns
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
            ValidateLogicalName(request.LogicalName);

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
        ValidateLogicalName(request.LogicalName);

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
        var physicalColName = _nameHasher.GenerateColumnName(request.TableId, request.LogicalName);
        var nativeType = TypeMapper.ToNativeType(request.DataType);
        var ordinalPosition = await _repository.GetNextOrdinalPositionAsync(request.TableId, cancellationToken);

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
            IsActive = true
        };

        var columnDef = ColumnDefinition.FromMetadata(column);

        // Execute DDL
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
                    Columns = [new IndexColumnInfo { ColumnId = columnId, PhysicalName = physicalColName }],
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
                DataType = request.DataType.ToString()
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
            ValidateLogicalName(request.LogicalName);

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
        ValidateLogicalName(request.LogicalName);

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
        ValidateLogicalName(request.LogicalName);

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
        var constraintName = $"fk_{sourceTable.PhysicalName}_{sourceColumn.PhysicalName}";

        var relation = new RelationMetadata
        {
            RelationId = relationId,
            TenantId = request.TenantId,
            LogicalName = request.LogicalName,
            SourceTableId = request.SourceTableId,
            SourceColumnId = request.SourceColumnId,
            TargetTableId = request.TargetTableId,
            TargetColumnId = request.TargetColumnId,
            RelationType = request.RelationType,
            OnDelete = request.OnDelete,
            IsActive = true
        };

        // Execute DDL
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
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
                TargetTable = targetTable.LogicalName
            }
        }, cancellationToken);

        return insertedRelation;
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

    private ColumnMetadata CreateSystemColumn(
        Guid tableId,
        string logicalName,
        MorphDataType dataType,
        int ordinalPosition,
        bool isPrimaryKey = false)
    {
        var columnId = Guid.NewGuid();
        var physicalName = _nameHasher.GenerateColumnName(tableId, logicalName);
        var nativeType = TypeMapper.ToNativeType(dataType);

        return new ColumnMetadata
        {
            ColumnId = columnId,
            TableId = tableId,
            LogicalName = logicalName,
            PhysicalName = physicalName,
            DataType = dataType,
            NativeType = nativeType,
            IsNullable = false,
            IsPrimaryKey = isPrimaryKey,
            OrdinalPosition = ordinalPosition,
            IsActive = true
        };
    }

    private static void ValidateLogicalName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new SchemaException("INVALID_NAME", "Logical name cannot be empty.");
        }

        if (name.Length > 255)
        {
            throw new SchemaException("INVALID_NAME", "Logical name cannot exceed 255 characters.");
        }

        if (name.StartsWith('_'))
        {
            throw new SchemaException("INVALID_NAME", "Logical name cannot start with underscore (reserved for system).");
        }
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
}
