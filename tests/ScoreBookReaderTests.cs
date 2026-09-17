using System.Text;
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
    public void AHeadlineSeriesFiltersReadingsAndPreservesSourceMetadata()
    {
        var stamp = Now.AddMinutes(-20);
        var first = Read(Now.AddMinutes(-9), 10, asOf: Now.AddMinutes(-30), off: 60) with
        {
            AsOf = stamp,
            Stale = true,
        };
        var second = Read(Now.AddMinutes(-6), 20, asOf: Now.AddMinutes(-25));
        var otherPeriod = Read(Now.AddMinutes(-3), 30, period: "A");
        var reader = Reader(
            second, first, otherPeriod,
            Read(Now.AddMinutes(-2), 40, source: "s-2"),
            Read(Now.AddMinutes(-1), 50) with { Headline = new Dictionary<string, double> { ["clan-place"] = 7 } },
            Final("B", Now, 999, 1) with { Headline = new Dictionary<string, double> { ["clan-points"] = 9990 } },
            Read(Now.AddDays(-36), 60));

        Assert.Equal(new[]
        {
            new SeriesPoint(first.T, 100, stamp, true, 60),
            new SeriesPoint(second.T, 200, null, false, -300),
        }, reader.HeadlineSeries("s-1", "clan-points", "B"));
        Assert.Equal(new[] { 100d, 200d, 300d }, reader.HeadlineSeries("s-1", "clan-points", null).Select(point => point.Value));
        Assert.Equal(400d, Assert.Single(reader.HeadlineSeries("s-2", "clan-points", "B")).Value);
        Assert.Empty(reader.HeadlineSeries("missing-source", "clan-points", null));
        Assert.Empty(reader.HeadlineSeries("s-1", "missing-headline", null));
        Assert.Empty(reader.HeadlineSeries("s-1", "clan-points", "missing-period"));
    }

    [Fact]
    public void HeadlineDuplicatesUseSourceTimestampsAndTheSixtySecondBoundary()
    {
        var stamp = Now.AddHours(-1);
        var first = Read(Now.AddMinutes(-10), 5) with { AsOf = stamp };
        var changed = Read(Now.AddMinutes(-6), 6) with { AsOf = stamp };
        var unstamped = Read(Now.AddMinutes(-4), 6);
        var boundary = Read(unstamped.T.AddSeconds(60), 6);
        var reader = Reader(
            first,
            Read(Now.AddMinutes(-7), 5) with { AsOf = stamp },
            changed, unstamped,
            Read(unstamped.T.AddSeconds(59), 6),
            boundary);

        Assert.Equal(new[]
        {
            new SeriesPoint(first.T, 50, stamp, false, -300),
            new SeriesPoint(changed.T, 60, stamp, false, -300),
            new SeriesPoint(unstamped.T, 60, null, false, -300),
            new SeriesPoint(boundary.T, 60, null, false, -300),
        }, reader.HeadlineSeries("s-1", "clan-points", "B"));
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
    public void TheLastReadingIsTheNewestOneForThatSourceAlone()
    {
        var reader = Reader(
            Read(Now.AddHours(-4), 100),
            Read(Now.AddHours(-1), 300, source: "s-2"),
            Read(Now.AddHours(-2), 200),
            Final("B", Now, 999, 1));

        // A final is not a reading, so the 2 h old line is still the last thing s-1 read.
        Assert.Equal(Now.AddHours(-2), reader.LastReading("s-1")?.T);
        Assert.Equal(Now.AddHours(-1), reader.LastReading("s-2")?.T);
        Assert.Null(reader.LastReading("s-nothing-here"));
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

        var records = Records.For(reader, Slug, Source.KeyOf(K0i2), "s-1", 111, "value", new ManualTime(Now));

        Assert.Equal((4200d, "B"), (records.BestPeriodValue!.Value, records.BestPeriod));
        Assert.Equal((1, "B"), (records.BestRank!.Value, records.BestRankPeriod));
        Assert.Equal(2, records.PeriodsPlayed);
        Assert.Equal(4200, records.Highest);
        Assert.Equal(800, records.BiggestDay);
        Assert.Equal(1000, records.FastestWeek);
    }

    /// <summary>
    /// Backlog S1-9.3. Two sources' readings of one account are not one series: a clan's points for an account are that
    /// clan's, and a copy of one number read through another source can't be told from a new number. Interleaving them
    /// drew a "rise" from one clan's 50 to the other's 1,010 that neither ever saw.
    /// </summary>
    [Fact]
    public void ARecordIsOneSourcesOwnReadingsAndNeverAnotherSourcesInterleaved()
    {
        var day = new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.FromHours(-5));
        var reader = Reader(
            Read(day.AddHours(9), 50, off: -300, source: "s-1"), Read(day.AddHours(20), 80, off: -300, source: "s-1"),
            Read(day.AddHours(12), 1_000, off: -300, source: "s-2"), Read(day.AddHours(21), 1_010, off: -300, source: "s-2"));

        var first = Records.For(reader, Slug, Source.KeyOf(K0i2), "s-1", 111, "value", new ManualTime(Now));
        var second = Records.For(reader, Slug, Source.KeyOf(K0i2), "s-2", 111, "value", new ManualTime(Now));

        // Merged, both came out 960. Each source's own: 50 to 80, and 1,000 to 1,010.
        Assert.Equal((30d, 30d), (first.BiggestDay!.Value, first.FastestWeek!.Value));
        Assert.Equal((10d, 10d), (second.BiggestDay!.Value, second.FastestWeek!.Value));
    }

    [Fact]
    public void FastestWeekComparesAgainstTheLowestPointInWindowNotTheOldest()
    {
        // A dip then a rise: the true fastest week is the rise from the low point (100 -> 5000 = 4900),
        // not from the first reading (1000 -> 5000 = 4000).
        var reader = Reader(Read(Now.AddDays(-2), 1000), Read(Now.AddDays(-1).AddHours(12), 100), Read(Now, 5000));

        var records = Records.For(reader, Slug, Source.KeyOf(K0i2), "s-1", 111, "value", new ManualTime(Now));

        Assert.Equal(4900, records.FastestWeek);
    }

    [Fact]
    public void FastestWeekCanBeNegativeWhenNothingRises()
    {
        // FastestWeek is the largest rise within any 7-day span; when every pair only falls, it's the
        // largest (least negative) difference, per the doc comment on Records.FastestWeek.
        var reader = Reader(Read(Now.AddDays(-1), 500), Read(Now, 400));

        var records = Records.For(reader, Slug, Source.KeyOf(K0i2), "s-1", 111, "value", new ManualTime(Now));

        Assert.Equal(-100, records.FastestWeek);
    }

    [Fact]
    public void OldReadingsAreEvictedAsTheClockAdvancesEvenWithoutAFullReload()
    {
        // Ruling R5: the cutoff moves forward on every Apply, so a reading kept on an earlier call must
        // still age out later, even though it is never re-added.
        var time = new ManualTime(Now);
        var reader = new ScoreBookReader("unused-root", time);

        reader.Apply(Read(Now, 1));
        time.Advance(TimeSpan.FromDays(36));
        reader.Apply(Read(time.Now, 2));

        Assert.Equal(new[] { 2d }, reader.Series("s-1", 111, "value", null, DateTimeOffset.MinValue).Select(p => p.Value).ToArray());
        Assert.Equal(2, reader.Readings(Slug));
    }

    [Fact]
    public void ALoadOfManyLinesKeepsTheLastFiveWeeksAndApplyStillEvictsAsTheClockAdvances()
    {
        // One reading every 30 minutes for about 62 days, spread over the month files the book writes.
        using var dir = TempDir.Create("urscore-reader");
        var lines = Enumerable.Range(0, 3000).Select(i => Read(Now.AddMinutes(-30 * (2999 - i)), i)).ToList();
        WriteBook(dir.Path, lines);

        var time = new ManualTime(Now);
        var reader = new ScoreBookReader(dir.Path, time);
        reader.Load([Slug]);

        Assert.Equal(3000, reader.Readings(Slug));
        Assert.Equal(lines[0].T, reader.FirstReading(Slug));
        Assert.Equal(
            lines.Where(l => l.T >= Now - ScoreBookReader.KeepReadings).Select(l => l.Accounts["111"].V["value"]).ToArray(),
            reader.Series("s-1", 111, "value", null, DateTimeOffset.MinValue).Select(p => p.Value).ToArray());

        time.Advance(TimeSpan.FromDays(10));
        reader.Apply(Read(time.Now, 5000));

        var cutoff = time.Now - ScoreBookReader.KeepReadings;
        Assert.Equal(
            lines.Where(l => l.T >= cutoff).Select(l => l.Accounts["111"].V["value"]).Append(5000d).ToArray(),
            reader.Series("s-1", 111, "value", null, DateTimeOffset.MinValue).Select(p => p.Value).ToArray());
        Assert.Equal(3001, reader.Readings(Slug));
        Assert.Equal(lines[0].T, reader.FirstReading(Slug));
    }

    [Fact]
    public void AReadingAppliedOutOfOrderStillAgesOut()
    {
        var time = new ManualTime(Now);
        var reader = new ScoreBookReader("unused-root", time);

        reader.Apply(Read(Now, 1));
        reader.Apply(Read(Now.AddDays(-20), 2));
        time.Advance(TimeSpan.FromDays(20));
        reader.Apply(Read(time.Now, 3));

        Assert.Equal(new[] { 1d, 3d }, reader.Series("s-1", 111, "value", null, DateTimeOffset.MinValue).Select(p => p.Value).ToArray());
        Assert.Equal(3, reader.Readings(Slug));
    }

    [Fact]
    public void MalformedLinesInTheBookAreSkippedAndTheRestLoads()
    {
        using var dir = TempDir.Create("urscore-reader");
        var nullInput = BookJson.Serialize(Final("X", Now.AddDays(-3), 1, 9)).Replace("\"clan\":\"K0i2\"", "\"clan\":null", StringComparison.Ordinal);
        var noValues = BookJson.Serialize(Read(Now.AddMinutes(-4), 5)).Replace("{\"v\":{\"value\":5}}", "{}", StringComparison.Ordinal);
        Assert.Contains("\"clan\":null", nullInput);
        Assert.Contains("\"111\":{}", noValues);

        var file = BookFiles.MonthFile(dir.Path, Slug, Now);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, string.Join("\n",
            BookJson.Serialize(Read(Now.AddMinutes(-9), 1)), nullInput, BookJson.Serialize(Final("B", Now.AddDays(-1), 4200, 1)),
            noValues, BookJson.Serialize(Read(Now.AddMinutes(-2), 2))) + "\n", new UTF8Encoding(false));

        var reader = new ScoreBookReader(dir.Path, new ManualTime(Now));
        reader.Load([Slug]);

        Assert.Equal(2, reader.Readings(Slug));
        Assert.Equal(new[] { 1d, 2d }, reader.Series("s-1", 111, "value", null, DateTimeOffset.MinValue).Select(p => p.Value).ToArray());
        Assert.Equal(new[] { "B" }, reader.Finals(Slug, Source.KeyOf(K0i2)).Select(f => f.Period).ToArray());
    }

    private static void WriteBook(string root, IEnumerable<BookLine> lines)
    {
        foreach (var month in lines.GroupBy(l => BookFiles.MonthFile(root, l.Recipe.Slug, l.T)))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(month.Key)!);
            File.AppendAllText(month.Key, string.Concat(month.Select(l => BookJson.Serialize(l) + "\n")), new UTF8Encoding(false));
        }
    }

    [Fact]
    public void ADayIsTheAccountsLocalDayFromTheLinesOffset()
    {
        // 03:30 UTC is 22:30 the evening before at UTC-5, so both readings share one local day.
        var reader = Reader(Read(new DateTimeOffset(2026, 9, 18, 15, 0, 0, TimeSpan.Zero), 10), Read(new DateTimeOffset(2026, 9, 19, 3, 30, 0, TimeSpan.Zero), 70));

        Assert.Equal(60, Records.For(reader, Slug, Source.KeyOf(K0i2), "s-1", 111, "value", new ManualTime(Now)).BiggestDay);
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
