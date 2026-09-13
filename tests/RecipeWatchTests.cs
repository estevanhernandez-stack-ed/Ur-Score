using Grpc.Core;
using Labs626.UrScore.Core;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class RecipeWatchTests
{
    private static readonly Guid Mine = Guid.Parse("9ad5e605-6b41-478c-add3-b916a31a5ab2");

    private static readonly HostAccount MyAccount = new(Mine, 111, "Alt One");

    private static Recipe PetSim => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

    private static readonly Dictionary<string, string> Clan = new() { ["clan"] = "Noodle Clan" };

    private sealed class FakeEngine(Func<RecipeReading> read) : IRecipeEngine
    {
        public Func<RecipeReading> Read { get; set; } = read;

        public int Calls { get; private set; }

        public IReadOnlyCollection<long> LastIds { get; private set; } = [];

        public Task<RecipeReading> ReadAsync(Recipe recipe, IReadOnlyDictionary<string, string> inputs,
            IReadOnlyCollection<long> accountUserIds, CancellationToken ct)
        {
            Calls++;
            LastIds = accountUserIds;
            return Task.FromResult(Read());
        }
    }

    private sealed class GatedEngine(TaskCompletionSource gate) : IRecipeEngine
    {
        private int _inFlight;

        public int Calls;

        public int MaxInFlight;

        public async Task<RecipeReading> ReadAsync(Recipe recipe, IReadOnlyDictionary<string, string> inputs,
            IReadOnlyCollection<long> accountUserIds, CancellationToken ct)
        {
            var call = Interlocked.Increment(ref Calls);
            MaxInFlight = Math.Max(MaxInFlight, Interlocked.Increment(ref _inFlight));
            if (call == 1) await gate.Task;
            Interlocked.Decrement(ref _inFlight);
            return Reading(("battle=A"), new RecipeRow(111, 1));
        }
    }

    private sealed class FakeHost(bool reachable, IReadOnlyList<HostAccount> accounts) : IHostClient
    {
        public List<(Guid Subject, string MetricId, double Value, DateTimeOffset ObservedAt)> Reported { get; } = [];

        public bool Reachable { get; set; } = reachable;

        public bool DenyAccounts { get; set; }

        public bool DenyReports { get; set; }

        public Task<bool> IsReachableAsync(CancellationToken ct) => Task.FromResult(Reachable);

        public Task<IReadOnlyList<HostAccount>> GetAccountsAsync(CancellationToken ct) =>
            DenyAccounts ? throw new RpcException(new Status(StatusCode.PermissionDenied, "revoked")) : Task.FromResult(accounts);

        public Task ReportMetricAsync(Guid subject, string metricId, double value, DateTimeOffset observedAt, CancellationToken ct)
        {
            if (DenyReports) throw new RpcException(new Status(StatusCode.PermissionDenied, "revoked"));
            Reported.Add((subject, metricId, value, observedAt));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeKeys : IKeyStore
    {
        public List<string> Saved { get; } = [];
        public SavedKey? Find(string keyId) => null;
        public void Save(string keyId, string host, string value) => Saved.Add(value);
        public bool Remove(string keyId) => false;
        public IReadOnlyCollection<string> Values() => [.. Saved];
    }

    private static RecipeReading Reading(string? context, params RecipeRow[] rows) =>
        new(ReadingOutcome.Read, null, rows, [], context, rows.Length);

    private static RecipeWatch Watch(IRecipeEngine engine, FakeHost host, IEnumerable<Guid>? allowed = null, FakeKeys? keys = null) =>
        new(engine, host, keys ?? new FakeKeys(), new ReportPolicy("clan.battle.points", new HashSet<Guid>(allowed ?? [Mine])), PetSim, Clan);

    [Fact]
    public async Task NeedsInputIsItsOwnStateAndSendsNothing()
    {
        var host = new FakeHost(true, [MyAccount]);
        var snapshot = await Watch(new FakeEngine(() => RecipeReading.Stop(ReadingOutcome.NeedsInput, "Set Your clan to start.")), host)
            .RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.NeedsInput, snapshot.State);
        Assert.Equal("Set Your clan to start.", snapshot.Detail);
        Assert.Empty(host.Reported);
    }

    [Fact]
    public async Task ReportsOnlyYourOwnAccountsRawAndInUtc()
    {
        var host = new FakeHost(true, [MyAccount]);
        var before = DateTimeOffset.UtcNow;

        var snapshot = await Watch(new FakeEngine(() => Reading("battle=A", new RecipeRow(111, 4200), new RecipeRow(222, 10))), host)
            .RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.Reporting, snapshot.State);
        var sent = Assert.Single(host.Reported);
        Assert.Equal((Mine, "clan.battle.points", 4200d), (sent.Subject, sent.MetricId, sent.Value));
        Assert.Equal(TimeSpan.Zero, sent.ObservedAt.Offset);
        Assert.True(sent.ObservedAt >= before);
        Assert.Equal(2, snapshot.Rows!.Count);
    }

    [Fact]
    public async Task SourceIdleIsNotAnErrorAndKeepsTheLastValues()
    {
        var host = new FakeHost(true, [MyAccount]);
        var engine = new FakeEngine(() => Reading("battle=A", new RecipeRow(111, 4200)));
        var watch = Watch(engine, host);
        await watch.RunOnceAsync(CancellationToken.None);

        engine.Read = () => RecipeReading.Stop(ReadingOutcome.Idle, "No clan battle running");
        var snapshot = await watch.RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.SourceIdle, snapshot.State);
        Assert.Equal(4200, Assert.Single(snapshot.Accounts).LastValue);
    }

    [Fact]
    public async Task ANewContextClearsRememberedValues()
    {
        // Last battle's points beside this battle's, with nothing saying which is which, is a lie.
        var host = new FakeHost(true, [MyAccount]);
        var engine = new FakeEngine(() => Reading("battle=A", new RecipeRow(111, 4200)));
        var watch = Watch(engine, host);
        await watch.RunOnceAsync(CancellationToken.None);

        engine.Read = () => Reading("battle=B", new RecipeRow(222, 5));
        var snapshot = await watch.RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.NoMatches, snapshot.State);
        Assert.Empty(snapshot.Accounts);
    }

    [Fact]
    public async Task WhileRoRoRoIsDownNothingIsSentAndNothingIsReplayedAfter()
    {
        var host = new FakeHost(false, [MyAccount]);
        var engine = new FakeEngine(() => Reading("battle=A", new RecipeRow(111, 100)));
        var watch = Watch(engine, host);

        var down = await watch.RunOnceAsync(CancellationToken.None);
        Assert.Equal(WatchState.HostDown, down.State);

        host.Reachable = true;
        engine.Read = () => Reading("battle=A", new RecipeRow(111, 200));
        await watch.RunOnceAsync(CancellationToken.None);

        Assert.Equal(new[] { 200d }, host.Reported.Select(r => r.Value).ToArray());
    }

    [Fact]
    public async Task DecliningTheAccountsCapabilityIsRejectedByName()
    {
        var host = new FakeHost(true, [MyAccount]) { DenyAccounts = true };
        var snapshot = await Watch(new FakeEngine(() => Reading("battle=A", new RecipeRow(111, 1))), host).RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.Rejected, snapshot.State);
        Assert.Contains("host.queries.accounts", snapshot.Detail);
    }

    [Fact]
    public async Task DecliningTheReportCapabilityIsRejectedByName()
    {
        var host = new FakeHost(true, [MyAccount]) { DenyReports = true };
        var snapshot = await Watch(new FakeEngine(() => Reading("battle=A", new RecipeRow(111, 1))), host).RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.Rejected, snapshot.State);
        Assert.Contains("host.metrics.report", snapshot.Detail);
    }

    [Fact]
    public async Task NoRowOfYoursIsNoMatches()
    {
        var host = new FakeHost(true, [MyAccount]);
        var snapshot = await Watch(new FakeEngine(() => Reading("battle=A", new RecipeRow(222, 1))), host).RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.NoMatches, snapshot.State);
        Assert.Equal("Read 1 row(s); none of them are your accounts.", snapshot.Detail);
    }

    [Fact]
    public async Task AnAccountOffTheSendListIsDroppedAndCounted()
    {
        var host = new FakeHost(true, [MyAccount]);
        var watch = Watch(new FakeEngine(() => Reading("battle=A", new RecipeRow(111, 1))), host, allowed: []);

        await watch.RunOnceAsync(CancellationToken.None);

        Assert.Empty(host.Reported);
        Assert.Equal(1, watch.Policy.Dropped);
    }

    [Fact]
    public async Task TheMetricIdSentIsTheOneThePolicyHolds()
    {
        var host = new FakeHost(true, [MyAccount]);
        var watch = Watch(new FakeEngine(() => Reading("battle=A", new RecipeRow(111, 1))), host);
        watch.UpdatePolicy("my.points", new HashSet<Guid> { Mine });

        await watch.RunOnceAsync(CancellationToken.None);

        Assert.Equal("my.points", Assert.Single(host.Reported).MetricId);
    }

    [Fact]
    public async Task OverlappingRunsTakeTurns()
    {
        var gate = new TaskCompletionSource();
        var engine = new GatedEngine(gate);
        var watch = Watch(engine, new FakeHost(true, [MyAccount]));

        var first = watch.RunOnceAsync(CancellationToken.None);
        var second = watch.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, engine.Calls);

        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(2, engine.Calls);
        Assert.Equal(1, engine.MaxInFlight);
    }

    [Fact]
    public async Task ARejectedKeyIsHeldUntilAKeyChanges()
    {
        var keys = new FakeKeys();
        var engine = new FakeEngine(() => RecipeReading.Stop(ReadingOutcome.KeyRejected, "api.tracker.example rejected your Tracker key."));
        var watch = Watch(engine, new FakeHost(true, [MyAccount]), keys: keys);

        await watch.RunOnceAsync(CancellationToken.None);
        var held = await watch.RunOnceAsync(CancellationToken.None);
        Assert.Equal(WatchState.KeyRejected, held.State);
        Assert.Equal(1, engine.Calls);

        keys.Saved.Add("a-new-key-value");
        await watch.RunOnceAsync(CancellationToken.None);
        Assert.Equal(2, engine.Calls);
    }

    [Fact]
    public async Task SignInRequiredIsHeldUntilTheRecipeOrInputsChange()
    {
        var engine = new FakeEngine(() => RecipeReading.Stop(ReadingOutcome.SignInRequired, "requires signing in"));
        var watch = Watch(engine, new FakeHost(true, [MyAccount]));

        await watch.RunOnceAsync(CancellationToken.None);
        await watch.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, engine.Calls);

        watch.UpdateRecipe(PetSim, new Dictionary<string, string> { ["clan"] = "Other Clan" });
        await watch.RunOnceAsync(CancellationToken.None);
        Assert.Equal(2, engine.Calls);
    }

    [Fact]
    public async Task TheSameRecipeAndInputsKeepTheRememberedValues()
    {
        var host = new FakeHost(true, [MyAccount]);
        var watch = Watch(new FakeEngine(() => Reading("battle=A", new RecipeRow(111, 4200))), host);
        await watch.RunOnceAsync(CancellationToken.None);

        watch.UpdateRecipe(PetSim, new Dictionary<string, string>(Clan));

        Assert.Single((await watch.RunOnceAsync(CancellationToken.None)).Accounts);
    }

    [Fact]
    public async Task OnlyResolvedUserIdsReachTheEngineAndUnresolvedAccountsAreNamed()
    {
        var waiting = new HostAccount(Guid.NewGuid(), 0, "New Alt");
        var engine = new FakeEngine(() => Reading("battle=A", new RecipeRow(111, 1)));

        var snapshot = await Watch(engine, new FakeHost(true, [MyAccount, waiting])).RunOnceAsync(CancellationToken.None);

        Assert.Equal(new long[] { 111 }, engine.LastIds.ToArray());
        Assert.Equal(WatchState.Reporting, snapshot.State);
        Assert.Equal("New Alt", Assert.Single(snapshot.Unresolved).DisplayName);
    }
}
