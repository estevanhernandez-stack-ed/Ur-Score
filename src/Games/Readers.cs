using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Games;

/// <summary>What the app reads with, and what it keeps but no longer reads.</summary>
/// <param name="Installed">Every reader a mode names, in manifest order: the embedded text with the saved or seeded state.</param>
/// <param name="Orphans">Data-folder recipes no mode names (a hand-imported custom recipe). Listed for Diagnostics; never read, written or deleted.</param>
/// <param name="Problems">Readers a mode names that could not be built, each naming the slug. A test makes this empty for the shipped app.</param>
public sealed record ReadersLoad(IReadOnlyList<InstalledRecipe> Installed, IReadOnlyList<InstalledRecipe> Orphans, IReadOnlyList<string> Problems);

/// <summary>
/// The readers come from the app, not the data folder (spec "Readers.Compose"). Before modes, a recipe ran on the
/// text that was imported, a snapshot nothing ever replaced, so a fix to how Pet Sim 99 is read reached only the people
/// who found and re-imported it. Now the text is always the copy built into this version and only the user's choices
/// (<c>{slug}.state.json</c>) come from disk. A data-folder <c>.recipe.json</c> is never read for its text again.
/// </summary>
public static class Readers
{
    /// <summary>
    /// Builds every reader the catalog names. A saved state is loaded through <see cref="RecipeStore.TryLoadState"/>
    /// (legacy <c>metricIdOverride</c> included, A8) and resolved by <see cref="RecipeStates.Effective"/> with the mode's
    /// <c>shows</c> for that slug (A1). Seeding writes nothing: a seeded state reaches disk only when the user changes a
    /// choice, so an upgrade's first start leaves the recipes folder exactly as it was (spec decision 4, as narrowed by A5).
    /// </summary>
    public static ReadersLoad Compose(GameCatalog catalog, IReadOnlyDictionary<string, BuiltInRecipe> builtIns, RecipeStore store)
    {
        var installed = new List<InstalledRecipe>();
        var problems = new List<string>();
        var named = new HashSet<string>(StringComparer.Ordinal);

        foreach (var mode in catalog.Modes)
        {
            foreach (var slug in mode.Reads)
            {
                if (!named.Add(slug)) continue;

                if (!builtIns.TryGetValue(slug, out var builtIn))
                {
                    problems.Add($"{slug}: the mode '{mode.Key}' reads it, but it is not built into this version.");
                    continue;
                }

                var parsed = RecipeParser.Parse(builtIn.Text);
                if (parsed.Recipe is not { } recipe)
                {
                    problems.Add($"{slug}: {string.Join(" ", parsed.Problems)}");
                    continue;
                }

                var state = RecipeStates.Effective(recipe, store.TryLoadState(recipe), ShowsFor(mode, slug));
                installed.Add(new InstalledRecipe(recipe, builtIn.Text, state));
            }
        }

        // Only to list them. Their text is parsed because that is how a slug is known, and their unreadable files are not
        // problems any more: nothing reads them either way.
        var orphans = store.LoadAll().Recipes.Where(r => !named.Contains(r.Recipe.Slug)).ToList();

        return new ReadersLoad(installed, orphans, problems);
    }

    /// <summary>The shipped recipes by slug, text included: what <see cref="Compose"/> builds from in the app.</summary>
    public static IReadOnlyDictionary<string, BuiltInRecipe> BuiltIn =>
        BuiltInRecipes.All.ToDictionary(b => b.Slug, StringComparer.Ordinal);

    /// <summary>
    /// The catalog with every mode <see cref="GameCatalog.Validate"/> names dropped, and the problems that dropped them.
    /// Run at startup so one bad mode costs that mode and a trail line, never the start (spec "GameCatalog"). A problem
    /// names a mode as <c>'game/mode'</c>; one that names no mode (two games with one id) drops nothing and is still said.
    /// </summary>
    public static (GameCatalog Catalog, IReadOnlyList<string> Problems) Sound(GameCatalog catalog, IReadOnlyDictionary<string, Recipe> builtIns)
    {
        var problems = GameCatalog.Validate(catalog, builtIns);
        if (problems.Count == 0) return (catalog, problems);

        bool Named(ModeDef mode) => problems.Any(p => p.Contains($"'{mode.Key}'", StringComparison.Ordinal));
        var games = catalog.Games
            .Select(g => g with { Modes = [.. g.Modes.Where(m => !Named(m))] })
            .Where(g => g.Modes.Count > 0)
            .ToList();

        return (new GameCatalog(games), problems);
    }

    private static IReadOnlyList<string> ShowsFor(ModeDef mode, string slug)
    {
        var prefix = slug + ":";
        return [.. mode.Shows.Where(s => s.StartsWith(prefix, StringComparison.Ordinal)).Select(s => s[prefix.Length..])];
    }
}
