using Labs626.UrScore.Board;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

using Source = Labs626.UrScore.Core.Source;

public sealed record RecipeItem(string Slug, string Name, string Hosts, string Every, string Sources, string? IconFile)
{
    public string RemoveName => $"Remove {Name}";

    public bool HasIcon => IconFile is not null;

    /// <summary>The recipe names an icon, so its row keeps the picture's space while the main clan's picture isn't there (A26).</summary>
    public bool HasIconSlot { get; init; }

    /// <summary>Whose picture the row draws: "CCGP clan icon". Empty while the recipe has no main clan to draw.</summary>
    public string IconName { get; init; } = "";

    public string Details => Sources.Length == 0 ? Every : $"{Every} · {Sources}";
}

/// <summary>Setup › Recipes (spec §7.4).</summary>
public static class RecipesModel
{
    public const string KeepsBook = "Removing a recipe never deletes its score book.";

    public static IReadOnlyList<RecipeItem> Items(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources, Func<string, string?> iconFile) =>
        [.. installed.Select(i => new RecipeItem(
            i.Recipe.Slug,
            i.Recipe.Name,
            string.Join(", ", RecipeHosts.ContactedBy(i.Recipe).Order(StringComparer.Ordinal)),
            $"Asks every {i.Recipe.EffectiveEverySeconds} s",
            SourcesText(i.Recipe, sources),
            iconFile(i.Recipe.Slug))
        {
            HasIconSlot = i.Recipe.Icon is not null,
            IconName = IconChoice.RecipeSource(i.Recipe.Slug, sources, installed) is { } main
                ? PanelText.IconName(LiveBoard.NameOf(main, i.Recipe), i.Recipe)
                : "",
        })];

    public static string SourcesText(Recipe recipe, IReadOnlyList<Source> sources)
    {
        // Since 0.3.7 a list keeps the field's own numbers, and no name: saying "never kept" was true and stopped being so.
        if (recipe.IsGroupList) return "Every row live; the field's numbers kept, no name";
        if (recipe.Inputs.Count == 0) return "";

        var count = sources.Count(s => string.Equals(s.Recipe, recipe.Slug, StringComparison.Ordinal));
        return count switch
        {
            0 => $"No {RecipeWords.GroupsLower(recipe)} yet",
            1 => $"1 {RecipeWords.Group(recipe)}",
            _ => $"{count} {RecipeWords.GroupsLower(recipe)}",
        };
    }

    public static string ConfirmRemove(Recipe recipe) =>
        $"Remove {recipe.Name}? It stops being read. Its score book stays on this PC, so importing it again carries on where it left off.";

    /// <summary>What Remove asks before it removes, for the themed confirmation to draw.</summary>
    public static Confirm RemoveQuestion(Recipe recipe) =>
        new("Remove recipe", ConfirmRemove(recipe), "Remove", $"Remove {recipe.Name}");
}
