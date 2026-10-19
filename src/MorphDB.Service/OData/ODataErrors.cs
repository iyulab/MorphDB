using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MorphDB.Service.ErrorHandling;
using MorphDB.Service.Models.Api;

namespace MorphDB.Service.OData;

/// <summary>
/// An error as OData JSON writes one (OData JSON Format 4.01 §21): <c>{"error": {"code", "message"}}</c>.
/// The codes are the REST error table's, so a caller branches on the same code whichever door it uses.
/// </summary>
public sealed record ODataError([property: JsonPropertyName("error")] ODataErrorDetail Error)
{
    /// <summary>
    /// The envelope's code and message; its per-field details become OData's <c>details</c>, one entry
    /// per message with the field as <c>target</c>.
    /// </summary>
    internal static ODataError From(ErrorResponse envelope) =>
        new(new ODataErrorDetail(envelope.Code, envelope.Message ?? envelope.Error)
        {
            Details = envelope.Details?
                .SelectMany(field => field.Value.Select(message => new ODataErrorDetail(envelope.Code, message) { Target = field.Key }))
                .ToList(),
        });

    internal static ODataError Of(string code, string message) => new(new ODataErrorDetail(code, message));
}

/// <summary>The <c>error</c> member of <see cref="ODataError"/>, and each of its <c>details</c>.</summary>
public sealed record ODataErrorDetail(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message)
{
    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Target { get; init; }

    [JsonPropertyName("details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ODataErrorDetail>? Details { get; init; }
}

/// <summary>
/// Which wire shape an error takes on a request: the OData surface speaks OData's, every other route
/// the REST envelope. One decision, read by the OData controller's filter, the global exception
/// handler and the authentication middleware alike — so a missing project, a refused secret and a
/// caller's mistake all reach an OData client in the shape it parses.
/// </summary>
internal static class ErrorWire
{
    public static bool IsOData(HttpRequest request) =>
        request.Path.StartsWithSegments("/odata", StringComparison.OrdinalIgnoreCase);

    public static object Body(HttpRequest request, ErrorResponse envelope) =>
        IsOData(request) ? ODataError.From(envelope) : envelope;
}

/// <summary>
/// The OData controller's error door: an <see cref="ErrorResponse"/> any action or filter answers with
/// is rewritten to <see cref="ODataError"/>, and an exception an action lets escape is classified as
/// REST classifies it (<see cref="ErrorClassification"/>) and answered the same way.
/// </summary>
internal sealed partial class ODataErrorFilter : IAsyncAlwaysRunResultFilter, IExceptionFilter
{
    private readonly ILogger<ODataErrorFilter> _logger;

    public ODataErrorFilter(ILogger<ODataErrorFilter> logger) => _logger = logger;

    public void OnException(ExceptionContext context)
    {
        if (context.Exception is OperationCanceledException)
        {
            return;
        }

        var (status, envelope) = ErrorClassification.Envelope(context.Exception);
        if (status >= StatusCodes.Status500InternalServerError)
        {
            LogUnexpected(_logger, context.Exception, context.HttpContext.Request.Method, context.HttpContext.Request.Path);
        }

        // A JSON result, not an object result: $metadata produces XML only, and an object result would
        // be negotiated against that and answered 406 instead of the error.
        context.Result = new JsonResult(ODataError.From(envelope)) { StatusCode = status };
        context.ExceptionHandled = true;
    }

    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        switch (context.Result)
        {
            case ObjectResult { Value: ErrorResponse envelope } result:
                result.Value = ODataError.From(envelope);
                result.DeclaredType = typeof(ODataError);
                break;
            case JsonResult { Value: ErrorResponse envelope } json:
                json.Value = ODataError.From(envelope);
                break;
        }

        return next();
    }

    [LoggerMessage(LogLevel.Error, "Unexpected error handling {Method} {Path}")]
    private static partial void LogUnexpected(ILogger logger, Exception exception, string method, string path);
}
