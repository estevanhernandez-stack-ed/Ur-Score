using Labs626.UrScore.Board;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

/// <summary>
/// The race chart draws the board itself, not a list you maintain: the top ten while one of your clans is in it, the
/// top twenty when none is, always in the faint colour so your own clans stay the bright lines.
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

    [Fact]
    public void InTheTopTenTheChartDrawsTheTopTen()
    {
        var (live, reader) = Board(place: 4, Mine);

        var race = PanelModels.Race(live, reader, new PanelSettings(Clan.Slug, SourceIds: [Mine.Id]));

        // Your clan, plus the nine others of the top ten.
        Assert.Equal(10, race.Series.Count);
        Assert.Equal("★ K0i2", race.Series[0].Label);
        Assert.All(race.Series.Skip(1), s => Assert.Equal(4, s.Colour));
        Assert.DoesNotContain(race.Series.Skip(1), s => s.Label == "K0i2");
        Assert.Contains(race.Legend, l => l.Text == "9 other clans");
    }

    [Fact]
    public void BelowTheTopTenTheChartDrawsTwenty()
    {
        var (live, reader) = Board(place: 14, Mine);

        var race = PanelModels.Race(live, reader, new PanelSettings(Clan.Slug, SourceIds: [Mine.Id]));

        Assert.Equal(20, race.Series.Count);
        Assert.Contains(race.Legend, l => l.Text == "19 other clans");
    }

    /// <summary>Two of your clans keep their own colours, and neither is drawn twice.</summary>
    [Fact]
    public void YourOwnClansAreNeverDrawnAsOthers()
    {
        var (live, reader) = Board(place: 4, Mine, Other);

        var race = PanelModels.Race(live, reader, new PanelSettings(Clan.Slug, SourceIds: [Mine.Id, Other.Id]));

        Assert.Equal("★ K0i2", race.Series[0].Label);
        Assert.Equal("CCGP", race.Series[1].Label);
        Assert.Equal(0, race.Series[0].Colour);
        Assert.Equal(1, race.Series[1].Colour);
        Assert.Single(race.Series, s => s.Label == "★ K0i2");
    }

    /// <summary>With no clans list switched on, the chart is what it always was: the clans you picked.</summary>
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
        Assert.DoesNotContain(race.Legend, l => l.Text.EndsWith("other clans", StringComparison.Ordinal));
    }
}
