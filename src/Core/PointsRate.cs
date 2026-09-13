namespace Labs626.UrScore.Core;

/// <summary>One remembered points reading, so the next one can turn two into a rate.</summary>
public sealed record PointsSample(double Points, DateTimeOffset At);

/// <summary>
/// Points per minute between two samples of the same account in the same battle — the window's own
/// display figure, computed from its last two polls of the clan's data.
/// <para>
/// THIS IS NOT THE RATE RORORO JUDGES (spec §6.1, "The rate shown here is not the rate RoRoRo
/// judges"). RoRoRo derives its own rate from the raw cumulative points this plugin reports, over
/// whatever window the user's Rate rule names — commonly ten minutes. This answers a different
/// question: what changed between this plugin's last poll and the one before it, roughly three
/// minutes apart by default. The two figures will disagree sometimes, and that disagreement is not
/// a bug — the window states which figure this is rather than letting either be mistaken for the
/// other.
/// </para>
/// <para>
/// Pure and clockless by design: both samples carry their own timestamp, so this is testable
/// without a timer, a network call, or a running poll loop.
/// </para>
/// </summary>
public static class PointsRate
{
    /// <summary>
    /// Null with no prior sample — nothing to compare yet, most commonly the first poll of a fresh
    /// battle. Also null when the clock did not move forward between the two samples: a zero or
    /// negative interval would make this a division by nonsense rather than a rate.
    /// <para>
    /// Deliberately NOT clamped at zero. A battle reset can make <paramref name="current"/> lower
    /// than <paramref name="previous"/>, and the honest answer is a negative number, not a hidden
    /// one — the report path already sends the low value as observed (§5, "never special-case a
    /// counter reset") for the same reason: smoothing it here would manufacture a rate that never
    /// happened.
    /// </para>
    /// </summary>
    public static double? PerMinute(PointsSample? previous, PointsSample current)
    {
        if (previous is null) return null;

        var minutes = (current.At - previous.At).TotalMinutes;
        if (minutes <= 0) return null;

        return (current.Points - previous.Points) / minutes;
    }
}
