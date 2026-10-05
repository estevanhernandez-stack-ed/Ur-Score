using System.IO;
using Labs626.UrScore.Core;

namespace Labs626.UrScore.Games;

/// <summary>
/// The one-time modes decision for an install that predates mode switches (spec A5). Before modes, what a player had
/// installed was what they read, so an upgrade keeps that: a mode is on only if one of its recipes is already on disk.
/// A fresh install writes nothing and lets the manifest defaults apply.
/// </summary>
public static class UpgradeModes
{
    private const string RecipeSuffix = ".recipe.json";

    /// <summary>
    /// The modes map to write, or null for "write nothing". Null when the settings already carry a map (decided), and
    /// null for a fresh install (<paramref name="dataFolderExisted"/> false). Otherwise an explicit map: the game on,
    /// and for every mode in the catalog, on iff one of its reads has a recipe file on disk.
    /// </summary>
    public static IReadOnlyDictionary<string, bool>? FirstRun(
        GameCatalog catalog, bool dataFolderExisted, IReadOnlyCollection<string> installedRecipeSlugsOnDisk,
        IReadOnlyDictionary<string, bool>? currentModes)
    {
        if (currentModes is not null || !dataFolderExisted) return null;

        var map = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var game in catalog.Games)
        {
            map[game.Id] = true;
            foreach (var mode in game.Modes)
            {
                map[mode.Key] = mode.Reads.Any(slug => installedRecipeSlugsOnDisk.Contains(slug, StringComparer.Ordinal));
            }
        }

        return map;
    }

    /// <summary>
    /// Whether the data folder was in use before this start: it has a <c>sources.json</c> or at least one
    /// <c>*.recipe.json</c>. Not settings.json, which <see cref="Settings.Load"/> creates on every first start (so it
    /// is present by the time this runs), and not the bare folder, which a failed earlier start may have left empty.
    /// Call it before anything in this start writes to the folder.
    /// </summary>
    public static bool DataFolderExisted(AppPaths paths) =>
        File.Exists(paths.Sources) || InstalledSlugs(paths).Count > 0;

    /// <summary>The slugs of the <c>{slug}.recipe.json</c> files in the recipes folder.</summary>
    public static IReadOnlyList<string> InstalledSlugs(AppPaths paths) =>
        Directory.Exists(paths.Recipes)
            ? [.. Directory.EnumerateFiles(paths.Recipes, "*" + RecipeSuffix).Select(f => Path.GetFileName(f)[..^RecipeSuffix.Length])]
            : [];
}
