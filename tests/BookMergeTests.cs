using Labs626.UrScore.Book;
using Labs626.UrScore.Core;

namespace UrScore.Tests;

/// <summary>
/// Bringing a score book from another PC. Each machine gives a source its own id, so the same clan arrives under a
/// name Ur Score has never seen: lines are matched to a local source by what the source IS — its recipe and the clan
/// it was set up for — not by the id the other machine happened to mint.
/// </summary>
public class BookMergeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private const string Slug = "pet-sim-99-clan-battle-points";

    private static Source Local(string id, string clan) =>
        new(id, Slug, new Dictionary<string, string> { ["clan"] = clan }, SourceRole.Main);

    private static BookLine Line(string sourceId, string clan, DateTimeOffset t, double points, string kind = BookLine.KindRead) => new(
        BookLine.Version, kind, t, 0, BookLine.TriggerTimer, new BookRecipeRef(Slug, "0123456789abcdef"),
        sourceId, "main", new Dictionary<string, string> { ["clan"] = clan }, new BookPeriod("B"),
        new Dictionary<string, double> { ["clan-points"] = points }, ["value"],
        new Dictionary<string, BookAccount>(StringComparer.Ordinal));

    [Fact]
    public void LinesArriveUnderThisPcsOwnSourceId()
    {
        var merge = BookMerge.Plan(
            [Line("their-id", "K0i2", Now, 500)],
            [Local("mine-id", "K0i2")],
            []);

        var line = Assert.Single(merge.Lines);
        Assert.Equal("mine-id", line.Source);
        Assert.Equal(500, line.Headline["clan-points"]);
        Assert.Equal(1, merge.Added);
    }

    /// <summary>The same reading twice is one reading: a book you have already brought in adds nothing the second time.</summary>
    [Fact]
    public void WhatIsAlreadyHereIsNotAddedAgain()
    {
        var mine = Line("mine-id", "K0i2", Now, 500);

        var merge = BookMerge.Plan([Line("their-id", "K0i2", Now, 500)], [Local("mine-id", "K0i2")], [mine]);

        Assert.Empty(merge.Lines);
        Assert.Equal(0, merge.Added);
        Assert.Equal(1, merge.AlreadyHere);
    }

    /// <summary>A clan that is not set up here is left alone, and said by name so it can be added.</summary>
    [Fact]
    public void AClanThisPcDoesNotFollowIsReportedNotGuessed()
    {
        var merge = BookMerge.Plan(
            [Line("their-id", "CCGP", Now, 90), Line("their-id-2", "K0i2", Now, 500)],
            [Local("mine-id", "K0i2")],
            []);

        Assert.Single(merge.Lines);
        Assert.Equal(1, merge.Added);
        Assert.Equal(["CCGP"], merge.NotSetUp);
    }

    /// <summary>Finals come across too: a finished battle is the part of a book you most want from another PC.</summary>
    [Fact]
    public void FinishedPeriodsComeAcrossAsWell()
    {
        var merge = BookMerge.Plan(
            [Line("their-id", "K0i2", Now, 500, BookLine.KindFinal)],
            [Local("mine-id", "K0i2")],
            []);

        Assert.Equal(BookLine.KindFinal, Assert.Single(merge.Lines).Kind);
    }

    /// <summary>Two sources of one recipe: each line goes to the clan it was actually about.</summary>
    [Fact]
    public void EachClanFindsItsOwnSource()
    {
        var merge = BookMerge.Plan(
            [Line("a", "K0i2", Now, 500), Line("b", "CCGP", Now, 90)],
            [Local("mine-1", "K0i2"), Local("mine-2", "CCGP")],
            []);

        Assert.Equal(["mine-1", "mine-2"], merge.Lines.Select(l => l.Source).Order(StringComparer.Ordinal));
    }

    /// <summary>A clan name typed differently on the other PC is still the same clan.</summary>
    [Fact]
    public void ClanNamesMatchHoweverTheyWereTyped()
    {
        var merge = BookMerge.Plan([Line("their-id", "k0i2", Now, 500)], [Local("mine-id", "K0i2")], []);

        Assert.Single(merge.Lines);
    }

    [Fact]
    public void AnEmptyBookIsNoWork()
    {
        var merge = BookMerge.Plan([], [Local("mine-id", "K0i2")], []);

        Assert.Empty(merge.Lines);
        Assert.Equal(0, merge.Added);
        Assert.Empty(merge.NotSetUp);
    }
}
