using System.IO;
using System.Text.Json;

namespace Labs626.UrScore.Core;

/// <summary>
/// What the user configures for Ur Score as a whole. Everything about a particular source — its
/// inputs, metric id and which accounts send — lives with that recipe (<c>RecipeState</c>), and
/// thresholds live in RoRoRo, because RoRoRo does the judging.
/// </summary>
public sealed record Settings(bool ResolveNames = true, string? ActiveRecipe = null)
{
    public static Settings Defaults { get; } = new();

    /// <summary>A sibling of RoRoRo's own folder, never inside it.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "626labs.ur-score", "settings.json");

    /// <summary>Case-insensitive so a file written by an older PascalCase build still loads.</summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

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

            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(file), Options) ?? Defaults;
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
