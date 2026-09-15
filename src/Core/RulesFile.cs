using System.IO;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

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

/// <summary>The kinds RoRoRo judges (its <c>MetricRuleKind</c>). Ur Score writes Rate and Level; Event is only ever read (A6).</summary>
public enum AlertKind { Rate, Level, Event }

/// <summary>Whose a rule is, by its <c>owner</c> field (A3).</summary>
public enum RuleOwner { UrScore, You, AnotherPlugin }

/// <summary>Why the rules file can't be used. None when it was read, or when there is no file.</summary>
public enum RulesProblem { None, CantOpen, NotJson, NotAList }

/// <summary>What a write did. Anything but Done changed nothing, and so did a Done that had nothing to change.</summary>
public enum RuleWrite { Done, AlreadyThere, NotThere, CantOpen, NotJson, NotAList, CantWrite }

/// <summary>One rule RoRoRo can read, at its <see cref="Index"/> in the file's list. A missing number reads as 0 and a missing direction as below, as RoRoRo reads them.</summary>
public sealed record AlertRule(
    int Index, string MetricId, AlertKind Kind, double Threshold, double WindowMinutes, bool AlertWhenBelow, RuleOwner Owner, string? Label);

/// <summary>What one Ur Score rule says: its kind, number, minutes (Rate) or direction (Level), under the stat's label.</summary>
public sealed record AlertSpec(AlertKind Kind, double Threshold, double WindowMinutes, bool AlertWhenBelow, string Label);

/// <summary>The rules file as read: its problem, whether it exists, every rule RoRoRo can read, and the metric ids of rows it would skip (A2).</summary>
public sealed record RulesRead(RulesProblem Problem, bool Exists, IReadOnlyList<AlertRule> Rules, IReadOnlyList<string> SkippedMetricIds)
{
    public static RulesRead NoFile { get; } = new(RulesProblem.None, false, [], []);

    public static RulesRead Unusable(RulesProblem problem) => new(problem, true, [], []);

    public IReadOnlyList<AlertRule> For(string metricId) =>
        [.. Rules.Where(r => string.Equals(r.MetricId, metricId, StringComparison.Ordinal))];

    public int SkippedFor(string metricId) =>
        SkippedMetricIds.Count(id => string.Equals(id, metricId, StringComparison.Ordinal));

    /// <summary>The Ur Score rule Change and Remove act on: the first of that kind for the metric, in file order (A5).</summary>
    public AlertRule? OursFor(string metricId, AlertKind kind) =>
        Rules.FirstOrDefault(r => r.Owner == RuleOwner.UrScore && r.Kind == kind && string.Equals(r.MetricId, metricId, StringComparison.Ordinal));
}

