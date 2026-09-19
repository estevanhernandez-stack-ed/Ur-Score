using Labs626.UrScore.Book;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

/// <summary>
/// What a clans list leaves behind on disk: four numbers about the field and how many clans they cover.
/// No clan but your own is named (the owner's ruling, 2026-09-19), so these are the only way a later read
/// can say how fast the field was going.
/// </summary>
public class FieldSummaryTests
{
    private static GroupRow Clan(string name, double points, int? rank = null) =>
        new(name, new Dictionary<string, double> { ["points"] = points }, rank);

    [Fact]
    public void TheSummaryIsFourNumbersAndACount()
    {
        var rows = new List<GroupRow>();
        for (var i = 1; i <= 100; i++) rows.Add(Clan($"C{i}", 1_000_000 * (101 - i)));

        var summary = FieldSummary.Of(rows, "points");

        Assert.Equal(100_000_000, summary[FieldSummary.Leader]);
        Assert.Equal(95_500_000, summary[FieldSummary.Top10]);    // 100M..91M
        Assert.Equal(50_500_000, summary[FieldSummary.Average]);  // 100M..1M
        Assert.Equal(5_500_000, summary[FieldSummary.Bottom10]);  // 10M..1M
        Assert.Equal(100, summary[FieldSummary.Clans]);
        Assert.Equal(5, summary.Count);
    }

    /// <summary>
    /// Measured on the live board 2026-09-19: the list's own rank disagreed with its own points — rank 10 held
    /// more points than rank 9. Ur Score ranks by points and never repeats a rank it was handed.
    /// </summary>
    [Fact]
    public void PointsDecideTheOrderNotTheRankTheListGave()
    {
        var rows = new List<GroupRow> { Clan("A", 181_549_159, rank: 9), Clan("B", 188_236_072, rank: 10) };

        var summary = FieldSummary.Of(rows, "points");

        Assert.Equal(188_236_072, summary[FieldSummary.Leader]);
    }

    /// <summary>A list shorter than ten is its own top and bottom, and the count says how many that was.</summary>
    [Fact]
    public void AShortListIsSummarisedHonestly()
    {
        var rows = new List<GroupRow> { Clan("A", 30), Clan("B", 20), Clan("C", 10) };

        var summary = FieldSummary.Of(rows, "points");

        Assert.Equal(30, summary[FieldSummary.Leader]);
        Assert.Equal(20, summary[FieldSummary.Top10]);
        Assert.Equal(20, summary[FieldSummary.Average]);
        Assert.Equal(20, summary[FieldSummary.Bottom10]);
        Assert.Equal(3, summary[FieldSummary.Clans]);
    }

    [Fact]
    public void RowsWithoutThatValueAreNotCounted()
    {
        var rows = new List<GroupRow>
        {
            Clan("A", 30),
            new("B", new Dictionary<string, double>(), null),
            Clan("C", 10),
        };

        var summary = FieldSummary.Of(rows, "points");

        Assert.Equal(2, summary[FieldSummary.Clans]);
        Assert.Equal(20, summary[FieldSummary.Average]);
    }

    [Fact]
    public void NothingToSummariseWritesNothing()
    {
        Assert.Empty(FieldSummary.Of([], "points"));
        Assert.Empty(FieldSummary.Of([new GroupRow("A", new Dictionary<string, double>(), null)], "points"));
        Assert.Empty(FieldSummary.Of([Clan("A", 30)], ""));
    }

    /// <summary>The keys a book line carries. They name a position in the field, never a clan.</summary>
    [Fact]
    public void TheKeysNameNoClan()
    {
        string[] keys = [FieldSummary.Leader, FieldSummary.Top10, FieldSummary.Average, FieldSummary.Bottom10, FieldSummary.Clans];

        Assert.Equal(["field-avg", "field-bottom10", "field-clans", "field-leader", "field-top10"], keys.Order(StringComparer.Ordinal));
    }
}
