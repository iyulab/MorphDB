using System.Net;
using System.Net.Http.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// Integration tests for Schema API endpoints.
/// Note: Tests are skipped due to .NET 10 preview PipeWriter.UnflushedBytes compatibility issue.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class SchemaApiTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;
    private readonly Guid _tenantId;

    public SchemaApiTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
        _tenantId = fixture.Api.TenantId;
    }

    #region Table Operations

    [Fact]
    public async Task CreateTable_WithValidRequest_ShouldReturnCreated()
    {
        // Arrange
        var request = new CreateTableApiRequest
        {
            Name = $"api_test_{Guid.NewGuid():N}"[..30],
            Columns =
            [
                new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "email", Type = "text", Nullable = false, Unique = true },
                new CreateColumnApiRequest { Name = "age", Type = "integer", Nullable = true }
            ]
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/schema/tables", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var table = await response.Content.ReadFromJsonAsync<TableApiResponse>();
        table.Should().NotBeNull();
        table!.Name.Should().Be(request.Name);
        table.Columns.Should().HaveCountGreaterOrEqualTo(3); // +1 for auto-added id column
    }

    [Fact]
    public async Task GetTable_WithExistingTable_ShouldReturnTable()
    {
        // Arrange
        var tableName = $"get_test_{Guid.NewGuid():N}"[..30];
        var createRequest = new CreateTableApiRequest
        {
            Name = tableName,
            Columns = [new CreateColumnApiRequest { Name = "name", Type = "text" }]
        };
        await _client.PostAsJsonAsync("/api/schema/tables", createRequest);

        // Act
        var response = await _client.GetAsync($"/api/schema/tables/{tableName}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var table = await response.Content.ReadFromJsonAsync<TableApiResponse>();
        table.Should().NotBeNull();
        table!.Name.Should().Be(tableName);
    }

    [Fact]
    public async Task GetTable_WithNonExistingTable_ShouldReturnNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/schema/tables/nonexistent_table");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListTables_ShouldReturnPagedResults()
    {
        // Arrange - Create a table first
        var tableName = $"list_test_{Guid.NewGuid():N}"[..30];
        await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns = [new CreateColumnApiRequest { Name = "name", Type = "text" }]
        });

        // Act
        var response = await _client.GetAsync("/api/schema/tables");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResponse<TableApiResponse>>();
        result.Should().NotBeNull();
        result!.Data.Should().NotBeEmpty();
    }

    [Fact]
    public async Task UpdateTable_WithValidRequest_ShouldReturnOk()
    {
        // Arrange
        var tableName = $"update_test_{Guid.NewGuid():N}"[..30];
        var createResponse = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns = [new CreateColumnApiRequest { Name = "name", Type = "text" }]
        });
        var createdTable = await createResponse.Content.ReadFromJsonAsync<TableApiResponse>();

        var updateRequest = new UpdateTableApiRequest
        {
            Name = $"updated_{Guid.NewGuid():N}"[..30],
            Version = createdTable!.Version
        };

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/schema/tables/{tableName}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteTable_WithExistingTable_ShouldReturnNoContent()
    {
        // Arrange
        var tableName = $"delete_test_{Guid.NewGuid():N}"[..30];
        await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns = [new CreateColumnApiRequest { Name = "name", Type = "text" }]
        });

        // Act
        var response = await _client.DeleteAsync($"/api/schema/tables/{tableName}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify deletion
        var getResponse = await _client.GetAsync($"/api/schema/tables/{tableName}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Column Operations

    [Fact]
    public async Task AddColumn_ToExistingTable_ShouldReturnOk()
    {
        // Arrange
        var tableName = $"col_test_{Guid.NewGuid():N}"[..30];
        await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns = [new CreateColumnApiRequest { Name = "name", Type = "text" }]
        });

        var addColumnRequest = new AddColumnApiRequest
        {
            Name = "description",
            Type = "text",
            Nullable = true
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/schema/tables/{tableName}/columns", addColumnRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var table = await response.Content.ReadFromJsonAsync<TableApiResponse>();
        table!.Columns.Should().Contain(c => c.Name == "description");
    }

    #endregion

    #region Index Operations

    [Fact]
    public async Task CreateIndex_OnExistingTable_ShouldReturnOk()
    {
        // Arrange
        var tableName = $"idx_test_{Guid.NewGuid():N}"[..30];
        await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns = [new CreateColumnApiRequest { Name = "email", Type = "text" }]
        });

        var createIndexRequest = new CreateIndexApiRequest
        {
            Name = "idx_email",
            Columns = ["email"],
            Type = "btree",
            Unique = true
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/schema/tables/{tableName}/indexes", createIndexRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var table = await response.Content.ReadFromJsonAsync<TableApiResponse>();
        table!.Indexes.Should().Contain(i => i.Name == "idx_email");
    }

    #endregion

    #region Tenant Isolation

    [Fact]
    public async Task CreateTable_DifferentTenants_ShouldBeIsolated()
    {
        // Arrange
        var tableName = "shared_name";
        var tenant1Client = _fixture.Api.CreateClientWithTenant(Guid.NewGuid());
        var tenant2Client = _fixture.Api.CreateClientWithTenant(Guid.NewGuid());

        var request = new CreateTableApiRequest
        {
            Name = tableName,
            Columns = [new CreateColumnApiRequest { Name = "data", Type = "text" }]
        };

        // Act
        var response1 = await tenant1Client.PostAsJsonAsync("/api/schema/tables", request);
        var response2 = await tenant2Client.PostAsJsonAsync("/api/schema/tables", request);

        // Assert - Both should succeed as they're in different tenants
        response1.StatusCode.Should().Be(HttpStatusCode.Created);
        response2.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Request_WithoutTenantHeader_ShouldReturnBadRequest()
    {
        // Arrange - Create a client without tenant header
        var client = _fixture.Api.CreateClientWithTenant(Guid.Empty);

        // Act
        var response = await client.GetAsync("/api/schema/tables");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion
}
