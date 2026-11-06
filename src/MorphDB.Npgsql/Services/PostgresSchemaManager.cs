using System.Text.Json;
using Dapper;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Ddl;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Security;
using Npgsql;

namespace MorphDB.Npgsql.Services;

/// <summary>
/// PostgreSQL implementation of schema management with advisory lock protection.
/// </summary>
public sealed class PostgresSchemaManager : ISchemaManager
{
    private readonly MorphConnectionFactory _connections;
    private readonly IMetadataRepository _repository;
    private readonly IAdvisoryLockManager _lockManager;
    private readonly INameHasher _nameHasher;
    private readonly IChangeLogger _changeLogger;
    private readonly IProjectRepository _projectRepository;
    private readonly SchemaManagerOptions _options;

    private readonly ILookupResolver _lookupResolver;
    private readonly IRollupResolver _rollupResolver;
    private readonly IFormulaResolver _formulaResolver;

    private readonly MetadataCache? _metadataCache;

    public PostgresSchemaManager(
        MorphConnectionFactory connections,
        IMetadataRepository repository,
        IAdvisoryLockManager lockManager,
        INameHasher nameHasher,
        IChangeLogger changeLogger,
        IProjectRepository projectRepository,
        SchemaManagerOptions? options = null,
        MetadataCache? metadataCache = null)
    {
        _metadataCache = metadataCache;
        // The resolvers a read computes derived columns with — a declaration is checked by the same.
        _lookupResolver = new PostgresLookupResolver(repository);
        _rollupResolver = new PostgresRollupResolver(repository);
        _formulaResolver = new PostgresFormulaResolver();
        _connections = connections;
        _repository = repository;
        _lockManager = lockManager;
        _nameHasher = nameHasher;
        _changeLogger = changeLogger;
        _projectRepository = projectRepository;
        _options = options ?? new SchemaManagerOptions();
    }

    #region Table Operations

    public async Task<TableMetadata> CreateTableAsync(
        CreateTableRequest request,
        CancellationToken cancellationToken = default)
    {
        LogicalNameValidator.ValidateEntityName(request.LogicalName, "Table");

        var tableId = Guid.NewGuid();

        // Acquire advisory lock for DDL. It is held by name: the new table's id is known to nobody else, so a
        // lock on it would serialize nothing, and two requests for one name would build the same physical
        // table. The second waits here for the first and then finds the name taken.
        await using var lockHandle = await _lockManager.AcquireDdlLockAsync(
            $"table-name:{request.ProjectId:N}:{request.LogicalName}",
            _options.LockTimeout,
            cancellationToken);

        await EnsureTableNameFreeAsync(request.ProjectId, request.LogicalName, cancellationToken);

        var plan = PlanTable(request, tableId);
        return await InUnitAsync(
            (connection, transaction) => ApplyTableAsync(plan, connection, transaction, cancellationToken),
            cancellationToken);
    }

    private async Task EnsureTableNameFreeAsync(Guid projectId, string logicalName, CancellationToken cancellationToken)
    {
        var existing = await _repository.GetTableByNameAsync(projectId, logicalName, cancellationToken: cancellationToken);
        if (existing is not null)
        {
            throw new DuplicateNameException("Table", logicalName);
        }
    }

    /// <summary>
    /// What a table is made of -- its description, its columns and the definitions the DDL builds them from --
    /// worked out from the request alone, before anything is written.
    /// </summary>
    private sealed record TablePlan(
        TableMetadata Table,
        List<ColumnMetadata> Columns,
        List<ColumnDefinition> ColumnDefinitions,
        SystemColumnOptions SystemColumns);

    private TablePlan PlanTable(CreateTableRequest request, Guid tableId)
    {
        var physicalTableName = _nameHasher.GenerateTableName(request.ProjectId, request.LogicalName);

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
            var createdByColumn = CreateSystemColumn(tableId, SystemColumns.CreatedBy, MorphDataType.Text, ordinal++, isNullable: true);
            columns.Add(createdByColumn);
            columnDefinitions.Add(ColumnDefinition.FromMetadata(createdByColumn));

            var updatedByColumn = CreateSystemColumn(tableId, SystemColumns.UpdatedBy, MorphDataType.Text, ordinal++, isNullable: true);
            columns.Add(updatedByColumn);
            columnDefinitions.Add(ColumnDefinition.FromMetadata(updatedByColumn));
        }

        // Add Optional columns based on options
        if (sysOpts.SoftDeleteEnabled)
        {
            var deletedAtColumn = CreateSystemColumn(tableId, SystemColumns.DeletedAt, MorphDataType.DateTime, ordinal++, isNullable: true);
            columns.Add(deletedAtColumn);
            columnDefinitions.Add(ColumnDefinition.FromMetadata(deletedAtColumn));

            var deletedByColumn = CreateSystemColumn(tableId, SystemColumns.DeletedBy, MorphDataType.Text, ordinal++, isNullable: true);
            columns.Add(deletedByColumn);
            columnDefinitions.Add(ColumnDefinition.FromMetadata(deletedByColumn));
        }

