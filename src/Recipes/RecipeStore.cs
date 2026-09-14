using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Labs626.UrScore.Recipes;

/// <summary>
/// One stat's two ticks and the name RoRoRo gets it under. Written to the state file as
/// <c>{ "show": true, "send": false, "metricId": "ps99.diamonds" }</c>. The metric id is pinned when
/// the entry is first written, so a recipe update can never move where reports go.
/// </summary>
public sealed record StatChoice(bool Show = false, bool Send = false, string MetricId = "");

/// <summary>
/// What the user chose for one recipe: input values, accounts switched off, which stats are shown and
/// sent under which names, and the counter names last read from the source. Never written into the
/// recipe file (spec §3.3). A state with no <see cref="Stats"/> ticks nothing (stats design §2).
/// </summary>
public sealed record RecipeState(
    IReadOnlyDictionary<string, string>? Inputs = null,
    IReadOnlyList<string>? ExcludedAccountIds = null,
    IReadOnlyDictionary<string, StatChoice>? Stats = null,
    IReadOnlyList<string>? CounterNames = null)
{
    [JsonIgnore]
    public IReadOnlyDictionary<string, string> InputValues => Inputs ?? new Dictionary<string, string>();

    /// <summary>An exclude list, so a newly added account is watched by default (carried from Settings).</summary>
    [JsonIgnore]
    public IReadOnlySet<Guid> Excluded => (ExcludedAccountIds ?? [])
        .Select(id => (Parsed: Guid.TryParse(id, out var guid), Id: guid))
        .Where(x => x.Parsed)
        .Select(x => x.Id)
        .ToHashSet();

    /// <summary>Keyed by stat key: a value's id, or <c>counter:</c> plus a counter's name.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, StatChoice> StatChoices => Stats ?? new Dictionary<string, StatChoice>();

    [JsonIgnore]
    public IReadOnlyList<string> SavedCounterNames => CounterNames ?? [];

    /// <summary>Tracked means Show or Send: the stats a read asks for. Only stats this recipe still offers.</summary>
    public IReadOnlySet<string> TrackedStats(Recipe recipe) =>
        Chosen(recipe).Where(c => c.Choice.Show || c.Choice.Send).Select(c => c.Stat.Key).ToHashSet(StringComparer.Ordinal);

    /// <summary>The stats with Show on, in recipe order: the window's columns.</summary>
    public IReadOnlyList<RecipeStat> ShownStats(Recipe recipe) =>
        [.. Chosen(recipe).Where(c => c.Choice.Show).Select(c => c.Stat)];

    /// <summary>The stats with Send on and a name, in recipe order, each under its pinned metric id.</summary>
    public IReadOnlyList<SentStat> SentStats(Recipe recipe) =>
        [.. Chosen(recipe)
            .Where(c => c.Choice.Send && !string.IsNullOrWhiteSpace(c.Choice.MetricId))
            .Select(c => new SentStat(c.Stat.Key, c.Stat.Label, c.Choice.MetricId.Trim()))];

    private IEnumerable<(RecipeStat Stat, StatChoice Choice)> Chosen(Recipe recipe)
    {
        var choices = StatChoices;
        return RecipeStats.Offered(recipe, choices.Keys)
            .Where(stat => choices.ContainsKey(stat.Key))
            .Select(stat => (stat, choices[stat.Key]));
    }
}

public sealed record InstalledRecipe(Recipe Recipe, string Text, RecipeState State);

public sealed record RecipeStoreLoad(IReadOnlyList<InstalledRecipe> Recipes, IReadOnlyList<string> Problems);

/// <summary>
/// Installed recipes on disk: <c>{slug}.recipe.json</c> holds the exact text imported, and
/// <c>{slug}.state.json</c> holds the user's choices. Nothing here ever holds a key value.
/// </summary>
public sealed class RecipeStore(string directory)
{
    private const string RecipeSuffix = ".recipe.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "626labs.ur-score", "recipes");

    public RecipeStoreLoad LoadAll()
    {
        if (!Directory.Exists(directory)) return new RecipeStoreLoad([], []);

        var recipes = new List<InstalledRecipe>();
        var problems = new List<string>();

        foreach (var file in Directory.EnumerateFiles(directory, "*" + RecipeSuffix).Order(StringComparer.Ordinal))
        {
            var loaded = Load(file, out var problem);
            if (loaded is not null) recipes.Add(loaded);
            else problems.Add($"{Path.GetFileName(file)}: {problem}");
        }

        return new RecipeStoreLoad(recipes, problems);
    }

    public InstalledRecipe? Find(string slug)
    {
        var file = RecipePath(slug);
        return File.Exists(file) ? Load(file, out _) : null;
    }

    public void Save(Recipe recipe, string text, RecipeState state)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(RecipePath(recipe.Slug), text);
        SaveState(recipe, state);
    }

    /// <summary><see cref="Save"/> comes through here too, so no saved state skips <see cref="Normalized"/>.</summary>
    public void SaveState(Recipe recipe, RecipeState state)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(StatePath(recipe.Slug), JsonSerializer.Serialize(Normalized(recipe, state), Options));
    }

    /// <summary>
    /// A saved state never keeps a tick for a stat this recipe doesn't offer. The entry keeps its pinned
    /// metric id with Show and Send off, so when a later update offers the stat again it comes back
    /// listed and unticked (stats design §7.2), and any Send goes back through the budget and the
    /// collision rules. Offered entries are untouched.
    /// </summary>
    private static RecipeState Normalized(Recipe recipe, RecipeState state)
    {
        if (state.Stats is not { Count: > 0 } stats) return state;

        var offered = RecipeStats.Offered(recipe, stats.Keys).Select(stat => stat.Key).ToHashSet(StringComparer.Ordinal);
        return state with
        {
            Stats = stats.ToDictionary(
                kv => kv.Key,
                kv => offered.Contains(kv.Key) ? kv.Value : kv.Value with { Show = false, Send = false },
                StringComparer.Ordinal),
        };
    }

    public bool Remove(string slug)
    {
        var existed = File.Exists(RecipePath(slug));
        File.Delete(RecipePath(slug));
        File.Delete(StatePath(slug));
        return existed;
    }

    private InstalledRecipe? Load(string file, out string? problem)
    {
        problem = null;
        try
        {
            var text = File.ReadAllText(file);
            var parsed = RecipeParser.Parse(text);
            if (!parsed.Ok)
            {
                problem = string.Join(" ", parsed.Problems);
                return null;
            }

            return new InstalledRecipe(parsed.Recipe!, text, LoadState(parsed.Recipe!.Slug));
        }
        catch (Exception ex)
        {
            problem = ex.Message;
            return null;
        }
    }

    private RecipeState LoadState(string slug)
    {
        try
        {
            var file = StatePath(slug);
            return File.Exists(file)
                ? JsonSerializer.Deserialize<RecipeState>(File.ReadAllText(file), Options) ?? new RecipeState()
                : new RecipeState();
        }
        catch (Exception)
        {
            // A hand-edited state file that no longer parses costs the choices, not the recipe.
            return new RecipeState();
        }
    }

    private string RecipePath(string slug) => Path.Combine(directory, slug + RecipeSuffix);

    private string StatePath(string slug) => Path.Combine(directory, slug + ".state.json");
}
