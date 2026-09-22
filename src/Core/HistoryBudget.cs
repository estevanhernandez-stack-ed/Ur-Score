using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Core;

/// <summary>What a proposed change would use of RoRoRo's history, whether it is allowed, and the line that says so.</summary>
public sealed record BudgetCheck(bool Allowed, int Count, string Line);

/// <summary>
/// RoRoRo keeps at most <see cref="Limit"/> metric series and silently refuses a new one past that
/// (stats design §0, verified live). Every account with Send on, times every stat with Send on, is one
/// series, summed across installed recipes (§5.3) — plus one series per clan-and-field number a list
/// sends, because those have no account behind them and each is a series of its own. Until 2026-09-21 the
/// count had no term for that, so six ticked clan numbers were six slots counted as zero (V3-S.28). Other
/// plugins share the same slots and Ur Score cannot see theirs, so the warning starts well before the limit.
/// </summary>
public static class HistoryBudget
{
    public const int Warn = 200;

    public const int Limit = 256;

    public static int Count(IEnumerable<(int SendingAccounts, int SentStats, int FieldMetrics)> recipes) =>
        recipes.Sum(r => r.SendingAccounts * r.SentStats + r.FieldMetrics);

    /// <summary>
    /// Each installed recipe's share, from the accounts the window knows and each recipe's own Send ticks: the
    /// accounts and stats for a recipe that reads accounts, the clan numbers ticked for a list.
    /// </summary>
    public static IReadOnlyList<(int SendingAccounts, int SentStats, int FieldMetrics)> Installed(
        IEnumerable<InstalledRecipe> recipes, IReadOnlyCollection<Guid> accountIds, string? exceptSlug = null) =>
        [.. recipes
            .Where(r => !string.Equals(r.Recipe.Slug, exceptSlug, StringComparison.Ordinal))
            .Select(r => r.Recipe.IsGroupList
                ? (0, 0, r.State.FieldMetricKeys.Count)
                : (accountIds.Count(id => !r.State.Excluded.Contains(id)), r.State.SentStats(r.Recipe).Count, 0))];

    /// <summary>
    /// The count once RoRoRo's accounts are known, which no tick was checked against: accounts that
    /// arrive with Send on can take it past <see cref="Limit"/> by themselves. Null within the limit.
    /// </summary>
    public static BudgetCheck? AfterSeed(IEnumerable<InstalledRecipe> recipes, IReadOnlyCollection<Guid> accountIds)
    {
        var count = Count(Installed(recipes, accountIds));
        return count <= Limit
            ? null
            : new BudgetCheck(false, count,
                $"{count} of RoRoRo's {Limit} history slots are in use, so RoRoRo will ignore the newest. Untick Send on some stats or accounts.");
    }

    /// <summary>
    /// A change is refused only when it raises the count past <see cref="Limit"/>. A change that lowers
    /// an already-too-high count is always allowed, so the way back under is never blocked.
    /// </summary>
    public static BudgetCheck Check(
        IEnumerable<(int SendingAccounts, int SentStats, int FieldMetrics)> otherRecipes,
        (int SendingAccounts, int SentStats, int FieldMetrics) before,
        (int SendingAccounts, int SentStats, int FieldMetrics) after,
        bool accountsKnown)
    {
        var others = Count(otherRecipes);
        var beforeCount = others + Count([before]);
        var afterCount = others + Count([after]);

        if (afterCount > Limit && afterCount > beforeCount)
        {
            return new BudgetCheck(false, afterCount,
                $"Not allowed: that would use {afterCount} of RoRoRo's {Limit} history slots. RoRoRo drops "
                + $"new series past {Limit} without a word, so turn Send off for a stat or an account first.");
        }

        var line = accountsKnown
            ? $"{afterCount} of RoRoRo's {Limit} history slots: accounts with Send on times stats with Send on, plus each clan number sent, across your installed recipes."
            : $"{afterCount} of RoRoRo's {Limit} history slots so far. RoRoRo hasn't been reached yet, so your accounts count as 0 until it is.";

        if (afterCount >= Warn)
        {
            line += " Other plugins share these slots, so leave room for them.";
        }

        return new BudgetCheck(true, afterCount, line);
    }
}
