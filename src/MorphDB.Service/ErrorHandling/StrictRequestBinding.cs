using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
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
internal static class StrictRequestBinding
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
            .Distinct()
            .ToList();

        var bodyType = context.ActionDescriptor.Parameters
            .FirstOrDefault(p => p.BindingInfo?.BindingSource == Microsoft.AspNetCore.Mvc.ModelBinding.BindingSource.Body)
            ?.ParameterType;
        if (bodyType is not null && bodyType.Namespace?.StartsWith("MorphDB.Service.Models", StringComparison.Ordinal) == true)
        {
            var members = bodyType.GetProperties()
                .Select(p => System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(p.Name))
                .OrderBy(n => n, StringComparer.Ordinal);
            details.Add($"Supported members for {bodyType.Name}: {string.Join(", ", members)}.");
        }

        return new BadRequestObjectResult(new ErrorResponse
        {
            Error = "BadRequest",
            Message = details.Count > 0 ? string.Join(" ", details) : "The request body could not be bound.",
            Code = "INVALID_ARGUMENT",
        });
    }
}
