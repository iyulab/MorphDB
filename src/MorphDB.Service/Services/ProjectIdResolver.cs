using MorphDB.Core.Exceptions;

namespace MorphDB.Service.Services;

/// <summary>
/// The one rule for turning a request's authenticated claim or <c>X-Project-Id</c> header into a
/// project id, and for diagnosing why it failed.
/// <para>
/// Both REST/GraphQL (via <see cref="HttpProjectContextAccessor"/>, which reads
/// <c>IHttpContextAccessor.HttpContext</c>) and <c>MorphHub</c> (which reads
/// <c>Context.GetHttpContext()</c>) need this rule, but neither may source the <see cref="HttpContext"/>
/// the same way: ASP.NET Core's own guidance is to avoid <c>IHttpContextAccessor</c> inside a
/// SignalR hub, where the ambient ASP.NET Core request pipeline the accessor's <c>AsyncLocal</c>
/// depends on has already ended by the time a hub method runs. Each caller resolves its own
/// <see cref="HttpContext"/> and hands it here — this class holds only the parsing and diagnosis,
/// never the retrieval.
/// </para>
/// </summary>
internal static class ProjectIdResolver
{
    private const string ProjectIdHeader = "X-Project-Id";
    private const string ProjectIdClaimType = "project_id";

    /// <summary>
    /// The single walk that decides both the resolved id and, when there is none, whether something
    /// unusable was sent — so the two questions can never disagree about which state a request is in.
    /// </summary>
    public static (Guid? Value, string? Malformed) Resolve(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return (null, null);
        }

        // 1. First, try to get project ID from authenticated user claims (set by API key authentication)
        var user = httpContext.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            var projectClaim = user.FindFirst(ProjectIdClaimType);
            if (projectClaim != null && Guid.TryParse(projectClaim.Value, out var claimProjectId) && claimProjectId != Guid.Empty)
            {
                return (claimProjectId, null);
            }
        }

        // 2. Otherwise the X-Project-Id header, which is how an unauthenticated caller says it.
        if (httpContext.Request.Headers.TryGetValue(ProjectIdHeader, out var projectIdHeader))
        {
            var raw = projectIdHeader.FirstOrDefault();
            if (raw is not null)
            {
                if (Guid.TryParse(raw, out var headerProjectId) && headerProjectId != Guid.Empty)
                {
                    return (headerProjectId, null);
                }

                // Guid.Empty parses fine but names no project a caller could own, so it stays in the
                // same "nothing usable" bucket as an absent header — only a value that fails to parse
                // at all is something the caller can be told to fix.
                var malformed = Guid.TryParse(raw, out _) ? null : raw;
                return (null, malformed);
            }
        }

        return (null, null);
    }

    /// <summary>
    /// <see cref="Resolve"/>, then the shared decision of which exception says so — the id, or the
    /// same diagnosis (missing vs. malformed) every caller of this resolver must give identically.
    /// </summary>
    public static Guid Require(HttpContext? httpContext)
    {
        var (value, malformed) = Resolve(httpContext);
        if (value is not null)
        {
            return value.Value;
        }

        throw malformed is not null
            ? new MalformedProjectIdException(malformed)
            : new MissingProjectException();
    }
}
