using System.Text.Json;
using MorphDB.Core.Security;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Security;
using MorphDB.Tests.Fixtures;
using Npgsql;

namespace MorphDB.Tests.Integration;

/// <summary>
/// <see cref="MorphConnectionFactory"/> is the one place a connection is opened, and what it promises
/// is about the session of the connection it hands back: the caller's security context is there to be
/// read, a bookkeeping connection carries no caller and bypasses, and neither survives into the next
/// open of the same pooled connection. These tests read the session back with
/// <c>current_setting</c>, which is how a row-level security policy would read it.
/// </summary>
[Collection("PostgreSQL")]
public class MorphConnectionFactoryTests
{
    private readonly PostgresFixture _fixture;

    public MorphConnectionFactoryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_caller_connection_carries_every_part_of_the_ambient_context()
    {
        var ct = TestContext.Current.CancellationToken;
        var context = new SecurityContext
        {
            ProjectId = Guid.NewGuid(),
            UserId = "user-42",
            Role = "editor",
            Email = "user-42@example.com",
            IsAuthenticated = true,
            Claims = { ["department"] = "it's \"R&D\"" },
        };

        var session = await UnderAsync(context, () => ReadSessionAsync(_fixture.Connections.OpenAsync(ct), ct));

        session.ProjectId.Should().Be(context.ProjectId.ToString());
        session.UserId.Should().Be("user-42");
        session.Role.Should().Be("editor");
        session.Email.Should().Be("user-42@example.com");
        session.IsAuthenticated.Should().Be("true");
        session.Bypass.Should().Be("off");

        using var claims = JsonDocument.Parse(session.Claims);
        claims.RootElement.GetProperty("department").GetString().Should().Be("it's \"R&D\"",
            "the claims are written as JSON, so a value with quotes in it has to come back as it went in");
    }

    [Fact]
    public async Task A_caller_connection_with_no_ambient_context_carries_nobody()
    {
        var ct = TestContext.Current.CancellationToken;

        var session = await WithNoAmbientContextAsync(() => ReadSessionAsync(_fixture.Connections.OpenAsync(ct), ct));

        session.Should().Be(new SessionSettings(
            ProjectId: string.Empty,
            UserId: string.Empty,
            Role: string.Empty,
            Email: string.Empty,
            IsAuthenticated: "false",
            Claims: "{}",
            Bypass: "off"),
            "a statement nobody asked for must look like nobody to a policy, not like the last caller or like the service");
    }

    [Fact]
    public async Task A_system_connection_bypasses_and_carries_no_caller_even_while_a_caller_is_ambient()
    {
        var ct = TestContext.Current.CancellationToken;
        var caller = new SecurityContext
        {
            ProjectId = Guid.NewGuid(),
            UserId = "user-42",
            Role = "editor",
            IsAuthenticated = true,
            BypassRls = false,
        };

        var session = await UnderAsync(caller, () => ReadSessionAsync(_fixture.Connections.OpenSystemAsync(ct), ct));

        session.Bypass.Should().Be("on");
        session.UserId.Should().BeEmpty(
            "bookkeeping is not done on behalf of whoever happened to trigger it, so it must not be attributed to them");
        session.ProjectId.Should().BeEmpty();
        session.Role.Should().BeEmpty();
        session.IsAuthenticated.Should().Be("false");
    }

