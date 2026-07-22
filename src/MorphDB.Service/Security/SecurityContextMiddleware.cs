using MorphDB.Core.Security;

namespace MorphDB.Service.Security;

/// <summary>
/// Populates the ambient <see cref="SecurityContext"/> from the project-scope header.
/// The service itself is unauthenticated by design; every caller operates in the anonymous
/// context of the project it addresses, and row-level security evaluates against that context.
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
