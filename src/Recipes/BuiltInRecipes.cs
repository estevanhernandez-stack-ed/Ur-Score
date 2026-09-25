using System.IO;
using System.Reflection;

namespace Labs626.UrScore.Recipes;

/// <summary>One recipe that ships inside Ur Score: its name, what it reads, and its text.</summary>
public sealed record BuiltInRecipe(string Slug, string Name, string Text);

/// <summary>
/// The recipes Ur Score carries with it, so a first run needs no download and no file picker.
/// <para>
/// They are the same files the release publishes, embedded at build time, so the two can never drift. Importing one
/// still goes through the review screen: the promise is that you see every host a recipe will contact before it is
/// added, and that promise is about the recipe, not about where its text came from.
/// </para>
/// </summary>
public static class BuiltInRecipes
{
    private static readonly Lazy<IReadOnlyList<BuiltInRecipe>> Loaded = new(Read);

    private static readonly Lazy<IReadOnlyDictionary<string, Recipe>> Parsed = new(() =>
        All.Select(b => RecipeParser.Parse(b.Text).Recipe).OfType<Recipe>().ToDictionary(r => r.Slug, StringComparer.Ordinal));

    public static IReadOnlyList<BuiltInRecipe> All => Loaded.Value;

    public static BuiltInRecipe? Find(string slug) =>
        All.FirstOrDefault(r => string.Equals(r.Slug, slug, StringComparison.Ordinal));

    /// <summary>
    /// Whether the copy Ur Score carries is a newer one this installed recipe could be updated to.
    /// <para>
    /// An installed recipe is a SNAPSHOT of the text that was imported, and there was no way to replace it with a
    /// newer one. Setup › Recipes' built-in list hides a built-in the moment it is installed — right, because offering it
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
        shipped is not null && CanUpdate(installed, shipped, RecipeParser.Parse(shipped.Text).Recipe);

    /// <summary>
    /// Whether Setup › Recipes offers an update that makes this clans list keep clan names: the installed copy does not say
    /// its groups are clans, and the copy Ur Score ships does. A copy imported before names became opt-in keeps only the
    /// field's numbers, and the race's rival lines stop without it (2026-09-24). Parsed once per run, since the board asks
    /// on every render while the note is up.
    /// </summary>
    public static bool HasGroupNamesUpdate(InstalledRecipe installed) =>
        installed.Recipe is { IsGroupList: true, GroupsAreClans: false }
        && Find(installed.Recipe.Slug) is { } shipped
        && Parsed.Value.GetValueOrDefault(shipped.Slug) is { GroupsAreClans: true } recipe
        && CanUpdate(installed, shipped, recipe);

    private static bool CanUpdate(InstalledRecipe installed, BuiltInRecipe shipped, Recipe? recipe) =>
        recipe is not null
        && !string.Equals(installed.Text, shipped.Text, StringComparison.Ordinal)
        && string.Equals(recipe.Name, installed.Recipe.Name, StringComparison.Ordinal)
        && string.Equals(recipe.Author, installed.Recipe.Author, StringComparison.Ordinal);

    private static IReadOnlyList<BuiltInRecipe> Read()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var recipes = new List<BuiltInRecipe>();

        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".recipe.json", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null) continue;

            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();

            // A recipe that ships broken is a bug in the build, not something to show a person: it is left out, and
            // ShippedRecipesTests fails on it long before a release.
            if (RecipeParser.Parse(text).Recipe is { } recipe) recipes.Add(new BuiltInRecipe(recipe.Slug, recipe.Name, text));
        }

        return recipes;
    }
}
