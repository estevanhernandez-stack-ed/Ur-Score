using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class SourceHostTests
{
    private static readonly HostAccount Alt = new(Guid.Parse("9ad5e605-6b41-478c-add3-b916a31a5ab2"), 111, "Alt One");

    private static readonly string ClanText = RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json");

    private static Recipe Clan => RecipeParser.Parse(ClanText).Recipe!;

    private static Source SourceNamed(string id, string clan, SourceRole role = SourceRole.Mine) =>
        new(id, Clan.Slug, new Dictionary<string, string> { ["clan"] = clan }, role);

    private static RecipeReading Reading() =>
        new(ReadingOutcome.Read, null, [new RecipeRow(111, new Dictionary<string, double> { ["value"] = 5 })],
            [new HeadlineValue("Clan place", "3") { Id = "clan-place", Number = 3 }], "battle=B", 1);

    private sealed class Factory(StubEngine engine, MemoryBook book)
    {
        public int Created { get; private set; }

        public RecipeWatch Create(Source source)
        {
            Created++;
            return new RecipeWatch(engine, new StubHost(true, Alt), new NoKeys(), new ReportPolicy([], new HashSet<Guid>()),
                Clan, source.Inputs, new HashSet<string> { "value" }, book, source, recipeText: ClanText);
        }
    }

    [Fact]
    public void ASourceKeepsOneWatchAcrossAppliesAndTakesItsNewRole()
    {
        var factory = new Factory(new StubEngine(Reading), new MemoryBook());
        using var host = new SourceHost(factory.Create, _ => 180);

        host.Apply([SourceNamed("s-1", "CCGP")]);
        var first = host.WatchFor("s-1");
        host.Apply([SourceNamed("s-1", "CCGP", SourceRole.Main)]);

        Assert.Equal(1, factory.Created);
        Assert.Same(first, host.WatchFor("s-1"));
        Assert.Equal(SourceRole.Main, host.WatchFor("s-1")!.Source!.Role);
    }

    [Fact]
    public async Task RemovedAndSwitchedOffSourcesLoseTheirWatchAndSnapshot()
    {
        var factory = new Factory(new StubEngine(Reading), new MemoryBook());
        using var host = new SourceHost(factory.Create, _ => 180);
        host.Apply([SourceNamed("s-1", "CCGP"), SourceNamed("s-2", "K0i2")]);
        await host.RunAllNowAsync(BookLine.TriggerManual, CancellationToken.None);

        host.Apply([SourceNamed("s-1", "CCGP") with { Enabled = false }]);

        Assert.Null(host.WatchFor("s-1"));
        Assert.Null(host.WatchFor("s-2"));
        Assert.Empty(host.Latest);
    }

    [Fact]
    public async Task TestNowReadsEverySourceAndRaisesEachSnapshot()
    {
        var engine = new StubEngine(Reading);
        var book = new MemoryBook();
        using var host = new SourceHost(new Factory(engine, book).Create, _ => 180);
        var seen = new List<string>();
        host.SnapshotReady += (id, _) => { lock (seen) seen.Add(id); };
        host.Apply([SourceNamed("s-1", "CCGP"), SourceNamed("s-2", "K0i2")]);

        await host.RunAllNowAsync(BookLine.TriggerManual, CancellationToken.None);

        Assert.Equal(new[] { "s-1", "s-2" }, seen.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(2, engine.Calls);
        Assert.Equal("s-2", host.Latest["s-2"].SourceId);
        Assert.Equal(new[] { "s-1", "s-2" }, book.Lines.Select(line => line.Source).Order(StringComparer.Ordinal));
        Assert.Equal("CCGP", Assert.Single(book.Lines, line => line.Source == "s-1").Inputs["clan"]);
        Assert.Equal("K0i2", Assert.Single(book.Lines, line => line.Source == "s-2").Inputs["clan"]);
        Assert.All(book.Lines, line =>
        {
            Assert.Equal(BookLine.TriggerManual, line.Trigger);
            Assert.Equal(BookLine.KindRead, line.Kind);
            Assert.Equal(5d, line.Accounts["111"].V["value"]);
        });
        Assert.True(host.Latest["s-1"].Recorded);
        Assert.True(host.Latest["s-2"].Recorded);
    }

    [Fact]
    public async Task StartReadsEverySourceAtOnceWithTheStartTriggerAndStopEndsIt()
    {
        var book = new MemoryBook();
        using var host = new SourceHost(new Factory(new StubEngine(Reading), book).Create, _ => 180);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.SnapshotReady += (_, _) => ready.TrySetResult();
        host.Apply([SourceNamed("s-1", "CCGP")]);

        host.Start();
        Assert.True(host.Running);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        host.Stop();

        Assert.False(host.Running);
        Assert.Equal(BookLine.TriggerStart, book.Lines[0].Trigger);
    }

    [Fact]
    public async Task ASourceAddedWhileRunningStartsReading()
    {
        using var host = new SourceHost(new Factory(new StubEngine(Reading), new MemoryBook()).Create, _ => 180);
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.SnapshotReady += (id, _) => ready.TrySetResult(id);

        host.Start();
        host.Apply([SourceNamed("s-9", "NovaForge", SourceRole.Watch)]);

        Assert.Equal("s-9", await ready.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task AReadThatThrowsBecomesASnapshotThatNamesOnlyTheErrorsType()
    {
        var engine = new StubEngine(() => throw new InvalidOperationException("failed at https://example.com/?key=SECRET-VALUE"));
        using var host = new SourceHost(new Factory(engine, new MemoryBook()).Create, _ => 180);
        host.Apply([SourceNamed("s-1", "CCGP")]);

        await host.RunAllNowAsync(BookLine.TriggerManual, CancellationToken.None);

        var snapshot = host.Latest["s-1"];
        Assert.Equal(WatchState.SourceUnreachable, snapshot.State);
        Assert.Contains("InvalidOperationException", snapshot.Detail);
        Assert.DoesNotContain("SECRET-VALUE", snapshot.Detail);
    }

    [Fact]
    public async Task AThrowingSnapshotSubscriberDoesNotStopReading()
    {
        var engine = new StubEngine(Reading);
        using var host = new SourceHost(new Factory(engine, new MemoryBook()).Create, _ => 180);
        host.SnapshotReady += (_, _) => throw new InvalidOperationException("subscriber boom");
        host.Apply([SourceNamed("s-1", "CCGP")]);

        await host.RunAllNowAsync(BookLine.TriggerManual, CancellationToken.None);
        await host.RunAllNowAsync(BookLine.TriggerManual, CancellationToken.None);

        Assert.Equal(2, engine.Calls);
        Assert.Equal("s-1", host.Latest["s-1"].SourceId);
    }

    [Fact]
    public async Task ARemovalDuringAReadIsNeverOverwrittenByThatReadsSnapshot()
    {
        SourceHost? host = null;
        var engine = new StubEngine(() =>
        {
            // Force the interleaving: the source is gone by the time this read finishes.
            host!.Apply([]);
            return Reading();
        });

        host = new SourceHost(new Factory(engine, new MemoryBook()).Create, _ => 180);
        using (host)
        {
            var sawEvent = false;
            host.SnapshotReady += (_, _) => sawEvent = true;
            host.Apply([SourceNamed("s-1", "CCGP")]);

            await host.RunAllNowAsync(BookLine.TriggerManual, CancellationToken.None);

            Assert.False(host.Latest.ContainsKey("s-1"));
            Assert.False(sawEvent);
        }
    }
}
