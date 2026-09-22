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

/// <summary>What loading <c>sources.json</c> found: a file that isn't there is not the same as one that can't be read.</summary>
public sealed record SourceLoad(IReadOnlyList<Source> Sources, bool Exists, bool Readable);

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

    /// <summary>A missing or hand-broken file is no sources. <see cref="LoadResult"/> says which it was.</summary>
    public IReadOnlyList<Source> Load() => LoadResult().Sources;

    /// <summary>
    /// The sources, whether the file exists, and whether it could be read (a broken, locked or denied file can't).
    /// <para>
    /// "Exists" is answered by trying to open the file, never by <c>File.Exists</c>. S1-14.15 feared that call
    /// answers false for a file that is there but denied — the one false answer that would do damage, because
    /// start reads "no file" as "migrate and save", which would write over the user's clans. CHECKED 2026-09-21
    /// with a real deny rule on the file, Read and then FullControl: on this platform <c>File.Exists</c> answers
    /// true both times, so the old code already reached the unreadable branch and start held. The open-based
    /// shape is kept anyway, because it is the one that cannot be fooled by any platform's answer to
    /// <c>File.Exists</c>, nor by the file vanishing between an exists check and the read: not-found means not
    /// there, and anything else that stops the read means there but unreadable.
    /// </para>
    /// </summary>
    public SourceLoad LoadResult()
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new SourceLoad([], Exists: false, Readable: true);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return new SourceLoad([], Exists: true, Readable: false);
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<List<Source>>(text, Options) ?? [];
            return new SourceLoad(
                [.. loaded.Where(s => !string.IsNullOrWhiteSpace(s.Id) && !string.IsNullOrWhiteSpace(s.Recipe) && s.Inputs is not null)],
                Exists: true, Readable: true);
        }
        catch (JsonException)
        {
            return new SourceLoad([], Exists: true, Readable: false);
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

    /// <summary>
    /// After an import or reload: a recipe that wasn't installed before, has no inputs and has no source yet
    /// gets one (watch for a group list, mine otherwise). A recipe with inputs gets nothing (its values are
    /// picked in Setup, and part 2a's saved inputs are never migrated again), and an already installed recipe
    /// whose source was removed doesn't get it back. Returns <paramref name="sources"/> itself when nothing is added.
    /// </summary>
    public static IReadOnlyList<Source> ForNewRecipes(
        IReadOnlyList<Source> sources, IReadOnlyList<InstalledRecipe> before, IReadOnlyList<InstalledRecipe> after)
    {
        var known = before.Select(i => i.Recipe.Slug).ToHashSet(StringComparer.Ordinal);
        List<Source>? result = null;

        foreach (var item in after)
        {
            var recipe = item.Recipe;
            if (known.Contains(recipe.Slug) || recipe.Inputs.Count > 0) continue;
            if ((result ?? sources).Any(s => string.Equals(s.Recipe, recipe.Slug, StringComparison.Ordinal))) continue;

            result ??= [.. sources];
            result.Add(new Source(NewId(), recipe.Slug, new Dictionary<string, string>(), recipe.IsGroupList ? SourceRole.Watch : SourceRole.Mine));
        }

        return result ?? sources;
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

    /// <summary>
    /// The clan names your own sources carry: the one answer to "which clans are mine", taken by the score book, the
    /// send, the alert labels and the board alike. This filter was hand-rolled in three places and drifted twice
    /// (V3-S.31), so there is one of it now and every caller takes it.
    /// <para>
    /// A WATCHED clan is never one of yours — that is the whole contract of the role: "never matched to your
    /// accounts, never sent, and no account recorded". A group list carries the field rather than a clan of yours,
    /// so its own source names nobody. Nor does a source whose recipe is not installed: this set makes the positive
    /// claim "this clan is mine", and without the recipe there is no way to tell whether its inputs name a clan at
    /// all. The two surviving copies disagreed on exactly that case until they were collapsed into this one.
    /// </para>
    /// <para>
    /// Both of the drifts this replaces were live on the owner's own board, which watches CCGP. Until 0.5.2 the
    /// send's copy filtered only on the recipe's shape, so watching a rival made its standing your standing: placed
    /// above you, FieldSummary took ITS points as field-mine, wrote them to the book, and sent its place, its gap
    /// and its roster counts to RoRoRo under your clan's ids. The board's copy was left role-blind by that fix, so
    /// a watched rival placed above you still anchored the race band on itself, bolded itself as yours in the
    /// standings, and measured every other clan's gap from its points instead of yours. Found by review on
    /// 2026-09-20 (V3-S.30, V3-S.31).
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> MyClanNames(IReadOnlyList<Source> sources, IReadOnlyList<InstalledRecipe> installed)
    {
        var lists = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var recipe in installed) lists[recipe.Recipe.Slug] = recipe.Recipe.IsGroupList;

        return [.. sources
            .Where(s => s.Enabled && s.Role != SourceRole.Watch && lists.TryGetValue(s.Recipe, out var isList) && !isList)
            .SelectMany(s => s.Inputs.Values)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    public static string NewId() => "s-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
}
