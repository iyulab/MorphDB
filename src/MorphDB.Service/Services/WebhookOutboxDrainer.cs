using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Service.Realtime;
using Npgsql;

namespace MorphDB.Service.Services;

/// <summary>
/// A nudge that a change may be waiting in the outbox. The change listener gives one for every
/// notification it hears; the drainer also wakes on its own on a timer, so a notification nobody
/// heard only delays a delivery, it does not lose it.
/// </summary>
public sealed class OutboxSignal : IDisposable
{
    private readonly SemaphoreSlim _pending = new(0, 1);

    /// <inheritdoc />
    public void Dispose() => _pending.Dispose();

    /// <summary>Says there may be work. Nudges given while one is already pending collapse into it.</summary>
    public void Notify()
    {
        try
        {
            _pending.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already nudged; the drainer will look once and find everything.
        }
    }

    /// <summary>Waits for a nudge or for <paramref name="timeout"/>, whichever comes first.</summary>
    public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _pending.WaitAsync(timeout, cancellationToken);
}

/// <summary>
/// Turns the changes recorded in <c>morphdb._morph_change_outbox</c> into webhook deliveries.
/// <para>
/// A data table's trigger writes a change some webhook subscribes to into the outbox in the writing
/// transaction itself, so the change is there exactly when the write committed — whether or not any
/// instance was listening for notifications at that moment. Queuing deliveries from the notification
/// instead lost every change committed while the listener was disconnected or the service was down.
/// </para>
/// <para>
/// Each pass takes a batch in commit-independent sequence order with <c>FOR UPDATE SKIP LOCKED</c>,
/// so instances share the work without handling a change twice; queues the deliveries; and removes
/// the batch in the same transaction that held it. An instance that stops between queuing and
/// removing leaves the batch to be taken again, and the delivery's change key — the transaction,
/// table, row and operation — keeps it to one delivery per webhook.
/// </para>
/// <para>
/// A delivery's data is the row read back when its change is handled, as it always was: the row as
/// it stands then, not the image the write produced, and none for a row deleted since or hidden from
/// a read with no caller (<c>docs/API.md</c>).
/// </para>
/// </summary>
public sealed partial class WebhookOutboxDrainer : BackgroundService
{
    /// <summary>How often the outbox is looked at with no nudge — the longest a missed notification delays a delivery.</summary>
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private const int BatchSize = 100;

    /// <summary>
    /// Passes a change may fail before it is given up. A failure leaves the change for the next pass;
    /// one that keeps failing (its table deactivated, say) would otherwise be retried for ever.
    /// </summary>
    internal const int MaxAttempts = 5;

    private readonly MorphConnectionFactory _connections;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMorphDataService _dataService;
    private readonly OutboxSignal _signal;
    private readonly ILogger<WebhookOutboxDrainer> _logger;

    public WebhookOutboxDrainer(
        MorphConnectionFactory connections,
        IServiceScopeFactory scopeFactory,
        IMorphDataService dataService,
        OutboxSignal signal,
        ILogger<WebhookOutboxDrainer> logger)
    {
        _connections = connections;
        _scopeFactory = scopeFactory;
        _dataService = dataService;
        _signal = signal;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await DrainBatchAsync(stoppingToken) == BatchSize)
                {
                    // A full batch: there may be more behind it.
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The outbox may not exist yet on a first start, or the database may be away; the
                // changes wait where they are and the next pass tries again.
                LogDrainError(_logger, ex);
            }

