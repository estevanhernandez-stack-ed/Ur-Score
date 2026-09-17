using System.Globalization;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class LineBuilderTests
{
    private static readonly Guid A = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid B = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static Recipe Clan => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

    private static Recipe Profile => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

    private static Recipe TopClans => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-top-clans.recipe.json")).Recipe!;

    private static Source SourceOf(Recipe recipe, SourceRole role) =>
        new("s-00000001", recipe.Slug, new Dictionary<string, string> { ["clan"] = "K0i2" }, role);

    private static ReadContext Context(Recipe recipe, SourceRole role = SourceRole.Mine) =>
        new(SourceOf(recipe, role), recipe, "3f9a1c0b7e2d4a55", BookLine.TriggerTimer, new DateTimeOffset(2026, 9, 19, 18, 3, 0, TimeSpan.Zero), -300);

    private static RecipeRow Row(long id, double points) => new(id, new Dictionary<string, double> { ["value"] = points });

    private static HeadlineValue Head(string id, double? number) => new(id, number?.ToString(CultureInfo.InvariantCulture)) { Id = id, Number = number };

    private static readonly HashSet<string> Points = ["value"];

    private static RecipeReading ClanReading(IReadOnlyList<RecipeRow> rows, params HeadlineValue[] headline) =>
        new(ReadingOutcome.Read, null, rows, headline, "battle=B", rows.Count) { Period = new ReadingPeriod("B", null, null) };

    [Fact]
    public void OnlyYourAccountsOfFiftyRowsAreWrittenWithTheirRank()
    {
        var rows = Enumerable.Range(1, 50).Select(i => Row(7_000_000 + i, i * 100)).ToList();
        var map = new Dictionary<long, Guid> { [7_000_050] = A, [7_000_001] = B };

        var line = LineBuilder.Reading(Context(Clan), ClanReading(rows, Head("clan-place", 14), Head("clan-points", 30214400)), map, Points)!;

        Assert.Equal(new[] { "7000001", "7000050" }, line.Accounts.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal((1, 50), (line.Accounts["7000050"].Rank!["value"], line.Accounts["7000050"].Of!.Value));
        Assert.Equal(50, line.Accounts["7000001"].Rank!["value"]);
        Assert.Equal(new BookPeriod("B"), line.Period);

        var json = BookJson.Serialize(line);
        foreach (var other in rows.Select(r => r.UserId).Where(id => !map.ContainsKey(id)))
        {
            Assert.DoesNotContain(other.ToString(CultureInfo.InvariantCulture), json);
        }
    }

    /// <summary>
    /// Backlog S1-6.9. A rank is counted among the rows that have that stat, so "of how many" is that count too, per stat. The
    /// line's <c>of</c> stays the row count spec §5.2 defines; <c>ranked</c> is the field each rank was counted in. Nothing
    /// in it names another player: it is a count.
    /// </summary>
    [Fact]
    public void EachRankKeepsHowManyRowsItWasCountedAmongForItsOwnStat()
    {
        // Fifty rows. One has no points; only ten have eggs.
        var rows = Enumerable.Range(1, 50)
            .Select(i => new RecipeRow(7_000_000 + i, new Dictionary<string, double>
                {
                    ["value"] = i * 100,
                    ["eggs"] = i,
                }.Where(kv => !(kv.Key == "value" && i == 3) && !(kv.Key == "eggs" && i <= 40)).ToDictionary(kv => kv.Key, kv => kv.Value)))
            .ToList();
        var map = new Dictionary<long, Guid> { [7_000_050] = A, [7_000_001] = B };

        var line = LineBuilder.Reading(Context(Clan), ClanReading(rows), map, new HashSet<string> { "value", "eggs" })!;
        var json = BookJson.Serialize(line);

        var top = line.Accounts["7000050"];
        Assert.Equal((1, 1, 50), (top.Rank!["value"], top.Rank["eggs"], top.Of!.Value));
        Assert.Equal(new Dictionary<string, int> { ["value"] = 49, ["eggs"] = 10 }, top.Ranked);

        // An account with no eggs has no eggs rank, and so no field for one either.
        var low = line.Accounts["7000001"];
        Assert.Equal(new Dictionary<string, int> { ["value"] = 49 }, low.Ranked);
        Assert.Equal(new[] { "value" }, low.Rank!.Keys.ToArray());

        Assert.Contains("\"of\":50,\"ranked\":{", json, StringComparison.Ordinal);
        Assert.Equal(new Dictionary<string, int> { ["value"] = 49, ["eggs"] = 10 }, BookJson.TryParse(json)!.Accounts["7000050"].Ranked);
    }

    [Fact]
    public void RanksAreCompetitionStyle()
    {
        var ranks = Ranking.Competition([Row(1, 10), Row(2, 20), Row(3, 20), Row(4, 5), new RecipeRow(5, new Dictionary<string, double>())], "value");

        Assert.Equal(new Dictionary<long, int> { [1] = 3, [2] = 1, [3] = 1, [4] = 4 }, ranks);
    }

    [Fact]
    public void TextHeadlinesAndHeadlinesEqualToAnotherPlayersIdAreDropped()
    {
        var rows = new[] { Row(7_000_001, 5), Row(7_000_002, 9) };
        var owner = new HeadlineValue("Owner", "7000002") { Id = "owner", Number = 7_000_002 };
        var motto = new HeadlineValue("Motto", "Go") { Id = "motto", Number = null };

        var line = LineBuilder.Reading(Context(Clan), ClanReading(rows, Head("clan-place", 14), owner, motto),
            new Dictionary<long, Guid> { [7_000_001] = A }, Points)!;

        Assert.Equal(new Dictionary<string, double> { ["clan-place"] = 14 }, line.Headline);
    }

    [Fact]
    public void ShowOnlyStatsAreWrittenAndUntickedOnesAreNot()
    {
        var row = new RecipeRow(7_000_001, new Dictionary<string, double> { ["value"] = 5, ["eggs"] = 9, ["secret"] = 1 });

        var line = LineBuilder.Reading(Context(Clan), ClanReading([row]), new Dictionary<long, Guid> { [7_000_001] = A }, new HashSet<string> { "value", "eggs" })!;

        Assert.Equal(new Dictionary<string, double> { ["value"] = 5, ["eggs"] = 9 }, line.Accounts["7000001"].V);
        Assert.Equal(new[] { "eggs", "value" }, line.Stats);
    }

    [Fact]
    public void UnavailableHoldsOnlyYourIdsAndAPerAccountLineCarriesEachAccountsSourceTime()
    {
        var stamp = new AsOfStamp(new DateTimeOffset(2026, 9, 19, 17, 40, 0, TimeSpan.Zero), true);
        var reading = new RecipeReading(ReadingOutcome.Read, null,
            [new RecipeRow(1, new Dictionary<string, double> { ["diamonds"] = 5 })], [], null, 3)
        {
            Unavailable = new Dictionary<long, string> { [2] = "Profile is private.", [99] = "Profile is private." },
            AccountAsOf = new Dictionary<long, AsOfStamp> { [1] = stamp },
        };
        var map = new Dictionary<long, Guid> { [1] = A, [2] = B };

        var line = LineBuilder.Reading(Context(Profile), reading, map, new HashSet<string> { "diamonds" })!;

        Assert.Equal(new[] { "2" }, line.Unavail);
        Assert.Equal((stamp.Time, (bool?)true), (line.Accounts["1"].AsOf!.Value, line.Accounts["1"].Stale));
        Assert.Null(line.Accounts["1"].Rank);
        Assert.Null(line.Accounts["1"].Of);
        Assert.Null(line.Accounts["1"].Ranked);
    }

    [Fact]
    public void AWatchSourceKeepsTheHeadlineAndNoAccounts()
    {
        var line = LineBuilder.Reading(Context(Clan, SourceRole.Watch), ClanReading([Row(7_000_001, 5)], Head("clan-points", 900)),
            new Dictionary<long, Guid> { [7_000_001] = A }, Points)!;

        Assert.Empty(line.Accounts);
        Assert.Equal(900, line.Headline["clan-points"]);
        Assert.Equal("watch", line.Role);
    }

    [Fact]
    public void NothingOfYoursAndNoHeadlineIsNoLine() =>
        Assert.Null(LineBuilder.Reading(Context(Clan), ClanReading([Row(7_000_001, 5)]), new Dictionary<long, Guid>(), Points));

    [Fact]
    public void GroupListsAndStoppedReadsAreNeverALine()
    {
        var groups = new RecipeReading(ReadingOutcome.Read, null, [], [], null, 1)
        {
            Groups = [new GroupRow("Aurelian", new Dictionary<string, double> { ["value"] = 1 }, 1)],
        };

        Assert.Null(LineBuilder.Reading(Context(TopClans, SourceRole.Watch), groups, new Dictionary<long, Guid>(), Points));
        Assert.Null(LineBuilder.Reading(Context(Clan), RecipeReading.Stop(ReadingOutcome.Idle, "No clan battle running"), new Dictionary<long, Guid> { [1] = A }, Points));
    }

    [Fact]
    public void AFinalLineKeepsOnlyTheAccountsAskedForAndSkipsOnesWithNoTrackedValue()
    {
        var past = new PastPeriodReading("Cannon",
            [Row(7_000_001, 40210), Row(7_000_002, 30), new RecipeRow(7_000_003, new Dictionary<string, double>())],
            [Head("clan-place", 435), Head("clan-points", 67104)], RowsReadable: true);
        var map = new Dictionary<long, Guid> { [7_000_001] = A, [7_000_002] = B, [7_000_003] = Guid.NewGuid() };

        var all = LineBuilder.Final(Context(Clan), past, map, Points, null, BookLine.TriggerBackfill);
        var one = LineBuilder.Final(Context(Clan), past, map, Points, [7_000_002], BookLine.TriggerEnded);

        Assert.Equal((BookLine.KindFinal, BookLine.TriggerBackfill, "Cannon"), (all.Kind, all.Trigger, all.Period!.Value));
        Assert.Equal(new[] { "7000001", "7000002" }, all.Accounts.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal((1, 3), (all.Accounts["7000001"].Rank!["value"], all.Accounts["7000001"].Of!.Value));
        // Three rows, and the third has no points: the rank was counted among two.
        Assert.Equal(2, all.Accounts["7000001"].Ranked!["value"]);
        Assert.Equal(new[] { "7000002" }, one.Accounts.Keys.ToArray());
        Assert.Equal(435, one.Headline["clan-place"]);
    }
}
