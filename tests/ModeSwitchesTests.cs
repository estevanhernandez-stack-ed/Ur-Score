using Labs626.UrScore.Games;

namespace UrScore.Tests;

public class ModeSwitchesTests
{
    private const string Battle = "pet-sim-99/battle";
    private const string Profile = "pet-sim-99/profile";
    private const string Game = "pet-sim-99";

    private static ModeSwitches Of(Dictionary<string, bool>? modes) => new(GameCatalog.BuiltIn, modes);

    [Fact]
    public void NoMapMeansTheManifestDefaults()
    {
        var switches = Of(null);

        Assert.True(switches.IsGameOn(Game));
        Assert.True(switches.IsModeSet(Battle));
        Assert.True(switches.IsOn(Profile));
        Assert.True(switches.IsReaderOn("pet-sim-99-top-clans"));
    }

    [Fact]
    public void AnExplicitKeyBeatsTheDefaultAndTheOtherModeKeepsIt()
    {
        var switches = Of(new() { [Profile] = false });

        Assert.False(switches.IsOn(Profile));
        Assert.False(switches.IsReaderOn("pet-sim-99-profile"));
        Assert.True(switches.IsOn(Battle));
    }

    [Fact]
    public void GameOffMasksEveryModeButTheModesStaySet()
    {
        var switches = Of(new() { [Game] = false, [Battle] = true, [Profile] = false });

        Assert.False(switches.IsGameOn(Game));
        Assert.False(switches.IsOn(Battle));
        Assert.True(switches.IsModeSet(Battle));
        Assert.False(switches.IsModeSet(Profile));
        Assert.False(switches.IsReaderOn("pet-sim-99-clan-battle-points"));
    }

    [Fact]
    public void TurningTheGameOffChangesOnlyTheGameKey()
    {
        var before = new Dictionary<string, bool> { [Battle] = true, [Profile] = false };

        var after = Of(before).With(Game, false);

        Assert.False(after[Game]);
        Assert.True(after[Battle]);
        Assert.False(after[Profile]);
        Assert.Equal(3, after.Count);

        // And back on: the modes are exactly as the player left them.
        var again = Of(new Dictionary<string, bool>(after)).With(Game, true);
        var restored = Of(new Dictionary<string, bool>(again));
        Assert.True(restored.IsOn(Battle));
        Assert.False(restored.IsOn(Profile));
    }

    [Fact]
    public void WithReturnsANewMapAndLeavesDefaultsImplicit()
    {
        var original = new Dictionary<string, bool> { [Profile] = false };

        var next = Of(original).With(Battle, false);

        Assert.Single(original);
        Assert.Equal(2, next.Count);
        Assert.False(next[Battle]);
        Assert.False(next[Profile]);
        Assert.False(next.ContainsKey(Game));
        Assert.Single(Of(null).With(Battle, true));
    }

    [Fact]
    public void AnOrphanSlugAndAnUnknownModeAreNeverOn()
    {
        var switches = Of(null);

        Assert.False(switches.IsReaderOn("roblox-followers"));
        Assert.False(switches.IsOn("no-game/no-mode"));
        Assert.False(switches.IsModeSet("no-game/no-mode"));
    }
}
