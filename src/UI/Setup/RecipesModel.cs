using Labs626.UrScore.Board;
using Labs626.UrScore.Book;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

using Source = Labs626.UrScore.Core.Source;

/// <summary>One recipe Ur Score carries with it, as the Recipes page offers it: not yet installed, one press away.</summary>
public sealed record BuiltInItem(string Slug, string Name, string Hosts)
{
    public string AddName => $"Add {Name}";
}

public sealed record RecipeItem(string Slug, string Name, string Hosts, string Every, string Sources, string? IconFile)
{
    public string RemoveName => $"Remove {Name}";

    /// <summary>Ur Score carries a newer copy of this recipe than the one installed (<see cref="RecipesModel.CanUpdate"/>).</summary>
    public bool CanUpdate { get; init; }

    public string UpdateName => $"Update {Name}";

    /// <summary>Said on the row rather than left to the button, so the reason a button appeared is on screen.</summary>
    public string UpdateNote => CanUpdate ? RecipesModel.NewerCopyShipped : "";

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

    public const string NewerCopyShipped = "This version of Ur Score carries a newer copy of this recipe.";

    /// <summary>
    /// Whether the copy Ur Score carries is a newer one this installed recipe could be updated to.
    /// <para>
    /// An installed recipe is a SNAPSHOT of the text that was imported, and there was no way to replace it with a
    /// newer one. <see cref="BuiltIn"/> hides a built-in the moment it is installed — right, because offering it
    /// again would read as a second copy — and the only other route was to find the recipe's JSON somewhere and
    /// import it by hand, which for a built-in means extracting an embedded resource out of the exe. So every
    /// user stayed on whatever they first imported, forever. The update MACHINERY was never missing:
    /// <c>ImportFlow.RunTextAsync</c> already compares, re-asks about changed hosts, and carries the user's
    /// choices across. Only a way to reach it was.
    /// </para>
    /// <para>
    /// The name and author have to match, and that is not belt-and-braces: a slug is derived from them
    /// (<see cref="Recipe.Slug"/>), so two different recipes CAN share one — "Pet Sim 99 clan battle points" and
    /// "Pet Sim 99 Clan Battle Points" slugify identically. <c>ImportFlow</c> refuses that case outright ("rename
    /// one of them before importing"), so without this clause the button would appear on somebody else's recipe
    /// and be guaranteed to fail. Asking the same question the flow asks is what keeps the two from drifting.
    /// </para>
    /// </summary>
    public static bool CanUpdate(InstalledRecipe installed, BuiltInRecipe? shipped) =>
        shipped is not null
        && !string.Equals(installed.Text, shipped.Text, StringComparison.Ordinal)
        && RecipeParser.Parse(shipped.Text).Recipe is { } recipe
        && string.Equals(recipe.Name, installed.Recipe.Name, StringComparison.Ordinal)
        && string.Equals(recipe.Author, installed.Recipe.Author, StringComparison.Ordinal);

    public static IReadOnlyList<RecipeItem> Items(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources, Func<string, string?> iconFile) =>
        [.. installed.Select(i => new RecipeItem(
            i.Recipe.Slug,
            i.Recipe.Name,
            string.Join(", ", RecipeHosts.ContactedBy(i.Recipe).Order(StringComparer.Ordinal)),
            $"Asks every {i.Recipe.EffectiveEverySeconds} s",
            SourcesText(i.Recipe, sources),
            iconFile(i.Recipe.Slug))
        {
            CanUpdate = CanUpdate(i, BuiltInRecipes.Find(i.Recipe.Slug)),
            HasIconSlot = i.Recipe.Icon is not null,
            IconName = IconChoice.RecipeSource(i.Recipe.Slug, sources, installed) is { } main
                ? PanelText.IconName(LiveBoard.NameOf(main, i.Recipe), i.Recipe)
                : "",
        })];

    /// <summary>
    /// The recipes Ur Score ships that are not installed yet, with the hosts each would contact. An installed one is
    /// a row of its own above; offering it again would read as a second copy.
    /// </summary>
    public static IReadOnlyList<BuiltInItem> BuiltIn(IReadOnlyList<InstalledRecipe> installed) =>
    [
        .. BuiltInRecipes.All
            .Where(b => !installed.Any(i => string.Equals(i.Recipe.Slug, b.Slug, StringComparison.Ordinal)))
            .Select(b => new BuiltInItem(
                b.Slug,
                b.Name,
                RecipeParser.Parse(b.Text).Recipe is { } recipe
                    ? string.Join(", ", RecipeHosts.ContactedBy(recipe).Order(StringComparer.Ordinal))
                    : "")),
    ];

    public static string SourcesText(Recipe recipe, IReadOnlyList<Source> sources)
    {
        // 0.3.7 kept the field's numbers; 0.3.10 also keeps the clans a chart draws, capped (GroupRows). Each wording
        // was true when written and stopped being true a day later, which is why this line says what is kept, not what isn't.
        if (recipe.IsGroupList) return $"Every row live; the top {GroupRows.Top} and yours kept";
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
