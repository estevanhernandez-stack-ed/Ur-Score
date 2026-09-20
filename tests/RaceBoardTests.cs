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

    /// <summary>Six neighbours need six tellable lines and the theme has five colours, so the sixth is dashed.</summary>
    [Fact]
    public void EveryLineIsTellableFromEveryOther()
    {
        var race = Race(10, Mine);

        var drawn = race.Series.Select(s => (s.Colour, s.Dash)).ToList();
        Assert.Equal(drawn.Count, drawn.Distinct().Count());
        Assert.Contains(drawn, d => d.Dash > 0);
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
