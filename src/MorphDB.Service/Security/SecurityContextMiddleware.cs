using MorphDB.Core.Security;

namespace MorphDB.Service.Security;

/// <summary>
/// Populates the ambient <see cref="SecurityContext"/> from the project-scope header.
/// <para>
/// This establishes the anonymous context of the project a request addresses; row-level security
/// evaluates against it. Whether the caller is anyone in particular is a separate question, decided
/// after this by <see cref="SecretAuthenticationMiddleware"/> — which replaces this context with an
/// authenticated one when a deployment has injected a master secret, and leaves it untouched when
/// none is injected (the default, in which the service authenticates nothing).
/// </para>
/// <para>
/// Note that the body below runs only when the header is present and parses. That is why secret
/// enforcement is a middleware of its own rather than a branch here: a check placed inside this
/// condition could be skipped by simply omitting the header.
/// </para>
/// </summary>
public sealed class SecurityContextMiddleware
{
    /// <summary>
    /// The header naming the project a request is scoped to.
    /// </summary>
    public const string ProjectIdHeaderName = "X-Project-Id";

    private readonly RequestDelegate _next;
    private readonly ISecurityContextAccessor _securityContextAccessor;

    public SecurityContextMiddleware(RequestDelegate next, ISecurityContextAccessor securityContextAccessor)
    {
        _next = next;
        _securityContextAccessor = securityContextAccessor;
    }

    public Task InvokeAsync(HttpContext context)
    {
        // The header is optional for endpoints that are not project-scoped (health, projects,
        // diagnostics); those that require it enforce it themselves via IProjectContextAccessor.
        if (context.Request.Headers.TryGetValue(ProjectIdHeaderName, out var header) &&
            Guid.TryParse(header.FirstOrDefault(), out var projectId))
        {
            _securityContextAccessor.SetContext(SecurityContext.Anonymous(projectId));
        }

        return _next(context);
    }
}

/// <summary>
/// Pipeline registration for <see cref="SecurityContextMiddleware"/>.
/// </summary>
public static class SecurityContextMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityContext(this IApplicationBuilder app)
    {
        return app.UseMiddleware<SecurityContextMiddleware>();
    }
}