    [Fact]
    public async Task A_pooled_connection_does_not_carry_one_caller_into_the_next_open()
    {
        var ct = TestContext.Current.CancellationToken;

        // A pool of one: every open below is handed the same physical connection.
        await using var dataSource = NpgsqlDataSource.Create(
            new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { MaxPoolSize = 1 });
        var connections = new MorphConnectionFactory(dataSource, new SecurityContextAccessor());
        var first = new SecurityContext { ProjectId = Guid.NewGuid(), UserId = "first-caller", IsAuthenticated = true };

        var (firstBackend, firstUser) = await UnderAsync(
            first, () => ReadBackendAndUserAsync(connections.OpenAsync(ct), ct));
        firstUser.Should().Be("first-caller");

        // Opened without the factory, so nothing is written over what the first caller left: this is
        // the reset the pool performs, on its own.
        var (rawBackend, rawUser) = await ReadBackendAndUserAsync(dataSource.OpenConnectionAsync(ct).AsTask(), ct);
        rawBackend.Should().Be(firstBackend, "the test is vacuous unless the pool handed back the same connection");
        rawUser.Should().BeNullOrEmpty("the pool resets session state when a connection is returned to it");

        var (nextBackend, nextUser) = await WithNoAmbientContextAsync(
            () => ReadBackendAndUserAsync(connections.OpenAsync(ct), ct));
        nextBackend.Should().Be(firstBackend);
        nextUser.Should().BeEmpty("the next open of the same connection has no caller, and must not inherit one");
    }

    [Theory]
    [InlineData("Multiplexing=true")]
    [InlineData("No Reset On Close=true")]
    public async Task A_data_source_that_would_share_session_state_between_callers_is_refused(string setting)
    {
        // Building a data source connects to nothing, so the refusal needs no server.
        await using var dataSource = NpgsqlDataSource.Create(
            $"Host=localhost;Database=never_reached;Username=x;Password=x;{setting}");

        var act = () => new MorphConnectionFactory(dataSource, new SecurityContextAccessor());

        act.Should().Throw<ArgumentException>()
            .WithMessage($"*{setting}*", "the message has to name the setting to remove");
    }

    /// <summary>
    /// Runs <paramref name="action"/> with <paramref name="context"/> as the ambient security context.
    /// The accessor is AsyncLocal-backed, and a value set inside an async method does not flow back
    /// to its caller — so the context is gone again when this returns.
    /// </summary>
    private static async Task<T> UnderAsync<T>(SecurityContext context, Func<Task<T>> action)
    {
        new SecurityContextAccessor().SetContext(context);
        return await action();
    }

    /// <summary>
    /// Runs <paramref name="action"/> where no security context can be ambient, whatever the test
    /// runner's own flow carries: the accessor has no way to clear a context, so the work is started
    /// on a task that captures no execution context at all.
    /// </summary>
    private static Task<T> WithNoAmbientContextAsync<T>(Func<Task<T>> action)
    {
        using (ExecutionContext.SuppressFlow())
        {
            return Task.Run(action);
        }
    }

    private static async Task<SessionSettings> ReadSessionAsync(Task<NpgsqlConnection> opening, CancellationToken ct)
    {
        await using var connection = await opening;
        await using var command = new NpgsqlCommand(
            $"""
            SELECT current_setting('{MorphConnectionFactory.ProjectIdSetting}', true),
                   current_setting('{MorphConnectionFactory.UserIdSetting}', true),
                   current_setting('{MorphConnectionFactory.RoleSetting}', true),
                   current_setting('{MorphConnectionFactory.EmailSetting}', true),
                   current_setting('{MorphConnectionFactory.IsAuthenticatedSetting}', true),
                   current_setting('{MorphConnectionFactory.ClaimsSetting}', true),
                   current_setting('{MorphConnectionFactory.BypassSetting}', true)
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        (await reader.ReadAsync(ct)).Should().BeTrue();

        return new SessionSettings(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6));
    }

    private static async Task<(int Backend, string? UserId)> ReadBackendAndUserAsync(
        Task<NpgsqlConnection> opening, CancellationToken ct)
    {
        await using var connection = await opening;
        await using var command = new NpgsqlCommand(
            $"SELECT pg_backend_pid(), current_setting('{MorphConnectionFactory.UserIdSetting}', true)",
            connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        (await reader.ReadAsync(ct)).Should().BeTrue();

        return (reader.GetInt32(0), await reader.IsDBNullAsync(1, ct) ? null : reader.GetString(1));
    }

    private sealed record SessionSettings(
        string ProjectId,
        string UserId,
        string Role,
        string Email,
        string IsAuthenticated,
        string Claims,
        string Bypass);
}
