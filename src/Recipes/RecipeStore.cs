using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Labs626.UrScore.Recipes;

/// <summary>
/// What the user chose for one recipe: input values, a metric id if they changed it, and accounts
/// switched off. Never written into the recipe file (spec §3.3).
/// </summary>
public sealed record RecipeState(
    IReadOnlyDictionary<string, string>? Inputs = null,
    string? MetricIdOverride = null,
    IReadOnlyList<string>? ExcludedAccountIds = null)
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

    public string MetricIdFor(Recipe recipe) =>
        string.IsNullOrWhiteSpace(MetricIdOverride) ? recipe.MetricId : MetricIdOverride.Trim();
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

    public void SaveState(Recipe recipe, RecipeState state)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(StatePath(recipe.Slug), JsonSerializer.Serialize(state, Options));
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
