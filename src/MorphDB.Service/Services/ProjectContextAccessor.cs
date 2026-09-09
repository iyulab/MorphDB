using System.Security.Claims;
using MorphDB.Core.Exceptions;

namespace MorphDB.Service.Services;

/// <summary>
/// Provides access to the current project context.
/// </summary>
public interface IProjectContextAccessor
{
    /// <summary>
    /// Gets the current project ID from the authenticated user or HTTP header.
    /// </summary>
    Guid ProjectId { get; }

    /// <summary>
    /// Gets the current project ID or null if not available.
    /// </summary>
    Guid? ProjectIdOrNull { get; }

    /// <summary>
    /// The raw <c>X-Project-Id</c> header value when one was sent but did not parse as a usable
    /// project id, or null when nothing failed to parse (no header sent, an authenticated claim
    /// supplied the id, or the header parsed fine). Lets a caller distinguish "you sent something,
    /// but it wasn't a GUID" from "you sent nothing" — cases <see cref="ProjectIdOrNull"/> alone
    /// collapses into the same null.
    /// </summary>
    string? MalformedProjectIdHeaderValue { get; }
}

/// <summary>
/// HTTP context-based project context accessor.
/// Resolves project ID from: 1) Authenticated user claims (API Key), 2) X-Project-Id header.
/// </summary>
public sealed class HttpProjectContextAccessor : IProjectContextAccessor
{
    private const string ProjectIdHeader = "X-Project-Id";
    private const string ProjectIdClaimType = "project_id";
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpProjectContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid ProjectId
    {
        get
        {
            var (value, malformed) = Resolve();
            if (value is not null)
            {
                return value.Value;
            }

            throw malformed is not null
                ? new MalformedProjectIdException(malformed)
                : new MissingProjectException();
        }
    }

    public Guid? ProjectIdOrNull => Resolve().Value;

    public string? MalformedProjectIdHeaderValue => Resolve().Malformed;

    /// <summary>
    /// The single walk that decides both <see cref="ProjectIdOrNull"/> and
    /// <see cref="MalformedProjectIdHeaderValue"/>, so the two can never disagree about which state
    /// a request is in.
    /// </summary>
    private (Guid? Value, string? Malformed) Resolve()
    {
        var httpContext = _httpContextAccessor.HttpContext;
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
}
