using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace MorphDB.Service.Infrastructure;

/// <summary>
/// Manages graceful shutdown with request draining.
/// Tracks in-flight requests and waits for them to complete during shutdown.
/// </summary>
public sealed partial class GracefulShutdownService : IHostedService, IDisposable
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<GracefulShutdownService> _logger;
    private readonly GracefulShutdownOptions _options;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _activeRequests = new();
    private readonly SemaphoreSlim _shutdownSemaphore = new(0);
    private volatile bool _isShuttingDown;
    private CancellationTokenRegistration _stoppingRegistration;
    private int _completedDrainCount;

    public GracefulShutdownService(
        IHostApplicationLifetime lifetime,
        ILogger<GracefulShutdownService> logger,
        IOptions<GracefulShutdownOptions> options)
    {
        _lifetime = lifetime;
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>
    /// Gets whether the application is currently shutting down.
    /// </summary>
    public bool IsShuttingDown => _isShuttingDown;

    /// <summary>
    /// Gets the current count of active requests.
    /// </summary>
    public int ActiveRequestCount => _activeRequests.Count;

    /// <summary>
    /// Registers a request as active.
    /// </summary>
    /// <param name="requestId">Unique identifier for the request.</param>
    /// <returns>True if registered, false if shutdown is in progress and rejecting new requests.</returns>
    public bool TryRegisterRequest(string requestId)
    {
        if (_isShuttingDown && _options.RejectNewRequestsDuringShutdown)
        {
            LogRequestRejected(requestId);
            return false;
        }

        _activeRequests.TryAdd(requestId, DateTimeOffset.UtcNow);
        return true;
    }

    /// <summary>
    /// Marks a request as completed.
    /// </summary>
    /// <param name="requestId">Unique identifier for the request.</param>
    public void CompleteRequest(string requestId)
    {
        if (_activeRequests.TryRemove(requestId, out var startTime))
        {
            if (_isShuttingDown)
            {
                Interlocked.Increment(ref _completedDrainCount);
                LogRequestDrained(requestId, (long)(DateTimeOffset.UtcNow - startTime).TotalMilliseconds);
            }
        }

        // Signal shutdown waiter if all requests are drained
        if (_isShuttingDown && _activeRequests.IsEmpty)
        {
            _shutdownSemaphore.Release();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stoppingRegistration = _lifetime.ApplicationStopping.Register(OnApplicationStopping);
        LogStarted(_options.ShutdownTimeoutSeconds);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _isShuttingDown = true;
        var activeCount = _activeRequests.Count;

        if (activeCount == 0)
        {
            LogNoActiveRequests();
            return;
        }

        LogDrainingRequests(activeCount, _options.ShutdownTimeoutSeconds);

        var sw = Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(_options.ShutdownTimeoutSeconds);

        // Wait for all requests to complete or timeout
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(timeout);

            // Wait until all requests complete or timeout
            while (!_activeRequests.IsEmpty && !linkedCts.Token.IsCancellationRequested)
            {
                await _shutdownSemaphore.WaitAsync(TimeSpan.FromMilliseconds(100), linkedCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout or cancellation - log remaining requests
        }

        sw.Stop();
        var remainingCount = _activeRequests.Count;

        if (remainingCount > 0)
        {
            LogDrainTimeout(remainingCount, sw.ElapsedMilliseconds);

            // Log details of stuck requests
            foreach (var (requestId, startTime) in _activeRequests)
            {
                var duration = (long)(DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
                LogStuckRequest(requestId, duration);
            }
        }
        else
        {
            LogDrainComplete(_completedDrainCount, sw.ElapsedMilliseconds);
        }
    }

    private void OnApplicationStopping()
    {
        _isShuttingDown = true;
        LogShutdownSignalReceived(_activeRequests.Count);
    }

    public void Dispose()
    {
        _stoppingRegistration.Dispose();
        _shutdownSemaphore.Dispose();
    }

    #region LoggerMessages

    [LoggerMessage(Level = LogLevel.Information, Message = "Graceful shutdown service started with {TimeoutSeconds}s timeout")]
    private partial void LogStarted(int timeoutSeconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Shutdown signal received, {ActiveCount} active requests")]
    private partial void LogShutdownSignalReceived(int activeCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "No active requests, shutdown proceeding immediately")]
    private partial void LogNoActiveRequests();

    [LoggerMessage(Level = LogLevel.Information, Message = "Draining {Count} active requests (timeout: {TimeoutSeconds}s)")]
    private partial void LogDrainingRequests(int count, int timeoutSeconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Request {RequestId} drained after {DurationMs}ms")]
    private partial void LogRequestDrained(string requestId, long durationMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Request {RequestId} rejected - shutdown in progress")]
    private partial void LogRequestRejected(string requestId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Drain timeout: {RemainingCount} requests still active after {ElapsedMs}ms")]
    private partial void LogDrainTimeout(int remainingCount, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stuck request: {RequestId} running for {DurationMs}ms")]
    private partial void LogStuckRequest(string requestId, long durationMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Drain complete: {Count} requests finished in {ElapsedMs}ms")]
    private partial void LogDrainComplete(int count, long elapsedMs);

    #endregion
}

/// <summary>
/// Options for graceful shutdown behavior.
/// </summary>
public sealed class GracefulShutdownOptions
{
    /// <summary>
    /// Maximum time to wait for requests to complete during shutdown.
    /// Default: 30 seconds.
    /// </summary>
    public int ShutdownTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Whether to reject new requests when shutdown is in progress.
    /// Default: false (allow requests to complete naturally).
    /// </summary>
    public bool RejectNewRequestsDuringShutdown { get; set; }
}
