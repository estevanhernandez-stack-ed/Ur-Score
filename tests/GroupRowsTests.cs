using Labs626.UrScore.Book;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

/// <summary>
/// Which clans a list keeps by name. The owner's ruling of 2026-09-20: these are public game standings, so keeping
/// them is fine — but not at any size. The cap is what a chart can use: the top of the board, your own clans, and
/// the places either side of you.
/// </summary>
public class GroupRowsTests
{
    private static GroupRow Clan(string name, double points) =>
        new(name, new Dictionary<string, double> { ["points"] = points }, null);

    private static IReadOnlyList<GroupRow> Board(int count) =>
        [.. Enumerable.Range(1, count).Select(i => Clan($"C{i}", 1_000_000 * (count + 1 - i)))];

    private static IReadOnlySet<string> Mine(params string[] names) => new HashSet<string>(names, StringComparer.Ordinal);

    [Fact]
    public void TheTopOfTheBoardIsKept()
    {
        var kept = GroupRows.Keep(Board(100), "points", null);

        Assert.Equal(GroupRows.Top, kept.Count);
        Assert.Equal(100_000_000, kept["C1"]);
        Assert.True(kept.ContainsKey($"C{GroupRows.Top}"));
        Assert.False(kept.ContainsKey($"C{GroupRows.Top + 1}"));
    }

    /// <summary>Your own clan and the places either side of it, wherever it sits: they are the chase.</summary>
    [Fact]
    public void YourClanAndItsNeighboursAreKeptHoweverFarDownTheyAre()
    {
        var kept = GroupRows.Keep(Board(100), "points", Mine("C60"));

        Assert.True(kept.ContainsKey("C59"));
        Assert.True(kept.ContainsKey("C60"));
        Assert.True(kept.ContainsKey("C61"));
        Assert.False(kept.ContainsKey("C62"));
        Assert.Equal(GroupRows.Top + 3, kept.Count);
    }

    [Fact]
    public void TwoOfYourClansBothKeepTheirNeighbours()
    {
        var kept = GroupRows.Keep(Board(100), "points", Mine("C60", "C80"));

        foreach (var name in new[] { "C59", "C60", "C61", "C79", "C80", "C81" }) Assert.True(kept.ContainsKey(name), name);
    }

    /// <summary>A clan already in the top of the board is not kept twice, and the leader has nobody above it.</summary>
    [Fact]
    public void NothingIsKeptTwiceAndTheLeaderHasNoNeighbourAbove()
    {
        var kept = GroupRows.Keep(Board(100), "points", Mine("C1"));

        Assert.Equal(GroupRows.Top, kept.Count);
        Assert.True(kept.ContainsKey("C2"));
    }

    [Fact]
    public void AShortBoardIsKeptWhole()
    {
        var kept = GroupRows.Keep(Board(4), "points", null);

        Assert.Equal(4, kept.Count);
    }

    [Fact]
    public void RowsWithoutTheValueAreNotKept()
    {
        IReadOnlyList<GroupRow> rows = [Clan("A", 30), new("B", new Dictionary<string, double>(), null), Clan("C", 10)];

        var kept = GroupRows.Keep(rows, "points", null);

        Assert.Equal(new[] { "A", "C" }, kept.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void NothingToKeepIsNoDictionary()
    {
        Assert.Empty(GroupRows.Keep([], "points", null));
        Assert.Empty(GroupRows.Keep(Board(4), "", null));
    }
}
