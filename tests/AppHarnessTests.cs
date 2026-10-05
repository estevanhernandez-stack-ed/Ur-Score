using System.Reflection;

namespace UrScore.Tests;

/// <summary>
/// The test harness's one <c>App</c> never starts the app (port of K0ii Score's fe8d103). WPF's <c>Application</c>
/// constructor queues <c>OnStartup</c> onto its dispatcher, and <see cref="UiThread.RunInApp"/> runs that dispatcher. So
/// until <c>App.HostedByTests</c> existed, every suite run started Ur Score for real inside the test process: it took the
/// single-instance mutex and, with no copy running, composed <c>AppServices</c> over the user's REAL data folder and
/// showed the board. With a real copy running (holding the mutex) it shut the test application down instead, which is
/// what four WPF tests failed with on 2026-10-05 ("The Application object is being shut down") while Ur Score was open.
/// </summary>
[Collection(WpfCollection.Name)]
public class AppHarnessTests
{
    private const string SingleInstanceName = @"Local\626labs.ur-score.single-instance";

    /// <summary>
    /// The proof, reproduced: with the app's mutex held elsewhere (here by this test), the harness still works, because
    /// the app's startup never ran. Had it run, it would have found the mutex taken and shut the application down.
    /// </summary>
    [Fact]
    public void WithTheAppsMutexHeldTheHarnessStillWorksAndTheAppNeverStarted()
    {
        // Held by this test, or already by another process (a running Ur Score): either way the app's start, had it run,
        // would find it taken. Released only when this test is the one that owns it.
        using var held = new Mutex(initiallyOwned: true, SingleInstanceName, out var ours);
        try
        {
            string? resource = null;
            UiThread.RunInApp(() => resource = System.Windows.Application.Current.Resources["BgBrush"]?.GetType().Name);
            // The shutdown a started app would ask for is queued, not immediate: give it time to land before asking again.
            Thread.Sleep(500);
            // A window is what "The Application object is being shut down" stops.
            UiThread.RunInApp(() => new System.Windows.Window().Close());

            Assert.Equal("SolidColorBrush", resource);
            Assert.True(Labs626.UrScore.App.HostedByTests);
            Assert.Equal(0, Labs626.UrScore.App.StartupsRun);
            Assert.Equal(0, Labs626.UrScore.Composition.AppServices.OwnCompositions);
            Assert.Null(typeof(Labs626.UrScore.App).GetField("_instance", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(UiThread.App()));
        }
        finally
        {
            if (ours) held.ReleaseMutex();
        }
    }
}
