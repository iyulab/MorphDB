using Microsoft.Extensions.DependencyInjection;
using MorphDB.Core.Abstractions;
using MorphDB.Npgsql;
using Npgsql;

namespace MorphDB.Tests.Unit.Infrastructure;

/// <summary>
/// Holds which startup failures the schema bootstrap waits out and which it surfaces.
/// <para>
/// A database the service reaches while it is still starting answers <c>57P03</c> — the server is
/// there and says so, it just cannot take a connection yet. That is a server answer, and the
/// bootstrap used to treat every server answer as final, so a service started beside a database
/// that was restarting (the official image restarts once after running its init scripts) exited
/// instead of retrying. Answers that retrying cannot fix — bad credentials, a missing database, a
/// schema fault — still surface on the first attempt.
/// </para>
/// </summary>
public class SchemaBootstrapRetryTests
{
    [Theory]
    [InlineData("57P03")] // cannot_connect_now: starting up or shutting down
    [InlineData("53300")] // too_many_connections
    public void A_server_answer_that_passes_is_waited_out(string sqlState)
    {
        ServiceCollectionExtensions.IsDatabaseUnreachable(Answer(sqlState)).Should().BeTrue();
    }

    [Theory]
    [InlineData("28P01")] // invalid_password
    [InlineData("3D000")] // invalid_catalog_name
    [InlineData("42P07")] // duplicate_table: a fault in the schema itself
    public void A_server_answer_that_retrying_cannot_fix_is_surfaced(string sqlState)
    {
        ServiceCollectionExtensions.IsDatabaseUnreachable(Answer(sqlState)).Should().BeFalse();
    }

    [Fact]
    public async Task Startup_retries_a_database_that_is_still_starting_up()
    {
        var schemaLayer = new Mock<ISchemaLayerService>();
        schemaLayer.SetupSequence(s => s.EnsureGlobalSchemaAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(Answer("57P03"))
            .Returns(Task.CompletedTask);

        await Services(schemaLayer.Object).EnsureMorphDbSchemaAsync(TestContext.Current.CancellationToken);

        schemaLayer.Verify(s => s.EnsureGlobalSchemaAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Startup_fails_at_once_on_an_answer_retrying_cannot_fix()
    {
        var schemaLayer = new Mock<ISchemaLayerService>();
        schemaLayer.Setup(s => s.EnsureGlobalSchemaAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(Answer("28P01"));

        var act = () => Services(schemaLayer.Object).EnsureMorphDbSchemaAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("28P01");
        schemaLayer.Verify(s => s.EnsureGlobalSchemaAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static PostgresException Answer(string sqlState) => new("message", "FATAL", "FATAL", sqlState);

    private static ServiceProvider Services(ISchemaLayerService schemaLayer) =>
        new ServiceCollection().AddSingleton(schemaLayer).BuildServiceProvider();
}
