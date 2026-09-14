using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Labs626.UrScore.Core;

/// <summary>What RoRoRo's rules file currently says about our metric id.</summary>
public enum RuleState
{
    /// <summary>No rules file at all. Nothing can ever alert until one exists.</summary>
    NoFile,

    /// <summary>The file is not valid JSON — probably a half-finished hand edit. Never overwritten.</summary>
    Unreadable,

    /// <summary>A rules file with no rule for our metric id. Reports will land and never match.</summary>
    NoRuleForMetric,

    /// <summary>Our rule, as we wrote it.</summary>
    OursIntact,

    /// <summary>Our rule, with a threshold someone has since changed. Reported, never corrected.</summary>
    OursChanged,

    /// <summary>A rule with no owner: the user wrote it. Never touched.</summary>
    UserOwned,

    /// <summary>Another plugin's rule. Left alone and named.</summary>
    OwnedByAnotherPlugin,
}

/// <summary>State, plus whatever detail the window needs to explain it.</summary>
public sealed record RuleStatus(RuleState State, string? Owner, double? Threshold);

/// <summary>
/// Reads RoRoRo's <c>metric-rules.json</c>, and adds exactly one rule to it on an explicit click.
/// <para>
/// This exists because the metric id is a silent-failure seam. It must match a rule in that file or
/// nothing can ever alert, and a mismatch is quiet on both sides: reports land, history accrues, no
/// rule matches, nothing looks broken. For an audience whose bar is a common Windows user, leaving
/// that to hand-editing JSON in another app's data folder is not a setup step, it is a trap.
/// </para>
/// <para>
/// EVERY WRITE IS FENCED. Explicit call only, never on startup. Merges, never replaces. Backs up
/// first. Never edits a rule already present for our metric id, because a threshold someone tuned
/// by hand is theirs. Never touches an unreadable file, because invalid JSON is more likely a
/// half-finished edit than corruption.
/// </para>
/// </summary>
public static class RulesFile
{
    /// <summary>Stamped on rules we write. Provenance, not security — the file is meant to be
    /// hand-editable and always will be. It exists because one file has several writers.</summary>
    public const string Owner = "626labs.ur-score";

