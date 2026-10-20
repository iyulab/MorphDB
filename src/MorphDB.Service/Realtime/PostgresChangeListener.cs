using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Core.Security;
using Npgsql;

namespace MorphDB.Service.Realtime;

/// <summary>
/// Listens for PostgreSQL NOTIFY events and broadcasts changes to connected clients.
/// <para>
/// Changes to one table are handled one at a time, in the order they arrive. PostgreSQL delivers
/// notifications in the order the transactions committed, and that order is the only thing a
/// subscriber has to tell two changes to one row apart — so the LISTEN connection's event handler
/// does nothing but hand each payload to a bounded channel, and one dispatch loop drains it into a
/// queue per table. Handling a payload inside the event handler itself (an <c>async void</c>, since
/// Npgsql's handler is a plain <c>void</c> delegate) let handlers overlap on their awaits and
/// broadcast out of commit order.
/// </para>
/// <para>
/// Different tables do not wait for each other. A change is read back once per distinct caller among
/// its table's subscribers, so a table with many callers holds its queue for as long as those reads
/// take; with one queue for everything, a change to any other table waited behind it. The order a
/// subscriber is promised is per table, which a queue per table keeps. At most
/// <see cref="ConcurrentTables"/> tables are handled at once, so a write burst across many tables
/// does not open that many database connections at once.
/// </para>
/// <para>
/// Changes not yet handled are bounded by <see cref="PendingNotificationCapacity"/> across all
/// tables: when that many wait, the LISTEN connection simply stops asking for the next notification
/// until one is done, and PostgreSQL queues the rest on its side.
/// </para>
/// </summary>
public sealed partial class PostgresChangeListener : BackgroundService
{
    private const string ChannelName = "morphdb_changes";

    /// <summary>
    /// Notifications waiting to be handled. Sized so a bulk write does not stall the LISTEN
    /// connection on every row, and small enough that a slow downstream (webhook queueing, a
    /// schema lookup) is felt as backpressure rather than as memory.
    /// </summary>
    private const int PendingNotificationCapacity = 1024;

    /// <summary>
    /// How many tables' changes are handled at the same time. Each handles one change at a time and
    /// reads its row once per distinct caller, so this bounds the database connections realtime holds.
    /// </summary>
    private const int ConcurrentTables = 8;

    /// <summary>
    /// Seconds of silence after which Npgsql sends a keepalive on the LISTEN connection. A
    /// connection that only ever waits is exactly the one a NAT or load balancer drops for idling,
    /// and without this the drop is only noticed at the next notification — which is then lost
    /// along with every one committed until the reconnect.
    /// </summary>
    private const int KeepAliveSeconds = 30;

    private readonly ILogger<PostgresChangeListener> _logger;
    private readonly IHubContext<MorphHub, IMorphHubClient> _hubContext;
    private readonly IMorphDataService _dataService;
    private readonly SubscriptionManager _subscriptions;
    private readonly ISecurityContextAccessor _securityContext;
    private readonly MorphDB.Service.Services.OutboxSignal _outboxSignal;
    private readonly string _connectionString;
    private readonly Channel<string> _pending = Channel.CreateBounded<string>(new BoundedChannelOptions(PendingNotificationCapacity)
    {
        SingleReader = true,
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.Wait,
    });

    /// <summary>A slot per change accepted and not yet handled, across every table.</summary>
    private readonly SemaphoreSlim _unhandled = new(PendingNotificationCapacity, PendingNotificationCapacity);

    private readonly SemaphoreSlim _tablesAtOnce = new(ConcurrentTables, ConcurrentTables);

    /// <summary>The last change queued for each table with changes still in hand; a table leaves when its queue is empty.</summary>
    private readonly Dictionary<string, Task> _tableTails = new(StringComparer.Ordinal);

