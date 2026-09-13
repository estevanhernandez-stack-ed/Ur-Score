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

    [Fact]
    public void TheMetricIdIsTheRecipesUnlessTheUserChangedIt()
    {
        Assert.Equal("clan.battle.points", new RecipeState().MetricIdFor(PetSim));
        Assert.Equal("my.points", new RecipeState(MetricIdOverride: " my.points ").MetricIdFor(PetSim));
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
