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
        Settings.Save(new Settings(ResolveNames: false, ActiveRecipe: "pet-sim-99-clan-battle-points"), File());
        Assert.Equal(new Settings(false, "pet-sim-99-clan-battle-points"), Settings.Load(File()));
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
}
