using System.Globalization;
using System.Text.RegularExpressions;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

/// <summary>A stat that gets a card: one you send, or one you no longer send that Ur Score still has an alert for (A11).</summary>
public sealed record AlertStat(string MetricId, string Label, bool Sent);

/// <summary>One alert on a card, as a sentence. <see cref="Managed"/> marks the one Ur Score's Change and Remove act on (A5).</summary>
public sealed record AlertLine(AlertRule Rule, string Sentence, string Mark, bool Managed);

/// <summary>A stat's card: its alerts, the kinds + Add an alert can still offer, and a note (a stale stat, a file problem, unreadable rows).</summary>
public sealed record AlertCard(AlertStat Stat, IReadOnlyList<AlertLine> Alerts, IReadOnlyList<AlertKind> CanAdd, string Note);

/// <summary>Every card, why the rules file can't be used (when it can't), and whether the "Next, in RoRoRo" line shows (A16).</summary>
public sealed record AlertsView(IReadOnlyList<AlertCard> Cards, RulesProblem Problem, bool ShowNext);

/// <summary>What the inline editor holds while you type, bound to its controls. Text, so a half-typed number stays as typed.</summary>
public sealed class AlertDraft
{
    public string Number { get; set; } = "";

    public string Minutes { get; set; } = "";

    public string Direction { get; set; } = AlertCards.Below;
}

public enum AlertEditMode { None, ChoosingKind, Adding, Changing }

/// <summary>The page's one open editor and its one result line (A13). Never written anywhere.</summary>
public sealed record AlertsUi(
    string? MetricId = null, AlertEditMode Mode = AlertEditMode.None, AlertKind Kind = AlertKind.Rate, AlertDraft? Draft = null,
    string Problem = "", string? ResultMetricId = null, string Result = "", bool ResultIsProblem = false)
{
    public static AlertsUi Closed { get; } = new();
}

/// <summary>What a kind, Change or Remove button acts on.</summary>
public sealed record AlertTarget(string MetricId, AlertKind Kind);

/// <summary>One alert as the page draws it.</summary>
public sealed record AlertLineRow(
    string Sentence, string Mark, AlertTarget Target, bool ShowChange, bool ShowRemove, string ChangeName, string RemoveName)
{
    public bool HasMark => Mark.Length > 0;

    public override string ToString() => Sentence;
}

/// <summary>One card as the page draws it: every part's visibility, text and accessible name.</summary>
public sealed record AlertCardRow(
    string MetricId, string Title, string Note, IReadOnlyList<AlertLineRow> Lines, string LinesName, string NoAlerts,
    bool ShowAdd, string AddName,
    bool ShowKinds, bool ShowRateKind, bool ShowLevelKind, AlertTarget RateTarget, AlertTarget LevelTarget, string RateKindName, string LevelKindName,
    bool ShowRateEditor, bool ShowLevelEditor, AlertDraft? Draft, IReadOnlyList<string> MinuteChoices,
    string ConfirmText, string ConfirmName, string CancelName, string NumberName, string MinutesName, string DirectionName,
    string Problem, string Result, bool ResultIsProblem)
{
    public bool HasNote => Note.Length > 0;

    public bool HasNoAlerts => NoAlerts.Length > 0;

    public bool ShowEditor => ShowRateEditor || ShowLevelEditor;

    public bool HasProblem => Problem.Length > 0;

    public bool ShowResult => Result.Length > 0 && !ResultIsProblem;

    public bool ShowResultProblem => Result.Length > 0 && ResultIsProblem;

    public IReadOnlyList<string> DirectionChoices => AlertCards.DirectionChoices;

    public override string ToString() => Title;
}

/// <summary>
/// Setup › Alerts' cards (the alerts card design): every sent stat's alerts as sentences in your words, what + Add an alert
/// offers, what you typed checked before anything is written, the page's one open editor, and where keyboard focus goes.
/// Pure: the page reads the file, calls these, writes through <see cref="RulesFile"/> and draws.
/// </summary>
public static partial class AlertCards
{
    public const double DefaultThreshold = 100;
    public const double DefaultMinutes = 10;
    public const double MaxNumber = 1e15;
    public const string Below = "below";
    public const string Above = "above";

