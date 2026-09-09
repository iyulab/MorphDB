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
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpProjectContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid ProjectId => ProjectIdResolver.Require(_httpContextAccessor.HttpContext);

    public Guid? ProjectIdOrNull => ProjectIdResolver.Resolve(_httpContextAccessor.HttpContext).Value;

    public string? MalformedProjectIdHeaderValue =>
        ProjectIdResolver.Resolve(_httpContextAccessor.HttpContext).Malformed;
}