            try
            {
                await _signal.WaitAsync(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>Handles one batch and returns how many changes it held.</summary>
    internal async Task<int> DrainBatchAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var changes = new List<(long Seq, int Attempts, DatabaseChangeEvent Change)>();
        await using (var select = new NpgsqlCommand(
            """
            SELECT seq, project_id, table_id, table_name, operation, record_id, transaction_id, occurred_at, attempts
            FROM morphdb._morph_change_outbox
            ORDER BY seq
            LIMIT @limit
            FOR UPDATE SKIP LOCKED
            """, connection, transaction))
        {
            select.Parameters.AddWithValue("limit", BatchSize);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                changes.Add((reader.GetInt64(0), reader.GetInt32(8), new DatabaseChangeEvent
                {
                    ProjectId = reader.GetGuid(1),
                    TableId = reader.GetGuid(2),
                    Table = reader.GetString(3),
                    Operation = reader.GetString(4),
                    RecordId = reader.IsDBNull(5) ? null : reader.GetGuid(5),
                    TransactionId = reader.GetString(6),
                    Timestamp = reader.GetFieldValue<DateTimeOffset>(7),
                }));
            }
        }

        if (changes.Count == 0)
        {
            return 0;
        }

        var done = new List<long>();
        var failed = new List<long>();
        foreach (var (seq, attempts, change) in changes)
        {
            try
            {
                await QueueDeliveriesAsync(change, cancellationToken);
                done.Add(seq);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (attempts + 1 >= MaxAttempts)
                {
                    LogChangeGivenUp(_logger, change.Operation, change.Table, change.RecordId, attempts + 1, ex);
                    done.Add(seq);
                }
                else
                {
                    LogChangeFailed(_logger, change.Operation, change.Table, change.RecordId, ex);
                    failed.Add(seq);
                }
            }
        }

        await using (var remove = new NpgsqlCommand(
            """
            DELETE FROM morphdb._morph_change_outbox WHERE seq = ANY(@done);
            UPDATE morphdb._morph_change_outbox SET attempts = attempts + 1 WHERE seq = ANY(@failed);
            """, connection, transaction))
        {
            remove.Parameters.AddWithValue("done", done.ToArray());
            remove.Parameters.AddWithValue("failed", failed.ToArray());
            await remove.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        LogBatchDrained(_logger, changes.Count);
        return changes.Count;
    }

    private async Task QueueDeliveriesAsync(DatabaseChangeEvent change, CancellationToken cancellationToken)
    {
        var webhookEvent = change.Operation.ToUpperInvariant() switch
        {
            ChangeOperation.Insert => WebhookEvent.Insert,
            ChangeOperation.Update => WebhookEvent.Update,
            ChangeOperation.Delete => WebhookEvent.Delete,
            _ => (WebhookEvent?)null,
        };
        if (webhookEvent is not { } subscribedEvent)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var webhookManager = scope.ServiceProvider.GetRequiredService<IWebhookManager>();
        var webhooks = await webhookManager.GetSubscribedWebhooksAsync(change.ProjectId, change.TableId, subscribedEvent, cancellationToken);
        if (webhooks.Count == 0)
        {
            // Unsubscribed or deactivated since the write: nothing to deliver.
            return;
        }

        var data = subscribedEvent == WebhookEvent.Delete ? null : await ReadRowAsync(change, cancellationToken);
        var payload = new WebhookPayload
        {
            Event = subscribedEvent.ToString().ToLowerInvariant(),
            Table = change.Table,
            RecordId = change.RecordId,
            Timestamp = change.Timestamp,
            Data = data,
        };

        var deliveryService = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryService>();
        foreach (var webhook in webhooks)
        {
            if (WebhookFilterMatcher.Matches(webhook.Filter, data))
            {
                await deliveryService.QueueDeliveryAsync(webhook, payload, change.ChangeKey, cancellationToken);
            }
        }
    }

    private async Task<IDictionary<string, object?>> ReadRowAsync(DatabaseChangeEvent change, CancellationToken cancellationToken)
    {
        if (change.RecordId is not { } recordId)
        {
            return new Dictionary<string, object?>();
        }

        return await _dataService.GetByIdAsync(change.ProjectId, change.Table, recordId, cancellationToken)
            ?? new Dictionary<string, object?>();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook deliveries for {Operation} on {Table}, record {RecordId} could not be queued; the change is kept for the next pass")]
    private static partial void LogChangeFailed(ILogger logger, string operation, string table, Guid? recordId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Webhook deliveries for {Operation} on {Table}, record {RecordId} could not be queued in {Attempts} passes; the change is given up")]
    private static partial void LogChangeGivenUp(ILogger logger, string operation, string table, Guid? recordId, int attempts, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Queued webhook deliveries for {Count} recorded change(s)")]
    private static partial void LogBatchDrained(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook outbox could not be drained; its changes wait for the next pass")]
    private static partial void LogDrainError(ILogger logger, Exception exception);
}
