using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Npgsql.Repositories;

namespace MorphDB.Service.Services;

/// <summary>
/// Whether a request is scoped to a project that exists, and the refusal when it is not — in one place, so the REST filter
/// and the MCP door refuse the same requests in the same words.
/// </summary>
internal static class ProjectScope
{
    /// <summary>A request that names no usable project: the status and error name to answer with, and the exception that owns the wording.</summary>
    internal sealed record Refusal(int Status, string Error, MorphDbException Exception);

    /// <summary>The project the request is scoped to, or why it is scoped to none.</summary>
    internal static async ValueTask<(Guid? ProjectId, Refusal? Refusal)> CheckAsync(
        IProjectContextAccessor projectContext,
        IProjectRepository projects,
        CancellationToken cancellationToken)
    {
        if (projectContext.ProjectIdOrNull is not { } projectId)
        {
            // The exception type owns the wording — duplicating the literal once let the two drift, and the
            // filter briefly advertised an API key the server never asks for. Which type to instantiate follows the
            // accessor's own distinction: a header that failed to parse said something and gets told what; a
            // request that said nothing gets asked to say something.
            MorphDbException missing = projectContext.MalformedProjectIdHeaderValue is { } raw
                ? new MalformedProjectIdException(raw)
                : new MissingProjectException();
            return (null, new Refusal(StatusCodes.Status400BadRequest, "BadRequest", missing));
        }

        // A deleted project's rows are still in its tables, and must not stay reachable.
        if (!await projects.ExistsAsync(projectId, cancellationToken))
        {
            return (null, new Refusal(StatusCodes.Status404NotFound, "NotFound", new ProjectNotFoundException(projectId)));
        }

        return (projectId, null);
    }
}
