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

    [Fact]
    public void ThereIsOnlyOneMainPerRecipe()
    {
        var sources = SourceRules.Add([], "clan-recipe", Clan("CCGP"), SourceRole.Main);
        sources = SourceRules.Add(sources, "clan-recipe", Clan("K0i2"), SourceRole.Mine);
        sources = SourceRules.Add(sources, "other-recipe", Clan("Elsewhere"), SourceRole.Main);
        var k0i2 = sources.Single(s => s.Inputs["clan"] == "K0i2");

        sources = SourceRules.MakeMain(sources, k0i2.Id);

        Assert.Equal(SourceRole.Mine, sources.Single(s => s.Inputs["clan"] == "CCGP").Role);
        Assert.Equal(SourceRole.Main, sources.Single(s => s.Inputs["clan"] == "K0i2").Role);
        Assert.Equal(SourceRole.Main, sources.Single(s => s.Recipe == "other-recipe").Role);
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