    public PostgresChangeListener(
        ILogger<PostgresChangeListener> logger,
        IHubContext<MorphHub, IMorphHubClient> hubContext,
        IMorphDataService dataService,
        SubscriptionManager subscriptions,
        ISecurityContextAccessor securityContext,
        MorphDB.Service.Services.OutboxSignal outboxSignal,
        IConfiguration configuration)
    {
        _logger = logger;
        _outboxSignal = outboxSignal;
        _hubContext = hubContext;
        _dataService = dataService;
        _subscriptions = subscriptions;
        _securityContext = securityContext;
        _connectionString = configuration.GetConnectionString("MorphDB")
            ?? throw new InvalidOperationException("Connection string 'MorphDB' not found.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogListenerStarting(_logger);

        var consumer = ConsumePendingAsync(stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ListenForChangesAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Normal shutdown
                    break;
                }
                catch (Exception ex)
                {
                    LogListenerError(_logger, ex);

                    // Wait before reconnecting
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }
        finally
        {
            _pending.Writer.TryComplete();
            await consumer;
        }

        LogListenerStopped(_logger);
    }

    public override void Dispose()
    {
        _unhandled.Dispose();
        _tablesAtOnce.Dispose();
        base.Dispose();
    }

    private async Task ListenForChangesAsync(CancellationToken stoppingToken)
    {
        var connectionSettings = new NpgsqlConnectionStringBuilder(_connectionString)
        {
            KeepAlive = KeepAliveSeconds,
        };

        // The one connection not opened through MorphConnectionFactory. It is held for the life of
        // the process and only ever waits on LISTEN, so it gets a data source of its own — with
        // keepalive, and outside the pool the request path draws from. No row is read on it: the
        // row a notification names is read back through the data service (ReadRowAsync), so there
        // is no caller here for a security context to describe.
        await using var dataSource = NpgsqlDataSource.Create(connectionSettings);
        await using var connection = await dataSource.OpenConnectionAsync(stoppingToken);

        // Npgsql raises this synchronously, from inside WaitAsync, once per notification and in
        // delivery order. It only collects; the payloads are queued after WaitAsync returns, so the
        // wait on a full channel happens on this loop and not inside the driver's event dispatch.
        var received = new Queue<string>();
        connection.Notification += (_, e) => received.Enqueue(e.Payload);

        await using (var cmd = new NpgsqlCommand($"LISTEN {ChannelName}", connection))
        {
            await cmd.ExecuteNonQueryAsync(stoppingToken);
        }

        LogListenerStarted(_logger, ChannelName);

        // Keep the connection open and wait for notifications
        while (!stoppingToken.IsCancellationRequested)
        {
            await connection.WaitAsync(stoppingToken);

            while (received.TryDequeue(out var payload))
            {
                await _pending.Writer.WriteAsync(payload, stoppingToken);
            }
        }
    }

    /// <summary>
    /// Takes notifications in the order they were queued and puts each behind the earlier changes to
    /// its table — the one place that order is decided.
    /// </summary>
    private async Task ConsumePendingAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var payload in _pending.Reader.ReadAllAsync(stoppingToken))
            {
                var changeEvent = Parse(payload);
                if (changeEvent is null)
                {
                    continue;
                }

                // A webhook's deliveries are not queued from here: the change was recorded in the outbox
                // by the writing transaction (WebhookOutboxDrainer). This only says to look now rather
                // than at the drainer's next timed pass — and says it before the change waits its turn
                // behind its table's earlier ones.
                _outboxSignal.Notify();

                await _unhandled.WaitAsync(stoppingToken);
                Enqueue(changeEvent, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }

        Task[] inHand;
        lock (_tableTails)
        {
            inHand = [.. _tableTails.Values];
        }

        await Task.WhenAll(inHand);
    }

