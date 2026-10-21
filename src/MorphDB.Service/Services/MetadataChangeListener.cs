using MorphDB.Npgsql.Infrastructure;
using Npgsql;

namespace MorphDB.Service.Services;

/// <summary>
/// Keeps the cached table definitions (<see cref="MetadataCache"/>) current. The database notifies every
/// write to the definition tables — whoever makes it: this instance, another instance of the service, a
/// migration — and the notification empties the cached definitions of the project it names. The cache
/// serves only while this listener is listening: before the first <c>LISTEN</c>, and from the moment its
/// connection is lost until it listens again, a change could go unheard, so every definition is read from
/// the database.
/// </summary>
public sealed partial class MetadataChangeListener : BackgroundService
{
    private const string ChannelName = "morphdb_metadata";
    private const string FunctionName = "morphdb.notify_metadata_change";
    private const string TriggerName = "_morph_notify_metadata";
    private const int KeepAliveSeconds = 30;

    // The definition tables a cached definition is read from (MetadataRepository.ReadTableAsync).
    private static readonly string[] DefinitionTables = ["_morph_tables", "_morph_columns", "_morph_relations", "_morph_indexes"];

    private readonly MetadataCache _cache;
    private readonly ILogger<MetadataChangeListener> _logger;
    private readonly string _connectionString;

    public MetadataChangeListener(MetadataCache cache, ILogger<MetadataChangeListener> logger, IConfiguration configuration)
    {
        _cache = cache;
        _logger = logger;
        _connectionString = configuration.GetConnectionString("MorphDB")
            ?? throw new InvalidOperationException("Connection string 'MorphDB' not found.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ListenAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _cache.Stop();
                    LogListenerError(_logger, ex);
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down while waiting to reconnect.
        }
        finally
        {
            _cache.Stop();
        }
    }

    private async Task ListenAsync(CancellationToken stoppingToken)
    {
        // A connection of its own for the life of the listener, outside the request path's pool — as the
        // change listener's. It reads no caller's rows.
        await using var dataSource = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(_connectionString) { KeepAlive = KeepAliveSeconds });
        await using var connection = await dataSource.OpenConnectionAsync(stoppingToken);

        await EnsureTriggersAsync(connection, stoppingToken);

        connection.Notification += (_, e) => _cache.Invalidate(Guid.TryParse(e.Payload, out var project) ? project : null);
        await using (var listen = new NpgsqlCommand($"LISTEN {ChannelName}", connection))
        {
            await listen.ExecuteNonQueryAsync(stoppingToken);
        }

        // Listening now, so no later change goes unheard; whatever the cache held before may have missed one.
        _cache.Start();
        LogListenerStarted(_logger, ChannelName);

        while (!stoppingToken.IsCancellationRequested)
        {
            await connection.WaitAsync(stoppingToken);
        }
    }

    /// <summary>
    /// Installs the notifying trigger on each definition table. Instances starting together take turns
    /// (a transaction-scoped advisory lock), since two replacing one function at once fail.
    /// </summary>
    private static async Task EnsureTriggersAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        // The payload is the project the changed definition belongs to — empty when it cannot be told,
        // which empties every cached definition.
        var sql = new System.Text.StringBuilder($$"""
            SELECT pg_advisory_xact_lock(hashtext('{{FunctionName}}'));

            CREATE OR REPLACE FUNCTION {{FunctionName}}() RETURNS trigger AS $$
            DECLARE
                project UUID;
                tbl UUID;
            BEGIN
                IF TG_TABLE_NAME IN ('_morph_tables', '_morph_relations') THEN
                    IF TG_OP = 'DELETE' THEN project := OLD.project_id; ELSE project := NEW.project_id; END IF;
                ELSE
                    IF TG_OP = 'DELETE' THEN tbl := OLD.table_id; ELSE tbl := NEW.table_id; END IF;
                    SELECT t.project_id INTO project FROM morphdb._morph_tables t WHERE t.table_id = tbl;
                END IF;

                PERFORM pg_notify('{{ChannelName}}', COALESCE(project::text, ''));
                RETURN NULL;
            END;
            $$ LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, pg_temp;

            """);
        foreach (var table in DefinitionTables)
        {
            sql.Append(System.Globalization.CultureInfo.InvariantCulture, $"""
                DROP TRIGGER IF EXISTS {TriggerName} ON morphdb.{table};
                CREATE TRIGGER {TriggerName} AFTER INSERT OR UPDATE OR DELETE ON morphdb.{table}
                    FOR EACH ROW EXECUTE FUNCTION {FunctionName}();

                """);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(sql.ToString(), connection, transaction))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Metadata listener started on channel {Channel}; table definitions are cached")]
    private static partial void LogListenerStarted(ILogger logger, string channel);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Metadata listener lost its connection; table definitions are read from the database until it listens again")]
    private static partial void LogListenerError(ILogger logger, Exception exception);
}
