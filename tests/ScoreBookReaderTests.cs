using Labs626.UrScore.Book;
using Labs626.UrScore.Core;

namespace UrScore.Tests;

public class ScoreBookReaderTests
{
    private const string Slug = "pet-sim-99-clan-battle-points";

    private static readonly DateTimeOffset Now = new(2026, 9, 19, 18, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, string> K0i2 = new() { ["clan"] = "K0i2" };

    private static BookLine Read(DateTimeOffset t, double points, string period = "B", DateTimeOffset? asOf = null, int off = -300, string source = "s-1") => new(
        BookLine.Version, BookLine.KindRead, t, off, BookLine.TriggerTimer, new BookRecipeRef(Slug, "3f9a1c0b7e2d4a55"), source, "mine", K0i2,
        new BookPeriod(period), new Dictionary<string, double> { ["clan-points"] = points * 10 }, ["value"],
        new Dictionary<string, BookAccount> { ["111"] = new(new Dictionary<string, double> { ["value"] = points }, AsOf: asOf) });

    private static BookLine Final(string period, DateTimeOffset t, double points, int rank, string account = "111") => new(
        BookLine.Version, BookLine.KindFinal, t, -300, BookLine.TriggerBackfill, new BookRecipeRef(Slug, "3f9a1c0b7e2d4a55"), "s-1", "mine", K0i2,
        new BookPeriod(period), new Dictionary<string, double> { ["clan-place"] = rank * 10 }, ["value"],
        new Dictionary<string, BookAccount> { [account] = new(new Dictionary<string, double> { ["value"] = points }, new Dictionary<string, int> { ["value"] = rank }, 48) });

    private static ScoreBookReader Reader(params BookLine[] lines)
    {
        var reader = new ScoreBookReader("unused-root", new ManualTime(Now));
        foreach (var line in lines) reader.Apply(line);
        return reader;
    }

    [Fact]
    public void ASeriesIsOneAccountsStatInOrderForAPeriod()
    {
        var reader = Reader(Read(Now.AddMinutes(-6), 20), Read(Now.AddMinutes(-9), 10), Read(Now.AddMinutes(-3), 30, period: "A"));

        var series = reader.Series("s-1", 111, "value", "B", DateTimeOffset.MinValue);

        Assert.Equal(new[] { 10d, 20d }, series.Select(p => p.Value).ToArray());
        Assert.Equal(-300, series[0].Off);
        Assert.Empty(reader.Series("s-2", 111, "value", "B", DateTimeOffset.MinValue));
    }

    [Fact]
    public void DuplicateReadingsCollapse()
    {
        // Ruling R4: same value and same asOf, or no asOf and under 60 s apart.
        var stamp = Now.AddMinutes(-30);
        var reader = Reader(
            Read(Now.AddMinutes(-10), 5, asOf: stamp), Read(Now.AddMinutes(-7), 5, asOf: stamp),
            Read(Now.AddMinutes(-4), 5), Read(Now.AddMinutes(-4).AddSeconds(20), 5),
            Read(Now.AddMinutes(-1), 5));

        Assert.Equal(3, reader.Series("s-1", 111, "value", "B", DateTimeOffset.MinValue).Count);
    }

    [Fact]
    public void ReadingsOlderThanThirtyFiveDaysAreNotKeptButStillCounted()
    {
        var reader = Reader(Read(Now.AddDays(-40), 1), Read(Now.AddDays(-1), 2));

        Assert.Single(reader.Series("s-1", 111, "value", null, DateTimeOffset.MinValue));
        Assert.Equal(2, reader.Readings(Slug));
        Assert.Equal(Now.AddDays(-40), reader.FirstReading(Slug));
    }

    [Fact]
    public void FinalsMergeSupplementaryLinesAndListNewestFirst()
    {
        var reader = Reader(Final("A", Now.AddDays(-20), 300, 2), Final("B", Now.AddDays(-5), 4200, 1), Final("A", Now.AddDays(-1), 250, 3, account: "333"));

        var finals = reader.Finals(Slug, Source.KeyOf(new Dictionary<string, string> { ["clan"] = "k0i2" }));

        Assert.Equal(new[] { "B", "A" }, finals.Select(f => f.Period).ToArray());
        Assert.Equal(new long[] { 111, 333 }, finals[1].Accounts.Keys.Order().ToArray());
        Assert.Equal(20, finals[1].Headline["clan-place"]);
    }

    [Fact]
    public void RecordsComeFromFinalsAndTheLastFiveWeeks()
    {
        var reader = Reader(
            Final("A", Now.AddDays(-20), 300, 2), Final("B", Now.AddDays(-5), 4200, 1),
            Read(Now.AddDays(-2).AddHours(1), 100), Read(Now.AddDays(-2).AddHours(10), 900),
            Read(Now.AddDays(-1).AddHours(1), 1000), Read(Now.AddDays(-1).AddHours(2), 1100));

        var records = Records.For(reader, Slug, Source.KeyOf(K0i2), ["s-1"], 111, "value", new ManualTime(Now));

        Assert.Equal((4200d, "B"), (records.BestPeriodValue!.Value, records.BestPeriod));
        Assert.Equal((1, "B"), (records.BestRank!.Value, records.BestRankPeriod));
        Assert.Equal(2, records.PeriodsPlayed);
        Assert.Equal(4200, records.Highest);
        Assert.Equal(800, records.BiggestDay);
        Assert.Equal(1000, records.FastestWeek);
    }

    [Fact]
    public void ADayIsTheAccountsLocalDayFromTheLinesOffset()
    {
        // 03:30 UTC is 22:30 the evening before at UTC-5, so both readings share one local day.
        var reader = Reader(Read(new DateTimeOffset(2026, 9, 18, 15, 0, 0, TimeSpan.Zero), 10), Read(new DateTimeOffset(2026, 9, 19, 3, 30, 0, TimeSpan.Zero), 70));

        Assert.Equal(60, Records.For(reader, Slug, Source.KeyOf(K0i2), ["s-1"], 111, "value", new ManualTime(Now)).BiggestDay);
    }

    [Fact]
    public void WouldPlaceRanksAValueAmongAnotherClansRows()
    {
        Assert.Equal((2, 4), Records.WouldPlace(9_100_000, [12_400_000, 8_240_900, 8_700_000]));
        Assert.Equal((1, 1), Records.WouldPlace(5, []));
    }

    [Fact]
    public void StalledNeedsTwoOthersAndMostOfThemMoving()
    {
        SeriesPoint P(double v, int minutes) => new(Now.AddMinutes(minutes), v, null, false, 0);
        var flat = new[] { P(5, -6), P(5, -3) };
        var moving = new[] { P(5, -6), P(9, -3) };

        Assert.True(Records.Stalled(flat, [moving, moving, flat]));
        Assert.False(Records.Stalled(flat, [moving]));
        Assert.False(Records.Stalled(moving, [moving, moving]));
        Assert.False(Records.Stalled(flat, [flat, flat, moving]));
    }

    [Fact]
    public void OverdueIsLaterThanOneAndAHalfIntervals()
    {
        Assert.False(Records.Overdue(Now.AddSeconds(-269), 180, Now));
        Assert.True(Records.Overdue(Now.AddSeconds(-271), 180, Now));
        Assert.False(Records.Overdue(null, 180, Now));
    }

    [Fact]
    public void ChangeSaysOverWhatSpan()
    {
        SeriesPoint P(double v, int minutes) => new(Now.AddMinutes(minutes), v, null, false, 0);

        Assert.Equal("+220K in 1h", Records.Change([P(1_000_000, -90), P(1_100_000, -60), P(1_320_000, 0)], Now));
        Assert.Equal("+50 in 20m", Records.Change([P(10, -20), P(60, 0)], Now));
        Assert.Equal("-1.5M in 3d", Records.Change([P(2_000_000, -72 * 60), P(500_000, 0)], Now));
        Assert.Equal("no earlier read", Records.Change([P(10, 0)], Now));
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(950, "950")]
    [InlineData(1234, "1.23K")]
    [InlineData(220000, "220K")]
    [InlineData(999999, "1M")]
    [InlineData(12418220, "12.4M")]
    [InlineData(9169613101, "9.17B")]
    [InlineData(-310000, "-310K")]
    public void NumbersAbbreviate(double value, string expected) => Assert.Equal(expected, StatText.Abbrev(value));

    [Fact]
    public void SpansAreMinutesHoursOrDays()
    {
        Assert.Equal("31m", StatText.Span(TimeSpan.FromMinutes(31)));
        Assert.Equal("5h", StatText.Span(TimeSpan.FromHours(5)));
        Assert.Equal("3d", StatText.Span(TimeSpan.FromDays(3)));
    }
}
