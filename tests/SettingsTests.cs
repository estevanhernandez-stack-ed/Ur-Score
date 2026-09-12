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
    public void AnUnreadableFileYieldsDefaultsRatherThanThrowing()
    {
        // The window must open. A settings file someone hand-edited into invalid JSON is a thing
        // to say out loud later, never a reason to fail to start.
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, "{ not json");

        Assert.Equal(Settings.Defaults, Settings.Load(path));
    }
}
