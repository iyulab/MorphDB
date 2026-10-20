using System.Net;
using System.Text.Json;

namespace MorphDB.Client;

/// <summary>
/// The single place a non-success HTTP response becomes a typed exception. Parses the server's
/// error envelope <c>{error, message, code}</c> so the exception carries the server's own words:
/// <see cref="Exception.Message"/> = the server's <c>message</c> (what went wrong and what to do),
/// <see cref="MorphDBApiException.ErrorCode"/> = the server's <c>code</c> (the machine-readable
/// contract consumers branch on). A body that is not an envelope falls back to the legacy fixed
/// messages and per-type default codes, so non-MorphDB intermediaries (proxies, gateways) never
/// break the exception surface.
/// </summary>
internal static class ErrorEnvelope
{
    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var (message, code) = Parse(body);
        throw response.StatusCode switch
        {
            HttpStatusCode.NotFound => new MorphDBNotFoundException(
                message ?? $"Resource not found: {response.RequestMessage?.RequestUri}", body, code),
            HttpStatusCode.BadRequest => new MorphDBValidationException(
                message ?? "Validation failed", responseBody: body, errorCode: code),
            HttpStatusCode.Unauthorized => new MorphDBAuthenticationException(
                message ?? "Authentication required", body, code),
            HttpStatusCode.Forbidden => new MorphDBAuthorizationException(
                message ?? "Access denied", body, code),
            HttpStatusCode.Conflict => new MorphDBConflictException(
                message ?? "Resource conflict", body, code),
            _ => new MorphDBApiException(
                message ?? $"API request failed: {response.ReasonPhrase}", response.StatusCode, code, body),
        };
    }

    /// <summary>
    /// Whether a response says the requested resource is absent — the case a nullable getter answers
    /// with <c>null</c>. A <c>404</c> whose code is <c>PROJECT_NOT_FOUND</c> is not that: the request's
    /// project is gone, so nothing in it can be read, and answering <c>null</c> would tell the caller
    /// "no such table" (or row, view, webhook) when the truth is "no such project". That one throws,
    /// as <see cref="EnsureSuccessAsync"/> would. Any other response returns <c>false</c> and is left
    /// to <see cref="EnsureSuccessAsync"/>.
    /// </summary>
    public static async Task<bool> IsAbsentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            return false;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var (message, code) = Parse(body);
        if (string.Equals(code, ProjectNotFound, StringComparison.Ordinal))
        {
            throw new MorphDBNotFoundException(message ?? "Project not found.", body, code);
        }

        return true;
    }

    /// <summary>The server's code for a request whose project does not exist.</summary>
    internal const string ProjectNotFound = "PROJECT_NOT_FOUND";

    private static (string? Message, string? Code) Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            return (ReadString(document.RootElement, "message"), ReadString(document.RootElement, "code"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? ReadString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
}
