using System.IO;
using System.Text.Json;

namespace Labs626.UrScore.Core;

/// <summary>
/// What the user configures. Three values, and deliberately not a fourth: thresholds live in
/// RoRoRo's own rules file because RoRoRo does the judging.
/// </summary>
public sealed record Settings(
    string ClanName,
    string MetricId,
    int PollSeconds,
    IReadOnlyList<string>? ExcludedAccountIds = null)
{
    /// <summary>
    /// Accounts the user has switched OFF, as Guid strings. An EXCLUDE list rather than an include
    /// list, and that is load-bearing: with an include list the window would have to know the
    /// user's accounts before it could allow any of them, and the first cycle would report nothing
    /// because nothing was on the list yet. Excluding means a new account is watched by default and
    /// turning one off persists.
    /// </summary>
    public IReadOnlySet<Guid> Excluded => (ExcludedAccountIds ?? [])
        .Select(id => (Parsed: Guid.TryParse(id, out var g), Id: g))
        .Where(x => x.Parsed)
        .Select(x => x.Id)
        .ToHashSet();

    /// <summary>The vendor's clan endpoints carry a three minute server cache
    /// (<c>max-age=60, s-maxage=180</c>). Polling faster returns the same bytes.</summary>
    public const int MinimumPollSeconds = 180;

    public const string DefaultMetricId = "clan.battle.points";

    public static Settings Defaults { get; } = new("", DefaultMetricId, MinimumPollSeconds);

    /// <summary>A sibling of RoRoRo's own folder, never inside it — the convention every other
    /// first-party plugin follows.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "626labs.ur-score", "settings.json");

    /// <summary>Honours the floor rather than trusting the file. A hand-edited 5 becomes 180 and
    /// the window says so.</summary>
    public int EffectivePollSeconds => Math.Max(MinimumPollSeconds, PollSeconds);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static Settings Load(string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            if (!File.Exists(file)) return Defaults;

            var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(file), Options);
            if (loaded is null) return Defaults;

            // A record's required-looking members are not enforced by deserialization: a file
            // missing one line produces nulls that every downstream consumer trusting
            // `Nullable enable` would then NRE on. Repair the missing fields and keep the rest of
            // what the user wrote.
            return loaded with
            {
                ClanName = loaded.ClanName ?? Defaults.ClanName,
                MetricId = string.IsNullOrWhiteSpace(loaded.MetricId) ? Defaults.MetricId : loaded.MetricId,
            };
        }
        catch (Exception)
        {
            // Broad and deliberate. IOException, JsonException, an unreadable ACL — to a caller
            // that needs a window on screen they all mean the same thing, and none of them is a
            // reason not to start.
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
