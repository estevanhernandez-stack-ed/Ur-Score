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

    /// <summary>
    /// The services of a board that is SHOWN, and null until then: only from then on is an unhandled UI-thread
    /// failure kept from ending the app. One field rather than a flag beside <see cref="_services"/>, because the
    /// guard then read <c>!_started || _services is null</c>, and the second half could never be true once the
    /// first was false — a check that reads as a case and is not one (S1-14.16).
    /// </summary>
    private Composition.AppServices? _shown;

    /// <summary>The tray icon, only when Ur Score started in the tray (<see cref="Core.LaunchMode"/>); disposed first on exit.</summary>
    private UI.Tray? _tray;

    /// <summary>Starting Ur Score again, while this copy runs, brings the board forward (<see cref="Core.SingleInstanceSignal"/>).</summary>
    private Core.SingleInstanceSignal? _signal;

    /// <summary>What a second start runs, once the board exists; null until then and once the app is going.</summary>
    private Action? _open;

    /// <summary>The mode Ur Score is in now, as an update should bring it back; written on every change and on exit.</summary>
    private Core.AppMode _mode;

    /// <summary>
    /// The tray's Open board, a left click on its icon, and a second start: the board, restored if minimized, in front. Never a
    /// board that has closed or is closing.
    /// </summary>
    private static void ShowBoard(UI.BoardWindow board)
    {
        if (board.ClosedOrQuitting) return;
        board.Show();
        if (board.WindowState == WindowState.Minimized) board.WindowState = WindowState.Normal;
        board.Activate();
    }

    /// <summary>
    /// Set by the test harness before it builds its one <c>App</c> (tests/TestProcess.cs, tests/UiThread.cs), so the app's
    /// start never runs in a test process. WPF's <c>Application</c> constructor queues <see cref="OnStartup"/> onto the
    /// dispatcher, and the harness runs that dispatcher, so without this every suite run started Ur Score for real: the
    /// single-instance mutex, AppServices over the user's REAL data folder, the board. With a real copy running, the mutex
    /// made that start shut the test application down. Ported from K0ii Score (fe8d103). Never set by the app.
    /// </summary>
    internal static bool HostedByTests { get; set; }

    /// <summary>How many times <see cref="OnStartup"/>'s body has run in this process: the app's own start, once; never in a test.</summary>
    private static int _startupsRun;

    internal static int StartupsRun => Volatile.Read(ref _startupsRun);

    protected override void OnStartup(StartupEventArgs e)
    {
        // Before anything, the --try branch and the mutex included: a test process gets the resources App.xaml gives and
        // nothing else. The try-out itself is tested by starting the built exe (TryCommandTests), never through this.
        if (HostedByTests) return;

        Interlocked.Increment(ref _startupsRun);

        // The --try branch comes first, before the single-instance mutex: a try-out runs no window, no RoRoRo,
        // no state, no book and no mutex (spec §10).
        if (Cli.TryCommand.Wants(e.Args))
        {
            // A console to write to when launched from one; redirected output works without it.
            AttachConsole(-1);

            using var http = new System.Net.Http.HttpClient(Recipes.HttpRecipeTransport.CreateHandler());
            var keys = new Recipes.KeyStore(Recipes.KeyStore.DefaultPath);
            var inner = new Recipes.HttpRecipeTransport(http, rawDirectory: null, new Recipes.Redactor(() => keys.Values()));
            var transport = new Recipes.SpacedTransport(inner, TimeProvider.System, Recipes.SpacedTransport.DefaultSpacing);
            var code = Cli.TryCommand.RunAsync(e.Args, Console.Out, transport, keys, CancellationToken.None).GetAwaiter().GetResult();
            Console.Out.Flush();
            Shutdown(code);
            return;
        }

        // RoRoRo 1.33 says why it started Ur Score. Read once, here, after the --try branch (which it never touches) and before
        // anything is started: cleared at once, so nothing Ur Score starts (a browser for a link) inherits it.
        var reason = Core.LaunchMode.TakeReasonFromProcess();

        _instance = new Mutex(initiallyOwned: true, SingleInstanceName, out var isFirst);
        _owns = isFirst;

        if (!isFirst)
        {
            // Do NOT release: this process never owned it, and releasing a mutex you do not own
            // throws ApplicationException — on the exact path this guard exists to serve.
            _instance.Dispose();
            _instance = null;

            // Bring the running copy's board forward, then go; an autostart signals nothing.
            Core.SingleInstanceSignal.Notify(reason);
            Shutdown();
            return;
        }

        // Straight after the mutex, so a second start finds it as early as possible. Posted to the UI thread; before the board
        // exists there is no Open yet and a signal does nothing. A convenience, never a reason not to start.
        try
        {
            _signal = new Core.SingleInstanceSignal(() => Dispatcher.BeginInvoke(() => _open?.Invoke()));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or System.IO.IOException)
        {
            _signal = null;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        Theming.ThemeService.Start();

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        _services = new Composition.AppServices(Dispatcher);
        var services = _services;

        // Window or tray (Core.LaunchMode): the tray only for RoRoRo's autostart with the setting on, or an update of a copy
        // that was in the tray, and never on a first run.
        var mode = services.StartMode(reason);
        var startedInTray = mode == Core.AppMode.Tray;
        services.AddTrail($"LAUNCH: {reason}, so {(startedInTray ? "the tray" : "the window")}.");

        var board = new UI.BoardWindow(services);
        MainWindow = board;

        // The icon before the hidden board: a tray start with no icon would be a copy nobody can see or quit, so an icon that
        // can't be made turns this start into a window start.
        if (startedInTray && !TryStartTray(board, services)) startedInTray = false;
        board.StartedInTray = startedInTray;
        if (startedInTray) board.OpenHidden();
        else board.Show();

        // Last, after Show: until here a failure must end the process, which releases the single-instance mutex. A tray start
        // counts as shown: from here on the app runs, window or not, and must not die on a callback.
        _shown = services;

        _open = () => ShowBoard(board);
        _mode = Core.LaunchMode.Current(startedInTray, board.IsVisible);
        services.RememberMode(_mode);
        board.IsVisibleChanged += (_, _) =>
        {
            // A board closing for good isn't a change of mode; only shown and hidden while Ur Score runs are.
            if (board.ClosedOrQuitting || EndingSession) return;
            _mode = Core.LaunchMode.Current(startedInTray, board.IsVisible);
            services.RememberMode(_mode);
        };

    }

    /// <summary>
    /// The tray icon of a tray start: Open board (and a left click), Pause or Resume reading through the board's own Pause, and
    /// Quit, which closes the board for real and with it the app. It stays while the board is open, since a tray start's close
    /// box hides the board back into it. A window start has none: closing it ends Ur Score, as it always has.
    /// </summary>
    private bool TryStartTray(UI.BoardWindow board, Composition.AppServices services)
    {
        try
        {
            StartTray(board, services);
            return true;
        }
        catch (Exception ex)
        {
            services.AddTrail($"TRAY NOT STARTED: {ex.GetType().Name}; the window opens instead.");
            _tray?.Dispose();
            _tray = null;
            return false;
        }
    }

    private void StartTray(UI.BoardWindow board, Composition.AppServices services)
    {
        var trayModel = new UI.TrayModel();
        _tray = new UI.Tray(
            board.TrayNow,
            new UI.TrayTargets(
                Open: () => ShowBoard(board),
                PauseOrResume: () => Core.Unawaited.TrailFailures(board.PauseOrResumeAsync(), services.AddTrail, "TRAY PAUSE FAILED"),
                Quit: () =>
                {
                    if (board.Quit()) Shutdown();
                }));

        // The main clan's icon, as the window and the taskbar wear it: now, and whenever it changes.
        _tray.ShowPicture(services.WindowIcon.File);
        services.IconChanged += icon => _tray?.ShowPicture(icon.File);
        services.Changed += () => _tray?.Refresh();
        board.HiddenToTray += () =>
        {
            if (trayModel.NoticeOnHide() is { } notice) _tray?.Show(UI.TrayModel.AppName, notice);
        };
    }

    /// <summary>
    /// The last net under every UI-thread callback: once the board is shown, a failure goes to the trail (its type
    /// only) and the app keeps running, so it can still flush the score book on exit. Before that a failure is left
    /// to end the process. <see cref="Application.MainWindow"/> can't tell the two apart: WPF sets it to the first
    /// window constructed, before that window's own constructor or <c>Show</c> has finished.
    /// </summary>
    private void OnUnhandled(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        if (_shown is not { } services) return;

        services.AddTrail($"UNHANDLED: {e.Exception.GetType().Name}");
        e.Handled = true;
    }

    /// <summary>Windows is signing out or shutting down: nothing can keep Ur Score running, so closing does not ask.</summary>
    public static bool EndingSession { get; private set; }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        EndingSession = true;
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // No second start reaches a copy that is going.
        _open = null;
        _signal?.Dispose();

        // The icon first, so it never outlives the app in the notification area.
        _tray?.Dispose();

        // The mode it ran in, for an update's start (written on every change already; this is the last word).
        _services?.RememberMode(_mode);

        // Flushes the score book's pending lines before the process goes.
        _services?.Dispose();
        if (_owns) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
}
