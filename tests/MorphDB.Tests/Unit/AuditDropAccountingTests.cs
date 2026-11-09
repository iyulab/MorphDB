using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Audit;
using MorphDB.Npgsql.Audit;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Security;
using MorphDB.Tests.Fixtures;
using Npgsql;

namespace MorphDB.Tests.Unit;

/// <summary>
/// The audit log is written from a bounded queue after the response, so an entry can be lost
/// without any request failing. These hold that every loss the running process can see is
/// counted, because the documentation promises exactly that and nothing else shows the gap.
/// <para>
/// The writer is held at the first thing it does for a batch — resolving the project's schema
/// names — so what is queued and what is discarded is decided by the test, not by timing.
/// </para>
/// </summary>
public sealed class AuditDropAccountingTests
{
    private const int QueueCapacity = 10_000;

    [Fact]
    public async Task Entries_pushed_out_of_a_full_queue_are_counted_as_overflow()
    {
        using var counts = new DropCounts();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (service, writerHeld, _) = CreateService(gate.Task);

        try
        {
            // The writer takes this one and stops on the gate, so it no longer occupies the queue.
            await service.LogAsync(NewEvent(), TestContext.Current.CancellationToken);
            await writerHeld.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            const int excess = 250;
            for (var i = 0; i < QueueCapacity + excess; i++)
            {
                await service.LogAsync(NewEvent(), TestContext.Current.CancellationToken);
            }

            counts.Of(AuditMetrics.QueueOverflow).Should().Be(excess,
                "a queue of 10,000 that was offered 10,250 entries discarded the 250 oldest — and a full DropOldest " +
                "queue still accepts every write, so no failed write can have signalled it");
        }
        finally
        {
            // A held writer would keep the service, and the test run, from ever finishing.
            gate.TrySetResult();
            await service.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_batch_the_database_cannot_take_is_counted_per_entry()
    {
        using var counts = new DropCounts();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (service, writerHeld, _) = CreateService(gate.Task);

        // One project, so the entries waiting behind the held one are written as a single group.
        var project = Guid.NewGuid();
        const int waiting = 7;
        try
        {
            await service.LogAsync(NewEvent(project), TestContext.Current.CancellationToken);
            await writerHeld.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            for (var i = 0; i < waiting; i++)
            {
                await service.LogAsync(NewEvent(project), TestContext.Current.CancellationToken);
            }
        }
        finally
        {
            // The writer fails on the entry it holds and then on the seven that waited behind it;
            // disposing waits for the queue to be drained.
            gate.TrySetResult();
            await service.DisposeAsync();
        }

        // At least: other tests in the run may lose entries of their own while this one counts.
        counts.Of(AuditMetrics.WriteFailed).Should().BeGreaterThanOrEqualTo(1 + waiting,
            "the counter adds the entries a batch held, not one per failed batch or group");
    }

    [Fact]
    public async Task Stopping_between_passes_does_not_write_the_last_batch_again()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (service, writerHeld, schemaLookups) = CreateService(gate.Task);

        // Every entry its own project, so each write attempt shows as one schema lookup.
        const int waiting = 7;
        try
        {
            await service.LogAsync(NewEvent(), TestContext.Current.CancellationToken);
            await writerHeld.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            for (var i = 0; i < waiting; i++)
            {
                await service.LogAsync(NewEvent(), TestContext.Current.CancellationToken);
            }
        }
        finally
        {
            gate.TrySetResult();
            await service.DisposeAsync();
        }

        schemaLookups().Should().Be(1 + waiting,
            "the entry the writer held was attempted when it was held; the final flush is for the seven that waited");
    }

    [Fact]
    public async Task An_entry_offered_after_the_service_stopped_is_counted()
    {
        using var counts = new DropCounts();
        var (service, _, _) = CreateService(Task.CompletedTask);
        await service.DisposeAsync();

        await service.LogAsync(NewEvent(), TestContext.Current.CancellationToken);

        counts.Of(AuditMetrics.ServiceStopped).Should().Be(1,
            "a completed queue refuses the write, and the entry is gone");
    }

    private static (PostgresAuditService Service, SemaphoreSlim WriterHeld, Func<int> SchemaLookups) CreateService(Task gate)
    {
        var writerHeld = new SemaphoreSlim(0);
        var lookups = 0;
        var projects = new Mock<IProjectRepository>();
        projects
            .Setup(p => p.GetSchemaNamesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                Interlocked.Increment(ref lookups);
                writerHeld.Release();
                await gate;
                throw new InvalidOperationException("the database is not there");
            });

        // The data source is never opened: the schema lookup above fails before any connection is asked for.
        var dataSource = NpgsqlDataSource.Create("Host=localhost;Database=never_reached;Username=x;Password=x");
        var connections = new MorphConnectionFactory(dataSource, new SecurityContextAccessor());

        var service = new PostgresAuditService(
            connections,
            Mock.Of<IPiiMaskingService>(),
            projects.Object,
            NullLogger<PostgresAuditService>.Instance);

        return (service, writerHeld, () => Volatile.Read(ref lookups));
    }

    private static AuditEvent NewEvent(Guid? project = null) => new() { ProjectId = project ?? Guid.NewGuid(), Action = "test" };
}
