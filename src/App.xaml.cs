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

        // Last, after Show: until here a failure must end the process, which releases the single-instance mutex.
        _shown = _services;
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
        // Flushes the score book's pending lines before the process goes.
        _services?.Dispose();
        if (_owns) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
}
