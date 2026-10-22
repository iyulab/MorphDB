using System.Net.Http.Json;
using MorphDB.Client.Models;

namespace MorphDB.Client;

/// <summary>
/// Client for batch data operations — many writes in one request.
/// </summary>
public sealed class BatchClient
{
    private readonly HttpClient _httpClient;

    internal BatchClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Executes a batch of operations in order. Each operation names its own table, so one batch may
    /// span tables. Operations are reported individually — inspect <see cref="BatchResponse.Results"/>
    /// for partial failures, since a batch with failed operations still returns success.
    /// </summary>
    public async Task<BatchResponse> ExecuteAsync(
        BatchRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/batch/data", request, MorphDBJsonContext.Default.BatchRequest, cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.BatchResponse, cancellationToken)
            ?? new BatchResponse();
    }

    /// <summary>
    /// Inserts many records into one table. Records without an <c>_id</c> are assigned one by the server.
    /// </summary>
    public async Task<BatchResponse> InsertManyAsync(
        string tableName,
        IReadOnlyList<IDictionary<string, object?>> records,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/batch/data/{Uri.EscapeDataString(tableName)}/insert",
            records,
            MorphDBJsonContext.Default.IReadOnlyListIDictionaryStringObject,
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.BatchResponse, cancellationToken)
            ?? new BatchResponse();
    }

    /// <summary>
    /// Updates every row of one table the filters select — all of them apply — writing the same values to
    /// each. <see cref="BatchOperationResult.AffectedRows"/> of the single result says how many rows changed.
    /// At least one filter is required, as the service requires it: an update with none would rewrite every row.
    /// </summary>
    /// <exception cref="ArgumentException">No filter, or no values, are given.</exception>
    public async Task<BatchResponse> UpdateManyAsync(
        string tableName,
        IReadOnlyList<Filter> filters,
        IDictionary<string, object?> data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(data);
        if (filters.Count == 0)
        {
            throw new ArgumentException("A bulk update names at least one filter, so it cannot rewrite the whole table by accident.", nameof(filters));
        }

        if (data.Count == 0)
        {
            throw new ArgumentException("An update writes at least one value.", nameof(data));
        }

        var response = await _httpClient.PatchAsJsonAsync(
            $"/api/batch/data/{Uri.EscapeDataString(tableName)}",
            new BulkUpdateWireRequest(data, [.. filters.Select(DataClient.FilterText)]),
            MorphDBJsonContext.Default.BulkUpdateWireRequest,
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.BatchResponse, cancellationToken)
            ?? new BatchResponse();
    }

    /// <summary>
    /// Deletes every row of one table the filters select — all of them apply. At least one filter is
    /// required, as the service requires it: a delete with none would empty the table.
    /// </summary>
    /// <exception cref="ArgumentException">No filter is given.</exception>
    public async Task<BatchResponse> DeleteManyAsync(
        string tableName,
        IReadOnlyList<Filter> filters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filters);
        if (filters.Count == 0)
        {
            throw new ArgumentException("A bulk delete names at least one filter, so it cannot empty the table by accident.", nameof(filters));
        }

        var query = string.Join('&', filters.Select(f => $"filter={Uri.EscapeDataString(DataClient.FilterText(f))}"));
        var response = await _httpClient.DeleteAsync($"/api/batch/data/{Uri.EscapeDataString(tableName)}?{query}", cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.BatchResponse, cancellationToken)
            ?? new BatchResponse();
    }

    /// <summary>
    /// Seeds a table: each record is inserted, or updates the one whose key columns hold the same values —
    /// so seeding the same records twice leaves the table as seeding them once.
    /// </summary>
    public async Task<SeedResult> SeedAsync(
        string tableName,
        IReadOnlyList<IDictionary<string, object?>> records,
        IReadOnlyList<string> keyColumns,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(keyColumns);
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/batch/data/{Uri.EscapeDataString(tableName)}/seed",
            new SeedWireRequest(records, keyColumns),
            MorphDBJsonContext.Default.SeedWireRequest,
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.SeedResult, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize seed response");
    }
}

/// <summary>The body of <c>PATCH /api/batch/data/{table}</c>.</summary>
internal sealed record BulkUpdateWireRequest(IDictionary<string, object?> Data, IReadOnlyList<string> Filter);

/// <summary>The body of <c>POST /api/batch/data/{table}/seed</c>.</summary>
internal sealed record SeedWireRequest(IReadOnlyList<IDictionary<string, object?>> Records, IReadOnlyList<string> KeyColumns);
