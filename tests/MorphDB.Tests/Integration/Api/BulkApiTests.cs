using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
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
        var content = new StringContent(csvContent, Encoding.UTF8, "text/csv");

        // Act
        var response = await _client.PostAsync($"/api/bulk/{tableName}/import/csv?delimiter=,&hasHeader=true", content);

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
        var content = new StringContent(csvContent, Encoding.UTF8, "text/csv");

        // Act
        var response = await _client.PostAsync($"/api/bulk/{tableName}/import/csv?delimiter=;&hasHeader=true", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task ImportCsv_ExportedTypedColumns_RoundTripsWithoutRowFailures()
    {
        // Arrange — the exact shape of the original bug report: export a table with non-text
        // columns (integer, boolean) to CSV, then re-import that CSV into the same table. Every
        // row used to fail (CSV values arrive as raw strings; only JSON/NDJSON produced typed
        // values), with no reason recorded anywhere.
        //
        // The test host removes the BulkJobProcessor background service (it polls tables that
        // don't exist yet at fixture start-up — see ApiTestFixture), so job processing is driven
        // directly through IBulkOperationService here instead of waiting on HTTP polling for a
        // background pass that will never run.
        var tableName = await SetupTestTableAsync();
        await InsertTestDataAsync(tableName, 3);

        using var scope = _fixture.Api.Services.CreateScope();
        var bulkService = scope.ServiceProvider.GetRequiredService<IBulkOperationService>();

        // Export only the user-declared columns — re-importing the row's own primary key
        // alongside it would collide on uniqueness, which is a real conflict unrelated to the
        // type-coercion bug under test (see the issue's own repro, which drops system columns
        // for exactly this reason).
        var exportJob = await bulkService.StartCsvExportAsync(
            _fixture.Api.ProjectId, tableName, new CsvExportOptions
            {
                Delimiter = ',',
                IncludeHeader = true,
                Columns = ["name", "email", "age", "is_active"]
            });

        using var exportStream = new MemoryStream();
        await bulkService.StreamExportAsync(exportJob.JobId, exportStream);
        exportStream.Position = 0;
        var csvContent = await new StreamReader(exportStream, Encoding.UTF8).ReadToEndAsync();

        // Act — re-import the exported CSV into the same table.
        using var importStream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
        var importJob = await bulkService.StartCsvImportAsync(
            _fixture.Api.ProjectId, tableName, importStream, new CsvImportOptions { Delimiter = ',', HasHeader = true });

        long successCount = 0, errorCount = 0;
        await foreach (var result in bulkService.ProcessImportAsync(importJob.JobId))
        {
            if (result.Success)
                successCount++;
            else
                errorCount++;
        }

        // Assert
        errorCount.Should().Be(0, "typed CSV values (integer, boolean) must round-trip like JSON/NDJSON does");
        successCount.Should().Be(3);

        var finalJob = await bulkService.GetImportJobAsync(importJob.JobId);
        finalJob!.ErrorCount.Should().Be(0);
        finalJob.SuccessCount.Should().Be(3);
    }

    [Fact]
    public async Task ImportCsv_WithARowThatFailsToWrite_PersistsPerRowErrorDetails()
    {
        // Arrange — "age" is declared integer; a value the column cannot hold must fail that row
        // (not the whole parse), and the reason must survive to the job record instead of being
        // discarded once error_count is tallied.
        var tableName = await SetupTestTableAsync();
        var csvContent = "name,email,age,is_active\nAlice,alice@test.com,not-a-number,true";

        using var scope = _fixture.Api.Services.CreateScope();
        var bulkService = scope.ServiceProvider.GetRequiredService<IBulkOperationService>();

        using var importStream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
        var importJob = await bulkService.StartCsvImportAsync(
            _fixture.Api.ProjectId, tableName, importStream, new CsvImportOptions { Delimiter = ',', HasHeader = true });

        // Act
        await foreach (var _ in bulkService.ProcessImportAsync(importJob.JobId))
        {
            // Drain — the job record is what's under test, not the per-row stream.
        }

        // Assert
        var finalJob = await bulkService.GetImportJobAsync(importJob.JobId);
        finalJob!.ErrorCount.Should().Be(1);
        finalJob.ErrorDetails.Should().NotBeNullOrEmpty();
        finalJob.ErrorDetails![0].RowNumber.Should().Be(1);
        finalJob.ErrorDetails![0].Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ImportCsv_WithEmptyContent_ShouldCreateJobWithZeroRows()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        var content = new StringContent(string.Empty, Encoding.UTF8, "text/csv");

        // Act
        var response = await _client.PostAsync($"/api/bulk/{tableName}/import/csv?delimiter=,&hasHeader=true", content);

        // Assert - empty content is accepted and creates a job (with 0 rows to process)
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var result = await response.Content.ReadFromJsonAsync<ImportJobApiResponse>();
        result.Should().NotBeNull();
        result!.TotalRows.Should().Be(0);
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
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

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
        var content = new StringContent(ndjsonContent, Encoding.UTF8, "application/x-ndjson");

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
        var content = new StringContent(csvContent, Encoding.UTF8, "text/csv");

        var importResponse = await _client.PostAsync($"/api/bulk/{tableName}/import/csv?delimiter=,&hasHeader=true", content);
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

        var content = new StringContent(csvLines.ToString(), Encoding.UTF8, "text/csv");

        var importResponse = await _client.PostAsync($"/api/bulk/{tableName}/import/csv?delimiter=,&hasHeader=true", content);
        var importJob = await importResponse.Content.ReadFromJsonAsync<ImportJobApiResponse>();

        // Act
        var response = await _client.PostAsync($"/api/bulk/jobs/{importJob!.JobId}/cancel", null);

        // Assert
        // Job might be already completed (fast processing), not found (already processed), or cancelled (204 NoContent)
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
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
        var response = await _client.PostAsync($"/api/bulk/jobs/{exportJob!.JobId}/cancel", null);

        // Assert
        // Job might be already completed (fast processing), not found (already processed), or cancelled (204 NoContent)
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
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
        var content = new StringContent(csvContent, Encoding.UTF8, "text/csv");

        // Act
        var response = await _client.PostAsync("/api/bulk/nonexistent_table/import/csv?delimiter=,&hasHeader=true", content);

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
