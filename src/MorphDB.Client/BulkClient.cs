using System.Net.Http.Json;
using MorphDB.Client.Models;

namespace MorphDB.Client;

/// <summary>
/// Client for bulk import/export operations.
/// </summary>
public sealed class BulkClient
{
    private readonly HttpClient _httpClient;

    internal BulkClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    #region Import Operations

    /// <summary>
    /// Imports data from a CSV file.
    /// </summary>
    public async Task<ImportJobStatus> ImportCsvAsync(
        string tableName,
        Stream csvStream,
        CsvImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new CsvImportOptions();

        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(csvStream), "file", "import.csv");
        content.Add(new StringContent(options.Delimiter.ToString()), "delimiter");
        content.Add(new StringContent(options.HasHeader.ToString().ToLowerInvariant()), "hasHeader");
        if (options.DateFormat != null)
            content.Add(new StringContent(options.DateFormat), "dateFormat");
        content.Add(new StringContent(options.TrimWhitespace.ToString().ToLowerInvariant()), "trimWhitespace");
        content.Add(new StringContent(options.NullHandling), "nullHandling");
        content.Add(new StringContent(options.DuplicateHandling), "duplicateHandling");
        if (options.KeyColumns != null)
            content.Add(new StringContent(string.Join(",", options.KeyColumns)), "keyColumns");

        var response = await _httpClient.PostAsync(
            $"/api/bulk/{Uri.EscapeDataString(tableName)}/import/csv",
            content,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ImportJobStatus>(MorphDBJson.Options, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize import job response");
    }

    /// <summary>
    /// Imports data from a JSON file.
    /// </summary>
    public async Task<ImportJobStatus> ImportJsonAsync(
        string tableName,
        Stream jsonStream,
        JsonImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new JsonImportOptions();

        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(jsonStream), "file", "import.json");
        if (options.JsonPath != null)
            content.Add(new StringContent(options.JsonPath), "jsonPath");
        if (options.DateFormat != null)
            content.Add(new StringContent(options.DateFormat), "dateFormat");
        content.Add(new StringContent(options.DuplicateHandling), "duplicateHandling");
        if (options.KeyColumns != null)
            content.Add(new StringContent(string.Join(",", options.KeyColumns)), "keyColumns");

        var response = await _httpClient.PostAsync(
            $"/api/bulk/{Uri.EscapeDataString(tableName)}/import/json",
            content,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ImportJobStatus>(MorphDBJson.Options, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize import job response");
    }

    /// <summary>
    /// Imports data from an NDJSON file.
    /// </summary>
    public async Task<ImportJobStatus> ImportNdjsonAsync(
        string tableName,
        Stream ndjsonStream,
        JsonImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new JsonImportOptions();

        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(ndjsonStream), "file", "import.ndjson");
        if (options.DateFormat != null)
            content.Add(new StringContent(options.DateFormat), "dateFormat");
        content.Add(new StringContent(options.DuplicateHandling), "duplicateHandling");
        if (options.KeyColumns != null)
            content.Add(new StringContent(string.Join(",", options.KeyColumns)), "keyColumns");

        var response = await _httpClient.PostAsync(
            $"/api/bulk/{Uri.EscapeDataString(tableName)}/import/ndjson",
            content,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ImportJobStatus>(MorphDBJson.Options, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize import job response");
    }

    /// <summary>
    /// Gets import job status.
    /// </summary>
    public async Task<ImportJobStatus?> GetImportJobStatusAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/bulk/import/{jobId}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ImportJobStatus>(MorphDBJson.Options, cancellationToken);
    }

    /// <summary>
    /// Cancels an import job.
    /// </summary>
    public async Task CancelImportJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync($"/api/bulk/import/{jobId}/cancel", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    #endregion

    #region Export Operations

    /// <summary>
    /// Exports data to CSV format.
    /// </summary>
    public async Task<ExportJobStatus> ExportCsvAsync(
        string tableName,
        CsvExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/bulk/{Uri.EscapeDataString(tableName)}/export/csv",
            options ?? new CsvExportOptions(),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ExportJobStatus>(MorphDBJson.Options, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize export job response");
    }

    /// <summary>
    /// Exports data to JSON format.
    /// </summary>
    public async Task<ExportJobStatus> ExportJsonAsync(
        string tableName,
        JsonExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/bulk/{Uri.EscapeDataString(tableName)}/export/json",
            options ?? new JsonExportOptions(),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ExportJobStatus>(MorphDBJson.Options, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize export job response");
    }

    /// <summary>
    /// Exports data to XLSX format.
    /// </summary>
    public async Task<ExportJobStatus> ExportXlsxAsync(
        string tableName,
        XlsxExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/bulk/{Uri.EscapeDataString(tableName)}/export/xlsx",
            options ?? new XlsxExportOptions(),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ExportJobStatus>(MorphDBJson.Options, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize export job response");
    }

    /// <summary>
    /// Gets export job status.
    /// </summary>
    public async Task<ExportJobStatus?> GetExportJobStatusAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/bulk/export/{jobId}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ExportJobStatus>(MorphDBJson.Options, cancellationToken);
    }

    /// <summary>
    /// Downloads an exported file.
    /// </summary>
    public async Task<Stream> DownloadExportAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/bulk/export/{jobId}/download", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }

    /// <summary>
    /// Cancels an export job.
    /// </summary>
    public async Task CancelExportJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync($"/api/bulk/export/{jobId}/cancel", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    #endregion

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw response.StatusCode switch
            {
                System.Net.HttpStatusCode.NotFound => new MorphDBNotFoundException($"Resource not found: {response.RequestMessage?.RequestUri}", body),
                System.Net.HttpStatusCode.BadRequest => new MorphDBValidationException($"Validation failed", responseBody: body),
                System.Net.HttpStatusCode.Unauthorized => new MorphDBAuthenticationException("Authentication required", body),
                System.Net.HttpStatusCode.Forbidden => new MorphDBAuthorizationException("Access denied", body),
                System.Net.HttpStatusCode.Conflict => new MorphDBConflictException("Resource conflict", body),
                _ => new MorphDBApiException($"API request failed: {response.ReasonPhrase}", response.StatusCode, responseBody: body)
            };
        }
    }
}
