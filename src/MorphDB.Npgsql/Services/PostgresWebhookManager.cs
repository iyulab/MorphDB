using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using Npgsql;

namespace MorphDB.Npgsql.Services;

/// <summary>
/// PostgreSQL implementation of webhook management.
/// </summary>
public sealed class PostgresWebhookManager : IWebhookManager
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresWebhookManager(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<WebhookMetadata> CreateWebhookAsync(
        CreateWebhookRequest request,
        CancellationToken cancellationToken = default)
    {
        var webhookId = Guid.NewGuid();
        var secret = GenerateSecret();
        var now = DateTimeOffset.UtcNow;
        var events = request.Events.Select(e => e.ToString().ToLowerInvariant()).ToArray();

        const string sql = """
            INSERT INTO morphdb._morph_webhooks (
                webhook_id, tenant_id, table_id, logical_name, url, secret, events, filter, headers, created_at, updated_at
            ) VALUES (
                @WebhookId, @TenantId, @TableId, @LogicalName, @Url, @Secret, @Events, @Filter::jsonb, @Headers::jsonb, @CreatedAt, @UpdatedAt
            )
            RETURNING webhook_id, tenant_id, table_id, logical_name, url, secret, events, filter, headers, is_active, created_at, updated_at
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var result = await connection.QuerySingleAsync<WebhookRow>(sql, new
        {
            WebhookId = webhookId,
            request.TenantId,
            request.TableId,
            request.LogicalName,
            request.Url,
            Secret = secret,
            Events = events,
            Filter = request.Filter?.RootElement.GetRawText(),
            Headers = request.Headers?.RootElement.GetRawText(),
            CreatedAt = now,
            UpdatedAt = now
        });

        return MapToMetadata(result);
    }

    public async Task<WebhookMetadata?> GetWebhookAsync(
        Guid webhookId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT w.webhook_id, w.tenant_id, w.table_id, w.logical_name, w.url, w.secret, w.events,
                   w.filter, w.headers, w.is_active, w.created_at, w.updated_at, t.logical_name as table_logical_name
            FROM morphdb._morph_webhooks w
            JOIN morphdb._morph_tables t ON w.table_id = t.table_id
            WHERE w.webhook_id = @WebhookId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await connection.QuerySingleOrDefaultAsync<WebhookRow>(sql, new { WebhookId = webhookId });

        return result is null ? null : MapToMetadata(result);
    }

    public async Task<IReadOnlyList<WebhookMetadata>> ListWebhooksAsync(
        Guid tenantId,
        string? tableName = null,
        CancellationToken cancellationToken = default)
    {
        var sql = """
            SELECT w.webhook_id, w.tenant_id, w.table_id, w.logical_name, w.url, w.secret, w.events,
                   w.filter, w.headers, w.is_active, w.created_at, w.updated_at, t.logical_name as table_logical_name
            FROM morphdb._morph_webhooks w
            JOIN morphdb._morph_tables t ON w.table_id = t.table_id
            WHERE w.tenant_id = @TenantId
            """;

        if (!string.IsNullOrEmpty(tableName))
        {
            sql += " AND t.logical_name = @TableName";
        }

        sql += " ORDER BY w.created_at DESC";

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var results = await connection.QueryAsync<WebhookRow>(sql, new { TenantId = tenantId, TableName = tableName });

        return results.Select(MapToMetadata).ToList();
    }

    public async Task<WebhookMetadata> UpdateWebhookAsync(
        UpdateWebhookRequest request,
        CancellationToken cancellationToken = default)
    {
        var updates = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("WebhookId", request.WebhookId);

        if (request.Url is not null)
        {
            updates.Add("url = @Url");
            parameters.Add("Url", request.Url);
        }

        if (request.Events is not null)
        {
            updates.Add("events = @Events");
            parameters.Add("Events", request.Events.Select(e => e.ToString().ToLowerInvariant()).ToArray());
        }

        if (request.Filter is not null)
        {
            updates.Add("filter = @Filter::jsonb");
            parameters.Add("Filter", request.Filter.RootElement.GetRawText());
        }

        if (request.Headers is not null)
        {
            updates.Add("headers = @Headers::jsonb");
            parameters.Add("Headers", request.Headers.RootElement.GetRawText());
        }

        if (request.IsActive.HasValue)
        {
            updates.Add("is_active = @IsActive");
            parameters.Add("IsActive", request.IsActive.Value);
        }

        updates.Add("updated_at = @UpdatedAt");
        parameters.Add("UpdatedAt", DateTimeOffset.UtcNow);

        var sql = $"""
            UPDATE morphdb._morph_webhooks
            SET {string.Join(", ", updates)}
            WHERE webhook_id = @WebhookId
            RETURNING webhook_id, tenant_id, table_id, logical_name, url, secret, events, filter, headers, is_active, created_at, updated_at
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await connection.QuerySingleAsync<WebhookRow>(sql, parameters);

        return MapToMetadata(result);
    }

    public async Task DeleteWebhookAsync(
        Guid webhookId,
        CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM morphdb._morph_webhooks WHERE webhook_id = @WebhookId";

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { WebhookId = webhookId });
    }

    public async Task<IReadOnlyList<WebhookMetadata>> GetSubscribedWebhooksAsync(
        Guid tenantId,
        Guid tableId,
        WebhookEvent webhookEvent,
        CancellationToken cancellationToken = default)
    {
        var eventName = webhookEvent.ToString().ToLowerInvariant();

        const string sql = """
            SELECT w.webhook_id, w.tenant_id, w.table_id, w.logical_name, w.url, w.secret, w.events,
                   w.filter, w.headers, w.is_active, w.created_at, w.updated_at, t.logical_name as table_logical_name
            FROM morphdb._morph_webhooks w
            JOIN morphdb._morph_tables t ON w.table_id = t.table_id
            WHERE w.tenant_id = @TenantId
              AND w.table_id = @TableId
              AND w.is_active = true
              AND @EventName = ANY(w.events)
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var results = await connection.QueryAsync<WebhookRow>(sql, new { TenantId = tenantId, TableId = tableId, EventName = eventName });

        return results.Select(MapToMetadata).ToList();
    }

    public async Task<WebhookDelivery> CreateDeliveryAsync(
        CreateDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        var deliveryId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        const string sql = """
            INSERT INTO morphdb._morph_webhook_deliveries (
                delivery_id, webhook_id, record_id, event, payload, status, created_at
            ) VALUES (
                @DeliveryId, @WebhookId, @RecordId, @Event, @Payload::jsonb, 'pending', @CreatedAt
            )
            RETURNING delivery_id, webhook_id, record_id, event, payload, status, attempt_count,
                      http_status_code, response_body, error_message, next_retry_at, created_at, delivered_at
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await connection.QuerySingleAsync<DeliveryRow>(sql, new
        {
            DeliveryId = deliveryId,
            request.WebhookId,
            request.RecordId,
            Event = request.Event.ToString().ToLowerInvariant(),
            Payload = request.Payload.RootElement.GetRawText(),
            CreatedAt = now
        });

        return MapToDelivery(result);
    }

    public async Task UpdateDeliveryAsync(
        UpdateDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE morphdb._morph_webhook_deliveries
            SET status = @Status,
                attempt_count = @AttemptCount,
                http_status_code = @HttpStatusCode,
                response_body = @ResponseBody,
                error_message = @ErrorMessage,
                next_retry_at = @NextRetryAt,
                delivered_at = @DeliveredAt
            WHERE delivery_id = @DeliveryId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new
        {
            request.DeliveryId,
            Status = request.Status.ToString().ToLowerInvariant(),
            request.AttemptCount,
            request.HttpStatusCode,
            request.ResponseBody,
            request.ErrorMessage,
            request.NextRetryAt,
            request.DeliveredAt
        });
    }

    public async Task<IReadOnlyList<WebhookDelivery>> GetPendingDeliveriesAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT delivery_id, webhook_id, record_id, event, payload, status, attempt_count,
                   http_status_code, response_body, error_message, next_retry_at, created_at, delivered_at
            FROM morphdb._morph_webhook_deliveries
            WHERE (status = 'pending' OR (status = 'retrying' AND next_retry_at <= @Now))
            ORDER BY created_at ASC
            LIMIT @Limit
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var results = await connection.QueryAsync<DeliveryRow>(sql, new { Now = DateTimeOffset.UtcNow, Limit = limit });

        return results.Select(MapToDelivery).ToList();
    }

    public async Task<IReadOnlyList<WebhookDelivery>> GetDeliveryHistoryAsync(
        Guid webhookId,
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT delivery_id, webhook_id, record_id, event, payload, status, attempt_count,
                   http_status_code, response_body, error_message, next_retry_at, created_at, delivered_at
            FROM morphdb._morph_webhook_deliveries
            WHERE webhook_id = @WebhookId
            ORDER BY created_at DESC
            LIMIT @Limit OFFSET @Offset
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var results = await connection.QueryAsync<DeliveryRow>(sql, new { WebhookId = webhookId, Limit = limit, Offset = offset });

        return results.Select(MapToDelivery).ToList();
    }

    public async Task<string> RegenerateSecretAsync(
        Guid webhookId,
        CancellationToken cancellationToken = default)
    {
        var newSecret = GenerateSecret();

        const string sql = """
            UPDATE morphdb._morph_webhooks
            SET secret = @Secret, updated_at = @UpdatedAt
            WHERE webhook_id = @WebhookId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new
        {
            WebhookId = webhookId,
            Secret = newSecret,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        return newSecret;
    }

    private static string GenerateSecret()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static WebhookMetadata MapToMetadata(WebhookRow row)
    {
        return new WebhookMetadata
        {
            WebhookId = row.webhook_id,
            TenantId = row.tenant_id,
            TableId = row.table_id,
            LogicalName = row.logical_name,
            Url = row.url,
            Secret = row.secret,
            Events = row.events?.Select(ParseEvent).ToList() ?? [],
            Filter = row.filter is null ? null : JsonDocument.Parse(row.filter),
            Headers = row.headers is null ? null : JsonDocument.Parse(row.headers),
            IsActive = row.is_active,
            CreatedAt = row.created_at,
            UpdatedAt = row.updated_at,
            TableLogicalName = row.table_logical_name
        };
    }

    private static WebhookDelivery MapToDelivery(DeliveryRow row)
    {
        return new WebhookDelivery
        {
            DeliveryId = row.delivery_id,
            WebhookId = row.webhook_id,
            RecordId = row.record_id,
            Event = ParseEvent(row.@event),
            Payload = JsonDocument.Parse(row.payload),
            Status = Enum.Parse<DeliveryStatus>(row.status, ignoreCase: true),
            AttemptCount = row.attempt_count,
            HttpStatusCode = row.http_status_code,
            ResponseBody = row.response_body,
            ErrorMessage = row.error_message,
            NextRetryAt = row.next_retry_at,
            CreatedAt = row.created_at,
            DeliveredAt = row.delivered_at
        };
    }

    private static WebhookEvent ParseEvent(string eventName)
    {
        return eventName.ToLowerInvariant() switch
        {
            "insert" => WebhookEvent.Insert,
            "update" => WebhookEvent.Update,
            "delete" => WebhookEvent.Delete,
            _ => throw new ArgumentException($"Unknown event: {eventName}")
        };
    }

    #region Row Types

    private sealed record WebhookRow
    {
        public Guid webhook_id { get; init; }
        public Guid tenant_id { get; init; }
        public Guid table_id { get; init; }
        public string logical_name { get; init; } = null!;
        public string url { get; init; } = null!;
        public string secret { get; init; } = null!;
        public string[]? events { get; init; }
        public string? filter { get; init; }
        public string? headers { get; init; }
        public bool is_active { get; init; }
        public DateTimeOffset created_at { get; init; }
        public DateTimeOffset updated_at { get; init; }
        public string? table_logical_name { get; init; }
    }

    private sealed record DeliveryRow
    {
        public Guid delivery_id { get; init; }
        public Guid webhook_id { get; init; }
        public Guid? record_id { get; init; }
        public string @event { get; init; } = null!;
        public string payload { get; init; } = null!;
        public string status { get; init; } = null!;
        public int attempt_count { get; init; }
        public int? http_status_code { get; init; }
        public string? response_body { get; init; }
        public string? error_message { get; init; }
        public DateTimeOffset? next_retry_at { get; init; }
        public DateTimeOffset created_at { get; init; }
        public DateTimeOffset? delivered_at { get; init; }
    }

    #endregion
}
