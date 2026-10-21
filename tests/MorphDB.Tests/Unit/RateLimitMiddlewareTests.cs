using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MorphDB.Core.Abstractions;
using MorphDB.Service.Middleware;
using MorphDB.Service.RateLimiting;

namespace MorphDB.Tests.Unit;

/// <summary>
/// <see cref="RateLimitMiddleware"/> over the real <see cref="MemoryRateLimiter"/>: off unless configured,
/// and once on, a quota per project that answers 429 past its limit.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RateLimitMiddlewareTests
{
    private static readonly Guid Project = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static async Task<(int Status, IHeaderDictionary Headers, int Reached)> SendAsync(RateLimitConfig config, int requests, Guid project)
    {
        var reached = 0;
        using var limiter = new MemoryRateLimiter(Options.Create(config), NullLogger<MemoryRateLimiter>.Instance);
        var middleware = new RateLimitMiddleware(_ => { reached++; return Task.CompletedTask; }, Options.Create(config), NullLogger<RateLimitMiddleware>.Instance);

        HttpContext last = null!;
        for (var i = 0; i < requests; i++)
        {
            last = new DefaultHttpContext();
            last.Request.Path = "/api/data/customers";
            last.Request.Headers["X-Project-Id"] = project.ToString();
            last.Response.Body = new MemoryStream();
            await middleware.InvokeAsync(last, limiter);
        }

        return (last.Response.StatusCode, last.Response.Headers, reached);
    }

    [Fact]
    public void Rate_limiting_is_off_unless_configured()
    {
        Assert.False(new RateLimitConfig().Enabled);
    }

    [Fact]
    public async Task Off_by_default_every_request_passes_without_quota_headers()
    {
        var (status, headers, reached) = await SendAsync(new RateLimitConfig { DefaultLimit = 2 }, 5, Project);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Equal(5, reached);
        Assert.False(headers.ContainsKey("X-RateLimit-Limit"));
    }

    [Fact]
    public async Task Once_on_a_project_past_its_quota_is_answered_429_with_retry_after()
    {
        var (status, headers, reached) = await SendAsync(new RateLimitConfig { Enabled = true, DefaultLimit = 2 }, 3, Project);

        Assert.Equal(StatusCodes.Status429TooManyRequests, status);
        Assert.Equal(2, reached);
        Assert.True(headers.ContainsKey("Retry-After"));
        Assert.Equal("2", headers["X-RateLimit-Limit"]);
    }
}