    public const string NextInRoRoRo = "Next, in RoRoRo: Settings › Alerts › turn on Metric alerts and choose where they go (desktop, Discord, phone).";
    public const string NoRecipe = "Import a recipe first.";
    public const string NoSentStat = "Nothing is sent to RoRoRo yet. Tick Send on a stat in Setup › Stats, or a number "
        + "under Clan and field on the same page, and it gets a card here.";
    public const string TypeANumber = "Type a number, like 100.";
    public const string UseADot = "Use a dot for decimals, like 1.5.";
    public const string TwoDecimals = "Use at most two decimal places, like 1.25.";
    public const string AboveZero = "Use a number above 0.";
    public const string TooBig = "Use a number below 1,000,000,000,000,000.";
    public const string TooManyDigits = "Use 15 digits or fewer, counting the decimals.";

    /// <summary>A double shows every decimal digit of a number with at most this many significant digits exactly, so what you typed is what the card says.</summary>
    public const int MaxDigits = 15;
    public const string ChooseMinutes = "Choose how many minutes.";
    public const string ChooseDirection = "Choose above or below.";

    private static readonly AlertKind[] Offered = [AlertKind.Rate, AlertKind.Level];

    public static IReadOnlyList<double> Minutes { get; } = [10, 15, 30];

    public static IReadOnlyList<string> DirectionChoices { get; } = [Below, Above];

    public static AlertsView Empty { get; } = new([], RulesProblem.None, false);

    // ---- cards ----

    /// <summary>
    /// A card per sent metric id in recipe order (A10), then one per metric id Ur Score has a rule for that you no
    /// longer send (A11).
    /// <para>
    /// The clan-and-field numbers a clans list sends get cards too, from 0.5.2. Without them 0.5.0 shipped six
    /// metrics a member could tick and then had nowhere to set an alert on — which was the entire point of them.
    /// </para>
    /// </summary>
    public static AlertsView Build(IReadOnlyList<InstalledRecipe> installed, RulesRead rules)
    {
        var recipes = installed.Where(i => !i.Recipe.IsGroupList).ToList();
        var sent = recipes
            .SelectMany(i => i.State.SentStats(i.Recipe))
            .GroupBy(s => s.MetricId, StringComparer.Ordinal)
            .Select(g => new AlertStat(g.Key, g.First().Label, Sent: true))
            .Concat(installed
                .Where(i => i.Recipe.IsGroupList)
                .SelectMany(i => FieldMetrics.Offered(i.State.FieldMetricKeys))
                .GroupBy(m => m.MetricId, StringComparer.Ordinal)
                .Select(g => new AlertStat(g.Key, g.First().Label, Sent: true)))
            .ToList();
        var sentIds = sent.Select(s => s.MetricId).ToHashSet(StringComparer.Ordinal);
        var stale = rules.Rules
            .Where(r => r.Owner == RuleOwner.UrScore && !sentIds.Contains(r.MetricId))
            .GroupBy(r => r.MetricId, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new AlertStat(
                g.Key,
                PinnedLabel(recipes, g.Key)
                    ?? FieldMetrics.All.FirstOrDefault(m => string.Equals(m.MetricId, g.Key, StringComparison.Ordinal))?.Label
                    ?? g.Select(r => r.Label).FirstOrDefault(l => l is not null)
                    ?? g.Key,
                Sent: false));

        var cards = sent.Concat(stale).Select(stat => Card(stat, rules)).ToList();
        var showNext = rules.Problem == RulesProblem.None && cards.Any(c => c.Stat.Sent && c.Alerts.Count > 0);
        return new AlertsView(cards, rules.Problem, showNext);
    }

    /// <summary>
    /// Whether two views say the same thing, so a refresh that changes nothing doesn't redraw (A14). A rule's place in the file
    /// is left out: a rule added or removed higher up shifts every index below it and changes nothing a card says, and a redraw
    /// would drop keyboard focus. Nothing acts on a view's index: every write finds Ur Score's rule in the file again.
    /// </summary>
    public static bool Same(AlertsView a, AlertsView b) =>
        a.Problem == b.Problem && a.ShowNext == b.ShowNext && a.Cards.Count == b.Cards.Count
        && a.Cards.Zip(b.Cards).All(pair =>
            pair.First.Stat == pair.Second.Stat && pair.First.Note == pair.Second.Note
            && pair.First.CanAdd.SequenceEqual(pair.Second.CanAdd)
            && pair.First.Alerts.Select(Placeless).SequenceEqual(pair.Second.Alerts.Select(Placeless)));

    private static AlertLine Placeless(AlertLine line) => line with { Rule = line.Rule with { Index = 0 } };

