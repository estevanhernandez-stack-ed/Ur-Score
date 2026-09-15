using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

using Source = Labs626.UrScore.Core.Source;

public sealed record PolicyItem(string RecipeName, string Line, string Counts);

/// <summary>
/// Setup › Alerts' report policy card (spec §7.5). The policy line is <see cref="ReportPolicy.Describe"/>'s own sentence.
/// The alert cards above it are <see cref="AlertCards"/>.
/// </summary>
public static class AlertsModel
{
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
