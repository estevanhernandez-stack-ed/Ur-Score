using System.Diagnostics;
using Labs626.UrScore.Book;
using Xunit.Abstractions;

namespace UrScore.Tests;

/// <summary>
/// Sweep D's numbers (S1-F.2, S1-F.3): how long the book takes to load, and how long one board's worth of chart
/// queries takes, on a book of a season's size. Every run measures; only a run with <c>URSCORE_BENCH=1</c> in the
/// environment measures at full size (five clan sources and a clans list, five weeks, a reading every three
/// minutes), which is what <c>tools/bench/book-bench.ps1</c> sets. Without it the same code runs on a day's book
/// in well under a second, so the harness itself is exercised on every ordinary run and cannot rot unseen.
/// <para>
/// The numbers are printed, not asserted: a timing assertion is a flaky test waiting for a slow machine. What is
/// asserted is that the generator wrote what it says and the reader read all of it — the harness has to be right
/// before its numbers mean anything (V3-S.37).
/// </para>
/// </summary>
public class BookBenchmarks(ITestOutputHelper output)
{
    public const string Switch = "URSCORE_BENCH";

    private static bool Full => Environment.GetEnvironmentVariable(Switch) == "1";

    [Fact]
    [Trait("Category", "Bench")]
    public void LoadingAndChartingASeasonsBook()
    {
        using var dir = TempDir.Create("urscore-bench");
        var (clanSources, days) = Full ? (5, 35) : (2, 1);
        var written = BookGenerator.Write(dir.Path, clanSources, days);
        output.WriteLine($"book: {clanSources} clan sources + 1 list, {days} day(s): {written.Lines:N0} lines, {written.Finals:N0} finals, {written.Bytes / 1024.0 / 1024.0:F1} MB");

        var time = new FixedTime(BookGenerator.End);
        var before = GC.GetTotalMemory(forceFullCollection: true);

        // Startup as AppServices does it: one pass of the files feeding the reader and the finals index (S1-F.2).
        // Before that change it was FinalsIndex.Load then reader.Load, two passes: 2,246 ms on this book.
        var sw = Stopwatch.StartNew();
        var finals = new FinalsIndex();
        var reader = new ScoreBookReader(dir.Path, time);
        var skipped = reader.Load(BookFiles.Slugs(dir.Path), finals);
        var load = sw.Elapsed;
        var after = GC.GetTotalMemory(forceFullCollection: true);

        output.WriteLine($"startup: reader and finals index in one pass {load.TotalMilliseconds:F0} ms; reader holds {(after - before) / 1024.0 / 1024.0:F1} MB");

        // The harness is right before its numbers mean anything.
        Assert.Equal(0, skipped);
        Assert.Equal(0, finals.Skipped);
        Assert.Equal(written.Lines - written.Finals, reader.Readings(BookGenerator.ClanSlug) + reader.Readings(BookGenerator.ListSlug));
        Assert.Equal(written.Finals / clanSources, reader.Finals(BookGenerator.ClanSlug, "clan=clan0").Count);

        // One board's worth: every clan source's headline line and each of your accounts' lines over the current
        // battle, its last reading, and the list's top ten by name. Repeated so the number is a mean, not a first call.
        var period = reader.LastReading(written.ClanSources[0])!.Period!.Value;
        var since = BookGenerator.End.AddDays(-BookGenerator.PeriodDays);
        var groups = reader.GroupsLatest(written.ListSource, period).Take(10).Select(g => g.Name).ToList();
        var points = 0;
        const int boards = 10;
        sw.Restart();
        for (var i = 0; i < boards; i++)
        {
            points = 0;
            foreach (var source in written.ClanSources)
            {
                points += reader.HeadlineSeries(source, "clan-points", period).Count;
                foreach (var id in BookGenerator.Accounts) points += reader.Series(source, id, "value", period, since).Count;
                _ = reader.LastReading(source);
            }

            foreach (var name in groups) points += reader.GroupSeries(written.ListSource, name, period).Count;
        }

        var perBoard = sw.Elapsed / boards;
        output.WriteLine($"charts: one board's worth ({written.ClanSources.Count * (1 + BookGenerator.Accounts.Length) + groups.Count} series, "
                         + $"{points:N0} points) in {perBoard.TotalMilliseconds:F1} ms, mean of {boards}");

        // Values climb every reading, so nothing collapses: a battle's worth of readings is a battle's worth of points.
        var perBattle = reader.Series(written.ClanSources[0], BookGenerator.Accounts[0], "value", period, since).Count;
        Assert.True(perBattle > 0);
        Assert.Equal(perBattle, reader.HeadlineSeries(written.ClanSources[0], "clan-points", period).Count);
    }
}