    public static AlertCard? CardFor(AlertsView view, string metricId) =>
        view.Cards.FirstOrDefault(c => string.Equals(c.Stat.MetricId, metricId, StringComparison.Ordinal));

    /// <summary>The alert a Change or Remove button stands for: Ur Score's managed rule of that kind on that card.</summary>
    public static AlertLine? Managed(AlertsView view, AlertTarget target) =>
        CardFor(view, target.MetricId)?.Alerts.FirstOrDefault(a => a.Managed && a.Rule.Kind == target.Kind);

    private static AlertCard Card(AlertStat stat, RulesRead rules)
    {
        var managed = new HashSet<AlertKind>();
        var lines = new List<AlertLine>();
        foreach (var rule in rules.For(stat.MetricId))
        {
            var isManaged = rule.Owner == RuleOwner.UrScore && managed.Add(rule.Kind);
            var mark = rule.Owner switch
            {
                RuleOwner.You => "yours",
                RuleOwner.AnotherPlugin => "another plugin's",
                _ => isManaged ? "" : "a copy",
            };

            // The stat's label from the recipe, whatever the rule's own label says (A4).
            lines.Add(new AlertLine(rule, Sentence(rule.Kind, stat.Label, rule.Threshold, rule.WindowMinutes, rule.AlertWhenBelow), mark, isManaged));
        }

        IReadOnlyList<AlertKind> canAdd = stat.Sent && rules.Problem == RulesProblem.None ? [.. Offered.Where(k => !managed.Contains(k))] : [];
        return new AlertCard(stat, lines, canAdd, Note(stat, rules));
    }

    private static string Note(AlertStat stat, RulesRead rules)
    {
        if (rules.Problem != RulesProblem.None) return ProblemNote(rules.Problem);

        var parts = new List<string>();
        if (!stat.Sent) parts.Add($"You don't send {stat.Label} any more, so its alerts can't fire. Tick Send in Setup › Stats, or remove them.");

        var skipped = rules.SkippedFor(stat.MetricId);
        if (skipped == 1) parts.Add($"1 rule for {stat.Label} is written in a way RoRoRo can't read, so it never alerts. Ur Score leaves it as it is.");
        if (skipped > 1) parts.Add($"{skipped} rules for {stat.Label} are written in a way RoRoRo can't read, so they never alert. Ur Score leaves them as they are.");

        return string.Join(" ", parts);
    }

    /// <summary>The label of a recipe stat pinned to this metric id, sent or not. A state file can hold a null metric id.</summary>
    private static string? PinnedLabel(IEnumerable<InstalledRecipe> recipes, string metricId) =>
        recipes
            .SelectMany(i => i.State.StatChoices
                .Where(c => string.Equals(c.Value.MetricId?.Trim(), metricId, StringComparison.Ordinal))
                .Select(c => RecipeStats.Find(i.Recipe, c.Key)?.Label))
            .FirstOrDefault(label => label is not null);

    // ---- words ----

    /// <summary>
    /// A number as a sentence shows it. RoRoRo reads a hand-typed threshold like 1e400 as infinity and keeps the rule, so a
    /// number that isn't finite is said in words rather than as a symbol.
    /// </summary>
    public static string Number(double value) =>
        double.IsFinite(value) ? value.ToString(Math.Abs(value) >= MaxNumber ? "R" : "#,0.##", CultureInfo.InvariantCulture)
        : value < 0 ? "a negative number too big to show"
        : "a number too big to show";

    /// <summary>A number as the number box shows it. Empty when it isn't finite: no box can hold it, so such an alert isn't changed.</summary>
    public static string Editable(double value) => double.IsFinite(value)
        ? value.ToString(Math.Abs(value) >= MaxNumber ? "R" : "0.##", CultureInfo.InvariantCulture) : "";

    /// <summary>
    /// The part after "Alert me when", also used by the result lines.
    /// <para>
    /// The subject is "an account's" for a stat and "your clan's" for a clan-and-field number, which belongs to no
    /// account and is reported without one. Until 0.5.3 every sentence said "an account's", so all six of the
    /// clan numbers described themselves as something they are not.
    /// </para>
    /// </summary>
    public static string Condition(
        AlertKind kind, string label, double threshold, double windowMinutes, bool below, string? metricId = null)
    {
        var whose = Whose(metricId);
        return kind switch
        {
            AlertKind.Rate => $"{whose} {label} gains fewer than {Number(threshold)} a minute for {Number(windowMinutes)} {(windowMinutes == 1 ? "minute" : "minutes")}",
            AlertKind.Level => $"{whose} {label} goes {(below ? Below : Above)} {Number(threshold)}",
            _ => $"{whose} {label} changes",
        };
    }

