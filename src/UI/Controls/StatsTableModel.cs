using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

/// <summary>What a one-off read of counter names found, or why it found nothing.</summary>
public sealed record CounterLookup(IReadOnlyList<string> Names, string? Problem);

/// <summary>
/// One row of the Stats table (spec §7.3). Raises change notifications so the slot line, the refusals and the
/// import button follow every tick. A row the recipe no longer offers is shown greyed and never ticked.
/// </summary>
public sealed class StatRow : INotifyPropertyChanged
{
    private bool _show;
    private bool _send;
    private string _metricId = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public required string Key { get; init; }

    public required string Label { get; init; }

    /// <summary>False for a saved choice this recipe no longer offers.</summary>
    public bool Offered { get; init; } = true;

    public bool Show
    {
        get => _show;
        set
        {
            if (SetField(ref _show, value)) Raise(nameof(Ticked));
        }
    }

    public bool Send
    {
        get => _send;
        set
        {
            if (!SetField(ref _send, value)) return;
            Raise(nameof(Ticked));
            Raise(nameof(NameShown));
        }
    }

    public string MetricId { get => _metricId; set => SetField(ref _metricId, value); }

    public bool Ticked => Show || Send;

    /// <summary>"Name RoRoRo uses" only matters for a stat that is sent, so it shows only then.</summary>
    public bool NameShown => Send;

    public string DisplayLabel => Offered ? Label : $"{Label} (no longer offered)";

    public string ShowName => $"Show {Label}";

    public string SendName => $"Send {Label}";