    private void Enqueue(DatabaseChangeEvent changeEvent, CancellationToken stoppingToken)
    {
        // The table's id, not its name: a table renamed between two changes is still one queue.
        var key = changeEvent.TableId != Guid.Empty
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{changeEvent.ProjectId}:{changeEvent.TableId}")
            : $"{changeEvent.ProjectId}:{changeEvent.Table}";

        Task tail;
        lock (_tableTails)
        {
            var before = _tableTails.GetValueOrDefault(key) ?? Task.CompletedTask;
            tail = HandleAfterAsync(before, changeEvent, stoppingToken);
            _tableTails[key] = tail;
        }

        _ = tail.ContinueWith(
            done =>
            {
                lock (_tableTails)
                {
                    if (_tableTails.TryGetValue(key, out var last) && last == done)
                    {
                        _tableTails.Remove(key);
                    }
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>Handles one change once every earlier change to its table is done. Never faults.</summary>
    private async Task HandleAfterAsync(Task before, DatabaseChangeEvent changeEvent, CancellationToken stoppingToken)
    {
        try
        {
            await before;
            await _tablesAtOnce.WaitAsync(stoppingToken);
            try
            {
                await HandleNotificationAsync(changeEvent, stoppingToken);
            }
            finally
            {
                _tablesAtOnce.Release();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown, mid-notification — not an error to report
        }
        catch (Exception ex)
        {
            LogNotificationError(_logger, ex);
        }
        finally
        {
            _unhandled.Release();
        }
    }

    private DatabaseChangeEvent? Parse(string payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DatabaseChangeEvent>(payload, JsonOptions);
        }
        catch (JsonException ex)
        {
            LogNotificationError(_logger, ex);
            return null;
        }
    }

    private async Task HandleNotificationAsync(DatabaseChangeEvent changeEvent, CancellationToken cancellationToken)
    {
        LogChangeReceived(_logger, changeEvent.Operation, changeEvent.Table, changeEvent.RecordId);

        var groupName = MorphHub.GetTableGroupName(changeEvent.ProjectId, changeEvent.Table);

        switch (changeEvent.Operation.ToUpperInvariant())
        {
            case ChangeOperation.Insert:
                await SendToThoseWhoCanReadAsync(changeEvent, ChangeOperation.Insert, cancellationToken);
                break;

            case ChangeOperation.Update:
                await SendToThoseWhoCanReadAsync(changeEvent, ChangeOperation.Update, cancellationToken);
                break;

            case ChangeOperation.Delete:
                await BroadcastRecordDeletedAsync(groupName, changeEvent);
                break;
        }
    }

    /// <summary>
    /// A created or changed row goes to each subscriber that may read it, and to no other. The row is
    /// read back through the same door REST serves it through — logical column names, decrypted values,
    /// system columns exactly as a <c>GET /api/data/{table}/{id}</c> — once per distinct caller among the
    /// table's subscribers, as that caller: a row-level security policy decides who hears of it, and
    /// subscribers who are the same caller cost one read between them. A subscriber that cannot read the
    /// row is told nothing — not that something changed, which would say a row it may not see exists.
    /// <para>
    /// This is the row as it stands when it is read, not the image the notifying statement wrote: a row
    /// changed again before the service got to its first notification arrives carrying the later state
    /// on both, and a row already deleted is read by nobody — its deletion is what subscribers hear.
    /// </para>
    /// </summary>
    private async Task SendToThoseWhoCanReadAsync(DatabaseChangeEvent changeEvent, string operation, CancellationToken cancellationToken)
    {
        if (changeEvent.RecordId is not { } recordId)
        {
            return;
        }

        foreach (var group in _subscriptions.Subscribers(changeEvent.ProjectId, changeEvent.Table).GroupBy(s => CallerKey(s.Caller), StringComparer.Ordinal))
        {
            var callers = group.ToList();
            var row = await ReadRowAsAsync(callers[0].Caller, changeEvent.ProjectId, changeEvent.Table, recordId, cancellationToken);
            if (row is null)
            {
                LogRowNotVisible(_logger, operation, changeEvent.Table, recordId, callers.Count);
                continue;
            }

            var message = new RecordChangedMessage
            {
                Table = changeEvent.Table,
                RecordId = recordId,
                Operation = operation,
                Data = row,
                Timestamp = changeEvent.Timestamp
            };
            List<string> recipients = [.. callers.Select(c => c.ConnectionId)];
            if (operation == ChangeOperation.Insert)
            {
                await _hubContext.Clients.Clients(recipients).RecordCreated(message);
            }
            else
            {
                await _hubContext.Clients.Clients(recipients).RecordUpdated(message);
            }
        }
    }

    /// <summary>Reads the row as <paramref name="caller"/>; the caller stays set only in this read's flow.</summary>
    private async Task<IDictionary<string, object?>?> ReadRowAsAsync(SecurityContext caller, Guid projectId, string table, Guid recordId, CancellationToken cancellationToken)
    {
        _securityContext.SetContext(caller);
        return await _dataService.GetByIdAsync(projectId, table, recordId, cancellationToken);
    }

    /// <summary>
    /// Everything about a caller a policy can read (what the connection factory writes to the session):
    /// two subscribers with the same key see the same rows.
    /// </summary>
    private static string CallerKey(SecurityContext caller) => string.Join('\u001f',
        caller.ProjectId, caller.UserId, caller.Role, caller.Email, caller.IsAuthenticated, caller.BypassRls,
        string.Join('\u001e', caller.Claims.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => $"{c.Key}={c.Value}")));

    private async Task BroadcastRecordDeletedAsync(string groupName, DatabaseChangeEvent changeEvent)
    {
        var message = new RecordDeletedMessage
        {
            Table = changeEvent.Table,
            RecordId = changeEvent.RecordId,
            Timestamp = changeEvent.Timestamp
        };

        await _hubContext.Clients.Group(groupName).RecordDeleted(message);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "PostgreSQL change listener starting")]
    private static partial void LogListenerStarting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "PostgreSQL change listener started, listening on channel {ChannelName}")]
    private static partial void LogListenerStarted(ILogger logger, string channelName);

