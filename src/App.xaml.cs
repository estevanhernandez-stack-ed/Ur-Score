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

    protected override void OnStartup(StartupEventArgs e)
    {
        _instance = new Mutex(initiallyOwned: true, SingleInstanceName, out var isFirst);
        if (!isFirst)
        {
            // No dialog. A user who double-clicked twice does not need telling.
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
