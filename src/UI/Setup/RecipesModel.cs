using Labs626.UrScore.Board;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

using Source = Labs626.UrScore.Core.Source;

public sealed record RecipeItem(string Slug, string Name, string Hosts, string Every, string Sources, string? IconFile)
{
    public string RemoveName => $"Remove {Name}";

    public bool HasIcon => IconFile is not null;

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
            iconFile(i.Recipe.Slug)))];

    public static string SourcesText(Recipe recipe, IReadOnlyList<Source> sources)
    {
        if (recipe.IsGroupList) return "Shown live, never kept";
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
}
