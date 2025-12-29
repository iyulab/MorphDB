using System.Net;
using System.Net.Http.Json;
using MorphDB.Service.Controllers;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// Integration tests for Quota and Rate Limit API endpoints.
/// Tests quota usage, limits, and rate limiting status.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class QuotaApiTests : IAsyncLifetime
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;
    private Guid _projectId;

    public QuotaApiTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
    }

    public async Task InitializeAsync()
    {
        // Create a project first to provision the system schema
        var projectName = $"quota_test_{Guid.NewGuid():N}"[..30];
        var response = await _client.PostAsJsonAsync("/api/projects", new
        {
            Name = projectName
        });

        if (response.StatusCode == HttpStatusCode.Created)
        {
            var project = await response.Content.ReadFromJsonAsync<ProjectApiResponse>();
            _projectId = project!.Id;
        }
        else
        {
            // Fallback to tenant ID if project creation fails
            _projectId = _fixture.Api.TenantId;
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    #region Summary Endpoint Tests

    [Fact]
    public async Task GetSummary_ShouldReturnCombinedQuotaAndRateLimitInfo()
    {
        // Act
        var response = await _client.GetAsync($"/api/projects/{_projectId}/quota");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<QuotaSummaryApiResponse>();
        result.Should().NotBeNull();
        result!.Usage.Should().NotBeNull();
        result.Limits.Should().NotBeNull();
        result.RateLimit.Should().NotBeNull();
    }

    [Fact]
    public async Task GetSummary_ShouldReturnValidUsageData()
    {
        // Act
        var response = await _client.GetAsync($"/api/projects/{_projectId}/quota");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<QuotaSummaryApiResponse>();
        result.Should().NotBeNull();

        // Verify usage structure
        result!.Usage.ProjectId.Should().Be(_projectId);
        result.Usage.ApiRequests.Should().BeGreaterOrEqualTo(0);
        result.Usage.DataReads.Should().BeGreaterOrEqualTo(0);
        result.Usage.DataWrites.Should().BeGreaterOrEqualTo(0);
        result.Usage.StorageBytes.Should().BeGreaterOrEqualTo(0);
        result.Usage.BandwidthBytes.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task GetSummary_ShouldReturnValidLimits()
    {
        // Act
        var response = await _client.GetAsync($"/api/projects/{_projectId}/quota");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<QuotaSummaryApiResponse>();
        result.Should().NotBeNull();

        // Verify limits structure
        result!.Limits.ProjectId.Should().Be(_projectId);
        result.Limits.MaxApiRequests.Should().BeGreaterThan(0);
        result.Limits.MaxDataReads.Should().BeGreaterThan(0);
        result.Limits.MaxDataWrites.Should().BeGreaterThan(0);
        result.Limits.MaxStorageBytes.Should().BeGreaterThan(0);
        result.Limits.MaxBandwidthBytes.Should().BeGreaterThan(0);
        result.Limits.Tier.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetSummary_ShouldReturnValidRateLimitStatus()
    {
        // Act
        var response = await _client.GetAsync($"/api/projects/{_projectId}/quota");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<QuotaSummaryApiResponse>();
        result.Should().NotBeNull();

        // Verify rate limit structure
        result!.RateLimit.Key.Should().NotBeNullOrEmpty();
        result.RateLimit.Available.Should().BeGreaterOrEqualTo(0);
        result.RateLimit.Limit.Should().BeGreaterThan(0);
        result.RateLimit.WindowSeconds.Should().BeGreaterThan(0);
        result.RateLimit.RequestCount.Should().BeGreaterOrEqualTo(0);
    }

    #endregion

    #region Usage Endpoint Tests

    [Fact]
    public async Task GetUsage_WithNoParameters_ShouldReturnCurrentMonthUsage()
    {
        // Act
        var response = await _client.GetAsync($"/api/projects/{_projectId}/quota/usage");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<QuotaUsageApiResponse>();
        result.Should().NotBeNull();
        result!.ProjectId.Should().Be(_projectId);

        // Should be current month in yyyy-MM format
        var expectedPeriod = DateTimeOffset.UtcNow.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        result.Period.Should().Be(expectedPeriod);
    }

    [Fact]
    public async Task GetUsage_WithValidPeriod_ShouldReturnUsageForThatMonth()
    {
        // Arrange
        var period = "2024-01";

        // Act
        var response = await _client.GetAsync($"/api/projects/{_projectId}/quota/usage?period={period}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<QuotaUsageApiResponse>();
        result.Should().NotBeNull();
        result!.Period.Should().Be(period);
    }

    [Fact]
    public async Task GetUsage_WithInvalidPeriodFormat_ShouldReturnBadRequest()
    {
        // Arrange
        var invalidPeriod = "invalid-period";

        // Act
        var response = await _client.GetAsync($"/api/projects/{_projectId}/quota/usage?period={invalidPeriod}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Limits Endpoint Tests

    [Fact]
    public async Task GetLimits_ShouldReturnProjectLimits()
    {
        // Act
        var response = await _client.GetAsync($"/api/projects/{_projectId}/quota/limits");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<QuotaLimitsApiResponse>();
        result.Should().NotBeNull();
        result!.ProjectId.Should().Be(_projectId);
        result.MaxApiRequests.Should().BeGreaterThan(0);
        result.MaxDataReads.Should().BeGreaterThan(0);
        result.MaxDataWrites.Should().BeGreaterThan(0);
        result.MaxStorageBytes.Should().BeGreaterThan(0);
        result.MaxBandwidthBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetLimits_ShouldIncludeTier()
    {
        // Act
        var response = await _client.GetAsync($"/api/projects/{_projectId}/quota/limits");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<QuotaLimitsApiResponse>();
        result.Should().NotBeNull();
        result!.Tier.Should().NotBeNullOrEmpty();
        // Valid tiers based on ROADMAP: Free, Pro, Team, Enterprise
        result.Tier.ToLowerInvariant().Should().BeOneOf("free", "pro", "team", "enterprise", "default");
    }

    #endregion

    #region Rate Limit Endpoint Tests

    [Fact]
    public async Task GetRateLimitStatus_ShouldReturnCurrentStatus()
    {
        // Act
        var response = await _client.GetAsync($"/api/projects/{_projectId}/quota/rate-limit");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<RateLimitStatusApiResponse>();
        result.Should().NotBeNull();
        result!.Key.Should().Contain(_projectId.ToString());
        result.Available.Should().BeGreaterOrEqualTo(0);
        result.Limit.Should().BeGreaterThan(0);
        result.WindowSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetRateLimitStatus_AfterRequests_ShouldShowDecrementedAvailable()
    {
        // Arrange - Make several API requests
        for (int i = 0; i < 3; i++)
        {
            await _client.GetAsync($"/api/projects/{_projectId}/quota/limits");
        }

        // Act
        var response = await _client.GetAsync($"/api/projects/{_projectId}/quota/rate-limit");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<RateLimitStatusApiResponse>();
        result.Should().NotBeNull();
        result!.RequestCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetRateLimitStatus_ShouldHaveValidResetTime()
    {
        // Act
        var response = await _client.GetAsync($"/api/projects/{_projectId}/quota/rate-limit");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<RateLimitStatusApiResponse>();
        result.Should().NotBeNull();
        result!.ResetAt.Should().BeAfter(DateTimeOffset.UtcNow.AddSeconds(-1));
    }

    #endregion

    #region Rate Limit Headers Tests

    [Fact]
    public async Task ApiRequests_ShouldIncludeRateLimitHeaders()
    {
        // Act
        var response = await _client.GetAsync("/api/schema/tables");

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        // Standard rate limit headers
        response.Headers.Contains("X-RateLimit-Limit").Should().BeTrue();
        response.Headers.Contains("X-RateLimit-Remaining").Should().BeTrue();
        response.Headers.Contains("X-RateLimit-Reset").Should().BeTrue();
    }

    [Fact]
    public async Task RateLimitHeaders_ShouldContainValidValues()
    {
        // Act
        var response = await _client.GetAsync("/api/schema/tables");

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        if (response.Headers.TryGetValues("X-RateLimit-Limit", out var limitValues))
        {
            int.TryParse(limitValues.FirstOrDefault(), out var limit).Should().BeTrue();
            limit.Should().BeGreaterThan(0);
        }

        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues))
        {
            int.TryParse(remainingValues.FirstOrDefault(), out var remaining).Should().BeTrue();
            remaining.Should().BeGreaterOrEqualTo(0);
        }

        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues))
        {
            long.TryParse(resetValues.FirstOrDefault(), out var reset).Should().BeTrue();
            reset.Should().BeGreaterThan(0);
        }
    }

    #endregion
}
