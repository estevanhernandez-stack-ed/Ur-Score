using Labs626.UrScore.Core;

namespace UrScore.Tests;

/// <summary>
/// A task nobody awaits used to lose its failure: a discarded <c>Task</c> that faults raises nothing on the
/// UI thread, reaches no handler, and leaves no line — a bug in a clan pick or the name lookup was silent
/// (S1-11.1, S1-14.6). <see cref="Unawaited.TrailFailures"/> is the one shape every such call now takes,
/// and it sends the failure's TYPE to the trail, nothing more, for the same reason every other trail line
/// does (S1-14.14).
/// </summary>
public class UnawaitedTests
{
    [Fact]
    public void AFaultedTaskPutsItsTypeOnTheTrailAndNothingMore()
    {
        var trail = new List<string>();

        Unawaited.TrailFailures(Task.FromException(new InvalidOperationException("failed at https://example.com/?key=SECRET-VALUE")), trail.Add, "CLAN PICK");

        Assert.Equal("CLAN PICK: InvalidOperationException", Assert.Single(trail));
    }

    /// <summary>
    /// The fault of an async method arrives wrapped, and the trail must name the cause and not the wrapper: a
    /// line reading "AggregateException" tells nobody anything.
    /// </summary>
    [Fact]
    public async Task AnAsyncMethodsFaultIsNamedByItsCauseNotItsWrapper()
    {
        var trail = new List<string>();
        var done = new TaskCompletionSource();
        static async Task Broken()
        {
            await Task.Yield();
            throw new ArgumentOutOfRangeException("clan");
        }

        Unawaited.TrailFailures(Broken(), line => { trail.Add(line); done.TrySetResult(); }, "NAMES");
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("NAMES: ArgumentOutOfRangeException", Assert.Single(trail));
    }

    /// <summary>A stop the caller asked for is not news, and a task that finished has nothing to say.</summary>
    [Fact]
    public async Task ACancelledOrFinishedTaskLeavesNoLine()
    {
        var trail = new List<string>();
        static async Task Cancelled()
        {
            await Task.Yield();
            throw new OperationCanceledException();
        }

        Unawaited.TrailFailures(Task.CompletedTask, trail.Add, "DONE");
        var cancelled = Cancelled();
        Unawaited.TrailFailures(cancelled, trail.Add, "STOPPED");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        await Task.Delay(50);

        Assert.Empty(trail);
    }
}
