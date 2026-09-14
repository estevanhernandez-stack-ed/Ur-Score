using System.Text.RegularExpressions;
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

    private static readonly SentStat PointsStat = new("value", "Points", "clan.battle.points");

    private sealed class FakeEngine(Func<RecipeReading> read) : IRecipeEngine
    {
        public Func<RecipeReading> Read { get; set; } = read;

        public int Calls { get; private set; }

        public IReadOnlyCollection<long> LastIds { get; private set; } = [];

        public IReadOnlySet<string> LastTracked { get; private set; } = new HashSet<string>();

        public Task<RecipeReading> ReadAsync(Recipe recipe, IReadOnlyDictionary<string, string> inputs,
            IReadOnlyCollection<long> accountUserIds, IReadOnlySet<string> trackedStats, CancellationToken ct)
        {
            Calls++;
            LastIds = accountUserIds;
            LastTracked = trackedStats;
            return Task.FromResult(Read());
        }
    }

    private sealed class GatedEngine(TaskCompletionSource gate) : IRecipeEngine
    {
        private int _inFlight;

        public int Calls;

        public int MaxInFlight;

        public async Task<RecipeReading> ReadAsync(Recipe recipe, IReadOnlyDictionary<string, string> inputs,
            IReadOnlyCollection<long> accountUserIds, IReadOnlySet<string> trackedStats, CancellationToken ct)
        {
            var call = Interlocked.Increment(ref Calls);
            MaxInFlight = Math.Max(MaxInFlight, Interlocked.Increment(ref _inFlight));
            if (call == 1) await gate.Task;
            Interlocked.Decrement(ref _inFlight);
            return Reading(("battle=A"), Row(111, 1));
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

    /// <summary>A minimal transport for driving a real <see cref="RecipeEngine"/> without a network.</summary>
    private sealed class FakeTransport : IRecipeTransport
    {
        private readonly List<(Func<Uri, bool> Match, FetchResult Result)> _routes = [];

        public FakeTransport On(string urlStart, int status, string body)
        {
            _routes.Add((u => u.AbsoluteUri.StartsWith(urlStart, StringComparison.Ordinal), new FetchResult(status, body, null)));
            return this;
        }

        public Task<FetchResult> GetAsync(Uri url, IReadOnlyDictionary<string, string> headers, string label, CancellationToken ct)
        {
            var route = _routes.FirstOrDefault(r => r.Match(url));
            return Task.FromResult(route.Result ?? new FetchResult(404, "{}", null));
        }
    }

    private static RecipeRow Row(long userId, double value) => RecipeEngineTests.Row(userId, value);

    private static RecipeReading Reading(string? context, params RecipeRow[] rows) =>
        new(ReadingOutcome.Read, null, rows, [], context, rows.Length);

    private static readonly HashSet<string> ValueOnly = ["value"];

    private static RecipeWatch Watch(IRecipeEngine engine, FakeHost host, IEnumerable<Guid>? allowed = null, FakeKeys? keys = null,
        IReadOnlyList<SentStat>? sent = null, IReadOnlySet<string>? tracked = null) =>
        new(engine, host, keys ?? new FakeKeys(), new ReportPolicy(sent ?? [PointsStat], new HashSet<Guid>(allowed ?? [Mine])),
            PetSim, Clan, tracked ?? ValueOnly);

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

        var snapshot = await Watch(new FakeEngine(() => Reading("battle=A", Row(111, 4200), Row(222, 10))), host)
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
        var engine = new FakeEngine(() => Reading("battle=A", Row(111, 4200)));
        var watch = Watch(engine, host);
        await watch.RunOnceAsync(CancellationToken.None);

        engine.Read = () => RecipeReading.Stop(ReadingOutcome.Idle, "No clan battle running");
        var snapshot = await watch.RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.SourceIdle, snapshot.State);
        Assert.Equal(4200, Assert.Single(snapshot.Accounts).LastValues["value"]);
    }

    [Fact]
    public async Task ANewContextClearsRememberedValues()
    {
        // Last battle's points beside this battle's, with nothing saying which is which, is a lie.
        var host = new FakeHost(true, [MyAccount]);
        var engine = new FakeEngine(() => Reading("battle=A", Row(111, 4200)));
        var watch = Watch(engine, host);
        await watch.RunOnceAsync(CancellationToken.None);

        engine.Read = () => Reading("battle=B", Row(222, 5));
        var snapshot = await watch.RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.NoMatches, snapshot.State);
        Assert.Empty(snapshot.Accounts);
    }

    [Fact]
    public async Task WhileRoRoRoIsDownNothingIsSentAndNothingIsReplayedAfter()
    {
        var host = new FakeHost(false, [MyAccount]);
        var engine = new FakeEngine(() => Reading("battle=A", Row(111, 100)));
        var watch = Watch(engine, host);

        var down = await watch.RunOnceAsync(CancellationToken.None);
        Assert.Equal(WatchState.HostDown, down.State);

        host.Reachable = true;
        engine.Read = () => Reading("battle=A", Row(111, 200));
        await watch.RunOnceAsync(CancellationToken.None);

        Assert.Equal(new[] { 200d }, host.Reported.Select(r => r.Value).ToArray());
    }

    [Fact]
    public async Task DecliningTheAccountsCapabilityIsRejectedByName()
    {
        var host = new FakeHost(true, [MyAccount]) { DenyAccounts = true };
        var snapshot = await Watch(new FakeEngine(() => Reading("battle=A", Row(111, 1))), host).RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.Rejected, snapshot.State);
        Assert.Contains("host.queries.accounts", snapshot.Detail);
    }

    [Fact]
    public async Task DecliningTheReportCapabilityIsRejectedByName()
    {
        var host = new FakeHost(true, [MyAccount]) { DenyReports = true };
        var snapshot = await Watch(new FakeEngine(() => Reading("battle=A", Row(111, 1))), host).RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.Rejected, snapshot.State);
        Assert.Contains("host.metrics.report", snapshot.Detail);
    }

    [Fact]
    public async Task NoRowOfYoursIsNoMatches()
    {
        var host = new FakeHost(true, [MyAccount]);
        var snapshot = await Watch(new FakeEngine(() => Reading("battle=A", Row(222, 1))), host).RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.NoMatches, snapshot.State);
        Assert.Equal("Read 1 row(s); none of them are your accounts.", snapshot.Detail);
    }

    [Fact]
    public async Task AnAccountOffTheSendListIsDroppedAndCounted()
    {
        var host = new FakeHost(true, [MyAccount]);
        var watch = Watch(new FakeEngine(() => Reading("battle=A", Row(111, 1))), host, allowed: []);

        await watch.RunOnceAsync(CancellationToken.None);

        Assert.Empty(host.Reported);
        Assert.Equal(1, watch.Policy.Dropped);
    }

    [Fact]
    public async Task TheMetricIdSentIsTheOneThePolicyHolds()
    {
        var host = new FakeHost(true, [MyAccount]);
        var watch = Watch(new FakeEngine(() => Reading("battle=A", Row(111, 1))), host);
        watch.UpdatePolicy([PointsStat with { MetricId = "my.points" }], new HashSet<Guid> { Mine });

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

        watch.UpdateRecipe(PetSim, new Dictionary<string, string> { ["clan"] = "Other Clan" }, ValueOnly);
        await watch.RunOnceAsync(CancellationToken.None);
        Assert.Equal(2, engine.Calls);
    }

    [Fact]
    public async Task SignInRequiredIsNotReleasedByAKeyChange()
    {
        // Saving a key cannot give a recipe a Roblox session; only a recipe or input change can.
        var keys = new FakeKeys();
        var engine = new FakeEngine(() => RecipeReading.Stop(ReadingOutcome.SignInRequired, "requires signing in"));
        var watch = Watch(engine, new FakeHost(true, [MyAccount]), keys: keys);

        await watch.RunOnceAsync(CancellationToken.None);
        keys.Saved.Add("an-unrelated-key-value");
        var held = await watch.RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.SignInRequired, held.State);
        Assert.Equal(1, engine.Calls);
    }

    [Fact]
    public async Task TheSameRecipeAndInputsKeepTheRememberedValues()
    {
        var host = new FakeHost(true, [MyAccount]);
        var watch = Watch(new FakeEngine(() => Reading("battle=A", Row(111, 4200))), host);
        await watch.RunOnceAsync(CancellationToken.None);

        watch.UpdateRecipe(PetSim, new Dictionary<string, string>(Clan), ValueOnly);

        Assert.Single((await watch.RunOnceAsync(CancellationToken.None)).Accounts);
    }

    [Fact]
    public async Task ARecipeChangedDuringTheReadSendsNothing()
    {
        // Single-value recipes all use the stat key "value", so an old reading would otherwise go out
        // under the new recipe's metric id.
        var followers = RecipeParser.Parse(RecipeParserTests.Fixture("roblox-followers.recipe.json")).Recipe!;
        var host = new FakeHost(true, [MyAccount]);
        RecipeWatch? watch = null;
        var engine = new FakeEngine(() =>
        {
            watch!.UpdateRecipe(followers, new Dictionary<string, string>(), ValueOnly);
            watch.UpdatePolicy([new SentStat("value", "Followers", "roblox.followers")], new HashSet<Guid> { Mine });
            return Reading("battle=A", Row(111, 4200));
        });
        watch = Watch(engine, host);

        var snapshot = await watch.RunOnceAsync(CancellationToken.None);

        Assert.Empty(host.Reported);
        Assert.Equal("The recipe changed while it was being read, so nothing was sent this time.", snapshot.Detail);
        Assert.Equal(PetSim.Slug, snapshot.RecipeSlug);
        Assert.Null(snapshot.Rows);
    }

    [Fact]
    public void TheWindowConstructsExactlyOneRecipeWatch()
    {
        // F2: a watch built per cycle gets a fresh serialization guard, and a timer tick and a Test
        // now click could then both report one observation.
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "UI", "MainWindow.xaml.cs"));
        var count = Regex.Matches(text, Regex.Escape("new RecipeWatch(")).Count;

        Assert.True(count == 1, $"src/UI/MainWindow.xaml.cs constructs RecipeWatch {count} time(s); expected exactly 1. "
            + "A watch built per cycle regresses F2: a fresh semaphore serializes nothing, and one observation can be reported twice.");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ur-Score.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.False(dir is null, "Could not locate Ur-Score.csproj above the test assembly.");
        return dir!.FullName;
    }

    [Fact]
    public async Task OnlyResolvedUserIdsReachTheEngineAndUnresolvedAccountsAreNamed()
    {
        var waiting = new HostAccount(Guid.NewGuid(), 0, "New Alt");
        var engine = new FakeEngine(() => Reading("battle=A", Row(111, 1)));

        var snapshot = await Watch(engine, new FakeHost(true, [MyAccount, waiting])).RunOnceAsync(CancellationToken.None);

        Assert.Equal(new long[] { 111 }, engine.LastIds.ToArray());
        Assert.Equal(WatchState.Reporting, snapshot.State);
        Assert.Equal("New Alt", Assert.Single(snapshot.Unresolved).DisplayName);
    }

    private static readonly SentStat Diamonds = new("diamonds", "Diamonds", "ps99.diamonds");

    private static readonly SentStat Rank = new("rank", "Player rank", "ps99.rank");

    private static RecipeRow Stats(long userId, params (string Key, double Value)[] values) =>
        new(userId, values.ToDictionary(v => v.Key, v => v.Value));

    [Fact]
    public async Task EachOfYourAccountsReportsOnceForEverySentStat()
    {
        var host = new FakeHost(true, [MyAccount]);
        var engine = new FakeEngine(() => Reading(null,
            Stats(111, ("diamonds", 10), ("eggs", 5), ("rank", 3)),
            Stats(222, ("diamonds", 99), ("eggs", 1), ("rank", 1))));
        var watch = Watch(engine, host, sent: [Diamonds, Rank], tracked: new HashSet<string> { "diamonds", "eggs", "rank" });

        var snapshot = await watch.RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.Reporting, snapshot.State);
        Assert.Equal(new[] { (Mine, "ps99.diamonds", 10d), (Mine, "ps99.rank", 3d) },
            host.Reported.Select(r => (r.Subject, r.MetricId, r.Value)).ToArray());
        var line = Assert.Single(snapshot.Accounts);
        Assert.Equal(new Dictionary<string, double> { ["diamonds"] = 10, ["rank"] = 3 }, line.LastValues);
    }

    [Fact]
    public async Task AStatWithNoNumberThisCycleIsNotReported()
    {
        var host = new FakeHost(true, [MyAccount]);
        var watch = Watch(new FakeEngine(() => Reading(null, Stats(111, ("diamonds", 10)))), host,
            sent: [Diamonds, Rank], tracked: new HashSet<string> { "diamonds", "rank" });

        await watch.RunOnceAsync(CancellationToken.None);

        Assert.Equal("ps99.diamonds", Assert.Single(host.Reported).MetricId);
        Assert.Equal(0, watch.Policy.Dropped);
    }

    [Fact]
    public async Task ShownStatsAreReadButOnlySentStatsAreReported()
    {
        var host = new FakeHost(true, [MyAccount]);
        var engine = new FakeEngine(() => Reading(null, Stats(111, ("diamonds", 10), ("eggs", 5))));
        var watch = Watch(engine, host, sent: [Diamonds], tracked: new HashSet<string> { "diamonds", "eggs" });

        await watch.RunOnceAsync(CancellationToken.None);

        Assert.Equal(new[] { "diamonds", "eggs" }, engine.LastTracked.Order().ToArray());
        Assert.Equal("ps99.diamonds", Assert.Single(host.Reported).MetricId);

        watch.UpdateRecipe(PetSim, Clan, new HashSet<string> { "rank" });
        await watch.RunOnceAsync(CancellationToken.None);

        Assert.Equal(new[] { "rank" }, engine.LastTracked.ToArray());
    }

    [Fact]
    public async Task WithNoStatSentYourAccountsAreShowingAndNothingIsReported()
    {
        var host = new FakeHost(true, [MyAccount]);
        var watch = Watch(new FakeEngine(() => Reading(null, Stats(111, ("eggs", 5)))), host,
            sent: [], tracked: new HashSet<string> { "eggs" });

        var snapshot = await watch.RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.Showing, snapshot.State);
        Assert.Equal("Read 1 of 1 row(s). No stat is set to send, so nothing went to RoRoRo.", snapshot.Detail);
        Assert.Empty(host.Reported);
    }

    [Fact]
    public async Task OnlyYourOwnAccountsCellMissesReachTheSnapshot()
    {
        var host = new FakeHost(true, [MyAccount]);
        var reading = Reading("battle=A", Row(111, 1), Row(222, 2)) with
        {
            CellMisses = new Dictionary<(long UserId, string Stat), string>
            {
                [(111, "eggs")] = "No 'Eggs' in this row.",
                [(222, "eggs")] = "No 'Eggs' in this row.",
            },
            StatMisses = new Dictionary<string, string> { ["rank"] = "No 'Rank' in this row." },
            CounterNames = ["Huge Pets Opened"],
            IconText = "rbxassetid://1",
        };

        var snapshot = await Watch(new FakeEngine(() => reading), host).RunOnceAsync(CancellationToken.None);

        Assert.Equal((111L, "eggs"), Assert.Single(snapshot.CellMisses).Key);
        Assert.Equal("No 'Rank' in this row.", snapshot.StatMisses["rank"]);
        Assert.Equal(new[] { "Huge Pets Opened" }, snapshot.CounterNames.ToArray());
        Assert.Equal("rbxassetid://1", snapshot.IconText);
    }

    [Fact]
    public async Task AnIdleStopKeepsTheIconItRead()
    {
        var host = new FakeHost(true, [MyAccount]);
        var idle = RecipeReading.Stop(ReadingOutcome.Idle, "Your clan hasn't joined this battle.") with { IconText = "rbxassetid://1" };

        var snapshot = await Watch(new FakeEngine(() => idle), host).RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.SourceIdle, snapshot.State);
        Assert.Equal("rbxassetid://1", snapshot.IconText);
    }

    [Fact]
    public async Task ThePetSimRecipeReportsYourAccountEndToEnd()
    {
        // The seam between RecipeWatch and RecipeEngine is otherwise only type-checked: this drives
        // a real engine, through a real watch, off a fake transport standing in for the network.
        const string battle = """{ "status": "ok", "data": { "configName": "B" } }""";
        const string clanResponse = """
            { "status": "ok", "data": { "Battles": { "B": {
                "PointContributions": [ { "UserID": 111, "Points": 4200 }, { "UserID": 222, "Points": 10 } ]
            } } } }
            """;

        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, battle)
            .On("https://ps99.biggamesapi.io/api/clan/", 200, clanResponse);

        var engine = new RecipeEngine(transport, new FakeKeys());
        var host = new FakeHost(true, [MyAccount]);
        var watch = new RecipeWatch(
            engine, host, new FakeKeys(), new ReportPolicy([PointsStat], new HashSet<Guid> { Mine }), PetSim, Clan, ValueOnly);

        var snapshot = await watch.RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.Reporting, snapshot.State);
        var sent = Assert.Single(host.Reported);
        Assert.Equal((Mine, "clan.battle.points", 4200d), (sent.Subject, sent.MetricId, sent.Value));
        Assert.Equal(2, snapshot.Rows!.Count);
    }
}
