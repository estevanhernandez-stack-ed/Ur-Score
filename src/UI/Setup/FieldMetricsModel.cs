using System.ComponentModel;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

/// <summary>One clan-and-field number in the Stats page's own section: its tick, and the id an alert is set on.</summary>
public sealed class FieldMetricItem(FieldMetric metric, bool send) : INotifyPropertyChanged
{
    private bool _send = send;

    public FieldMetric Metric { get; } = metric;

    public string Key => Metric.Key;

    public string Label => Metric.Label;

    public string What => Metric.What;

    public string MetricId => Metric.MetricId;

    public bool Send
    {
        get => _send;
        set
        {
            if (_send == value) return;
            _send = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Send)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Setup › Stats' clan-and-field section: which numbers about your clan's standing go to RoRoRo.
/// <para>
/// It is its own section because a clans list has no per-account stats to tick — the Stats table is a table of
/// your accounts, and a clans list has none of yours in it. These numbers are about a position, not an account,
/// so they get their own list, their own Save, and fixed ids a clan leader can read out loud.
/// </para>
/// </summary>
public static class FieldMetricsModel
{
    /// <summary>
    /// Stats design §5.3 for a list: the history slots these clan numbers would use, across installed recipes.
    /// Each is one series with no account behind it. The field save ran no check at all until 2026-09-21, so
    /// this screen could take a setup past RoRoRo's ceiling without a word (V3-S.28).
    /// </summary>
    public static BudgetCheck Budget(
        Recipe list, RecipeState existing, IReadOnlyList<InstalledRecipe> installed, IReadOnlyCollection<Guid> accountIds,
        IReadOnlyList<string> ticked)
    {
        var others = HistoryBudget.Installed(installed, accountIds, exceptSlug: list.Slug);
        return HistoryBudget.Check(others, (0, 0, existing.FieldMetricKeys.Count), (0, 0, ticked.Count), accountsKnown: accountIds.Count > 0);
    }

    internal const string NoClanSet =
        "Set your clan's name on a clan source in Setup › Clans first. Without it a read cannot tell which row is yours, "
        + "so there is no standing to send.";

    /// <summary>
    /// The clans list these numbers come from: the first one installed. A second clans list would send the same
    /// fixed ids as the first, so only one may own them, and the section names the one that does.
    /// </summary>
    public static InstalledRecipe? ListFor(IReadOnlyList<InstalledRecipe> installed) =>
        installed.FirstOrDefault(i => i.Recipe.IsGroupList);

    /// <summary>Every number that can be ticked, with the ticks this clans list has saved.</summary>
    public static IReadOnlyList<FieldMetricItem> Items(RecipeState state)
    {
        var ticked = state.FieldMetricKeys.ToHashSet(StringComparer.Ordinal);
        return [.. FieldMetrics.All.Select(m => new FieldMetricItem(m, ticked.Contains(m.Key)))];
    }

    /// <summary>What a Save writes: the ticked keys in catalogue order, so the file reads the way the list does.</summary>
    public static IReadOnlyList<string> Ticked(IEnumerable<FieldMetricItem> items) =>
        [.. items.Where(i => i.Send).Select(i => i.Key)];

    /// <summary>
    /// The sentence under the heading: which list the numbers come from, and — when no clan of yours is named on a
    /// source — what to do about it, because every one of these numbers needs to know which row is yours.
    /// </summary>
    public static string Line(InstalledRecipe list, IReadOnlyList<Source> sources, IReadOnlyList<InstalledRecipe> installed)
    {
        var mine = SourceRules.MyClanNames(sources, installed);
        var from = $"From {list.Recipe.Name}, about ";
        return mine.Count == 0
            ? from + "your clan. " + NoClanSet
            : from + (mine.Count == 1 ? $"{mine[0]}. " : $"the best placed of {AndList(mine)}. ")
              + "Each number goes to RoRoRo with no account attached, so an alert on it is worded about the clan, not a player.";
    }

    /// <summary>Two names read "A and B", three "A, B and C": a comma-run reads as a list of one long name.</summary>
    private static string AndList(IReadOnlyList<string> names) =>
        names.Count <= 1 ? string.Join("", names) : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";
}