    /// <summary>Whose number this is. A clan-and-field id is nobody's account, and never was.</summary>
    private static string Whose(string? metricId) =>
        metricId is not null && FieldMetrics.All.Any(m => string.Equals(m.MetricId, metricId, StringComparison.Ordinal))
            ? "your clan's"
            : "an account's";

    public static string Sentence(
        AlertKind kind, string label, double threshold, double windowMinutes, bool below, string? metricId = null) =>
        $"Alert me when {Condition(kind, label, threshold, windowMinutes, below, metricId)}.";

    public static string ProblemNote(RulesProblem problem) => problem switch
    {
        RulesProblem.CantOpen => "RoRoRo's rules file is locked or can't be opened, so Ur Score can't show or change alerts right now. Try again in a moment.",
        RulesProblem.NotJson => "RoRoRo's rules file has a mistake in it, so Ur Score can't show or change alerts. Fix it by hand, then come back.",
        RulesProblem.NotAList => "RoRoRo's rules file isn't a list of rules, so Ur Score can't show or change alerts. Fix it by hand, then come back.",
        _ => "",
    };

    public static string Failed(RuleWrite outcome, string label) => outcome switch
    {
        RuleWrite.AlreadyThere => $"Ur Score already has an alert of this kind for {label}, so nothing was added. Change that one instead.",
        RuleWrite.NotThere => "That alert isn't in RoRoRo's rules file any more, so nothing was changed.",
        RuleWrite.CantOpen => "RoRoRo's rules file is locked or can't be opened, so nothing was changed. Try again in a moment.",
        RuleWrite.CantWrite => "Ur Score couldn't save RoRoRo's rules file. It may be locked by another program. Nothing was changed; try again in a moment.",
        RuleWrite.NotJson => "RoRoRo's rules file has a mistake in it, so Ur Score won't change it. Nothing was changed. Fix it by hand, then come back.",
        RuleWrite.NotAList => "RoRoRo's rules file isn't a list of rules, so Ur Score won't change it. Nothing was changed.",
        _ => "",
    };

    private static string KindWords(AlertKind kind) => kind switch
    {
        AlertKind.Rate => "stops climbing",
        AlertKind.Level => "crosses a number",
        _ => "changes",
    };

    // ---- accessible names (A18) ----

    public static string AddName(string label) => $"Add an alert for {label}";

    public static string KindName(AlertKind kind, string label)
    {
        var words = KindWords(kind);
        return $"{char.ToUpperInvariant(words[0])}{words[1..]}, for {label}";
    }

    public static string ChangeName(AlertKind kind, string label) => $"Change the {KindWords(kind)} alert for {label}";

    public static string RemoveName(AlertKind kind, string label) => $"Remove the {KindWords(kind)} alert for {label}";

    public static string NumberName(string label) => $"Number for {label}";

    public static string MinutesName(string label) => $"Minutes for {label}";

    public static string DirectionName(string label) => $"Above or below for {label}";

    public static string CancelName(string label) => $"Cancel, for {label}";

    // ---- what you type (A12) ----

    public static AlertDraft NewDraft(AlertKind kind) => kind == AlertKind.Rate
        ? new AlertDraft { Number = Editable(DefaultThreshold), Minutes = Editable(DefaultMinutes) }
        : new AlertDraft();

    public static AlertDraft DraftOf(AlertRule rule) => new()
    {
        Number = Editable(rule.Threshold),
        Minutes = Editable(rule.WindowMinutes > 0 ? rule.WindowMinutes : DefaultMinutes),
        Direction = rule.AlertWhenBelow ? Below : Above,
    };

    /// <summary>
    /// 10, 15 and 30, and nothing else, whatever <paramref name="current"/> holds (controller ruling, Task 2 review, replacing
    /// A12's extra choice). A rule whose window is another number reads as-is in its sentence; its Change opens with no minutes
    /// chosen, says why (<see cref="OtherMinutes"/>), and Save asks you to choose one of these. The parameter stays so the
    /// contract's signature does.
    /// </summary>
    public static IReadOnlyList<string> MinuteChoices(string? current) => [.. Minutes.Select(Editable)];

