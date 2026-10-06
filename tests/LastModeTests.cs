using System.Windows.Threading;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Core;
using Labs626.UrScore.Fetch;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

/// <summary>
/// Tray mode through the composition: the start's mode from the reason, the settings and whether a first run is due, and the
/// last mode remembered in settings.json so an update brings Ur Score back the way it was. The whole app composed through its
/// seams over a folder of its own.
/// </summary>
public class LastModeTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 5, 18, 0, 0, TimeSpan.Zero);

    private static AppServices Compose(string folder) =>
        new(Dispatcher.CurrentDispatcher, new AppPaths(folder), new StubHost(reachable: false), new NullTransport(),
            new ManualTime(Start), Path.Combine(folder, "metric-rules.json"));

    [Fact]
    public void AFreshInstallIsAFirstRunAndOpensTheWindowEvenOnAnAutostart()
    {
        using var dir = TempDir.Create("urscore-last-mode-fresh");
        using var services = Compose(dir.Path);

        // Battle is on and asks for a clan nobody has picked: Setup opens on the game page, and that has to be seen.
        Assert.True(services.FirstRunDue);
        Assert.True(services.Settings.AutostartInTray);
        Assert.Equal(AppMode.Window, services.StartMode(LaunchReason.Autostart));
    }

    [Fact]
    public void AnInstallWithItsClansKeepsScoreInTheTrayOnAnAutostartOnly()
    {
        using var dir = OldInstallFixtureTests.Copy();
        using var services = Compose(dir.Path);

        Assert.False(services.FirstRunDue);
        Assert.Equal(AppMode.Tray, services.StartMode(LaunchReason.Autostart));
        Assert.Equal(AppMode.Window, services.StartMode(LaunchReason.Manual));
        Assert.Equal(AppMode.Window, services.StartMode(LaunchReason.Unknown));
        Assert.Equal(AppMode.Window, services.StartMode(LaunchReason.Update));

        services.SaveSettings(services.Settings with { AutostartInTray = false });
        Assert.Equal(AppMode.Window, services.StartMode(LaunchReason.Autostart));
    }

    [Fact]
    public void TheLastModeIsSavedAndAnUpdateNextStartComesBackInIt()
    {
        using var dir = OldInstallFixtureTests.Copy();
        var settingsFile = new AppPaths(dir.Path).Settings;

        using (var services = Compose(dir.Path))
        {
            services.RememberMode(AppMode.Tray);
            Assert.Equal("tray", services.Settings.LastMode);
            Assert.Equal("tray", Settings.Load(settingsFile).LastMode);
        }

        using (var next = Compose(dir.Path))
        {
            Assert.Equal(AppMode.Tray, next.StartMode(LaunchReason.Update));

            next.RememberMode(AppMode.Window);
            Assert.Equal("window", Settings.Load(settingsFile).LastMode);
            Assert.Equal(AppMode.Window, next.StartMode(LaunchReason.Update));
        }
    }

    [Fact]
    public void RememberingTheSameModeAgainWritesNothing()
    {
        using var dir = OldInstallFixtureTests.Copy();
        var settingsFile = new AppPaths(dir.Path).Settings;
        using var services = Compose(dir.Path);
        services.RememberMode(AppMode.Tray);
        var written = File.GetLastWriteTimeUtc(settingsFile);
        File.SetLastWriteTimeUtc(settingsFile, written.AddMinutes(-5));
        var changed = 0;
        services.Changed += () => changed++;

        services.RememberMode(AppMode.Tray);

        Assert.Equal(written.AddMinutes(-5), File.GetLastWriteTimeUtc(settingsFile));
        // Not a change anyone sees: the board doesn't redraw for it.
        Assert.Equal(0, changed);
    }

    [Fact]
    public void AnUnreadableSettingsFileIsNeverWrittenToRememberAMode()
    {
        using var dir = OldInstallFixtureTests.Copy();
        var settingsFile = new AppPaths(dir.Path).Settings;
        File.WriteAllText(settingsFile, "{ not json");
        using var services = Compose(dir.Path);

        services.RememberMode(AppMode.Tray);

        // The file stays what the player can fix, as every other settings write leaves it.
        Assert.Equal("{ not json", File.ReadAllText(settingsFile));
    }

    /// <summary>Nothing is read in these tests; a read that ran anyway finds nothing.</summary>
    private sealed class NullTransport : IRecipeTransport
    {
        public Task<FetchResult> GetAsync(Uri url, IReadOnlyDictionary<string, string> headers, string label, CancellationToken cancellationToken) =>
            Task.FromResult(new FetchResult(404, "{}", null));
    }
}
