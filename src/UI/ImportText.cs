using Labs626.UrScore.Board;
using Labs626.UrScore.Book;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

/// <summary>
/// The import screen's words about what a recipe keeps in your score book.
/// <para>
/// It used to tell you a clans list kept nothing. That stopped being true in 0.3.10, when the field's numbers and
/// the top clans by name started being written, and it stayed on screen until 0.5.3 — a false sentence on the one
/// screen whose whole job is telling you what you are agreeing to. The owner's direction on 2026-09-20: we are
/// holding the information, they know we are scoring, so say what is kept and stop promising what is not.
/// </para>
/// </summary>
public static class ImportText
{
    public const string ShowEveryStat = "Show every game statistic (reads once from the hosts above)";

    /// <summary>What a recipe keeps beside the stats you tick, named the way the screen says them.</summary>
    public static IReadOnlyList<string> Kept(Recipe recipe)
    {
        var kept = recipe.IsGroupList
            ? [
                "Where yours stands in the list, and what the place above it holds",
                "How the whole field is doing: the leader, the top ten, the average and the bottom ten",
                $"The top {GroupRows.Top} by name, with the places either side of yours",
            ]
            : recipe.Headline.Select(headline => headline.Label).ToList();
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
        recipe.IsGroupList ? "Every read keeps these. A list like this holds no players, so no player is kept."
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
