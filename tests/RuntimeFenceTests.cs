using System.Windows.Threading;
using Labs626.UrScore.Board;
using Labs626.UrScore.Book;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Core;
using Labs626.UrScore.Fetch;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

/// <summary>
/// In a test process the real data folder can't be reached at all (<see cref="TestProcess"/>): the latch is set before any
/// test runs, every default path resolves through it, and the app's own composition fails before it builds anything.
/// Ported from K0ii Score's fence (fe8d103), where the harness had started the app for real inside every suite run.
/// <para>
/// Not RoRoRo's rules file (<c>RulesFile.DefaultPath</c>): that is RoRoRo's, in RoRoRo's folder, and Ur Score only reads
/// it. K0ii moved its rules under its own folder, so its fence covers them; here they stay outside it.
/// </para>
/// </summary>
public class RuntimeFenceTests
{
    [Fact]
    public void TheAppsStartIsSwitchedOffBeforeAnyTest() => Assert.True(Labs626.UrScore.App.HostedByTests);

    /// <summary>
    /// The deliberate breach: every way a test could resolve the user's real folder, tried, and each one refused. Checked
    /// once against the latch removed (the ModuleInitializer's <c>RefuseDefault</c> commented out): this failed, then passed
    /// with it restored.
    /// </summary>
    [Fact]
    public void EveryDefaultPathRefusesInATestProcess()
    {
        Func<object>[] defaults =
        [
            () => AppPaths.Default,
            () => KeyStore.DefaultPath,
            () => RecipeStore.DefaultDirectory,
            () => Settings.DefaultPath,
            () => AccountsCache.DefaultPath,
            () => SourceStore.DefaultPath,
            () => BoardsFile.DefaultPath,
            () => BookFiles.DefaultRoot,
            () => IconClient.DefaultCacheDirectory,
        ];

        Assert.All(defaults, resolve => Assert.Contains("test process", Assert.Throws<InvalidOperationException>(resolve).Message));
    }

    [Fact]
    public void TheAppsOwnCompositionRefusesAndBuildsNothing()
    {
        Assert.Throws<InvalidOperationException>(() => new AppServices(Dispatcher.CurrentDispatcher));

        Assert.Equal(0, AppServices.OwnCompositions);
    }

    [Fact]
    public void TheEndOfRunCheckNamesABreachAndPassesACleanRun()
    {
        Assert.Null(TestProcess.Breach(0, 0));
        Assert.Contains("started 1 time(s)", TestProcess.Breach(1, 0));
        Assert.Contains("composed itself over the real data folder 1 time(s)", TestProcess.Breach(0, 1));
        Assert.Null(TestProcess.Breach(Labs626.UrScore.App.StartupsRun, AppServices.OwnCompositions));
    }
}
