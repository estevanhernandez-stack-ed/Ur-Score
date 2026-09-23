using System.Diagnostics;
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

    /// <summary>
    /// An engine whose read blocks until the test opens a gate, and honours cancellation while it waits. The
    /// suite's <see cref="StubEngine"/> answers at once and ignores the token, which cannot show what happens to
    /// a read that is IN FLIGHT when its source is removed — the whole question of S1-8.2, S1-8.3 and S1-8.4.
    /// </summary>
    private sealed class GatedEngine : IRecipeEngine
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _started;
        private int _finished;
        private int _cancelled;

        public int Started => Volatile.Read(ref _started);

        public int Finished => Volatile.Read(ref _finished);

        public int Cancelled => Volatile.Read(ref _cancelled);

        /// <summary>Set when the first read is waiting at the gate, so a test can act while one is in flight.</summary>
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Set when a read has SEEN its cancellation. The cancel itself lands on the token at once, but the read's
        /// own catch runs on a pool thread a moment later, and a test that counted cancellations before that moment
        /// read a zero — once in twelve runs on 2026-09-22. Awaited, not assumed.
        /// </summary>
        public TaskCompletionSource CancelledOnce { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Open() => _gate.TrySetResult();

        public async Task<RecipeReading> ReadAsync(Recipe recipe, IReadOnlyDictionary<string, string> inputs,
            IReadOnlyCollection<long> accountUserIds, IReadOnlySet<string> trackedStats, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _started);
            Waiting.TrySetResult();
            try
            {
                await _gate.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _cancelled);
                CancelledOnce.TrySetResult();
                throw;
            }

            Interlocked.Increment(ref _finished);
            return Reading();
        }
    }

    private static SourceHost HostOf(IRecipeEngine engine, MemoryBook book, StubHost? host = null, Func<Source, int>? interval = null) =>
        new(source => new RecipeWatch(engine, host ?? new StubHost(true, Alt), new NoKeys(), new ReportPolicy([], new HashSet<Guid>()),
                Clan, source.Inputs, new HashSet<string> { "value" }, book, source, recipeText: ClanText),
            interval ?? (_ => 180));

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

    /// <summary>
    /// An Apply whose factory throws changes nothing: the sources it would have removed are still there and the
    /// ones it had already built are not added. The factory used to run inside the lock, in the same pass as the
    /// removals and the adds, so a throw on the third source left the first two applied and the rest not — a
    /// host in a state no caller asked for, with the caller's exception the only sign (S1-8.5). Now every watch
    /// is built first and applied after, so the exception means "not applied" and nothing else.
    /// </summary>
    [Fact]
    public void AnApplyWhoseFactoryThrowsAppliesNothing()
    {
        var factory = new Factory(new StubEngine(Reading), new MemoryBook());
        using var host = new SourceHost(
            source => source.Id == "s-3" ? throw new InvalidOperationException("the factory is broken") : factory.Create(source),
            _ => 180);
        host.Apply([SourceNamed("s-1", "CCGP")]);
        var first = host.WatchFor("s-1");

        Assert.Throws<InvalidOperationException>(() => host.Apply([SourceNamed("s-2", "K0i2"), SourceNamed("s-3", "NovaForge")]));

        Assert.Same(first, host.WatchFor("s-1"));
        Assert.Null(host.WatchFor("s-2"));
        Assert.Null(host.WatchFor("s-3"));
    }

    /// <summary>
    /// A slow factory holds up nobody but the Apply that called it. Inside the lock it held up every caller of
    /// the host — a loop filing its snapshot, the window asking which watch a source has — for as long as the
    /// factory took, and Apply runs on the UI thread, so "slow" there was the window frozen (S1-8.5). The factory
    /// here blocks until released; another thread's <c>WatchFor</c> has to answer while it is blocked.
    /// </summary>
    [Fact]
    public async Task ASlowFactoryDoesNotHoldUpTheHostsOtherCallers()
    {
        var factory = new Factory(new StubEngine(Reading), new MemoryBook());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = new SourceHost(
            source =>
            {
                if (source.Id == "s-2")
                {
                    entered.TrySetResult();
                    release.Task.Wait();
                }

                return factory.Create(source);
            },
            _ => 180);
        host.Apply([SourceNamed("s-1", "CCGP")]);

        var applying = Task.Run(() => host.Apply([SourceNamed("s-1", "CCGP"), SourceNamed("s-2", "K0i2")]));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var asked = Task.Run(() => host.WatchFor("s-1"));
        var answered = await Task.WhenAny(asked, Task.Delay(TimeSpan.FromMilliseconds(500))) == asked;
        release.TrySetResult();
        await applying.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(answered, "WatchFor waited on the factory");
        Assert.NotNull(await asked);
        Assert.NotNull(host.WatchFor("s-2"));
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

    /// <summary>
    /// The loop reads again when its interval has passed and not a moment before. This is the timer-driven read
    /// S1-8.7 could not test while the wait ran on the wall clock — the floor is a minute, so proving the second
    /// read meant sitting through one. The host's clock is now a <see cref="TimeProvider"/> and the test's is
    /// moved by hand: the loop is caught waiting (its timer registered), the clock is advanced to a second short
    /// of the interval, then over it, and the second read carries the timer trigger.
    /// </summary>
    [Fact]
    public async Task TheLoopReadsAgainWhenTheIntervalHasPassedAndNotBefore()
    {
        var time = new ManualTime(new DateTimeOffset(2026, 9, 19, 18, 0, 0, TimeSpan.Zero));
        var engine = new StubEngine(Reading);
        var book = new MemoryBook();
        using var host = new SourceHost(new Factory(engine, book).Create, _ => 180, time);
        host.Apply([SourceNamed("s-1", "CCGP")]);

        host.Start();
        await WaitingAsync(time);
        Assert.Equal(1, engine.Calls);

        time.Advance(TimeSpan.FromSeconds(179));
        Assert.Equal(1, engine.Calls);

        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.SnapshotReady += (_, _) => second.TrySetResult();
        time.Advance(TimeSpan.FromSeconds(1));
        await second.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, engine.Calls);
        Assert.Equal(new[] { BookLine.TriggerStart, BookLine.TriggerTimer }, book.Lines.Select(l => l.Trigger).ToArray());
    }

    /// <summary>
    /// The floor holds in the loop, not only in <see cref="SourceHost.DelaySeconds"/>: a lookup asking for five
    /// seconds gets no second read at five seconds and gets one at the floor. Cheap now, and the one that says
    /// the clamp is actually wired to the wait (S1-8.7).
    /// </summary>
    [Fact]
    public async Task TheLoopWaitsAtLeastTheFloorWhateverTheLookupAsks()
    {
        var time = new ManualTime(new DateTimeOffset(2026, 9, 19, 18, 0, 0, TimeSpan.Zero));
        var engine = new StubEngine(Reading);
        using var host = new SourceHost(new Factory(engine, new MemoryBook()).Create, _ => 5, time);
        host.Apply([SourceNamed("s-1", "CCGP")]);

        host.Start();
        await WaitingAsync(time);

        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, engine.Calls);

        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.SnapshotReady += (_, _) => second.TrySetResult();
        time.Advance(TimeSpan.FromSeconds(Recipe.MinimumEverySeconds - 5));
        await second.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, engine.Calls);
    }

    /// <summary>
    /// A source removed while its loop waits between reads never reads again: the wait is cut short and the loop
    /// leaves, so advancing past the interval brings no read for it. The other half of S1-8.7's removal question —
    /// a removal during a read — is covered by the gated-engine tests above.
    /// </summary>
    [Fact]
    public async Task ASourceRemovedBetweenReadsDoesNotReadAgainWhenTheIntervalPasses()
    {
        var time = new ManualTime(new DateTimeOffset(2026, 9, 19, 18, 0, 0, TimeSpan.Zero));
        var engine = new StubEngine(Reading);
        using var host = new SourceHost(new Factory(engine, new MemoryBook()).Create, _ => 180, time);
        host.Apply([SourceNamed("s-1", "CCGP")]);

        host.Start();
        await WaitingAsync(time);

        host.Apply([]);
        await Task.Delay(50);
        time.Advance(TimeSpan.FromSeconds(600));
        await Task.Delay(50);

        Assert.Equal(1, engine.Calls);
        Assert.Equal(0, time.Waiting);
    }

    /// <summary>The loop is between reads: its wait is registered on the clock. Bounded, so a loop that never waits is a named failure.</summary>
    private static async Task WaitingAsync(ManualTime time)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (time.Waiting == 0)
        {
            Assert.True(DateTime.UtcNow < until, "the loop never reached its wait");
            await Task.Delay(10);
        }
    }

    /// <summary>
    /// A recipe asking to be read more often than the floor allows is clamped to it. The floor exists to protect
    /// somebody else's server, so a recipe cannot opt out of it by asking for five seconds — and an interval
    /// ABOVE the floor has to survive untouched, or the clamp would quietly make every recipe poll every minute.
    /// Both directions, because a clamp written as Min rather than Max passes a test that only checks the low
    /// side (S1-8.7).
    /// </summary>
    [Theory]
    [InlineData(5, Recipe.MinimumEverySeconds)]
    [InlineData(0, Recipe.MinimumEverySeconds)]
    [InlineData(-30, Recipe.MinimumEverySeconds)]
    [InlineData(Recipe.MinimumEverySeconds, Recipe.MinimumEverySeconds)]
    [InlineData(300, 300)]
    public void AnIntervalIsClampedToTheFloorAndNoFurther(int asked, int expected) =>
        Assert.Equal(expected, SourceHost.DelaySeconds(_ => asked, SourceNamed("s-1", "CCGP")));

    /// <summary>
    /// The caller's interval lookup throwing must never end a source's loop. That failure would be total (the
    /// source stops reading), permanent (nothing restarts the loop) and silent (no snapshot, no state change, no
    /// trail line) — the worst combination available, and reachable from an ordinary bug in whatever computes
    /// an interval. The loop falls back to the floor and carries on (S1-8.7).
    /// </summary>
    [Fact]
    public void AnIntervalLookupThatThrowsFallsBackToTheFloorRatherThanEndingTheLoop()
    {
        var asked = 0;

        var seconds = SourceHost.DelaySeconds(
            _ =>
            {
                asked++;
                throw new InvalidOperationException("the interval lookup is broken");
            },
            SourceNamed("s-1", "CCGP"));

        Assert.Equal(Recipe.MinimumEverySeconds, seconds);
        Assert.Equal(1, asked);
    }

    /// <summary>
    /// Stop then Start reads again. Stop cancels the run token and drops it; Start builds a NEW one and restarts
    /// every entry's loop, so the question is whether an entry survives its loop being cancelled — nothing
    /// pinned that it does, and a source that went quiet after a Stop/Start would look exactly like a source that
    /// was never switched on (S1-8.7).
    /// </summary>
    [Fact]
    public async Task StopThenStartReadsAgain()
    {
        var engine = new StubEngine(Reading);
        using var host = new SourceHost(new Factory(engine, new MemoryBook()).Create, _ => 180);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.SnapshotReady += (_, _) => ready.TrySetResult();
        host.Apply([SourceNamed("s-1", "CCGP")]);

        host.Start();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        host.Stop();
        Assert.False(host.Running);

        var again = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.SnapshotReady += (_, _) => again.TrySetResult();
        host.Start();

        Assert.True(host.Running);
        await again.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(engine.Calls >= 2);
    }

    /// <summary>
    /// A second Start while already running changes nothing. Without the guard each Start would add another loop
    /// per entry, so a source read once a minute would be read twice, then three times — against somebody
    /// else's server, and the floor that exists to protect it would be worth nothing (S1-8.7).
    /// </summary>
    [Fact]
    public async Task StartingTwiceDoesNotDoubleTheReading()
    {
        var engine = new StubEngine(Reading);
        using var host = new SourceHost(new Factory(engine, new MemoryBook()).Create, _ => 180);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.SnapshotReady += (_, _) => ready.TrySetResult();
        host.Apply([SourceNamed("s-1", "CCGP")]);

        host.Start();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        host.Start();
        host.Start();

        // The interval is 180s, so with no second loop there can be no second read inside this window; another
        // loop would read immediately on starting, which is what makes the count decisive rather than a guess.
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        Assert.Equal(1, engine.Calls);
    }

    /// <summary>
    /// Dispose stops the reading and can be called twice. It runs from the composition root on the way down, and
    /// on that path a second Dispose or a Dispose after Stop is ordinary rather than exotic (S1-8.7).
    /// </summary>
    [Fact]
    public async Task DisposeStopsTheReadingAndIsSafeTwice()
    {
        var engine = new StubEngine(Reading);
        var host = new SourceHost(new Factory(engine, new MemoryBook()).Create, _ => 180);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.SnapshotReady += (_, _) => ready.TrySetResult();
        host.Apply([SourceNamed("s-1", "CCGP")]);

        host.Start();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));

        host.Dispose();
        Assert.False(host.Running);
        host.Dispose();
        host.Stop();

        var after = engine.Calls;
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        Assert.Equal(after, engine.Calls);
    }

    /// <summary>
    /// Start and Stop repeatedly, because each pair used to leave a cancellation source and one linked source per
    /// entry behind and now disposes them (S1-8.1). Disposal is the part that can bite: a loop holds a source
    /// LINKED to the one Stop disposes, and a linked source outliving its parent is the assumption the fix rests
    /// on. If it were wrong this throws ObjectDisposedException rather than leaking quietly, which is why the
    /// cycle is run ten times and the host is still asked to read afterwards.
    /// </summary>
    [Fact]
    public async Task StartAndStopManyTimesReleasesItsCancellationSourcesAndStillReads()
    {
        var engine = new StubEngine(Reading);
        using var host = new SourceHost(new Factory(engine, new MemoryBook()).Create, _ => 180);
        host.Apply([SourceNamed("s-1", "CCGP"), SourceNamed("s-2", "K0i2")]);

        for (var i = 0; i < 10; i++)
        {
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Ready(string id, RecipeSnapshot snapshot) => ready.TrySetResult();
            host.SnapshotReady += Ready;
            host.Start();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            host.Stop();
            host.SnapshotReady -= Ready;
        }

        Assert.False(host.Running);
        await host.RunAllNowAsync(BookLine.TriggerManual, CancellationToken.None);
        Assert.Equal(WatchState.Showing, host.Latest["s-1"].State);
    }

    /// <summary>
    /// Pause, then the status card's ⟳ (or F5, or Test now): the read must actually run. Stop cancels the token
    /// every read links to, so the read in flight stops, and only Start used to replace it, so every read-now after
    /// a Pause was cancelled before it began and the card's promise that ⟳ reads while paused was false (UIA walk,
    /// 2026-09-23). Counted in the book by trigger, because a snapshot left over from the Start would pass a
    /// check of <see cref="SourceHost.Latest"/> whether or not the read-now ran.
    /// </summary>
    [Fact]
    public async Task AfterAPauseReadNowStillReadsEverySource()
    {
        var engine = new StubEngine(Reading);
        var book = new MemoryBook();
        using var host = new SourceHost(new Factory(engine, book).Create, _ => 180);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.SnapshotReady += (_, _) => ready.TrySetResult();
        host.Apply([SourceNamed("s-1", "CCGP"), SourceNamed("s-2", "K0i2")]);

        host.Start();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        host.Stop();

        await host.RunAllNowAsync(BookLine.TriggerManual, CancellationToken.None);

        var manual = book.Lines.Where(line => line.Trigger == BookLine.TriggerManual).Select(line => line.Source);
        Assert.Equal(new[] { "s-1", "s-2" }, manual.Order(StringComparer.Ordinal));
        Assert.False(host.Running);
    }

    /// <summary>
    /// The other side of the fix above: read-now replaces a cancelled read token only for a Pause, never after
    /// exit. Exit cancels the same token last of all, and a read-now that revived it would read, record and send
    /// for a host that is shutting down.
    /// </summary>
    [Fact]
    public async Task AfterExitReadNowReadsNothing()
    {
        var engine = new StubEngine(Reading);
        var book = new MemoryBook();
        var host = new SourceHost(new Factory(engine, book).Create, _ => 180);
        host.Apply([SourceNamed("s-1", "CCGP")]);
        host.Start();
        host.Stop();
        host.Dispose();
        var before = engine.Calls;

        await host.RunAllNowAsync(BookLine.TriggerManual, CancellationToken.None);

        Assert.Equal(before, engine.Calls);
        Assert.DoesNotContain(book.Lines, line => line.Trigger == BookLine.TriggerManual);
    }

    /// <summary>
    /// Test now used to run each read under the caller's token alone, so a source removed while its read was in
    /// flight had only its snapshot hidden: the read went on, recorded to the book and sent to RoRoRo for a source
    /// that was gone. The timer loop's reads were always cancelled by a removal; Test now's are now too (S1-8.3).
    /// The engine is gated so the removal lands with the read provably in flight, and the gate is opened AFTER the
    /// removal so that, were the read not cancelled, it would go on to finish and record — which is the failure
    /// this test exists to catch, and the one it showed before the fix.
    /// </summary>
    [Fact]
    public async Task ASourceRemovedWhileTestNowIsReadingItHasThatReadCancelledNotHidden()
    {
        var engine = new GatedEngine();
        var book = new MemoryBook();
        using var host = HostOf(engine, book);
        host.Apply([SourceNamed("s-1", "CCGP")]);

        var testNow = host.RunAllNowAsync(BookLine.TriggerManual, CancellationToken.None);
        await engine.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));

        host.Apply([]);
        engine.Open();
        await testNow.WaitAsync(TimeSpan.FromSeconds(5));
        await engine.CancelledOnce.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, engine.Cancelled);
        Assert.Equal(0, engine.Finished);
        Assert.Empty(book.Lines);
        Assert.Empty(host.Latest);
    }

    /// <summary>
    /// The fetch honoured the token; nothing after it did. So a read whose fetch had RETURNED by the time its
    /// source was removed still recorded and sent, because the only cancellation point was behind it. There is
    /// now one after the fetch too (S1-8.3). The engine answers at once here and the caller's own token is
    /// cancelled between the answer and the record, which is exactly the window.
    /// </summary>
    [Fact]
    public async Task AReadCancelledAfterItsFetchReturnsRecordsAndSendsNothing()
    {
        var book = new MemoryBook();
        var cancel = new CancellationTokenSource();
        // Cancels from inside the engine's answer: the fetch has "returned", the token is now cancelled, and the
        // next thing the watch does decides whether that matters.
        var engine = new StubEngine(() => { cancel.Cancel(); return Reading(); });
        var sent = new StubHost(true, Alt);
        using var host = HostOf(engine, book, sent);
        host.Apply([SourceNamed("s-1", "CCGP")]);

        await host.RunAllNowAsync(BookLine.TriggerManual, cancel.Token);

        Assert.Equal(1, engine.Calls);
        Assert.Empty(book.Lines);
        Assert.Empty(sent.Reported);
    }

    /// <summary>
    /// Switching a clan off and straight back on while it is being read: the old watch's read is cancelled by
    /// the removal and the new watch starts its own, so for a moment there are two — one draining, one
    /// starting. What the row feared was two watches READING: a burst against the server and a doubled line in
    /// the book (S1-8.4). This pins the bound: the old read is cancelled, never finishes, and never records; the
    /// new one reads once. Two starts, one finish, one line. A third start or a second line is the defect.
    /// </summary>
    [Fact]
    public async Task OffAndStraightBackOnDuringAReadLeavesOneReaderStanding()
    {
        var engine = new GatedEngine();
        var book = new MemoryBook();
        using var host = HostOf(engine, book);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.SnapshotReady += (_, _) => ready.TrySetResult();
        host.Apply([SourceNamed("s-1", "CCGP")]);

        host.Start();
        await engine.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, engine.Started);

        host.Apply([SourceNamed("s-1", "CCGP") with { Enabled = false }]);
        host.Apply([SourceNamed("s-1", "CCGP")]);
        engine.Open();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await engine.CancelledOnce.Task.WaitAsync(TimeSpan.FromSeconds(5));
        host.Stop();

        Assert.Equal(2, engine.Started);
        Assert.Equal(1, engine.Cancelled);
        Assert.Equal(1, engine.Finished);
        Assert.Single(book.Lines);
    }

    /// <summary>
    /// Exit gives a read in flight a moment to land before cancelling it. Dispose used to cancel everything at
    /// once, so a read that had already fetched lost its lines every time the window happened to close during
    /// one — at most one read's worth, and a read a person could have kept for a moment's patience (S1-8.2).
    /// The gate is opened from another thread a beat after Dispose begins, inside the grace, and the line is
    /// expected in the book; under the old code the read is cancelled before the gate opens and the book is empty.
    /// </summary>
    [Fact]
    public async Task ExitWaitsBrieflyForAReadInFlightBeforeCancellingIt()
    {
        var engine = new GatedEngine();
        var book = new MemoryBook();
        var host = HostOf(engine, book);
        host.Apply([SourceNamed("s-1", "CCGP")]);

        host.Start();
        await engine.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));

        _ = Task.Run(async () => { await Task.Delay(300); engine.Open(); });
        host.Dispose();

        Assert.Equal(1, engine.Finished);
        Assert.Equal(0, engine.Cancelled);
        Assert.Single(book.Lines);
    }

    /// <summary>
    /// And the grace is a grace, not a wait: a read that never lands is cancelled once the grace is up, so exit
    /// cannot hang on a dead network. Cheap to pin and expensive to lose.
    /// </summary>
    [Fact]
    public async Task ExitDoesNotWaitLongerThanTheGraceForAReadThatNeverLands()
    {
        var engine = new GatedEngine();
        var host = HostOf(engine, new MemoryBook());
        host.Apply([SourceNamed("s-1", "CCGP")]);

        host.Start();
        await engine.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var began = Stopwatch.GetTimestamp();
        host.Dispose();
        var took = Stopwatch.GetElapsedTime(began);

        Assert.True(took >= SourceHost.ExitGrace - TimeSpan.FromMilliseconds(50), $"exit gave up too early: {took.TotalMilliseconds:F0} ms");
        Assert.True(took < SourceHost.ExitGrace + TimeSpan.FromSeconds(2), $"exit hung past the grace: {took.TotalMilliseconds:F0} ms");
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
