using AwesomeAssertions;
using MorphDB.Core.Abstractions;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Schema;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration;

/// <summary>
/// <see cref="ProjectRepository.ExistsAsync"/> is asked by every project-scoped request, so it may
/// remember a "yes" briefly. These tests pin what that memory is allowed to cost: nothing on the
/// instance that deletes the project, a bounded window on any other, and never a remembered "no".
/// </summary>
[Collection("PostgreSQL")]
public class ProjectExistenceTests
{
    private readonly PostgresFixture _fixture;

    public ProjectExistenceTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private ProjectRepository Repository(TimeProvider? time = null) =>
        new(_fixture.DataSource, new PostgresSchemaNameResolver(), time);

    private static async Task<Guid> CreateAsync(ProjectRepository repository)
    {
        var project = await repository.CreateAsync(
            new CreateProjectRequest { Name = $"exists_{Guid.NewGuid():N}"[..28] },
            TestContext.Current.CancellationToken);
        return project.ProjectId;
    }

    [Fact]
    public async Task A_delete_on_the_same_instance_takes_effect_at_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = Repository();
        var projectId = await CreateAsync(repository);
        (await repository.ExistsAsync(projectId, ct)).Should().BeTrue();

        await repository.DeleteAsync(projectId, ct);

        (await repository.ExistsAsync(projectId, ct)).Should().BeFalse(
            "the instance that changed the status forgets what it remembered");
    }

    [Fact]
    public async Task A_delete_on_another_instance_is_seen_once_the_remembered_answer_expires()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new ManualTimeProvider();
        var reader = Repository(clock);
        var projectId = await CreateAsync(reader);
        (await reader.ExistsAsync(projectId, ct)).Should().BeTrue();

        await Repository().DeleteAsync(projectId, ct);

        (await reader.ExistsAsync(projectId, ct)).Should().BeTrue(
            "another instance's delete cannot reach this one's memory — that is the window");
        clock.Advance(TimeSpan.FromSeconds(11));
        (await reader.ExistsAsync(projectId, ct)).Should().BeFalse();
    }

    [Fact]
    public async Task An_absent_project_is_not_remembered_as_absent()
    {
        var ct = TestContext.Current.CancellationToken;
        var reader = Repository();
        var projectId = Guid.NewGuid();
        (await reader.ExistsAsync(projectId, ct)).Should().BeFalse();

        await Repository().CreateAsync(
            new CreateProjectRequest { ProjectId = projectId, Name = $"late_{Guid.NewGuid():N}"[..28] },
            ct);

        (await reader.ExistsAsync(projectId, ct)).Should().BeTrue(
            "a project created a moment after a miss must be usable at once");
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
