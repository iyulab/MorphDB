using System.Net;
using System.Net.Http.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// Integration tests for Data API endpoints.
/// Note: Tests are skipped due to .NET 10 preview PipeWriter.UnflushedBytes compatibility issue.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class DataApiTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;

    public DataApiTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
    }

    private async Task<string> SetupTestTableAsync()
    {
        var tableName = $"data_test_{Guid.NewGuid():N}"[..30];
        await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns =
            [
                new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "email", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "age", Type = "integer", Nullable = true },
                new CreateColumnApiRequest { Name = "is_active", Type = "boolean", Nullable = false }
            ]
        });
        return tableName;
    }

    #region Insert Operations

    [Fact]
    public async Task Insert_WithValidData_ShouldReturnCreated()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var data = new Dictionary<string, object?>
        {
            ["name"] = "John Doe",
            ["email"] = "john@example.com",
            ["age"] = 30,
            ["is_active"] = true
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/data/{tableName}", data);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<DataRecordResponse>();
        result.Should().NotBeNull();
        result!.Id.Should().NotBeEmpty();
        result.Data["name"]?.ToString().Should().Be("John Doe");
    }

    [Fact]
    public async Task Insert_WithCustomId_ShouldUseProvidedId()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var customId = Guid.NewGuid();
        var data = new Dictionary<string, object?>
        {
            ["_id"] = customId,
            ["name"] = "Jane Doe",
            ["email"] = "jane@example.com",
            ["age"] = 25,
            ["is_active"] = true
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/data/{tableName}", data);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<DataRecordResponse>();
        result!.Id.Should().Be(customId);
    }

    #endregion

    #region Query Operations

    [Fact]
    public async Task Query_AllRecords_ShouldReturnPagedResults()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();

        // Insert test data
        for (var i = 0; i < 5; i++)
        {
            await _client.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
            {
                ["name"] = $"User {i}",
                ["email"] = $"user{i}@example.com",
                ["age"] = 20 + i,
                ["is_active"] = true
            });
        }

        // Act
        var response = await _client.GetAsync($"/api/data/{tableName}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResponse<DataRecordResponse>>();
        result.Should().NotBeNull();
        result!.Data.Should().HaveCount(5);
        result.Pagination.TotalCount.Should().Be(5);
    }

    [Fact]
    public async Task Query_WithPagination_ShouldReturnCorrectPage()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();

        for (var i = 0; i < 10; i++)
        {
            await _client.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
            {
                ["name"] = $"User {i}",
                ["email"] = $"user{i}@example.com",
                ["age"] = 20 + i,
                ["is_active"] = true
            });
        }

        // Act
        var response = await _client.GetAsync($"/api/data/{tableName}?page=2&pageSize=3");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResponse<DataRecordResponse>>();
        result!.Data.Should().HaveCount(3);
        result.Pagination.Page.Should().Be(2);
        result.Pagination.PageSize.Should().Be(3);
    }

    [Fact]
    public async Task Query_WithFilter_ShouldReturnFilteredResults()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();

        await _client.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["name"] = "Alice",
            ["email"] = "alice@example.com",
            ["age"] = 25,
            ["is_active"] = true
        });
        await _client.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["name"] = "Bob",
            ["email"] = "bob@example.com",
            ["age"] = 30,
            ["is_active"] = false
        });

        // Act
        var response = await _client.GetAsync($"/api/data/{tableName}?filter=is_active:eq:true");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResponse<DataRecordResponse>>();
        result!.Data.Should().HaveCount(1);
        result.Data[0].Data["name"]?.ToString().Should().Be("Alice");
    }

    [Fact]
    public async Task Query_WithOrdering_ShouldReturnOrderedResults()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();

        await _client.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["name"] = "Charlie",
            ["email"] = "charlie@example.com",
            ["age"] = 35,
            ["is_active"] = true
        });
        await _client.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["name"] = "Alice",
            ["email"] = "alice@example.com",
            ["age"] = 25,
            ["is_active"] = true
        });

        // Act
        var response = await _client.GetAsync($"/api/data/{tableName}?orderBy=name:asc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResponse<DataRecordResponse>>();
        result!.Data[0].Data["name"]?.ToString().Should().Be("Alice");
        result.Data[1].Data["name"]?.ToString().Should().Be("Charlie");
    }

    [Fact]
    public async Task Query_WithSelect_ShouldReturnOnlySelectedColumns()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();

        await _client.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["name"] = "Alice",
            ["email"] = "alice@example.com",
            ["age"] = 25,
            ["is_active"] = true
        });

        // Act
        var response = await _client.GetAsync($"/api/data/{tableName}?select=name,email");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResponse<DataRecordResponse>>();
        result!.Data[0].Data.Should().ContainKey("name");
        result.Data[0].Data.Should().ContainKey("email");
    }

    #endregion

    #region GetById Operations

    [Fact]
    public async Task GetById_WithExistingRecord_ShouldReturnRecord()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var insertResponse = await _client.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["name"] = "Test User",
            ["email"] = "test@example.com",
            ["age"] = 28,
            ["is_active"] = true
        });
        var insertedRecord = await insertResponse.Content.ReadFromJsonAsync<DataRecordResponse>();

        // Act
        var response = await _client.GetAsync($"/api/data/{tableName}/{insertedRecord!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<DataRecordResponse>();
        result!.Id.Should().Be(insertedRecord.Id);
        result.Data["name"]?.ToString().Should().Be("Test User");
    }

    [Fact]
    public async Task GetById_WithNonExistingRecord_ShouldReturnNotFound()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();

        // Act
        var response = await _client.GetAsync($"/api/data/{tableName}/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Update Operations

    [Fact]
    public async Task Update_WithValidData_ShouldReturnOk()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var insertResponse = await _client.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["name"] = "Original Name",
            ["email"] = "original@example.com",
            ["age"] = 25,
            ["is_active"] = true
        });
        var insertedRecord = await insertResponse.Content.ReadFromJsonAsync<DataRecordResponse>();

        var updateData = new Dictionary<string, object?>
        {
            ["name"] = "Updated Name",
            ["age"] = 26
        };

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/data/{tableName}/{insertedRecord!.Id}", updateData);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<DataRecordResponse>();
        result!.Data["name"]?.ToString().Should().Be("Updated Name");
    }

    [Fact]
    public async Task Update_WithNonExistingRecord_ShouldReturnNotFound()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/data/{tableName}/{Guid.NewGuid()}",
            new Dictionary<string, object?> { ["name"] = "Test" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Delete Operations

    [Fact]
    public async Task Delete_WithExistingRecord_ShouldReturnNoContent()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var insertResponse = await _client.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["name"] = "To Delete",
            ["email"] = "delete@example.com",
            ["age"] = 30,
            ["is_active"] = true
        });
        var insertedRecord = await insertResponse.Content.ReadFromJsonAsync<DataRecordResponse>();

        // Act
        var response = await _client.DeleteAsync($"/api/data/{tableName}/{insertedRecord!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify deletion
        var getResponse = await _client.GetAsync($"/api/data/{tableName}/{insertedRecord.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_WithNonExistingRecord_ShouldReturnNotFound()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();

        // Act
        var response = await _client.DeleteAsync($"/api/data/{tableName}/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Unique Constraint Tests (REST JsonElement regression — issue rest-jsonelement-defects #1)

    private async Task<string> SetupUniqueTableAsync()
    {
        var tableName = $"uniq_test_{Guid.NewGuid():N}"[..25];
        await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns =
            [
                new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "sku", Type = "text", Nullable = false, Unique = true }
            ]
        });
        return tableName;
    }

    [Fact]
    public async Task Insert_IntoUniqueColumnTable_ShouldSucceed()
    {
        // Regression: unique column + REST insert previously threw
        // NotSupportedException (JsonElement not bindable by Dapper) -> 500.
        // Arrange
        var tableName = await SetupUniqueTableAsync();
        var data = new Dictionary<string, object?>
        {
            ["name"] = "Widget",
            ["sku"] = "SKU-001"
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/data/{tableName}", data);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<DataRecordResponse>();
        result.Should().NotBeNull();
        result!.Data["sku"]?.ToString().Should().Be("SKU-001");
    }

    [Fact]
    public async Task Insert_DuplicateUniqueValue_ShouldReturnValidationError()
    {
        // Regression: the unique check must actually execute over REST (JsonElement
        // value converted before the Dapper query) and reject duplicates.
        // Arrange
        var tableName = await SetupUniqueTableAsync();
        var data = new Dictionary<string, object?>
        {
            ["name"] = "Widget",
            ["sku"] = "SKU-DUP"
        };
        var first = await _client.PostAsJsonAsync($"/api/data/{tableName}", data);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act — insert a second row with the same unique value
        var response = await _client.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["name"] = "Widget Clone",
            ["sku"] = "SKU-DUP"
        });

        // Assert — unique violation surfaces as a validation error, not a 500
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error!.Code.Should().Be("VALIDATION_FAILED");
    }

    #endregion

    #region CHECK Constraint Tests (REST JsonElement regression — audit finding #3)

    [Fact]
    public async Task Insert_ViolatingStringCheckConstraint_ShouldReturnValidationError()
    {
        // Regression: over REST the value is a JsonElement, which CheckValidator's
        // `left is string` comparison did not match, so the app-layer CHECK was bypassed
        // (the violation only surfaced later as a DB constraint error -> 500). It must now
        // be caught at the validation layer as a clean 400.
        // Arrange
        var tableName = $"chk_test_{Guid.NewGuid():N}"[..25];
        await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns =
            [
                new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false },
                new CreateColumnApiRequest
                {
                    Name = "status",
                    Type = "text",
                    Nullable = false,
                    Check = "status = 'active' OR status = 'pending'"
                }
            ]
        });

        // Act — insert a value that violates the CHECK constraint
        var response = await _client.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["name"] = "Test",
            ["status"] = "banned"
        });

        // Assert — rejected at the validation layer with a clean 400
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error!.Code.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Insert_SatisfyingStringCheckConstraint_ShouldSucceed()
    {
        // Arrange
        var tableName = $"chk_ok_{Guid.NewGuid():N}"[..25];
        await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns =
            [
                new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false },
                new CreateColumnApiRequest
                {
                    Name = "status",
                    Type = "text",
                    Nullable = false,
                    Check = "status = 'active' OR status = 'pending'"
                }
            ]
        });

        // Act
        var response = await _client.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["name"] = "Test",
            ["status"] = "active"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    #endregion
}
