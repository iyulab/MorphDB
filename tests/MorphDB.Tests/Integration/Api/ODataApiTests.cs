using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Service.OData;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// Integration tests for OData API endpoints.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ODataApiTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;

    public ODataApiTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
    }

    private async Task<string> SetupTestTableAsync()
    {
        var tableName = $"odata_test_{Guid.NewGuid():N}"[..30];
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

    private async Task<Guid> InsertTestRecordAsync(string tableName, string name, string email, int? age, bool isActive)
    {
        var data = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["email"] = email,
            ["age"] = age,
            ["is_active"] = isActive
        };

        var response = await _client.PostAsJsonAsync($"/api/data/{tableName}", data);
        var result = await response.Content.ReadFromJsonAsync<DataRecordResponse>();
        return result!.Id;
    }

    #region Metadata Tests

    [Fact]
    public async Task GetMetadata_ShouldReturnXml()
    {
        // Arrange
        await SetupTestTableAsync();

        // Act
        var response = await _client.GetAsync("/odata/$metadata");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/xml");
    }

    #endregion

    #region Query Tests

    [Fact]
    public async Task GetEntitySet_ShouldReturnODataResponse()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        await InsertTestRecordAsync(tableName, "John", "john@example.com", 30, true);
        await InsertTestRecordAsync(tableName, "Jane", "jane@example.com", 25, true);

        var entitySetName = ToPascalCase(tableName);

        // Act
        var response = await _client.GetAsync($"/odata/{entitySetName}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("@odata.context");
        content.Should().Contain("value");
    }

    [Fact]
    public async Task GetEntitySet_WithTop_ShouldLimitResults()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        for (int i = 0; i < 5; i++)
        {
            await InsertTestRecordAsync(tableName, $"User{i}", $"user{i}@example.com", 20 + i, true);
        }

        var entitySetName = ToPascalCase(tableName);

        // Act
        var response = await _client.GetAsync($"/odata/{entitySetName}?$top=2");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonDoc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var valueArray = jsonDoc!.RootElement.GetProperty("value");
        valueArray.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GetEntitySet_WithCount_ShouldIncludeTotalCount()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        for (int i = 0; i < 5; i++)
        {
            await InsertTestRecordAsync(tableName, $"User{i}", $"user{i}@example.com", 20 + i, true);
        }

        var entitySetName = ToPascalCase(tableName);

        // Act
        var response = await _client.GetAsync($"/odata/{entitySetName}?$count=true");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonDoc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        jsonDoc!.RootElement.TryGetProperty("@odata.count", out var countProp).Should().BeTrue();
        countProp.GetInt64().Should().Be(5);
    }

    [Fact]
    public async Task GetEntitySet_WithFilter_ShouldFilterResults()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        await InsertTestRecordAsync(tableName, "John", "john@example.com", 30, true);
        await InsertTestRecordAsync(tableName, "Jane", "jane@example.com", 25, false);

        var entitySetName = ToPascalCase(tableName);

        // Act
        var response = await _client.GetAsync($"/odata/{entitySetName}?$filter=is_active eq true");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonDoc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var valueArray = jsonDoc!.RootElement.GetProperty("value");
        valueArray.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task GetEntitySet_WithOrderBy_ShouldSortResults()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        await InsertTestRecordAsync(tableName, "Charlie", "charlie@example.com", 35, true);
        await InsertTestRecordAsync(tableName, "Alice", "alice@example.com", 25, true);
        await InsertTestRecordAsync(tableName, "Bob", "bob@example.com", 30, true);

        var entitySetName = ToPascalCase(tableName);

        // Act
        var response = await _client.GetAsync($"/odata/{entitySetName}?$orderby=name");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonDoc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var valueArray = jsonDoc!.RootElement.GetProperty("value");
        var firstItem = valueArray[0];
        firstItem.GetProperty("name").GetString().Should().Be("Alice");
    }

    [Fact]
    public async Task GetEntitySet_WithSelect_ShouldReturnSelectedFields()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        await InsertTestRecordAsync(tableName, "John", "john@example.com", 30, true);

        var entitySetName = ToPascalCase(tableName);

        // Act
        var response = await _client.GetAsync($"/odata/{entitySetName}?$select=name,email");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonDoc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var valueArray = jsonDoc!.RootElement.GetProperty("value");
        var firstItem = valueArray[0];
        firstItem.TryGetProperty("name", out _).Should().BeTrue();
        firstItem.TryGetProperty("email", out _).Should().BeTrue();
    }

    #endregion

    #region Single Entity Tests

    [Fact]
    public async Task GetEntity_ById_ShouldReturnSingleEntity()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var id = await InsertTestRecordAsync(tableName, "John", "john@example.com", 30, true);

        var entitySetName = ToPascalCase(tableName);

        // Act
        var response = await _client.GetAsync($"/odata/{entitySetName}({id})");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonDoc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        jsonDoc!.RootElement.TryGetProperty("@odata.context", out _).Should().BeTrue();
        jsonDoc.RootElement.TryGetProperty("value", out var valueProp).Should().BeTrue();
        valueProp.GetProperty("name").GetString().Should().Be("John");
    }

    [Fact]
    public async Task GetEntity_NotFound_ShouldReturn404()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var entitySetName = ToPascalCase(tableName);
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/odata/{entitySetName}({nonExistentId})");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region CUD Operations

    [Fact]
    public async Task CreateEntity_ShouldReturnCreated()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var entitySetName = ToPascalCase(tableName);
        var data = new Dictionary<string, object?>
        {
            ["name"] = "New User",
            ["email"] = "new@example.com",
            ["age"] = 28,
            ["is_active"] = true
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/odata/{entitySetName}", data);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var jsonDoc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        jsonDoc!.RootElement.TryGetProperty("value", out var valueProp).Should().BeTrue();
        valueProp.GetProperty("name").GetString().Should().Be("New User");
    }

    [Fact]
    public async Task UpdateEntity_ShouldReturnOk()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var id = await InsertTestRecordAsync(tableName, "John", "john@example.com", 30, true);
        var entitySetName = ToPascalCase(tableName);

        var updateData = new Dictionary<string, object?>
        {
            ["name"] = "John Updated"
        };

        // Act
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/odata/{entitySetName}({id})")
        {
            Content = JsonContent.Create(updateData)
        };
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var jsonDoc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        jsonDoc!.RootElement.GetProperty("value").GetProperty("name").GetString().Should().Be("John Updated");
    }

    [Fact]
    public async Task DeleteEntity_ShouldReturnNoContent()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var id = await InsertTestRecordAsync(tableName, "John", "john@example.com", 30, true);
        var entitySetName = ToPascalCase(tableName);

        // Act
        var response = await _client.DeleteAsync($"/odata/{entitySetName}({id})");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify deletion
        var getResponse = await _client.GetAsync($"/odata/{entitySetName}({id})");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Batch Tests

    [Fact]
    public async Task BatchRequest_ShouldExecuteMultipleOperations()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var id = await InsertTestRecordAsync(tableName, "John", "john@example.com", 30, true);
        var entitySetName = ToPascalCase(tableName);

        var batchRequest = new ODataBatchRequest
        {
            Requests =
            [
                new ODataBatchRequestItem
                {
                    Id = "1",
                    Method = "POST",
                    EntitySet = entitySetName,
                    Body = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
                    {
                        ["name"] = "New User",
                        ["email"] = "new@example.com",
                        ["is_active"] = true
                    })
                },
                new ODataBatchRequestItem
                {
                    Id = "2",
                    Method = "GET",
                    EntitySet = entitySetName,
                    Key = id
                },
                new ODataBatchRequestItem
                {
                    Id = "3",
                    Method = "DELETE",
                    EntitySet = entitySetName,
                    Key = id
                }
            ]
        };

        // Act
        var response = await _client.PostAsJsonAsync("/odata/$batch", batchRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var batchResponse = await response.Content.ReadFromJsonAsync<ODataBatchResponse>();
        batchResponse.Should().NotBeNull();
        batchResponse!.Responses.Should().HaveCount(3);

        batchResponse.Responses[0].Status.Should().Be(201); // POST created
        batchResponse.Responses[1].Status.Should().Be(200); // GET success
        batchResponse.Responses[2].Status.Should().Be(204); // DELETE no content
    }

    #endregion

    private static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var parts = name.Split('_', '-', ' ');
        return string.Concat(parts.Select(p =>
            p.Length > 0
                ? char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()
                : p));
    }
}
