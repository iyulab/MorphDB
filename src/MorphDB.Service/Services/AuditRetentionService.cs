using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;

namespace MorphDB.Service.Services;

/// <summary>
/// Applies each project's audit retention window on a schedule.
/// <para>
/// Retention runs here rather than opportunistically on write for two reasons: a quiet project
/// would never reach its own cleanup, and putting a DELETE in front of the audit path would make
/// every request pay for it. Upkeep of the physical world is this server's obligation — the caller
/// cannot reach the audit table, so asking them to trigger a purge would be an encapsulation
/// failure, not a feature.
/// </para>
/// <para>
/// A project with no configured window is skipped by the service itself, so this loop is a no-op
/// for a deployment that has not asked for retention.
/// </para>
/// </summary>
public sealed partial class AuditRetentionService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AuditRetentionService> _logger;
    private readonly AuditRetentionOptions _options;

    public AuditRetentionService(
        IServiceProvider serviceProvider,
        ILogger<AuditRetentionService> logger,
        AuditRetentionOptions options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Wait first: a restart loop must not turn into a delete loop, and nothing here is
            // urgent enough to run before the service is serving.
            try
            {
                await Task.Delay(_options.SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogSweepError(_logger, ex);
            }
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var projects = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();

        var offset = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var page = await projects.ListAsync(
                ProjectStatus.Active, offset, _options.ProjectPageSize, cancellationToken);
            if (page.Count == 0)
            {
                break;
            }

            foreach (var project in page)
            {
                // One project's failure must not stop the sweep: the next project's history is
                // just as much this server's obligation.
                try
                {
                    await audit.ApplyRetentionAsync(project.ProjectId, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LogProjectRetentionError(_logger, project.ProjectId, ex);
                }
            }

            offset += page.Count;
        }
    }

    [LoggerMessage(LogLevel.Error, "Audit retention sweep failed")]
    private static partial void LogSweepError(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Error, "Audit retention failed for project {ProjectId}")]
    private static partial void LogProjectRetentionError(
        ILogger logger, Guid projectId, Exception exception);
}

/// <summary>
/// How often audit retention is applied, and how many projects are read per page while doing it.
/// </summary>
public sealed class AuditRetentionOptions
{
    /// <summary>
    /// How long to wait between sweeps. Retention windows are expressed in days, so sweeping
    /// hourly keeps the store within roughly an hour of its declared window without turning
    /// cleanup into background load.
    /// </summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How many projects are listed per page while sweeping.
    /// </summary>
    public int ProjectPageSize { get; set; } = 100;
}
