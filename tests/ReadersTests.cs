using Labs626.UrScore.Core;
using Labs626.UrScore.Games;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

/// <summary>Readers.Compose: the embedded text, the saved or seeded state, and the orphans (spec "Readers", A1, A8).</summary>
public class ReadersTests
{
    private const string Clan = "pet-sim-99-clan-battle-points";
    private const string Top = "pet-sim-99-top-clans";
    private const string Profile = "pet-sim-99-profile";

    private static ReadersLoad Compose(string dataRoot) =>
        Readers.Compose(GameCatalog.BuiltIn, Readers.BuiltIn, new RecipeStore(new AppPaths(dataRoot).Recipes));

    /// <summary>Every file under a folder with its bytes, so "nothing was written" is a comparison, not a hope.</summary>
    private static Dictionary<string, string> Files(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(root, f), f => Convert.ToBase64String(File.ReadAllBytes(f)));

    [Fact]
    public void AnEmptyFolderGivesEveryReaderInManifestOrderWithTheEmbeddedTextAndWritesNothing()
    {
        using var dir = TempDir.Create("urscore-readers");

        var load = Compose(dir.Path);

        Assert.Equal([Clan, Top, Profile], load.Installed.Select(i => i.Recipe.Slug));
        Assert.All(load.Installed, i => Assert.Equal(BuiltInRecipes.Find(i.Recipe.Slug)!.Text, i.Text));
        Assert.Empty(load.Orphans);
        Assert.Empty(load.Problems);
        Assert.Empty(Files(dir.Path));
        Assert.False(Directory.Exists(new AppPaths(dir.Path).Recipes));
    }

    /// <summary>
    /// A1: the clan-battle recipe marks no value <c>show</c>, so without the manifest's <c>shows</c> a seeded Battle ticks
    /// nothing and its board can never fill. The profile seed is the recipe's own suggestion, untouched.
    /// </summary>
    [Fact]
    public void AFreshBattleIsSeededWithTheManifestsShowAndNothingSent()
    {
        using var dir = TempDir.Create("urscore-readers");

        var load = Compose(dir.Path);

        var clan = Assert.Single(load.Installed, i => i.Recipe.Slug == Clan);
        Assert.Equal(["value"], clan.State.ShownStats(clan.Recipe).Select(s => s.Key));
        Assert.Empty(clan.State.SentStats(clan.Recipe));
        var profile = Assert.Single(load.Installed, i => i.Recipe.Slug == Profile);
        Assert.Equal(RecipeStats.SuggestedChoices(profile.Recipe), profile.State.StatChoices);
    }

    /// <summary>
    /// Over the 0.6.3 fixture: the data folder's top-clans TEXT is an old one (no <c>groupsAreClans</c>) and is ignored,
    /// while its STATE (the clan numbers sent) is kept; the legacy <c>metricIdOverride</c> becomes its tick under the
    /// pinned id (A8); the orphan is listed and not installed; and not one byte of the folder changes.
    /// </summary>
    [Fact]
    public void OverAnOldInstallTheTextIsEmbeddedTheChoicesAreTheSavedOnesAndTheOrphanIsOnlyListed()
    {
        using var dir = OldInstallFixtureTests.Copy();
        var before = Files(dir.Path);

        var load = Compose(dir.Path);

        var top = Assert.Single(load.Installed, i => i.Recipe.Slug == Top);
        Assert.Equal(BuiltInRecipes.Find(Top)!.Text, top.Text);
        Assert.True(top.Recipe.GroupsAreClans);
        Assert.Equal(["gap-above", "place", "points"], top.State.FieldMetricKeys.Order(StringComparer.Ordinal));

        var clan = Assert.Single(load.Installed, i => i.Recipe.Slug == Clan);
        Assert.Equal(new StatChoice(Show: true, Send: true, MetricId: "clan.battle.points.mine"), clan.State.StatChoices["value"]);
        Assert.Equal("TestClan", clan.State.InputValues["clan"]);

        Assert.Equal(["roblox-followers"], load.Orphans.Select(o => o.Recipe.Slug));
        Assert.DoesNotContain(load.Installed, i => i.Recipe.Slug == "roblox-followers");
        Assert.Equal(before, Files(dir.Path));
    }

    [Fact]
    public void AModeThatReadsARecipeThatDoesNotShipIsAProblemNamingTheSlugAndTheRestStillLoad()
    {
        using var dir = TempDir.Create("urscore-readers");
        var catalog = new GameCatalog([new GameDef("g", "G", [
            new ModeDef("g", "a", "A", "", [Profile, "not-built-in"], null, null, null, true, []),
        ])]);

        var load = Readers.Compose(catalog, Readers.BuiltIn, new RecipeStore(new AppPaths(dir.Path).Recipes));

        Assert.Equal([Profile], load.Installed.Select(i => i.Recipe.Slug));
        Assert.Contains("not-built-in", Assert.Single(load.Problems), StringComparison.Ordinal);
    }

    /// <summary>A bad mode is dropped and said; the good one beside it is untouched, so one typo never costs the start.</summary>
    [Fact]
    public void SoundDropsOnlyTheModeAProblemNames()
    {
        var catalog = new GameCatalog([new GameDef("g", "G", [
            new ModeDef("g", "good", "Good", "", [Profile], null, null, "alts", true, []),
            new ModeDef("g", "bad", "Bad", "", [Clan], "nobody", null, "battle", true, []),
        ])]);

        var (sound, problems) = Readers.Sound(catalog, BuiltInRecipes.BySlug);

        Assert.Equal(["g/good"], sound.Modes.Select(m => m.Key));
        Assert.Contains("'g/bad'", Assert.Single(problems), StringComparison.Ordinal);
        Assert.Same(GameCatalog.BuiltIn, Readers.Sound(GameCatalog.BuiltIn, BuiltInRecipes.BySlug).Catalog);
    }
}
