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
        var response = await _httpClient.PostAsJsonAsync("/api/batch/data", request, MorphDBJson.Options, cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<BatchResponse>(MorphDBJson.Options, cancellationToken)
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
            MorphDBJson.Options,
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<BatchResponse>(MorphDBJson.Options, cancellationToken)
            ?? new BatchResponse();
    }

}
