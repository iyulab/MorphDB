using System.Net;
using System.Net.Http.Json;
using MorphDB.Service.Controllers;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// Integration tests for Audit API endpoints.
/// Tests audit log querying, filtering, and statistics.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class AuditApiTests : IAsyncLifetime
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;
    private Guid _projectId;

    public AuditApiTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
    }

    public async Task InitializeAsync()
    {
        // Create a project first to provision the system schema with _audit_logs table
        var projectName = $"audit_test_{Guid.NewGuid():N}"[..30];
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

    #region Query Logs Tests

    [Fact]
    public async Task QueryLogs_WithNoFilters_ShouldReturnPagedResults()
    {
        // Act
        var response = await _client.GetAsync($"/api/projects/{_projectId}/audit/logs");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuditLogPageApiResponse>();
        result.Should().NotBeNull();
        result!.Page.Should().BeGreaterThanOrEqualTo(1);
        result.PageSize.Should().BeGreaterThan(0);
        result.Items.Should().NotBeNull();
    }

    [Fact]
    public async Task QueryLogs_WithCategoryFilter_ShouldReturnFilteredResults()
    {
        // Act - Filter by Data category (1)
        var response = await _client.GetAsync($"/api/projects/{_projectId}/audit/logs?category=1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuditLogPageApiResponse>();
        result.Should().NotBeNull();
        // All returned items should have "data" category if any exist
        foreach (var item in result!.Items)
        {
            item.Category.Should().Be("data");
        }
    }

    [Fact]
    public async Task QueryLogs_WithSeverityFilter_ShouldReturnFilteredResults()
    {
        // Act - Filter by minimum severity Warning (2)
        var response = await _client.GetAsync($"/api/projects/{_projectId}/audit/logs?minSeverity=2");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuditLogPageApiResponse>();
        result.Should().NotBeNull();
        // Results should only contain Warning, Error, or Critical severity
        var validSeverities = new[] { "warning", "error", "critical" };
        foreach (var item in result!.Items)
        {
            item.Severity.Should().BeOneOf(validSeverities);
        }
    }

    [Fact]
    public async Task QueryLogs_WithTimeRangeFilter_ShouldReturnFilteredResults()
    {
        // Arrange
        var from = DateTimeOffset.UtcNow.AddHours(-1);
        var to = DateTimeOffset.UtcNow;

        // URL encode the dates to handle '+' in timezone offset
        var fromStr = Uri.EscapeDataString(from.ToString("O"));
        var toStr = Uri.EscapeDataString(to.ToString("O"));

        // Act
        var response = await _client.GetAsync(
            $"/api/projects/{_projectId}/audit/logs?from={fromStr}&to={toStr}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuditLogPageApiResponse>();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task QueryLogs_WithPagination_ShouldReturnCorrectPage()
    {
        // Act
        var response = await _client.GetAsync(
            $"/api/projects/{_projectId}/audit/logs?page=1&pageSize=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuditLogPageApiResponse>();
        result.Should().NotBeNull();
        result!.Page.Should().Be(1);
        result.PageSize.Should().Be(10);
        result.Items.Should().HaveCountLessThanOrEqualTo(10);
    }

    [Fact]
    public async Task QueryLogs_WithDescendingOrder_ShouldReturnSortedResults()
    {
        // Act
        var response = await _client.GetAsync(
            $"/api/projects/{_projectId}/audit/logs?orderBy=timestamp&descending=true");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuditLogPageApiResponse>();
        result.Should().NotBeNull();

        // Verify descending order if there are multiple items
        if (result!.Items.Count > 1)
        {
            for (int i = 0; i < result.Items.Count - 1; i++)
            {
                result.Items[i].Timestamp.Should()
                    .BeOnOrAfter(result.Items[i + 1].Timestamp);
            }
        }
    }

    [Fact]
    public async Task QueryLogs_WithActorFilter_ShouldReturnFilteredResults()
    {
        // Act
        var response = await _client.GetAsync(
            $"/api/projects/{_projectId}/audit/logs?actorId=test-actor");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuditLogPageApiResponse>();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task QueryLogs_WithResourceFilter_ShouldReturnFilteredResults()
    {
        // Act
        var response = await _client.GetAsync(
            $"/api/projects/{_projectId}/audit/logs?resourceType=table&resourceId=test_table");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuditLogPageApiResponse>();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task QueryLogs_WithActionFilter_ShouldReturnFilteredResults()
    {
        // Act
        var response = await _client.GetAsync(
            $"/api/projects/{_projectId}/audit/logs?action=create");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuditLogPageApiResponse>();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task QueryLogs_WithSearchText_ShouldReturnFilteredResults()
    {
        // Act
        var response = await _client.GetAsync(
            $"/api/projects/{_projectId}/audit/logs?searchText=test");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuditLogPageApiResponse>();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task QueryLogs_WithInvalidPageSize_ShouldClampToValidRange()
    {
        // Act - Request page size larger than max (100)
        var response = await _client.GetAsync(
            $"/api/projects/{_projectId}/audit/logs?pageSize=200");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuditLogPageApiResponse>();
        result.Should().NotBeNull();
        result!.PageSize.Should().BeLessThanOrEqualTo(100);
    }

    #endregion

    #region Get Log By ID Tests

    [Fact]
    public async Task GetLog_WithNonExistentId_ShouldReturnNotFound()
    {
        // Arrange
        var nonExistentLogId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync(
            $"/api/projects/{_projectId}/audit/logs/{nonExistentLogId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetLog_WithInvalidGuid_ShouldReturnNotFound()
    {
        // Act - Invalid GUID format should return 404 (route won't match)
        var response = await _client.GetAsync(
            $"/api/projects/{_projectId}/audit/logs/invalid-guid");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Get Stats Tests

    [Fact]
    public async Task GetStats_WithNoTimeRange_ShouldReturnStats()
    {
        // Act
        var response = await _client.GetAsync($"/api/projects/{_projectId}/audit/stats");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuditStatsApiResponse>();
        result.Should().NotBeNull();
        result!.TotalEvents.Should().BeGreaterThanOrEqualTo(0);
        result.ByCategory.Should().NotBeNull();
        result.BySeverity.Should().NotBeNull();
        result.TopActors.Should().NotBeNull();
        result.TopActions.Should().NotBeNull();
        result.ErrorRate.Should().BeGreaterThanOrEqualTo(0).And.BeLessThanOrEqualTo(100);
    }

    [Fact]
    public async Task GetStats_WithTimeRange_ShouldReturnStatsForPeriod()
    {
        // Arrange
        var from = DateTimeOffset.UtcNow.AddDays(-7);
        var to = DateTimeOffset.UtcNow;

        // URL encode the dates to handle '+' in timezone offset
        var fromStr = Uri.EscapeDataString(from.ToString("O"));
        var toStr = Uri.EscapeDataString(to.ToString("O"));

        // Act
        var response = await _client.GetAsync(
            $"/api/projects/{_projectId}/audit/stats?from={fromStr}&to={toStr}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuditStatsApiResponse>();
        result.Should().NotBeNull();
        result!.From.Should().BeCloseTo(from, TimeSpan.FromSeconds(1));
        result.To.Should().BeCloseTo(to, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetStats_CategoryBreakdown_ShouldHaveValidCategories()
    {
        // Act
        var response = await _client.GetAsync($"/api/projects/{_projectId}/audit/stats");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuditStatsApiResponse>();
        result.Should().NotBeNull();

        // Valid categories should be lowercase enum names
        var validCategories = new[] { "auth", "data", "schema", "admin", "security", "system" };
        foreach (var key in result!.ByCategory.Keys)
        {
            key.Should().BeOneOf(validCategories);
        }
    }

    [Fact]
    public async Task GetStats_SeverityBreakdown_ShouldHaveValidSeverities()
    {
        // Act
        var response = await _client.GetAsync($"/api/projects/{_projectId}/audit/stats");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuditStatsApiResponse>();
        result.Should().NotBeNull();

        // Valid severities should be lowercase enum names
        var validSeverities = new[] { "debug", "info", "warning", "error", "critical" };
        foreach (var key in result!.BySeverity.Keys)
        {
            key.Should().BeOneOf(validSeverities);
        }
    }

    #endregion
}
