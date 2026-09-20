using Labs626.UrScore.Board;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

/// <summary>
/// The race chart draws the clans you are racing — three either side of yours — each its own line with its own name,
/// and the whole board goes on the card's standings list. Twenty clans in one colour was unreadable, and the ones
/// that decide your place are your neighbours, not the leader.
/// </summary>
public class RaceBoardTests
{
    private const string Period = "SpaceMineBattle2026";

    private static readonly ReadingPeriod LivePeriod = new(Period, Now.AddDays(-2), Now.AddHours(10));

    private static Recipe TopClans => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-top-clans.recipe.json")).Recipe!;

    private static readonly Source Mine = SourceOf("s-00000001", Clan, "K0i2", SourceRole.Main);
    private static readonly Source Other = SourceOf("s-00000002", Clan, "CCGP", SourceRole.Mine);
    private static Source Field => new("s-00000009", TopClans.Slug, new Dictionary<string, string>(), SourceRole.Watch);

    private static BookLine FieldRead(Source field, DateTimeOffset t, IReadOnlyDictionary<string, double> groups) => new(
        BookLine.Version, BookLine.KindRead, t, 0, BookLine.TriggerTimer, new BookRecipeRef(field.Recipe, "0123456789abcdef"),
        field.Id, "watch", field.Inputs, new BookPeriod(Period),
        new Dictionary<string, double> { [FieldSummary.Leader] = groups.Values.Max() }, [],
        new Dictionary<string, BookAccount>(StringComparer.Ordinal), null, null, null, groups);

