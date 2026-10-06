using Labs626.UrScore.Core;

namespace UrScore.Tests;

/// <summary>
/// A second start while the first copy runs, tray or window, brings the board forward: the second copy sets a named event and
/// exits, and the first copy's wait on it shows the board. Ported from K0ii Score. Each test uses its own event name, so they
/// never meet the real app's or each other's.
/// </summary>
public class SingleInstanceSignalTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(300);

    private static string NewName() => $@"Local\626labs.ur-score.test-{Guid.NewGuid():N}";

    [Fact]
    public void TheNameIsTheAppsOwn()
    {
        Assert.Equal(@"Local\626labs.ur-score.show", SingleInstanceSignal.DefaultName);
    }

    [Fact]
    public void ASecondStartSetsTheEventAndTheFirstCopysCallbackRunsOnce()
    {
        var name = NewName();
        var calls = 0;
        using var fired = new ManualResetEventSlim();
        using var signal = new SingleInstanceSignal(() =>
        {
            Interlocked.Increment(ref calls);
            fired.Set();
        }, name);

        Assert.True(SingleInstanceSignal.Notify(LaunchReason.Manual, name));

        Assert.True(fired.Wait(Wait));
        Thread.Sleep(Quiet);
        Assert.Equal(1, Volatile.Read(ref calls));
    }

    [Fact]
    public void EachSecondStartBringsItBackAgain()
    {
        var name = NewName();
        var calls = 0;
        using var fired = new SemaphoreSlim(0);
        using var signal = new SingleInstanceSignal(() =>
        {
            Interlocked.Increment(ref calls);
            fired.Release();
        }, name);

        Assert.True(SingleInstanceSignal.Notify(LaunchReason.Unknown, name));
        Assert.True(fired.Wait(Wait));
        Assert.True(SingleInstanceSignal.Notify(LaunchReason.Manual, name));
        Assert.True(fired.Wait(Wait));

        Assert.Equal(2, Volatile.Read(ref calls));
    }

    [Fact]
    public void AnAutostartSignalsNothing()
    {
        // RoRoRo opening again must never pop the board of a copy the player left in the tray.
        var name = NewName();
        var calls = 0;
        using var signal = new SingleInstanceSignal(() => Interlocked.Increment(ref calls), name);

        Assert.False(SingleInstanceSignal.Notify(LaunchReason.Autostart, name));

        Thread.Sleep(Quiet);
        Assert.Equal(0, Volatile.Read(ref calls));
    }

    [Fact]
    public void WithNoFirstCopyThereIsNothingToSignal()
    {
        Assert.False(SingleInstanceSignal.Notify(LaunchReason.Manual, NewName()));
    }

    [Fact]
    public void DisposeUnregistersTheWait()
    {
        var name = NewName();
        var calls = 0;
        var signal = new SingleInstanceSignal(() => Interlocked.Increment(ref calls), name);

        // Held open from outside, so the event itself outlives the signal and a set still lands on it.
        using var outside = EventWaitHandle.OpenExisting(name);
        signal.Dispose();

        outside.Set();
        Thread.Sleep(Quiet);

        Assert.Equal(0, Volatile.Read(ref calls));
        // Nothing consumed the set: the wait really is gone, not just the callback muted.
        Assert.True(outside.WaitOne(0));
    }

    [Fact]
    public void DisposingTwiceIsHarmless()
    {
        var signal = new SingleInstanceSignal(() => { }, NewName());
        signal.Dispose();
        signal.Dispose();
    }
}
