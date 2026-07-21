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
            return ProjectIdOrNull ?? throw new MissingProjectException();
        }
    }

    public Guid? ProjectIdOrNull
    {
        get
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext is null)
                return null;

            // 1. First, try to get project ID from authenticated user claims (set by API key authentication)
            var user = httpContext.User;
            if (user.Identity?.IsAuthenticated == true)
            {
                var projectClaim = user.FindFirst(ProjectIdClaimType);
                if (projectClaim != null && Guid.TryParse(projectClaim.Value, out var claimProjectId) && claimProjectId != Guid.Empty)
                {
                    return claimProjectId;
                }
            }

            // 2. Otherwise the X-Project-Id header, which is how an unauthenticated caller says it.
            if (httpContext.Request.Headers.TryGetValue(ProjectIdHeader, out var projectIdHeader) &&
                Guid.TryParse(projectIdHeader.FirstOrDefault(), out var headerProjectId) &&
                headerProjectId != Guid.Empty)
            {
                return headerProjectId;
            }

            return null;
        }
    }
}