    /// <summary>A board of clans, best first, with yours at <paramref name="place"/> (1-based).</summary>
    private static Dictionary<string, double> BoardAt(int place, double scale, int count = 25)
    {
        var groups = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var i = 1; i <= count; i++) groups[i == place ? "K0i2" : $"C{i}"] = scale * (count + 1 - i);
        return groups;
    }

    private static (LiveBoard Live, ScoreBookReader Reader) Board(int place, params Source[] sources)
    {
        var field = Field;
        var all = sources.Append(field).ToArray();
        var live = Live(all, [Installed(Clan, "value"), Installed(TopClans)],
            all.ToDictionary(s => s.Id, s => Snapshot(s.Id, [], period: LivePeriod), StringComparer.Ordinal));

        var lines = new List<BookLine>
        {
            FieldRead(field, Now.AddHours(-1), BoardAt(place, 1_000)),
            FieldRead(field, Now, BoardAt(place, 1_200)),
        };
        foreach (var source in sources)
        {
            lines.Add(Read(source, Now.AddHours(-1), Period, new Dictionary<string, double> { ["clan-points"] = 400 }, "value"));
            lines.Add(Read(source, Now, Period, new Dictionary<string, double> { ["clan-points"] = 900 }, "value"));
        }

        return (live, Reader([.. lines]));
    }

    private static RaceModel Race(int place, params Source[] sources)
    {
        var (live, reader) = Board(place, sources);
        return PanelModels.Race(live, reader, new PanelSettings(Clan.Slug, SourceIds: [.. sources.Select(s => s.Id)]));
    }

    [Fact]
    public void TheClansEitherSideOfYouAreDrawnByName()
    {
        var race = Race(10, Mine);

        // Yours, then C7, C8, C9 above and C11, C12, C13 below — the six that decide whether you move a place.
        Assert.Equal(7, race.Series.Count);
        Assert.Equal("★ K0i2", race.Series[0].Label);
        Assert.Equal(["C11", "C12", "C13", "C7", "C8", "C9"], race.Series.Skip(1).Select(s => s.Label).Order(StringComparer.Ordinal));
        Assert.All(race.Legend.Skip(1), item => Assert.Contains("C", item.Text, StringComparison.Ordinal));
    }

    /// <summary>
    /// Every line its own colour, and at the band's size no line has to fall back to a dash. The owner's verdict
    /// on 2026-09-20 was that they were all the same colour — they were: the old set was cyan, magenta, white and
    /// two greys, so six rivals shared three near-identical neutrals. The band is at most eight lines and
    /// <see cref="ChartPalette"/> now has eight colours, so a dash is the overflow it was meant to be.
    /// </summary>
    [Fact]
    public void EveryLineHasItsOwnColourWithNoneLeftOnADash()
    {
        var race = Race(10, Mine, Other);

        var colours = race.Series.Select(s => s.Colour).ToList();
        Assert.Equal(8, colours.Count);
        Assert.Equal(colours.Count, colours.Distinct().Count());
        Assert.All(race.Series, s => Assert.Equal(0, s.Dash));
        Assert.True(colours.Max() < ChartPalette.Count, "a line took a colour the palette does not have");
    }

    /// <summary>The palette has to hold the whole band, or the chart is back to telling lines apart by dash.</summary>
    [Fact]
    public void ThePaletteHoldsTheWholeBand()
    {
        // Yours, plus three either side. A second clan of yours takes one more.
        Assert.True(ChartPalette.Count >= (3 * 2) + 2,
            $"ChartPalette has {ChartPalette.Count} colours; a band of three either side of two of your clans needs 8.");
        Assert.Equal(ChartPalette.Count, ChartPalette.Keys.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>At the top of the board there is nobody above: the band is what there is, never padded out.</summary>
    [Fact]
    public void TheLeaderHasOnlyTheClansBelowIt()
    {
        var race = Race(1, Mine);

        Assert.Equal(4, race.Series.Count);
        Assert.Equal(["C2", "C3", "C4"], race.Series.Skip(1).Select(s => s.Label).Order(StringComparer.Ordinal));
    }

    /// <summary>Two of your clans keep their own colours, and neither is drawn again as a rival.</summary>
    [Fact]
    public void YourOwnClansAreNeverDrawnAsRivals()
    {
        var race = Race(10, Mine, Other);

        Assert.Equal("★ K0i2", race.Series[0].Label);
        Assert.Equal("CCGP", race.Series[1].Label);
        Assert.DoesNotContain(race.Series.Skip(2), s => s.Label is "K0i2" or "CCGP");
    }

    /// <summary>
    /// The names the chart cannot hold live on the card: the whole board by place, with each clan's distance from
    /// yours, and yours marked so it is found at a glance.
    /// </summary>
    [Fact]
    public void TheStandingsCarryTheNamesAndTheGaps()
    {
        var race = Race(10, Mine);

        Assert.True(race.HasStandings);
        Assert.Equal(25, race.Standings.Count);
        Assert.Equal("1st", race.Standings[0].Place);
        Assert.Equal("C1", race.Standings[0].Name);

        var yours = race.Standings.Single(s => s.Yours);
        Assert.Equal("K0i2", yours.Name);
        Assert.Equal("10th", yours.Place);
        Assert.Equal("", yours.Gap);

        // C9 is one place above: 1,200 more at this read's scale.
        Assert.Equal("+1.2K", race.Standings[8].Gap);
        Assert.StartsWith("-", race.Standings[10].Gap, StringComparison.Ordinal);
    }

    /// <summary>
    /// Your own clan is in the book from the battle's first minute; a rival only from the first clans-list read
    /// that kept it. Drawn untrimmed, yours spans the chart and the whole band is a stub in its last tenth — which
    /// is what the owner saw on 2026-09-20: "I can barely see the other clan's lines ... they're over to the right."
    /// The band sets the window instead, and the subtitle says so rather than still claiming the whole battle.
    /// </summary>
    [Fact]
    public void TheBandSetsTheWindowSoEveryLineUsesTheWholeChart()
    {
        var field = Field;
        Source[] all = [Mine, field];
        var live = Live(all, [Installed(Clan, "value"), Installed(TopClans)],
            all.ToDictionary(s => s.Id, s => Snapshot(s.Id, [], period: LivePeriod), StringComparer.Ordinal));

        var reader = Reader(
            Read(Mine, Now.AddHours(-10), Period, new Dictionary<string, double> { ["clan-points"] = 100 }, "value"),
            Read(Mine, Now.AddHours(-5), Period, new Dictionary<string, double> { ["clan-points"] = 300 }, "value"),
            Read(Mine, Now.AddHours(-1), Period, new Dictionary<string, double> { ["clan-points"] = 400 }, "value"),
            Read(Mine, Now, Period, new Dictionary<string, double> { ["clan-points"] = 900 }, "value"),
            FieldRead(field, Now.AddHours(-1), BoardAt(10, 1_000)),
            FieldRead(field, Now, BoardAt(10, 1_200)));

        var race = PanelModels.Race(live, reader, new PanelSettings(Clan.Slug, SourceIds: [Mine.Id]));

        Assert.Equal(7, race.Series.Count);
        Assert.All(race.Series, s => Assert.True(
            s.Points.Count >= 2 && s.Points[0].T >= Now.AddHours(-1),
            $"{s.Label} starts at {(s.Points.Count == 0 ? "nothing" : s.Points[0].T.ToString())}, before the band does"));

        // Yours keeps only the readings inside the window, not the ten hours before it.
        Assert.Equal([400d, 900d], race.Series[0].Points.Select(p => p.Value));
        Assert.Equal("clan points since the clans list was first read", race.Head.Subtitle);
    }

    /// <summary>No band, no trim: your own history is the whole point of the chart when there is nothing to race.</summary>
    [Fact]
    public void WithNothingToRaceYourWholeHistoryIsStillDrawn()
    {
        var live = Live([Mine], [Installed(Clan, "value")],
            new Dictionary<string, RecipeSnapshot> { [Mine.Id] = Snapshot(Mine.Id, [], period: LivePeriod) });
        var reader = Reader(
            Read(Mine, Now.AddHours(-10), Period, new Dictionary<string, double> { ["clan-points"] = 100 }, "value"),
            Read(Mine, Now, Period, new Dictionary<string, double> { ["clan-points"] = 900 }, "value"));

        var race = PanelModels.Race(live, reader, new PanelSettings(Clan.Slug, SourceIds: [Mine.Id]));

        Assert.Equal(2, Assert.Single(race.Series).Points.Count);
        Assert.Equal(Now.AddHours(-10), race.Series[0].Points[0].T);
        Assert.Equal("clan points since the battle started", race.Head.Subtitle);
    }

    /// <summary>With no clans list switched on, the chart is what it always was: the clans you picked, and no list.</summary>
    [Fact]
    public void WithoutAClansListNothingElseIsDrawn()
    {
        var live = Live([Mine], [Installed(Clan, "value")],
            new Dictionary<string, RecipeSnapshot> { [Mine.Id] = Snapshot(Mine.Id, [], period: LivePeriod) });
        var reader = Reader(
            Read(Mine, Now.AddHours(-1), Period, new Dictionary<string, double> { ["clan-points"] = 400 }, "value"),
            Read(Mine, Now, Period, new Dictionary<string, double> { ["clan-points"] = 900 }, "value"));

        var race = PanelModels.Race(live, reader, new PanelSettings(Clan.Slug, SourceIds: [Mine.Id]));

        Assert.Single(race.Series);
        Assert.False(race.HasStandings);
        Assert.True(race.FromZero);
    }
}
