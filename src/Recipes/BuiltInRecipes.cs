using System.IO;
using System.Reflection;

namespace Labs626.UrScore.Recipes;

/// <summary>One recipe that ships inside Ur Score: its name, what it reads, and its text.</summary>
public sealed record BuiltInRecipe(string Slug, string Name, string Text);

/// <summary>
/// The recipes Ur Score carries with it: the only reader text there is (spec "Readers"). They are the same files the
/// release publishes, embedded at build time, so the two can never drift; the game page shows the hosts each mode's readers
/// contact (<see cref="Games.ModeLines"/>), worked out from this text.
/// </summary>
public static class BuiltInRecipes
{
    private static readonly Lazy<IReadOnlyList<BuiltInRecipe>> Loaded = new(Read);

    private static readonly Lazy<IReadOnlyDictionary<string, Recipe>> Parsed = new(() =>
        All.Select(b => RecipeParser.Parse(b.Text).Recipe).OfType<Recipe>().ToDictionary(r => r.Slug, StringComparer.Ordinal));

    public static IReadOnlyList<BuiltInRecipe> All => Loaded.Value;

    /// <summary>The shipped recipes parsed, by slug: what the game manifest is checked against.</summary>
    public static IReadOnlyDictionary<string, Recipe> BySlug => Parsed.Value;

    public static BuiltInRecipe? Find(string slug) =>
        All.FirstOrDefault(r => string.Equals(r.Slug, slug, StringComparison.Ordinal));

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
