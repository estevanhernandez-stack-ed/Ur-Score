using Labs626.UrScore.Core;

namespace UrScore.Tests;

public class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "urscore-settings-" + Guid.NewGuid().ToString("N"));

    private string File(string name = "settings.json") => Path.Combine(_dir, name);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private void Write(string json)
    {
        Directory.CreateDirectory(_dir);
        System.IO.File.WriteAllText(File(), json);
    }

    [Fact]
    public void DefaultsAreUsableWithoutAFile()
    {
        Assert.True(Settings.Defaults.ResolveNames);
        Assert.Null(Settings.Defaults.ActiveRecipe);
    }

    [Fact]
    public void RoundTripsThroughDisk()
    {
        Settings.Save(new Settings(ResolveNames: false, ActiveRecipe: "pet-sim-99-clan-battle-points", SettingsVersion: Settings.CurrentVersion), File());
        Assert.Equal(new Settings(false, "pet-sim-99-clan-battle-points", SettingsVersion: Settings.CurrentVersion), Settings.Load(File()));
    }

    [Fact]
    public void SavedJsonUsesCamelCaseKeys()
    {
        Settings.Save(new Settings(false, "x"), File());
        var json = System.IO.File.ReadAllText(File());

        Assert.Contains("\"resolveNames\"", json);
        Assert.Contains("\"activeRecipe\"", json);
        Assert.DoesNotContain("\"ResolveNames\"", json);
    }

    [Fact]
    public void APascalCaseFileStillLoads()
    {
        Write("""{ "ResolveNames": false }""");
        Assert.False(Settings.Load(File()).ResolveNames);
    }

    [Fact]
    public void AFileFromBeforeRecipesStillLoads()
    {
        // Este's own test install wrote this shape on 2026-09-13.
        Write("""{ "clanName": "", "metricId": "clan.battle.points", "pollSeconds": 180, "excludedAccountIds": [], "resolveNames": false }""");

        var loaded = Settings.Load(File());
        Assert.False(loaded.ResolveNames);
        Assert.Null(loaded.ActiveRecipe);
    }

    [Fact]
    public void LoadingWithNoFileWritesTheDefaultsSoThereIsSomethingToEdit()
    {
        Assert.Equal(Settings.Defaults, Settings.Load(File()));
        Assert.True(System.IO.File.Exists(File()));
    }

    [Fact]
    public void AnUnreadableFileYieldsDefaultsRatherThanThrowing()
    {
        Write("{ not json");
        Assert.Equal(Settings.Defaults, Settings.Load(File()));
    }

    [Fact]
    public void AnEmptyObjectYieldsDefaults()
    {
        Write("{}");
        Assert.Equal(Settings.Defaults, Settings.Load(File()));
    }

    /// <summary>BC1 (2026-09-23, superseding A31): reading starts on open by default, for a new install.</summary>
    [Fact]
    public void ANewInstallReadsOnOpen()
    {
        Assert.True(Settings.Defaults.StartOnOpen);
        Assert.Equal(Settings.CurrentVersion, Settings.Defaults.SettingsVersion);
        Assert.True(Settings.Load(File()).StartOnOpen);
    }

    /// <summary>
    /// BC1, "yes for both": an existing install turns on too. Every settings.json written before 0.6 holds an explicit
    /// false only because Load wrote the defaults on first launch, so the file is migrated once, and saved.
    /// </summary>
    [Fact]
    public void AnExistingInstallIsTurnedOnOnceAndSaved()
    {
        Write("""{ "resolveNames": false, "activeRecipe": "pet-sim-99-profile", "startOnOpen": false }""");

        var loaded = Settings.Load(File());

        Assert.Equal(new Settings(false, "pet-sim-99-profile", StartOnOpen: true, SettingsVersion: Settings.CurrentVersion), loaded);
        Assert.Contains("\"settingsVersion\": 2", System.IO.File.ReadAllText(File()));
    }

    /// <summary>Review Focus 1: the migration runs once. A player who unticks it afterwards keeps it off.</summary>
    [Fact]
    public void MigrationRunsOnce()
    {
        Settings.Save(new Settings(StartOnOpen: false, SettingsVersion: Settings.CurrentVersion), File());

        Assert.False(Settings.Load(File()).StartOnOpen);
        Assert.False(Settings.Load(File()).StartOnOpen);
    }

    [Fact]
    public void StartOnOpenAndTheVersionRoundTripUnderCamelCaseKeys()
    {
        var saved = new Settings(ResolveNames: false, ActiveRecipe: "x", StartOnOpen: true, SettingsVersion: Settings.CurrentVersion);
        Settings.Save(saved, File());
        var json = System.IO.File.ReadAllText(File());

        Assert.Contains("\"startOnOpen\"", json);
        Assert.Contains("\"settingsVersion\"", json);
        Assert.DoesNotContain("\"StartOnOpen\"", json);
        Assert.Equal(saved, Settings.Load(File()));
    }

    /// <summary>A migrated file that can't be written still reads on open: the next open tries the write again.</summary>
    [Fact]
    public void MigrateIsPureAndIdempotent()
    {
        var old = new Settings(false, "x", StartOnOpen: false, SettingsVersion: 0);
        var once = Settings.Migrate(old);

        Assert.Equal(old with { StartOnOpen = true, SettingsVersion = Settings.CurrentVersion }, once);
        Assert.Same(once, Settings.Migrate(once));
    }
}