        if (sysOpts.OwnershipEnabled)
        {
            var ownerColumn = CreateSystemColumn(tableId, SystemColumns.OwnerId, MorphDataType.Text, ordinal++, isNullable: true);
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

        // CHECK is a virtual constraint: the app-layer grammar is the single canon and nothing is
        // emitted into DDL (§3.10-C1). The declaration gate rejects what the evaluator cannot
        // enforce - a stored-but-unenforceable CHECK would constrain nothing, silently.
        foreach (var definition in columnDefinitions)
        {
            if (definition.CheckExpression is not null)
            {
                Infrastructure.CheckGrammar.EnsureSupported(definition.CheckExpression);
                Infrastructure.CheckGrammar.EnsureFieldsExist(definition.CheckExpression, columns.Select(c => c.LogicalName));
            }
        }

        // Serialize system column options into descriptor for persistence
        TableMetadataLimits.EnsureValid(request.Metadata);
        var descriptor = BuildDescriptor(sysOpts, request.Metadata);

        // Create table metadata with system column options
        var tableMetadata = new TableMetadata
        {
            TableId = tableId,
            ProjectId = request.ProjectId,
            LogicalName = request.LogicalName,
            PhysicalName = physicalTableName,
            SchemaVersion = 1,
            Descriptor = descriptor,
            Metadata = request.Metadata is { } given ? new Dictionary<string, string>(given, StringComparer.Ordinal) : new Dictionary<string, string>(),
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

        return new TablePlan(tableMetadata, columns, columnDefinitions, sysOpts);
    }

    /// <summary>
    /// Builds the physical table and writes everything that describes it, on the transaction given: the
    /// table does not exist for anyone until the description does, and neither exists if either fails.
    /// </summary>
    private async Task<TableMetadata> ApplyTableAsync(
        TablePlan plan,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var (tableMetadata, columns, columnDefinitions, sysOpts) = plan;
        var physicalTableName = tableMetadata.PhysicalName;
        var projectColumn = columns.First(c => c.LogicalName == SystemColumns.ProjectId);

        // Create physical table
        await ExecuteDdlAsync(connection, transaction, DdlBuilder.BuildCreateTable(physicalTableName, columnDefinitions), cancellationToken);

        // Create project_id index for RLS performance
        await ExecuteDdlAsync(connection, transaction, DdlBuilder.BuildCreateIndex(new IndexDefinition
        {
            PhysicalName = $"idx_{physicalTableName}_project",
            TablePhysicalName = physicalTableName,
            Columns = [new IndexColumnInfo { ColumnId = projectColumn.ColumnId, LogicalName = projectColumn.LogicalName, PhysicalName = projectColumn.PhysicalName }],
            IndexType = IndexType.BTree
        }), cancellationToken);

        // Create unique/indexed columns
        foreach (var col in columns.Where(c => c.IsUnique && !c.IsPrimaryKey))
        {
            await ExecuteDdlAsync(connection, transaction, DdlBuilder.BuildAddUniqueConstraint(
                physicalTableName,
                $"uq_{physicalTableName}_{col.PhysicalName}",
                col.PhysicalName,
                overActiveRowsOnly: sysOpts.SoftDeleteEnabled), cancellationToken);
        }

        foreach (var col in columns.Where(c => c.IsIndexed && !c.IsPrimaryKey && !c.IsUnique))
        {
            await ExecuteDdlAsync(connection, transaction, DdlBuilder.BuildCreateIndex(new IndexDefinition
            {
                PhysicalName = $"idx_{physicalTableName}_{col.PhysicalName}",
                TablePhysicalName = physicalTableName,
                Columns = [new IndexColumnInfo { ColumnId = col.ColumnId, LogicalName = col.LogicalName, PhysicalName = col.PhysicalName }],
                IndexType = TypeMapper.GetRecommendedIndexType(col.DataType)
            }), cancellationToken);
        }

        // Lookup, rollup and formula columns are checked by planning the read that computes them,
        // before the table exists for anyone: one that cannot run refuses the whole declaration.
        await Query.TableSource.VerifyAsync(
            connection, transaction, tableMetadata, _lookupResolver, _rollupResolver, _formulaResolver, cancellationToken);

        // Insert metadata
        var insertedTable = await _repository.InsertTableAsync(tableMetadata, transaction, cancellationToken);
        foreach (var column in columns)
        {
            await _repository.InsertColumnAsync(column, transaction, cancellationToken);
        }

        // Log change
        await _changeLogger.LogChangeAsync(new SchemaChangeEntry
        {
            TableId = tableMetadata.TableId,
            Operation = SchemaOperation.CreateTable,
            SchemaVersion = 1,
            Changes = new
            {
                LogicalName = tableMetadata.LogicalName,
                ColumnCount = columns.Count
            }
        }, transaction, cancellationToken);

        // The inserted row read back carries everything the table was created with (its system
        // column options are restored from the descriptor); only the columns are inserted separately.
        return insertedTable with { Columns = columns };
    }

    /// <summary>
    /// Runs a schema change as one transaction (<see cref="SchemaUnit"/>) and, once it has committed,
    /// empties what is kept in memory.
    /// </summary>
    private async Task<T> InUnitAsync<T>(
        Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> change,
        CancellationToken cancellationToken)
    {
        var result = await SchemaUnit.RunAsync(_connections, change, cancellationToken);

        // Committed: only now can anyone else see the change, so only now is what is kept in memory out of date.
        _repository.InvalidateCache();
        return result;
    }

    /// <inheritdoc cref="InUnitAsync{T}"/>
    private async Task InUnitAsync(
        Func<NpgsqlConnection, NpgsqlTransaction, Task> change,
        CancellationToken cancellationToken) =>
        await InUnitAsync<bool>(
            async (connection, transaction) =>
            {
                await change(connection, transaction);
                return true;
            },
            cancellationToken);

    /// <summary>
    /// One DDL statement of a schema change. What PostgreSQL refuses is reported as the change's failure;
    /// a deadlock or a serialization failure is left as it is for <see cref="InUnitAsync{T}"/> to run again.
    /// </summary>
    private static async Task ExecuteDdlAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(sql, transaction: transaction, cancellationToken: cancellationToken));
        }
        catch (PostgresException ex) when (ex.SqlState is not (PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure))
        {
            throw new SchemaException("DDL_EXECUTION_FAILED", $"DDL execution failed: {ex.MessageText}", ex);
        }
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

        // Acquire advisory lock. The version is checked under it: of two requests that saw the same version
        // the one that gets the lock second finds it moved, and is refused rather than applied on top.
        await using var lockHandle = await _lockManager.AcquireDdlLockAsync(
            $"table:{request.TableId}",
            _options.LockTimeout,
            cancellationToken);

        // Optimistic concurrency check
        var currentVersion = await _repository.GetCurrentVersionAsync(request.TableId, cancellationToken: cancellationToken);
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

        // The new name and the log entry are one change.
        await InUnitAsync(async (_, transaction) =>
        {
            await _repository.UpdateTableAsync(request.TableId, request.LogicalName, newVersion, transaction, cancellationToken);

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
            }, transaction, cancellationToken);
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

        var currentVersion = await _repository.GetCurrentVersionAsync(tableId, cancellationToken: cancellationToken);

        // The physical table, the table's description and the log entry are one change: a table whose
        // deletion cannot be written down is still there.
        await InUnitAsync(async (connection, transaction) =>
        {
            var dropTableSql = DdlBuilder.BuildDropTable(table.PhysicalName);
            try
            {
                await connection.ExecuteAsync(new CommandDefinition(dropTableSql, transaction: transaction, cancellationToken: cancellationToken));
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
            await _repository.SoftDeleteTableAsync(tableId, transaction, cancellationToken);

            // Log change
            await _changeLogger.LogChangeAsync(new SchemaChangeEntry
            {
                TableId = tableId,
                Operation = SchemaOperation.DeleteTable,
                SchemaVersion = currentVersion,
                Changes = new
                {
                    LogicalName = table.LogicalName
                }
            }, transaction, cancellationToken);
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

        // Optimistic concurrency check. Unlike an update, it is not repeated under the lock below: two columns
        // added at the same moment are both wanted, and a caller that read the version just before it
        // asked (as the REST layer does) must not be refused for the other's addition.
        var currentVersion = await _repository.GetCurrentVersionAsync(request.TableId, cancellationToken: cancellationToken);
        if (currentVersion != request.ExpectedVersion)
        {
            throw new SchemaVersionConflictException(request.ExpectedVersion, currentVersion);
        }

        // Acquire advisory lock
        await using var lockHandle = await _lockManager.AcquireDdlLockAsync(
            $"table:{request.TableId}",
            _options.LockTimeout,
            cancellationToken);

        // What the first read saw may be out of date: a request ahead of this one for the same table commits
        // before the lock is let go. The name is checked again against the table as it is now, so a retry
        // that overlaps its own first attempt is told the name is taken rather than what PostgreSQL said.
        table = await _repository.GetTableByIdAsync(request.TableId, includeColumns: true, cancellationToken)
            ?? throw new TableNotFoundException(request.TableId.ToString());
        if (table.Columns.Any(c => c.LogicalName.Equals(request.LogicalName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DuplicateNameException("Column", request.LogicalName);
        }

        // A policy that already uses the new name for a function or a keyword would read it as this
        // column once it exists — whatever kind of column it is. Held until the column is in
        // metadata, so a policy written meanwhile is checked against the table with the column.
        await using var policyConnection = await _connections.OpenSystemAsync(cancellationToken);
        await using var policyLock = await NativePolicyProjector.LockAsync(policyConnection, table, cancellationToken);
        NativePolicyProjector.EnsureNameUnused(
            await NativePolicyProjector.LoadStoredAsync(policyConnection, table.TableId, cancellationToken),
            request.LogicalName,
            $"Column '{request.LogicalName}' cannot be added to table '{table.LogicalName}'");

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

        // Only a physical column has DDL
        ColumnDefinition? columnDef = null;
        if (!isVirtualColumn)
        {
            columnDef = ColumnDefinition.FromMetadata(column);

            // Virtual constraint: declaration gate only, nothing reaches DDL (see CreateTableAsync).
            if (columnDef.CheckExpression is not null)
            {
                Infrastructure.CheckGrammar.EnsureSupported(columnDef.CheckExpression);
                Infrastructure.CheckGrammar.EnsureFieldsExist(
                    columnDef.CheckExpression, table.Columns.Select(c => c.LogicalName).Append(request.LogicalName));
            }
        }

        // The physical column, its description, the version the table moves to and the log entry are one
        // change: a column added whose description cannot be written is taken back with it.
        return await InUnitAsync(async (connection, transaction) =>
        {
            if (columnDef is not null)
            {
                await ExecuteDdlAsync(connection, transaction, DdlBuilder.BuildAddColumn(table.PhysicalName, columnDef), cancellationToken);

                if (request.IsUnique)
                {
                    await ExecuteDdlAsync(connection, transaction, DdlBuilder.BuildAddUniqueConstraint(
                        table.PhysicalName,
                        $"uq_{table.PhysicalName}_{physicalColName}",
                        physicalColName,
                        overActiveRowsOnly: table.SoftDeleteEnabled), cancellationToken);
                }

                if (request.IsIndexed && !request.IsUnique)
                {
                    await ExecuteDdlAsync(connection, transaction, DdlBuilder.BuildCreateIndex(new IndexDefinition
                    {
                        PhysicalName = $"idx_{table.PhysicalName}_{physicalColName}",
                        TablePhysicalName = table.PhysicalName,
                        Columns = [new IndexColumnInfo { ColumnId = columnId, LogicalName = request.LogicalName, PhysicalName = physicalColName }],
                        IndexType = TypeMapper.GetRecommendedIndexType(request.DataType)
                    }), cancellationToken);
                }
            }
            else
            {
                // Checked by planning the read that would compute it, with the column in place.
                var declared = table with { Columns = [.. table.Columns, column] };
                await Query.TableSource.VerifyAsync(
                    connection, transaction, declared, _lookupResolver, _rollupResolver, _formulaResolver, cancellationToken);
            }

            // Insert metadata and increment version
            var insertedColumn = await _repository.InsertColumnAsync(column, transaction, cancellationToken);
            await _repository.IncrementVersionAsync(request.TableId, transaction, cancellationToken);

            // Log change
            await _changeLogger.LogChangeAsync(new SchemaChangeEntry
            {
                TableId = request.TableId,
                Operation = SchemaOperation.AddColumn,
                SchemaVersion = currentVersion + 1,
                Changes = new
                {
                    ColumnId = insertedColumn.ColumnId,
                    column.LogicalName,
                    DataType = request.DataType.ToString(),
                    IsVirtual = isVirtualColumn
                }
            }, transaction, cancellationToken);

            return insertedColumn;
        }, cancellationToken);
    }

    private static bool IsPersonColumnCatchingUp(ColumnMetadata column, UpdateColumnRequest request) =>
        SystemColumns.RecordsAPerson(column.LogicalName)
        && column.DataType == MorphDataType.Uuid
        && request.DataType == MorphDataType.Text
        && request.IsNullable is null
        && request.IsUnique is null;

    public async Task<ColumnMetadata> UpdateColumnAsync(
        UpdateColumnRequest request,
        CancellationToken cancellationToken = default)
    {
        var column = await _repository.GetColumnByIdAsync(request.ColumnId, cancellationToken)
            ?? throw new ColumnNotFoundException("unknown", request.ColumnId.ToString());

        // System columns cannot have type/constraint changes. The one change they take is the one that lets a table made
        // before a person's id could be any string catch up with a table made now: a column that records a person,
        // holding a uuid, becomes text — nothing else about it changes.
        if (column.IsSystemColumn &&
            (request.DataType.HasValue || request.IsNullable.HasValue || request.IsUnique.HasValue) &&
            !IsPersonColumnCatchingUp(column, request))
        {
            throw new SchemaException("SYSTEM_COLUMN", "System columns cannot be modified");
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

        // Acquire advisory lock. The version is checked under it: of two requests that saw the same version
        // the one that gets the lock second finds it moved, and is refused rather than applied on top.
        await using var lockHandle = await _lockManager.AcquireDdlLockAsync(
            $"table:{column.TableId}",
            _options.LockTimeout,
            cancellationToken);

        // Optimistic concurrency check
        var currentVersion = await _repository.GetCurrentVersionAsync(column.TableId, cancellationToken: cancellationToken);
        if (currentVersion != request.ExpectedVersion)
        {
            throw new SchemaVersionConflictException(request.ExpectedVersion, currentVersion);
        }

        var renamesLogically = request.LogicalName is not null && request.LogicalName != column.LogicalName;
        var hasDdlChanges = request.DataType.HasValue || request.IsNullable.HasValue ||
                            request.IsUnique.HasValue || request.CheckExpression is not null;

        TableMetadata? table = null;
        if (renamesLogically || hasDdlChanges)
        {
            table = await _repository.GetTableByIdAsync(column.TableId, includeColumns: true, cancellationToken)
                ?? throw new TableNotFoundException(column.TableId.ToString());
        }

        // The same declaration gate a new column passes: a blank expression clears the check.
        if (hasDdlChanges && !string.IsNullOrWhiteSpace(request.CheckExpression))
        {
            Infrastructure.CheckGrammar.EnsureSupported(request.CheckExpression);
            Infrastructure.CheckGrammar.EnsureFieldsExist(
                request.CheckExpression,
                table!.Columns.Where(c => c.ColumnId != column.ColumnId).Select(c => c.LogicalName).Append(request.LogicalName ?? column.LogicalName));
        }

        // Whoever writes a policy reads the table's stored policies under this lock and writes them
        // back; without it, a policy written while the column is renamed could keep the old name. Held
        // until the rename is committed, on a connection of its own.
        await using var policyConnection = renamesLogically ? await _connections.OpenSystemAsync(cancellationToken) : null;
        await using var policyLock = policyConnection is null
            ? null
            : await NativePolicyProjector.LockAsync(policyConnection, table!, cancellationToken);

        // The name, the physical change, the description, the version and the log entry are one change: a
        // column whose update cannot be written down is as it was.
        PostgresException? ddlFailure = null;
        try
        {
            await InUnitAsync(async (connection, transaction) =>
            {
                // Update simple metadata (logical name, default value). The physical column keeps its name
                // here, so a new logical name is a change to metadata alone — and to every policy that
                // names the column.
                if (renamesLogically)
                {
                    await RenameColumnWithPoliciesAsync(
                        connection, transaction, table!, column, request.LogicalName!, newPhysicalName: null, request.DefaultValue, cancellationToken);
                }
                else
                {
                    await _repository.UpdateColumnAsync(request.ColumnId, request.LogicalName, request.DefaultValue, transaction, cancellationToken);
                }

                // Apply DDL changes for type/constraint modifications
                if (hasDdlChanges)
                {
                    try
                    {
                        await ApplyColumnDdlChangesAsync(connection, transaction, table!, column, request, cancellationToken);
                    }
                    catch (PostgresException ex) when (ex.SqlState is not (PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure))
                    {
                        ddlFailure = ex;
                        throw;
                    }

                    // Update metadata for type/constraint changes
                    string? newDataType = request.DataType?.ToString();
                    string? newNativeType = request.DataType.HasValue
                        ? TypeMapper.ToNativeType(request.DataType.Value) : null;

                    await _repository.UpdateColumnMetadataAsync(
                        request.ColumnId, newDataType, newNativeType,
                        request.IsNullable, request.IsUnique,
                        request.CheckExpression, transaction, cancellationToken);
                }

                await _repository.IncrementVersionAsync(column.TableId, transaction, cancellationToken);

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
                }, transaction, cancellationToken);
            }, cancellationToken);
        }
        catch (PostgresException ex) when (ReferenceEquals(ex, ddlFailure))
        {
            // The change has been rolled back, so the policies can be read again — on a connection of their own.
            // PostgreSQL will not change the type of a column a policy reads: the policy was
            // checked against the type it had. Which policy that is, is the caller's to know.
            if (ex.SqlState == PostgresErrorCodes.FeatureNotSupported)
            {
                await using var connection = await _connections.OpenSystemAsync(cancellationToken);
                if (await RowLevelSecurityRefusal.ForColumnAsync(connection, table!, column, "given another type", cancellationToken) is { } refusal)
                {
                    throw refusal;
                }
            }

            throw new SchemaException("DDL_EXECUTION_FAILED",
                $"Column modification DDL failed: {ex.MessageText}", ex);
        }

        if (renamesLogically)
        {
            _metadataCache?.Invalidate(table!.ProjectId);
        }

        return (await _repository.GetColumnByIdAsync(request.ColumnId, cancellationToken))!;
    }

    /// <summary>
    /// Applies physical DDL changes for column type/constraint modifications, on the transaction of the
    /// change they belong to. What PostgreSQL refuses is the caller's to translate once that change has
    /// been rolled back.
    /// </summary>
    private static async Task ApplyColumnDdlChangesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TableMetadata table,
        ColumnMetadata column,
        UpdateColumnRequest request,
        CancellationToken cancellationToken)
    {
        // Type change: ALTER COLUMN TYPE
        if (request.DataType.HasValue && request.DataType.Value != column.DataType)
        {
            var newNativeType = TypeMapper.ToNativeType(request.DataType.Value);
            if (!TypeMapper.IsTypeCastSafe(column.DataType, request.DataType.Value) && !request.ForceCast)
            {
                throw new SchemaException("UNSAFE_TYPE_CAST",
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
                    table.PhysicalName, constraintName, column.PhysicalName,
                    overActiveRowsOnly: table.SoftDeleteEnabled);
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

        // Acquire advisory lock. The version is checked under it: of two requests that saw the same version
        // the one that gets the lock second finds it moved, and is refused rather than applied on top.
        await using var lockHandle = await _lockManager.AcquireDdlLockAsync(
            $"table:{column.TableId}",
            _options.LockTimeout,
            cancellationToken);

        // Optimistic concurrency check
        var currentVersion = await _repository.GetCurrentVersionAsync(column.TableId, cancellationToken: cancellationToken);
        if (currentVersion != request.ExpectedVersion)
        {
            throw new SchemaVersionConflictException(request.ExpectedVersion, currentVersion);
        }

        // A stored column's physical name follows its logical one. A system or virtual column keeps
        // the physical name it has: only its logical name changes, in metadata.
        var newPhysicalName = !column.IsSystemColumn && !column.PhysicalName.StartsWith("virtual_", StringComparison.Ordinal)
            ? _nameHasher.GenerateColumnName(column.TableId, request.NewLogicalName)
            : null;

        // Whoever writes a policy reads the table's stored policies under this lock and writes them
        // back; without it, a policy written while the column is renamed could keep the old name. Held
        // until the rename is committed, on a connection of its own.
        await using var policyConnection = await _connections.OpenSystemAsync(cancellationToken);
        await using var policyLock = await NativePolicyProjector.LockAsync(policyConnection, table, cancellationToken);

        // The rename, the version and the log entry are one change.
        await InUnitAsync(async (connection, transaction) =>
        {
            await RenameColumnWithPoliciesAsync(
                connection, transaction, table, column, request.NewLogicalName, newPhysicalName, defaultValue: null, cancellationToken);

            await _repository.IncrementVersionAsync(column.TableId, transaction, cancellationToken);

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
            }, transaction, cancellationToken);
        }, cancellationToken);

        // Written on a connection of its own, not through the repository: the cached definitions are
        // emptied here, once the rename is committed.
        _metadataCache?.Invalidate(table.ProjectId);

        return (await _repository.GetColumnByIdAsync(request.ColumnId, cancellationToken))!;
    }

    /// <summary>
    /// Gives a column its new logical name — and, when <paramref name="newPhysicalName"/> is given,
    /// renames the physical column to it — and follows the change in every row-level security
    /// policy of the table, on the transaction given. A policy is written in logical column names, so a
    /// rename that left it behind would leave a policy nothing can translate any more. The caller holds
    /// the table's policy lock (<see cref="NativePolicyProjector.LockAsync"/>) until it has committed.
    /// </summary>
    private static async Task RenameColumnWithPoliciesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TableMetadata table,
        ColumnMetadata column,
        string newLogicalName,
        string? newPhysicalName,
        string? defaultValue,
        CancellationToken cancellationToken)
    {
        if (newPhysicalName is not null)
        {
            var renameSql = DdlBuilder.BuildRenameColumn(table.PhysicalName, column.PhysicalName, newPhysicalName);
            await connection.ExecuteAsync(new CommandDefinition(renameSql, transaction: transaction, cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE morphdb._morph_columns
            SET logical_name = @LogicalName,
                physical_name = COALESCE(@PhysicalName, physical_name),
                default_value = COALESCE(@DefaultValue, default_value)
            WHERE column_id = @ColumnId
            """,
            new { column.ColumnId, LogicalName = newLogicalName, PhysicalName = newPhysicalName, DefaultValue = defaultValue },
            transaction,
            cancellationToken: cancellationToken));

        await NativePolicyProjector.RenameColumnInStoredAsync(
            connection, transaction, table, column.LogicalName, newLogicalName, cancellationToken);
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

        var currentVersion = await _repository.GetCurrentVersionAsync(column.TableId, cancellationToken: cancellationToken);

        try
        {
            // The physical column, its description, the version and the log entry are one change: a column
            // whose deletion cannot be written down is still there.
            await InUnitAsync(async (connection, transaction) =>
            {
                var dropColumnSql = DdlBuilder.BuildDropColumn(table.PhysicalName, column.PhysicalName);
                await connection.ExecuteAsync(new CommandDefinition(dropColumnSql, transaction: transaction, cancellationToken: cancellationToken));

                // Soft delete metadata
                await _repository.SoftDeleteColumnAsync(columnId, transaction, cancellationToken);
                await _repository.IncrementVersionAsync(column.TableId, transaction, cancellationToken);

                // Log change
                await _changeLogger.LogChangeAsync(new SchemaChangeEntry
                {
                    TableId = column.TableId,
                    Operation = SchemaOperation.DeleteColumn,
                    SchemaVersion = currentVersion + 1,
                    Changes = new
                    {
                        ColumnId = columnId,
                        column.LogicalName
                    }
                }, transaction, cancellationToken);
            }, cancellationToken);
        }
        catch (PostgresException pg) when (pg.SqlState == PostgresErrorCodes.DependentObjectsStillExist)
        {
            // A policy that reads the column depends on it, and the DROP carries no CASCADE —
            // deliberately: removing a column must not quietly remove the rule that guarded the
            // table's rows. The caller changes or deletes the policy first, and is told which.
            // (The failed change has been rolled back; the policies are read on a connection of their own.)
            await using var connection = await _connections.OpenSystemAsync(cancellationToken);
            if (await RowLevelSecurityRefusal.ForColumnAsync(connection, table, column, "deleted", cancellationToken) is { } refusal)
            {
                throw refusal;
            }

            throw;
        }
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

        var indexColumns = ResolveIndexColumns(request, table);

        // Acquire advisory lock
        await using var lockHandle = await _lockManager.AcquireDdlLockAsync(
            $"table:{request.TableId}",
            _options.LockTimeout,
            cancellationToken);

        // The index's physical name follows its logical one, so a name that is taken would only be met as
        // PostgreSQL's own refusal. Checked under the lock, it also catches a request still in flight.
        if ((await _repository.GetIndexesByTableIdAsync(request.TableId, cancellationToken)).Any(i => i.LogicalName == request.LogicalName))
        {
            throw new DuplicateNameException("Index", request.LogicalName);
        }

        return await InUnitAsync(
            (connection, transaction) => CreateIndexInAsync(request, table, indexColumns, connection, transaction, cancellationToken),
            cancellationToken);
    }

    /// <summary>The columns an index is declared over, each of them a column of <paramref name="table"/>.</summary>
    private static List<IndexColumnInfo> ResolveIndexColumns(CreateIndexRequest request, TableMetadata table)
    {
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

        return indexColumns;
    }

    /// <summary>
    /// Builds an index and writes its description, on the transaction given. The table is passed in rather
    /// than read: it may be one the same transaction created, which nobody else can see yet.
    /// </summary>
    private async Task<IndexMetadata> CreateIndexInAsync(
        CreateIndexRequest request,
        TableMetadata table,
        IReadOnlyList<IndexColumnInfo> indexColumns,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
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
        var createIndexSql = DdlBuilder.BuildCreateIndex(new IndexDefinition
        {
            PhysicalName = physicalIndexName,
            TablePhysicalName = table.PhysicalName,
            Columns = indexColumns,
            IndexType = request.IndexType,
            IsUnique = request.IsUnique,
            WhereClause = TranslateIndexPredicate(request.WhereClause, table)
        });
        await DefinitionRefusal.AsInvalidExpressionAsync(
            "The index definition",
            "a predicate may use only the table's own columns, written as the table's own reads name them -- unquoted -- "
            + "and only functions PostgreSQL marks immutable.",
            () => connection.ExecuteAsync(new CommandDefinition(createIndexSql, transaction: transaction, cancellationToken: cancellationToken)));

        // Insert metadata and increment version
        var insertedIndex = await _repository.InsertIndexAsync(index, transaction, cancellationToken);
        await _repository.IncrementVersionAsync(request.TableId, transaction, cancellationToken);

        var newVersion = await _repository.GetCurrentVersionAsync(request.TableId, transaction, cancellationToken);

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
        }, transaction, cancellationToken);

        return insertedIndex;
    }

    /// <summary>
    /// An index predicate is written in the table's own column names, like a view expression or a policy;
    /// the DDL needs the names the table is stored under. A name that is no column of the table is left as
    /// written (it may be a keyword or a function), and PostgreSQL then refuses the definition.
    /// </summary>
    private static string? TranslateIndexPredicate(string? predicate, TableMetadata table) =>
        string.IsNullOrWhiteSpace(predicate)
            ? predicate
            : SqlIdentifierRewriter.Rewrite(predicate, token =>
                table.Columns.FirstOrDefault(c => c.LogicalName == token) is { } column
                    ? DdlBuilder.QuoteIdentifier(column.PhysicalName)
                    : token.StartsWith('_') ? DdlBuilder.QuoteIdentifier(token) : null);

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

        var currentVersion = await _repository.GetCurrentVersionAsync(index.TableId, cancellationToken: cancellationToken);

        // The physical index, its description, the version and the log entry are one change.
        await InUnitAsync(async (connection, transaction) =>
        {
            var dropIndexSql = DdlBuilder.BuildDropIndex(index.PhysicalName);
            await connection.ExecuteAsync(new CommandDefinition(dropIndexSql, transaction: transaction, cancellationToken: cancellationToken));

            // Soft delete metadata
            await _repository.SoftDeleteIndexAsync(indexId, transaction, cancellationToken);
            await _repository.IncrementVersionAsync(index.TableId, transaction, cancellationToken);

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
            }, transaction, cancellationToken);
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

        // A request that does not state enforcement takes the project's standing answer. This is
        // resolved once, here, and stored on the relation: the physical constraint below is decided
        // from the same value, and a project that changes its default later must not leave existing
        // relations claiming an enforcement the database no longer backs.
        var enforceOnWrite = request.EnforceOnWrite ?? await GetDefaultEnforceOnWriteAsync(
            request.ProjectId, cancellationToken);

        // Lock both tables, in one order whichever end each is — two requests relating the same pair
        // in opposite directions would otherwise each hold one lock and wait for the other — and a
        // table related to itself once: each lock is held by a connection of its own, so a second
        // lock on the same table would wait for the first.
        var (firstTable, secondTable) = request.SourceTableId.CompareTo(request.TargetTableId) <= 0
            ? (request.SourceTableId, request.TargetTableId)
            : (request.TargetTableId, request.SourceTableId);

        await using var firstLock = await _lockManager.AcquireDdlLockAsync(
            $"table:{firstTable}",
            _options.LockTimeout,
            cancellationToken);

        await using var secondLock = secondTable == firstTable
            ? null
            : await _lockManager.AcquireDdlLockAsync(
                $"table:{secondTable}",
                _options.LockTimeout,
                cancellationToken);

        // The foreign key's name follows its table and column, so a column carries one: a second enforced
        // relation on it would meet PostgreSQL's refusal of the name. Said here, under the locks, in the
        // caller's own words.
        if (request.RelationType != RelationType.ManyToMany && _options.CreatePhysicalForeignKeys && enforceOnWrite
            && (await _repository.GetRelationsByTableIdAsync(request.SourceTableId, cancellationToken)).Any(r =>
                r.SourceColumnId == request.SourceColumnId && r.RelationType != RelationType.ManyToMany && r.EnforceOnWrite))
        {
            throw new SchemaException(
                "DDL_EXECUTION_FAILED",
                $"Column '{sourceColumn.LogicalName}' of table '{sourceTable.LogicalName}' already has an enforced relation: " +
                "delete it before creating another on the same column.");
        }

        var relationId = Guid.NewGuid();

        // A junction table with its index, the foreign key, the relation's description, the version and the
        // log entry are one change: whatever is built before a failure is taken back with it.
        return await InUnitAsync(async (connection, transaction) =>
        {
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
                    connection,
                    transaction,
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
                // Whether the relation is checked on write is the caller's to say: these were pinned
                // to true here, so the request's values were discarded and every relation enforced
                // regardless.
                EnforceOnWrite = enforceOnWrite,
                VirtualCascade = request.VirtualCascade,
                IsActive = true
            };

            // For non-ManyToMany, create the physical FK. Referential integrity is enforced by the
            // database — app-layer validation runs ahead of it to translate a caller's mistake into a
            // 4xx, not to replace the backstop. The option below exists for deployments that manage
            // constraints out of band, not as the recommended shape.
            //
            // A relation that resolves to non-enforcing gets no physical constraint either. Without
            // this clause EnforceOnWrite would only turn off the layer that answers politely: the
            // database would still reject the write, so the option would appear to do nothing. It reads
            // the resolved value rather than the request, so a project default reaches both layers or
            // neither — the same switch that is otherwise only half thrown.
            if (request.RelationType != RelationType.ManyToMany
                && _options.CreatePhysicalForeignKeys
                && enforceOnWrite)
            {
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
                await connection.ExecuteAsync(new CommandDefinition(addFkSql, transaction: transaction, cancellationToken: cancellationToken));
            }

            // Insert metadata and increment version
            var insertedRelation = await _repository.InsertRelationAsync(relation, transaction, cancellationToken);
            await _repository.IncrementVersionAsync(request.SourceTableId, transaction, cancellationToken);

            var newVersion = await _repository.GetCurrentVersionAsync(request.SourceTableId, transaction, cancellationToken);

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
            }, transaction, cancellationToken);

            return insertedRelation;
        }, cancellationToken);
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
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
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

        LogicalNameValidator.ValidateEntityName(junctionTableRequest.LogicalName, "Table");
        await EnsureTableNameFreeAsync(projectId, junctionTableName, cancellationToken);

        // The junction table is made on the caller's transaction, so nobody can see it -- and nothing is
        // left of it -- unless the relation that needs it is made.
        var junctionTable = await ApplyTableAsync(PlanTable(junctionTableRequest, Guid.NewGuid()), connection, transaction, cancellationToken);

        // Create unique composite index on (source_id, target_id) to prevent duplicates
        var junctionSourceCol = junctionTable.Columns.First(c => c.LogicalName == $"{sourceTable.LogicalName}_id");
        var junctionTargetCol = junctionTable.Columns.First(c => c.LogicalName == $"{targetTable.LogicalName}_id");

        var pairIndex = new CreateIndexRequest
        {
            TableId = junctionTable.TableId,
            LogicalName = $"ux_{junctionTableName}_pair",
            ColumnIds = [junctionSourceCol.ColumnId, junctionTargetCol.ColumnId],
            IsUnique = true,
            IndexType = IndexType.BTree
        };
        await CreateIndexInAsync(pairIndex, junctionTable, ResolveIndexColumns(pairIndex, junctionTable), connection, transaction, cancellationToken);

        return junctionTable;
    }

    /// <summary>
    /// The project's standing answer for relations that do not declare their own enforcement.
    /// A project row that does not exist, or has no settings, enforces — the constitution's default
    /// applies to a project that never said anything, not just to one that said "enforce".
    /// </summary>
    private async Task<bool> GetDefaultEnforceOnWriteAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var project = await _projectRepository.GetByIdAsync(projectId, cancellationToken);
        return project?.Settings?.DefaultEnforceOnWrite ?? true;
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

        var currentVersion = await _repository.GetCurrentVersionAsync(relation.SourceTableId, cancellationToken: cancellationToken);

        // The foreign key, the relation's description, the version and the log entry are one change.
        await InUnitAsync(async (connection, transaction) =>
        {
            var constraintName = $"fk_{sourceTable.PhysicalName}_{sourceColumn.PhysicalName}";
            var dropFkSql = DdlBuilder.BuildDropForeignKey(sourceTable.PhysicalName, constraintName);
            await connection.ExecuteAsync(new CommandDefinition(dropFkSql, transaction: transaction, cancellationToken: cancellationToken));

            // Soft delete metadata
            await _repository.SoftDeleteRelationAsync(relationId, transaction, cancellationToken);
            await _repository.IncrementVersionAsync(relation.SourceTableId, transaction, cancellationToken);

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
            }, transaction, cancellationToken);
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

    private static JsonDocument BuildDescriptor(SystemColumnOptions sysOpts, IReadOnlyDictionary<string, string>? metadata)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            metadata = metadata is { Count: > 0 } ? metadata : null,
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
    /// When true (the default, and the supported shape), referential integrity is enforced by
    /// physical FK constraints, with app-layer validation ahead of them for clean error reporting.
    /// Setting this to false leaves only the app-layer check, which cannot hold under concurrency.
    /// Reachable only when embedding the library — configured through the AddMorphDbNpgsql
    /// callback; the server does not bind it from configuration, so a hosted instance always
    /// creates them.
    /// </summary>
    public bool CreatePhysicalForeignKeys { get; init; } = true;
}
