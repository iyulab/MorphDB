using System.Globalization;
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
    /// Imports data from a CSV file: the file is the request body, the options its query.
    /// </summary>
    public Task<ImportJobStatus> ImportCsvAsync(
        string tableName,
        Stream csvStream,
        CsvImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new CsvImportOptions();

        var query = new List<string>
        {
            Parameter("delimiter", options.Delimiter.ToString()),
            Parameter("hasHeader", options.HasHeader ? "true" : "false"),
            Parameter("trimWhitespace", options.TrimWhitespace ? "true" : "false"),
            Parameter("nullHandling", options.NullHandling),
            Parameter("duplicateHandling", options.DuplicateHandling),
        };
        if (options.DateFormat != null)
            query.Add(Parameter("dateFormat", options.DateFormat));
        query.AddRange(options.KeyColumns?.Select(k => Parameter("keyColumns", k)) ?? []);

        return ImportAsync(tableName, "csv", "text/csv", csvStream, query, cancellationToken);
    }

    /// <summary>
    /// Imports data from a JSON file (an array of objects): the file is the request body, the options
    /// its query.
    /// </summary>
    public Task<ImportJobStatus> ImportJsonAsync(
        string tableName,
        Stream jsonStream,
        JsonImportOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ImportAsync(tableName, "json", "application/json", jsonStream, JsonQuery(options ?? new JsonImportOptions()), cancellationToken);

    /// <summary>
    /// Imports data from an NDJSON file (one object per line): the file is the request body, the
    /// options its query.
    /// </summary>
    public Task<ImportJobStatus> ImportNdjsonAsync(
        string tableName,
        Stream ndjsonStream,
        JsonImportOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ImportAsync(tableName, "ndjson", "application/x-ndjson", ndjsonStream, JsonQuery(options ?? new JsonImportOptions()), cancellationToken);

    private static List<string> JsonQuery(JsonImportOptions options)
    {
        var query = new List<string> { Parameter("duplicateHandling", options.DuplicateHandling) };
        if (options.DateFormat != null)
            query.Add(Parameter("dateFormat", options.DateFormat));
        query.AddRange(options.KeyColumns?.Select(k => Parameter("keyColumns", k)) ?? []);
        return query;
    }

    private static string Parameter(string name, string value) => $"{name}={Uri.EscapeDataString(value)}";

    /// <summary>
    /// Posts a file to an import route as the service reads it: the file is the whole request body,
    /// under its format's media type. (A multipart form, which this client used to send, is refused.)
    /// </summary>
    private async Task<ImportJobStatus> ImportAsync(
        string tableName,
        string format,
        string mediaType,
        Stream file,
        IReadOnlyList<string> query,
        CancellationToken cancellationToken)
    {
        using var content = new StreamContent(file);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);

        var response = await _httpClient.PostAsync(
            $"/api/bulk/{Uri.EscapeDataString(tableName)}/import/{format}?{string.Join("&", query)}",
            content,
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.ImportJobStatus, cancellationToken)
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
        if (await ErrorEnvelope.IsAbsentAsync(response, cancellationToken))
            return null;
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.ImportJobStatus, cancellationToken);
    }

    /// <summary>
    /// Cancels an import job.
    /// </summary>
    public async Task CancelImportJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync($"/api/bulk/jobs/{jobId}/cancel", null, cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
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
            MorphDBJsonContext.Default.CsvExportOptions,
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.ExportJobStatus, cancellationToken)
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
            MorphDBJsonContext.Default.JsonExportOptions,
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.ExportJobStatus, cancellationToken)
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
            MorphDBJsonContext.Default.XlsxExportOptions,
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.ExportJobStatus, cancellationToken)
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
        if (await ErrorEnvelope.IsAbsentAsync(response, cancellationToken))
            return null;
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.ExportJobStatus, cancellationToken);
    }

    /// <summary>
    /// Downloads an exported file.
    /// </summary>
    public async Task<Stream> DownloadExportAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/bulk/export/{jobId}/download", cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }

    /// <summary>
    /// Cancels an export job.
    /// </summary>
    public async Task CancelExportJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync($"/api/bulk/jobs/{jobId}/cancel", null, cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
    }

    #endregion

    /// <summary>
    /// The project's import jobs, newest first.
    /// </summary>
    public Task<IReadOnlyList<ImportJobStatus>> ListImportJobsAsync(int? limit = null, int? offset = null, CancellationToken cancellationToken = default)
        => ListAsync("/api/bulk/import", limit, offset, MorphDBJsonContext.Default.ListImportJobStatus, cancellationToken);

    /// <summary>
    /// The project's export jobs, newest first.
    /// </summary>
    public Task<IReadOnlyList<ExportJobStatus>> ListExportJobsAsync(int? limit = null, int? offset = null, CancellationToken cancellationToken = default)
        => ListAsync("/api/bulk/export", limit, offset, MorphDBJsonContext.Default.ListExportJobStatus, cancellationToken);

    /// <summary>
    /// How far an import or export job has got, or null when the project has no such job.
    /// </summary>
    public async Task<JobProgress?> GetJobProgressAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/bulk/jobs/{jobId}/progress", cancellationToken);
        if (await ErrorEnvelope.IsAbsentAsync(response, cancellationToken))
            return null;
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.JobProgress, cancellationToken);
    }

    private async Task<IReadOnlyList<T>> ListAsync<T>(
        string path, int? limit, int? offset, System.Text.Json.Serialization.Metadata.JsonTypeInfo<List<T>> typeInfo, CancellationToken cancellationToken)
    {
        var parts = new List<string>();
        if (limit is { } l)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"limit={l}"));
        if (offset is { } o)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"offset={o}"));
        var query = parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
        var response = await _httpClient.GetAsync(path + query, cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken) ?? [];
    }
}
