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

    /// <summary>
    /// A read keeps names only because the recipe SAYS its groups are clans. "Group" is whatever a recipe's
    /// <c>groupName</c> points at, so the shape of a list says nothing about what is in it: a recipe we did not
    /// write whose rows are PLAYERS would put strangers' usernames on disk down this exact path, which the owner's
    /// 2026-09-20 ruling never covered. The claim has to be made by the recipe, and is false until it is (V3-S.25).
    /// </summary>
    [Fact]
    public void NothingIsKeptByNameUntilTheRecipeSaysItsGroupsAreClans()
    {
        Assert.Empty(GroupRows.Keep(Board(100), "points", Mine("C60"), groupsAreClans: false));
        Assert.NotEmpty(GroupRows.Keep(Board(100), "points", Mine("C60"), groupsAreClans: true));
    }

    [Fact]
    public void TheTopOfTheBoardIsKept()
    {
        var kept = GroupRows.Keep(Board(100), "points", null, groupsAreClans: true);

        Assert.Equal(GroupRows.Top, kept.Count);
        Assert.Equal(100_000_000, kept["C1"]);
        Assert.True(kept.ContainsKey($"C{GroupRows.Top}"));
        Assert.False(kept.ContainsKey($"C{GroupRows.Top + 1}"));
    }

    /// <summary>
    /// Your own clan and the places either side of it, wherever it sits: they are the chase. The count is stated
    /// against <see cref="GroupRows.Neighbours"/> rather than against a number typed here, because a test that
    /// names its own copy of the size is the very drift this pair of constants was joined to stop (V3-S.34).
    /// </summary>
    [Fact]
    public void YourClanAndItsNeighboursAreKeptHoweverFarDownTheyAre()
    {
        var kept = GroupRows.Keep(Board(100), "points", Mine("C60"), groupsAreClans: true);

        Assert.True(kept.ContainsKey("C60"));
        Assert.True(kept.ContainsKey($"C{60 - GroupRows.Neighbours}"));
        Assert.True(kept.ContainsKey($"C{60 + GroupRows.Neighbours}"));
        Assert.False(kept.ContainsKey($"C{60 + GroupRows.Neighbours + 1}"));
        Assert.Equal(GroupRows.Top + (GroupRows.Neighbours * 2) + 1, kept.Count);
    }

    [Fact]
    public void TwoOfYourClansBothKeepTheirNeighbours()
    {
        var kept = GroupRows.Keep(Board(100), "points", Mine("C60", "C80"), groupsAreClans: true);

        foreach (var name in new[] { "C59", "C60", "C61", "C79", "C80", "C81" }) Assert.True(kept.ContainsKey(name), name);
    }

    /// <summary>A clan already in the top of the board is not kept twice, and the leader has nobody above it.</summary>
    [Fact]
    public void NothingIsKeptTwiceAndTheLeaderHasNoNeighbourAbove()
    {
        var kept = GroupRows.Keep(Board(100), "points", Mine("C1"), groupsAreClans: true);

        Assert.Equal(GroupRows.Top, kept.Count);
        Assert.True(kept.ContainsKey("C2"));
    }

    [Fact]
    public void AShortBoardIsKeptWhole()
    {
        var kept = GroupRows.Keep(Board(4), "points", null, groupsAreClans: true);

        Assert.Equal(4, kept.Count);
    }

    [Fact]
    public void RowsWithoutTheValueAreNotKept()
    {
        IReadOnlyList<GroupRow> rows = [Clan("A", 30), new("B", new Dictionary<string, double>(), null), Clan("C", 10)];

        var kept = GroupRows.Keep(rows, "points", null, groupsAreClans: true);

        Assert.Equal(new[] { "A", "C" }, kept.Keys.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The band the race chart draws and the band the book keeps are the same band, or the chart quietly shows less
    /// than it was asked for. The chart asks for <see cref="GroupRows.Neighbours"/> either side; until 2026-09-21
    /// the book kept one either side, so a clan placed below the top 25 lost four of its six neighbours and the
    /// chart gave no sign of it. Nobody saw it because the owner's clan has been placed high, where the top-25 rule
    /// covers the band by accident (V3-S.34).
    /// <para>
    /// C40 is deliberately well below <see cref="GroupRows.Top"/>, so every neighbour here is kept for being a
    /// neighbour and none of them for being near the top. A fixture with the clan at, say, 24th would pass under
    /// the old one-either-side rule and prove nothing.
    /// </para>
    /// </summary>
    [Fact]
    public void AMidTableClanKeepsTheWholeBandTheChartDraws()
    {
        var kept = GroupRows.Keep(Board(100), "points", Mine("C40"), groupsAreClans: true);

        for (var away = -GroupRows.Neighbours; away <= GroupRows.Neighbours; away++)
        {
            Assert.True(kept.ContainsKey($"C{40 + away}"), $"C{40 + away}, {away} from yours, was not kept");
        }

        // And not a place further, or the cap the ruling set stops meaning anything.
        Assert.False(kept.ContainsKey($"C{40 - GroupRows.Neighbours - 1}"));
        Assert.False(kept.ContainsKey($"C{40 + GroupRows.Neighbours + 1}"));
    }

    /// <summary>
    /// The band the chart draws and the band the book keeps must be the same size, and this asserts the sizes
    /// rather than how they are spelled — a literal that happens to agree is not a bug, a literal that drifts is.
    /// The chart's copy is private, so this reaches it by reflection instead of pretending it is public.
    /// </summary>
    [Fact]
    public void TheChartAsksForNoMoreThanTheBookKeeps()
    {
        var drawn = typeof(Labs626.UrScore.Board.PanelModels)
            .GetField("Neighbours", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(drawn);
        Assert.Equal(GroupRows.Neighbours, Assert.IsType<int>(drawn!.GetRawConstantValue()));
    }

    [Fact]
    public void NothingToKeepIsNoDictionary()
    {
        Assert.Empty(GroupRows.Keep([], "points", null, groupsAreClans: true));
        Assert.Empty(GroupRows.Keep(Board(4), "", null, groupsAreClans: true));
    }
}
