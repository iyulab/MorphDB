using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// Integration tests for Bulk Import/Export API endpoints.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class BulkApiTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;

    public BulkApiTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
    }

    private async Task<string> SetupTestTableAsync()
    {
        var tableName = $"bulk_test_{Guid.NewGuid():N}"[..30];
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

    private async Task InsertTestDataAsync(string tableName, int count = 5)
    {
        for (var i = 0; i < count; i++)
        {
            await _client.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
            {
                ["name"] = $"User {i}",
                ["email"] = $"user{i}@example.com",
                ["age"] = 20 + i,
                ["is_active"] = i % 2 == 0
            });
        }
    }

    #region CSV Import Tests

    [Fact]
    public async Task ImportCsv_WithValidData_ShouldCreateJob()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var csvContent = "name,email,age,is_active\nAlice,alice@test.com,25,true\nBob,bob@test.com,30,false";
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(csvContent), "file", "test.csv");
        content.Add(new StringContent(","), "delimiter");
        content.Add(new StringContent("true"), "hasHeader");

        // Act
        var response = await _client.PostAsync($"/api/bulk/{tableName}/import/csv", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var result = await response.Content.ReadFromJsonAsync<ImportJobApiResponse>();
        result.Should().NotBeNull();
        result!.JobId.Should().NotBeEmpty();
        result.TableName.Should().Be(tableName);
        result.Format.Should().Be("csv");
        result.Status.Should().BeOneOf("pending", "processing", "completed");
    }

    [Fact]
    public async Task ImportCsv_WithCustomDelimiter_ShouldParseCorrectly()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var csvContent = "name;email;age;is_active\nCharlie;charlie@test.com;28;true";
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(csvContent), "file", "test.csv");
        content.Add(new StringContent(";"), "delimiter");
        content.Add(new StringContent("true"), "hasHeader");

        // Act
        var response = await _client.PostAsync($"/api/bulk/{tableName}/import/csv", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task ImportCsv_WithoutFile_ShouldReturnBadRequest()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var content = new MultipartFormDataContent();

        // Act
        var response = await _client.PostAsync($"/api/bulk/{tableName}/import/csv", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region JSON Import Tests

    [Fact]
    public async Task ImportJson_WithValidArray_ShouldCreateJob()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var jsonContent = """
            [
                {"name": "David", "email": "david@test.com", "age": 35, "is_active": true},
                {"name": "Eve", "email": "eve@test.com", "age": 28, "is_active": false}
            ]
            """;
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(jsonContent), "file", "test.json");

        // Act
        var response = await _client.PostAsync($"/api/bulk/{tableName}/import/json", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var result = await response.Content.ReadFromJsonAsync<ImportJobApiResponse>();
        result.Should().NotBeNull();
        result!.Format.Should().Be("json");
    }

    [Fact]
    public async Task ImportNdjson_WithValidLines_ShouldCreateJob()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var ndjsonContent = """
            {"name": "Frank", "email": "frank@test.com", "age": 40, "is_active": true}
            {"name": "Grace", "email": "grace@test.com", "age": 33, "is_active": true}
            """;
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(ndjsonContent), "file", "test.ndjson");

        // Act
        var response = await _client.PostAsync($"/api/bulk/{tableName}/import/ndjson", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var result = await response.Content.ReadFromJsonAsync<ImportJobApiResponse>();
        result.Should().NotBeNull();
        result!.Format.Should().Be("ndjson");
    }

    #endregion

    #region CSV Export Tests

    [Fact]
    public async Task ExportCsv_WithExistingData_ShouldCreateJob()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        await InsertTestDataAsync(tableName, 3);

        var request = new CsvExportApiRequest
        {
            Delimiter = ',',
            IncludeHeader = true
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/bulk/{tableName}/export/csv", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var result = await response.Content.ReadFromJsonAsync<ExportJobApiResponse>();
        result.Should().NotBeNull();
        result!.JobId.Should().NotBeEmpty();
        result.TableName.Should().Be(tableName);
        result.Format.Should().Be("csv");
    }

    [Fact]
    public async Task ExportCsv_WithColumnSelection_ShouldAccept()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        await InsertTestDataAsync(tableName, 2);

        var request = new CsvExportApiRequest
        {
            Columns = ["name", "email"],
            Delimiter = ','
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/bulk/{tableName}/export/csv", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    #endregion

    #region JSON Export Tests

    [Fact]
    public async Task ExportJson_WithExistingData_ShouldCreateJob()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        await InsertTestDataAsync(tableName, 3);

        var request = new JsonExportApiRequest
        {
            Pretty = true
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/bulk/{tableName}/export/json", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var result = await response.Content.ReadFromJsonAsync<ExportJobApiResponse>();
        result.Should().NotBeNull();
        result!.Format.Should().Be("json");
    }

    [Fact]
    public async Task ExportXlsx_WithExistingData_ShouldCreateJob()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        await InsertTestDataAsync(tableName, 3);

        var request = new XlsxExportApiRequest
        {
            SheetName = "TestSheet"
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/bulk/{tableName}/export/xlsx", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var result = await response.Content.ReadFromJsonAsync<ExportJobApiResponse>();
        result.Should().NotBeNull();
        result!.Format.Should().Be("xlsx");
    }

    #endregion

    #region Job Status Tests

    [Fact]
    public async Task GetImportJobStatus_WithValidJobId_ShouldReturnStatus()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var csvContent = "name,email,age,is_active\nTest,test@test.com,25,true";
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(csvContent), "file", "test.csv");
        content.Add(new StringContent(","), "delimiter");
        content.Add(new StringContent("true"), "hasHeader");

        var importResponse = await _client.PostAsync($"/api/bulk/{tableName}/import/csv", content);
        var importJob = await importResponse.Content.ReadFromJsonAsync<ImportJobApiResponse>();

        // Act
        var response = await _client.GetAsync($"/api/bulk/import/{importJob!.JobId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ImportJobApiResponse>();
        result.Should().NotBeNull();
        result!.JobId.Should().Be(importJob.JobId);
    }

    [Fact]
    public async Task GetImportJobStatus_WithInvalidJobId_ShouldReturnNotFound()
    {
        // Act
        var response = await _client.GetAsync($"/api/bulk/import/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetExportJobStatus_WithValidJobId_ShouldReturnStatus()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        await InsertTestDataAsync(tableName, 2);

        var exportResponse = await _client.PostAsJsonAsync($"/api/bulk/{tableName}/export/csv",
            new CsvExportApiRequest());
        var exportJob = await exportResponse.Content.ReadFromJsonAsync<ExportJobApiResponse>();

        // Act
        var response = await _client.GetAsync($"/api/bulk/export/{exportJob!.JobId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ExportJobApiResponse>();
        result.Should().NotBeNull();
        result!.JobId.Should().Be(exportJob.JobId);
    }

    [Fact]
    public async Task GetExportJobStatus_WithInvalidJobId_ShouldReturnNotFound()
    {
        // Act
        var response = await _client.GetAsync($"/api/bulk/export/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Job Cancellation Tests

    [Fact]
    public async Task CancelImportJob_WithPendingJob_ShouldCancel()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        // Create a large import that might take some time
        var csvLines = new StringBuilder("name,email,age,is_active\n");
        for (var i = 0; i < 100; i++)
        {
            csvLines.AppendLine(CultureInfo.InvariantCulture, $"User{i},user{i}@test.com,{20 + i % 50},true");
        }

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(csvLines.ToString()), "file", "test.csv");
        content.Add(new StringContent(","), "delimiter");
        content.Add(new StringContent("true"), "hasHeader");

        var importResponse = await _client.PostAsync($"/api/bulk/{tableName}/import/csv", content);
        var importJob = await importResponse.Content.ReadFromJsonAsync<ImportJobApiResponse>();

        // Act
        var response = await _client.PostAsync($"/api/bulk/import/{importJob!.JobId}/cancel", null);

        // Assert
        // Job might be already completed (fast processing) or cancelled
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CancelExportJob_WithPendingJob_ShouldCancel()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        await InsertTestDataAsync(tableName, 10);

        var exportResponse = await _client.PostAsJsonAsync($"/api/bulk/{tableName}/export/csv",
            new CsvExportApiRequest());
        var exportJob = await exportResponse.Content.ReadFromJsonAsync<ExportJobApiResponse>();

        // Act
        var response = await _client.PostAsync($"/api/bulk/export/{exportJob!.JobId}/cancel", null);

        // Assert
        // Job might be already completed (fast processing) or cancelled
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);
    }

    #endregion

    #region Download Tests

    [Fact]
    public async Task DownloadExport_WithCompletedJob_ShouldReturnFile()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        await InsertTestDataAsync(tableName, 3);

        var exportResponse = await _client.PostAsJsonAsync($"/api/bulk/{tableName}/export/csv",
            new CsvExportApiRequest { Delimiter = ',', IncludeHeader = true });
        var exportJob = await exportResponse.Content.ReadFromJsonAsync<ExportJobApiResponse>();

        // Wait for job to complete (with timeout)
        ExportJobApiResponse? jobStatus = null;
        for (var i = 0; i < 10; i++)
        {
            await Task.Delay(500);
            var statusResponse = await _client.GetAsync($"/api/bulk/export/{exportJob!.JobId}");
            jobStatus = await statusResponse.Content.ReadFromJsonAsync<ExportJobApiResponse>();
            if (jobStatus?.Status == "completed")
                break;
        }

        // Act
        var response = await _client.GetAsync($"/api/bulk/export/{exportJob!.JobId}/download");

        // Assert
        if (jobStatus?.Status == "completed")
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");
        }
        else
        {
            // Job not yet completed
            response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task DownloadExport_WithInvalidJobId_ShouldReturnNotFound()
    {
        // Act
        var response = await _client.GetAsync($"/api/bulk/export/{Guid.NewGuid()}/download");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task ImportCsv_ToNonExistentTable_ShouldReturnNotFound()
    {
        // Arrange
        var csvContent = "name,email,age,is_active\nTest,test@test.com,25,true";
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(csvContent), "file", "test.csv");

        // Act
        var response = await _client.PostAsync("/api/bulk/nonexistent_table/import/csv", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExportCsv_FromNonExistentTable_ShouldReturnNotFound()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/api/bulk/nonexistent_table/export/csv",
            new CsvExportApiRequest());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion
}
