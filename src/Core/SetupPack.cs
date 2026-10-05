using System.IO;
using System.Text.Json;
using Labs626.UrScore.Board;
using Labs626.UrScore.Games;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Core;

/// <summary>
/// One reader's saved choices as they travel, with exclusions as Roblox user ids rather than RoRoRo's GUIDs. The reader's
/// text never travels (A7): both PCs run the copy built into their own version. <see cref="Text"/> stays optional so a
/// caller that holds one can carry it, but <see cref="SetupPack.FromHere"/> sets it null and <see cref="SetupPack.FromFolder"/> ignores an old export's.
/// </summary>
public sealed record SetupRecipe(string Slug, string Name, string? Text, RecipeState State, IReadOnlyList<long> ExcludedUserIds);

/// <summary>The NAME of a key a recipe wants — never its value — so the other PC's reminder is exact.</summary>
public sealed record SetupKey(string Id, string Label, string RecipeSlug, string RecipeName);

/// <summary>
/// What travels with the stats (design 2026-09-22, §1; games-and-modes A7): the ticks of every reader whose choices were
/// changed, clans, boards, the mode switches, the one setting and the names of keys. No reader text. Written into and read from a <c>setup</c> folder beside the <c>scorebook</c> one in the
/// stats file. Never <c>keys.dat</c>, never <c>accounts.json</c>, never <c>StartOnOpen</c> (a per-machine choice),
/// and never a RoRoRo account GUID: exclusions cross as Roblox user ids, which are the same everywhere.
/// </summary>
public sealed record SetupPack(
    IReadOnlyList<SetupRecipe> Recipes,
    IReadOnlyList<Source> Sources,
    IReadOnlyList<BoardDef> Boards,
    Settings Settings,
    IReadOnlyList<SetupKey> Keys)
{
    public const string Folder = "setup";

    /// <summary>
    /// The file's <c>modes.json</c> was there and could not be read (review round 2): the switches are left out, and the
    /// plan says so as one skipped item instead of the whole import failing over them.
    /// </summary>
    public bool ModesUnreadable { get; init; }

    /// <summary>
    /// Nothing the person made. A pristine PC now holds the shipped readers and their automatic sources, so "nothing"
    /// can no longer mean "no lists" (item 9). It means: no reader whose choices differ from its seed
    /// (<see cref="FromHere"/> only lists those), no clan (a source with an input), no board of their own, no key
    /// name, and no mode switch away from its default (<see cref="FromHere"/> drops a switch equal to its default).
    /// A pristine PC's export then attaches no setup at all and the other side opens a stats-only file.
    /// </summary>
    public bool IsEmpty => Recipes.Count == 0
        && !Sources.Any(s => s.Inputs.Values.Any(v => !string.IsNullOrWhiteSpace(v)))
        && Boards.All(b => b.Follows is not null)
        && Keys.Count == 0
        && (Settings.Modes is not { Count: > 0 });

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    /// <summary>
    /// <c>settings.json</c>'s own shape: the one setting that travels. Written and read by hand, never
    /// through <see cref="Settings.Save"/>/<see cref="Settings.Load"/>, so that <c>StartOnOpen</c> — a
    /// per-machine choice — has no KEY on disk here, not merely a false value. A 0.6.3 export also holds
    /// <c>activeRecipe</c>; it is tolerated on read (an unknown key) and never written.
    /// </summary>
    private sealed record SettingsDto(bool ResolveNames);

    /// <summary>
    /// This machine's setup, made ready to travel: boards sanitized to your own ids, exclusions as user ids, keys as names.
    /// A reader is listed only when its state differs from the seed a fresh install would run with (counter names
    /// aside, which a read writes by itself), and the mode switches only when one differs from its default, so the
    /// file carries what the person did and nothing a clean install would not already have.
    /// </summary>
    public static SetupPack FromHere(
        IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources, IReadOnlyList<BoardDef> savedBoards,
        Settings settings, IReadOnlyList<HostAccount> accounts, GameCatalog? catalog = null)
    {
        catalog ??= GameCatalog.BuiltIn;
        var userIdOf = accounts.Where(a => a.RobloxUserId != 0).ToDictionary(a => a.AccountId, a => a.RobloxUserId);
        var recipes = installed.Where(i => Changed(i, catalog)).Select(i => new SetupRecipe(
            i.Recipe.Slug, i.Recipe.Name, null,
            i.State with { ExcludedAccountIds = null },
            [.. i.State.Excluded.Where(userIdOf.ContainsKey).Select(id => userIdOf[id]).Order()])).ToList();
        var keys = installed
            .SelectMany(i => i.Recipe.Keys.Select(k => new SetupKey(k.Id, k.Label, i.Recipe.Slug, i.Recipe.Name)))
            .ToList();

        var deviates = settings.Modes?.Any(kv => !IsDefault(catalog, kv.Key, kv.Value)) == true;

        return new SetupPack(
            recipes,
            sources,
            BoardDefs.Sanitize(savedBoards, LiveBoard.UserIdsOf(accounts)),
            new Settings(ResolveNames: settings.ResolveNames, Modes: deviates ? settings.Modes : null),
            keys);
    }

    /// <summary>A switch equal to what it falls back to anyway: a game with no key is on, a mode with none follows its manifest.</summary>
    private static bool IsDefault(GameCatalog catalog, string key, bool value) =>
        catalog.Find(key) is { } mode ? mode.OnByDefault == value
        : catalog.Games.Any(g => string.Equals(g.Id, key, StringComparison.Ordinal)) && value;

    private static bool Changed(InstalledRecipe installed, GameCatalog catalog)
    {
        var shows = catalog.ModeOf(installed.Recipe.Slug) is { } mode ? Readers.ShowsFor(mode, installed.Recipe.Slug) : [];
        var seed = RecipeStates.Effective(installed.Recipe, null, shows);
        return Canonical(installed.State with { CounterNames = null }) != Canonical(seed);
    }

    /// <summary>
    /// A state as comparable text: dictionaries sorted (a record's own equality compares them by reference), with
    /// what never travels as it is set aside: Send arrives off and the sent field list is cleared on arrival, so
    /// neither is what makes two states "the same".
    /// </summary>
    public static string Canonical(RecipeState state) => RecipeStore.SerializeState(state with
    {
        // No inputs and an empty inputs object say the same thing (review round 2); one writer leaves the key out, another writes {}.
        Inputs = state.Inputs is { Count: > 0 } inputs
            ? inputs.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)
            : null,
        Stats = state.Stats?.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(kv => kv.Key, kv => kv.Value with { Send = false }, StringComparer.Ordinal),
        SentFieldMetrics = null,
        ExcludedAccountIds = state.ExcludedAccountIds?.Order(StringComparer.Ordinal).ToList(),
    });

    public void ToFolder(string root)
    {
        var folder = Path.Combine(root, Folder);
        var recipes = Path.Combine(folder, "recipes");
        Directory.CreateDirectory(recipes);
        foreach (var recipe in Recipes)
        {
            File.WriteAllText(Path.Combine(recipes, recipe.Slug + ".state.json"), RecipeStore.SerializeState(recipe.State));
        }

        File.WriteAllText(Path.Combine(folder, "exclusions.json"),
            JsonSerializer.Serialize(Recipes.ToDictionary(r => r.Slug, r => r.ExcludedUserIds), Json));
        File.WriteAllText(Path.Combine(folder, "sources.json"), SourceStore.Serialize(Sources));
        File.WriteAllText(Path.Combine(folder, "boards.json"), BoardJson.Serialize(Boards));
        File.WriteAllText(Path.Combine(folder, "settings.json"),
            JsonSerializer.Serialize(new SettingsDto(Settings.ResolveNames), Json));
        File.WriteAllText(Path.Combine(folder, "modes.json"),
            JsonSerializer.Serialize(Settings.Modes ?? new Dictionary<string, bool>(), Json));
        File.WriteAllText(Path.Combine(folder, "keys.json"), JsonSerializer.Serialize(Keys, Json));
    }

    /// <summary>The setup a folder holds, or null when it holds none (a 0.5.5 stats-only file).</summary>
    public static SetupPack? FromFolder(string root)
    {
        var folder = Path.Combine(root, Folder);
        if (!Directory.Exists(folder)) return null;

        var exclusions = ReadJson<Dictionary<string, List<long>>>(Path.Combine(folder, "exclusions.json")) ?? [];
        var recipes = new List<SetupRecipe>();
        var recipesFolder = Path.Combine(folder, "recipes");
        // A 0.6.3 export has both files per reader, a new one only the state; either names the slug. The old text is
        // ignored: it would only ever say what the sender's copy was, and this PC reads its own (A7).
        var slugs = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var suffix in new[] { ".state.json", ".recipe.json" })
        {
            if (!Directory.Exists(recipesFolder)) break;
            foreach (var file in Directory.EnumerateFiles(recipesFolder, "*" + suffix)) slugs.Add(Path.GetFileName(file)[..^suffix.Length]);
        }

        foreach (var slug in slugs)
        {
            recipes.Add(new SetupRecipe(slug, slug, null, StateFrom(recipesFolder, slug), exclusions.GetValueOrDefault(slug) ?? []));
        }

        // A broken modes.json costs the switches, said as one skipped item, never the whole import (review round 2).
        Dictionary<string, bool>? modesRead;
        var modesUnreadable = false;
        try
        {
            modesRead = ReadJson<Dictionary<string, bool>>(Path.Combine(folder, "modes.json"));
        }
        catch (JsonException)
        {
            modesRead = null;
            modesUnreadable = true;
        }

        IReadOnlyDictionary<string, bool>? modes = modesRead is { Count: > 0 } ? modesRead : null;

        return new SetupPack(
            recipes,
            File.Exists(Path.Combine(folder, "sources.json")) ? SourceStore.Parse(File.ReadAllText(Path.Combine(folder, "sources.json"))) : [],
            File.Exists(Path.Combine(folder, "boards.json")) ? BoardJson.Parse(File.ReadAllText(Path.Combine(folder, "boards.json"))) : [],
            ReadJson<SettingsDto>(Path.Combine(folder, "settings.json")) is { } settings
                ? new Settings(ResolveNames: settings.ResolveNames, Modes: modes)
                : new Settings(Modes: modes),
            ReadJson<List<SetupKey>>(Path.Combine(folder, "keys.json")) ?? [])
        {
            ModesUnreadable = modesUnreadable,
        };
    }

    /// <summary>
    /// A reader's state from the pack, read the way the app reads its own folder when the reader is built in (review round 2):
    /// a 0.6.3 export's clan-battle state holds only a legacy <c>metricIdOverride</c>, which plain parsing drops, so the
    /// pinned id would never arrive. Its legacy choices become the state's ticks here, as <c>RecipeStates.Effective</c>
    /// makes them at home. A slug this version doesn't build in is parsed plainly; the plan skips it anyway.
    /// </summary>
    private static RecipeState StateFrom(string recipesFolder, string slug)
    {
        var stateFile = Path.Combine(recipesFolder, slug + ".state.json");
        if (!File.Exists(stateFile)) return new RecipeState();
        if (!BuiltInRecipes.BySlug.TryGetValue(slug, out var builtIn)) return RecipeStore.ParseState(File.ReadAllText(stateFile));

        var state = new RecipeStore(recipesFolder).TryLoadState(builtIn) ?? RecipeStore.ParseState(File.ReadAllText(stateFile));
        return state.Stats is null && state.ChoicesForUpdate.Count > 0
            ? state with { Stats = new Dictionary<string, StatChoice>(state.ChoicesForUpdate, StringComparer.Ordinal) }
            : state;
    }

    private static T? ReadJson<T>(string file) where T : class =>
        File.Exists(file) ? JsonSerializer.Deserialize<T>(File.ReadAllText(file), Json) : null;
}
