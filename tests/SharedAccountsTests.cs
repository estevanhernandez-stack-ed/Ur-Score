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
