using System.Diagnostics;
using Labs626.UrScore.Core;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class SharedAccountsTests
{
    private static readonly HostAccount Alt = new(Guid.Parse("9ad5e605-6b41-478c-add3-b916a31a5ab2"), 111, "Alt One");

    private static readonly DateTimeOffset Start = new(2026, 9, 19, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OneFetchServesEveryWatchForThirtySecondsAndIsSaved()
    {
        using var dir = TempDir.Create("urscore-accounts");
        var cache = new AccountsCache(Path.Combine(dir.Path, "accounts.json"));
        var host = new StubHost(true, Alt);
        var time = new ManualTime(Start);
        var shared = new SharedAccounts(host, cache, time);

        var first = await shared.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(29));
        await shared.GetAsync(CancellationToken.None);

        Assert.Equal(1, host.AccountCalls);
        Assert.Equal((true, false, false, (DateTimeOffset?)Start), (first.HostUp, first.FromCache, first.Denied, first.ListedAt));
        Assert.Equal(Alt, Assert.Single(cache.Load()));

        time.Advance(TimeSpan.FromSeconds(2));
        await shared.GetAsync(CancellationToken.None);
        Assert.Equal(2, host.AccountCalls);
    }

    [Fact]
    public async Task WhenRoRoRoIsClosedTheSavedAccountsAreUsed()
    {
        using var dir = TempDir.Create("urscore-accounts");
        var cache = new AccountsCache(Path.Combine(dir.Path, "accounts.json"));
        cache.Save([Alt]);

        var list = await new SharedAccounts(new StubHost(false), cache, new ManualTime(Start)).GetAsync(CancellationToken.None);

        Assert.Equal((false, true), (list.HostUp, list.FromCache));
        Assert.Equal(Alt, Assert.Single(list.Accounts));
        Assert.NotNull(list.ListedAt);
    }

    [Fact]
    public async Task ARefusedAccountListIsDeniedAndEmpty()
    {
        using var dir = TempDir.Create("urscore-accounts");
        var host = new StubHost(true, Alt) { DenyAccounts = true };

        var list = await new SharedAccounts(host, new AccountsCache(Path.Combine(dir.Path, "a.json")), new ManualTime(Start)).GetAsync(CancellationToken.None);

        Assert.True(list.Denied);
        Assert.Empty(list.Accounts);
    }

    [Fact]
    public async Task ListedFiresWithTheFetchedListBeforeGetAsyncReturnsAndAThrowingHandlerBreaksNothing()
    {
        using var dir = TempDir.Create("urscore-accounts");
        var time = new ManualTime(Start);
        var shared = new SharedAccounts(new StubHost(true, Alt), new AccountsCache(Path.Combine(dir.Path, "accounts.json")), time);
        var returned = false;
        var fired = 0;
        AccountList? seen = null;
        var beforeReturn = false;
        var lastWasSet = false;

        shared.Listed += _ => throw new InvalidOperationException("a broken subscriber");
        shared.Listed += list =>
        {
            fired++;
            seen = list;
            beforeReturn = !returned;
            lastWasSet = ReferenceEquals(shared.Last, list);
        };

        var result = await shared.GetAsync(CancellationToken.None);
        returned = true;

        Assert.Same(result, seen);
        Assert.True(beforeReturn);
        Assert.True(lastWasSet);
        Assert.Equal(Alt, Assert.Single(result.Accounts));

        // Within the shared window nothing new is fetched, so nothing new is listed.
        time.Advance(TimeSpan.FromSeconds(5));
        await shared.GetAsync(CancellationToken.None);
        Assert.Equal(1, fired);
    }

    private sealed class OneRowEngine(long userId, double value) : IRecipeEngine
    {
        public Task<RecipeReading> ReadAsync(Recipe recipe, IReadOnlyDictionary<string, string> inputs,
            IReadOnlyCollection<long> accountUserIds, IReadOnlySet<string> trackedStats, CancellationToken ct) =>
            Task.FromResult(new RecipeReading(ReadingOutcome.Read, null, [RecipeEngineTests.Row(userId, value)], [], "battle=A", 1));
    }

    private sealed class NoKeys : IKeyStore
    {
        public SavedKey? Find(string keyId) => null;

        public void Save(string keyId, string host, string value) => throw new NotSupportedException();

        public bool Remove(string keyId) => false;

        public IReadOnlyCollection<string> Values() => [];
    }

    [Fact]
    public async Task AWatchWhosePolicyFollowsListedSendsANewlyListedAccountOnItsFirstRead()
    {
        // F8: the allow list must know an account RoRoRo just listed before that same read sends, not a cycle later.
        using var dir = TempDir.Create("urscore-accounts");
        var host = new StubHost(true, Alt);
        var shared = new SharedAccounts(host, new AccountsCache(Path.Combine(dir.Path, "accounts.json")), new ManualTime(Start));
        var recipe = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;
        var points = new SentStat("value", "Points", "clan.battle.points");

        RecipeWatch? watch = null;
        shared.Listed += list => watch!.UpdatePolicy(watch.Policy.SentStats, list.Accounts.Select(a => a.AccountId).ToHashSet());
        watch = new RecipeWatch(
            new OneRowEngine(Alt.RobloxUserId, 5), host, new NoKeys(), new ReportPolicy([points], new HashSet<Guid>()), recipe,
            new Dictionary<string, string> { ["clan"] = "Noodle Clan" }, new HashSet<string> { "value" }, sharedAccounts: shared);

        var snapshot = await watch.RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.Reporting, snapshot.State);
        Assert.Equal((Alt.AccountId, "clan.battle.points", 5.0), host.Reported.Select(r => (r.Subject, r.MetricId, r.Value)).Single());
        Assert.Equal((1, 0), (watch.Policy.Sent, watch.Policy.Dropped));
    }

    [Fact]
    public void ACacheThatIsMissingOrBrokenLoadsEmpty()
    {
        using var dir = TempDir.Create("urscore-accounts");
        var path = Path.Combine(dir.Path, "accounts.json");
        Assert.Empty(new AccountsCache(path).Load());
        Assert.Null(new AccountsCache(path).SavedAt());

        File.WriteAllText(path, "[ nope");
        Assert.Empty(new AccountsCache(path).Load());
    }

    [Fact]
    public void AnAccountBelongsToTheFirstSourceThatClaimsItUntilTheClaimGoesStale()
    {
        var time = new ManualTime(Start);
        var claims = new AccountClaims(time);
        var window = TimeSpan.FromMinutes(6);

        Assert.True(claims.TryClaim("clan", 111, "s-1", window));
        Assert.False(claims.TryClaim("clan", 111, "s-2", window));
        Assert.True(claims.TryClaim("clan", 111, "s-1", window));
        Assert.True(claims.TryClaim("other-recipe", 111, "s-2", window));

        time.Advance(TimeSpan.FromMinutes(7));
        Assert.True(claims.TryClaim("clan", 111, "s-2", window));
        Assert.False(claims.TryClaim("clan", 111, "s-1", window));
    }

    private sealed class CountingTransport : IRecipeTransport
    {
        private int _inFlight;

        public int MaxInFlight;

        public List<(string Host, long At)> Starts { get; } = [];

        public async Task<FetchResult> GetAsync(Uri url, IReadOnlyDictionary<string, string> headers, string label, CancellationToken cancellationToken)
        {
            var now = Interlocked.Increment(ref _inFlight);
            lock (Starts)
            {
                MaxInFlight = Math.Max(MaxInFlight, now);
                Starts.Add((url.Host, Stopwatch.GetTimestamp()));
            }

            await Task.Delay(20, cancellationToken);
            Interlocked.Decrement(ref _inFlight);
            return new FetchResult(200, "{}", null);
        }
    }

    [Fact]
    public async Task RequestsToOneHostGoOneAtATimeAndSpacedButOtherHostsDontWait()
    {
        var inner = new CountingTransport();
        var spacing = TimeSpan.FromMilliseconds(150);
        var transport = new SpacedTransport(inner, TimeProvider.System, spacing);
        var headers = new Dictionary<string, string>();

        await Task.WhenAll(
            transport.GetAsync(new Uri("https://one.example/a"), headers, "a", CancellationToken.None),
            transport.GetAsync(new Uri("https://one.example/b"), headers, "b", CancellationToken.None),
            transport.GetAsync(new Uri("https://two.example/c"), headers, "c", CancellationToken.None));

        var one = inner.Starts.Where(s => s.Host == "one.example").Select(s => s.At).Order().ToList();
        Assert.Equal(2, one.Count);
        Assert.True(Stopwatch.GetElapsedTime(one[0], one[1]) >= spacing, "the second request to one host started too soon");
        Assert.Contains(inner.Starts, s => s.Host == "two.example");
    }
}