    /// <summary>
    /// Why the minutes box is empty on a rule whose own minutes it doesn't offer, said as Change opens and again if Save is
    /// pressed before one is chosen (backlog AC-2.13).
    /// </summary>
    public static string OtherMinutes(string minutes)
    {
        var choices = MinuteChoices(null);
        return $"Ur Score offers {string.Join(", ", choices.Take(choices.Count - 1))} or {choices[^1]} minutes, and this alert uses {minutes}. "
               + "Choose one to save a change, or Cancel to leave the alert as it is.";
    }

    /// <summary>Minutes a rule of its own could hold and the box doesn't offer: a finite number above 0 that isn't one of the choices.</summary>
    private static bool IsOtherMinutes(string? minutes) =>
        double.TryParse(minutes?.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
        && double.IsFinite(value) && value > 0
        && !MinuteChoices(null).Contains(minutes!.Trim(), StringComparer.Ordinal);

    [GeneratedRegex(@"^([0-9]{1,3}(,[0-9]{3})+|[0-9]+)(\.[0-9]{1,2})?$")]
    private static partial Regex PlainNumber();

    [GeneratedRegex(@"^[0-9]+,[0-9]{1,2}$")]
    private static partial Regex CommaDecimal();

    [GeneratedRegex(@"^([0-9]{1,3}(,[0-9]{3})+|[0-9]+)\.[0-9]{3,}$")]
    private static partial Regex LongDecimal();

    /// <summary>
    /// "" and the number, or why it can't be used. Digits only (ASCII), commas in threes, a dot and up to two decimals, and at
    /// most <see cref="MaxDigits"/> significant digits: a double formats at 15, so "999999999999999.9" would otherwise be accepted
    /// and shown as "1,000,000,000,000,000", the very limit <see cref="TooBig"/> names.
    /// </summary>
    public static string ParseNumber(string? text, AlertKind kind, out double value)
    {
        value = 0;
        var typed = (text ?? "").Trim();
        if (typed.Contains('e', StringComparison.OrdinalIgnoreCase)
            && double.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out var exponent)
            && exponent >= MaxNumber) return TooBig;
        if (CommaDecimal().IsMatch(typed)) return UseADot;
        if (LongDecimal().IsMatch(typed)) return TwoDecimals;
        if (!PlainNumber().IsMatch(typed)) return TypeANumber;

        var plain = typed.Replace(",", "", StringComparison.Ordinal);
        var number = double.Parse(plain, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        if (number >= MaxNumber) return TooBig;
        if (plain.Replace(".", "", StringComparison.Ordinal).Trim('0').Length > MaxDigits) return TooManyDigits;
        if (kind == AlertKind.Rate && number <= 0) return AboveZero;

        value = number;
        return "";
    }

    /// <summary>The rule Turn on or Save writes, or why it can't be written yet. Nothing is written by this.</summary>
    public static (AlertSpec? Spec, string Problem) Check(AlertKind kind, AlertDraft draft, string label)
    {
        var problem = ParseNumber(draft.Number, kind, out var threshold);
        if (problem.Length > 0) return (null, problem);

        if (kind == AlertKind.Rate)
        {
            // Only a choice the box offers, matched as text: "Infinity", "NaN", 0 or a rule's own 20 never reach the file.
            var chosen = Minutes.Where(m => string.Equals(Editable(m), draft.Minutes?.Trim(), StringComparison.Ordinal)).ToList();
            if (chosen.Count == 1) return (new AlertSpec(kind, threshold, chosen[0], AlertWhenBelow: true, label), "");
            return (null, IsOtherMinutes(draft.Minutes) ? OtherMinutes(draft.Minutes!.Trim()) : ChooseMinutes);
        }

        return draft.Direction is Below or Above
            ? (new AlertSpec(kind, threshold, 0, draft.Direction == Below, label), "")
            : (null, ChooseDirection);
    }

    // ---- the editor's steps (A13) ----

    public static AlertsUi OpenAdd(string metricId) => new(metricId, AlertEditMode.ChoosingKind);

    public static AlertsUi ChooseKind(AlertTarget target) => new(target.MetricId, AlertEditMode.Adding, target.Kind, NewDraft(target.Kind));

    /// <summary>
    /// Change opens on the rule's own values. A stops-climbing rule whose minutes the box doesn't offer opens saying why its
    /// minutes box is empty (backlog AC-2.13), rather than leaving that for Save to find.
    /// </summary>
    public static AlertsUi OpenChange(AlertLine line)
    {
        var draft = DraftOf(line.Rule);
        var problem = Math.Abs(line.Rule.Threshold) >= MaxNumber ? ParseNumber(draft.Number, line.Rule.Kind, out _)
            : line.Rule.Kind == AlertKind.Rate && IsOtherMinutes(draft.Minutes) ? OtherMinutes(draft.Minutes) : "";
        return new(line.Rule.MetricId, AlertEditMode.Changing, line.Rule.Kind, draft, Problem: problem);
    }

    /// <summary>After Turn on or Save wrote (or didn't): the result on the card; a file that can't be opened or written keeps the editor open.</summary>
    public static AlertsUi AfterWrite(AlertsUi ui, RuleWrite outcome, AlertSpec spec)
    {
        var condition = Condition(spec.Kind, spec.Label, spec.Threshold, spec.WindowMinutes, spec.AlertWhenBelow, ui.MetricId);
        return outcome switch
        {
            RuleWrite.Done when ui.Mode == AlertEditMode.Changing => Said(ui.MetricId, $"Changed. RoRoRo will now alert you when {condition}.", false),
            RuleWrite.Done => Said(ui.MetricId, $"On. RoRoRo will alert you when {condition}.", false),
            RuleWrite.CantOpen or RuleWrite.CantWrite => ui with { Problem = Failed(outcome, spec.Label) },
            _ => Said(ui.MetricId, Failed(outcome, spec.Label), true),
        };
    }

    public static AlertsUi AfterRemove(AlertTarget target, AlertLine line, string label, RuleWrite outcome)
    {
        var rule = line.Rule;
        return outcome == RuleWrite.Done
            ? Said(target.MetricId, $"Removed. RoRoRo won't alert you when {Condition(rule.Kind, label, rule.Threshold, rule.WindowMinutes, rule.AlertWhenBelow, target.MetricId)} any more.", false)
            : Said(target.MetricId, Failed(outcome, label), true);
    }

    /// <summary>
    /// What a Change or Remove click says when the file, read again at the click, no longer holds the alert the card was drawn
    /// with: a hand edit removed it or gave it another owner since. The card says so, in the theme, and nothing is written
    /// (Task 3 review, Minor 1). <see cref="Managed"/> against that fresh view is what picks the alert, and null is this.
    /// </summary>
    public static AlertsUi Gone(AlertTarget target) => Said(target.MetricId, Failed(RuleWrite.NotThere, ""), true);

    private static AlertsUi Said(string? metricId, string result, bool problem) => new(ResultMetricId: metricId, Result: result, ResultIsProblem: problem);

    // ---- rows ----

    public static IReadOnlyList<AlertCardRow> Rows(AlertsView view, AlertsUi ui) => [.. view.Cards.Select(card => Row(card, ui))];

    private static AlertCardRow Row(AlertCard card, AlertsUi ui)
    {
        var id = card.Stat.MetricId;
        var label = card.Stat.Label;
        var mode = string.Equals(ui.MetricId, id, StringComparison.Ordinal) ? ui.Mode : AlertEditMode.None;
        var editing = mode is AlertEditMode.Adding or AlertEditMode.Changing;

        var lines = card.Alerts.Select(a =>
        {
            var underChange = mode == AlertEditMode.Changing && a.Managed && a.Rule.Kind == ui.Kind;
            return new AlertLineRow(
                a.Sentence, a.Mark, new AlertTarget(id, a.Rule.Kind),
                ShowChange: a.Managed && CanChange(card, a) && !underChange,
                ShowRemove: a.Managed && !underChange,
                ChangeName(a.Rule.Kind, label), RemoveName(a.Rule.Kind, label));
        }).ToList();

        return new AlertCardRow(
            MetricId: id, Title: label, Note: card.Note, Lines: lines, LinesName: $"Alerts for {label}",
            NoAlerts: card.Alerts.Count == 0 && card.Note.Length == 0 ? "No alerts yet." : "",
            ShowAdd: mode == AlertEditMode.None && card.CanAdd.Count > 0, AddName: AddName(label),
            ShowKinds: mode == AlertEditMode.ChoosingKind,
            ShowRateKind: mode == AlertEditMode.ChoosingKind && card.CanAdd.Contains(AlertKind.Rate),
            ShowLevelKind: mode == AlertEditMode.ChoosingKind && card.CanAdd.Contains(AlertKind.Level),
            RateTarget: new AlertTarget(id, AlertKind.Rate), LevelTarget: new AlertTarget(id, AlertKind.Level),
            RateKindName: KindName(AlertKind.Rate, label), LevelKindName: KindName(AlertKind.Level, label),
            ShowRateEditor: editing && ui.Kind == AlertKind.Rate, ShowLevelEditor: editing && ui.Kind == AlertKind.Level,
            Draft: editing ? ui.Draft : null, MinuteChoices: MinuteChoices(editing ? ui.Draft?.Minutes : null),
            ConfirmText: mode == AlertEditMode.Changing ? "Save" : "Turn on",
            ConfirmName: mode == AlertEditMode.Changing ? $"Save the alert for {label}" : $"Turn on the alert for {label}",
            CancelName: CancelName(label), NumberName: NumberName(label), MinutesName: MinutesName(label), DirectionName: DirectionName(label),
            Problem: editing ? ui.Problem : "",
            Result: string.Equals(ui.ResultMetricId, id, StringComparison.Ordinal) ? ui.Result : "",
            ResultIsProblem: ui.ResultIsProblem);
    }

    // ---- focus (A15) ----

    /// <summary>The accessible name of the control keyboard focus goes to after the page redraws, or "" to leave it.</summary>
    public static string FocusName(AlertsView view, string metricId, AlertsUi ui, AlertKind kind)
    {
        if (CardFor(view, metricId) is not { } card) return "";
        var label = card.Stat.Label;

        if (string.Equals(ui.MetricId, metricId, StringComparison.Ordinal) && ui.Mode != AlertEditMode.None)
        {
            if (ui.Mode != AlertEditMode.ChoosingKind) return NumberName(label);
            return card.CanAdd.Count > 0 ? KindName(card.CanAdd[0], label) : CancelName(label);
        }

        if (card.Alerts.FirstOrDefault(a => a.Managed && a.Rule.Kind == kind) is { } acted && CanChange(card, acted)) return ChangeName(kind, label);
        if (card.CanAdd.Count > 0) return AddName(label);
        return card.Alerts.FirstOrDefault(a => a.Managed) is { } first
            ? CanChange(card, first) ? ChangeName(first.Rule.Kind, label) : RemoveName(first.Rule.Kind, label)
            : "";
    }

    /// <summary>Cancel goes back to the button that opened the editor: + Add an alert, or that alert's Change.</summary>
    public static string FocusAfterCancel(AlertsView view, AlertsUi open)
    {
        if (open.MetricId is not { } id || CardFor(view, id) is not { } card) return "";
        return open.Mode != AlertEditMode.Changing && card.CanAdd.Count > 0
            ? AddName(card.Stat.Label)
            : FocusName(view, id, AlertsUi.Closed, open.Kind);
    }

    /// <summary>
    /// Change is offered for a stat you send, on a kind Ur Score writes, with a number the box can hold: a threshold that isn't
    /// finite (hand-typed, like 1e400) can only be removed.
    /// </summary>
    private static bool CanChange(AlertCard card, AlertLine line) =>
        card.Stat.Sent && line.Rule.Kind != AlertKind.Event && double.IsFinite(line.Rule.Threshold);

    // ---- lines around the cards ----

    public static string EmptyLine(IReadOnlyList<InstalledRecipe> installed, AlertsView view) =>
        installed.Count == 0 ? NoRecipe : view.Cards.Count == 0 ? NoSentStat : "";

    /// <summary>A result whose card went away (the last alert of a stat you no longer send was removed) shows under the cards (A13).</summary>
    public static string OrphanResult(AlertsView view, AlertsUi ui) =>
        ui.ResultMetricId is { } id && ui.Result.Length > 0 && CardFor(view, id) is null ? ui.Result : "";

    /// <summary>The Stats table's line for a sent stat (A17).</summary>
    public static string StatLine(RulesRead rules, string metricId)
    {
        if (rules.Problem != RulesProblem.None) return "RoRoRo's rules file can't be read right now. Setup › Alerts says why.";

        return rules.For(metricId).Count switch
        {
            0 => "No alert yet. Add one in Setup › Alerts.",
            1 => "1 alert in RoRoRo. See it in Setup › Alerts.",
            var n => $"{n} alerts in RoRoRo. See them in Setup › Alerts.",
        };
    }
}
