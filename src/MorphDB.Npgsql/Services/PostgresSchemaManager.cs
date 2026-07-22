using System.Text.Json;
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
            request.ProjectId, request.LogicalName, cancellationToken: cancellationToken);
        if (existing is not null)
        {
            throw new DuplicateNameException("Table", request.LogicalName);
        }

        var tableId = Guid.NewGuid();
        var physicalTableName = _nameHasher.GenerateTableName(request.ProjectId, request.LogicalName);

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

        // project_id: Internal column for multi-tenancy (not exposed to API)
        var projectColumn = CreateSystemColumn(tableId, SystemColumns.ProjectId, MorphDataType.Uuid, ordinal++);
        columns.Add(projectColumn);
        columnDefinitions.Add(ColumnDefinition.FromMetadata(projectColumn));

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

        if (sysOpts.RowStateEnabled)
        {
            var rowStateColumn = CreateSystemColumn(tableId, SystemColumns.RowState, MorphDataType.Text, ordinal++, isNullable: true);
            columns.Add(rowStateColumn);
            columnDefinitions.Add(ColumnDefinition.FromMetadata(rowStateColumn) with { DefaultExpression = "'valid'" });

            var rowErrorsColumn = CreateSystemColumn(tableId, SystemColumns.RowErrors, MorphDataType.Json, ordinal++, isNullable: true);
            columns.Add(rowErrorsColumn);
            columnDefinitions.Add(ColumnDefinition.FromMetadata(rowErrorsColumn));
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
                CheckExpression = colReq.CheckExpression,
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

        // Translate logical names in CHECK expressions to physical names for DDL
        var logicalToPhysicalMap = columns
            .Where(c => !c.PhysicalName.StartsWith("virtual_", StringComparison.Ordinal))
            .ToDictionary(c => c.LogicalName, c => c.PhysicalName);

        for (var i = 0; i < columnDefinitions.Count; i++)
        {
            if (columnDefinitions[i].CheckExpression is not null)
            {
                columnDefinitions[i] = columnDefinitions[i] with
                {
                    CheckExpression = DdlBuilder.TranslateCheckExpression(
                        columnDefinitions[i].CheckExpression, logicalToPhysicalMap)
                };
            }
        }

        // Serialize system column options into descriptor for persistence
        var descriptor = BuildSystemColumnsDescriptor(sysOpts);

        // Create table metadata with system column options
        var tableMetadata = new TableMetadata
        {
            TableId = tableId,
            ProjectId = request.ProjectId,
            LogicalName = request.LogicalName,
            PhysicalName = physicalTableName,
            SchemaVersion = 1,
            Descriptor = descriptor,
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
            SourceTrackingEnabled = sysOpts.SourceTrackingEnabled,
            RowStateEnabled = sysOpts.RowStateEnabled
        };

        // Execute DDL and insert metadata in a transaction
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Create physical table
            var createTableSql = DdlBuilder.BuildCreateTable(physicalTableName, columnDefinitions);
            await connection.ExecuteAsync(new CommandDefinition(createTableSql, transaction: transaction, cancellationToken: cancellationToken));

            // Create project_id index for RLS performance
            var projectIndexSql = DdlBuilder.BuildCreateIndex(new IndexDefinition
            {
                PhysicalName = $"idx_{physicalTableName}_project",
                TablePhysicalName = physicalTableName,
                Columns = [new IndexColumnInfo { ColumnId = projectColumn.ColumnId, LogicalName = projectColumn.LogicalName, PhysicalName = projectColumn.PhysicalName }],
                IndexType = IndexType.BTree
            });
            await connection.ExecuteAsync(new CommandDefinition(projectIndexSql, transaction: transaction, cancellationToken: cancellationToken));

            // Create unique/indexed columns
            foreach (var col in columns.Where(c => c.IsUnique && !c.IsPrimaryKey))
            {
                var uniqueConstraintSql = DdlBuilder.BuildAddUniqueConstraint(
                    physicalTableName,
                    $"uq_{physicalTableName}_{col.PhysicalName}",
                    col.PhysicalName);
                await connection.ExecuteAsync(new CommandDefinition(uniqueConstraintSql, transaction: transaction, cancellationToken: cancellationToken));
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
                await connection.ExecuteAsync(new CommandDefinition(indexSql, transaction: transaction, cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new SchemaException("DDL_EXECUTION_FAILED", $"DDL execution failed: {ex.MessageText}", ex);
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
            ProjectId = insertedTable.ProjectId,
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
            SourceTrackingEnabled = sysOpts.SourceTrackingEnabled,
            RowStateEnabled = sysOpts.RowStateEnabled
        };
    }

    public async Task<TableMetadata?> GetTableAsync(
        Guid projectId,
        string logicalName,
        CancellationToken cancellationToken = default)
    {
        return await _repository.GetTableByNameAsync(projectId, logicalName, includeColumns: true, cancellationToken);
    }

    public async Task<TableMetadata?> GetTableByIdAsync(
        Guid tableId,
        CancellationToken cancellationToken = default)
    {
        return await _repository.GetTableByIdAsync(tableId, includeColumns: true, cancellationToken);
    }

    public async Task<IReadOnlyList<TableMetadata>> ListTablesAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        return await _repository.ListTablesAsync(projectId, includeColumns: true, cancellationToken);
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
            var existing = await _repository.GetTableByNameAsync(table.ProjectId, request.LogicalName, cancellationToken: cancellationToken);
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
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(dropTableSql, cancellationToken: cancellationToken));
        }
        catch (PostgresException pg) when (pg.SqlState == PostgresErrorCodes.DependentObjectsStillExist)
        {
            // A relation pointing at this table is a foreign key on the other table, and the DROP
            // carries no CASCADE — deliberately, since tearing down another table's constraint is
            // not something a delete of this one should decide. The caller has to remove the
            // relation first, and needs to be told so: left alone this escaped as an opaque 500
            // naming a physical table they are not supposed to know exists.
            throw new SchemaException(
                "TABLE_HAS_DEPENDENTS",
                $"Table '{table.LogicalName}' cannot be deleted while other tables reference it. " +
                "Delete the relations that target it first.");
        }

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
            CheckExpression = request.CheckExpression,
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

            // Translate logical names in CHECK expression to physical names
            if (columnDef.CheckExpression is not null)
            {
                var logicalToPhysicalMap = table.Columns
                    .Where(c => c.IsActive && !c.PhysicalName.StartsWith("virtual_", StringComparison.Ordinal))
                    .ToDictionary(c => c.LogicalName, c => c.PhysicalName);
                logicalToPhysicalMap[column.LogicalName] = column.PhysicalName;

                columnDef = columnDef with
                {
                    CheckExpression = DdlBuilder.TranslateCheckExpression(
                        columnDef.CheckExpression, logicalToPhysicalMap)
                };
            }

            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                var addColumnSql = DdlBuilder.BuildAddColumn(table.PhysicalName, columnDef);
                await connection.ExecuteAsync(new CommandDefinition(addColumnSql, transaction: transaction, cancellationToken: cancellationToken));

                if (request.IsUnique)
                {
                    var uniqueSql = DdlBuilder.BuildAddUniqueConstraint(
                        table.PhysicalName,
                        $"uq_{table.PhysicalName}_{physicalColName}",
                        physicalColName);
                    await connection.ExecuteAsync(new CommandDefinition(uniqueSql, transaction: transaction, cancellationToken: cancellationToken));
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
                    await connection.ExecuteAsync(new CommandDefinition(indexSql, transaction: transaction, cancellationToken: cancellationToken));
                }

                await transaction.CommitAsync(cancellationToken);
            }
            catch (PostgresException ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw new SchemaException("DDL_EXECUTION_FAILED", $"DDL execution failed: {ex.MessageText}", ex);
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

        // System columns cannot have type/constraint changes
        if (column.IsSystemColumn &&
            (request.DataType.HasValue || request.IsNullable.HasValue || request.IsUnique.HasValue))
        {
            throw new ValidationException("SYSTEM_COLUMN", "System columns cannot be modified");
        }

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

        // Update simple metadata (logical name, default value)
        await _repository.UpdateColumnAsync(request.ColumnId, request.LogicalName, request.DefaultValue, cancellationToken);

        // Apply DDL changes for type/constraint modifications
        var hasDdlChanges = request.DataType.HasValue || request.IsNullable.HasValue ||
                            request.IsUnique.HasValue || request.CheckExpression is not null;

        if (hasDdlChanges)
        {
            var table = await _repository.GetTableByIdAsync(column.TableId, includeColumns: true, cancellationToken)
                ?? throw new TableNotFoundException(column.TableId.ToString());

            await ApplyColumnDdlChangesAsync(table, column, request, cancellationToken);

            // Update metadata for type/constraint changes
            string? newDataType = request.DataType?.ToString();
            string? newNativeType = request.DataType.HasValue
                ? TypeMapper.ToNativeType(request.DataType.Value) : null;

            await _repository.UpdateColumnMetadataAsync(
                request.ColumnId, newDataType, newNativeType,
                request.IsNullable, request.IsUnique,
                request.CheckExpression, cancellationToken);
        }

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
                NewLogicalName = request.LogicalName ?? column.LogicalName,
                OldDataType = column.DataType.ToString(),
                NewDataType = request.DataType?.ToString(),
                OldIsNullable = column.IsNullable,
                NewIsNullable = request.IsNullable,
                OldIsUnique = column.IsUnique,
                NewIsUnique = request.IsUnique,
                NewCheckExpression = request.CheckExpression
            }
        }, cancellationToken);

        return (await _repository.GetColumnByIdAsync(request.ColumnId, cancellationToken))!;
    }

    /// <summary>
    /// Applies physical DDL changes for column type/constraint modifications.
    /// </summary>
    private async Task ApplyColumnDdlChangesAsync(
        TableMetadata table,
        ColumnMetadata column,
        UpdateColumnRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Type change: ALTER COLUMN TYPE
            if (request.DataType.HasValue && request.DataType.Value != column.DataType)
            {
                var newNativeType = TypeMapper.ToNativeType(request.DataType.Value);
                if (!TypeMapper.IsTypeCastSafe(column.DataType, request.DataType.Value) && !request.ForceCast)
                {
                    throw new ValidationException("UNSAFE_TYPE_CAST",
                        $"Cannot safely convert column '{column.LogicalName}' from {column.DataType} to {request.DataType.Value}. " +
                        "Data loss may occur. Set ForceCast=true to attempt the conversion, or create a new column and migrate data manually.");
                }

                var alterTypeSql = DdlBuilder.BuildAlterColumnType(
                    table.PhysicalName, column.PhysicalName, newNativeType);
                await connection.ExecuteAsync(new CommandDefinition(
                    alterTypeSql, transaction: transaction, cancellationToken: cancellationToken));
            }

            // Unique constraint change
            if (request.IsUnique.HasValue && request.IsUnique.Value != column.IsUnique)
            {
                var constraintName = $"uq_{table.PhysicalName}_{column.PhysicalName}";
                if (request.IsUnique.Value)
                {
                    var addUniqueSql = DdlBuilder.BuildAddUniqueConstraint(
                        table.PhysicalName, constraintName, column.PhysicalName);
                    await connection.ExecuteAsync(new CommandDefinition(
                        addUniqueSql, transaction: transaction, cancellationToken: cancellationToken));
                }
                else
                {
                    var dropUniqueSql = DdlBuilder.BuildDropUniqueConstraint(
                        table.PhysicalName, constraintName);
                    await connection.ExecuteAsync(new CommandDefinition(
                        dropUniqueSql, transaction: transaction, cancellationToken: cancellationToken));
                }
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new SchemaException("DDL_EXECUTION_FAILED",
                $"Column modification DDL failed: {ex.MessageText}", ex);
        }
        catch (ValidationException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ColumnMetadata> RenameColumnAsync(
        RenameColumnRequest request,
        CancellationToken cancellationToken = default)
    {
        LogicalNameValidator.ValidateColumnName(request.NewLogicalName);

        var column = await _repository.GetColumnByIdAsync(request.ColumnId, cancellationToken)
            ?? throw new ColumnNotFoundException("unknown", request.ColumnId.ToString());

        var table = await _repository.GetTableByIdAsync(column.TableId, includeColumns: true, cancellationToken)
            ?? throw new TableNotFoundException(column.TableId.ToString());

        // Check for duplicate column name
        if (table.Columns.Any(c => c.ColumnId != request.ColumnId &&
            c.LogicalName.Equals(request.NewLogicalName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DuplicateNameException("Column", request.NewLogicalName);
        }

        // Optimistic concurrency check
        var currentVersion = await _repository.GetCurrentVersionAsync(column.TableId, cancellationToken);
        if (currentVersion != request.ExpectedVersion)
        {
            throw new SchemaVersionConflictException(request.ExpectedVersion, currentVersion);
        }

        // Acquire advisory lock
        await using var lockHandle = await _lockManager.AcquireDdlLockAsync(
            $"table:{column.TableId}",
            _options.LockTimeout,
            cancellationToken);

        // For non-system columns, generate new physical name and execute RENAME COLUMN DDL
        if (!column.IsSystemColumn && !column.PhysicalName.StartsWith("virtual_", StringComparison.Ordinal))
        {
            var newPhysicalName = _nameHasher.GenerateColumnName(column.TableId, request.NewLogicalName);

            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            var renameSql = DdlBuilder.BuildRenameColumn(table.PhysicalName, column.PhysicalName, newPhysicalName);
            await connection.ExecuteAsync(new CommandDefinition(renameSql, cancellationToken: cancellationToken));

            // Update metadata with new logical name and physical name
            await _repository.UpdateColumnAsync(request.ColumnId, request.NewLogicalName, null, cancellationToken);
            // Update physical name separately — need to update the physical_name column in metadata
            await UpdateColumnPhysicalNameAsync(request.ColumnId, newPhysicalName, cancellationToken);
        }
        else
        {
            // System columns or virtual columns: only update logical name in metadata
            await _repository.UpdateColumnAsync(request.ColumnId, request.NewLogicalName, null, cancellationToken);
        }

        await _repository.IncrementVersionAsync(column.TableId, cancellationToken);

        // Log change
        await _changeLogger.LogChangeAsync(new SchemaChangeEntry
        {
            TableId = column.TableId,
            Operation = SchemaOperation.RenameColumn,
            SchemaVersion = currentVersion + 1,
            Changes = new
            {
                ColumnId = request.ColumnId,
                OldLogicalName = column.LogicalName,
                NewLogicalName = request.NewLogicalName
            }
        }, cancellationToken);

        return (await _repository.GetColumnByIdAsync(request.ColumnId, cancellationToken))!;
    }

    private async Task UpdateColumnPhysicalNameAsync(
        Guid columnId,
        string newPhysicalName,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE morphdb._morph_columns
            SET physical_name = @PhysicalName
            WHERE column_id = @ColumnId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, new { ColumnId = columnId, PhysicalName = newPhysicalName }, cancellationToken: cancellationToken));
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
        await connection.ExecuteAsync(new CommandDefinition(dropColumnSql, cancellationToken: cancellationToken));

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
        await connection.ExecuteAsync(new CommandDefinition(createIndexSql, cancellationToken: cancellationToken));

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
        await connection.ExecuteAsync(new CommandDefinition(dropIndexSql, cancellationToken: cancellationToken));

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
                request.ProjectId,
                junctionTableName,
                sourceTable,
                targetTable,
                cancellationToken);

            junctionTableId = junctionTable.TableId;
        }

        var relation = new RelationMetadata
        {
            RelationId = relationId,
            ProjectId = request.ProjectId,
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
            await connection.ExecuteAsync(new CommandDefinition(addFkSql, cancellationToken: cancellationToken));
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
        Guid projectId,
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
            ProjectId = projectId,
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
        await connection.ExecuteAsync(new CommandDefinition(dropFkSql, cancellationToken: cancellationToken));

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

    private static JsonDocument BuildSystemColumnsDescriptor(SystemColumnOptions sysOpts)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            systemColumns = new
            {
                timestamps = true,
                versioning = sysOpts.VersioningEnabled,
                auditFields = sysOpts.AuditFieldsEnabled,
                softDelete = sysOpts.SoftDeleteEnabled,
                ownership = sysOpts.OwnershipEnabled,
                hierarchy = sysOpts.HierarchyEnabled,
                sourceTracking = sysOpts.SourceTrackingEnabled,
                rowState = sysOpts.RowStateEnabled
            }
        });
        return JsonDocument.Parse(json);
    }

    public async Task<BatchDdlResult> ExecuteBatchDdlAsync(
        BatchDdlRequest request,
        CancellationToken cancellationToken = default)
    {
        var table = await _repository.GetTableByIdAsync(request.TableId, includeColumns: true, cancellationToken)
            ?? throw new TableNotFoundException(request.TableId.ToString());

        // Optimistic concurrency check
        var currentVersion = await _repository.GetCurrentVersionAsync(request.TableId, cancellationToken);
        if (currentVersion != request.ExpectedVersion)
        {
            throw new SchemaVersionConflictException(request.ExpectedVersion, currentVersion);
        }

        var executedCount = 0;

        try
        {
            foreach (var op in request.Operations)
            {
                switch (op.Type.ToLowerInvariant())
                {
                    case "addcolumn" when op.AddColumn is not null:
                        var addReq = op.AddColumn with { TableId = request.TableId };
                        await AddColumnAsync(addReq, cancellationToken);
                        break;

                    case "updatecolumn" when op.UpdateColumn is not null:
                        await UpdateColumnAsync(op.UpdateColumn, cancellationToken);
                        break;

                    case "deletecolumn" when op.DeleteColumnId.HasValue:
                        await DeleteColumnAsync(op.DeleteColumnId.Value, cancellationToken);
                        break;

                    case "createindex" when op.CreateIndex is not null:
                        var idxReq = op.CreateIndex with { TableId = request.TableId };
                        await CreateIndexAsync(idxReq, cancellationToken);
                        break;

                    case "deleteindex" when op.DeleteIndexId.HasValue:
                        await DeleteIndexAsync(op.DeleteIndexId.Value, cancellationToken);
                        break;

                    case "createrelation" when op.CreateRelation is not null:
                        await CreateRelationAsync(op.CreateRelation, cancellationToken);
                        break;

                    case "deleterelation" when op.DeleteRelationId.HasValue:
                        await DeleteRelationAsync(op.DeleteRelationId.Value, cancellationToken);
                        break;

                    default:
                        throw new ValidationException("INVALID_OPERATION",
                            $"Unknown or incomplete batch operation type: '{op.Type}'");
                }

                executedCount++;
            }

            var newVersion = await _repository.GetCurrentVersionAsync(request.TableId, cancellationToken);
            return new BatchDdlResult
            {
                Success = true,
                OperationsExecuted = executedCount,
                NewSchemaVersion = newVersion
            };
        }
        catch (Exception ex) when (ex is not ValidationException and not SchemaException)
        {
            throw new SchemaException("BATCH_DDL_FAILED",
                $"Batch DDL failed after {executedCount}/{request.Operations.Count} operations: {ex.Message}", ex);
        }
    }
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
