using System.IO;
using System.Text.Json;
using Labs626.UrScore.Board;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Core;

/// <summary>One recipe as it travels: its text and state, with exclusions as Roblox user ids rather than RoRoRo's GUIDs.</summary>
public sealed record SetupRecipe(string Slug, string Name, string Text, RecipeState State, IReadOnlyList<long> ExcludedUserIds);

/// <summary>The NAME of a key a recipe wants — never its value — so the other PC's reminder is exact.</summary>
public sealed record SetupKey(string Id, string Label, string RecipeSlug, string RecipeName);

/// <summary>
/// What travels with the stats (design 2026-09-22, §1): recipes with their ticks, clans, boards, two settings and
/// the names of keys. Written into and read from a <c>setup</c> folder beside the <c>scorebook</c> one in the
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
    /// Nothing to carry: no recipes, no clans, no boards, no key names. A pristine PC's export attaches no setup at
    /// all rather than a folder holding two settings and four empty lists, so the other side opens it as the
    /// stats-only file it is and no preview offers a person an empty setup (spec §1).
    /// </summary>
    public bool IsEmpty => Recipes.Count == 0 && Sources.Count == 0 && Boards.Count == 0 && Keys.Count == 0;

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    /// <summary>
    /// <c>settings.json</c>'s own shape: exactly the two settings that travel. Written and read by hand, never
    /// through <see cref="Settings.Save"/>/<see cref="Settings.Load"/>, so that <c>StartOnOpen</c> — a
    /// per-machine choice — has no KEY on disk here, not merely a false value.
    /// </summary>
    private sealed record SettingsDto(bool ResolveNames, string? ActiveRecipe);

    /// <summary>This machine's setup, made ready to travel: boards sanitized to your own ids, exclusions as user ids, keys as names.</summary>
    public static SetupPack FromHere(
        IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources, IReadOnlyList<BoardDef> savedBoards,
        Settings settings, IReadOnlyList<HostAccount> accounts)
    {
        var userIdOf = accounts.Where(a => a.RobloxUserId != 0).ToDictionary(a => a.AccountId, a => a.RobloxUserId);
        var recipes = installed.Select(i => new SetupRecipe(
            i.Recipe.Slug, i.Recipe.Name, i.Text,
            i.State with { ExcludedAccountIds = null },
            [.. i.State.Excluded.Where(userIdOf.ContainsKey).Select(id => userIdOf[id]).Order()])).ToList();
        var keys = installed
            .SelectMany(i => i.Recipe.Keys.Select(k => new SetupKey(k.Id, k.Label, i.Recipe.Slug, i.Recipe.Name)))
            .ToList();

        return new SetupPack(
            recipes,
            sources,
            BoardDefs.Sanitize(savedBoards, LiveBoard.UserIdsOf(accounts)),
            new Settings(ResolveNames: settings.ResolveNames, ActiveRecipe: settings.ActiveRecipe),
            keys);
    }

    public void ToFolder(string root)
    {
        var folder = Path.Combine(root, Folder);
        var recipes = Path.Combine(folder, "recipes");
        Directory.CreateDirectory(recipes);
        foreach (var recipe in Recipes)
        {
            File.WriteAllText(Path.Combine(recipes, recipe.Slug + ".recipe.json"), recipe.Text);
            File.WriteAllText(Path.Combine(recipes, recipe.Slug + ".state.json"), RecipeStore.SerializeState(recipe.State));
        }

        File.WriteAllText(Path.Combine(folder, "exclusions.json"),
            JsonSerializer.Serialize(Recipes.ToDictionary(r => r.Slug, r => r.ExcludedUserIds), Json));
        File.WriteAllText(Path.Combine(folder, "sources.json"), SourceStore.Serialize(Sources));
        File.WriteAllText(Path.Combine(folder, "boards.json"), BoardJson.Serialize(Boards));
        File.WriteAllText(Path.Combine(folder, "settings.json"),
            JsonSerializer.Serialize(new SettingsDto(Settings.ResolveNames, Settings.ActiveRecipe), Json));
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
        foreach (var file in Directory.Exists(recipesFolder) ? Directory.EnumerateFiles(recipesFolder, "*.recipe.json").Order(StringComparer.Ordinal).ToList() : [])
        {
            var slug = Path.GetFileName(file)[..^".recipe.json".Length];
            var text = File.ReadAllText(file);
            var parsed = RecipeParser.Parse(text);
            var stateFile = Path.Combine(recipesFolder, slug + ".state.json");
            var state = File.Exists(stateFile) ? RecipeStore.ParseState(File.ReadAllText(stateFile)) : new RecipeState();
            recipes.Add(new SetupRecipe(slug, parsed.Recipe?.Name ?? slug, text, state, exclusions.GetValueOrDefault(slug) ?? []));
        }

        return new SetupPack(
            recipes,
            File.Exists(Path.Combine(folder, "sources.json")) ? SourceStore.Parse(File.ReadAllText(Path.Combine(folder, "sources.json"))) : [],
            File.Exists(Path.Combine(folder, "boards.json")) ? BoardJson.Parse(File.ReadAllText(Path.Combine(folder, "boards.json"))) : [],
            ReadJson<SettingsDto>(Path.Combine(folder, "settings.json")) is { } settings
                ? new Settings(ResolveNames: settings.ResolveNames, ActiveRecipe: settings.ActiveRecipe)
                : Settings.Defaults,
            ReadJson<List<SetupKey>>(Path.Combine(folder, "keys.json")) ?? []);
    }

    private static T? ReadJson<T>(string file) where T : class =>
        File.Exists(file) ? JsonSerializer.Deserialize<T>(File.ReadAllText(file), Json) : null;
}
