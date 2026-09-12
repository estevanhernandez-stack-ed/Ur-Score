using Labs626.UrScore.Core;
using Labs626.UrScore.Host;
using Labs626.UrScore.Source;

namespace UrScore.Tests;

public class ScoreWatchTests
{
    private static readonly Guid Mine = Guid.Parse("9ad5e605-6b41-478c-add3-b916a31a5ab2");

    private sealed class FakeSource(BattleProbe probe, ContributionsResult contributions) : IClanSource
    {
        /// <summary>Mutable so one source can be driven across a battle changing mid-sequence,
        /// which is what the stale-line test needs — a fresh source for the later cycle would
        /// pass whether or not the old line was cleared.</summary>
        public BattleProbe Probe { get; set; } = probe;

        public ContributionsResult Contributions { get; set; } = contributions;

        public Task<BattleProbe> ActiveBattleAsync(CancellationToken ct) => Task.FromResult(Probe);

        public Task<ContributionsResult> ContributionsAsync(string clan, string config, CancellationToken ct) =>
            Task.FromResult(Contributions);
    }

    private sealed class FakeHost(bool reachable, IReadOnlyList<HostAccount> accounts) : IHostClient
    {
        public List<(Guid Subject, double Value)> Reported { get; } = [];
        public bool ThrowPermissionDenied { get; set; }

        /// <summary>Mutable so one watch can be driven across the host going away and coming back,
        /// which is the only way the no-backlog test can actually fail.</summary>
        public bool Reachable { get; set; } = reachable;

        public Task<bool> IsReachableAsync(CancellationToken ct) => Task.FromResult(Reachable);

        public Task<IReadOnlyList<HostAccount>> GetAccountsAsync(CancellationToken ct) =>
            Task.FromResult(accounts);

        public Task ReportMetricAsync(
            Guid subject, string metricId, double value, DateTimeOffset observedAt, CancellationToken ct)
        {
            if (ThrowPermissionDenied)
            {
                throw new Grpc.Core.RpcException(
                    new Grpc.Core.Status(Grpc.Core.StatusCode.PermissionDenied, "revoked"));
            }

            Reported.Add((subject, value));
            return Task.CompletedTask;
        }
    }

    private sealed class GatedSource(TaskCompletionSource gate, Action onEnter) : IClanSource
    {
        private int _calls;

        public async Task<BattleProbe> ActiveBattleAsync(CancellationToken ct)
        {
            onEnter();

            // Only the FIRST call blocks. A second call landing here while the first is still
            // blocked is exactly what a missing reentrancy guard would allow.
            if (Interlocked.Increment(ref _calls) == 1)
            {
                await gate.Task;
            }

            return new BattleProbe("B", null);
        }

        public Task<ContributionsResult> ContributionsAsync(string clan, string config, CancellationToken ct) =>
            Task.FromResult(new ContributionsResult([new(111, 4200)], null));
    }

    private static Settings Configured => new("Noodle Clan", "clan.battle.points", 180);

    private static ScoreWatch Watch(
        IClanSource source, FakeHost host, IEnumerable<Guid>? allowed = null, Settings? settings = null)
    {
        var config = settings ?? Configured;
        var policy = new ReportPolicy(config.MetricId, new HashSet<Guid>(allowed ?? [Mine]));
        return new ScoreWatch(source, host, policy, config);
    }

