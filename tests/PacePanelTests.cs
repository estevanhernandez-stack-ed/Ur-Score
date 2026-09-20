using Labs626.UrScore.Board;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

/// <summary>
/// The Pace panel, end to end from the book: this clan's own pace, the field's, and what catching the place above
/// would take. Lines are left out rather than guessed — a panel that invents a pace is worse than one that waits.
/// </summary>
public class PacePanelTests
{
    private const string Period = "SpaceMineBattle2026";

    private static readonly ReadingPeriod LivePeriod = new(Period, Now.AddDays(-2), Now.AddHours(10));

    private static Recipe TopClans => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-top-clans.recipe.json")).Recipe!;

    private static readonly Source Mine = SourceOf("s-00000001", Clan, "K0i2", SourceRole.Main);

    private static Source Field => new("s-00000009", TopClans.Slug, new Dictionary<string, string>(), SourceRole.Watch);

    private static Dictionary<string, double> Points(double points) => new() { ["clan-points"] = points };

    private static Dictionary<string, double> FieldLine(double leader, double mine, double above) => new()
    {
        [FieldSummary.Leader] = leader,
        [FieldSummary.Top10] = leader / 2,
        [FieldSummary.Average] = leader / 8,
        [FieldSummary.Mine] = mine,
        [FieldSummary.MineRank] = 9,
        [FieldSummary.Above] = above,
        [FieldSummary.GapAbove] = above - mine,
    };

    private static LiveBoard Board(params Source[] sources) =>
        Live(sources, [Installed(Clan, "value"), Installed(TopClans)],
            sources.ToDictionary(s => s.Id, s => Snapshot(s.Id, [], period: LivePeriod), StringComparer.Ordinal));

    private static string ValueOf(PaceModel model, string label) =>
        model.Facts.FirstOrDefault(f => f.Label.StartsWith(label, StringComparison.Ordinal))?.Value ?? "(absent)";

    [Fact]
    public void OneClanAlonePacesItselfAndSaysOverWhat()
    {
        var reader = Reader(
            Read(Mine, Now.AddHours(-4), Period, Points(100), "value"),
            Read(Mine, Now.AddHours(-1), Period, Points(700), "value"),
            Read(Mine, Now, Period, Points(1_000), "value"));

        var model = PacePanel.Of(Board(Mine), reader, new PanelSettings(Clan.Slug, SourceId: Mine.Id));

        Assert.Equal("K0i2", model.Head.Subtitle);
        Assert.Equal("300/h over 1h", ValueOf(model, "Current"));
        Assert.StartsWith("225/h since ", ValueOf(model, "Average"));
        Assert.StartsWith("300/h since ", ValueOf(model, "Best hour"));

        // 1,000 now, 300/h, ten hours left.
        Assert.StartsWith("4K by ", ValueOf(model, "On this pace"));
    }

    /// <summary>
    /// The measurement that made this rule: 25 minutes of readings, carried across 140 hours, projected 26 billion
    /// points on the owner's own board. Under a quarter of an hour there is no pace, and nothing is projected from one.
    /// </summary>
    [Fact]
    public void TooLittleHistorySaysSoInsteadOfProjecting()
    {
        var reader = Reader(
            Read(Mine, Now.AddMinutes(-5), Period, Points(900), "value"),
            Read(Mine, Now, Period, Points(1_000), "value"));

        var model = PacePanel.Of(Board(Mine), reader, new PanelSettings(Clan.Slug, SourceId: Mine.Id));

        Assert.Equal(PaceText.TooEarly, ValueOf(model, "Current"));
        Assert.Equal(PaceText.TooEarly, ValueOf(model, "Average"));
        Assert.Equal(PaceText.TooEarly, ValueOf(model, "Best hour"));
        Assert.Equal(PaceText.TooEarly, ValueOf(model, "On this pace"));
        Assert.Equal("(absent)", ValueOf(model, "To pass"));
    }

    [Fact]
    public void WithAClansListTheFieldAndTheChaseAreThereToo()
    {
        var field = Field;
        var reader = Reader(
            Read(Mine, Now.AddHours(-2), Period, Points(400), "value"),
            Read(Mine, Now, Period, Points(1_000), "value"),
            Read(field, Now.AddHours(-2), Period, FieldLine(leader: 8_000, mine: 400, above: 900), "value"),
            Read(field, Now, Period, FieldLine(leader: 10_000, mine: 1_000, above: 1_100), "value"));

        var model = PacePanel.Of(Board(Mine, field), reader, new PanelSettings(Clan.Slug, SourceId: Mine.Id));

        Assert.Equal("1K/h", ValueOf(model, "The leader"));
        Assert.Equal("500/h", ValueOf(model, "Top 10"));
        Assert.Equal("125/h", ValueOf(model, "The field"));

        // 100 behind, you 300/h, they 100/h: closing 200/h, so half an hour.
        Assert.Equal("passed in 30m at these paces", ValueOf(model, "To pass 8th"));
    }

    /// <summary>A chase nobody can win says so, against this clan's own best hour rather than a feeling.</summary>
    [Fact]
    public void AChaseBeyondYourBestHourIsCalledOutOfReach()
    {
        var field = Field;
        var reader = Reader(
            Read(Mine, Now.AddHours(-2), Period, Points(400), "value"),
            Read(Mine, Now, Period, Points(1_000), "value"),
            Read(field, Now.AddHours(-2), Period, FieldLine(leader: 8_000, mine: 400, above: 40_000), "value"),
            Read(field, Now, Period, FieldLine(leader: 10_000, mine: 1_000, above: 60_000), "value"));

        var model = PacePanel.Of(Board(Mine, field), reader, new PanelSettings(Clan.Slug, SourceId: Mine.Id));

        Assert.Equal("out of reach even if they stop now", ValueOf(model, "To pass 8th"));
    }

    /// <summary>A watched clan paces exactly as your own does: the book keeps a watched clan's totals too.</summary>
    [Fact]
    public void AWatchedClanPacesTheSameWay()
    {
        var watched = SourceOf("s-00000002", Clan, "CCGP", SourceRole.Watch);
        var reader = Reader(
            Read(watched, Now.AddHours(-2), Period, Points(200), "value"),
            Read(watched, Now, Period, Points(800), "value"));

        var model = PacePanel.Of(Board(watched), reader, new PanelSettings(Clan.Slug, SourceId: watched.Id));

        Assert.Equal("CCGP", model.Head.Subtitle);
        Assert.Equal("watching", model.Head.Chip);
        Assert.Equal("300/h over 2h", ValueOf(model, "Current"));
    }

    /// <summary>
    /// The owner asked on 2026-09-20 whether "Now" was their pace or the clan's. It was always the clan's; their own
    /// accounts now have their own lines, from readings the book already kept.
    /// </summary>
    [Fact]
    public void YourOwnAccountsHaveTheirOwnPaceAndShare()
    {
        var reader = Reader(
            Read(Mine, Now.AddHours(-2), Period, Points(400), "value", (101, 20), (201, 10)),
            Read(Mine, Now, Period, Points(1_000), "value", (101, 60), (201, 30)));

        var model = PacePanel.Of(Board(Mine), reader, new PanelSettings(Clan.Slug, SourceId: Mine.Id));

        // The clan gained 600 in two hours; these two accounts 60 of it.
        Assert.Equal("300/h over 2h", ValueOf(model, "Current"));
        Assert.Equal("30/h", ValueOf(model, "Your 2 accounts"));
        Assert.Equal("90 · 9.0% of the clan", ValueOf(model, "Your share"));
    }
}
