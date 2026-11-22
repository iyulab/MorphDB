using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
using Npgsql;

namespace MorphDB.Tests.Unit;

/// <summary>
/// The metadata inserts run as autocommit statements, and a table created in a busy service was seen to lose
/// one of them to a deadlock -- leaving a physical table with no metadata. A deadlock victim's statement
/// changed nothing, so it is sent again; nothing else is.
/// </summary>
public class DeadlockRetryTests
{
    private static PostgresException Failure(string sqlState) => new("failed", "ERROR", "ERROR", sqlState);

    [Theory]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    public async Task A_statement_chosen_as_a_deadlock_victim_is_sent_again_until_it_succeeds(string sqlState)
    {
        var calls = 0;

        var result = await DeadlockRetry.RunAsync(() => ++calls < 3 ? throw Failure(sqlState) : Task.FromResult("done"));

        result.Should().Be("done");
        calls.Should().Be(3);
    }

    [Fact]
    public async Task A_deadlock_that_keeps_happening_is_thrown_after_the_last_attempt()
    {
        var calls = 0;

        var act = () => DeadlockRetry.RunAsync<string>(() => { calls++; throw Failure(PostgresErrorCodes.DeadlockDetected); });

        await act.Should().ThrowAsync<PostgresException>();
        calls.Should().Be(4, "a deadlock is retried a few times, not forever");
    }

    [Fact]
    public async Task Any_other_failure_is_thrown_at_once_and_never_sent_again()
    {
        var calls = 0;

        var act = () => DeadlockRetry.RunAsync<string>(() => { calls++; throw Failure(PostgresErrorCodes.UniqueViolation); });

        await act.Should().ThrowAsync<PostgresException>();
        calls.Should().Be(1, "a unique violation is the caller's answer, not a collision to retry");
    }

    [Theory]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    public async Task A_metadata_read_chosen_as_a_deadlock_victim_is_sent_again(string sqlState)
    {
        var calls = 0;

        var result = await DeadlockRetry.RunReadAsync(() => ++calls < 2 ? throw Failure(sqlState) : Task.FromResult("listed"));

        result.Should().Be("listed");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task A_read_inside_a_unit_of_work_is_not_sent_again_because_the_whole_transaction_was_the_victim()
    {
        var calls = 0;
        using var scope = ConnectionScope.Begin(new UnitTransaction(null!));

        var act = () => DeadlockRetry.RunReadAsync<string>(() => { calls++; throw Failure(PostgresErrorCodes.DeadlockDetected); });

        await act.Should().ThrowAsync<PostgresException>();
        calls.Should().Be(1, "PostgreSQL has abandoned the transaction; the statement cannot run in it again");
    }

    [Fact]
    public async Task A_metadata_read_that_fails_otherwise_is_thrown_at_once()
    {
        var calls = 0;

        var act = () => DeadlockRetry.RunReadAsync<string>(() => { calls++; throw Failure(PostgresErrorCodes.UndefinedTable); });

        await act.Should().ThrowAsync<PostgresException>();
        calls.Should().Be(1);
    }
}
