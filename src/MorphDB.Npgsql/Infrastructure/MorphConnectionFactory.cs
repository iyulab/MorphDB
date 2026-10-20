using System.Buffers;
using System.Text;
using System.Text.Json;
using MorphDB.Core.Security;
using Npgsql;

namespace MorphDB.Npgsql.Infrastructure;

/// <summary>
/// The one place a database connection is opened. Every connection it hands out has a security
/// context written to its session first, so anything PostgreSQL evaluates on that connection — a
/// row-level security policy reading <c>current_setting('morphdb.user_id', true)</c>, say — sees who is
/// asking, whichever component built the SQL.
/// </summary>
/// <remarks>
/// <para>Two kinds of open, and the difference is who the statement is for.
/// <see cref="OpenAsync"/> is for a caller's rows: it writes the ambient
/// <see cref="SecurityContext"/>, and when there is none it writes an empty one — a policy then sees
/// nobody, and nobody is not allowed through. <see cref="OpenSystemAsync"/> is for MorphDB's own
/// bookkeeping — metadata, schema changes, jobs, audit — which is not done on anyone's behalf and
/// must not be filtered by whoever happened to trigger it.</para>
/// <para>Row-level security policies restrict the role MorphDB logs in as, and nothing in them says
/// who may pass: a caller that bypasses row-level security has its connection switch to the
/// <see cref="BypassRole"/>, which no policy names. The switch is part of the same statement that
/// writes the context, and the pool's reset on return switches back. A system connection keeps the
/// login role — it creates and alters tables, which must stay owned by that role — and reads no
/// caller's rows, so the policies it is subject to find nothing to filter.</para>
/// <para>The context is written per open rather than once per physical connection because a pooled
/// connection serves a different caller each time it is handed out. Npgsql resets session state when
/// a connection goes back to the pool, so a value written here cannot reach the next caller; the two
/// settings that defeat that reset are refused by the constructor.</para>
/// </remarks>
public sealed class MorphConnectionFactory
{
    /// <summary>The project the caller is working in; empty when there is no caller.</summary>
    public const string ProjectIdSetting = "morphdb.project_id";

    /// <summary>The caller's id; empty for an anonymous caller.</summary>
    public const string UserIdSetting = "morphdb.user_id";

    /// <summary>The caller's role; empty when it has none.</summary>
    public const string RoleSetting = "morphdb.role";

    /// <summary>The caller's e-mail address; empty when it is not known.</summary>
    public const string EmailSetting = "morphdb.email";

    /// <summary><c>true</c> or <c>false</c>: whether the caller presented credentials that were accepted.</summary>
    public const string IsAuthenticatedSetting = "morphdb.is_authenticated";

    /// <summary>The caller's claims as a JSON object of strings; <c>{}</c> when it has none.</summary>
    public const string ClaimsSetting = "morphdb.claims";

    /// <summary>
    /// The role a connection switches to when its caller bypasses row-level security, unless the
    /// host names another. It logs in to nothing and needs no <c>BYPASSRLS</c>: it passes because no
    /// policy names it. The login role must be a member of it.
    /// </summary>
    public const string DefaultBypassRole = "morphdb_bypass";

    private const string WriteContextSql =
        $"SELECT set_config('{ProjectIdSetting}', $1, false), set_config('{UserIdSetting}', $2, false), " +
        $"set_config('{RoleSetting}', $3, false), set_config('{EmailSetting}', $4, false), " +
        $"set_config('{IsAuthenticatedSetting}', $5, false), set_config('{ClaimsSetting}', $6, false), " +
        "set_config('role', $7, false)";

    private const string NoClaims = "{}";

    // set_config('role', 'none') is RESET ROLE: back to the role the connection logged in as.
    private const string LoginRole = "none";

    private readonly NpgsqlDataSource _dataSource;
    private readonly ISecurityContextAccessor _securityContext;

    /// <exception cref="ArgumentException">
    /// The data source multiplexes, or keeps session state when a connection is returned to the pool.
    /// </exception>
    public MorphConnectionFactory(
        NpgsqlDataSource dataSource,
        ISecurityContextAccessor securityContext,
        string bypassRole = DefaultBypassRole)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(securityContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(bypassRole);

        var settings = new NpgsqlConnectionStringBuilder(dataSource.ConnectionString);
        if (settings.Multiplexing)
        {
            throw new ArgumentException(
                "The connection string sets Multiplexing=true. A multiplexed command runs on whichever physical " +
                "connection is free, so there is no session for a security context to be written to, and one " +
                "caller's context would be read by another's statement. Remove the setting.",
                nameof(dataSource));
        }

        if (settings.NoResetOnClose)
        {
            throw new ArgumentException(
                "The connection string sets No Reset On Close=true. A connection returned to the pool would then " +
                "keep the security context of the caller that used it last. Remove the setting.",
                nameof(dataSource));
        }

        _dataSource = dataSource;
        _securityContext = securityContext;
        BypassRole = bypassRole;
    }

    /// <summary>
    /// The role a connection switches to for a caller that bypasses row-level security. Policies
    /// name the login role, never this one.
    /// </summary>
    public string BypassRole { get; }

    /// <summary>
    /// Opens a connection for a statement run on a caller's behalf, with the ambient security context
    /// written to its session. With no ambient context every setting is empty and bypass is off.
    /// </summary>
    public Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken = default)
        => OpenWithAsync(_securityContext.ContextOrNull, cancellationToken);

    /// <summary>
    /// A connection for a statement run on a caller's behalf: inside a unit of work
    /// (<see cref="ConnectionScope"/>) the unit's connection, so the statement sees and joins what the unit
    /// has done; otherwise a connection opened as <see cref="OpenAsync"/> opens one, closed when the lease ends.
    /// </summary>
    internal async ValueTask<ScopedConnection> LeaseAsync(CancellationToken cancellationToken = default)
    {
        if (ConnectionScope.Current is { } unit)
        {
            var (connection, transaction) = await unit.OpenAsync(cancellationToken).ConfigureAwait(false);
            return new ScopedConnection(connection, transaction, ownsConnection: false);
        }

        return new ScopedConnection(await OpenAsync(cancellationToken).ConfigureAwait(false), null, ownsConnection: true);
    }

    /// <summary>
    /// Opens a connection for MorphDB's own bookkeeping: no caller, as the login role — the role that
    /// owns what it creates. Not for reading or writing a caller's rows: a policy on a table sees
    /// nobody here.
    /// </summary>
    public Task<NpgsqlConnection> OpenSystemAsync(CancellationToken cancellationToken = default)
        => OpenWithAsync(context: null, cancellationToken);

    private async Task<NpgsqlConnection> OpenWithAsync(SecurityContext? context, CancellationToken cancellationToken)
    {
        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var command = new NpgsqlCommand(WriteContextSql, connection);
            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = ProjectOf(context) });
            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = context?.UserId ?? string.Empty });
            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = context?.Role ?? string.Empty });
            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = context?.Email ?? string.Empty });
            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = context?.IsAuthenticated == true ? "true" : "false" });
            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = ClaimsOf(context) });
            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = context?.BypassRls == true ? BypassRole : LoginRole });
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static string ProjectOf(SecurityContext? context)
        => context is null || context.ProjectId == Guid.Empty ? string.Empty : context.ProjectId.ToString();

    // Written by hand rather than through the serializer: the claims are a flat map of strings, and
    // this runs on every open, in hosts published with trimming where reflection metadata is absent.
    private static string ClaimsOf(SecurityContext? context)
    {
        if (context is null || context.Claims.Count == 0)
        {
            return NoClaims;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in context.Claims)
            {
                writer.WriteString(name, value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
