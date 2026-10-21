using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
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
    public void Settings_bind_from_configuration_including_a_limit_per_project()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:DefaultLimit"] = "50",
            ["RateLimiting:DefaultWindow"] = "00:00:10",
            [$"RateLimiting:ProjectLimits:{Project}"] = "5",
        }).Build();

        var config = new RateLimitConfig();
        configuration.GetSection("RateLimiting").Bind(config);

        Assert.True(config.Enabled);
        Assert.Equal(50, config.DefaultLimit);
        Assert.Equal(TimeSpan.FromSeconds(10), config.DefaultWindow);
        Assert.Equal(5, config.ProjectLimits[Project.ToString()]);
    }

    [Fact]
    public async Task A_limit_set_for_one_project_replaces_the_default_for_it()
    {
        var config = new RateLimitConfig { Enabled = true, DefaultLimit = 100, ProjectLimits = { [Project.ToString("B").ToUpperInvariant()] = 1 } };

        var (status, _, reached) = await SendAsync(config, 2, Project);

        Assert.Equal(StatusCodes.Status429TooManyRequests, status);
        Assert.Equal(1, reached);
    }

    [Fact]
    public void A_project_limit_keyed_by_something_other_than_an_id_stops_the_start()
    {
        var config = new RateLimitConfig { Enabled = true, ProjectLimits = { ["my-app"] = 5 } };

        Assert.Throws<InvalidOperationException>(() => new MemoryRateLimiter(Options.Create(config), NullLogger<MemoryRateLimiter>.Instance));
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
