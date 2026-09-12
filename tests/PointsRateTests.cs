using Labs626.UrScore.Core;

namespace UrScore.Tests;

public class PointsRateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NoPriorSampleIsNullRatherThanZero()
    {
        // The first poll of a fresh battle has nothing to compare against. Zero would read as "not
        // scoring," which is a lie — there is simply no rate yet.
        var rate = PointsRate.PerMinute(previous: null, new PointsSample(4200, T0));

        Assert.Null(rate);
    }

    [Fact]
    public void ComputesPointsPerMinuteBetweenTwoSamples()
    {
        // The default poll cadence: three minutes apart, 300 points gained.
        var previous = new PointsSample(1000, T0);
        var current = new PointsSample(1300, T0.AddMinutes(3));

        Assert.Equal(100, PointsRate.PerMinute(previous, current));
    }

    [Fact]
    public void ScalesToASingleMinuteRegardlessOfTheActualPollInterval()
    {
        // A user with a shorter interval (still floored at 180s by Settings, but a hand-edited
        // file is not something this class can see) must still get a per-MINUTE figure, not a
        // per-poll one.
        var previous = new PointsSample(0, T0);
        var current = new PointsSample(30, T0.AddSeconds(90));

        Assert.Equal(20, PointsRate.PerMinute(previous, current));
    }

    [Fact]
    public void AZeroIntervalIsNullRatherThanDividingByNonsense()
    {
        var sample = new PointsSample(500, T0);

        Assert.Null(PointsRate.PerMinute(sample, sample));
    }

    [Fact]
    public void ATimestampThatMovedBackwardIsNullRatherThanANegativeDuration()
    {
        // Two samples handed in out of order — a caller bug, not a battle event. Dividing by a
        // negative number of minutes would produce a number that looks like a rate and is not one.
        var previous = new PointsSample(500, T0);
        var current = new PointsSample(600, T0.AddMinutes(-1));

        Assert.Null(PointsRate.PerMinute(previous, current));
    }

    [Fact]
    public void ADropInPointsIsANegativeRateRatherThanClampedOrHidden()
    {
        // A battle reset drops the counter. The report path sends the low value as observed rather
        // than smoothing it (§5) — the display figure follows the same honesty rather than hiding
        // the drop behind a floor of zero.
        var previous = new PointsSample(9000, T0);
        var current = new PointsSample(40, T0.AddMinutes(2));

        Assert.Equal(-4480, PointsRate.PerMinute(previous, current));
    }
}
