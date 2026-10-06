using System.IO;
using System.Reflection;
using System.Text.Json;
using Labs626.UrScore.Board;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Games;

/// <summary>A game manifest that cannot be read. The message names what is wrong, never a byte offset.</summary>
public sealed class GameManifestException(string message) : Exception(message);

/// <summary>
/// One thing a game can be read for. <see cref="Reads"/> are recipe slugs; <see cref="Asks"/> names an input id of one
/// of those recipes; <see cref="Shows"/> are "slug:statKey" ticks a fresh install starts with (A1), because a recipe that
/// marks no value <c>show</c> would otherwise seed a mode that ticks nothing.
/// </summary>
public sealed record ModeDef(
    string GameId, string Id, string Name, string Blurb, IReadOnlyList<string> Reads, string? Asks,
    string? Note, string? Board, bool OnByDefault, IReadOnlyList<string> Shows, ModeLink? Link = null)
{
    /// <summary>The key a mode's switch and its board travel under: "pet-sim-99/battle".</summary>
    public string Key => $"{GameId}/{Id}";
}

/// <summary>
/// Where a mode's accounts get linked so it can read them (the Profile mode's site), and what the button says. From the
/// manifest, never from code, so Ur Score names no host; https only, because the page opens it in the browser.
/// </summary>
public sealed record ModeLink(string Text, Uri Url);

public sealed record GameDef(string Id, string Name, IReadOnlyList<ModeDef> Modes);

/// <summary>
/// The games Ur Score knows: which recipes belong to which mode. Pure data parsed from embedded manifests, so a game's
/// hosts and intervals are still derived from its recipes and never typed here.
/// </summary>
public sealed class GameCatalog(IReadOnlyList<GameDef> games)
{
    public const int SupportedVersion = 1;

    private static readonly Lazy<(GameCatalog Catalog, IReadOnlyList<string> Problems)> Loaded = new(ReadBuiltIn);

    /// <summary>The embedded manifests that parse. One that doesn't is left out and named in <see cref="BuiltInProblems"/>.</summary>
    public static GameCatalog BuiltIn => Loaded.Value.Catalog;

    /// <summary>
    /// Each embedded manifest that could not be parsed, by resource name and why. A test keeps it empty for the shipped app;
    /// the composition says each one in the trail and in Diagnostics, so a broken manifest costs its game and never the start.
    /// </summary>
    public static IReadOnlyList<string> BuiltInProblems => Loaded.Value.Problems;

    public IReadOnlyList<GameDef> Games { get; } = games;

    public IEnumerable<ModeDef> Modes => Games.SelectMany(g => g.Modes);

    /// <summary>The mode that reads a recipe, or null for a slug no mode names (an orphan).</summary>
    public ModeDef? ModeOf(string slug) =>
        Modes.FirstOrDefault(m => m.Reads.Contains(slug, StringComparer.Ordinal));

    public ModeDef? Find(string modeKey) =>
        Modes.FirstOrDefault(m => string.Equals(m.Key, modeKey, StringComparison.Ordinal));