    /// <summary>
    /// RoRoRo's own folder, which is the one place this plugin reaches outside its own.
    /// <para>
    /// HARDCODED, and coupled to a decision the host made differently. RoRoRo derives this from
    /// AppSettings rather than a literal, "so it follows settings.json if the app's data location
    /// moves again" — implying it has moved once. The plugin contract exposes no way to ask where
    /// that folder is, so this literal is the best available. If the host's data location ever
    /// moves, this silently points at a file nothing reads, which is exactly the
    /// reports-land-and-nothing-alerts failure this class exists to close.
    /// </para>
    /// </summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ROROROblox", "metric-rules.json");

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static RuleStatus Inspect(string? path, string metricId)
    {
        var file = path ?? DefaultPath;
        if (!File.Exists(file)) return new RuleStatus(RuleState.NoFile, null, null);

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(file), documentOptions: ReadOptions);
        }
        catch (Exception)
        {
            return new RuleStatus(RuleState.Unreadable, null, null);
        }

        if (root is not JsonArray rules) return new RuleStatus(RuleState.Unreadable, null, null);

        var match = rules.FirstOrDefault(r => MetricIdOf(r) == metricId);
        if (match is null) return new RuleStatus(RuleState.NoRuleForMetric, null, null);

        var owner = StringOf(match, "owner");
        var threshold = DoubleOf(match, "threshold");

        if (owner is null) return new RuleStatus(RuleState.UserOwned, null, threshold);
        if (owner != Owner) return new RuleStatus(RuleState.OwnedByAnotherPlugin, owner, threshold);

        // Ours. Whether it still says what we wrote is the inventory's question, and the window
        // asks it there — drift is reported, never corrected.
        return new RuleStatus(RuleState.OursIntact, owner, threshold);
    }

    /// <summary>The exact JSON object <see cref="AddRule"/> will add. This is the consent: if the
    /// preview and the write can differ, the click was uninformed.</summary>
    public static string Preview(string metricId, double threshold, int windowMinutes) =>
        BuildRule(metricId, threshold, windowMinutes).ToJsonString(WriteOptions);

    /// <summary>
    /// Adds our rule. Returns false, having changed nothing, when there is already a rule for this
    /// metric id or the file cannot be read. Also returns false, touching no file at all, when
    /// <paramref name="metricId"/> is null, empty or whitespace.
    /// </summary>
    public static bool AddRule(string? path, string metricId, double threshold, int windowMinutes)
    {
        if (string.IsNullOrWhiteSpace(metricId)) return false;

        var file = path ?? DefaultPath;

        JsonArray rules;
        if (File.Exists(file))
        {
            var status = Inspect(file, metricId);

            // Covers every case but NoRuleForMetric in one check: Unreadable (almost certainly a
            // hand edit in progress — replacing it would destroy work the user can see and we
            // cannot), and any rule already there for this id — ours, another plugin's, or the
            // user's — left exactly as it is.
            if (status.State != RuleState.NoRuleForMetric) return false;

            rules = JsonNode.Parse(File.ReadAllText(file), documentOptions: ReadOptions) as JsonArray ?? [];

            // Back up before the first byte changes, beside the original so it is findable
            // without knowing where we would have put it.
            //
            // The state immediately before THIS write, not an archive: a later AddRule overwrites
            // it. That is acceptable because every merge here is purely additive — we only ever add
            // one rule and never modify or remove another — so the user's own rules are recoverable
            // from the live file by deleting what we own. The backup exists for the case where that
            // reasoning turns out to be wrong.
            File.Copy(file, file + ".ur-score-backup", overwrite: true);
        }
        else
        {
            rules = [];
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        }

        rules.Add(BuildRule(metricId, threshold, windowMinutes));
        File.WriteAllText(file, rules.ToJsonString(WriteOptions));
        return true;
    }

    /// <summary>
    /// Rate, because the thing being watched is a contribution rate falling off. The field names
    /// are the host parser's own; a rule it cannot read never fires and nothing says so.
    /// </summary>
    private static JsonObject BuildRule(string metricId, double threshold, int windowMinutes) => new()
    {
        ["metricId"] = metricId,
        ["kind"] = "Rate",
        ["threshold"] = threshold,
        ["windowMinutes"] = windowMinutes,
        ["alertWhenBelow"] = true,
        ["owner"] = Owner,
    };

    private static string? MetricIdOf(JsonNode? rule) => StringOf(rule, "metricId");

    private static string? StringOf(JsonNode? rule, string property)
    {
        if (rule is not JsonObject obj) return null;

        // Case-insensitive, because the host's own parser reads this file case-insensitively and a
        // hand-written "MetricId" must not read as a different rule here than it does there.
        foreach (var pair in obj)
        {
            if (!string.Equals(pair.Key, property, StringComparison.OrdinalIgnoreCase)) continue;

            // A hand-edited file can hold anything, and GetValue<string> THROWS on a number — so a
            // single mistyped row ANYWHERE in the file used to take down every call here, while
            // the host tolerated the identical shape row by row and carried on.
            return pair.Value is JsonValue value && value.TryGetValue<string>(out var text)
                ? text
                : null;
        }

        return null;
    }

    private static double? DoubleOf(JsonNode? rule, string property)
    {
        if (rule is not JsonObject obj) return null;

        foreach (var pair in obj)
        {
            if (string.Equals(pair.Key, property, StringComparison.OrdinalIgnoreCase)
                && pair.Value is not null
                && double.TryParse(pair.Value.ToJsonString().Trim('"'), out var value))
            {
                return value;
            }
        }

        return null;
    }
}
