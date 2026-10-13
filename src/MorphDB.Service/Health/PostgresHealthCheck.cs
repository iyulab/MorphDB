using Microsoft.Extensions.Diagnostics.HealthChecks;
using MorphDB.Npgsql.Infrastructure;

namespace MorphDB.Service.Health;

/// <summary>
/// Answers healthy when the database the service is actually using answers a trivial query.
/// It opens through the same <see cref="MorphConnectionFactory"/> every repository opens through, so
/// the probe follows whatever data source that factory is bound to — a test host that swaps the
/// source swaps the probe with it, and a check registered from a separate connection string could
/// not drift from the real one.
/// A failure surfaces as the thrown exception: the health service records it as unhealthy and
/// carries the message into the report.
/// </summary>
internal sealed class PostgresHealthCheck(MorphConnectionFactory connections) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenSystemAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return HealthCheckResult.Healthy();
    }
}
