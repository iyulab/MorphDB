using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using MorphDB.Service.Models.Api;

namespace MorphDB.Service.ErrorHandling;

/// <summary>
/// The request-envelope half of fail-loud writes (0.8.0 closed the field level with
/// <c>UNKNOWN_COLUMN</c>; this closes the body level). A JSON member the request DTO does not
/// declare is refused with a 400 naming the member and listing the supported ones — never silently
/// dropped, because a dropped member turns a caller's typo into a confidently wrong 200 (live
/// probe: <c>{"filters": …}</c> against <c>/query</c> answered every row, filter ignored).
/// Model-binding failures answer the same <c>{error, message, code}</c> envelope as every other
/// error — not the framework's ProblemDetails — so consumers see one error shape everywhere.
/// </summary>
internal static partial class StrictRequestBinding
{
    /// <summary>
    /// Rejects unmapped JSON members on every API model (<c>MorphDB.Service.Models</c> namespace).
    /// Dictionary-bodied endpoints (<c>/api/data</c> row writes) are unaffected — every member of a
    /// dictionary maps by definition; their unknown-field policy lives in the write pipeline.
    /// </summary>
    public static void DisallowUnmappedMembers(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind == JsonTypeInfoKind.Object
            && typeInfo.Type.Namespace?.StartsWith("MorphDB.Service.Models", StringComparison.Ordinal) == true)
        {
            typeInfo.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        }
    }

    /// <summary>
    /// Replaces the framework's ValidationProblemDetails 400 with the standard error envelope,
    /// appending the body DTO's supported members so the caller learns what is possible, not just
    /// that they failed.
    /// </summary>
    public static IActionResult InvalidModelStateResponse(ActionContext context)
    {
        var details = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .SelectMany(entry => entry.Value!.Errors.Select(error =>
                string.IsNullOrEmpty(error.ErrorMessage) ? error.Exception?.Message : error.ErrorMessage))
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Select(message => Humanize(message!))
            .Distinct()
            .ToList();

        // When the body itself failed to bind, MVC also records "The <parameter> field is required."
        // for the null parameter -- a second sentence that restates the failure in the action's
        // parameter name, which the caller never sees. Keep it only when it is all there is.
        if (details.Count > 1)
        {
            details.RemoveAll(d => ParameterRequired().IsMatch(d));
        }

        var bodyType = context.ActionDescriptor.Parameters
            .FirstOrDefault(p => p.BindingInfo?.BindingSource == Microsoft.AspNetCore.Mvc.ModelBinding.BindingSource.Body)
            ?.ParameterType;
        if (bodyType is not null && bodyType.Namespace?.StartsWith("MorphDB.Service.Models", StringComparison.Ordinal) == true)
        {
            var members = bodyType.GetProperties()
                .Select(p => System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(p.Name))
                .OrderBy(n => n, StringComparer.Ordinal);
            details.Add($"Supported members: {string.Join(", ", members)}.");
        }

        return new BadRequestObjectResult(new ErrorResponse
        {
            Error = "BadRequest",
            Message = details.Count > 0 ? string.Join(" ", details) : "The request body could not be bound.",
            Code = "INVALID_ARGUMENT",
        });
    }

    /// <summary>
    /// Rewrites System.Text.Json's binding messages in the caller's terms. The originals name the
    /// .NET type the body binds to and the CLR type a value failed to convert to — identifiers of
    /// this implementation, not of the wire contract, and the one thing a consumer reading the error
    /// cannot act on. Each known shape is restated with what the caller sent and what is accepted;
    /// anything else has the implementation names scrubbed and its position kept.
    /// </summary>
    internal static string Humanize(string message)
    {
        var path = PathSuffix().Match(message);
        var at = path.Success ? $" (at {path.Groups["path"].Value})" : string.Empty;
        var core = PathSuffix().Replace(message, string.Empty).TrimEnd();

        var unknown = UnknownMember().Match(core);
        if (unknown.Success)
        {
            return $"Unknown member '{unknown.Groups["member"].Value}'{at}.";
        }

        var missing = MissingRequired().Match(core);
        if (missing.Success)
        {
            return $"Missing required member(s): {missing.Groups["list"].Value.Trim().TrimEnd('.')}{at}.";
        }

        var convert = CouldNotConvert().Match(core);
        if (convert.Success)
        {
            return $"The value is not {ExpectedKind(convert.Groups["clr"].Value)}{at}.";
        }

        return ImplementationIdentifier().Replace(core, "the request body") + at;
    }

    private static string ExpectedKind(string clrType) => clrType switch
    {
        "System.Int32" or "System.Int64" or "System.Int16" => "an integer",
        "System.Decimal" or "System.Double" or "System.Single" => "a number",
        "System.Boolean" => "a boolean (true/false)",
        "System.Guid" => "a UUID",
        "System.String" => "a string",
        "System.DateTime" or "System.DateTimeOffset" => "a date-time",
        "System.Char" => "a single character",
        _ when clrType.EndsWith("[]", StringComparison.Ordinal) || clrType.Contains("List", StringComparison.Ordinal) => "an array",
        _ => "of the expected type",
    };

    [GeneratedRegex(@"\s*Path:\s*(?<path>\$[^\s|]*)(\s*\|\s*LineNumber:\s*\d+)?(\s*\|\s*BytePositionInLine:\s*\d+)?\.?\s*$")]
    private static partial Regex PathSuffix();

    [GeneratedRegex(@"The JSON property '(?<member>[^']+)' could not be mapped to any \.NET member contained in type '[^']+'\.?")]
    private static partial Regex UnknownMember();

    [GeneratedRegex(@"JSON deserialization for type '[^']+' was missing required properties[^:]*:\s*(?<list>.+)$")]
    private static partial Regex MissingRequired();

    [GeneratedRegex(@"The JSON value could not be converted to (?<clr>[A-Za-z0-9_`\[\]]+(?:\.[A-Za-z0-9_`\[\]]+)*)\.?")]
    private static partial Regex CouldNotConvert();

    [GeneratedRegex(@"'?\bMorphDB\.Service\.[A-Za-z0-9_.]+'?")]
    private static partial Regex ImplementationIdentifier();

    [GeneratedRegex(@"^The \w+ field is required\.$")]
    private static partial Regex ParameterRequired();
}
