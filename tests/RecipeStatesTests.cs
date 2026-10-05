using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

/// <summary>Readers.Compose's state rules (games-and-modes A1, A8), pure.</summary>
public class RecipeStatesTests
{
    private static Recipe Shipped(string slug) => RecipeParser.Parse(BuiltInRecipes.Find(slug)!.Text).Recipe!;

    private static Recipe ProfileRecipe() => Shipped("pet-sim-99-profile");

    [Fact]
    public void SuggestedChoicesIsWhatTheStatsTableSuggests()
    {
        var recipe = ProfileRecipe();
        Assert.Equal(RecipeStats.SuggestedChoices(recipe), Labs626.UrScore.UI.StatsTableModel.Suggested(recipe));
        Assert.All(RecipeStats.SuggestedChoices(recipe).Values, c => { Assert.True(c.Show); Assert.False(c.Send); });
    }

    [Fact]
    public void ASavedStateWithStatsIsUsedAsIs()
    {
        var saved = new RecipeState(Stats: new Dictionary<string, StatChoice> { ["x"] = new(false, true, "a.b") });
        Assert.Same(saved, RecipeStates.Effective(ProfileRecipe(), saved, ["whatever"]));
    }

    [Fact]
    public void ASavedStateWithNoStatsAndNoLegacyIsUnseededAndKeepsTheRest()
    {
        var saved = new RecipeState(
            Inputs: new Dictionary<string, string> { ["clan"] = "C" }, ExcludedAccountIds: ["g"], CounterNames: ["n"], SentFieldMetrics: ["f"]);
        var recipe = ProfileRecipe();
        var effective = RecipeStates.Effective(recipe, saved, ["anything"]);

        Assert.Equal(RecipeStats.SuggestedChoices(recipe), effective.StatChoices);
        Assert.NotNull(effective.Stats);
        Assert.Equal("C", effective.InputValues["clan"]);
        Assert.Equal(["g"], effective.ExcludedAccountIds);
        Assert.Equal(["n"], effective.CounterNames);
        Assert.Equal(["f"], effective.SentFieldMetrics);
    }

    [Fact]
    public void AClanStateWithNullStatsAndNoLegacyTakesTheModesShows()
    {
        var recipe = Shipped("pet-sim-99-clan-battle-points");
        var saved = new RecipeState(Inputs: new Dictionary<string, string> { ["clan"] = "X" });
        var effective = RecipeStates.Effective(recipe, saved, ["value"]);

        var choice = Assert.Single(effective.StatChoices);
        Assert.True(choice.Value.Show);
        Assert.False(choice.Value.Send);
        Assert.Equal("X", effective.InputValues["clan"]);
    }

    [Fact]
    public void ALegacyStateTakesItsLegacyChoices()
    {
        var recipe = Shipped("pet-sim-99-clan-battle-points");
        var key = Assert.Single(recipe.LastStep.Values).Id;
        var saved = new RecipeState(Inputs: new Dictionary<string, string> { ["clan"] = "C" })
        {
            LegacyStatChoices = new Dictionary<string, StatChoice> { [key] = new(true, true, "clan.battle.points") },
        };
        var effective = RecipeStates.Effective(recipe, saved);

        Assert.Equal(new StatChoice(true, true, "clan.battle.points"), effective.StatChoices[key]);
        Assert.Equal("C", effective.InputValues["clan"]);
    }

    [Fact]
    public void TheFixturesLegacyClanBattleStateYieldsShowAndSend()
    {
        using var dir = TempDir.Create("urscore-states");
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "old-install-0.6.3", "recipes");
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(dir.Path, Path.GetFileName(file)));

        var recipe = Shipped("pet-sim-99-clan-battle-points");
        var saved = new RecipeStore(dir.Path).LoadState(recipe);
        var effective = RecipeStates.Effective(recipe, saved, ["value"]);

        var choice = Assert.Single(effective.StatChoices.Values);
        Assert.True(choice.Show);
        Assert.True(choice.Send);
        Assert.Equal("clan.battle.points", choice.MetricId);
    }

    [Fact]
    public void NoSavedStateSeedsTheSuggestedTicksAndNeverSend()
    {
        var recipe = ProfileRecipe();
        var effective = RecipeStates.Effective(recipe, null);

        Assert.Equal(RecipeStats.SuggestedChoices(recipe), effective.StatChoices);
        Assert.DoesNotContain(effective.StatChoices.Values, c => c.Send);
    }

    [Fact]
    public void ExtraShowsTickTheClanBattleRowValueSoAStarterBoardCanBuild()
    {
        var recipe = Shipped("pet-sim-99-clan-battle-points");
        Assert.Empty(RecipeStats.SuggestedChoices(recipe));

        var key = Assert.Single(RecipeStats.Offered(recipe, [])).Key;
        Assert.Equal("value", key);
        var effective = RecipeStates.Effective(recipe, null, [key, "no-such-stat"]);

        var choice = Assert.Single(effective.StatChoices);
        Assert.Equal(key, choice.Key);
        Assert.True(choice.Value.Show);
        Assert.False(choice.Value.Send);
        Assert.Equal("clan.battle.points", choice.Value.MetricId);
        Assert.Contains(key, effective.TrackedStats(recipe));
    }

    [Fact]
    public void ExtraShowsNeverTouchASavedState()
    {
        var recipe = Shipped("pet-sim-99-clan-battle-points");
        var key = recipe.LastStep.Values[0].Id;
        var saved = new RecipeState(Stats: new Dictionary<string, StatChoice>());
        Assert.Empty(RecipeStates.Effective(recipe, saved, [key]).StatChoices);
    }
}
