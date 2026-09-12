using Labs626.UrScore.Core;

namespace UrScore.Tests;

public class SettingsTests
{
    [Fact]
    public void PollSecondsIsFlooredAtTheVendorsCache()
    {
        // Their clan endpoints send max-age=60, s-maxage=180. Polling faster returns the same
        // bytes and is simply rude, so a smaller configured value is raised rather than obeyed.
        Assert.Equal(180, new Settings("Clan", "clan.battle.points", 5).EffectivePollSeconds);
        Assert.Equal(180, new Settings("Clan", "clan.battle.points", 180).EffectivePollSeconds);
        Assert.Equal(600, new Settings("Clan", "clan.battle.points", 600).EffectivePollSeconds);
    }

    [Fact]
    public void DefaultsAreUsableWithoutAFile()
    {
        var s = Settings.Defaults;
        Assert.Equal("", s.ClanName);
        Assert.Equal("clan.battle.points", s.MetricId);
        Assert.Equal(180, s.EffectivePollSeconds);
    }

    [Fact]
    public void RoundTripsThroughDisk()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "settings.json");
        var written = new Settings("Noodle Clan", "run.points", 300);

        Settings.Save(written, path);

        Assert.Equal(written, Settings.Load(path));
    }

    [Fact]
    public void ExcludedAccountsRoundTripAndIgnoreRubbish()
    {
        // An exclude list, not an include list: a new account is watched by default, and turning
        // one off is what persists. A hand-edited entry that is not a Guid is dropped rather than
        // taking the list down with it.
        var settings = new Settings("Clan", "clan.battle.points", 180,
            ["9ad5e605-6b41-478c-add3-b916a31a5ab2", "not-a-guid"]);

        Assert.Single(settings.Excluded);
        Assert.Contains(Guid.Parse("9ad5e605-6b41-478c-add3-b916a31a5ab2"), settings.Excluded);
    }

    [Fact]
    public void NoExclusionsMeansEveryAccountIsWatched()
    {
        Assert.Empty(Settings.Defaults.Excluded);
    }

    [Fact]
    public void ResolveNamesDefaultsOnAndSurvivesADiskRoundTripWhenTurnedOff()
    {
        // Default true: a fresh install shows names without the user finding a switch first.
        Assert.True(Settings.Defaults.ResolveNames);

        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "settings.json");
        var written = new Settings("Noodle Clan", "run.points", 300, ResolveNames: false);

        Settings.Save(written, path);

        Assert.False(Settings.Load(path).ResolveNames);
    }

    [Fact]
    public void LoadingWithNoFilePresentWritesTheDefaultsSoThereIsSomethingToEdit()
    {
        // The README and the window both say "start it once and it creates the file." Before this,
        // that was false: Save's only other caller needs seeded rows, which need a reachable
        // RoRoRo — unreachable on a fresh install that has never run RoRoRo yet. A user following
        // that instruction found no folder at all and stopped.
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "settings.json");

        var loaded = Settings.Load(path);

        Assert.True(File.Exists(path));
        Assert.Equal(Settings.Defaults, loaded);
        Assert.Equal(Settings.Defaults, Settings.Load(path));   // idempotent on a second read
    }

    [Fact]
    public void AnUnreadableFileYieldsDefaultsRatherThanThrowing()
    {
        // The window must open. A settings file someone hand-edited into invalid JSON is a thing
        // to say out loud later, never a reason to fail to start.
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, "{ not json");

        Assert.Equal(Settings.Defaults, Settings.Load(path));
    }

    [Fact]
    public void AnEmptyJsonObjectYieldsUsableDefaultsForBothStrings()
    {
        // Deserializing a record does not enforce non-nullability. Valid JSON that simply has
        // nothing in it must not hand back a Settings whose ClanName or MetricId is null.
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, "{}");

        var loaded = Settings.Load(path);

        Assert.Equal(Settings.Defaults.ClanName, loaded.ClanName);
        Assert.Equal(Settings.Defaults.MetricId, loaded.MetricId);
    }

    [Fact]
    public void APartialFileKeepsWhatItHasAndDefaultsWhatIsMissing()
    {
        // The far more likely real-world shape than corrupt syntax: a file missing one line.
        // Repair per field rather than discarding the whole thing the user wrote.
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, "{\"clanName\":\"Noodle Clan\"}");

        var loaded = Settings.Load(path);

        Assert.Equal("Noodle Clan", loaded.ClanName);
        Assert.Equal(Settings.DefaultMetricId, loaded.MetricId);
    }

    [Fact]
    public void ASettingsFileFromBeforeResolveNamesExistedLoadsWithItOn()
    {
        // A real installed base wrote this file before ResolveNames existed. If a missing
        // property deserialized to bool's own default (false) rather than the constructor
        // parameter's stated default (true), every existing install would silently lose names
        // the moment it updated — with nothing anywhere saying why.
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, "{\"clanName\":\"Noodle Clan\",\"metricId\":\"run.points\",\"pollSeconds\":300}");

        Assert.True(Settings.Load(path).ResolveNames);
    }
}