/// <summary>
/// Reads RoRoRo's <c>metric-rules.json</c>, and turns on, changes and removes Ur Score's own rules in it, on an explicit click only.
/// <para>
/// The metric id is a silent-failure seam: it must match a rule in that file or nothing can ever alert, and a mismatch is
/// quiet on both sides. Leaving that to hand-editing JSON in another app's folder is a trap for a common Windows user, so
/// Setup › Alerts does it in your words.
/// </para>
/// <para>
/// EVERY WRITE IS FENCED. Explicit call only, never on startup. A rule is read exactly as RoRoRo's parser reads it, and a
/// row it would skip costs only itself. Only rules owned by <see cref="Owner"/> are ever changed or removed, and every other
/// rule keeps every field. The file is backed up before each write and replaced through a temporary file. A file that can't
/// be opened, isn't valid JSON or isn't a list is never written.
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

    /// <summary>The environment variable the Setup › Alerts walk sets to point Ur Score at a scratch rules file (A1).</summary>
    public const string PathVariable = "UR_SCORE_RULES_FILE";

    /// <summary>Beside the rules file: the file as it was just before Ur Score's latest write (A8).</summary>
    public const string BackupSuffix = ".ur-score-backup";

    private const string TempSuffix = ".ur-score-writing";

    /// <summary>Relaxed escaping keeps labels readable in a file people edit by hand; it is never shown in a browser.</summary>
    private static readonly JsonSerializerOptions EditOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>A fully qualified override (the walk's scratch file), else RoRoRo's own file (A1).</summary>
    public static string ResolvePath(string? overridePath) =>
        !string.IsNullOrWhiteSpace(overridePath) && Path.IsPathFullyQualified(overridePath.Trim())
            ? overridePath.Trim()
            : DefaultPath;

    /// <summary>Every rule RoRoRo can read in the file at <paramref name="path"/>. Never throws for a file problem: it says which.</summary>
    public static RulesRead Read(string path) => File.Exists(path) ? Load(path).Read : RulesRead.NoFile;

    /// <summary>Adds Ur Score's rule of <paramref name="spec"/>'s kind for a metric, at the end. AlreadyThere when Ur Score has one (A5).</summary>
    public static RuleWrite TurnOn(string path, string metricId, AlertSpec spec)
    {
        Guard(metricId, spec.Kind);
        return Write(path, (rules, read) =>
        {
            if (read.OursFor(metricId, spec.Kind) is not null) return (RuleWrite.AlreadyThere, false);
            rules.Add(Build(metricId, spec));
            return (RuleWrite.Done, true);
        });
    }

    /// <summary>
    /// Rewrites Ur Score's rule of that kind in place, label included (A4, A7). NotThere when Ur Score has none. Done with no
    /// write when the rule already says exactly this: a rewrite would still drop the file's comments and formatting and
    /// replace the backup with the same rules.
    /// </summary>
    public static RuleWrite Change(string path, string metricId, AlertSpec spec)
    {
        Guard(metricId, spec.Kind);
        return Write(path, (rules, read) =>
        {
            if (read.OursFor(metricId, spec.Kind) is not { } ours) return (RuleWrite.NotThere, false);
            var rule = Build(metricId, spec);
            if (Unchanged(rules[ours.Index], rule)) return (RuleWrite.Done, false);
            rules[ours.Index] = rule;
            return (RuleWrite.Done, true);
        });
    }

    /// <summary>Deletes exactly the one Ur Score rule of that kind for the metric (A5). NotThere when Ur Score has none.</summary>
    public static RuleWrite Remove(string path, string metricId, AlertKind kind)
    {
        if (string.IsNullOrWhiteSpace(metricId)) throw new ArgumentException("A rule needs a metric id.", nameof(metricId));
        return Write(path, (rules, read) =>
        {
            if (read.OursFor(metricId, kind) is not { } ours) return (RuleWrite.NotThere, false);
            rules.RemoveAt(ours.Index);
            return (RuleWrite.Done, true);
        });
    }

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

    private static void Guard(string metricId, AlertKind kind)
    {
        if (string.IsNullOrWhiteSpace(metricId)) throw new ArgumentException("A rule needs a metric id.", nameof(metricId));
        if (kind == AlertKind.Event) throw new ArgumentException("Ur Score writes Rate and Level rules only.", nameof(kind));
    }

    /// <summary>The file's list and its rules as read, or why there isn't one (with no list).</summary>
    private static (JsonArray? Rules, RulesRead Read) Load(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, RulesRead.Unusable(RulesProblem.CantOpen));
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text, documentOptions: ReadOptions);
        }
        catch (JsonException)
        {
            return (null, RulesRead.Unusable(RulesProblem.NotJson));
        }

        return root is JsonArray rules ? (rules, RowsOf(text)) : (null, RulesRead.Unusable(RulesProblem.NotAList));
    }

    /// <summary>
    /// Each row as RoRoRo's parser takes it (A2): a row it would skip is left out, and counted when its metric id is readable.
    /// <para>
    /// Rows are read from a <see cref="JsonDocument"/> of the same text, not from the <see cref="JsonArray"/> a write edits:
    /// a row with a name written twice can't be listed as a <see cref="JsonObject"/>, while RoRoRo reads it (the last one
    /// wins). The indices agree because both are the same list.
    /// </para>
    /// </summary>
    private static RulesRead RowsOf(string text)
    {
        var rules = new List<AlertRule>();
        var skipped = new List<string>();

        using var document = JsonDocument.Parse(text, ReadOptions);
        var index = -1;
        foreach (var row in document.RootElement.EnumerateArray())
        {
            index++;
            if (row.ValueKind != JsonValueKind.Object) continue;

            RuleRow? parsed;
            try
            {
                parsed = row.Deserialize<RuleRow>(RowOptions);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
            {
                if (LastText(row, "metricId") is { } unread && !string.IsNullOrWhiteSpace(unread)) skipped.Add(unread);
                continue;
            }

            if (parsed?.MetricId is not { } metricId || string.IsNullOrWhiteSpace(metricId)) continue;

            if (RuleAt(index, metricId, parsed, row) is { } rule) rules.Add(rule);
            else skipped.Add(metricId);
        }

        return new RulesRead(RulesProblem.None, true, rules, skipped);
    }

    private static AlertRule? RuleAt(int index, string metricId, RuleRow parsed, JsonElement row)
    {
        if (!Enum.TryParse<AlertKind>(parsed.Kind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind)) return null;

        // RoRoRo turns the window into a TimeSpan and skips a row whose window can't be one.
        try
        {
            _ = TimeSpan.FromMinutes(parsed.WindowMinutes);
        }
        catch (Exception ex) when (ex is OverflowException or ArgumentException)
        {
            return null;
        }

        var owner = LastText(row, "owner");
        var whose = string.IsNullOrWhiteSpace(owner) ? RuleOwner.You
            : string.Equals(owner, Owner, StringComparison.Ordinal) ? RuleOwner.UrScore
            : RuleOwner.AnotherPlugin;
        var label = parsed.Label?.Trim();

        return new AlertRule(
            index, metricId, kind, parsed.Threshold, parsed.WindowMinutes, parsed.AlertWhenBelow, whose, string.IsNullOrEmpty(label) ? null : label);
    }

    /// <summary>
    /// A field RoRoRo doesn't read (the owner), or the metric id of a row it can't: by name ignoring case, the last one
    /// written winning, as RoRoRo's parser takes its own fields. Null when that one isn't text.
    /// </summary>
    private static string? LastText(JsonElement row, string name)
    {
        string? text = null;
        foreach (var field in row.EnumerateObject())
        {
            if (!string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            text = field.Value.ValueKind == JsonValueKind.String ? field.Value.GetString() : null;
        }

        return text;
    }

    /// <summary>RoRoRo's own parser options (<c>LocalFileMetricRuleSource.Options</c>), so a row reads here exactly as it reads there.</summary>
    private static readonly JsonSerializerOptions RowOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// RoRoRo's row (<c>LocalFileMetricRuleSource.RuleRow</c>), field for field. Deserialising it the way RoRoRo does is what
    /// makes "a rule is what RoRoRo reads" true by construction: a field of the wrong type anywhere in the row (a label that
    /// is a number, a quoted or null number) skips the row, and a name written twice in any letter case takes the last one.
    /// The owner is left out on purpose: RoRoRo ignores it, so an owner of the wrong type must not cost the row (A3).
    /// </summary>
    private sealed class RuleRow
    {
        [JsonPropertyName("metricId")] public string? MetricId { get; set; }
        [JsonPropertyName("kind")] public string? Kind { get; set; }
        [JsonPropertyName("threshold")] public double Threshold { get; set; }
        [JsonPropertyName("windowMinutes")] public double WindowMinutes { get; set; }
        [JsonPropertyName("alertWhenBelow")] public bool AlertWhenBelow { get; set; } = true;
        [JsonPropertyName("label")] public string? Label { get; set; }
    }

    /// <summary>
    /// Loads the list, lets <paramref name="edit"/> change it, and writes it back only when the edit says Done and that it changed
    /// the list (A8): a copy of the file as it was goes beside it first, then the text goes to a temporary file that replaces
    /// the rules file in one move.
    /// </summary>
    private static RuleWrite Write(string path, Func<JsonArray, RulesRead, (RuleWrite Outcome, bool Changed)> edit)
    {
        var exists = File.Exists(path);
        JsonArray rules = [];
        var read = RulesRead.NoFile;

        if (exists)
        {
            (var loaded, read) = Load(path);
            if (loaded is null)
            {
                return read.Problem switch
                {
                    RulesProblem.CantOpen => RuleWrite.CantOpen,
                    RulesProblem.NotAList => RuleWrite.NotAList,
                    _ => RuleWrite.NotJson,
                };
            }

            rules = loaded;
        }

        var (outcome, changed) = edit(rules, read);
        if (outcome != RuleWrite.Done || !changed) return outcome;

        string text;
        try
        {
            text = rules.ToJsonString(EditOptions);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return RuleWrite.NotJson;
        }

        var temp = path + TempSuffix;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (exists) File.Copy(path, path + BackupSuffix, overwrite: true);
            File.WriteAllText(temp, text);
            File.Move(temp, path, overwrite: true);
            return RuleWrite.Done;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // A temporary file left behind is harmless: nothing reads it, and the next write replaces it.
            }

            return RuleWrite.CantWrite;
        }
    }

    /// <summary>Whether the row in the file is already exactly the rule a Change would write.</summary>
    private static bool Unchanged(JsonNode? row, JsonObject rule)
    {
        try
        {
            return JsonNode.DeepEquals(row, rule);
        }
        catch (ArgumentException)
        {
            // A row with a name written twice can't be compared as an object. Rewriting it is what makes it one.
            return false;
        }
    }

    /// <summary>A rule in the host parser's own field names and JSON types (A7). A rule it can't read never fires, and nothing says so.</summary>
    private static JsonObject Build(string metricId, AlertSpec spec)
    {
        var rule = new JsonObject
        {
            ["metricId"] = metricId,
            ["kind"] = spec.Kind.ToString(),
            ["threshold"] = spec.Threshold,
        };
        if (spec.Kind == AlertKind.Rate) rule["windowMinutes"] = spec.WindowMinutes;
        rule["alertWhenBelow"] = spec.Kind == AlertKind.Rate || spec.AlertWhenBelow;
        rule["owner"] = Owner;
        if (!string.IsNullOrWhiteSpace(spec.Label)) rule["label"] = spec.Label.Trim();
        return rule;
    }
}