    [Fact]
    public async Task NoClanNameIsIdleAndPollsNothing()
    {
        var source = new FakeSource(new BattleProbe("B", null), new ContributionsResult([], null));
        var host = new FakeHost(true, []);

        var snapshot = await Watch(source, host, settings: new("", "clan.battle.points", 180))
            .RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.Idle, snapshot.State);
        Assert.Empty(host.Reported);
    }

    [Fact]
    public async Task NoBattleRunningIsItsOwnStateAndNotAnError()
    {
        // The state this sits in most of the time. If it read as an error, the window would cry
        // wolf for days between battles and nobody would believe it when it mattered.
        var source = new FakeSource(new BattleProbe(null, null), new ContributionsResult([], null));
        var snapshot = await Watch(source, new FakeHost(true, [])).RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.NoBattle, snapshot.State);
    }

    [Fact]
    public async Task AnUnreachableSourceIsDistinctFromAnUnreadableOne()
    {
        var unreachable = new FakeSource(
            new BattleProbe(null, "Could not reach the active-battle endpoint: no network", MissIsTransport: true),
            new ContributionsResult([], null));

        var snapshot = await Watch(unreachable, new FakeHost(true, [])).RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.SourceUnreachable, snapshot.State);
        Assert.Contains("no network", snapshot.Detail);
    }

    [Fact]
    public async Task AnUnreadableShapeSaysSoAndCarriesTheKeys()
    {
        var source = new FakeSource(
            new BattleProbe("B", null),
            new ContributionsResult([], "No 'Battles' object in the clan response. Keys present: Name, Contribution."));

        var snapshot = await Watch(source, new FakeHost(true, [])).RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.ShapeNotUnderstood, snapshot.State);
        Assert.Contains("Contribution", snapshot.Detail);
    }

    [Fact]
    public async Task ContributorsWithNoMatchOfOursSayHowManyWereSeen()
    {
        // Otherwise this is indistinguishable from an empty battle, and the user cannot tell
        // "nobody has scored" from "your accounts are not in this clan".
        var source = new FakeSource(
            new BattleProbe("B", null),
            new ContributionsResult([new(999, 4200)], null));

        var snapshot = await Watch(source, new FakeHost(true, [new(Mine, 111, "mine")]))
            .RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.NoMatches, snapshot.State);
        Assert.Equal(1, snapshot.ContributorsSeen);
    }

    [Fact]
    public async Task ReportsTheRawCumulativeValueForAMatchedAccount()
    {
        var source = new FakeSource(
            new BattleProbe("B", null),
            new ContributionsResult([new(111, 4200), new(999, 8000)], null));
        var host = new FakeHost(true, [new(Mine, 111, "mine")]);

        var snapshot = await Watch(source, host).RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.Reporting, snapshot.State);
        Assert.Single(host.Reported);
        Assert.Equal(Mine, host.Reported[0].Subject);
        Assert.Equal(4200, host.Reported[0].Value);   // raw, exactly as the source gave it
    }

    [Fact]
    public async Task NeverReportsAnotherMembersNumber()
    {
        // The policy is what stops it, and this is the test that says so from the loop's side.
        var source = new FakeSource(
            new BattleProbe("B", null),
            new ContributionsResult([new(999, 8000)], null));
        var host = new FakeHost(true, [new(Mine, 111, "mine")]);

        await Watch(source, host).RunOnceAsync(CancellationToken.None);

        Assert.Empty(host.Reported);
    }

    [Fact]
    public async Task APointsDropIsReportedAsObserved()
    {
        // A new battle resets the counter. Smoothing it here would manufacture the false
        // catastrophic rate the host is careful to avoid — so send the low number.
        var source = new FakeSource(
            new BattleProbe("B", null),
            new ContributionsResult([new(111, 40)], null));
        var host = new FakeHost(true, [new(Mine, 111, "mine")]);

        await Watch(source, host).RunOnceAsync(CancellationToken.None);

        Assert.Equal(40, host.Reported[0].Value);
    }

    [Fact]
    public async Task AnAbsentHostHoldsReportsAndKeepsPolling()
    {
        var source = new FakeSource(
            new BattleProbe("B", null),
            new ContributionsResult([new(111, 4200)], null));
        var host = new FakeHost(reachable: false, [new(Mine, 111, "mine")]);

        var snapshot = await Watch(source, host).RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.HostDown, snapshot.State);
        Assert.Empty(host.Reported);

        // And the contributors were still read, so the window can show them.
        Assert.Equal(1, snapshot.ContributorsSeen);
    }

    [Fact]
    public async Task DoesNotReplayABacklogWhenTheHostComesBack()
    {
        // A stale observation stamped minutes ago is exactly the input a Rate rule cannot use and
        // a Level rule would judge on.
        //
        // ONE watch and ONE host across all four cycles, with reachability flipped in the middle.
        // An earlier draft of this test built a fresh watch and a fresh host for the final cycle,
        // which passes whether or not a backlog exists — a fresh watch with one matching
        // contributor reports once no matter what happened before it. This version fails if
        // ScoreWatch ever queues.
        var source = new FakeSource(
            new BattleProbe("B", null), new ContributionsResult([new(111, 4200)], null));
        var host = new FakeHost(reachable: false, [new(Mine, 111, "mine")]);
        var watch = Watch(source, host);

        await watch.RunOnceAsync(CancellationToken.None);
        await watch.RunOnceAsync(CancellationToken.None);
        await watch.RunOnceAsync(CancellationToken.None);
        Assert.Empty(host.Reported);

        host.Reachable = true;
        await watch.RunOnceAsync(CancellationToken.None);

        Assert.Single(host.Reported);
    }

    [Fact]
    public async Task ARevokedCapabilityStopsReportingAndSaysWhich()
    {
        var source = new FakeSource(
            new BattleProbe("B", null),
            new ContributionsResult([new(111, 4200)], null));
        var host = new FakeHost(true, [new(Mine, 111, "mine")]) { ThrowPermissionDenied = true };

        var snapshot = await Watch(source, host).RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.Rejected, snapshot.State);
        Assert.Contains("host.metrics.report", snapshot.Detail);
    }

    [Fact]
    public async Task UnresolvedAccountsRideAlongsideWhateverTheStateIs()
    {
        // Not a state: three accounts can report while a fourth has no Roblox user id yet.
        var source = new FakeSource(
            new BattleProbe("B", null),
            new ContributionsResult([new(111, 4200)], null));
        var host = new FakeHost(true, [new(Mine, 111, "mine"), new(Guid.NewGuid(), 0, "pending")]);

        var snapshot = await Watch(source, host).RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.Reporting, snapshot.State);
        Assert.Single(snapshot.Unresolved);
        Assert.Equal("pending", snapshot.Unresolved[0].DisplayName);
    }

    [Fact]
    public async Task AnUnreadableActiveBattleShapeIsNotMistakenForUnreachable()
    {
        // Guards the FIRST miss-check specifically: a shape miss on the active-battle call (the
        // default MissIsTransport: false) must land on ShapeNotUnderstood, not SourceUnreachable.
        // A mutation that hardcodes this check to always return SourceUnreachable passed 12/12
        // without this test.
        var source = new FakeSource(
            new BattleProbe(null, "No 'configName' in the active-battle response. Keys present: Foo."),
            new ContributionsResult([], null));

        var snapshot = await Watch(source, new FakeHost(true, [])).RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.ShapeNotUnderstood, snapshot.State);
    }

    [Fact]
    public async Task AnUnreachableContributionsCallIsNotMistakenForAnUnreadableShape()
    {
        // Guards the SECOND miss-check specifically: a transport miss on the contributions call
        // must land on SourceUnreachable. A mutation that read battle.MissIsTransport here instead
        // of contributions.MissIsTransport passed 12/12 without this test, because the only prior
        // contributions-miss test left MissIsTransport at its false default.
        var source = new FakeSource(
            new BattleProbe("B", null),
            new ContributionsResult([], "Could not reach the clan endpoint: timed out", MissIsTransport: true));

        var snapshot = await Watch(source, new FakeHost(true, [])).RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.SourceUnreachable, snapshot.State);
    }

    [Fact]
    public async Task ANewBattleClearsAStaleLineFromTheOneBefore()
    {
        // Two cycles in battle "A" report and remember 4200 for the account. Battle "A" then ends
        // and "B" starts before the account has scored in it. The remembered 4200 must not sit in
        // the snapshot looking like a live figure for "B" — the reviewer reproduced exactly that
        // across three cycles before this fix.
        var source = new FakeSource(
            new BattleProbe("A", null), new ContributionsResult([new(111, 4200)], null));
        var host = new FakeHost(true, [new(Mine, 111, "mine")]);
        var watch = Watch(source, host);

        await watch.RunOnceAsync(CancellationToken.None);
        var second = await watch.RunOnceAsync(CancellationToken.None);
        Assert.Single(second.Accounts);
        Assert.Equal("A", second.Battle);

        source.Probe = new BattleProbe("B", null);
        source.Contributions = new ContributionsResult([new(999, 100)], null);   // not ours

        var third = await watch.RunOnceAsync(CancellationToken.None);

        Assert.Equal("B", third.Battle);
        Assert.Empty(third.Accounts);
    }

    [Fact]
    public async Task TwoOverlappingCyclesDoNotBothRun()
    {
        // Task 9 drives this from a poll timer AND a "Test now" button, so overlap is not
        // hypothetical. Before the semaphore, two in-flight cycles sent the SAME observation twice
        // while ReportPolicy.Sent recorded one — the policy's account of what left the plugin
        // disagreeing with what the host actually received.
        //
        // Deterministic by construction: the source blocks on a gate the test controls, so the
        // second cycle either entered (no guard) or did not (guard working). Nothing is timed.
        var entered = 0;
        var gate = new TaskCompletionSource();

        var source = new GatedSource(gate, () => Interlocked.Increment(ref entered));
        var host = new FakeHost(true, [new(Mine, 111, "mine")]);
        var watch = Watch(source, host);

        var first = watch.RunOnceAsync(CancellationToken.None);
        var second = watch.RunOnceAsync(CancellationToken.None);

        // One cycle is inside the source and blocked; the other must not have got in.
        await Task.Yield();
        Assert.Equal(1, Volatile.Read(ref entered));

        gate.SetResult();
        await first;
        await second;

        // And having run one after the other, the observation was sent once per cycle — never
        // twice for the same read.
        Assert.Equal(2, host.Reported.Count);
    }
}
