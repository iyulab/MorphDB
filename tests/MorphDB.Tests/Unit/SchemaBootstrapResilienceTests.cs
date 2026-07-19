using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Core.Abstractions;
using MorphDB.Npgsql;
using Npgsql;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Holds startup's tolerance for a database that is not accepting connections yet.
/// <para>
/// Under an orchestrator the database routinely starts accepting connections a few seconds after this
/// process does. Treating the first refusal as fatal exits the container, which then stays down even
/// though the database arrives moments later — so startup waits instead. What it must not do is wait
/// on a fault waiting cannot fix: if the server answered and rejected something, that surfaces at once.
/// </para>
/// </summary>
public class SchemaBootstrapResilienceTests
{
    [Fact]
    public async Task Startup_waits_for_a_database_that_is_not_accepting_connections_yet()
    {
        var (services, attempts) = Build(failures: 2, Unreachable);

        await services.EnsureMorphDbSchemaAsync();

        attempts().Should().Be(3, "the two refusals must be retried, not fatal");
    }

    [Fact]
    public async Task A_rejection_from_the_server_is_not_retried()
    {
        // The server answered — retrying cannot change the outcome, and doing so would stall startup
        // for the whole timeout before reporting a fault that was knowable immediately.
        var (services, attempts) = Build(failures: 1, () => new PostgresException(
            "permission denied for schema", "FATAL", "FATAL", PostgresErrorCodes.InsufficientPrivilege));

        var act = () => services.EnsureMorphDbSchemaAsync();

        await act.Should().ThrowAsync<PostgresException>();
        attempts().Should().Be(1);
    }

    private static (IServiceProvider Services, Func<int> Attempts) Build(int failures, Func<Exception> failWith)
    {
        var attempts = 0;
        var schema = new Mock<ISchemaLayerService>();
        schema.Setup(s => s.EnsureGlobalSchemaAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                attempts++;
                return attempts <= failures ? Task.FromException(failWith()) : Task.CompletedTask;
            });

        var services = new ServiceCollection();
        services.AddSingleton(schema.Object);
        return (services.BuildServiceProvider(), () => attempts);
    }

    /// <summary>How Npgsql surfaces a refused or unresolvable endpoint.</summary>
    private static Exception Unreachable()
        => new NpgsqlException("Failed to connect", new SocketException((int)SocketError.ConnectionRefused));
}
