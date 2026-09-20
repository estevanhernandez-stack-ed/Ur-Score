using Labs626.UrScore.Board;
using Labs626.UrScore.Book;

namespace UrScore.Tests;

/// <summary>
/// How fast a clan is going, and what catching the place above would take. Measured on the owner's own board on
/// 2026-09-19: 25 minutes of readings projected across 140 hours gave 26 billion, so a window states itself and a
/// short one is not projected at all.
/// </summary>
public class PaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 20, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<SeriesPoint> Series(params (double Hours, double Value)[] points) =>
        [.. points.Select(p => new SeriesPoint(Now.AddHours(-p.Hours), p.Value, null, false, 0))];

    [Fact]
    public void APaceIsTheGainOverTheTimeItTook()
    {
        var series = Series((3, 100), (2, 200), (1, 300), (0, 400));

        var hour = Pace.Over(series, Now.AddHours(-1));
        Assert.NotNull(hour);
        Assert.Equal(100, hour.PerHour);
        Assert.Equal(TimeSpan.FromHours(1), hour.Span);

        var all = Pace.Over(series, DateTimeOffset.MinValue);
        Assert.NotNull(all);
        Assert.Equal(100, all.PerHour);
        Assert.Equal(TimeSpan.FromHours(3), all.Span);
        Assert.Equal(Now.AddHours(-3), all.From);
    }

    /// <summary>
    /// One reading is a number, not a rate, and two a minute apart are a rate nobody should act on. Under a quarter
    /// of an hour there is no pace, so nothing downstream can project one.
    /// </summary>
    [Fact]
    public void TooShortAWindowIsNoPace()
    {
        Assert.Null(Pace.Over(Series((0, 400)), DateTimeOffset.MinValue));
        Assert.Null(Pace.Over(Series((0.1, 380), (0, 400)), DateTimeOffset.MinValue));
        Assert.Null(Pace.Over([], DateTimeOffset.MinValue));
        Assert.NotNull(Pace.Over(Series((0.25, 380), (0, 400)), DateTimeOffset.MinValue));
    }

    /// <summary>A clan's points only rise; a fall is a reset or a bad read, and a negative pace is never reported.</summary>
    [Fact]
    public void AFallIsNoPace()
    {
        Assert.Null(Pace.Over(Series((1, 500), (0, 400)), DateTimeOffset.MinValue));
    }

    [Fact]
    public void TheBestHourIsTheBestAnyHourDid()
    {
        // Quiet, then a burst of 300 in one hour, then quiet again.
        var series = Series((4, 0), (3, 100), (2, 400), (1, 450), (0, 500));

        var best = Pace.BestHour(series);

        Assert.NotNull(best);
        Assert.Equal(300, best.PerHour);
        Assert.Equal(Now.AddHours(-3), best.From);
    }

    [Fact]
    public void WithoutAnHourThereIsNoBestHour() =>
        Assert.Null(Pace.BestHour(Series((0.5, 100), (0, 300))));

    /// <summary>
    /// Catching the place above, with both of you still moving: the gap has to be closed AND their gains matched, so
    /// the pace needed is the gap over the time left plus whatever they are doing.
    /// </summary>
    [Fact]
    public void CatchingThemMeansOutPacingThemByTheGap()
    {
        var chase = Pace.Chase(gap: 1000, mine: 500, theirs: 400, left: TimeSpan.FromHours(10), best: 900);

        Assert.Equal(500, chase.Needed);          // 1000/10 + 400
        Assert.Equal(100, chase.Closing);         // 500 - 400
        Assert.Equal(TimeSpan.FromHours(10), chase.CatchIn);
        Assert.Equal(PaceVerdict.OnTrack, chase.Verdict);
    }

    [Fact]
    public void FallingBehindIsSaidAsWhatItWouldTake()
    {
        var chase = Pace.Chase(gap: 1000, mine: 300, theirs: 400, left: TimeSpan.FromHours(10), best: 900);

        Assert.Equal(500, chase.Needed);
        Assert.Equal(-100, chase.Closing);
        Assert.Null(chase.CatchIn);
        Assert.Equal(PaceVerdict.NeedsALift, chase.Verdict);   // 500 needed, 900 done before
    }

    /// <summary>
    /// Out of reach is measured against your own best hour, not against a feeling: the gap is closable in the time
    /// left (900 x 10h = 9,000 covers 6,000), but only at a pace this clan has never held for an hour.
    /// </summary>
    [Fact]
    public void BeyondYourBestHourIsOutOfReach()
    {
        var chase = Pace.Chase(gap: 6_000, mine: 300, theirs: 400, left: TimeSpan.FromHours(10), best: 900);

        Assert.Equal(1_000, chase.Needed);
        Assert.Equal(PaceVerdict.OutOfReach, chase.Verdict);
    }

    /// <summary>The certain one: their points now are beyond your best hour for all the time that is left.</summary>
    [Fact]
    public void EvenIfTheyStopIsItsOwnVerdict()
    {
        var chase = Pace.Chase(gap: 10_000, mine: 300, theirs: 400, left: TimeSpan.FromHours(10), best: 900);

        Assert.Equal(PaceVerdict.OutOfReachEvenIfTheyStop, chase.Verdict);   // 900 x 10h = 9,000 < 10,000
    }

    /// <summary>With the battle over there is nothing to chase, and no division by nothing.</summary>
    [Fact]
    public void NoTimeLeftIsNoChase()
    {
        var chase = Pace.Chase(gap: 1000, mine: 500, theirs: 400, left: TimeSpan.Zero, best: 900);

        Assert.Null(chase.Needed);
        Assert.Equal(PaceVerdict.Ended, chase.Verdict);
    }

    /// <summary>
    /// What a closed app does to a chart, seen on the owner's board 2026-09-20: Ur Score was off for fourteen hours,
    /// so the newest reading before "the last hour" was yesterday's and a fourteen-hour average was labelled
    /// "Current". The window is still true; it is just not current, so it is not offered as one.
    /// </summary>
    [Fact]
    public void AWindowThatReachesAcrossAGapIsNotCurrent()
    {
        var series = Series((14, 1_000), (0, 8_000));

        Assert.Null(Pace.Over(series, Now.AddHours(-1), Pace.LongestCurrent));
        Assert.NotNull(Pace.Over(series, Now.AddHours(-1)));   // the same window, asked for without a limit
    }

    /// <summary>A best hour that spans a gap was never an hour: fourteen hours of gains is not this clan's best hour.</summary>
    [Fact]
    public void TheBestHourNeverSpansAGap()
    {
        Assert.Null(Pace.BestHour(Series((14, 1_000), (0, 8_000))));

        // One missed read still counts: a window a little past the hour is an hour.
        var nearly = Series((1.2, 100), (0, 400));
        Assert.NotNull(Pace.BestHour(nearly));
    }
}
