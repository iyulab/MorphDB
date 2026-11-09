using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MorphDB.Service.OData;

/// <summary>
/// Rewrites the request's query string so every system option carries its canonical name (<c>$top</c>) before the
/// action's parameters are bound. OData 4.01 Part 2 §5.1 has a service take system options with or without the
/// <c>$</c>, in any case; the actions bind <c>$top</c> and nothing else, so without this <c>top=0</c> binds to no
/// <c>$top</c> and the request is answered with every row. A resource filter runs before model binding, which is
/// the point.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class CanonicalSystemOptionsAttribute : Attribute, IResourceFilter
{
    /// <inheritdoc />
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var request = context.HttpContext.Request;
        if (!request.QueryString.HasValue || !request.Query.Keys.Any(name => ODataSystemOptions.Canonical(name) != name))
        {
            return;
        }

        request.QueryString = QueryString.Create(ODataSystemOptions.Canonicalize(request.Query));
    }

    /// <inheritdoc />
    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
