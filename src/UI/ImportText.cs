using Labs626.UrScore.Board;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

/// <summary>The import screen's words about the score book (spec §7.3, §14: headline items are listed as kept).</summary>
public static class ImportText
{
    public const string ShowEveryStat = "Show every game statistic (reads once from the hosts above)";

    /// <summary>The headline items every successful read writes to the book. A group list writes nothing.</summary>
    public static IReadOnlyList<string> Kept(Recipe recipe) =>
        recipe.IsGroupList ? [] : [.. recipe.Headline.Select(h => h.Label)];

    public static string KeptNote(Recipe recipe) =>
        recipe.IsGroupList ? "Nothing from this recipe is kept. Its rows are groups, shown live only."
        : recipe.Headline.Count == 0 ? "Every read keeps the stats you tick, for your own accounts only."
        : "Every read keeps these headline items, and the stats you tick for your own accounts only.";

    /// <summary>A recipe with inputs has its values picked in Setup, where the search list helps.</summary>
    public static string InputsNote(Recipe recipe) =>
        recipe.IsGroupList || RecipeWords.MainInput(recipe) is not { } input
            ? ""
            : $"After you import it, pick {RecipeWords.Lower(input.Label)} in Setup › {RecipeWords.Groups(recipe)}.";
}
