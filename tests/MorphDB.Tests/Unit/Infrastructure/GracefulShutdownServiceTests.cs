using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MorphDB.Service.Infrastructure;

namespace MorphDB.Tests.Unit.Infrastructure;

/// <summary>
/// Tests for GracefulShutdownService request tracking and shutdown behavior.
/// </summary>
public sealed class GracefulShutdownServiceTests : IDisposable
{
    private readonly Mock<IHostApplicationLifetime> _lifetimeMock;
    private readonly Mock<ILogger<GracefulShutdownService>> _loggerMock;
    private readonly GracefulShutdownOptions _options;
    private readonly GracefulShutdownService _service;
    private CancellationTokenSource? _stoppingTokenSource;

    public GracefulShutdownServiceTests()
    {
        _lifetimeMock = new Mock<IHostApplicationLifetime>();
        _loggerMock = new Mock<ILogger<GracefulShutdownService>>();
        _options = new GracefulShutdownOptions
        {
            ShutdownTimeoutSeconds = 5,
            RejectNewRequestsDuringShutdown = false
        };

        // Setup the ApplicationStopping token
        _stoppingTokenSource = new CancellationTokenSource();
        _lifetimeMock.Setup(x => x.ApplicationStopping)
            .Returns(_stoppingTokenSource.Token);

        _service = new GracefulShutdownService(
            _lifetimeMock.Object,
            _loggerMock.Object,
            Options.Create(_options));
    }

    public void Dispose()
    {
        _service.Dispose();
        _stoppingTokenSource?.Dispose();
    }

    [Fact]
    public void InitialState_NotShuttingDown()
    {
        Assert.False(_service.IsShuttingDown);
        Assert.Equal(0, _service.ActiveRequestCount);
    }

    [Fact]
    public void TryRegisterRequest_BeforeShutdown_Succeeds()
    {
        var requestId = Guid.NewGuid().ToString();

        var result = _service.TryRegisterRequest(requestId);

        Assert.True(result);
        Assert.Equal(1, _service.ActiveRequestCount);
    }

    [Fact]
    public void CompleteRequest_DecreasesActiveCount()
    {
        var requestId = Guid.NewGuid().ToString();
        _service.TryRegisterRequest(requestId);

        _service.CompleteRequest(requestId);

        Assert.Equal(0, _service.ActiveRequestCount);
    }

    [Fact]
    public void MultipleRequests_TrackedCorrectly()
    {
        var request1 = Guid.NewGuid().ToString();
        var request2 = Guid.NewGuid().ToString();
        var request3 = Guid.NewGuid().ToString();

        _service.TryRegisterRequest(request1);
        _service.TryRegisterRequest(request2);
        _service.TryRegisterRequest(request3);

        Assert.Equal(3, _service.ActiveRequestCount);

        _service.CompleteRequest(request2);
        Assert.Equal(2, _service.ActiveRequestCount);

        _service.CompleteRequest(request1);
        _service.CompleteRequest(request3);
        Assert.Equal(0, _service.ActiveRequestCount);
    }

    [Fact]
    public void CompleteRequest_UnknownRequest_DoesNotThrow()
    {
        // Should not throw for unknown request
        _service.CompleteRequest("unknown-request-id");

        Assert.Equal(0, _service.ActiveRequestCount);
    }

    [Fact]
    public async Task StartAsync_RegistersStoppingCallback()
    {
        await _service.StartAsync(CancellationToken.None);

        // Trigger the stopping callback
        _stoppingTokenSource!.Cancel();

        // Service should now be shutting down
        Assert.True(_service.IsShuttingDown);
    }

    [Fact]
    public async Task StopAsync_NoActiveRequests_CompletesImmediately()
    {
        await _service.StartAsync(CancellationToken.None);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await _service.StopAsync(CancellationToken.None);
        stopwatch.Stop();

        // Should complete almost immediately (< 500ms)
        Assert.True(stopwatch.ElapsedMilliseconds < 500);
    }

    [Fact]
    public async Task StopAsync_WithActiveRequests_WaitsForCompletion()
    {
        await _service.StartAsync(CancellationToken.None);

        var requestId = Guid.NewGuid().ToString();
        _service.TryRegisterRequest(requestId);

        // Complete the request after a short delay
        var completionTask = Task.Run(async () =>
        {
            await Task.Delay(100);
            _service.CompleteRequest(requestId);
        });

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await _service.StopAsync(CancellationToken.None);
        stopwatch.Stop();

        // Should have waited for the request to complete
        Assert.True(stopwatch.ElapsedMilliseconds >= 100);
        Assert.Equal(0, _service.ActiveRequestCount);
    }

    [Fact]
    public async Task StopAsync_WithStuckRequests_TimesOut()
    {
        // Use shorter timeout for test
        var shortTimeoutOptions = new GracefulShutdownOptions
        {
            ShutdownTimeoutSeconds = 1,
            RejectNewRequestsDuringShutdown = false
        };

        using var shortTimeoutService = new GracefulShutdownService(
            _lifetimeMock.Object,
            _loggerMock.Object,
            Options.Create(shortTimeoutOptions));

        await shortTimeoutService.StartAsync(CancellationToken.None);

        // Register a request that won't complete
        shortTimeoutService.TryRegisterRequest("stuck-request");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await shortTimeoutService.StopAsync(CancellationToken.None);
        stopwatch.Stop();

        // Should have timed out
        Assert.True(stopwatch.ElapsedMilliseconds >= 1000);
        Assert.True(stopwatch.ElapsedMilliseconds < 3000); // But not too long
        Assert.Equal(1, shortTimeoutService.ActiveRequestCount); // Request still active
    }

    [Fact]
    public async Task TryRegisterRequest_DuringShutdown_WithRejectEnabled_Fails()
    {
        var rejectOptions = new GracefulShutdownOptions
        {
            ShutdownTimeoutSeconds = 5,
            RejectNewRequestsDuringShutdown = true
        };

        using var rejectService = new GracefulShutdownService(
            _lifetimeMock.Object,
            _loggerMock.Object,
            Options.Create(rejectOptions));

        await rejectService.StartAsync(CancellationToken.None);

        // Trigger shutdown
        _stoppingTokenSource!.Cancel();

        // New request should be rejected
        var result = rejectService.TryRegisterRequest("new-request");

        Assert.False(result);
        Assert.Equal(0, rejectService.ActiveRequestCount);
    }

    [Fact]
    public async Task TryRegisterRequest_DuringShutdown_WithRejectDisabled_Succeeds()
    {
        await _service.StartAsync(CancellationToken.None);

        // Trigger shutdown
        _stoppingTokenSource!.Cancel();

        // New request should still be accepted
        var result = _service.TryRegisterRequest("new-request");

        Assert.True(result);
        Assert.Equal(1, _service.ActiveRequestCount);

        // Clean up
        _service.CompleteRequest("new-request");
    }
}
