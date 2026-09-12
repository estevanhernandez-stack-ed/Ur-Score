using System.IO;
using System.Text.Json;

namespace Labs626.UrScore.Core;

/// <summary>
/// What this plugin has written into RoRoRo's rules file, so drift can be reported.
/// <para>
/// Without this, a rule we own and a rule we own whose threshold someone has since tuned are
/// indistinguishable — and the wrong response to the second is to "fix" it back, silently undoing
/// a deliberate change. Drift is reported, never corrected.
/// </para>
/// </summary>
public static class RuleInventory
{
    private static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "626labs.ur-score", "written-rules.json");

    public static void Record(string metricId, double threshold, string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);

            var all = Read(file);
            all[metricId] = threshold;
            File.WriteAllText(file, JsonSerializer.Serialize(all));
        }
        catch (Exception)
        {
            // An inventory that cannot be written costs a drift warning, not a feature.
        }
    }

    /// <summary>The threshold we wrote for this metric id, or null if we never wrote one.</summary>
    public static double? Recorded(string metricId, string? path = null) =>
        Read(path ?? DefaultPath).TryGetValue(metricId, out var threshold) ? threshold : null;

    private static Dictionary<string, double> Read(string file)
    {
        try
        {
            return File.Exists(file)
                ? JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(file)) ?? []
                : [];
        }
        catch (Exception)
        {
            return [];
        }
    }
}
