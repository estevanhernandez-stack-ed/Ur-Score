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

    /// <summary>Your own clans, the way AppServices hands them over: the clan names your sources were set up with.</summary>
    private static IReadOnlySet<string> Mine(params string[] names) => new HashSet<string>(names, StringComparer.Ordinal);

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

    /// <summary>
    /// Where you stand in the field, for the catch-up pace: your own clan's points as the same read saw them, the
    /// place you hold by points, the points of the place directly above you, and the gap to it. The place above is
    /// a position, never a clan: whoever holds it, the series keeps meaning the same thing.
    /// </summary>
    [Fact]
    public void YourPlaceAndTheOneAboveYouAreKept()
    {
        var rows = new List<GroupRow> { Clan("UN0", 700), Clan("K0i2", 180), Clan("CCGP", 90), Clan("BOSS", 200) };

        var summary = FieldSummary.Of(rows, "points", Mine("K0i2", "CCGP"));

        Assert.Equal(180, summary[FieldSummary.Mine]);
        Assert.Equal(3, summary[FieldSummary.MineRank]);
        Assert.Equal(200, summary[FieldSummary.Above]);
        Assert.Equal(20, summary[FieldSummary.GapAbove]);
    }

    /// <summary>The leader has nobody above it, so there is no gap to keep — not a zero, which would read as a tie.</summary>
    [Fact]
    public void TheLeaderHasNoPlaceAbove()
    {
        var rows = new List<GroupRow> { Clan("K0i2", 700), Clan("BOSS", 200) };

        var summary = FieldSummary.Of(rows, "points", Mine("K0i2"));

        Assert.Equal(700, summary[FieldSummary.Mine]);
        Assert.Equal(1, summary[FieldSummary.MineRank]);
        Assert.False(summary.ContainsKey(FieldSummary.Above));
        Assert.False(summary.ContainsKey(FieldSummary.GapAbove));
    }

    /// <summary>Your clan outside the list (it hasn't joined, or it is past the hundredth) leaves the field alone.</summary>
    [Fact]
    public void WithoutYourClanOnlyTheFieldIsKept()
    {
        var rows = new List<GroupRow> { Clan("UN0", 700), Clan("BOSS", 200) };

        var summary = FieldSummary.Of(rows, "points", Mine("K0i2"));

        Assert.Equal(700, summary[FieldSummary.Leader]);
        Assert.False(summary.ContainsKey(FieldSummary.Mine));
        Assert.False(summary.ContainsKey(FieldSummary.Above));
    }

    /// <summary>Two of your clans in one list: the better placed one is the one the catch-up numbers are about.</summary>
    [Fact]
    public void TheBetterPlacedOfYourClansIsTheOneMeasured()
    {
        var rows = new List<GroupRow> { Clan("UN0", 700), Clan("CCGP", 300), Clan("K0i2", 180) };

        var summary = FieldSummary.Of(rows, "points", Mine("K0i2", "CCGP"));

        Assert.Equal(300, summary[FieldSummary.Mine]);
        Assert.Equal(2, summary[FieldSummary.MineRank]);
        Assert.Equal(400, summary[FieldSummary.GapAbove]);
    }

    /// <summary>A clan name matches however it was typed: Setup takes what you type, the list has its own casing.</summary>
    [Fact]
    public void YourClansNameMatchesWhateverItsCasing()
    {
        var rows = new List<GroupRow> { Clan("UN0", 700), Clan("K0i2", 180) };

        Assert.Equal(180, FieldSummary.Of(rows, "points", Mine("k0i2"))[FieldSummary.Mine]);
    }

    /// <summary>
    /// The counts behind two decisions a clan leader makes during a battle: whether there is a slot to move an alt
    /// into, and how much of the roster is sitting out. Kept for your own clan only, and counts name nobody.
    /// Measured on K0i2 on 2026-09-19: 74 of 75 members, 64 of them scoring.
    /// </summary>
    [Fact]
    public void YourClansRosterCountsAreKept()
    {
        var rows = new List<GroupRow>
        {
            new("UN0", new Dictionary<string, double> { ["points"] = 700, ["members"] = 75, ["capacity"] = 75, ["contributors"] = 70 }, 1),
            new("K0i2", new Dictionary<string, double> { ["points"] = 280, ["members"] = 74, ["capacity"] = 75, ["contributors"] = 64 }, 2),
        };

        var summary = FieldSummary.Of(rows, "points", Mine("K0i2"));

        Assert.Equal(74, summary[FieldSummary.MineMembers]);
        Assert.Equal(75, summary[FieldSummary.MineCapacity]);
        Assert.Equal(64, summary[FieldSummary.MineContributors]);
    }

    /// <summary>A list that carries no roster counts keeps none: an absent count must never read as a full clan.</summary>
    [Fact]
    public void RosterCountsAreKeptOnlyWhenTheListCarriesThem()
    {
        var rows = new List<GroupRow> { Clan("UN0", 700), Clan("K0i2", 280) };

        var summary = FieldSummary.Of(rows, "points", Mine("K0i2"));

        Assert.False(summary.ContainsKey(FieldSummary.MineMembers));
        Assert.False(summary.ContainsKey(FieldSummary.MineCapacity));
        Assert.False(summary.ContainsKey(FieldSummary.MineContributors));
    }

    /// <summary>
    /// The clans just below the best placed of yours, best first, with none of yours among them. Ranked by the
    /// value itself, never by the rank the list handed over, as every other field number is.
    /// </summary>
    [Fact]
    public void BehindTakesTheClansBelowTheBestPlacedOfYours()
    {
        var rows = new List<GroupRow>
        {
            Clan("Leader", 900), Clan("K0i2", 500), Clan("H8ER", 400), Clan("LXCC", 300), Clan("R0W", 200), Clan("Tail", 100),
        };

        var behind = FieldSummary.Behind(rows, "points", Mine("K0i2"), count: 3);

        Assert.Equal(["H8ER", "LXCC", "R0W"], behind.Select(b => b.Name));
        Assert.Equal(400, behind[0].Value);

        // Gap is OUR points minus THEIRS: how far behind us that clan is. Pinned explicitly, not just implied by
        // the ordering above, because a Gap with the sign flipped would still pass every other assertion here
        // (owner's ruling, 2026-09-20).
        Assert.Equal(100, behind[0].Gap);
        Assert.Equal([100, 200, 300], behind.Select(b => b.Gap));
    }

    /// <summary>With none of yours in the read there is nobody to be behind, which is nothing rather than the tail.</summary>
    [Fact]
    public void BehindIsEmptyWhenNoneOfYoursIsInTheRead()
    {
        IReadOnlyList<GroupRow> rows = [Clan("Leader", 900), Clan("H8ER", 400)];

        Assert.Empty(FieldSummary.Behind(rows, "points", Mine("K0i2"), count: 3));
    }

    /// <summary>
    /// A clan name matches however it was typed, exactly as <see cref="FieldSummary.Of"/> already matches it
    /// (FieldSummary.cs:92-94): Setup takes what you type, the list has its own casing, and a live-correct result
    /// today must not depend on the caller happening to hand over an already case-insensitive set. Without the
    /// fold, "k0i2" fails to find "K0i2" at all, and <see cref="FieldSummary.Behind"/> reports nobody behind you
    /// instead of the clans that are actually there.
    /// </summary>
    [Fact]
    public void BehindFindsYourRowHoweverItsCasingWasTyped()
    {
        var rows = new List<GroupRow>
        {
            Clan("Leader", 900), Clan("K0i2", 500), Clan("H8ER", 400), Clan("LXCC", 300), Clan("R0W", 200), Clan("Tail", 100),
        };

        var behind = FieldSummary.Behind(rows, "points", Mine("k0i2"), count: 3);

        Assert.Equal(["H8ER", "LXCC", "R0W"], behind.Select(b => b.Name));
        Assert.Equal(100, behind[0].Gap);
    }

    /// <summary>
    /// With two of your clans in the read, the lower one is excluded from "behind" by the same fold, not only
    /// when it is spelled exactly as Setup typed it. Anchor-finding alone would not catch this: skipping the
    /// anchor's own index already keeps it out regardless of casing, so this needs a second clan of yours,
    /// stored under different casing, sitting below the anchor.
    /// </summary>
    [Fact]
    public void BehindExcludesEachOfYourClansHoweverItsCasingWasTyped()
    {
        var rows = new List<GroupRow>
        {
            Clan("Leader", 900), Clan("K0i2", 500), Clan("H8ER", 400), Clan("LXCC", 300), Clan("R0W", 200), Clan("Tail", 100),
        };

        var behind = FieldSummary.Behind(rows, "points", Mine("K0i2", "h8er"), count: 3);

        Assert.Equal(["LXCC", "R0W", "Tail"], behind.Select(b => b.Name));
    }
}
