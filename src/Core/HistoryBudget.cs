using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Core;

/// <summary>What a proposed change would use of RoRoRo's history, whether it is allowed, and the line that says so.</summary>
public sealed record BudgetCheck(bool Allowed, int Count, string Line);

/// <summary>
/// RoRoRo keeps at most <see cref="Limit"/> metric series and silently refuses a new one past that
/// (stats design §0, verified live). Every account with Send on, times every stat with Send on, is one
/// series, summed across installed recipes (§5.3). Other plugins share the same slots and Ur Score
/// cannot see theirs, so the warning starts well before the limit.
/// </summary>
public static class HistoryBudget
{
    public const int Warn = 200;

    public const int Limit = 256;

    public static int Count(IEnumerable<(int SendingAccounts, int SentStats)> recipes) =>
        recipes.Sum(r => r.SendingAccounts * r.SentStats);

    /// <summary>Each installed recipe's share, from the accounts the window knows and each recipe's own Send ticks.</summary>
    public static IReadOnlyList<(int SendingAccounts, int SentStats)> Installed(
        IEnumerable<InstalledRecipe> recipes, IReadOnlyCollection<Guid> accountIds, string? exceptSlug = null) =>
        [.. recipes
            .Where(r => !string.Equals(r.Recipe.Slug, exceptSlug, StringComparison.Ordinal))
            .Select(r => (accountIds.Count(id => !r.State.Excluded.Contains(id)), r.State.SentStats(r.Recipe).Count))];

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
        IEnumerable<(int SendingAccounts, int SentStats)> otherRecipes,
        (int SendingAccounts, int SentStats) before,
        (int SendingAccounts, int SentStats) after,
        bool accountsKnown)
    {
        var others = Count(otherRecipes);
        var beforeCount = others + before.SendingAccounts * before.SentStats;
        var afterCount = others + after.SendingAccounts * after.SentStats;

        if (afterCount > Limit && afterCount > beforeCount)
        {
            return new BudgetCheck(false, afterCount,
                $"Not allowed: that would use {afterCount} of RoRoRo's {Limit} history slots. RoRoRo drops "
                + $"new series past {Limit} without a word, so turn Send off for a stat or an account first.");
        }

        var line = accountsKnown
            ? $"{afterCount} of RoRoRo's {Limit} history slots: accounts with Send on times stats with Send on, across your installed recipes."
            : $"{afterCount} of RoRoRo's {Limit} history slots so far. RoRoRo hasn't been reached yet, so your accounts count as 0 until it is.";

        if (afterCount >= Warn)
        {
            line += " Other plugins share these slots, so leave room for them.";
        }

        return new BudgetCheck(true, afterCount, line);
    }
}
