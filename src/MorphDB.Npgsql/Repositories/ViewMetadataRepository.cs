using System.Text.Json;
using Dapper;
using MorphDB.Core.Models;
using Npgsql;

namespace MorphDB.Npgsql.Repositories;

/// <summary>
/// Dapper-based implementation of view metadata repository.
/// </summary>
public sealed class ViewMetadataRepository : IViewMetadataRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public ViewMetadataRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<ViewMetadata> InsertViewAsync(
        ViewMetadata view,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO morphdb._morph_views
                (view_id, tenant_id, logical_name, physical_name, definition,
                 is_materialized, refresh_policy, refresh_schedule, descriptor)
            VALUES
                (@ViewId, @TenantId, @LogicalName, @PhysicalName, @Definition::jsonb,
                 @IsMaterialized, @RefreshPolicy, @RefreshSchedule, @Descriptor::jsonb)
            RETURNING view_id, tenant_id, logical_name, physical_name, definition,
                      is_materialized, refresh_policy, refresh_schedule, last_refreshed_at,
                      is_stale, descriptor, is_active, created_at, updated_at
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await connection.QuerySingleAsync<ViewRow>(sql, new
        {
            view.ViewId,
            view.TenantId,
            view.LogicalName,
            view.PhysicalName,
            Definition = JsonSerializer.Serialize(view.Definition),
            view.IsMaterialized,
            RefreshPolicy = view.RefreshPolicy.ToString(),
            view.RefreshSchedule,
            Descriptor = view.Descriptor?.RootElement.GetRawText()
        });

        return MapToViewMetadata(result);
    }

    public async Task<ViewMetadata?> GetViewByIdAsync(
        Guid viewId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT view_id, tenant_id, logical_name, physical_name, definition,
                   is_materialized, refresh_policy, refresh_schedule, last_refreshed_at,
                   is_stale, descriptor, is_active, created_at, updated_at
            FROM morphdb._morph_views
            WHERE view_id = @ViewId AND is_active = true
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ViewRow>(sql, new { ViewId = viewId });

        if (row == null)
            return null;

        var view = MapToViewMetadata(row);
        var columns = await GetViewColumnsAsync(viewId, cancellationToken);
        return view with { Columns = columns };
    }

    public async Task<ViewMetadata?> GetViewByNameAsync(
        Guid tenantId,
        string logicalName,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT view_id, tenant_id, logical_name, physical_name, definition,
                   is_materialized, refresh_policy, refresh_schedule, last_refreshed_at,
                   is_stale, descriptor, is_active, created_at, updated_at
            FROM morphdb._morph_views
            WHERE tenant_id = @TenantId AND logical_name = @LogicalName AND is_active = true
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ViewRow>(
            sql, new { TenantId = tenantId, LogicalName = logicalName });

        if (row == null)
            return null;

        var view = MapToViewMetadata(row);
        var columns = await GetViewColumnsAsync(view.ViewId, cancellationToken);
        return view with { Columns = columns };
    }

    public async Task<IReadOnlyList<ViewMetadata>> ListViewsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT view_id, tenant_id, logical_name, physical_name, definition,
                   is_materialized, refresh_policy, refresh_schedule, last_refreshed_at,
                   is_stale, descriptor, is_active, created_at, updated_at
            FROM morphdb._morph_views
            WHERE tenant_id = @TenantId AND is_active = true
            ORDER BY created_at DESC
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ViewRow>(sql, new { TenantId = tenantId });

        var views = new List<ViewMetadata>();
        foreach (var row in rows)
        {
            var view = MapToViewMetadata(row);
            var columns = await GetViewColumnsAsync(view.ViewId, cancellationToken);
            views.Add(view with { Columns = columns });
        }

        return views;
    }

    public async Task UpdateViewAsync(
        Guid viewId,
        string? logicalName,
        ViewDefinition? definition,
        MaterializedViewRefreshPolicy? refreshPolicy,
        string? refreshSchedule,
        string? description,
        CancellationToken cancellationToken = default)
    {
        var updates = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("ViewId", viewId);

        if (logicalName != null)
        {
            updates.Add("logical_name = @LogicalName");
            parameters.Add("LogicalName", logicalName);
        }

        if (definition != null)
        {
            updates.Add("definition = @Definition::jsonb");
            parameters.Add("Definition", JsonSerializer.Serialize(definition));
        }

        if (refreshPolicy != null)
        {
            updates.Add("refresh_policy = @RefreshPolicy");
            parameters.Add("RefreshPolicy", refreshPolicy.Value.ToString());
        }

        if (refreshSchedule != null)
        {
            updates.Add("refresh_schedule = @RefreshSchedule");
            parameters.Add("RefreshSchedule", refreshSchedule);
        }

        if (description != null)
        {
            updates.Add("descriptor = jsonb_set(COALESCE(descriptor, '{}'), '{description}', to_jsonb(@Description::text))");
            parameters.Add("Description", description);
        }

        updates.Add("updated_at = NOW()");

        var sql = $"""
            UPDATE morphdb._morph_views
            SET {string.Join(", ", updates)}
            WHERE view_id = @ViewId AND is_active = true
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, parameters);
    }

    public async Task UpdateLastRefreshAsync(
        Guid viewId,
        DateTimeOffset refreshedAt,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE morphdb._morph_views
            SET last_refreshed_at = @RefreshedAt,
                is_stale = false,
                updated_at = NOW()
            WHERE view_id = @ViewId AND is_active = true
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { ViewId = viewId, RefreshedAt = refreshedAt });
    }

    public async Task UpdateStaleStatusAsync(
        Guid viewId,
        bool isStale,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE morphdb._morph_views
            SET is_stale = @IsStale,
                updated_at = NOW()
            WHERE view_id = @ViewId AND is_active = true
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { ViewId = viewId, IsStale = isStale });
    }

    public async Task SoftDeleteViewAsync(
        Guid viewId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE morphdb._morph_views
            SET is_active = false,
                updated_at = NOW()
            WHERE view_id = @ViewId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { ViewId = viewId });
    }

    public async Task<ViewColumnMetadata> InsertViewColumnAsync(
        ViewColumnMetadata column,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO morphdb._morph_view_columns
                (column_id, view_id, logical_name, data_type, is_computed, expression, ordinal_position)
            VALUES
                (@ColumnId, @ViewId, @LogicalName, @DataType, @IsComputed, @Expression, @OrdinalPosition)
            RETURNING column_id, view_id, logical_name, data_type, is_computed, expression, ordinal_position
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await connection.QuerySingleAsync<ViewColumnRow>(sql, new
        {
            column.ColumnId,
            column.ViewId,
            column.LogicalName,
            DataType = column.DataType.ToString(),
            column.IsComputed,
            column.Expression,
            column.OrdinalPosition
        });

        return MapToViewColumnMetadata(result);
    }

    public async Task<IReadOnlyList<ViewColumnMetadata>> GetViewColumnsAsync(
        Guid viewId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT column_id, view_id, logical_name, data_type, is_computed, expression, ordinal_position
            FROM morphdb._morph_view_columns
            WHERE view_id = @ViewId
            ORDER BY ordinal_position
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ViewColumnRow>(sql, new { ViewId = viewId });

        return rows.Select(MapToViewColumnMetadata).ToList();
    }

    private static ViewMetadata MapToViewMetadata(ViewRow row)
    {
        ViewDefinition definition;
        try
        {
            definition = JsonSerializer.Deserialize<ViewDefinition>(row.definition)
                ?? throw new InvalidOperationException("Failed to deserialize view definition");
        }
        catch
        {
            definition = new ViewDefinition { BaseTable = "unknown" };
        }

        return new ViewMetadata
        {
            ViewId = row.view_id,
            TenantId = row.tenant_id,
            LogicalName = row.logical_name,
            PhysicalName = row.physical_name,
            Definition = definition,
            IsMaterialized = row.is_materialized,
            RefreshPolicy = Enum.TryParse<MaterializedViewRefreshPolicy>(row.refresh_policy, out var policy)
                ? policy : MaterializedViewRefreshPolicy.OnDemand,
            RefreshSchedule = row.refresh_schedule,
            LastRefreshedAt = row.last_refreshed_at,
            IsStale = row.is_stale,
            Descriptor = row.descriptor != null ? JsonDocument.Parse(row.descriptor) : null,
            CreatedAt = row.created_at,
            UpdatedAt = row.updated_at,
            IsActive = row.is_active
        };
    }

    private static ViewColumnMetadata MapToViewColumnMetadata(ViewColumnRow row)
    {
        return new ViewColumnMetadata
        {
            ColumnId = row.column_id,
            ViewId = row.view_id,
            LogicalName = row.logical_name,
            DataType = Enum.TryParse<MorphDataType>(row.data_type, out var dt) ? dt : MorphDataType.Text,
            IsComputed = row.is_computed,
            Expression = row.expression,
            OrdinalPosition = row.ordinal_position
        };
    }

    #pragma warning disable IDE1006 // Naming convention for Dapper mapping
    private sealed class ViewRow
    {
        public Guid view_id { get; set; }
        public Guid tenant_id { get; set; }
        public string logical_name { get; set; } = "";
        public string physical_name { get; set; } = "";
        public string definition { get; set; } = "";
        public bool is_materialized { get; set; }
        public string refresh_policy { get; set; } = "";
        public string? refresh_schedule { get; set; }
        public DateTimeOffset? last_refreshed_at { get; set; }
        public bool is_stale { get; set; }
        public string? descriptor { get; set; }
        public bool is_active { get; set; }
        public DateTimeOffset created_at { get; set; }
        public DateTimeOffset updated_at { get; set; }
    }

    private sealed class ViewColumnRow
    {
        public Guid column_id { get; set; }
        public Guid view_id { get; set; }
        public string logical_name { get; set; } = "";
        public string data_type { get; set; } = "";
        public bool is_computed { get; set; }
        public string? expression { get; set; }
        public int ordinal_position { get; set; }
    }
    #pragma warning restore IDE1006
}
