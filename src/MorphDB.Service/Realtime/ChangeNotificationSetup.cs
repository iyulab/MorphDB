using Npgsql;

namespace MorphDB.Service.Realtime;

/// <summary>
/// Sets up PostgreSQL triggers for change notifications.
/// </summary>
public sealed partial class ChangeNotificationSetup
{
    private const string ChannelName = "morphdb_changes";
    private const string FunctionName = "morphdb.notify_change";
    private const string TriggerPrefix = "_morph_notify_";

    private readonly ILogger<ChangeNotificationSetup> _logger;
    private readonly string _connectionString;

    public ChangeNotificationSetup(
        ILogger<ChangeNotificationSetup> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _connectionString = configuration.GetConnectionString("MorphDB")
            ?? throw new InvalidOperationException("Connection string 'MorphDB' not found.");
    }

    /// <summary>
    /// Ensures the notification function exists in the database.
    /// </summary>
    public async Task EnsureNotificationFunctionAsync(CancellationToken cancellationToken = default)
    {
        await using var dataSource = NpgsqlDataSource.Create(_connectionString);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        // Use dynamic column access since MorphDB uses hashed physical column names
        var sql = $"""
            CREATE OR REPLACE FUNCTION {FunctionName}() RETURNS trigger AS $$
            DECLARE
                payload JSONB;
                record_id UUID;
                tenant_id UUID;
                table_name TEXT;
                id_col_name TEXT;
                tenant_col_name TEXT;
                row_data JSONB;
            BEGIN
                -- Get the logical table name and column physical names from system table
                SELECT t.logical_name INTO table_name
                FROM morphdb._morph_tables t
                WHERE t.physical_name = TG_TABLE_NAME;

                -- If not found in system table, use the physical name
                IF table_name IS NULL THEN
                    table_name := TG_TABLE_NAME;
                END IF;

                -- Get physical column names for id and tenant_id
                SELECT c.physical_name INTO id_col_name
                FROM morphdb._morph_tables t
                JOIN morphdb._morph_columns c ON c.table_id = t.table_id
                WHERE t.physical_name = TG_TABLE_NAME AND c.logical_name = 'id';

                SELECT c.physical_name INTO tenant_col_name
                FROM morphdb._morph_tables t
                JOIN morphdb._morph_columns c ON c.table_id = t.table_id
                WHERE t.physical_name = TG_TABLE_NAME AND c.logical_name = 'tenant_id';

                IF TG_OP = 'DELETE' THEN
                    row_data := to_jsonb(OLD);
                    record_id := (row_data ->> id_col_name)::uuid;
                    tenant_id := (row_data ->> tenant_col_name)::uuid;
                    payload := jsonb_build_object(
                        'tenant_id', tenant_id,
                        'table', table_name,
                        'operation', TG_OP,
                        'record_id', record_id,
                        'timestamp', NOW()
                    );
                ELSE
                    row_data := to_jsonb(NEW);
                    record_id := (row_data ->> id_col_name)::uuid;
                    tenant_id := (row_data ->> tenant_col_name)::uuid;
                    payload := jsonb_build_object(
                        'tenant_id', tenant_id,
                        'table', table_name,
                        'operation', TG_OP,
                        'record_id', record_id,
                        'data', row_data,
                        'timestamp', NOW()
                    );
                END IF;

                PERFORM pg_notify('{ChannelName}', payload::text);

                RETURN COALESCE(NEW, OLD);
            END;
            $$ LANGUAGE plpgsql;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        LogFunctionCreated(_logger, FunctionName);
    }

    /// <summary>
    /// Creates a notification trigger for a specific table.
    /// </summary>
    public async Task CreateTriggerForTableAsync(string physicalTableName, CancellationToken cancellationToken = default)
    {
        await using var dataSource = NpgsqlDataSource.Create(_connectionString);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var triggerName = $"{TriggerPrefix}{physicalTableName}";

        // Drop existing trigger if exists
        var dropSql = $"DROP TRIGGER IF EXISTS {triggerName} ON {physicalTableName}";
        await using (var dropCmd = new NpgsqlCommand(dropSql, connection))
        {
            await dropCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Create new trigger
        var createSql = $"""
            CREATE TRIGGER {triggerName}
            AFTER INSERT OR UPDATE OR DELETE ON {physicalTableName}
            FOR EACH ROW
            EXECUTE FUNCTION {FunctionName}();
            """;

        await using var createCmd = new NpgsqlCommand(createSql, connection);
        await createCmd.ExecuteNonQueryAsync(cancellationToken);

        LogTriggerCreated(_logger, triggerName, physicalTableName);
    }

    /// <summary>
    /// Removes the notification trigger from a specific table.
    /// </summary>
    public async Task RemoveTriggerFromTableAsync(string physicalTableName, CancellationToken cancellationToken = default)
    {
        await using var dataSource = NpgsqlDataSource.Create(_connectionString);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var triggerName = $"{TriggerPrefix}{physicalTableName}";
        var sql = $"DROP TRIGGER IF EXISTS {triggerName} ON {physicalTableName}";

        await using var cmd = new NpgsqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        LogTriggerRemoved(_logger, triggerName, physicalTableName);
    }

    /// <summary>
    /// Creates triggers for all existing user tables.
    /// </summary>
    public async Task CreateTriggersForAllTablesAsync(CancellationToken cancellationToken = default)
    {
        await using var dataSource = NpgsqlDataSource.Create(_connectionString);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        // Get all physical table names from system table
        var sql = "SELECT physical_name FROM morphdb._morph_tables WHERE is_active = true";
        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var tableNames = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            tableNames.Add(reader.GetString(0));
        }

        foreach (var tableName in tableNames)
        {
            await CreateTriggerForTableAsync(tableName, cancellationToken);
        }

        LogAllTriggersCreated(_logger, tableNames.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created notification function {FunctionName}")]
    private static partial void LogFunctionCreated(ILogger logger, string functionName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Created trigger {TriggerName} on table {TableName}")]
    private static partial void LogTriggerCreated(ILogger logger, string triggerName, string tableName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Removed trigger {TriggerName} from table {TableName}")]
    private static partial void LogTriggerRemoved(ILogger logger, string triggerName, string tableName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created notification triggers for {Count} tables")]
    private static partial void LogAllTriggersCreated(ILogger logger, int count);
}

/// <summary>
/// Hosted service that initializes change notification infrastructure on startup.
/// </summary>
public sealed partial class ChangeNotificationInitializer : IHostedService
{
    private readonly ChangeNotificationSetup _setup;
    private readonly ILogger<ChangeNotificationInitializer> _logger;

    public ChangeNotificationInitializer(
        ChangeNotificationSetup setup,
        ILogger<ChangeNotificationInitializer> logger)
    {
        _setup = setup;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            LogInitializing(_logger);

            await _setup.EnsureNotificationFunctionAsync(cancellationToken);
            await _setup.CreateTriggersForAllTablesAsync(cancellationToken);

            LogInitialized(_logger);
        }
        catch (Exception ex)
        {
            LogInitializationError(_logger, ex);
            // Don't throw - allow the application to start even if notification setup fails
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Initializing change notification infrastructure")]
    private static partial void LogInitializing(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Change notification infrastructure initialized")]
    private static partial void LogInitialized(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to initialize change notification infrastructure")]
    private static partial void LogInitializationError(ILogger logger, Exception exception);
}
