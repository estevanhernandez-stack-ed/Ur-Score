using System.Threading;

namespace Labs626.UrScore.Core;

/// <summary>
/// Starting Ur Score again brings the running copy's board forward. With tray mode (RoRoRo 1.33's autostart) the first copy can
/// be sitting in the tray with its board hidden, and Windows 11 hides new tray icons, so without this a second start (Open in
/// RoRoRo's Plugins list) did nothing anyone could see. Ported from K0ii Score.
/// <para>
/// The single-instance mutex can't carry a message, so this is the channel: the first copy creates a named auto-reset event
/// and waits on it from the pool; a second copy that loses the mutex sets it (<see cref="Notify"/>) and exits. Each set
/// runs <c>onSignal</c> once, on a pool thread, so the app posts it to the UI thread itself. An autostart sets nothing
/// (<see cref="LaunchMode.SecondStartShows"/>): RoRoRo opening again must never pop up a board left in the tray.
/// </para>
/// <para>No WPF here, and the name is a parameter, so a test sets and observes it in-process under a name of its own.</para>
/// </summary>
public sealed class SingleInstanceSignal : IDisposable
{
    public const string DefaultName = @"Local\626labs.ur-score.show";

    private readonly EventWaitHandle _handle;
    private readonly RegisteredWaitHandle _registration;
    private readonly Lock _gate = new();
    private bool _disposed;

    /// <param name="onSignal">Runs once per set, on a pool thread.</param>
    /// <param name="name">The event's name; only the tests change it.</param>
    public SingleInstanceSignal(Action onSignal, string name = DefaultName)
    {
        ArgumentNullException.ThrowIfNull(onSignal);
        _handle = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, name);
        try
        {
            _registration = ThreadPool.RegisterWaitForSingleObject(
                _handle,
                (_, _) =>
                {
                    lock (_gate)
                    {
                        if (_disposed) return;
                    }

                    onSignal();
                },
                state: null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }
        catch
        {
            // Not registered: the handle is ours to let go of.
            _handle.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The second copy's half: sets the running copy's event, unless this start is an autostart. False when nothing was set
    /// (an autostart, or no copy is running, or it hasn't made its event yet); the caller exits either way.
    /// </summary>
    public static bool Notify(LaunchReason reason, string name = DefaultName)
    {
        if (!LaunchMode.SecondStartShows(reason)) return false;

        try
        {
            using var handle = EventWaitHandle.OpenExisting(name);
            return handle.Set();
        }
        catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or System.IO.IOException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _registration.Unregister(null);
        _handle.Dispose();
    }
}
