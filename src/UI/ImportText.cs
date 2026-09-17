using Labs626.UrScore.Board;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

/// <summary>The import screen's words about the headline, period and freshness details kept in the score book.</summary>
public static class ImportText
{
    public const string ShowEveryStat = "Show every game statistic (reads once from the hosts above)";

    /// <summary>The details a recipe can keep beside ticked stats. A group list writes nothing.</summary>
    public static IReadOnlyList<string> Kept(Recipe recipe)
    {
        if (recipe.IsGroupList) return [];

        var kept = recipe.Headline.Select(headline => headline.Label).ToList();
        if (recipe.Period is { } period)
        {
            var word = RecipeWords.Period(recipe);
            kept.Add($"Which {word} each read belongs to");
            if (period.Starts is not null && period.Ends is not null)
                kept.Add($"When the {word} starts and ends");
            else if (period.Starts is not null)
                kept.Add($"When the {word} starts");
            else if (period.Ends is not null)
                kept.Add($"When the {word} ends");
        }

        if (recipe.LastStep.AsOf is { } asOf)
        {
            kept.Add(asOf.Stale is null
                ? "When the source last updated the numbers"
                : "When the source last updated the numbers, and whether it calls them stale");
        }

        return kept;
    }

    public static string KeptNote(Recipe recipe) =>
        recipe.IsGroupList ? "Nothing from this recipe is kept. Its rows are groups, shown live only."
        : Kept(recipe).Count == 0 ? "Every read keeps the stats you tick, for your own accounts only."
        : "Every read keeps these details, and the stats you tick for your own accounts only.";

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
