using GreenDonut;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Service.Services;

namespace MorphDB.Service.GraphQL;

/// <summary>
/// DataLoader for fetching table metadata by name.
/// </summary>
public sealed class TableByNameDataLoader : BatchDataLoader<string, TableMetadata?>
{
    private readonly ISchemaManager _schemaManager;
    private readonly ITenantContextAccessor _tenantAccessor;

    public TableByNameDataLoader(
        ISchemaManager schemaManager,
        ITenantContextAccessor tenantAccessor,
        IBatchScheduler batchScheduler,
        DataLoaderOptions? options = null)
        : base(batchScheduler, options ?? new DataLoaderOptions())
    {
        _schemaManager = schemaManager;
        _tenantAccessor = tenantAccessor;
    }

    protected override async Task<IReadOnlyDictionary<string, TableMetadata?>> LoadBatchAsync(
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantAccessor.TenantId;
        var tables = await _schemaManager.ListTablesAsync(tenantId, cancellationToken);

        var result = new Dictionary<string, TableMetadata?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            result[key] = tables.FirstOrDefault(t =>
                t.LogicalName.Equals(key, StringComparison.OrdinalIgnoreCase));
        }

        return result;
    }
}

/// <summary>
/// DataLoader for fetching table metadata by ID.
/// </summary>
public sealed class TableByIdDataLoader : BatchDataLoader<Guid, TableMetadata?>
{
    private readonly ISchemaManager _schemaManager;

    public TableByIdDataLoader(
        ISchemaManager schemaManager,
        IBatchScheduler batchScheduler,
        DataLoaderOptions? options = null)
        : base(batchScheduler, options ?? new DataLoaderOptions())
    {
        _schemaManager = schemaManager;
    }

    protected override async Task<IReadOnlyDictionary<Guid, TableMetadata?>> LoadBatchAsync(
        IReadOnlyList<Guid> keys,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, TableMetadata?>();

        // Batch fetch all tables by ID
        var tasks = keys.Select(async id =>
        {
            var table = await _schemaManager.GetTableByIdAsync(id, cancellationToken);
            return (id, table);
        });

        var results = await Task.WhenAll(tasks);
        foreach (var (id, table) in results)
        {
            result[id] = table;
        }

        return result;
    }
}

/// <summary>
/// DataLoader for fetching records by ID from a specific table.
/// </summary>
public sealed class RecordByIdDataLoader : BatchDataLoader<RecordKey, IDictionary<string, object?>?>
{
    private readonly IMorphDataService _dataService;
    private readonly ITenantContextAccessor _tenantAccessor;

    public RecordByIdDataLoader(
        IMorphDataService dataService,
        ITenantContextAccessor tenantAccessor,
        IBatchScheduler batchScheduler,
        DataLoaderOptions? options = null)
        : base(batchScheduler, options ?? new DataLoaderOptions())
    {
        _dataService = dataService;
        _tenantAccessor = tenantAccessor;
    }

    protected override async Task<IReadOnlyDictionary<RecordKey, IDictionary<string, object?>?>> LoadBatchAsync(
        IReadOnlyList<RecordKey> keys,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantAccessor.TenantId;
        var result = new Dictionary<RecordKey, IDictionary<string, object?>?>();

        // Group by table for efficient batching
        var groupedByTable = keys.GroupBy(k => k.TableName);

        foreach (var group in groupedByTable)
        {
            var tableName = group.Key;
            var ids = group.Select(k => k.RecordId).ToList();

            // Fetch records for this table
            var records = await _dataService.Query(tenantId)
                .From(tableName)
                .SelectAll()
                .WhereIn("id", ids.Cast<object>())
                .ToListAsync(cancellationToken);

            // Map results
            foreach (var key in group)
            {
                var record = records.FirstOrDefault(r =>
                    r.ContainsKey("id") &&
                    r["id"] is Guid id &&
                    id == key.RecordId);
                result[key] = record;
            }
        }

        return result;
    }
}

/// <summary>
/// Key for identifying a record across tables.
/// </summary>
public readonly record struct RecordKey(string TableName, Guid RecordId);

/// <summary>
/// DataLoader for fetching related records via foreign key.
/// </summary>
public sealed class RelatedRecordsDataLoader : GroupedDataLoader<RelationKey, IDictionary<string, object?>>
{
    private readonly IMorphDataService _dataService;
    private readonly ITenantContextAccessor _tenantAccessor;

    public RelatedRecordsDataLoader(
        IMorphDataService dataService,
        ITenantContextAccessor tenantAccessor,
        IBatchScheduler batchScheduler,
        DataLoaderOptions? options = null)
        : base(batchScheduler, options ?? new DataLoaderOptions())
    {
        _dataService = dataService;
        _tenantAccessor = tenantAccessor;
    }

    protected override async Task<ILookup<RelationKey, IDictionary<string, object?>>> LoadGroupedBatchAsync(
        IReadOnlyList<RelationKey> keys,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantAccessor.TenantId;
        var results = new List<(RelationKey Key, IDictionary<string, object?> Record)>();

        // Group by relation for efficient batching
        var groupedByRelation = keys
            .GroupBy(k => (k.TargetTableName, k.ForeignKeyColumn));

        foreach (var group in groupedByRelation)
        {
            var tableName = group.Key.TargetTableName;
            var foreignKeyColumn = group.Key.ForeignKeyColumn;
            var sourceIds = group.Select(k => k.SourceRecordId).Distinct().Cast<object>().ToList();

            // Fetch all related records
            var records = await _dataService.Query(tenantId)
                .From(tableName)
                .SelectAll()
                .WhereIn(foreignKeyColumn, sourceIds)
                .ToListAsync(cancellationToken);

            // Map results to keys
            foreach (var record in records)
            {
                if (!record.TryGetValue(foreignKeyColumn, out var fkValue) || fkValue is not Guid sourceId)
                    continue;

                var matchingKey = group.FirstOrDefault(k => k.SourceRecordId == sourceId);
                if (!matchingKey.Equals(default))
                {
                    results.Add((matchingKey, record));
                }
            }
        }

        return results.ToLookup(x => x.Key, x => x.Record);
    }
}

/// <summary>
/// Key for identifying a relation query.
/// </summary>
public readonly record struct RelationKey(
    string TargetTableName,
    string ForeignKeyColumn,
    Guid SourceRecordId);
