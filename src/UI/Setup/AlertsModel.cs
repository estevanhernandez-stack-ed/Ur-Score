using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

/// <summary>One card: the mode (or clans list) it is about, never a reader slug or a recipe name.</summary>
public sealed record PolicyItem(string ModeName, string Line, string Counts);

/// <summary>
/// Setup › Alerts' report policy card (spec §7.5). The policy line is <see cref="ReportPolicy.Describe"/>'s own sentence.
/// The alert cards above it are <see cref="AlertCards"/>.
/// </summary>
public static class AlertsModel
{
    /// <summary>A recipe whose sources are all watched sends nothing, whatever its ticks say.</summary>
    public static string WatchOnly(Recipe recipe) => $"Nothing is sent to RoRoRo: you only watch its {RecipeWords.GroupsLower(recipe)}.";

    /// <summary>
    /// One report policy card line per mode (<paramref name="readerName"/> labels it, <c>ReaderNames.For</c>), in <see cref="ReportPolicy.Describe"/>'s words, with the allow
    /// list the running watches use (<see cref="ReportPolicies.Allowed"/>). A reader whose mode is off
    /// (<paramref name="offModeName"/> names the mode) has no watch, so it says that and nothing more (review round 2).
    /// <para>
    /// A clans list gets a card of its own from 0.5.0. It sends for no account and never will, but it may send
    /// the clan-and-field numbers you ticked (<see cref="FieldMetrics"/>) — and a window whose whole job is to
    /// say what leaves must say those too.
    /// </para>
    /// </summary>
    public static IReadOnlyList<PolicyItem> Policies(
        IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<HostAccount> accounts, IReadOnlyList<Source> sources, bool resolveNames,
        Func<string, (int Sent, int Dropped, int Held)> counts, Func<string, string?>? offModeName = null,
        Func<string, string>? readerName = null) =>
        [.. installed.Select(i =>
        {
            var (sent, dropped, held) = counts(i.Recipe.Slug);
            var line = offModeName?.Invoke(i.Recipe.Slug) is { } mode ? $"{mode} is off, so nothing is sent."
                : i.Recipe.IsGroupList
                ? new ReportPolicy([], new HashSet<Guid>(), FieldMetrics.Offered(i.State.FieldMetricKeys)).DescribeField()
                : ReportPolicies.SendsByRole(i, sources)
                    ? new ReportPolicy(i.State.SentStats(i.Recipe), ReportPolicies.Allowed(i, accounts, sources)).Describe(accounts.Count, resolveNames)
                    : WatchOnly(i.Recipe);
            // The held clause appears only when there is something held, because it names a fault in the rules
            // file and a permanent "held back 0" would be one more number nobody reads (V3-S.35).
            var counted = $"Sent {sent}, dropped {dropped} this session.";
            if (held > 0) counted += $" {held} held back: the alert name couldn't be written.";

            return new PolicyItem(readerName?.Invoke(i.Recipe.Slug) ?? i.Recipe.Name, line, counted);
        })];
}
