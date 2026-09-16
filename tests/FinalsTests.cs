using System.Globalization;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class FinalsTests
{
    private static readonly Guid A = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DateTimeOffset At = new(2026, 9, 19, 18, 3, 0, TimeSpan.Zero);

    private static Recipe Clan => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

    private static ReadContext Context(SourceRole role = SourceRole.Mine) => new(
        new Source("s-00000001", Clan.Slug, new Dictionary<string, string> { ["clan"] = "K0i2" }, role),
        Clan, "3f9a1c0b7e2d4a55", BookLine.TriggerTimer, At, -300);

    private static RecipeRow Row(long id, double points) => new(id, new Dictionary<string, double> { ["value"] = points });

    private static PastPeriodReading Past(string value, params RecipeRow[] rows) => new(value, rows,
        [new HeadlineValue("Clan place", "40") { Id = "clan-place", Number = 40 }], RowsReadable: rows.Length > 0);

    private static RecipeReading Reading(string current, DateTimeOffset? ends, params PastPeriodReading[] past) =>
        new(ReadingOutcome.Read, null, [], [], $"battle={current}", 0) { Period = new ReadingPeriod(current, null, ends), Past = past };

    /// <summary>A stop that still carried its past periods: no live period, so every key it holds is finished.</summary>
    private static RecipeReading Stopped(ReadingOutcome outcome, params PastPeriodReading[] past) =>
        RecipeReading.Stop(outcome, "No clan battle running") with { Past = past };

    private static readonly HashSet<string> Points = ["value"];

    private static readonly Dictionary<long, Guid> Map = new() { [111] = A };

    [Fact]
    public void EveryFinishedBattleIsBackfilledOnce()
    {
        var index = new FinalsIndex();
        var reading = Reading("C", At.AddDays(3), Past("A", Row(111, 300), Row(222, 200)), Past("B", Row(111, 90)), Past("C", Row(111, 5)));

        var lines = FinalsPlanner.Plan(Context(), reading, Map, Points, index, previousPeriod: null);

        Assert.Equal(new[] { "A", "B" }, lines.Select(l => l.Period!.Value).ToArray());
        Assert.All(lines, l => Assert.Equal((BookLine.KindFinal, BookLine.TriggerBackfill), (l.Kind, l.Trigger)));
        Assert.Equal(new[] { "111" }, lines[0].Accounts.Keys.ToArray());

        foreach (var line in lines) index.Add(line);
        Assert.Empty(FinalsPlanner.Plan(Context(), reading, Map, Points, index, previousPeriod: null));
    }

    [Fact]
    public void AnAltAddedLaterGetsAFinalOfItsOwn()
    {
        var index = new FinalsIndex();
        var reading = Reading("C", null, Past("A", Row(111, 300), Row(333, 250)));
        foreach (var line in FinalsPlanner.Plan(Context(), reading, Map, Points, index, null)) index.Add(line);

        var withAlt = new Dictionary<long, Guid>(Map) { [333] = Guid.NewGuid() };
        var later = Assert.Single(FinalsPlanner.Plan(Context(), reading, withAlt, Points, index, null));

        Assert.Equal(new[] { "333" }, later.Accounts.Keys.ToArray());
        Assert.Equal(40, later.Headline["clan-place"]);
    }

    [Fact]
    public void TheCurrentBattleEndsWhenItsEndTimePasses()
    {
        var index = new FinalsIndex();
        var reading = Reading("B", At.AddMinutes(-1), Past("B", Row(111, 4200)));

        var line = Assert.Single(FinalsPlanner.Plan(Context(), reading, Map, Points, index, "B"));

        Assert.Equal((BookLine.TriggerEnded, "B"), (line.Trigger, line.Period!.Value));
        Assert.True(FinalsPlanner.CurrentPeriodEnded(Context(), reading, index));
        Assert.False(FinalsPlanner.CurrentPeriodEnded(Context(), Reading("B", At.AddMinutes(5)), index));
    }

    [Fact]
    public void ABattleReplacedByTheNextOneEnds()
    {
        var reading = Reading("C", null, Past("B", Row(111, 4200)), Past("C", Row(111, 1)));

        var line = Assert.Single(FinalsPlanner.Plan(Context(), reading, Map, Points, new FinalsIndex(), previousPeriod: "B"));

        Assert.Equal((BookLine.TriggerEnded, "B"), (line.Trigger, line.Period!.Value));
    }

    [Fact]
    public void ABattleWithNoContributionsStillKeepsTheClansResultOnce()
    {
        var index = new FinalsIndex();
        var reading = Reading("C", null, Past("Empty"));

        var line = Assert.Single(FinalsPlanner.Plan(Context(), reading, Map, Points, index, null));
        Assert.Empty(line.Accounts);
        Assert.Equal(40, line.Headline["clan-place"]);

        index.Add(line);
        Assert.Empty(FinalsPlanner.Plan(Context(), reading, Map, Points, index, null));
    }

    [Fact]
    public void AnAccountWithNoTrackedValueNeverAsksForAnotherLine()
    {
        var index = new FinalsIndex();
        var reading = Reading("C", null, Past("A", new RecipeRow(111, new Dictionary<string, double>())));
        foreach (var line in FinalsPlanner.Plan(Context(), reading, Map, Points, index, null)) index.Add(line);

        Assert.Empty(FinalsPlanner.Plan(Context(), reading, Map, Points, index, null));
    }

    [Fact]
    public void AnIdleSourceStillBackfillsTheBattlesItHandedOver()
    {
        // V3-S.1: a clan between battles has no live period, so every past key it carries is finished
        // and the one it just left is the previous period.
        var index = new FinalsIndex();
        var reading = Stopped(ReadingOutcome.Idle, Past("A", Row(111, 300), Row(222, 200)), Past("B", Row(111, 90)));

        var lines = FinalsPlanner.Plan(Context(), reading, Map, Points, index, previousPeriod: "B");

        Assert.Equal(new[] { "A", "B" }, lines.Select(l => l.Period!.Value).ToArray());
        Assert.Equal(new[] { BookLine.TriggerBackfill, BookLine.TriggerEnded }, lines.Select(l => l.Trigger).ToArray());
        Assert.All(lines, l => Assert.Equal(new[] { "111" }, l.Accounts.Keys.ToArray()));

        foreach (var line in lines) index.Add(line);
        Assert.Empty(FinalsPlanner.Plan(Context(), reading, Map, Points, index, previousPeriod: "B"));
    }

    [Theory]
    [InlineData(ReadingOutcome.ShapeNotUnderstood)]
    [InlineData(ReadingOutcome.Unreachable)]
    [InlineData(ReadingOutcome.RateLimited)]
    [InlineData(ReadingOutcome.KeyRejected)]
    public void AStopThatIsNotIdleBackfillsNothing(ReadingOutcome outcome)
    {
        // A response we could not parse is not a response to mine, whatever happens to be attached to it.
        Assert.Empty(FinalsPlanner.Plan(Context(), Stopped(outcome, Past("A", Row(111, 300))), Map, Points, new FinalsIndex(), null));
    }

    [Fact]
    public void AWatchedClansFinalsKeepNoAccounts()
    {
        var line = Assert.Single(FinalsPlanner.Plan(Context(SourceRole.Watch), Reading("C", null, Past("A", Row(111, 300))), Map, Points, new FinalsIndex(), null));

        Assert.Empty(line.Accounts);
    }

    [Fact]
    public void TheIndexLoadsFromTheBookAndIgnoresInputCase()
    {
        using var dir = TempDir.Create("urscore-finals");
        using (var book = new ScoreBook(dir.Path, background: false))
        {
            foreach (var line in FinalsPlanner.Plan(Context(), Reading("C", null, Past("A", Row(111, 300))), Map, Points, new FinalsIndex(), null))
            {
                book.Append(line, "recipe text");
            }
        }

        var index = FinalsIndex.Load(dir.Path);
        var key = Source.KeyOf(new Dictionary<string, string> { ["clan"] = " k0i2" });

        Assert.True(index.HasClan(Clan.Slug, key, "A"));
        Assert.True(index.HasAccount(Clan.Slug, key, "A", 111));
        Assert.False(index.HasAccount(Clan.Slug, key, "A", 222));
    }

    [Fact]
    public void AMalformedLineInTheBookIsSkippedAndTheIndexStillLoads()
    {
        using var dir = TempDir.Create("urscore-finals");
        var lines = FinalsPlanner.Plan(Context(), Reading("C", null, Past("A", Row(111, 300)), Past("B", Row(111, 90))), Map, Points, new FinalsIndex(), null);
        var a = BookJson.Serialize(lines[0]);
        var broken = BookJson.Serialize(lines[1] with { Period = new BookPeriod("X") }).Replace("\"clan\":\"K0i2\"", "\"clan\":null", StringComparison.Ordinal);
        var b = BookJson.Serialize(lines[1]);
        Assert.Contains("\"clan\":null", broken);

        var file = BookFiles.MonthFile(dir.Path, Clan.Slug, At);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, a + "\n" + broken + "\n" + b + "\n", new System.Text.UTF8Encoding(false));

        var index = FinalsIndex.Load(dir.Path);
        var key = Context().Source.InputsKey;

        Assert.True(index.HasClan(Clan.Slug, key, "A"));
        Assert.True(index.HasClan(Clan.Slug, key, "B"));
        Assert.False(index.HasClan(Clan.Slug, key, "X"));
    }
}