    public string MetricIdName => $"Name RoRoRo uses for {Label}";

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(propertyName);
        return true;
    }

    private void Raise(string? propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// The Stats table's rules, kept out of the control so they are testable (spec §7.3). The budget and
/// save rules are the ones the import screen used before, moved here unchanged.
/// </summary>
public static class StatsTableModel
{
    /// <summary>
    /// Recipe values in recipe order, then counter names in the order the source gave them, then saved
    /// counters that are not in that list, then saved choices this recipe no longer offers.
    /// </summary>
    public static IReadOnlyList<StatRow> Build(
        Recipe recipe, IReadOnlyDictionary<string, StatChoice> choices, IReadOnlyList<string> counterNames)
    {
        var rows = new List<StatRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(RecipeStat stat)
        {
            if (!seen.Add(stat.Key)) return;

            var choice = choices.GetValueOrDefault(stat.Key);
            rows.Add(new StatRow
            {
                Key = stat.Key,
                Label = stat.Label,
                Show = choice?.Show ?? false,
                Send = choice?.Send ?? false,
                MetricId = choice?.MetricId ?? stat.SuggestedMetricId,
            });
        }

        foreach (var value in recipe.LastStep.Values)
        {
            if (RecipeStats.Find(recipe, value.Id) is { } stat) Add(stat);
        }

        if (recipe.LastStep.Counters is not null)
        {
            foreach (var name in counterNames.Where(RecipeStats.CanPick))
            {
                if (RecipeStats.Find(recipe, RecipeStats.CounterKey(name)) is { } stat) Add(stat);
            }

            foreach (var key in choices.Keys.Where(key => RecipeStats.IsCounterKey(key, out _)).Order(StringComparer.Ordinal))
            {
                if (RecipeStats.Find(recipe, key) is { } stat) Add(stat);
            }
        }

        foreach (var key in choices.Keys.Where(key => !seen.Contains(key)).Order(StringComparer.Ordinal))
        {
            seen.Add(key);
            rows.Add(new StatRow
            {
                Key = key,
                Label = RecipeStats.IsCounterKey(key, out var name) ? name : key,
                Offered = false,
                MetricId = choices[key].MetricId,
            });
        }

        return rows;
    }

    /// <summary>
    /// Every entry already saved, plus every offered row ticked now. An entry, once written, keeps its
    /// name through recipe updates (stats design §5.1), so a row never ticked writes nothing.
    /// </summary>
    public static Dictionary<string, StatChoice> Choices(IReadOnlyDictionary<string, StatChoice> saved, IEnumerable<StatRow> rows)
    {
        var choices = new Dictionary<string, StatChoice>(saved, StringComparer.Ordinal);
        foreach (var row in rows.Where(r => r.Offered && (choices.ContainsKey(r.Key) || r.Ticked)))
        {
            choices[row.Key] = new StatChoice(row.Show, row.Send, row.MetricId.Trim());
        }

        return choices;
    }

    /// <summary>Rows whose label contains the query, ignoring case, plus every ticked row. A blank query shows all.</summary>
    public static IReadOnlyList<StatRow> Visible(IReadOnlyList<StatRow> rows, string? query)
    {
        var trimmed = query?.Trim() ?? "";
        return trimmed.Length == 0
            ? rows
            : [.. rows.Where(r => r.Ticked || r.Label.Contains(trimmed, StringComparison.OrdinalIgnoreCase))];
    }

    public static string ShowingLine(int visible, int total, string? query) =>
        string.IsNullOrWhiteSpace(query) ? "" : $"Showing {visible} of {total} · ticked stats always shown";

    public static bool AnyTicked(IEnumerable<StatRow> rows) => rows.Any(r => r.Offered && r.Ticked);

    /// <summary>Stats design §5.3: the history slots these ticks would use, across installed recipes.</summary>
    public static BudgetCheck Budget(
        Recipe recipe, RecipeState existing, IReadOnlyList<InstalledRecipe> installed,
        IReadOnlyCollection<Guid> accountIds, IEnumerable<StatRow> rows)
    {
        var sending = accountIds.Count(id => !existing.Excluded.Contains(id));
        var others = HistoryBudget.Installed(installed, accountIds, exceptSlug: recipe.Slug);
        var before = (sending, existing.SentStats(recipe).Count);
        var after = (sending, new RecipeState(Stats: Choices(existing.StatChoices, rows)).SentStats(recipe).Count);
        return HistoryBudget.Check(others, before, after, accountsKnown: accountIds.Count > 0);
    }

    /// <summary>The screen's own refusals, then "tick at least one", then the last budget refusal.</summary>
    public static IReadOnlyList<string> Refusals(IEnumerable<string> extra, IEnumerable<StatRow> rows, string? budgetRefusal)
    {
        var refusals = extra.ToList();
        if (!AnyTicked(rows)) refusals.Add(StatRules.TickOne);
        if (budgetRefusal is not null) refusals.Add(budgetRefusal);
        return refusals;
    }

    /// <summary>Every reason these ticks cannot be saved: <see cref="StatRules"/>, then the budget.</summary>
    public static IReadOnlyList<string> SaveProblems(
        Recipe recipe, RecipeState existing, IReadOnlyList<InstalledRecipe> installed,
        IReadOnlyCollection<Guid> accountIds, IEnumerable<StatRow> rows)
    {
        var list = rows.ToList();
        var problems = StatRules.Problems(recipe, Choices(existing.StatChoices, list), installed).ToList();
        if (Budget(recipe, existing, installed, accountIds, list) is { Allowed: false } refused) problems.Add(refused.Line);
        return problems;
    }

    public static string RuleLines(IEnumerable<StatRow> rows, Func<string, string> ruleSentence) =>
        string.Join(Environment.NewLine, rows
            .Where(r => r.Offered && r.Send && !string.IsNullOrWhiteSpace(r.MetricId))
            .Select(r => $"{r.Label}: {ruleSentence(r.MetricId.Trim())}"));

    public static string NamesLine(Recipe recipe, int savedNames) =>
        recipe.LastStep.Counters is null ? ""
        : savedNames == 0 ? "No statistic names read yet."
        : $"{savedNames.ToString("N0", CultureInfo.InvariantCulture)} statistic names from the last read.";

    public static string FoundNamesLine(int count) =>
        $"Found {count.ToString("N0", CultureInfo.InvariantCulture)} statistic names. Search them above.";

    public static string ReadingNamesLine(Recipe recipe) =>
        $"Reading stat names once from {RecipeHosts.HostOf(recipe.LastStep.Url)}…";
}
