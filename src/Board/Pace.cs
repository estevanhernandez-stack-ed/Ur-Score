using Labs626.UrScore.Book;

namespace Labs626.UrScore.Board;

/// <summary>What a pace window covers: the rate, how long it took, and when it started, so a panel can say which.</summary>
public sealed record PaceWindow(double PerHour, TimeSpan Span, DateTimeOffset From);

/// <summary>Where a chase stands. The order is the ladder a reader climbs: fine, work needed, no, certainly no.</summary>
public enum PaceVerdict
{
    /// <summary>At these paces you pass them before it ends.</summary>
    OnTrack,

    /// <summary>More than you are doing, but no more than you have already done in an hour of this battle.</summary>
    NeedsALift,

    /// <summary>More than your best hour of this battle, for every hour that is left.</summary>
    OutOfReach,

    /// <summary>Your best hour, for all the time left, does not reach the points they already have.</summary>
    OutOfReachEvenIfTheyStop,

    /// <summary>It is over; there is nothing left to chase.</summary>
    Ended,
}

/// <summary>
/// The chase, as numbers a panel can read out: what it would take, how fast the gap is closing, when it closes.
/// <paramref name="Needed"/> is null once there is no time left.
/// </summary>
public sealed record PaceChase(double? Needed, double Closing, TimeSpan? CatchIn, PaceVerdict Verdict);

/// <summary>
/// How fast a clan is going, from the readings the score book already keeps.
/// <para>
/// Every pace states the window it came from, because a short one lies loudly: measured on the owner's board on
/// 2026-09-19, twenty-five minutes of readings carried across the battle's remaining 140 hours projected 26 billion
/// points. Under a quarter of an hour there is no pace at all, so nothing downstream can project one.
/// </para>
/// </summary>
public static class Pace
{
    /// <summary>The shortest window worth a rate. Reads land every few minutes, so this is several of them.</summary>
    public static readonly TimeSpan Shortest = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The pace from <paramref name="since"/> to the last reading, or null when there is too little to say: fewer
    /// than two readings, a window under <see cref="Shortest"/>, or a fall (a clan's points only rise, so a drop is
    /// a reset or a bad read, never a negative pace).
    /// </summary>
    public static PaceWindow? Over(IReadOnlyList<SeriesPoint> series, DateTimeOffset since)
    {
        if (series.Count < 2) return null;

        var last = series[^1];
        // The last reading at or before the window opens, so the window covers the whole of it rather than starting
        // at whatever happened to be read next.
        var first = series.LastOrDefault(p => p.T <= since) ?? series.FirstOrDefault(p => p.T <= last.T);
        if (first is null || first.T >= last.T) return null;

        var span = last.T - first.T;
        if (span < Shortest) return null;

        var gain = last.Value - first.Value;
        return gain < 0 ? null : new PaceWindow(gain / span.TotalHours, span, first.T);
    }

    /// <summary>
    /// The best hour of the readings: every hour that ends on a reading is measured, and the fastest wins. Null with
    /// less than an hour of history — a best hour that never was an hour is not one.
    /// </summary>
    public static PaceWindow? BestHour(IReadOnlyList<SeriesPoint> series)
    {
        PaceWindow? best = null;
        foreach (var end in series)
        {
            var from = end.T - TimeSpan.FromHours(1);
            var start = series.LastOrDefault(p => p.T <= from);
            if (start is null) continue;

            var gain = end.Value - start.Value;
            if (gain < 0) continue;

            var span = end.T - start.T;
            var perHour = gain / span.TotalHours;
            if (best is null || perHour > best.PerHour) best = new PaceWindow(perHour, span, start.T);
        }

        return best;
    }

    /// <summary>
    /// What catching the place above would take. Both of you are moving, so the pace needed is the gap spread over
    /// the time left PLUS whatever they are doing; matching them alone never closes a gap.
    /// </summary>
    /// <param name="gap">How far ahead they are now.</param>
    /// <param name="mine">Your pace, per hour.</param>
    /// <param name="theirs">Their pace, per hour.</param>
    /// <param name="left">How long the period has left.</param>
    /// <param name="best">Your best hour of this period, which is the honest ceiling for "could we".</param>
    public static PaceChase Chase(double gap, double mine, double theirs, TimeSpan left, double? best)
    {
        var closing = mine - theirs;
        if (left <= TimeSpan.Zero) return new PaceChase(null, closing, null, PaceVerdict.Ended);

        var hours = left.TotalHours;
        var needed = gap / hours + theirs;
        TimeSpan? catchIn = closing > 0 && gap > 0 ? TimeSpan.FromHours(gap / closing) : null;
        if (catchIn > left) catchIn = null;

        var verdict =
            catchIn is not null || gap <= 0 ? PaceVerdict.OnTrack
            : best is not { } ceiling ? PaceVerdict.NeedsALift
            : ceiling * hours < gap ? PaceVerdict.OutOfReachEvenIfTheyStop
            : needed > ceiling ? PaceVerdict.OutOfReach
            : PaceVerdict.NeedsALift;

        return new PaceChase(needed, closing, catchIn, verdict);
    }
}
