using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Core;

/// <summary>Score book spec §4.1.</summary>
public enum SourceRole
{
    /// <summary>The ★ clan. At most one per recipe; promotion checks measure other sources against it.</summary>
    Main,

    /// <summary>Your accounts are expected here: matched, sent and recorded.</summary>
    Mine,

    /// <summary>Clan-level only: never matched to your accounts, never sent, and no account recorded.</summary>
    Watch,
}

/// <summary>One installed recipe with one set of input values and a role.</summary>
public sealed record Source(string Id, string Recipe, IReadOnlyDictionary<string, string> Inputs, SourceRole Role, bool Enabled = true)
{
    /// <summary>The same clan typed twice with different case or spaces is one source.</summary>
    [JsonIgnore]
    public string InputsKey => KeyOf(Inputs);

    public static string KeyOf(IReadOnlyDictionary<string, string> inputs) =>
        string.Join('\u001f', inputs
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value.Trim().ToLowerInvariant()}"));
}

/// <summary><c>sources.json</c>. Holds recipe slugs, input values and roles: never a key, never an account.</summary>
public sealed class SourceStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "626labs.ur-score", "sources.json");

    /// <summary>A missing or hand-broken file is no sources; migration rebuilds what it can from recipe state.</summary>
    public IReadOnlyList<Source> Load()
    {
        try
        {
            if (!File.Exists(path)) return [];

            var loaded = JsonSerializer.Deserialize<List<Source>>(File.ReadAllText(path), Options) ?? [];
            return [.. loaded.Where(s => !string.IsNullOrWhiteSpace(s.Id) && !string.IsNullOrWhiteSpace(s.Recipe) && s.Inputs is not null)];
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Save(IReadOnlyList<Source> sources)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(sources, Options));
        File.Move(temp, path, overwrite: true);
    }
}

public static class SourceRules
{
    /// <summary>
    /// Every installed recipe that has no source yet gets one when it can: a recipe without inputs gets
    /// one (watch for a group list, mine otherwise), and a recipe whose saved inputs are all filled gets
    /// its main (Ruling R1). Existing sources are kept as they are, including ones whose recipe didn't
    /// load this time, so a recipe file that briefly fails to parse doesn't cost the user's clans.
    /// </summary>
    public static IReadOnlyList<Source> Migrate(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> existing)
    {
        var result = existing.ToList();
        foreach (var item in installed)
        {
            var recipe = item.Recipe;
            if (result.Any(s => s.Recipe == recipe.Slug)) continue;

            if (recipe.Inputs.Count == 0)
            {
                result.Add(new Source(NewId(), recipe.Slug, new Dictionary<string, string>(),
                    recipe.IsGroupList ? SourceRole.Watch : SourceRole.Mine));
                continue;
            }

            var values = item.State.InputValues;
            if (recipe.Inputs.All(i => values.TryGetValue(i.Id, out var v) && !string.IsNullOrWhiteSpace(v)))
            {
                result.Add(new Source(NewId(), recipe.Slug,
                    recipe.Inputs.ToDictionary(i => i.Id, i => values[i.Id].Trim(), StringComparer.Ordinal), SourceRole.Main));
            }
        }

        return result;
    }

    /// <summary>The same recipe and inputs update the existing source's role; a new pair becomes a new source.</summary>
    public static IReadOnlyList<Source> Add(IReadOnlyList<Source> sources, string recipe, IReadOnlyDictionary<string, string> inputs, SourceRole role)
    {
        var key = Source.KeyOf(inputs);
        var list = sources.ToList();
        var index = list.FindIndex(s => s.Recipe == recipe && s.InputsKey == key);

        string id;
        if (index >= 0)
        {
            list[index] = list[index] with { Role = role, Enabled = true };
            id = list[index].Id;
        }
        else
        {
            id = NewId();
            list.Add(new Source(id, recipe,
                inputs.ToDictionary(kv => kv.Key, kv => kv.Value.Trim(), StringComparer.Ordinal), role));
        }

        return role == SourceRole.Main ? MakeMain(list, id) : list;
    }

    public static IReadOnlyList<Source> MakeMain(IReadOnlyList<Source> sources, string sourceId)
    {
        var target = sources.FirstOrDefault(s => s.Id == sourceId);
        if (target is null) return sources;

        return [.. sources.Select(s =>
            s.Id == sourceId ? s with { Role = SourceRole.Main }
            : s.Recipe == target.Recipe && s.Role == SourceRole.Main ? s with { Role = SourceRole.Mine }
            : s)];
    }

    public static IReadOnlyList<Source> Remove(IReadOnlyList<Source> sources, string sourceId) =>
        [.. sources.Where(s => s.Id != sourceId)];

    public static IReadOnlyList<Source> ForgetRecipe(IReadOnlyList<Source> sources, string recipe) =>
        [.. sources.Where(s => s.Recipe != recipe)];

    public static string NewId() => "s-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
}
