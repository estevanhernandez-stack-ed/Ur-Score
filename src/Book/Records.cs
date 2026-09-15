using Labs626.UrScore.Core;

namespace Labs626.UrScore.Book;

public sealed record AccountRecords(
    double? BestPeriodValue, string? BestPeriod, int? BestRank, string? BestRankPeriod, int PeriodsPlayed,
    double? Highest, double? BiggestDay, double? FastestWeek);

/// <summary>Score book spec §9.5 and §9.6. Pure: derived from the reader, never written anywhere.</summary>
public static class Records
{
    public static AccountRecords For(
        ScoreBookReader reader, string slug, string inputsKey, IEnumerable<string> sourceIds, long userId, string stat, TimeProvider time)
    {
        var finals = reader.Finals(slug, inputsKey)
            .Where(f => f.Accounts.TryGetValue(userId, out var account) && account.V.ContainsKey(stat))
            .Select(f => (f.Period, Account: f.Accounts[userId]))
            .ToList();

        (double Value, string Period)? best = finals.Count == 0 ? null : finals.Select(f => (f.Account.V[stat], f.Period)).MaxBy(x => x.Item1);

        var ranked = finals
            .Where(f => f.Account.Rank?.ContainsKey(stat) == true)
            .Select(f => (Rank: f.Account.Rank![stat], f.Period))
            .ToList();
        (int Rank, string Period)? bestRank = ranked.Count == 0 ? null : ranked.MinBy(x => x.Rank);

        var since = time.GetUtcNow() - ScoreBookReader.KeepReadings;
        var series = sourceIds.Distinct(StringComparer.Ordinal)
            .SelectMany(id => reader.Series(id, userId, stat, null, since))
            .OrderBy(p => p.T)
            .ToList();

        var values = series.Select(p => p.Value).Concat(finals.Select(f => f.Account.V[stat])).ToList();

        return new AccountRecords(
            best?.Value, best?.Period, bestRank?.Rank, bestRank?.Period, finals.Count,
            values.Count == 0 ? null : values.Max(), BiggestDay(series), FastestWeek(series));
    }

    /// <summary>Where this value would place among another group's values: competition place, and the group's size with this one added.</summary>
    public static (int Place, int Of)? WouldPlace(double value, IEnumerable<double> otherRows)
    {
        var others = otherRows.Where(double.IsFinite).ToList();
        return (1 + others.Count(o => o > value), others.Count + 1);
    }

    /// <summary>Unchanged over the last two readings while more than half of at least two others moved.</summary>
    public static bool Stalled(IReadOnlyList<SeriesPoint> mine, IEnumerable<IReadOnlyList<SeriesPoint>> others)
    {
        if (mine.Count < 2 || !mine[^1].Value.Equals(mine[^2].Value)) return false;

        var comparable = others.Where(o => o.Count >= 2).ToList();
        if (comparable.Count < 2) return false;

        var moved = comparable.Count(o => !o[^1].Value.Equals(o[^2].Value));
        return moved > comparable.Count / 2.0;
    }

    public static bool Overdue(DateTimeOffset? lastRead, int intervalSeconds, DateTimeOffset now) =>
        lastRead is { } last && now - last > TimeSpan.FromSeconds(intervalSeconds * 1.5);

    /// <summary>
    /// "+220K in 1h": the last value against the latest reading at least an hour before it, or the first
    /// reading when all are within the hour. <paramref name="now"/> is kept for callers that show "ago" beside it.
    /// </summary>
    public static string Change(IReadOnlyList<SeriesPoint> series, DateTimeOffset now) =>
        Movement(series) is not { } moved
            ? NoEarlierRead
            : $"{(moved.Delta < 0 ? "-" : "+")}{StatText.Abbrev(Math.Abs(moved.Delta))} in {StatText.Span(moved.Span)}";

    /// <summary>What <see cref="Change"/> says with fewer than two readings.</summary>
    public const string NoEarlierRead = "no earlier read";

    /// <summary>
    /// The rise <see cref="Change"/> writes and the span it covers, for a caller that writes the rise its own way
    /// (a duration as time); null with fewer than two readings.
    /// </summary>
    public static (double Delta, TimeSpan Span)? Movement(IReadOnlyList<SeriesPoint> series)
    {
        if (series.Count < 2) return null;

        var last = series[^1];
        var target = last.T - TimeSpan.FromHours(1);
        var earlier = series[0];
        for (var i = series.Count - 2; i >= 0; i--)
        {
            if (series[i].T <= target)
            {
                earlier = series[i];
                break;
            }
        }

        return (last.Value - earlier.Value, last.T - earlier.T);
    }

    private static double? BiggestDay(IReadOnlyList<SeriesPoint> series)
    {
        var days = series
            .GroupBy(p => (p.T + TimeSpan.FromMinutes(p.Off)).UtcDateTime.Date)
            .Where(g => g.Count() >= 2)
            .Select(g => g.OrderBy(p => p.T).Last().Value - g.OrderBy(p => p.T).First().Value)
            .ToList();

        return days.Count == 0 ? null : days.Max();
    }

    /// <summary>
    /// The biggest rise from any earlier point to a later one within 7 days: for each point, against the
    /// lowest point still in its 7-day window, not merely the oldest one (a dip then a rise is a bigger
    /// week than the dip alone). When nothing ever rises, this is the largest (least negative) fall,
    /// matching <see cref="Change"/>'s "delta, whichever sign" contract rather than clamping to zero.
    /// Sliding-window minimum via a monotonic deque of indices, values increasing front to back.
    /// </summary>
    private static double? FastestWeek(IReadOnlyList<SeriesPoint> series)
    {
        double? best = null;
        var window = new List<int>();

        for (var i = 0; i < series.Count; i++)
        {
            while (window.Count > 0 && series[i].T - series[window[0]].T > TimeSpan.FromDays(7)) window.RemoveAt(0);

            if (window.Count > 0) best = Math.Max(best ?? double.MinValue, series[i].Value - series[window[0]].Value);

            while (window.Count > 0 && series[window[^1]].Value >= series[i].Value) window.RemoveAt(window.Count - 1);
            window.Add(i);
        }

        return best;
    }
}
