using System.Diagnostics;
using Grpc.Core;
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

    /// <summary>
    /// "When RoRoRo last listed these accounts" means exactly that, and the file's own timestamp is a different
    /// fact wearing the same clothes: it says when WE wrote the cache, which is later, and reading it as a
    /// listing time overstates how fresh the list is on a screen whose whole job is to say how stale it is. So
    /// RoRoRo's own last listing wins whenever there has been one, and the file's timestamp is the fallback for
    /// having never heard from RoRoRo at all. The fallback was covered; the preference was not (S1-12.11).
    /// </summary>
    [Fact]
    public async Task ACachedListKeepsRoRoRosOwnListingTimeRatherThanTheFilesOwn()
    {
        using var dir = TempDir.Create("urscore-accounts");
        var cache = new AccountsCache(Path.Combine(dir.Path, "accounts.json"));
        var time = new ManualTime(Start);
        var host = new StubHost(true, Alt);
        var shared = new SharedAccounts(host, cache, time);

        // Heard from once, so there IS a listing time, and it is nothing like the file's: the fixture clock reads
        // 2026-09-19 while the cache file is written now, which is what makes the two distinguishable at all.
        var live = await shared.GetAsync(CancellationToken.None);
        Assert.Equal(Start, live.ListedAt);

        host.AccountsFailure = new IOException("broken pipe");
        time.Advance(SharedAccounts.Window + TimeSpan.FromSeconds(1));
        var cached = await shared.GetAsync(CancellationToken.None);

        Assert.True(cached.FromCache);
        Assert.Equal(Start, cached.ListedAt);
        Assert.NotEqual(cache.SavedAt(), cached.ListedAt);
    }

    /// <summary>
    /// A failure that is not the transport's is not "RoRoRo not answering". The fallback caught every exception,
    /// so a bug in the client — an argument out of range, an invalid operation — was answered with the saved
    /// accounts and a cached-list note, and looked exactly like RoRoRo being closed (S1-F.9). Now only the
    /// transport's own failures (an RpcException, a pipe's IOException) take the saved list; anything else
    /// leaves as the exception it is, lists nothing, and reaches the read's own "last read failed" line, where
    /// its type is named. The transport case is covered beside this and is the control.
    /// </summary>
    [Fact]
    public async Task AFailureThatIsNotTheTransportsIsNotAnsweredWithTheSavedAccounts()
    {
        using var dir = TempDir.Create("urscore-accounts");
        var cache = new AccountsCache(Path.Combine(dir.Path, "accounts.json"));
        cache.Save([Alt]);
        var host = new StubHost(true, Alt) { AccountsFailure = new InvalidOperationException("a bug in the client") };
        var shared = new SharedAccounts(host, cache, new ManualTime(Start));
        var listed = 0;
        shared.Listed += _ => listed++;

        await Assert.ThrowsAsync<InvalidOperationException>(() => shared.GetAsync(CancellationToken.None));

        Assert.Null(shared.Last);
        Assert.Equal(0, listed);
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
    public async Task AnAccountListThatFailsAfterAGoodProbeUsesTheSavedAccounts()
    {
        // Spec §5.4, §5.5: RoRoRo answering the probe and then failing the list is RoRoRo not answering, not a lost read.
        using var dir = TempDir.Create("urscore-accounts");
        var cache = new AccountsCache(Path.Combine(dir.Path, "accounts.json"));
        cache.Save([Alt]);

        foreach (var failure in new Exception[] { new RpcException(new Status(StatusCode.Unavailable, "pipe gone")), new IOException("broken pipe") })
        {
            var host = new StubHost(true, Alt) { AccountsFailure = failure };
            var shared = new SharedAccounts(host, cache, new ManualTime(Start));
            AccountList? listed = null;
            shared.Listed += l => listed = l;

            var list = await shared.GetAsync(CancellationToken.None);

            Assert.Equal((false, true, false), (list.HostUp, list.FromCache, list.Denied));
            Assert.Equal(Alt, Assert.Single(list.Accounts));
            Assert.Equal(cache.SavedAt(), list.ListedAt);
            Assert.Same(list, shared.Last);
            Assert.Same(list, listed);
            Assert.Equal(1, host.AccountCalls);
        }
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
    public async Task AnotherHostCompletesWhileTheFirstHostHasARequestInFlight()
    {
        var inner = new GatedTransport();
        var transport = new SpacedTransport(inner, TimeProvider.System, TimeSpan.Zero);
        var headers = new Dictionary<string, string>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var first = transport.GetAsync(new Uri("https://one.example/a"), headers, "a", cancellation.Token);
        var sameHost = transport.GetAsync(new Uri("https://one.example/b"), headers, "b", cancellation.Token);
        var otherHost = transport.GetAsync(new Uri("https://two.example/c"), headers, "c", cancellation.Token);

        try
        {
            await otherHost;
            Assert.False(first.IsCompleted);
            Assert.False(sameHost.IsCompleted);
            Assert.Equal(1, inner.FirstHostCalls);
        }
        finally
        {
            inner.Release.TrySetResult();
            await Task.WhenAll(first, sameHost, otherHost);
        }

        Assert.Equal(2, inner.FirstHostCalls);
    }

    private sealed class GatedTransport : IRecipeTransport
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int FirstHostCalls;

        public async Task<FetchResult> GetAsync(Uri url, IReadOnlyDictionary<string, string> headers, string label, CancellationToken cancellationToken)
        {
            if (url.Host == "one.example")
            {
                Interlocked.Increment(ref FirstHostCalls);
                await Release.Task.WaitAsync(cancellationToken);
            }

            return new FetchResult(200, "{}", null);
        }
    }

    /// <summary>
    /// A request cancelled while still waiting its turn never reached the host, so it does not restart the
    /// host's spacing clock. It used to: the finally stamped the lane whether or not anything was sent, so the
    /// next caller waited a full spacing for a request that had gone nowhere (S1-4.2). A request that LEFT and
    /// then failed still counts, because it did reach the server and the spacing exists to protect the server.
    /// <para>
    /// Timing-based, so the numbers are chosen with room: spacing 400 ms, the cancel at 200 ms. Under the old
    /// code the third request cannot start before 600 ms from the first; under the new one it starts at 400.
    /// The assertion accepts up to 520, which is 80 ms of slack one way and 80 ms clear of the old answer.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ARequestCancelledWhileWaitingItsTurnDoesNotSpaceTheNextOne()
    {
        var inner = new CountingTransport();
        var spacing = TimeSpan.FromMilliseconds(400);
        var transport = new SpacedTransport(inner, TimeProvider.System, spacing);
        var headers = new Dictionary<string, string>();
        var host = new Uri("https://one.example/a");

        await transport.GetAsync(host, headers, "first", CancellationToken.None);
        var firstDone = Stopwatch.GetTimestamp();

        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transport.GetAsync(host, headers, "cancelled", cancel.Token));
        Assert.Single(inner.Starts);

        await transport.GetAsync(host, headers, "third", CancellationToken.None);
        var third = inner.Starts.Last().At;

        var gap = Stopwatch.GetElapsedTime(firstDone, third);
        Assert.True(gap >= spacing - TimeSpan.FromMilliseconds(30), $"the third request started too soon: {gap.TotalMilliseconds:F0} ms");
        Assert.True(gap <= TimeSpan.FromMilliseconds(520), $"the cancelled wait still spaced the next request: {gap.TotalMilliseconds:F0} ms");
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
