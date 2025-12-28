using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Abstractions;
using MorphDB.Service.Models.Api;

namespace MorphDB.Service.Controllers;

/// <summary>
/// LoggerMessage delegates for QuotaController.
/// </summary>
internal static partial class QuotaControllerLogs
{
    [LoggerMessage(LogLevel.Information, "Getting quota usage for project {ProjectId}")]
    public static partial void GettingQuotaUsage(ILogger logger, Guid projectId);

    [LoggerMessage(LogLevel.Information, "Getting quota limits for project {ProjectId}")]
    public static partial void GettingQuotaLimits(ILogger logger, Guid projectId);

    [LoggerMessage(LogLevel.Information, "Getting rate limit status for project {ProjectId}")]
    public static partial void GettingRateLimitStatus(ILogger logger, Guid projectId);

    [LoggerMessage(LogLevel.Error, "Quota operation failed: {Error}")]
    public static partial void QuotaOperationFailed(ILogger logger, string error, Exception exception);
}

/// <summary>
/// REST API controller for querying quota and rate limit information.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/quota")]
[Produces("application/json")]
public sealed class QuotaController : ControllerBase
{
    private readonly IQuotaService _quotaService;
    private readonly IRateLimiter _rateLimiter;
    private readonly ILogger<QuotaController> _logger;

    public QuotaController(
        IQuotaService quotaService,
        IRateLimiter rateLimiter,
        ILogger<QuotaController> logger)
    {
        _quotaService = quotaService;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    /// <summary>
    /// Gets current quota usage for a project.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="period">Optional period (format: yyyy-MM). Defaults to current month.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current quota usage.</returns>
    [HttpGet("usage")]
    [ProducesResponseType(typeof(QuotaUsageApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetUsage(
        Guid projectId,
        [FromQuery] string? period,
        CancellationToken cancellationToken)
    {
        QuotaControllerLogs.GettingQuotaUsage(_logger, projectId);

        try
        {
            DateTimeOffset? targetPeriod = null;
            if (!string.IsNullOrEmpty(period))
            {
                if (DateTimeOffset.TryParseExact(
                    period + "-01",
                    "yyyy-MM-dd",
                    null,
                    System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var parsed))
                {
                    targetPeriod = parsed;
                }
                else
                {
                    return BadRequest(new ErrorResponse
                    {
                        Error = "INVALID_PERIOD_FORMAT",
                        Message = "Period must be in format yyyy-MM",
                        Code = "INVALID_PERIOD_FORMAT"
                    });
                }
            }

            var usage = await _quotaService.GetUsageAsync(projectId, targetPeriod, cancellationToken);
            return Ok(QuotaUsageApiResponse.FromModel(usage));
        }
        catch (Exception ex)
        {
            QuotaControllerLogs.QuotaOperationFailed(_logger, ex.Message, ex);
            return BadRequest(new ErrorResponse
            {
                Error = "QUOTA_QUERY_FAILED",
                Message = ex.Message,
                Code = "QUOTA_QUERY_FAILED"
            });
        }
    }

    /// <summary>
    /// Gets quota limits for a project.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Quota limits.</returns>
    [HttpGet("limits")]
    [ProducesResponseType(typeof(QuotaLimitsApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLimits(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        QuotaControllerLogs.GettingQuotaLimits(_logger, projectId);

        var limits = await _quotaService.GetLimitsAsync(projectId, cancellationToken);
        return Ok(QuotaLimitsApiResponse.FromModel(limits));
    }

    /// <summary>
    /// Gets current rate limit status for a project.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Rate limit status.</returns>
    [HttpGet("rate-limit")]
    [ProducesResponseType(typeof(RateLimitStatusApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRateLimitStatus(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        QuotaControllerLogs.GettingRateLimitStatus(_logger, projectId);

        var key = $"project:{projectId}";
        var status = await _rateLimiter.GetStatusAsync(key, cancellationToken);

        return Ok(RateLimitStatusApiResponse.FromModel(status));
    }

    /// <summary>
    /// Gets combined quota and rate limit summary.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Combined summary.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(QuotaSummaryApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var usageTask = _quotaService.GetUsageAsync(projectId, null, cancellationToken);
        var limitsTask = _quotaService.GetLimitsAsync(projectId, cancellationToken);
        var rateLimitTask = _rateLimiter.GetStatusAsync($"project:{projectId}", cancellationToken);

        await Task.WhenAll(usageTask, limitsTask);
        var rateLimit = await rateLimitTask;

        var usage = await usageTask;
        var limits = await limitsTask;

        return Ok(new QuotaSummaryApiResponse
        {
            Usage = QuotaUsageApiResponse.FromModel(usage),
            Limits = QuotaLimitsApiResponse.FromModel(limits),
            RateLimit = RateLimitStatusApiResponse.FromModel(rateLimit)
        });
    }
}

/// <summary>
/// API response for quota usage.
/// </summary>
public sealed class QuotaUsageApiResponse
{
    public Guid ProjectId { get; init; }
    public string Period { get; init; } = string.Empty;
    public long ApiRequests { get; init; }
    public long DataReads { get; init; }
    public long DataWrites { get; init; }
    public long StorageBytes { get; init; }
    public long BandwidthBytes { get; init; }
    public DateTimeOffset LastUpdated { get; init; }

    public static QuotaUsageApiResponse FromModel(QuotaUsage model) => new()
    {
        ProjectId = model.ProjectId,
        Period = model.Period.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture),
        ApiRequests = model.ApiRequests,
        DataReads = model.DataReads,
        DataWrites = model.DataWrites,
        StorageBytes = model.StorageBytes,
        BandwidthBytes = model.BandwidthBytes,
        LastUpdated = model.LastUpdated
    };
}

/// <summary>
/// API response for quota limits.
/// </summary>
public sealed class QuotaLimitsApiResponse
{
    public Guid ProjectId { get; init; }
    public long MaxApiRequests { get; init; }
    public long MaxDataReads { get; init; }
    public long MaxDataWrites { get; init; }
    public long MaxStorageBytes { get; init; }
    public long MaxBandwidthBytes { get; init; }
    public string Tier { get; init; } = string.Empty;

    public static QuotaLimitsApiResponse FromModel(QuotaLimits model) => new()
    {
        ProjectId = model.ProjectId,
        MaxApiRequests = model.MaxApiRequests,
        MaxDataReads = model.MaxDataReads,
        MaxDataWrites = model.MaxDataWrites,
        MaxStorageBytes = model.MaxStorageBytes,
        MaxBandwidthBytes = model.MaxBandwidthBytes,
        Tier = model.Tier
    };
}

/// <summary>
/// API response for rate limit status.
/// </summary>
public sealed class RateLimitStatusApiResponse
{
    public string Key { get; init; } = string.Empty;
    public int Available { get; init; }
    public int Limit { get; init; }
    public double WindowSeconds { get; init; }
    public DateTimeOffset ResetAt { get; init; }
    public long RequestCount { get; init; }

    public static RateLimitStatusApiResponse FromModel(RateLimitStatus model) => new()
    {
        Key = model.Key,
        Available = model.Available,
        Limit = model.Limit,
        WindowSeconds = model.Window.TotalSeconds,
        ResetAt = model.ResetAt,
        RequestCount = model.RequestCount
    };
}

/// <summary>
/// Combined quota and rate limit summary.
/// </summary>
public sealed class QuotaSummaryApiResponse
{
    public required QuotaUsageApiResponse Usage { get; init; }
    public required QuotaLimitsApiResponse Limits { get; init; }
    public required RateLimitStatusApiResponse RateLimit { get; init; }
}
