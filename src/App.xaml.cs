using System.Threading;
using System.Windows;

namespace Labs626.UrScore;

public partial class App : Application
{
    /// <summary>
    /// One copy. Two instances polling the same clan would double the request rate against
    /// someone else's API and report every number twice, which the host would see as a rate
    /// computed over duplicated samples.
    /// </summary>
    private const string SingleInstanceName = @"Local\626labs.ur-score.single-instance";

    private Mutex? _instance;
    private bool _owns;
    private Composition.AppServices? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Task 15 adds the --try branch HERE, first, before the single-instance mutex: a try-out runs no
        // window, no RoRoRo, no state, no book and no mutex (spec §10).

        _instance = new Mutex(initiallyOwned: true, SingleInstanceName, out var isFirst);
        _owns = isFirst;

        if (!isFirst)
        {
            // Do NOT release: this process never owned it, and releasing a mutex you do not own
            // throws ApplicationException — on the exact path this guard exists to serve.
            _instance.Dispose();
            _instance = null;
            Shutdown();
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        Theming.ThemeService.Start();

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        _services = new Composition.AppServices(Dispatcher);
        var board = new UI.BoardWindow(_services);
        MainWindow = board;
        board.Show();
    }

    /// <summary>
    /// The last net under every UI-thread callback: once the board is up, a failure goes to the trail (its type
    /// only) and the app keeps running, so it can still flush the score book on exit. Before that there is no
    /// window to keep, so a startup failure is left to end the process.
    /// </summary>
    private void OnUnhandled(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        if (_services is null || MainWindow is null) return;

        _services.AddTrail($"UNHANDLED: {e.Exception.GetType().Name}");
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Flushes the score book's pending lines before the process goes.
        _services?.Dispose();
        if (_owns) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
