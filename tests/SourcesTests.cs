using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class SourcesTests
{
    private static InstalledRecipe Installed(string fixture, RecipeState? state = null)
    {
        var text = RecipeParserTests.Fixture(fixture);
        return new InstalledRecipe(RecipeParser.Parse(text).Recipe!, text, state ?? new RecipeState());
    }

    private static Dictionary<string, string> Clan(string name) => new() { ["clan"] = name };

    /// <summary>
    /// "This clan is mine" is a positive claim, and a source whose recipe is not installed cannot support it: with no
    /// recipe there is no telling whether its inputs name a clan of yours or a field of everybody's. The two
    /// surviving copies of this filter disagreed on exactly this case — <c>AppServices</c> dropped such a source,
    /// <c>FieldMetricsModel</c> kept it — and a filter free to disagree with itself is what V3-S.31 exists to stop.
    /// </summary>
    [Fact]
    public void ASourceWhoseRecipeIsNotInstalledNamesNoClanOfYours()
    {
        var installed = Installed("petsim99-clan-battle.recipe.json");
        var slug = installed.Recipe.Slug;

        IReadOnlyList<Source> sources =
        [
            new("s-1", slug, Clan("K0i2"), SourceRole.Main),
            new("s-2", "some-recipe-that-was-uninstalled", Clan("Ghost"), SourceRole.Mine),
        ];

        Assert.Equal(["K0i2"], SourceRules.MyClanNames(sources, [installed]));
    }

    /// <summary>A clan you only WATCH is never one of yours, and neither is one whose source is switched off.</summary>
    [Fact]
    public void WatchedAndSwitchedOffClansAreNotYours()
    {
        var installed = Installed("petsim99-clan-battle.recipe.json");
        var slug = installed.Recipe.Slug;

        IReadOnlyList<Source> sources =
        [
            new("s-1", slug, Clan("K0i2"), SourceRole.Main),
            new("s-2", slug, Clan("H8ER"), SourceRole.Watch),
            new("s-3", slug, Clan("Dormant"), SourceRole.Mine, Enabled: false),
        ];

        Assert.Equal(["K0i2"], SourceRules.MyClanNames(sources, [installed]));
    }

    [Fact]
    public void AnInputKeyIgnoresOrderCaseAndSpaces()
    {
        var a = new Dictionary<string, string> { ["clan"] = " K0i2 ", ["region"] = "EU" };
        var b = new Dictionary<string, string> { ["region"] = "eu", ["clan"] = "k0i2" };

        Assert.Equal(Source.KeyOf(a), Source.KeyOf(b));
        Assert.NotEqual(Source.KeyOf(a), Source.KeyOf(Clan("CCGP")));
    }

    [Fact]
    public void MigrationTurnsTheOldClanIntoTheMainSourceAndGivesInputlessRecipesOne()
    {
        // Ruling R1: the one clan a 2a user had is their main.
        var installed = new[]
        {
            Installed("petsim99-clan-battle.recipe.json", new RecipeState(Inputs: Clan("CCGP"))),
            Installed("petsim99-profile.recipe.json"),
            Installed("petsim99-top-clans.recipe.json"),
        };

        var sources = SourceRules.Migrate(installed, []);

        Assert.Equal(3, sources.Count);
        var clan = sources.Single(s => s.Recipe == installed[0].Recipe.Slug);
        Assert.Equal((SourceRole.Main, "CCGP"), (clan.Role, clan.Inputs["clan"]));
        Assert.Equal(SourceRole.Mine, sources.Single(s => s.Recipe == installed[1].Recipe.Slug).Role);
        Assert.Equal(SourceRole.Watch, sources.Single(s => s.Recipe == installed[2].Recipe.Slug).Role);
        Assert.All(sources, s => Assert.Matches("^s-[0-9a-f]{8}$", s.Id));
    }

    [Fact]
    public void MigrationLeavesExistingSourcesAndSkipsARecipeWithNoInputsSet()
    {
        var clan = Installed("petsim99-clan-battle.recipe.json");
        var existing = new[] { new Source("s-00000001", "somewhere-else", Clan("X"), SourceRole.Watch) };

        var sources = SourceRules.Migrate([clan], existing);

        Assert.Equal(existing, sources);
    }

    [Fact]
    public void AddingTheSameClanTwiceKeepsOneSourceAndUpdatesItsRole()
    {
        var sources = SourceRules.Add([], "clan-recipe", Clan("NovaForge"), SourceRole.Watch);
        sources = SourceRules.Add(sources, "clan-recipe", Clan(" novaforge "), SourceRole.Mine);

        var source = Assert.Single(sources);
        Assert.Equal(SourceRole.Mine, source.Role);
    }

    [Theory]
    [InlineData(SourceRole.Mine)]
    [InlineData(SourceRole.Watch)]
    public void ThereIsOnlyOneMainPerRecipe(SourceRole previousRole)
    {
        var sources = SourceRules.Add([], "clan-recipe", Clan("CCGP"), SourceRole.Main);
        sources = SourceRules.Add(sources, "clan-recipe", Clan("K0i2"), previousRole);
        sources = SourceRules.Add(sources, "other-recipe", Clan("Elsewhere"), SourceRole.Main);
        var k0i2 = sources.Single(s => s.Inputs["clan"] == "K0i2");

        sources = SourceRules.MakeMain(sources, k0i2.Id);

        Assert.Equal(3, sources.Count);
        Assert.Equal(k0i2 with { Role = SourceRole.Main }, sources.Single(s => s.Id == k0i2.Id));
        Assert.Equal(previousRole, k0i2.Role);
        Assert.Equal(SourceRole.Mine, sources.Single(s => s.Inputs["clan"] == "CCGP").Role);
        Assert.Equal(SourceRole.Main, sources.Single(s => s.Inputs["clan"] == "K0i2").Role);
        Assert.Equal(SourceRole.Main, sources.Single(s => s.Recipe == "other-recipe").Role);
    }

    /// <summary>
    /// Adding a clan you had switched off switches it back on, takes the new role, and keeps its id. On purpose:
    /// adding is an affirmative act, and leaving it off would make the Add button look broken for that one clan.
    /// The id matters because the score book's lines are filed under it. This went undocumented and untested
    /// from the start (S1-3.2); the behaviour is unchanged, the intent is now written down and pinned.
    /// </summary>
    [Fact]
    public void ReAddingASwitchedOffClanSwitchesItBackOnAndKeepsItsId()
    {
        var sources = SourceRules.Add([], "clan-recipe", Clan("CCGP"), SourceRole.Mine);
        var id = sources[0].Id;
        sources = [.. sources.Select(s => s with { Enabled = false })];
        Assert.False(sources[0].Enabled);

        var again = SourceRules.Add(sources, "clan-recipe", Clan("CCGP"), SourceRole.Watch);

        var one = Assert.Single(again);
        Assert.Equal(id, one.Id);
        Assert.True(one.Enabled);
        Assert.Equal(SourceRole.Watch, one.Role);
    }

    [Fact]
    public void RemovingASourceOrARecipeTakesOnlyThose()
    {
        var sources = SourceRules.Add([], "clan-recipe", Clan("CCGP"), SourceRole.Main);
        sources = SourceRules.Add(sources, "clan-recipe", Clan("K0i2"), SourceRole.Mine);
        sources = SourceRules.Add(sources, "profile", new Dictionary<string, string>(), SourceRole.Mine);

        Assert.Equal(2, SourceRules.Remove(sources, sources[0].Id).Count);
        Assert.Equal("profile", Assert.Single(SourceRules.ForgetRecipe(sources, "clan-recipe")).Recipe);
    }

    [Fact]
    public void TheStoreTellsAMissingFileFromAnUnreadableOne()
    {
        using var dir = TempDir.Create("urscore-sources");
        var path = Path.Combine(dir.Path, "sources.json");
        var store = new SourceStore(path);

        static (int Count, bool Exists, bool Readable) Shape(SourceLoad load) => (load.Sources.Count, load.Exists, load.Readable);

        Assert.Equal((0, false, true), Shape(store.LoadResult()));

        store.Save(SourceRules.Add([], "clan-recipe", Clan("CCGP"), SourceRole.Main));
        Assert.Equal((1, true, true), Shape(store.LoadResult()));

        File.WriteAllText(path, "{ not json");
        Assert.Equal((0, true, false), Shape(store.LoadResult()));
    }

    /// <summary>
    /// A file this account is DENIED is there-but-unreadable, never missing. Missing is the one answer that does
    /// damage, because start reads it as "migrate and save" and would write over the user's clans (S1-14.15).
    /// <para>
    /// The row feared <c>File.Exists</c> answers false for a denied file. It does not, on this platform —
    /// checked here with a real deny rule, first Read and then FullControl, and it answered true both times, so
    /// the row's scenario could not happen and the old code already held. The store now learns existence from the
    /// open itself regardless, and this pins the answer that matters whichever way any platform's
    /// <c>File.Exists</c> goes. The deny is put back in a finally so a failing assertion cannot leave a temp file
    /// nobody can delete. Windows-only, like the rest of this project.
    /// </para>
    /// </summary>
    [Fact]
    public void AFileYouAreDeniedIsThereButUnreadableNotMissing()
    {
        using var dir = TempDir.Create("urscore-sources");
        var path = Path.Combine(dir.Path, "sources.json");
        var store = new SourceStore(path);
        store.Save(SourceRules.Add([], "clan-recipe", Clan("CCGP"), SourceRole.Main));

        var me = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        // FullControl, not Read: a plain Read deny still lets File.Exists answer true (it queries attributes, which
        // Read does not cover), so the old code already handled that one. The deny that fooled it is one that
        // covers attributes too — which is what a file left owned by another account actually looks like.
        var deny = new System.Security.AccessControl.FileSystemAccessRule(
            me, System.Security.AccessControl.FileSystemRights.FullControl, System.Security.AccessControl.AccessControlType.Deny);
        var info = new FileInfo(path);
        var security = info.GetAccessControl();
        security.AddAccessRule(deny);
        info.SetAccessControl(security);
        try
        {
            var load = store.LoadResult();

            Assert.True(load.Exists, "a denied file was read as missing, which is how the user's clans got saved over");
            Assert.False(load.Readable);
            Assert.Empty(load.Sources);
        }
        finally
        {
            security.RemoveAccessRule(deny);
            info.SetAccessControl(security);
        }
    }

    [Fact]
    public void OnlyANewlyInstalledRecipeWithNoInputsGetsASource()
    {
        // A 2a clan's saved inputs are never turned into a source again after the first start.
        var clan = Installed("petsim99-clan-battle.recipe.json", new RecipeState(Inputs: Clan("CCGP")));
        var profile = Installed("petsim99-profile.recipe.json");
        var top = Installed("petsim99-top-clans.recipe.json");

        Assert.Empty(SourceRules.ForNewRecipes([], [], [clan]));

        var added = SourceRules.ForNewRecipes([], [clan], [clan, profile, top]);

        Assert.Equal(
            new[] { (profile.Recipe.Slug, SourceRole.Mine), (top.Recipe.Slug, SourceRole.Watch) },
            added.Select(s => (s.Recipe, s.Role)).ToArray());
        Assert.All(added, s => Assert.Empty(s.Inputs));
    }

    [Fact]
    public void ARecipeAlreadyInstalledOrAlreadyReadGetsNoSourceAndTheListIsUnchanged()
    {
        var profile = Installed("petsim99-profile.recipe.json");

        // Its source was removed on purpose: an update or a reload must not bring it back.
        IReadOnlyList<Source> none = [];
        Assert.Same(none, SourceRules.ForNewRecipes(none, [profile], [profile]));

        // Newly loaded, but a source kept from before (say the file failed to parse once) already reads it.
        IReadOnlyList<Source> kept = [new Source("s-00000001", profile.Recipe.Slug, new Dictionary<string, string>(), SourceRole.Mine)];
        Assert.Same(kept, SourceRules.ForNewRecipes(kept, [], [profile]));
    }

    [Fact]
    public void AtStartEveryInstalledRecipeWithNoInputsAndNoSourceGetsOne()
    {
        // At start nothing counts as installed before, so a recipe placed in the folder while Ur Score was closed
        // is read too. A recipe with inputs still gets nothing, and one that already has a source keeps just that.
        var clan = Installed("petsim99-clan-battle.recipe.json", new RecipeState(Inputs: Clan("CCGP")));
        var profile = Installed("petsim99-profile.recipe.json");
        var top = Installed("petsim99-top-clans.recipe.json");
        IReadOnlyList<Source> loaded = [new Source("s-00000001", profile.Recipe.Slug, new Dictionary<string, string>(), SourceRole.Mine)];

        var sources = SourceRules.ForNewRecipes(loaded, [], [clan, profile, top]);

        Assert.Equal(
            new[] { ("s-00000001", profile.Recipe.Slug, SourceRole.Mine), (sources[1].Id, top.Recipe.Slug, SourceRole.Watch) },
            sources.Select(s => (s.Id, s.Recipe, s.Role)).ToArray());
        Assert.Same(loaded, SourceRules.ForNewRecipes(loaded, [], [clan, profile]));
    }

    [Fact]
    public void TheStoreRoundTripsAndABrokenFileLoadsAsNoSources()
    {
        var dir = Path.Combine(Path.GetTempPath(), "urscore-sources-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "sources.json");
        try
        {
            var store = new SourceStore(path);
            Assert.Empty(store.Load());

            var sources = SourceRules.Add([], "clan-recipe", Clan("CCGP"), SourceRole.Main);
            store.Save(sources);

            var loaded = Assert.Single(store.Load());
            Assert.Equal((sources[0].Id, "clan-recipe", SourceRole.Main, "CCGP", true),
                (loaded.Id, loaded.Recipe, loaded.Role, loaded.Inputs["clan"], loaded.Enabled));
            Assert.Contains("\"role\": \"main\"", File.ReadAllText(path));

            File.WriteAllText(path, "{ not json");
            Assert.Empty(store.Load());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
