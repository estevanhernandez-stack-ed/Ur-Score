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

    public static IReadOnlyList<BuiltInRecipe> All => Loaded.Value;

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
