using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Games;

/// <summary>
/// The disclosure lines on a game's page, worked out from the recipes themselves and never typed (A4): the hosts a mode
/// contacts and how often, and what it sends about your accounts. Built from <see cref="ReaderDisclosure"/>, so the page
/// and the import screen can never say different things.
/// </summary>
public static class ModeLines
{
    private sealed class NoKeys : IKeyStore
    {
        public SavedKey? Find(string keyId) => null;

        public void Save(string keyId, string host, string value) { }

        public bool Remove(string keyId) => false;

        public IReadOnlyCollection<string> Values() => [];
    }

    private static readonly IKeyStore None = new NoKeys();

    /// <summary>
    /// "Reads a.example, b.example every 3 min": distinct hosts in disclosure order (a recipe's steps, its clans list, then
    /// its icon hosts, recipe by recipe), and the shortest interval of the mode's recipes. The recipe's own interval already
    /// carries the runner's 60 s floor. Null when none of the mode's recipes is installed.
    /// </summary>
    public static string? Reads(ModeDef mode, IReadOnlyList<InstalledRecipe> installed)
    {
        var recipes = Of(mode, installed);
        if (recipes.Count == 0) return null;

        var hosts = recipes
            .SelectMany(r => ReaderDisclosure.Review(r, None).Hosts.Select(h => h.Host))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var seconds = recipes.Min(r => r.EffectiveEverySeconds);
        var every = seconds % 60 == 0 ? $"{seconds / 60} min" : $"{seconds} s";
        return $"Reads {string.Join(", ", hosts)} every {every}";
    }

    /// <summary>What the mode sends about your accounts, or null when it sends nothing per account.</summary>
    public static string? Sends(ModeDef mode, IReadOnlyList<InstalledRecipe> installed) =>
        Of(mode, installed).Any(r => ReaderDisclosure.Review(r, None).Hosts.Any(h => h.Sends.Contains(ReaderDisclosure.SendsUserIds)))
            ? $"Sends {ReaderDisclosure.SendsUserIds}."
            : null;

    private static List<Recipe> Of(ModeDef mode, IReadOnlyList<InstalledRecipe> installed) =>
        [.. mode.Reads.Select(slug => installed.FirstOrDefault(i => i.Recipe.Slug == slug)?.Recipe).OfType<Recipe>()];
}
