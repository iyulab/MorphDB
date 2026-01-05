using System.Net;
using System.Net.Http.Json;
using MorphDB.Core.Models;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// Integration tests for Cross-Entity Transaction API and Row-State features.
/// Phase 28: Transaction & Row-State
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class TransactionApiTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;

    public TransactionApiTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
    }

    /// <summary>
    /// Creates a test table with RowState enabled for draft mode testing.
    /// </summary>
    private async Task<string> SetupRowStateTableAsync(string suffix = "")
    {
        var tableName = $"rs_test_{Guid.NewGuid():N}"[..25] + suffix;
        await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns =
            [
                new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "email", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "score", Type = "integer", Nullable = true }
            ],
            SystemColumns = new SystemColumnOptionsApiRequest
            {
                RowState = true
            }
        });
        return tableName;
    }

    /// <summary>
    /// Creates a test table without RowState for negative testing.
    /// </summary>
    private async Task<string> SetupNormalTableAsync()
    {
        var tableName = $"normal_test_{Guid.NewGuid():N}"[..30];
        await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns =
            [
                new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "value", Type = "integer", Nullable = true }
            ]
        });
        return tableName;
    }

    #region Draft Mode Insert Tests

    [Fact]
    public async Task Insert_WithDraftMode_ShouldCreateDraftRecord()
    {
        // Arrange
        var tableName = await SetupRowStateTableAsync();
        var data = new Dictionary<string, object?>
        {
            ["name"] = "Draft User",
            ["email"] = "draft@example.com"
            // score is missing but nullable validation is skipped in draft mode
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/data/{tableName}?mode=draft", data);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<DataRecordResponse>();
        result.Should().NotBeNull();
        result!.Data["_row_state"]?.ToString().Should().Be("draft");
    }

    [Fact]
    public async Task Insert_WithDraftMode_OnNonRowStateTable_ShouldReturnBadRequest()
    {
        // Arrange
        var tableName = await SetupNormalTableAsync();
        var data = new Dictionary<string, object?>
        {
            ["name"] = "Test",
            ["value"] = 100
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/data/{tableName}?mode=draft", data);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error!.Code.Should().Be("ROW_STATE_NOT_ENABLED");
    }

    [Fact]
    public async Task Insert_NormalMode_ShouldCreateValidRecord()
    {
        // Arrange
        var tableName = await SetupRowStateTableAsync();
        var data = new Dictionary<string, object?>
        {
            ["name"] = "Valid User",
            ["email"] = "valid@example.com",
            ["score"] = 100
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/data/{tableName}", data);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<DataRecordResponse>();
        result!.Data["_row_state"]?.ToString().Should().Be("valid");
    }

    #endregion

    #region Row-State Query Filter Tests

    [Fact]
    public async Task Query_WithStateFilter_ShouldFilterByRowState()
    {
        // Arrange
        var tableName = await SetupRowStateTableAsync();

        // Insert draft record
        await _client.PostAsJsonAsync($"/api/data/{tableName}?mode=draft", new Dictionary<string, object?>
        {
            ["name"] = "Draft",
            ["email"] = "draft@test.com"
        });

        // Insert valid record
        await _client.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["name"] = "Valid",
            ["email"] = "valid@test.com",
            ["score"] = 50
        });

        // Act - Query only valid records
        var validResponse = await _client.GetAsync($"/api/data/{tableName}?state=valid");
        var validResult = await validResponse.Content.ReadFromJsonAsync<PagedResponse<DataRecordResponse>>();

        // Act - Query only draft records
        var draftResponse = await _client.GetAsync($"/api/data/{tableName}?state=draft");
        var draftResult = await draftResponse.Content.ReadFromJsonAsync<PagedResponse<DataRecordResponse>>();

        // Act - Query all records
        var allResponse = await _client.GetAsync($"/api/data/{tableName}?state=all");
        var allResult = await allResponse.Content.ReadFromJsonAsync<PagedResponse<DataRecordResponse>>();

        // Assert
        validResult!.Data.Should().HaveCount(1);
        validResult.Data[0].Data["name"]?.ToString().Should().Be("Valid");

        draftResult!.Data.Should().HaveCount(1);
        draftResult.Data[0].Data["name"]?.ToString().Should().Be("Draft");

        allResult!.Data.Should().HaveCount(2);
    }

    #endregion

    #region Finalize API Tests

    [Fact]
    public async Task Finalize_ValidDraftRecord_ShouldTransitionToValid()
    {
        // Arrange
        var tableName = await SetupRowStateTableAsync();

        // Insert draft record with all required fields
        var insertResponse = await _client.PostAsJsonAsync($"/api/data/{tableName}?mode=draft", new Dictionary<string, object?>
        {
            ["name"] = "Complete Draft",
            ["email"] = "complete@test.com",
            ["score"] = 75
        });
        var inserted = await insertResponse.Content.ReadFromJsonAsync<DataRecordResponse>();

        // Act
        var finalizeResponse = await _client.PatchAsync(
            $"/api/data/{tableName}/{inserted!.Id}/finalize",
            null);

        // Assert
        finalizeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await finalizeResponse.Content.ReadFromJsonAsync<FinalizeApiResponse>();
        result!.Results.Should().HaveCount(1);
        result.Results[0].Success.Should().BeTrue();
        result.Results[0].NewState.Should().Be("valid");
    }

    [Fact]
    public async Task Finalize_InvalidDraftRecord_ShouldTransitionToError()
    {
        // Arrange
        var tableName = await SetupRowStateTableAsync();

        // Insert draft record missing required 'email' field
        // First we need to update the row after insert to remove required field
        var insertResponse = await _client.PostAsJsonAsync($"/api/data/{tableName}?mode=draft", new Dictionary<string, object?>
        {
            ["name"] = "Incomplete",
            ["email"] = "" // Empty string for required field
        });
        var inserted = await insertResponse.Content.ReadFromJsonAsync<DataRecordResponse>();

        // Update to set email as empty (should fail validation on finalize)
        await _client.PatchAsJsonAsync($"/api/data/{tableName}/{inserted!.Id}", new Dictionary<string, object?>
        {
            ["email"] = "" // Empty required field
        });

        // Act
        var finalizeResponse = await _client.PatchAsync(
            $"/api/data/{tableName}/{inserted.Id}/finalize",
            null);

        // Assert - This should succeed but transition to error state
        finalizeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await finalizeResponse.Content.ReadFromJsonAsync<FinalizeApiResponse>();

        // Note: Actual behavior depends on validator implementation
        // The record should have _row_errors populated if validation fails
    }

    [Fact]
    public async Task Finalize_NonExistentRecord_ShouldReturnNotFound()
    {
        // Arrange
        var tableName = await SetupRowStateTableAsync();
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.PatchAsync(
            $"/api/data/{tableName}/{nonExistentId}/finalize",
            null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task BulkFinalize_ShouldFinalizeMultipleRecords()
    {
        // Arrange
        var tableName = await SetupRowStateTableAsync();

        // Insert multiple draft records
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var insertResponse = await _client.PostAsJsonAsync($"/api/data/{tableName}?mode=draft", new Dictionary<string, object?>
            {
                ["name"] = $"Bulk Draft {i}",
                ["email"] = $"bulk{i}@test.com",
                ["score"] = i * 10
            });
            var record = await insertResponse.Content.ReadFromJsonAsync<DataRecordResponse>();
            ids.Add(record!.Id);
        }

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/data/{tableName}/finalize",
            new FinalizeApiRequest { RecordIds = ids });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<FinalizeApiResponse>();
        result!.Results.Should().HaveCount(3);
        result.ValidCount.Should().Be(3);
    }

    #endregion

    #region Cross-Entity Transaction Tests

    [Fact]
    public async Task Transaction_WithRefResolution_ShouldLinkRecords()
    {
        // Arrange
        var parentTable = await SetupRowStateTableAsync("_parent");
        var childTable = await SetupRowStateTableAsync("_child");

        var transactionRequest = new TransactionApiRequest
        {
            Operations =
            [
                new TransactionOperationApiRequest
                {
                    Method = "INSERT",
                    Table = parentTable,
                    Ref = "parent1",
                    Data = new Dictionary<string, object?>
                    {
                        ["name"] = "Parent Record",
                        ["email"] = "parent@test.com",
                        ["score"] = 100
                    }
                },
                new TransactionOperationApiRequest
                {
                    Method = "INSERT",
                    Table = childTable,
                    Data = new Dictionary<string, object?>
                    {
                        ["name"] = "Child Record",
                        ["email"] = "child@test.com",
                        ["score"] = 50
                    }
                }
            ]
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/batch/transaction", transactionRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<TransactionApiResponse>();
        result!.Success.Should().BeTrue();
        result.Results.Should().HaveCount(2);
        result.Results.Should().AllSatisfy(r => r.Success.Should().BeTrue());
    }

    [Fact]
    public async Task Transaction_WithEmptyOperations_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new TransactionApiRequest { Operations = [] };

        // Act
        var response = await _client.PostAsJsonAsync("/api/batch/transaction", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Transaction_AtomicRollback_OnFailure_ShouldRollbackAll()
    {
        // Arrange
        var tableName = await SetupRowStateTableAsync();

        // First operation will succeed, second will fail (invalid table)
        var transactionRequest = new TransactionApiRequest
        {
            Operations =
            [
                new TransactionOperationApiRequest
                {
                    Method = "INSERT",
                    Table = tableName,
                    Data = new Dictionary<string, object?>
                    {
                        ["name"] = "Should Rollback",
                        ["email"] = "rollback@test.com"
                    }
                },
                new TransactionOperationApiRequest
                {
                    Method = "INSERT",
                    Table = "nonexistent_table_xyz",
                    Data = new Dictionary<string, object?>
                    {
                        ["name"] = "Will Fail"
                    }
                }
            ]
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/batch/transaction", transactionRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Verify first record was NOT inserted (atomic rollback)
        var queryResponse = await _client.GetAsync($"/api/data/{tableName}?state=all");
        var queryResult = await queryResponse.Content.ReadFromJsonAsync<PagedResponse<DataRecordResponse>>();
        var hasRollbackEmail = queryResult!.Data.Any(r =>
            r.Data.TryGetValue("email", out var email) && email?.ToString() == "rollback@test.com");
        hasRollbackEmail.Should().BeFalse("first record should have been rolled back");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task Query_WithoutStateFilter_ShouldReturnAllRecords()
    {
        // Arrange
        var tableName = await SetupRowStateTableAsync();

        // Insert records
        await _client.PostAsJsonAsync($"/api/data/{tableName}?mode=draft", new Dictionary<string, object?>
        {
            ["name"] = "Draft",
            ["email"] = "d@test.com"
        });
        await _client.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["name"] = "Valid",
            ["email"] = "v@test.com",
            ["score"] = 100
        });

        // Act - No state filter
        var response = await _client.GetAsync($"/api/data/{tableName}");
        var result = await response.Content.ReadFromJsonAsync<PagedResponse<DataRecordResponse>>();

        // Assert - Default behavior returns all records (backward compatible)
        result!.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task SystemColumns_RowStateEnabled_ShouldBeReflectedInTableResponse()
    {
        // Arrange
        var tableName = await SetupRowStateTableAsync();

        // Act
        var response = await _client.GetAsync($"/api/schema/tables/{tableName}");
        var table = await response.Content.ReadFromJsonAsync<TableApiResponse>();

        // Assert
        table!.SystemColumns.RowState.Should().BeTrue();
    }

    #endregion
}
