using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MorphDB.Core.Abstractions;
using MorphDB.Service.Realtime;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="HubRateLimitFilter"/> — the only place a SignalR hub method call is
/// checked against <see cref="IRateLimiter"/>, since a connection's methods never re-enter the HTTP
/// pipeline <see cref="RateLimitMiddleware"/> guards.
/// </summary>
[Trait("Category", "Unit")]
public sealed class HubRateLimitFilterTests
{
    private static readonly MethodInfo SubscribeMethod = typeof(TestHub).GetMethod(nameof(TestHub.Subscribe))!;

    private readonly Mock<IRateLimiter> _rateLimiterMock = new();
    private readonly RateLimitConfig _config = new();

    private HubRateLimitFilter CreateFilter() =>
        new(_rateLimiterMock.Object, Options.Create(_config), NullLogger<HubRateLimitFilter>.Instance);

    private static HubInvocationContext CreateInvocationContext(Guid? projectId, string connectionId = "conn-1")
    {
        var httpContext = new DefaultHttpContext();
        if (projectId is { } id)
        {
            httpContext.Request.Headers["X-Project-Id"] = id.ToString();
        }

        var httpContextFeature = Mock.Of<IHttpContextFeature>(f => f.HttpContext == httpContext);
        var features = new Mock<IFeatureCollection>();
        features.Setup(f => f.Get<IHttpContextFeature>()).Returns(httpContextFeature);

        var callerContext = new Mock<HubCallerContext>();
        callerContext.Setup(c => c.ConnectionId).Returns(connectionId);
        callerContext.Setup(c => c.Features).Returns(features.Object);

        return new HubInvocationContext(
            callerContext.Object,
            Mock.Of<IServiceProvider>(),
            new TestHub(),
            SubscribeMethod,
            hubMethodArguments: []);
    }

    [Fact]
    public async Task InvokeMethodAsync_WhenDisabled_SkipsTheRateLimiterEntirely()
    {
        _config.Enabled = false;
        var filter = CreateFilter();
        var context = CreateInvocationContext(Guid.NewGuid());
        var nextCalled = false;

        await filter.InvokeMethodAsync(context, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(null);
        });

        nextCalled.Should().BeTrue();
        _rateLimiterMock.Verify(
            r => r.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task InvokeMethodAsync_WhenAllowed_CallsNext()
    {
        _rateLimiterMock
            .Setup(r => r.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RateLimitResult.Allowed(999, 1000, DateTimeOffset.UtcNow.AddMinutes(1)));
        var filter = CreateFilter();
        var context = CreateInvocationContext(Guid.NewGuid());
        var nextCalled = false;

        await filter.InvokeMethodAsync(context, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(null);
        });

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeMethodAsync_WhenDenied_ThrowsHubExceptionAndNeverCallsNext()
    {
        _rateLimiterMock
            .Setup(r => r.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RateLimitResult.Denied(1000, TimeSpan.FromSeconds(30), DateTimeOffset.UtcNow.AddSeconds(30)));
        var filter = CreateFilter();
        var context = CreateInvocationContext(Guid.NewGuid());
        var nextCalled = false;

        var act = () => filter.InvokeMethodAsync(context, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(null);
        }).AsTask();

        await act.Should().ThrowAsync<HubException>();
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeMethodAsync_KeysByProject_SoTwoProjectsDoNotShareABucket()
    {
        var seenKeys = new List<string>();
        _rateLimiterMock
            .Setup(r => r.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<string, int, CancellationToken>((key, _, _) => seenKeys.Add(key))
            .ReturnsAsync(RateLimitResult.Allowed(1, 1000, DateTimeOffset.UtcNow));
        var filter = CreateFilter();
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();

        await filter.InvokeMethodAsync(CreateInvocationContext(projectA), _ => ValueTask.FromResult<object?>(null));
        await filter.InvokeMethodAsync(CreateInvocationContext(projectB), _ => ValueTask.FromResult<object?>(null));

        seenKeys.Should().Equal($"project:{projectA}", $"project:{projectB}");
    }

    [Fact]
    public async Task InvokeMethodAsync_WithNoProjectHeader_FallsBackToTheConnectionId()
    {
        var seenKeys = new List<string>();
        _rateLimiterMock
            .Setup(r => r.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<string, int, CancellationToken>((key, _, _) => seenKeys.Add(key))
            .ReturnsAsync(RateLimitResult.Allowed(1, 1000, DateTimeOffset.UtcNow));
        var filter = CreateFilter();

        await filter.InvokeMethodAsync(
            CreateInvocationContext(projectId: null, connectionId: "conn-xyz"),
            _ => ValueTask.FromResult<object?>(null));

        seenKeys.Should().Equal("connection:conn-xyz");
    }

    /// <summary>Stands in for <c>MorphHub</c> — the filter never reads <c>HubInvocationContext.Hub</c>.</summary>
    private sealed class TestHub : Hub
    {
        public void Subscribe(string tableName)
        {
        }
    }
}