    [LoggerMessage(Level = LogLevel.Information, Message = "PostgreSQL change listener stopped")]
    private static partial void LogListenerStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "PostgreSQL change listener error, will reconnect")]
    private static partial void LogListenerError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error handling notification")]
    private static partial void LogNotificationError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Change received: {Operation} on {Table}, record {RecordId}")]
    private static partial void LogChangeReceived(ILogger logger, string operation, string table, Guid? recordId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Operation} on {Table}, record {RecordId}: {Subscribers} subscriber(s) cannot read the row (deleted since, or hidden by a row-level security policy); not sent to them")]
    private static partial void LogRowNotVisible(ILogger logger, string operation, string table, Guid recordId, int subscribers);
}

/// <summary>
/// The operation values a change event carries on the wire.
/// <para>
/// They are the names PostgreSQL gives the triggering statement, kept upper case, and a client
/// branches on them — so they are a published contract rather than an internal detail. Having one
/// home instead of six literals is what keeps the vocabulary from drifting between the switch that
/// reads it and the messages that send it, and gives the documentation something to be checked
/// against.
/// </para>
/// </summary>
internal static class ChangeOperation
{
    public const string Insert = "INSERT";
    public const string Update = "UPDATE";
    public const string Delete = "DELETE";

    public static readonly IReadOnlyList<string> All = [Insert, Update, Delete];
}

/// <summary>
/// Event structure from PostgreSQL NOTIFY payload — the key of the changed row and nothing else
/// (see <c>ChangeNotificationSetup</c> for why the row itself does not ride along).
/// </summary>
internal sealed class DatabaseChangeEvent
{
    public Guid ProjectId { get; init; }
    public Guid TableId { get; init; }
    public required string Table { get; init; }
    public required string Operation { get; init; }
    public Guid? RecordId { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// The writing transaction. Every instance listening receives the same notification, so the
    /// transaction with the row and the operation names this change the same way on all of them.
    /// </summary>
    public string? TransactionId { get; init; }

    /// <summary>
    /// The change's identity across instances, or null for a notification from a trigger that does not
    /// name its transaction (one installed by an earlier version, until the service replaces it).
    /// </summary>
    public string? ChangeKey => TransactionId is { Length: > 0 } tx
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{tx}:{TableId}:{RecordId}:{Operation}")
        : null;
}
