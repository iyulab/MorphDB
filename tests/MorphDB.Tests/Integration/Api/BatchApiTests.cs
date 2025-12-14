using System.Net;
using System.Net.Http.Json;
using MorphDB.Service.Controllers;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// Integration tests for Batch API endpoints.
/// Note: Tests are skipped due to .NET 10 preview PipeWriter.UnflushedBytes compatibility issue.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class BatchApiTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;

    public BatchApiTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
    }

    private async Task<string> SetupTestTableAsync()
    {
        var tableName = $"batch_test_{Guid.NewGuid():N}"[..30];
        await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns =
            [
                new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "email", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "score", Type = "integer", Nullable = true }
            ]
        });
        return tableName;
    }

    #region Batch Operations

    [Fact]
    public async Task ExecuteBatch_WithMixedOperations_ShouldExecuteAll()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();

        // First insert a record to update/delete later
        var insertResponse = await _client.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["name"] = "Existing",
            ["email"] = "existing@example.com",
            ["score"] = 100
        });
        var existingRecord = await insertResponse.Content.ReadFromJsonAsync<DataRecordResponse>();

        var batchRequest = new BatchRequest
        {
            Operations =
            [
                new BatchOperation
                {
                    Method = "INSERT",
                    Table = tableName,
                    Data = new Dictionary<string, object?>
                    {
                        ["name"] = "New User",
                        ["email"] = "new@example.com",
                        ["score"] = 50
                    }
                },
                new BatchOperation
                {
                    Method = "UPDATE",
                    Table = tableName,
                    Id = existingRecord!.Id,
                    Data = new Dictionary<string, object?>
                    {
                        ["score"] = 200
                    }
                }
            ]
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/batch/data", batchRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<BatchResponse>();
        result.Should().NotBeNull();
        result!.Results.Should().HaveCount(2);
        result.SuccessCount.Should().Be(2);
        result.FailureCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteBatch_WithEmptyOperations_ShouldReturnBadRequest()
    {
        // Arrange
        var batchRequest = new BatchRequest { Operations = [] };

        // Act
        var response = await _client.PostAsJsonAsync("/api/batch/data", batchRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExecuteBatch_WithInvalidOperation_ShouldReportFailure()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();

        var batchRequest = new BatchRequest
        {
            Operations =
            [
                new BatchOperation
                {
                    Method = "UPDATE",
                    Table = tableName,
                    // Missing ID for update
                    Data = new Dictionary<string, object?>
                    {
                        ["name"] = "Test"
                    }
                }
            ]
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/batch/data", batchRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<BatchResponse>();
        result!.Results[0].Success.Should().BeFalse();
        result.FailureCount.Should().Be(1);
    }

    #endregion

    #region Bulk Insert

    [Fact]
    public async Task BulkInsert_WithValidRecords_ShouldInsertAll()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var records = new List<Dictionary<string, object?>>
        {
            new() { ["name"] = "User 1", ["email"] = "user1@example.com", ["score"] = 10 },
            new() { ["name"] = "User 2", ["email"] = "user2@example.com", ["score"] = 20 },
            new() { ["name"] = "User 3", ["email"] = "user3@example.com", ["score"] = 30 }
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/batch/data/{tableName}/insert", records);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<BatchResponse>();
        result!.SuccessCount.Should().Be(3);
        result.Results.Should().HaveCount(3);
        result.Results.Should().AllSatisfy(r => r.Success.Should().BeTrue());
    }

    [Fact]
    public async Task BulkInsert_WithEmptyRecords_ShouldReturnBadRequest()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var records = new List<Dictionary<string, object?>>();

        // Act
        var response = await _client.PostAsJsonAsync($"/api/batch/data/{tableName}/insert", records);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Bulk Update

    [Fact]
    public async Task BulkUpdate_WithValidFilter_ShouldUpdateAll()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();

        // Insert records first
        await _client.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["name"] = "User 1",
            ["email"] = "u1@example.com",
            ["score"] = 10
        });
        await _client.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["name"] = "User 2",
            ["email"] = "u2@example.com",
            ["score"] = 20
        });

        var updateRequest = new BulkUpdateRequest
        {
            Data = new Dictionary<string, object?> { ["score"] = 100 },
            Filter = "score:lt:50"
        };

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/batch/data/{tableName}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<BatchResponse>();
        result.Should().NotBeNull();
        result!.Results.Should().HaveCount(1);
        result.Results[0].Success.Should().BeTrue();
    }

    #endregion

    #region Bulk Delete

    [Fact]
    public async Task BulkDelete_WithValidFilter_ShouldDeleteAll()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();

        // Insert records first
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        await _client.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["id"] = id1,
            ["name"] = "User 1",
            ["email"] = "u1@example.com",
            ["score"] = 10
        });
        await _client.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["id"] = id2,
            ["name"] = "User 2",
            ["email"] = "u2@example.com",
            ["score"] = 20
        });

        // Act - Delete using filter
        var response = await _client.DeleteAsync($"/api/batch/data/{tableName}?filter=score:lt:50");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<BatchResponse>();
        result.Should().NotBeNull();
        result!.Results.Should().HaveCount(1);
        result.Results[0].Success.Should().BeTrue();

        // Verify deletion
        var getResponse1 = await _client.GetAsync($"/api/data/{tableName}/{id1}");
        getResponse1.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task BulkDelete_WithoutFilter_ShouldReturnBadRequest()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();

        // Act
        var response = await _client.DeleteAsync($"/api/batch/data/{tableName}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Upsert

    [Fact]
    public async Task Upsert_NewRecord_ShouldInsert()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var request = new UpsertRequest
        {
            Data = new Dictionary<string, object?>
            {
                ["name"] = "Upsert User",
                ["email"] = "upsert@example.com",
                ["score"] = 50
            },
            KeyColumns = ["email"]
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/batch/data/{tableName}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<DataRecordResponse>();
        result!.Data["name"]?.ToString().Should().Be("Upsert User");
    }

    [Fact]
    public async Task Upsert_ExistingRecord_ShouldUpdate()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();

        // Insert first
        await _client.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["name"] = "Original",
            ["email"] = "existing@example.com",
            ["score"] = 10
        });

        var request = new UpsertRequest
        {
            Data = new Dictionary<string, object?>
            {
                ["name"] = "Updated",
                ["email"] = "existing@example.com",
                ["score"] = 100
            },
            KeyColumns = ["email"]
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/batch/data/{tableName}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<DataRecordResponse>();
        result!.Data["name"]?.ToString().Should().Be("Updated");
    }

    [Fact]
    public async Task Upsert_WithoutKeyColumns_ShouldReturnBadRequest()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var request = new UpsertRequest
        {
            Data = new Dictionary<string, object?>
            {
                ["name"] = "Test"
            },
            KeyColumns = []
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/batch/data/{tableName}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion
}
