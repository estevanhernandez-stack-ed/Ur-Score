using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class RecipeStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "urscore-recipes-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static string PetSimText => RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json");

    private static Recipe PetSim => RecipeParser.Parse(PetSimText).Recipe!;

    [Fact]
    public void ASavedRecipeComesBackWithItsTextAndState()
    {
        var store = new RecipeStore(_dir);
        store.Save(PetSim, PetSimText, new RecipeState(new Dictionary<string, string> { ["clan"] = "Noodle Clan" }));

        var installed = Assert.Single(new RecipeStore(_dir).LoadAll().Recipes);

        Assert.Equal(PetSim.Slug, installed.Recipe.Slug);
        Assert.Equal(PetSimText, installed.Text);
        Assert.Equal("Noodle Clan", installed.State.InputValues["clan"]);
    }

    [Fact]
    public void TheRecipeFileIsTheExactTextImported()
    {
        new RecipeStore(_dir).Save(PetSim, PetSimText, new RecipeState());
        Assert.Equal(PetSimText, File.ReadAllText(Path.Combine(_dir, $"{PetSim.Slug}.recipe.json")));
    }

    [Fact]
    public void ChoicesLiveInTheStateFileNeverTheRecipe()
    {
        var store = new RecipeStore(_dir);
        store.Save(PetSim, PetSimText, new RecipeState(new Dictionary<string, string> { ["clan"] = "Noodle Clan" }));

        Assert.DoesNotContain("Noodle Clan", File.ReadAllText(Path.Combine(_dir, $"{PetSim.Slug}.recipe.json")));
        Assert.Contains("Noodle Clan", File.ReadAllText(Path.Combine(_dir, $"{PetSim.Slug}.state.json")));
    }

    [Fact]
    public void SaveStateLeavesTheRecipeTextAlone()
    {
        var store = new RecipeStore(_dir);
        store.Save(PetSim, PetSimText, new RecipeState());
        store.SaveState(PetSim, new RecipeState(ExcludedAccountIds: ["9ad5e605-6b41-478c-add3-b916a31a5ab2"]));

        var installed = store.Find(PetSim.Slug)!;
        Assert.Equal(PetSimText, installed.Text);
        Assert.Contains(Guid.Parse("9ad5e605-6b41-478c-add3-b916a31a5ab2"), installed.State.Excluded);
    }

    [Fact]
    public void AnInvalidRecipeFileIsNamedAndSkipped()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "broken.recipe.json"), "{ \"recipe\": 1 }");
        new RecipeStore(_dir).Save(PetSim, PetSimText, new RecipeState());

        var load = new RecipeStore(_dir).LoadAll();

        Assert.Single(load.Recipes);
        Assert.StartsWith("broken.recipe.json: ", Assert.Single(load.Problems));
    }

    [Fact]
    public void AMissingOrUnreadableStateFileMeansDefaultState()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, $"{PetSim.Slug}.recipe.json"), PetSimText);
        File.WriteAllText(Path.Combine(_dir, $"{PetSim.Slug}.state.json"), "not json");

        var installed = Assert.Single(new RecipeStore(_dir).LoadAll().Recipes);
        Assert.Empty(installed.State.InputValues);
        Assert.Empty(installed.State.Excluded);
    }

    [Fact]
    public void RubbishExclusionsAreIgnored() =>
        Assert.Equal(new[] { Guid.Parse("9ad5e605-6b41-478c-add3-b916a31a5ab2") },
            new RecipeState(ExcludedAccountIds: ["nope", "9ad5e605-6b41-478c-add3-b916a31a5ab2"]).Excluded.ToArray());

    private static Recipe Profile => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

    private static string ProfileText => RecipeParserTests.Fixture("petsim99-profile.recipe.json");

    [Fact]
    public void StatChoicesAreSavedUnderTheirKeysWithShowSendAndMetricId()
    {
        var store = new RecipeStore(_dir);
        var state = new RecipeState(Stats: new Dictionary<string, StatChoice>
        {
            ["diamonds"] = new(Show: true, Send: true, MetricId: "ps99.diamonds"),
            ["counter:Huge Pets Opened"] = new(Show: true, Send: false, MetricId: "ps99.stat.huge-pets-opened"),
        }, CounterNames: ["Huge Pets Opened", "Eggs Opened"]);

        store.Save(Profile, ProfileText, state);

        var text = File.ReadAllText(Path.Combine(_dir, $"{Profile.Slug}.state.json"));
        Assert.Contains("\"counter:Huge Pets Opened\": {", text);
        Assert.Contains("\"show\": true", text);
        Assert.Contains("\"send\": false", text);
        Assert.Contains("\"metricId\": \"ps99.diamonds\"", text);

        var loaded = store.Find(Profile.Slug)!.State;
        Assert.Equal(state.StatChoices["counter:Huge Pets Opened"], loaded.StatChoices["counter:Huge Pets Opened"]);
        Assert.Equal(new[] { "Huge Pets Opened", "Eggs Opened" }, loaded.SavedCounterNames.ToArray());
    }

    [Fact]
    public void ALegacyMetricIdIsKeptForUpdateReviewWithoutEnablingReportsOnLoad()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, $"{PetSim.Slug}.recipe.json"), PetSimText);
        File.WriteAllText(Path.Combine(_dir, $"{PetSim.Slug}.state.json"),
            """{ "inputs": { "clan": "Noodle Clan" }, "metricIdOverride": "my.points" }""");

        var state = Assert.Single(new RecipeStore(_dir).LoadAll().Recipes).State;

        Assert.Equal("Noodle Clan", state.InputValues["clan"]);
        Assert.Empty(state.StatChoices);
        Assert.Empty(state.SentStats(PetSim));
        Assert.Equal(new StatChoice(Show: true, Send: true, MetricId: "my.points"),
            Assert.Single(state.LegacyStatChoices!).Value);
        Assert.Equal("value", Assert.Single(state.LegacyStatChoices!).Key);
    }

    [Fact]
    public void TrackedShownAndSentFollowTheTicksInRecipeOrder()
    {
        var state = new RecipeState(Stats: new Dictionary<string, StatChoice>
        {
            ["counter:Huge Pets Opened"] = new(Show: true, MetricId: "ps99.stat.huge-pets-opened"),
            ["rank"] = new(Show: true, Send: true, MetricId: "ps99.rank"),
            ["eggs"] = new(MetricId: "ps99.eggs-hatched"),
            ["diamonds"] = new(Send: true, MetricId: "ps99.diamonds"),
        });

        Assert.Equal(new[] { "counter:Huge Pets Opened", "diamonds", "rank" }, state.TrackedStats(Profile).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "rank", "counter:Huge Pets Opened" }, state.ShownStats(Profile).Select(s => s.Key).ToArray());
        Assert.Equal(new[] { new SentStat("diamonds", "Diamonds", "ps99.diamonds"), new SentStat("rank", "Player rank", "ps99.rank") },
            state.SentStats(Profile).ToArray());
    }

    [Fact]
    public void AStatTheRecipeNoLongerOffersIsNeitherReadNorSent()
    {
        var state = new RecipeState(Stats: new Dictionary<string, StatChoice>
        {
            ["prestige"] = new(Show: true, Send: true, MetricId: "ps99.prestige"),
            ["diamonds"] = new(Send: true, MetricId: ""),
        });

        Assert.Empty(state.TrackedStats(PetSim));
        Assert.Empty(state.SentStats(Profile));
    }

    [Fact]
    public void SavingDropsTicksForAStatTheRecipeNoLongerOffers()
    {
        // Stats design §7.2 and §5.2: a tick kept for an orphaned stat would come back unasked when a
        // later update offers the stat again, under a metric id another recipe may have claimed since.
        var store = new RecipeStore(_dir);
        store.Save(PetSim, PetSimText, new RecipeState(Stats: new Dictionary<string, StatChoice>
        {
            ["value"] = new(Show: true, Send: true, MetricId: "clan.battle.points"),
            ["rank"] = new(Show: true, Send: true, MetricId: "ps99.rank"),
        }));

        var saved = store.Find(PetSim.Slug)!.State.StatChoices;

        Assert.Equal(new StatChoice(Show: false, Send: false, MetricId: "ps99.rank"), saved["rank"]);
        Assert.Equal(new StatChoice(Show: true, Send: true, MetricId: "clan.battle.points"), saved["value"]);
    }

    [Fact]
    public void ARecipeUpdateNeverMovesAPinnedMetricId()
    {
        var state = new RecipeState(Stats: new Dictionary<string, StatChoice> { ["value"] = new(Send: true, MetricId: "clan.battle.points") });
        var updated = PetSim with
        {
            Steps = [PetSim.Steps[0], PetSim.LastStep with { Values = [PetSim.LastStep.Values[0] with { MetricId = "clan.points" }] }],
        };

        Assert.Equal("clan.battle.points", Assert.Single(state.SentStats(updated)).MetricId);
    }

    [Fact]
    public void RemoveDeletesBothFiles()
    {
        var store = new RecipeStore(_dir);
        store.Save(PetSim, PetSimText, new RecipeState());

        Assert.True(store.Remove(PetSim.Slug));
        Assert.Empty(store.LoadAll().Recipes);
        Assert.False(File.Exists(Path.Combine(_dir, $"{PetSim.Slug}.state.json")));
    }

    [Fact]
    public void NoDirectoryMeansNoRecipes() => Assert.Empty(new RecipeStore(_dir).LoadAll().Recipes);
}
