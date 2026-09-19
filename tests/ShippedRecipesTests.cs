using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

/// <summary>
/// The recipes in <c>recipes/</c> are what the clan downloads from each release and imports by hand, so a recipe
/// that doesn't parse is a support call, not a test failure. Nothing guarded them until the top-clans recipe
/// shipped on 2026-09-19.
/// </summary>
public class ShippedRecipesTests
{
    private static IEnumerable<string> Files()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ur-Score.csproj"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Directory.EnumerateFiles(Path.Combine(directory.FullName, "recipes"), "*.recipe.json");
    }

    [Fact]
    public void EveryShippedRecipeParses()
    {
        var files = Files().ToList();
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var result = RecipeParser.Parse(File.ReadAllText(file));
            Assert.True(result.Recipe is not null, $"{Path.GetFileName(file)}: {string.Join("; ", result.Problems)}");
            Assert.Empty(result.Problems);
        }
    }

    /// <summary>
    /// The clans-list recipe is the one the field's numbers come from: it must read as a group list (so no account is
    /// ever matched to it) and carry the battle as its period (so its readings belong to one battle).
    /// </summary>
    [Fact]
    public void TheTopClansRecipeIsAGroupListWithTheBattleAsItsPeriod()
    {
        var file = Files().Single(f => Path.GetFileName(f) == "pet-sim-99-top-clans.recipe.json");
        var recipe = RecipeParser.Parse(File.ReadAllText(file)).Recipe;

        Assert.NotNull(recipe);
        Assert.True(recipe.IsGroupList);
        Assert.NotNull(recipe.Period);
        Assert.Equal("battle", recipe.Period.Value);
        // Points first: the field is ranked by the first value a clans list declares.
        Assert.Equal("points", recipe.LastStep.Values[0].Id);
        Assert.Equal(["points", "members", "capacity", "contributors"], recipe.LastStep.Values.Select(v => v.Id));
    }
}
