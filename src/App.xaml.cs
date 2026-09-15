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

    /// <summary>The board is shown; only from then on is an unhandled UI-thread failure kept from ending the app.</summary>
    private bool _started;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Task 15 adds the --try branch HERE, first, before the single-instance mutex: a try-out runs no
        // window, no RoRoRo, no state, no book and no mutex (spec §10).

        if (Cli.TryCommand.Wants(e.Args))
        {
            // A console to write to when launched from one; redirected output works without it.
            AttachConsole(-1);

            using var http = new System.Net.Http.HttpClient(Recipes.HttpRecipeTransport.CreateHandler());
            var keys = new Recipes.KeyStore(Recipes.KeyStore.DefaultPath);
            var transport = new Recipes.HttpRecipeTransport(http, rawDirectory: null, new Recipes.Redactor(() => keys.Values()));
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
        _started = true;
    }

    /// <summary>
    /// The last net under every UI-thread callback: once the board is shown, a failure goes to the trail (its type
    /// only) and the app keeps running, so it can still flush the score book on exit. Before that a failure is left
    /// to end the process. <see cref="Application.MainWindow"/> can't tell the two apart: WPF sets it to the first
    /// window constructed, before that window's own constructor or <c>Show</c> has finished.
    /// </summary>
    private void OnUnhandled(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        if (!_started || _services is null) return;

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

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
}
