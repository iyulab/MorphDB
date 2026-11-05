using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Services;

namespace MorphDB.Tests.Fixtures;

/// <summary>
/// Two schema requests that meet in the middle: the first is held open -- its change made, its transaction not
/// yet committed -- the second is started behind it and given time to get as far as it can, and then the
/// first is let go.
/// </summary>
internal static class SchemaRace
{
    public static async Task<(Exception? First, Exception? Second)> RunAsync(
        MorphConnectionFactory connections,
        Func<IChangeLogger, Task> first,
        Func<Task> second,
        CancellationToken ct)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstTask = first(new GatedChangeLogger(new ChangeLogger(connections), reached, gate.Task));
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);

        var secondTask = second();
        await Task.Delay(1500, ct);
        gate.SetResult();

        return (await CaptureAsync(firstTask), await CaptureAsync(secondTask));
    }

    private static async Task<Exception?> CaptureAsync(Task task)
    {
        try
        {
            await task;
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