    public static GameDef Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            throw new GameManifestException($"The game manifest is not valid JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new GameManifestException("A game manifest is a JSON object.");

            if (!root.TryGetProperty("game", out var version) || !version.TryGetInt32(out var number) || number != SupportedVersion)
            {
                throw new GameManifestException($"This game manifest is not format version {SupportedVersion}.");
            }

            var id = Text(root, "id") ?? throw new GameManifestException("The game manifest has no id.");
            var name = Text(root, "name") ?? throw new GameManifestException($"The game '{id}' has no name.");

            if (!root.TryGetProperty("modes", out var modes) || modes.ValueKind != JsonValueKind.Array || modes.GetArrayLength() == 0)
            {
                throw new GameManifestException($"The game '{id}' has no modes.");
            }

            var list = new List<ModeDef>();
            foreach (var mode in modes.EnumerateArray())
            {
                if (mode.ValueKind != JsonValueKind.Object) throw new GameManifestException($"A mode of '{id}' is not an object.");

                var modeId = Text(mode, "id") ?? throw new GameManifestException($"A mode of '{id}' has no id.");
                if (list.Any(m => m.Id == modeId)) throw new GameManifestException($"The game '{id}' has two modes with the id '{modeId}'.");

                var modeName = Text(mode, "name") ?? throw new GameManifestException($"The mode '{id}/{modeId}' has no name.");
                list.Add(new ModeDef(
                    id, modeId, modeName, Text(mode, "blurb") ?? "", Strings(mode, "reads", $"{id}/{modeId}"),
                    Text(mode, "asks"), Text(mode, "note"), Text(mode, "board"),
                    !mode.TryGetProperty("onByDefault", out var on) || on.ValueKind != JsonValueKind.False,
                    Strings(mode, "shows", $"{id}/{modeId}"), Link(mode, $"{id}/{modeId}")));
            }

            return new GameDef(id, name, list);
        }
    }

    /// <summary>
    /// Every problem in a catalog against the recipes that ship, each naming the mode. Empty means the manifest is sound.
    /// Run by tests, and defensively at startup so one bad mode is dropped with a trail line instead of crashing.
    /// </summary>
    public static IReadOnlyList<string> Validate(GameCatalog catalog, IReadOnlyDictionary<string, Recipe> builtIns)
    {
        var problems = new List<string>();
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var dup in catalog.Games.GroupBy(g => g.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            problems.Add($"Two games have the id '{dup.Key}'.");
        }

        foreach (var game in catalog.Games)
        {
            foreach (var dup in game.Modes.GroupBy(m => m.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                problems.Add($"The game '{game.Id}' has two modes with the id '{dup.Key}'.");
            }

            foreach (var mode in game.Modes)
            {
                var recipes = new List<Recipe>();
                if (mode.Reads.Count == 0) problems.Add($"The mode '{mode.Key}' reads no recipes.");

                foreach (var slug in mode.Reads)
                {
                    if (!builtIns.TryGetValue(slug, out var recipe)) problems.Add($"The mode '{mode.Key}' reads '{slug}', which is not a recipe that ships.");
                    else recipes.Add(recipe);

                    if (owner.TryGetValue(slug, out var first) && first != mode.Key)
                    {
                        problems.Add($"The recipe '{slug}' is read by both '{first}' and '{mode.Key}'.");
                    }
                    else owner[slug] = mode.Key;
                }

                if (mode.Board is not null && !StarterBoards.Names.Any(n => StarterBoards.KeyOf(n) == mode.Board))
                {
                    problems.Add($"The mode '{mode.Key}' names the board '{mode.Board}', which is not a starter board.");
                }

                if (mode.Asks is not null && !recipes.Any(r => r.Inputs.Any(i => i.Id == mode.Asks)))
                {
                    problems.Add($"The mode '{mode.Key}' asks for '{mode.Asks}', which is not an input of its recipes.");
                }

                foreach (var shown in mode.Shows)
                {
                    var colon = shown.IndexOf(':');
                    var slug = colon < 0 ? shown : shown[..colon];
                    var key = colon < 0 ? "" : shown[(colon + 1)..];

                    if (colon < 0 || !mode.Reads.Contains(slug, StringComparer.Ordinal) || !builtIns.TryGetValue(slug, out var recipe))
                    {
                        problems.Add($"The mode '{mode.Key}' shows '{shown}', which is not a stat of one of its recipes (slug:key).");
                    }
                    else if (!RecipeStats.Offered(recipe, []).Any(s => s.Key == key))
                    {
                        problems.Add($"The mode '{mode.Key}' shows '{shown}', but '{slug}' offers no stat '{key}'.");
                    }
                }
            }
        }

        return problems;
    }

    private static (GameCatalog, IReadOnlyList<string>) ReadBuiltIn()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var manifests = new List<(string Name, string Text)>();

        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".game.json", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null) continue;

            using var reader = new StreamReader(stream);
            manifests.Add((name, reader.ReadToEnd()));
        }

        return FromManifests(manifests);
    }

    /// <summary>
    /// The catalog of every manifest that parses, in the order given, and a problem line for each that doesn't (review round 2:
    /// <see cref="Parse"/> threw inside the lazy <see cref="BuiltIn"/>, so one bad manifest stopped the app starting).
    /// </summary>
    internal static (GameCatalog Catalog, IReadOnlyList<string> Problems) FromManifests(IEnumerable<(string Name, string Text)> manifests)
    {
        var games = new List<GameDef>();
        var problems = new List<string>();
        foreach (var (name, text) in manifests)
        {
            try
            {
                games.Add(Parse(text));
            }
            catch (GameManifestException ex)
            {
                problems.Add($"{name}: {ex.Message}");
            }
        }

        return (new GameCatalog(games), problems);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static ModeLink? Link(JsonElement mode, string where)
    {
        if (!mode.TryGetProperty("link", out var link) || link.ValueKind == JsonValueKind.Null) return null;
        if (link.ValueKind != JsonValueKind.Object)
        {
            throw new GameManifestException($"The mode '{where}' has a 'link' that must be an object with a text and a url.");
        }

        var text = Text(link, "text") ?? throw new GameManifestException($"The mode '{where}' has a link that has no text.");
        if (Text(link, "url") is not { } url || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(uri.Host))
        {
            throw new GameManifestException($"The mode '{where}' has a link whose url must be an https address.");
        }

        return new ModeLink(text, uri);
    }

    private static IReadOnlyList<string> Strings(JsonElement element, string name, string where)
    {
        if (!element.TryGetProperty(name, out var value)) return [];
        if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.String))
        {
            throw new GameManifestException($"The mode '{where}' has a '{name}' that is not a list of text.");
        }

        return [.. value.EnumerateArray().Select(v => v.GetString()!)];
    }
}
