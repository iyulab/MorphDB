using Microsoft.AspNetCore.Mvc.Filters;
using MorphDB.Service.Security;

namespace MorphDB.Service.Filters;

/// <summary>
/// Holds every action of a controller to the master secret while the service enforces credentials.
/// <para>
/// Set on the controller rather than called in each action, so an action added later is held by default —
/// the failure mode of a check in each action is that the newest action is the one missing it. With no master
/// secret injected the service authenticates nothing and this passes, as <see cref="ProjectAccess"/> does.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireMasterSecretAttribute : ActionFilterAttribute
{
    private readonly string _refusal;

    /// <param name="refusal">What the refusal says.</param>
    public RequireMasterSecretAttribute(string refusal)
    {
        _refusal = refusal;
    }

    /// <inheritdoc />
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.HttpContext.RequestServices.GetRequiredService<ProjectAccess>().RequireMaster(_refusal);
    }
}
