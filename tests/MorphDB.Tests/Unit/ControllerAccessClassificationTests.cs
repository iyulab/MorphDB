using Microsoft.AspNetCore.Mvc;
using MorphDB.Service.Controllers;
using MorphDB.Service.Filters;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Every controller decides who it answers. A project-scoped one says so with
/// <see cref="RequireProjectAttribute"/>, which refuses the request that names no project, a project
/// that does not exist, or one that was deleted — before the action runs. A controller that does not
/// carry it answers for itself, and is listed here with how. A new controller fails this test until it
/// takes one of the two, so a route cannot be added that checks nothing: service diagnostics and the
/// security policies once were such routes.
/// </summary>
public class ControllerAccessClassificationTests
{
    private static readonly IReadOnlyDictionary<Type, string> AnswersForItself = new Dictionary<Type, string>
    {
        [typeof(ProjectController)] = "addresses a project by its path: writes take the master secret, reads ProjectAccess.RequireAccessTo",
        [typeof(AuditController)] = "addresses a project by its path: ProjectAccess.RequireAccessTo on every action",
        [typeof(SecretsController)] = "administration: the master secret only",
        [typeof(DiagnosticsController)] = "the whole service's state: ProjectAccess.RequireMaster on every action",
    };

    private static IEnumerable<Type> Controllers() => typeof(DataController).Assembly.GetTypes()
        .Where(t => t is { IsAbstract: false, IsClass: true } && typeof(ControllerBase).IsAssignableFrom(t));

    [Fact]
    public void Every_controller_is_project_scoped_or_says_how_it_decides_access()
    {
        var unclassified = Controllers()
            .Where(c => c.GetCustomAttributes(typeof(RequireProjectAttribute), inherit: true).Length == 0)
            .Where(c => !AnswersForItself.ContainsKey(c))
            .Select(c => c.Name)
            .ToList();

        unclassified.Should().BeEmpty(
            "a controller either carries [RequireProject] or is listed in AnswersForItself with the check it makes");
    }

    [Fact]
    public void The_list_names_only_controllers_that_exist_and_are_not_project_scoped()
    {
        foreach (var listed in AnswersForItself.Keys)
        {
            Controllers().Should().Contain(listed);
            listed.GetCustomAttributes(typeof(RequireProjectAttribute), inherit: true).Should()
                .BeEmpty($"{listed.Name} is project-scoped; it does not belong in the list");
        }
    }
}
