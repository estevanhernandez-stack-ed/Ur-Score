using System.IO;
using System.Text.Json;

namespace Labs626.UrScore.Core;

/// <summary>
/// What the user configures for Ur Score as a whole. Everything about a particular source — its
/// inputs, metric id and which accounts send — lives with that recipe (<c>RecipeState</c>), and
/// thresholds live in RoRoRo, because RoRoRo does the judging.
/// <para>
/// <c>StartOnOpen</c> is on by default and was switched on once for every existing install (BC1, 2026-09-23,
/// superseding A31). The constructor's defaults are what a file WITHOUT those keys means — off, version 0 — so an
/// old file is recognisable and <see cref="Migrate"/> can turn it on exactly once; <see cref="Defaults"/> is what a
/// new install writes. It is about this app's own window, not about RoRoRo launching the plugin, which is the
/// manifest's <c>autostartDefault</c> and stays off (A33).
/// </para>
/// </summary>
public sealed record Settings(bool ResolveNames = true, string? ActiveRecipe = null, bool StartOnOpen = false, int SettingsVersion = 0)
{
    /// <summary>2: BC1's migration has run. A file below it is from before 0.6.</summary>
    public const int CurrentVersion = 2;

    public static Settings Defaults { get; } = new(StartOnOpen: true, SettingsVersion: CurrentVersion);

    /// <summary>A sibling of RoRoRo's own folder, never inside it.</summary>
    public static string DefaultPath => AppPaths.Default.Settings;

    /// <summary>Case-insensitive so a file written by an older PascalCase build still loads.</summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>BC1: an install from before 0.6 reads on open from now on; once migrated, what the player chooses stands.</summary>
    public static Settings Migrate(Settings settings) =>
        settings.SettingsVersion >= CurrentVersion ? settings : settings with { StartOnOpen = true, SettingsVersion = CurrentVersion };

    public static Settings Load(string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;

            if (!File.Exists(file))
            {
                // "Start it once and it creates the file" has to be true.
                Save(Defaults, file);
                return Defaults;
            }

            var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(file), Options) ?? Defaults;
            var migrated = Migrate(loaded);
            if (ReferenceEquals(migrated, loaded)) return loaded;

            try
            {
                Save(migrated, file);
            }
            catch (Exception)
            {
                // Reading on open is still what this install should do; the next open writes it.
            }

            return migrated;
        }
        catch (Exception)
        {
            // An unreadable file is not a reason not to start.
            return Defaults;
        }
    }

    public static void Save(Settings settings, string? path = null)
    {
        var file = path ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(settings, Options));
    }
}
