using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Core;

/// <summary>One row of the leaderboard, as the window renders it, with every stat read for it.</summary>
public sealed record RankedRow(int Position, long UserId, IReadOnlyDictionary<string, double> Values, bool IsMine);

/// <summary>Moved from <c>ClanStanding.Rank</c>, generalized from points to any stat.</summary>
public static class Leaderboard
{
    /// <summary>
    /// Highest <paramref name="statKey"/> first, with the user's own accounts marked. Positions are
    /// distinct even on ties, and ties break on user id so the order is stable across polls. A row
    /// with no number for the stat ranks after every row that has one, never as a zero.
    /// </summary>
    public static IReadOnlyList<RankedRow> Rank(IReadOnlyList<RecipeRow> rows, IReadOnlySet<long> mine, string statKey) =>
        [.. rows
            .OrderBy(r => r.Values.ContainsKey(statKey) ? 0 : 1)
            .ThenByDescending(r => r.Values.GetValueOrDefault(statKey))
            .ThenBy(r => r.UserId)
            .Select((r, index) => new RankedRow(index + 1, r.UserId, r.Values, mine.Contains(r.UserId)))];
}
