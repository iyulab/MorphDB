using System.Text.Json;
using Dapper;
using Npgsql;

namespace MorphDB.Npgsql.Services;

/// <summary>
/// Logs schema changes to the _morph_changelog table.
/// </summary>
public sealed class ChangeLogger : IChangeLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly NpgsqlDataSource _dataSource;

    public ChangeLogger(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task LogChangeAsync(
        SchemaChangeEntry entry,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO morphdb._morph_changelog
                (change_id, table_id, operation, schema_version, changes, performed_by, performed_at)
            VALUES
                (@ChangeId, @TableId, @Operation, @SchemaVersion, @Changes::jsonb, @PerformedBy, @PerformedAt)
            """;

        var changesJson = JsonSerializer.Serialize(entry.Changes, JsonOptions);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new
        {
            entry.ChangeId,
            entry.TableId,
            Operation = entry.Operation.ToString(),
            entry.SchemaVersion,
            Changes = changesJson,
            entry.PerformedBy,
            entry.PerformedAt
        });
    }

    public async Task<IReadOnlyList<SchemaChangeEntry>> GetHistoryAsync(
        Guid tableId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT change_id, table_id, operation, schema_version, changes, performed_by, performed_at
            FROM morphdb._morph_changelog
            WHERE table_id = @TableId
            ORDER BY performed_at DESC
            LIMIT @Limit
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ChangeLogRow>(sql, new { TableId = tableId, Limit = limit });

        return rows.Select(MapToEntry).ToList();
    }

    private static SchemaChangeEntry MapToEntry(ChangeLogRow row) => new()
    {
        ChangeId = row.change_id,
        TableId = row.table_id,
        Operation = Enum.Parse<SchemaOperation>(row.operation, ignoreCase: true),
        SchemaVersion = row.schema_version,
        Changes = JsonSerializer.Deserialize<object>(row.changes) ?? new { },
        PerformedBy = row.performed_by,
        PerformedAt = row.performed_at
    };

    private sealed record ChangeLogRow(
        Guid change_id,
        Guid table_id,
        string operation,
        int schema_version,
        string changes,
        string? performed_by,
        DateTimeOffset performed_at);
}
