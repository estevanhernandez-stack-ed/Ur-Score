using Labs626.UrScore.Core;
using Labs626.UrScore.Host;
using Labs626.UrScore.Source;

namespace UrScore.Tests;

public class ScoreWatchTests
{
    private static readonly Guid Mine = Guid.Parse("9ad5e605-6b41-478c-add3-b916a31a5ab2");

    private sealed class FakeSource(BattleProbe probe, ContributionsResult contributions) : IClanSource
    {
        public Task<BattleProbe> ActiveBattleAsync(CancellationToken ct) => Task.FromResult(probe);

        public Task<ContributionsResult> ContributionsAsync(string clan, string config, CancellationToken ct) =>
            Task.FromResult(contributions);
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
}
