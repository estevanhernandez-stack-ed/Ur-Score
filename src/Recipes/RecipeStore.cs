using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Labs626.UrScore.Core;

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
/// <para>
/// <see cref="SettledAccountIds"/> (name your clan once, 0.7.0) are the RoRoRo account ids, yours only, that Setup has stopped
/// asking about: placed in a clan by a members read, or let go with <b>That's all</b>. Null until either happens. It is a set
/// rather than a flag so that an account RoRoRo lists later, which is not in it, brings the question back for that account
/// alone, and nothing has to be cleared to re-arm it.
/// </para>
/// </summary>
public sealed record RecipeState(
    IReadOnlyDictionary<string, string>? Inputs = null,
    IReadOnlyList<string>? ExcludedAccountIds = null,
    IReadOnlyDictionary<string, StatChoice>? Stats = null,
    IReadOnlyList<string>? CounterNames = null,
    IReadOnlyList<string>? SentFieldMetrics = null,
    IReadOnlyList<string>? SettledAccountIds = null)
{
    /// <summary>
    /// The clan-and-field numbers ticked on a clans list. Keys only: unlike a stat, these ids are fixed by the
    /// version, not pinned per install, because the whole point of them is that every member of a clan can set
    /// an alert on the same name.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> FieldMetricKeys => SentFieldMetrics ?? [];

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
    public IReadOnlyDictionary<string, StatChoice>? LegacyStatChoices { get; init; }

    [JsonIgnore]
    public IReadOnlyDictionary<string, StatChoice> ChoicesForUpdate => Stats ?? LegacyStatChoices ?? StatChoices;

    [JsonIgnore]
    public IReadOnlyList<string> SavedCounterNames => CounterNames ?? [];

    /// <summary><see cref="SettledAccountIds"/> parsed; an id that isn't a GUID is ignored, as with the exclude list.</summary>
    [JsonIgnore]
    public IReadOnlySet<Guid> Settled => (SettledAccountIds ?? [])
        .Select(id => (Parsed: Guid.TryParse(id, out var guid), Id: guid))
        .Where(x => x.Parsed)
        .Select(x => x.Id)
        .ToHashSet();

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

public sealed record InstalledRecipe(Recipe Recipe, string Text, RecipeState State)
{
    /// <summary>
    /// The state's ticks are the seed (<see cref="RecipeStates.Effective"/> made them: no saved stats, no legacy choices), not
    /// the person's. A write that isn't the person changing a choice, such as a read's counter names, writes the state
    /// without them, so the seed is never frozen into the file and the next start seeds again (review round 2).
    /// </summary>
    public bool Seeded { get; init; }
}

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

    /// <summary>A state's JSON, and back, in the shape the state file uses. Public so a stats file can carry a setup's ticks.</summary>
    public static string SerializeState(RecipeState state) => JsonSerializer.Serialize(state, Options);

    public static RecipeState ParseState(string json) => JsonSerializer.Deserialize<RecipeState>(json, Options) ?? new RecipeState();

    public static string DefaultDirectory => AppPaths.Default.Recipes;

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

            return new InstalledRecipe(parsed.Recipe!, text, LoadState(parsed.Recipe!));
        }
        catch (Exception ex)
        {
            problem = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// One recipe's saved state, with the legacy <c>metricIdOverride</c> carried into <see cref="RecipeState.LegacyStatChoices"/>
    /// (needs the recipe for that). A missing file gives an empty state; an unreadable one costs the choices, not the recipe.
    /// Readers.Compose calls this per built-in recipe (games-and-modes A8).
    /// </summary>
    internal RecipeState LoadState(Recipe recipe) => TryLoadState(recipe) ?? new RecipeState();

    /// <summary>
    /// As <see cref="LoadState"/>, but null when there is no usable saved state, so <c>RecipeStates.Effective</c> reaches its
    /// "no saved state" branch and seeds a fresh install's ticks (A1). A missing file is null. So is an unreadable or corrupt
    /// one: its choices are lost either way, and treating it as unseeded gives the user the mode's defaults back instead of a
    /// board stuck on "No stats turned on yet" forever.
    /// </summary>
    internal RecipeState? TryLoadState(Recipe recipe)
    {
        try
        {
            var file = StatePath(recipe.Slug);
            if (!File.Exists(file)) return null;

            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var state = document.RootElement.Deserialize<RecipeState>(Options) ?? new RecipeState();
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || document.RootElement.EnumerateObject().Any(property =>
                    string.Equals(property.Name, "stats", StringComparison.OrdinalIgnoreCase))
                || recipe.IsGroupList || recipe.LastStep.Values.Count != 1 || recipe.LastStep.Counters is not null)
                return state;

            var legacy = document.RootElement.EnumerateObject().LastOrDefault(property =>
                string.Equals(property.Name, "metricIdOverride", StringComparison.OrdinalIgnoreCase)).Value;
            if (legacy.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) return state;

            var value = recipe.LastStep.Values[0];
            var metricId = legacy.GetString();
            metricId = string.IsNullOrWhiteSpace(metricId) ? value.MetricId : metricId.Trim();
            if (string.IsNullOrWhiteSpace(metricId)) return state;

            return state with
            {
                LegacyStatChoices = new Dictionary<string, StatChoice>(StringComparer.Ordinal)
                {
                    [value.Id] = new(Show: true, Send: true, MetricId: metricId),
                },
            };
        }
        catch (Exception)
        {
            // A hand-edited state file that no longer parses costs the choices, not the recipe.
            return null;
        }
    }

    private string RecipePath(string slug) => Path.Combine(directory, slug + RecipeSuffix);

    private string StatePath(string slug) => Path.Combine(directory, slug + ".state.json");
}
