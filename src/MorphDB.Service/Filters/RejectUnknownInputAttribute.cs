using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using MorphDB.Service.Models.Api;

namespace MorphDB.Service.Filters;

/// <summary>
/// Refuses a conditional header on a request that changes something: <c>If-Match</c>, <c>If-None-Match</c> and
/// <c>If-Unmodified-Since</c> ask the server to write only if the resource is (or is not) what the caller saw, and no
/// door evaluates them — a row carries no entity tag and no door compares a version the request names. Ignored, the
/// write goes through as if the precondition had held, which is the lost update the caller asked to be protected from.
/// On a read the same headers are the cache validators every client may send, and are left alone.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class RejectConditionalHeadersAttribute : ActionFilterAttribute
{
    private static readonly string[] ConditionalHeaders = ["If-Match", "If-None-Match", "If-Unmodified-Since"];

    /// <summary>Whether <paramref name="header"/> makes a write conditional on the state it finds.</summary>
    internal static bool IsConditional(string header) =>
        ConditionalHeaders.Contains(header, StringComparer.OrdinalIgnoreCase);

    /// <summary>What a refusal of a conditional header on a write says; <paramref name="where"/> names whose header it is.</summary>
    internal static string Refusal(string header, string where = "") =>
        $"The '{header}' header{where} is not supported on a write: a row carries no entity tag and no door " +
        "checks a precondition, so it would be ignored and the write would go through. Send the request without it.";

    /// <inheritdoc />
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method))
        {
            return;
        }

        foreach (var header in ConditionalHeaders)
        {
            if (request.Headers.ContainsKey(header))
            {
                context.Result = Refuse(Refusal(header));
                return;
            }
        }
    }

    /// <summary>The refusal, as the REST error envelope (the OData controller's error filter rewrites it to OData's).</summary>
    protected static BadRequestObjectResult Refuse(string message) =>
        new(new ErrorResponse { Error = "BadRequest", Message = message, Code = "INVALID_ARGUMENT" });
}

/// <summary>
/// Holds every action of a controller to the input it declares: a query parameter the action does not take is
/// refused naming it, and so is a conditional header on a request that changes something
/// (<see cref="RejectConditionalHeadersAttribute"/>).
/// <para>
/// A parameter that is not read is dropped without a word, and that is harmless until the caller meant it.
/// <c>?expectedVersion=3</c> and <c>If-Match</c> ask the server to write only if the row is still what the caller
/// saw; answering such a write with a success without evaluating the precondition hands the caller the lost
/// update they asked to be protected from. A misspelled <c>fliter</c> narrows nothing, so the answer is every row.
/// Refusing is the only answer that is not a lie, until a door really takes the precondition — then it is declared
/// and passes.
/// </para>
/// <para>
/// The known parameters are read from the action itself — the simple parameters it binds from the query, and the
/// members of a record it binds from the query — so an action added or changed later is held by what it declares,
/// not by a list kept elsewhere. Set on the controller, like <see cref="RequireMasterSecretAttribute"/>, so the
/// newest action cannot be the one missing it.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RejectUnknownInputAttribute : RejectConditionalHeadersAttribute
{
    private static readonly ConcurrentDictionary<string, KnownParameters> KnownByAction = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        base.OnActionExecuting(context);
        if (context.Result is not null)
        {
            return;
        }

        if (context.ActionDescriptor is ControllerActionDescriptor action)
        {
            var known = KnownByAction.GetOrAdd(action.Id, _ => KnownQueryParameters(action));
            if (QueryParameterCheck.Unknown(context.HttpContext.Request.Query.Keys, known.Accepted, known.Listed) is { } unknown)
            {
                context.Result = Refuse(unknown);
            }
        }
    }

    /// <summary>What a request may carry: every spelling the action's binding takes, and the names a refusal lists.</summary>
    private sealed record KnownParameters(string[] Accepted, string[] Listed);

    /// <summary>
    /// What the action binds from the query: its simple parameters by name, a record's members by name — and the
    /// members again behind the parameter's name (<c>options.delimiter</c>), the prefixed spelling model binding
    /// reads for a record just as it reads the bare one.
    /// </summary>
    private static KnownParameters KnownQueryParameters(ControllerActionDescriptor action)
    {
        var listed = new List<string>();
        var accepted = new List<string>();
        foreach (var parameter in action.Parameters)
        {
            if (parameter.BindingInfo?.BindingSource != BindingSource.Query)
                continue;

            var name = parameter.BindingInfo.BinderModelName ?? parameter.Name;
            if (IsSimple(parameter.ParameterType))
            {
                listed.Add(name);
                accepted.Add(name);
                continue;
            }

            foreach (var member in parameter.ParameterType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..]))
            {
                listed.Add(member);
                accepted.Add(member);
                accepted.Add($"{name}.{member}");
            }
        }

        return new KnownParameters(
            [.. accepted.Distinct(StringComparer.OrdinalIgnoreCase)],
            [.. listed.Distinct(StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>A value (or a sequence of values) read from one query key, as opposed to a record read from several.</summary>
    private static bool IsSimple(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsArray)
            type = type.GetElementType()!;

        return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
            || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan);
    }
}
