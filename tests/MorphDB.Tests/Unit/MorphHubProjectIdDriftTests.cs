using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Unit;

/// <summary>
/// <c>MorphHub</c> used to parse <c>X-Project-Id</c> itself instead of sharing
/// <see cref="MorphDB.Service.Services.ProjectIdResolver"/> with REST/GraphQL, so a header that was
/// sent but did not parse was diagnosed as missing on the hub while REST/GraphQL correctly reported
/// it as malformed — two implementations of the same rule, disagreeing.
/// <para>
/// This gate holds the fix in place the same way <c>ChangelogStructureTests</c> holds its own: not by
/// re-deriving the resolution rule, but by asserting the literal that names the header never
/// reappears in the hub — a reintroduced local <c>Guid.TryParse</c> against that literal is exactly
/// how the two implementations would drift apart again, and the compiler has no opinion about it.
/// </para>
/// </summary>
public class MorphHubProjectIdDriftTests
{
    private const string ProjectIdHeaderLiteral = "X-Project-Id";
    private const string HubRelativePath = "src/MorphDB.Service/Realtime/MorphHub.cs";

    [Fact]
    public void MorphHub_does_not_name_the_project_header_itself()
    {
        var hubSource = ConstraintBoundaryDoc.ReadRepoFile(HubRelativePath);

        hubSource.Should().NotContain(ProjectIdHeaderLiteral,
            $"{HubRelativePath} must resolve the project id through ProjectIdResolver, the same rule "
            + "REST/GraphQL use — a header literal back in this file is how the malformed/missing "
            + "distinction drifted out of sync the first time");
    }

    [Fact]
    public void The_gate_reports_a_hub_that_names_the_header_itself()
    {
        var reintroduced = "var raw = Context.GetHttpContext()?.Request.Headers[\"X-Project-Id\"];";

        reintroduced.Should().Contain(ProjectIdHeaderLiteral,
            "without this the check above would pass over a reintroduced literal and be vacuous");
    }
}
