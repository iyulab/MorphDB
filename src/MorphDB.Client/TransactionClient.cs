using System.Net.Http.Json;
using MorphDB.Client.Models;

namespace MorphDB.Client;

/// <summary>
/// Client for transaction operations.
/// </summary>
public sealed class TransactionClient
{
    private readonly HttpClient _httpClient;

    internal TransactionClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Executes a set of operations atomically within a transaction.
    /// All operations succeed or all are rolled back.
    /// </summary>
    public async Task<TransactionResponse> ExecuteAsync(
        TransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "/api/batch/transaction",
            request,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TransactionResponse>(MorphDBJson.Options, cancellationToken)
            ?? new TransactionResponse { Success = false, Error = "Failed to deserialize response" };
    }

    /// <summary>
    /// Finalizes a single draft record by validating it and transitioning its state.
    /// </summary>
    public async Task<FinalizeResponse> FinalizeRecordAsync(
        string tableName,
        Guid recordId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PatchAsync(
            $"/api/data/{Uri.EscapeDataString(tableName)}/{recordId}/finalize",
            null,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<FinalizeResponse>(MorphDBJson.Options, cancellationToken)
            ?? new FinalizeResponse();
    }

    /// <summary>
    /// Finalizes multiple draft records in batch.
    /// </summary>
    public async Task<FinalizeResponse> FinalizeBatchAsync(
        string tableName,
        FinalizeRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/data/{Uri.EscapeDataString(tableName)}/finalize",
            request,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<FinalizeResponse>(MorphDBJson.Options, cancellationToken)
            ?? new FinalizeResponse();
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw response.StatusCode switch
            {
                System.Net.HttpStatusCode.NotFound => new MorphDBNotFoundException("Resource not found", body),
                System.Net.HttpStatusCode.BadRequest => new MorphDBValidationException("Validation failed", responseBody: body),
                System.Net.HttpStatusCode.Conflict => new MorphDBConflictException("Transaction conflict", body),
                _ => new MorphDBApiException($"API request failed: {response.ReasonPhrase}", response.StatusCode, responseBody: body)
            };
        }
    }
}
