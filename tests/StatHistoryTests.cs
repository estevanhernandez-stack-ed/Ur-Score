using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class StatHistoryTests
{
    private static readonly Guid One = Guid.Parse("9ad5e605-6b41-478c-add3-b916a31a5ab2");
    private static readonly Guid Two = Guid.Parse("88dc7685-3a36-4f93-b526-a9bff2d7da6c");
    private static readonly DateTimeOffset T0 = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheFirstReadShowsTheValueAlone()
    {
        var history = new StatHistory();

        var previous = history.Record(One, "diamonds", 9169613101, T0);

        Assert.Null(previous);
        Assert.Equal("9,169,613,101", StatText.Cell(9169613101, previous));
    }

    [Fact]
    public void ALaterReadShowsTheChangeSinceTheEarlierOne()
    {
        var history = new StatHistory();
        history.Record(One, "diamonds", 9169613101, T0);

        var previous = history.Record(One, "diamonds", 9171913101, T0.AddMinutes(30));

        Assert.Equal("9,171,913,101 (+2,300,000)", StatText.Cell(9171913101, previous));
    }

    [Fact]
    public void ADropShowsAsANegativeChangeAndNoChangeAsPlusZero()
    {
        Assert.Equal("10 (-40)", StatText.Cell(10, new PointsSample(50, T0)));
        Assert.Equal("50 (+0)", StatText.Cell(50, new PointsSample(50, T0)));
    }

    [Fact]
    public void EachAccountAndStatKeepsItsOwnHistory()
    {
        var history = new StatHistory();
        history.Record(One, "diamonds", 100, T0);

        Assert.Null(history.Record(Two, "diamonds", 5, T0));
        Assert.Null(history.Record(One, "eggs", 7, T0));
        Assert.Equal(100, history.Record(One, "diamonds", 120, T0.AddMinutes(1))!.Points);
    }

    [Fact]
    public void ClearForgetsEveryEarlierRead()
    {
        // A new battle: last battle's number beside this one's would be a change that never happened.
        var history = new StatHistory();
        history.Record(One, "value", 4200, T0);

        history.Clear();

        Assert.Null(history.Record(One, "value", 10, T0.AddMinutes(3)));
    }

    [Fact]
    public void LastSentIsTheNumberForOneSentStatAndLabelledForSeveral()
    {
        var diamonds = new SentStat("diamonds", "Diamonds", "ps99.diamonds");
        var rank = new SentStat("rank", "Player rank", "ps99.rank");
        var last = new Dictionary<string, double> { ["diamonds"] = 9169613101, ["rank"] = 12 };

        Assert.Equal("9,169,613,101", StatText.LastSent([diamonds], last));
        Assert.Equal("Diamonds 9,169,613,101 · Player rank 12", StatText.LastSent([diamonds, rank], last));
        Assert.Equal(StatText.Dash, StatText.LastSent([rank], new Dictionary<string, double>()));
    }

    [Fact]
    public void TheNoteIsTheUnavailableMessageElseTheStatsThatCouldNotBeRead()
    {
        Assert.Equal("Profile is private.", StatText.Note("Profile is private.", ["Diamonds"]));
        Assert.Equal("can't read Eggs hatched, Player rank", StatText.Note(null, ["Eggs hatched", "Player rank"]));
        Assert.Equal("", StatText.Note(null, []));
    }
}
