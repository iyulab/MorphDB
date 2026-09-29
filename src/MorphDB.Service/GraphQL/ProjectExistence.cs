using HotChocolate;
using HotChocolate.Execution;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Service.Services;

namespace MorphDB.Service.GraphQL;

/// <summary>
/// Refuses a GraphQL request whose <c>X-Project-Id</c> names a project that does not exist or was
/// deleted, before any field resolves — the refusal REST gives with <c>[RequireProject]</c>. Resolvers
/// read the project id themselves, so without this a deleted project's tables stayed readable and
/// writable here after every REST route had stopped serving them.
/// <para>
/// A request that names no project, or a malformed one, is left to the resolvers, which already give
/// that answer field by field.
/// </para>
/// </summary>
internal static class ProjectExistence
{
    public static async ValueTask RequireAsync(
        HttpContext context,
        IRequestExecutor executor,
        OperationRequestBuilder request,
        CancellationToken cancellationToken)
    {
        if (ProjectIdResolver.Resolve(context).Value is not { } projectId)
        {
            return;
        }

        var projects = context.RequestServices.GetRequiredService<IProjectRepository>();
        if (!await projects.ExistsAsync(projectId, cancellationToken))
        {
            var notFound = new ProjectNotFoundException(projectId);
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage(notFound.Message)
                .SetCode(notFound.ErrorCode)
                .Build());
        }
    }
}
