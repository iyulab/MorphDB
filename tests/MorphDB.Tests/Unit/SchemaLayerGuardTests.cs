using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Schema;
using Npgsql;

namespace MorphDB.Tests.Unit;

public class SchemaLayerGuardTests
{
    /// <summary>
    /// Guid.Empty is what a broken mapping yields, not a project. Provisioning it once created
    /// p_00000000 schemas silently, and every later mis-mapped read collided with them two steps
    /// away from the actual cause. The refusal has to be loud and at the boundary.
    /// </summary>
    [Fact]
    public async Task Provisioning_the_empty_project_id_is_refused()
    {
        var dataSource = NpgsqlDataSource.Create("Host=localhost;Database=never_reached;Username=x;Password=x");
        var resolver = new PostgresSchemaNameResolver();
        var service = new PostgresSchemaLayerService(
            dataSource,
            resolver,
            new ProjectRepository(dataSource, resolver),
            NullLogger<PostgresSchemaLayerService>.Instance);

        var act = () => service.ProvisionProjectSchemasAsync(Guid.Empty);

        await act.Should().ThrowAsync<ArgumentException>("the guard must fire before any connection is opened");
    }
}
