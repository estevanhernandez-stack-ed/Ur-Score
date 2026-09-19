using Labs626.UrScore.UI;

namespace UrScore.Tests;

/// <summary>
/// The line the board's clock draws. The control rewrites itself once a second from the system clock, so what a test
/// can hold still is the line at a given instant: <see cref="CountdownText.Line"/> is that, and the control draws
/// nothing else.
/// </summary>
public class CountdownTextTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Ends = new(2026, 9, 25, 16, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    [Fact]
    public void TheLeadKeepsTheCountdownCompany()
    {
        Assert.Equal("SpaceMineBattle2026 · ends in 5d 22:00:00 · Fri 25 Sep 11:00",
            CountdownText.Line("SpaceMineBattle2026", Ends, Now, Chicago));

        // The top bar has one line's width, so it takes the countdown without the clock.
        Assert.Equal("SpaceMineBattle2026 · next read in 2m · ends in 5d 22:00:00",
            CountdownText.Line("SpaceMineBattle2026 · next read in 2m", Ends, Now, Chicago, withClock: false));
    }

    [Fact]
    public void WithNoEndTheLineIsTheLeadAlone()
    {
        Assert.Equal("Reads every 30m", CountdownText.Line("Reads every 30m", null, Now, Chicago));
        Assert.Equal("", CountdownText.Line("", null, Now, Chicago));
    }

    /// <summary>A period with an end but no name to lead it never opens with a stray separator.</summary>
    [Fact]
    public void WithNoLeadTheCountdownStandsAlone()
    {
        Assert.Equal("ends in 5d 22:00:00 · Fri 25 Sep 11:00", CountdownText.Line("", Ends, Now, Chicago));
        Assert.Equal("ended", CountdownText.Line("", Ends, Ends, Chicago));
    }
}
