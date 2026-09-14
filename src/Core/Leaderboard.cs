using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Core;

/// <summary>One row of the leaderboard, as the window renders it.</summary>
public sealed record RankedRow(int Position, long UserId, double Value, bool IsMine);

/// <summary>Moved from <c>ClanStanding.Rank</c>, unchanged in behaviour, generalized from points to any value.</summary>
public static class Leaderboard
{
    /// <summary>
    /// Highest first, with the user's own accounts marked. Positions are distinct even on ties, and
    /// ties break on user id so the order is stable across polls.
    /// </summary>
    public static IReadOnlyList<RankedRow> Rank(IReadOnlyList<RecipeRow> rows, IReadOnlySet<long> mine) =>
        [.. rows
            .OrderByDescending(r => r.Value)
            .ThenBy(r => r.UserId)
            .Select((r, index) => new RankedRow(index + 1, r.UserId, r.Value, mine.Contains(r.UserId)))];
}
