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

    /// <summary>Why some Show boxes start ticked on a first import (D11), or "" when the recipe suggests none.</summary>
    public static string SuggestedNote(Recipe recipe)
    {
        List<string> labels = [.. RecipeStats.Suggested(recipe).Select(v => v.Label)];
        if (labels.Count == 0) return "";

        var named = labels.Count == 1 ? labels[0] : $"{string.Join(", ", labels.Take(labels.Count - 1))} and {labels[^1]}";
        var start = labels.Count == 1 ? "so it starts ticked" : "so they start ticked";
        return $"The recipe suggests showing {named}, {start}. Untick any you don't want. Nothing is sent to RoRoRo unless you tick Send.";
    }
}
