using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

using Source = Labs626.UrScore.Core.Source;

/// <summary>One sent stat in the Rule for list. Its text is its string, so a screen reader names the pick.</summary>
public sealed record RuleChoice(string MetricId, string Text)
{
    public override string ToString() => Text;
}

public sealed record PolicyItem(string RecipeName, string Line, string Counts);

/// <summary>
/// Setup › Alerts (spec §7.5): the rule card and the report policy card, moved from the retired main window
/// with their behaviour unchanged. The policy line is <see cref="ReportPolicy.Describe"/>'s own sentence.
/// </summary>
public static class AlertsModel
{
    public const double DefaultThreshold = 100;

    public const int DefaultWindowMinutes = 10;

    public const string MetricAlertsOff =
        "RoRoRo's metric alerts are off by default. A rule is not enough on its own — turn Metric alerts on in RoRoRo's Settings too, or a perfectly matching rule will still never ring your phone.";

    public const string NoSentStat = "No stat is set to send. Tick Send on a stat in Setup › Stats, then add its rule here.";

    public const string NoRecipe = "Import a recipe first.";

    /// <summary>Every sent stat across installed recipes, once per metric id.</summary>
    public static IReadOnlyList<RuleChoice> Choices(IReadOnlyList<InstalledRecipe> installed) =>
        [.. installed
            .Where(i => !i.Recipe.IsGroupList)
            .SelectMany(i => i.State.SentStats(i.Recipe))
            .GroupBy(s => s.MetricId, StringComparer.Ordinal)
            .Select(g => g.First())
            .Select(s => new RuleChoice(s.MetricId, $"{s.Label} ({s.MetricId})"))];

    /// <summary>What RoRoRo's rules file holds for a metric id, and whether the helper can add one.</summary>
    public static (string Text, bool CanAdd) RuleSentence(string metricId, string? rulesPath = null, string? inventoryPath = null)
    {
        var status = RulesFile.Inspect(rulesPath, metricId);
        var recorded = RuleInventory.Recorded(metricId, inventoryPath);

        return status.State switch
        {
            RuleState.NoFile =>
                ("RoRoRo has no rules file yet, so nothing can alert until a rule is added. Adding one creates the file.", true),
            RuleState.NoRuleForMetric =>
                ($"RoRoRo has rules, but none for {metricId} — so reports will land and never alert.", true),
            RuleState.OursIntact when recorded is not null && status.Threshold != recorded =>
                ($"Your rule for {metricId} has been changed since Ur Score added it "
                 + $"(now {status.Threshold}, was {recorded}). Left exactly as it is.", false),
            RuleState.OursIntact =>
                ($"Ready: RoRoRo has a rule for {metricId} at {status.Threshold}.", false),
            RuleState.UserOwned =>
                ($"You wrote the rule for {metricId} yourself (threshold {status.Threshold}). Ur Score will not touch it.", false),
            RuleState.OwnedByAnotherPlugin =>
                ($"A rule for {metricId} belongs to {status.Owner}. Left alone.", false),
            RuleState.Unreadable =>
                ("RoRoRo's rules file is not valid JSON. Ur Score will not overwrite it — check it by hand.", false),
            _ => (status.State.ToString(), false),
        };
    }

    public static string Preview(bool canAdd) =>
        canAdd ? $"Rate, below {DefaultThreshold} per minute over {DefaultWindowMinutes} minutes" : "";

    /// <summary>A recipe whose sources are all watched sends nothing, whatever its ticks say.</summary>
    public static string WatchOnly(Recipe recipe) => $"Nothing is sent to RoRoRo: you only watch its {RecipeWords.GroupsLower(recipe)}.";

    /// <summary>
    /// One report policy card line per sending recipe, in <see cref="ReportPolicy.Describe"/>'s words, with the
    /// allow list the running watches use (<see cref="ReportPolicies.Allowed"/>).
    /// </summary>
    public static IReadOnlyList<PolicyItem> Policies(
        IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<HostAccount> accounts, IReadOnlyList<Source> sources, bool resolveNames,
        Func<string, (int Sent, int Dropped)> counts) =>
        [.. installed.Where(i => !i.Recipe.IsGroupList).Select(i =>
        {
            var (sent, dropped) = counts(i.Recipe.Slug);
            var line = ReportPolicies.SendsByRole(i, sources)
                ? new ReportPolicy(i.State.SentStats(i.Recipe), ReportPolicies.Allowed(i, accounts, sources)).Describe(accounts.Count, resolveNames)
                : WatchOnly(i.Recipe);
            return new PolicyItem(i.Recipe.Name, line, $"Sent {sent}, dropped {dropped} this session.");
        })];
}
