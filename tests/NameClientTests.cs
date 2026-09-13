using System.Net;
using System.Text;
using System.Text.Json;
using Labs626.UrScore.Source;

namespace UrScore.Tests;

public class NameClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        public List<string> UserAgents { get; } = [];
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            UserAgents.Add(request.Headers.UserAgent.ToString());
            var body = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Bodies.Add(body);
            return respond(request, body);
        }
    }

    /// <summary>
    /// Never completes on its own — only cancellation of the token handed to it ends the wait.
    /// Copied from <c>ClanClientTests</c>: stands in for a merely slow endpoint (with a short
    /// <see cref="HttpClient.Timeout"/>) or for a caller's own cancellation (with an already- or
    /// soon-cancelled token), depending which token actually fires first.
    /// </summary>
    private sealed class NeverRespondsHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);   // unreachable: the delay above never returns normally
        }
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>The directory <see cref="Echo"/> answers out of — three well-known ids, matching
    /// the brief's live-verified fixture.</summary>
    private static readonly Dictionary<long, string> KnownUsers = new()
    {
        [1] = "Roblox",
        [156] = "builderman",
        [261] = "Shedletsky",
    };

    /// <summary>
    /// Answers with rows for ONLY the ids actually present in the request body, which is what
    /// users.roblox.com does (verified live 2026-09-12; see the brief). Fix round 1: the original
    /// stub answered every request with the same three-id fixture regardless of the body — a
    /// shape the real endpoint cannot produce — and that gap forced a production change
    /// (narrowing the cache to the requested batch) to compensate for the test double rather than
    /// for the contract. With a realistic stub, the caching tests below prove the caching
    /// behaviour rather than the narrowing.
    /// </summary>
    private static HttpResponseMessage Echo(HttpRequestMessage request, string body)
    {
        using var document = JsonDocument.Parse(body);
        var ids = document.RootElement.GetProperty("userIds").EnumerateArray().Select(e => e.GetInt64());

        var rows = ids.Where(KnownUsers.ContainsKey).Select(id =>
            $$"""{"hasVerifiedBadge":true,"id":{{id}},"name":"{{KnownUsers[id]}}","displayName":"{{KnownUsers[id]}}"}""");

        return Ok($$"""{"data":[{{string.Join(",", rows)}}]}""");
    }

    [Fact]
    public async Task ResolvesAWholeBatchInOneCall()
    {
        // A clan battle returns around 75 contributors and the endpoint takes 100 ids, so a whole
        // clan is one request. Anything that made this N requests would be the defect.
        var handler = new StubHandler(Echo);
        var client = new NameClient(new HttpClient(handler));

        var names = await client.ResolveAsync([1, 156, 261], CancellationToken.None);

        Assert.Equal(1, handler.Calls);
        Assert.Equal("builderman", names[156]);
        Assert.Equal("Shedletsky", names[261]);
    }

    [Fact]
    public async Task AsksOnlyForIdsItHasNotSeen()
    {
        // Re-resolving 75 names every three minutes would be twenty calls an hour against someone
        // else's service for data that changes about never.
        var handler = new StubHandler(Echo);
        var client = new NameClient(new HttpClient(handler));

        await client.ResolveAsync([1, 156, 261], CancellationToken.None);
        var second = await client.ResolveAsync([1, 156], CancellationToken.None);

        Assert.Equal(1, handler.Calls);              // no second request at all
        Assert.Equal("Roblox", second[1]);           // served from cache
    }

    [Fact]
    public async Task AsksOnlyForTheNewOnesWhenSomeAreCached()
    {
        var handler = new StubHandler(Echo);
        var client = new NameClient(new HttpClient(handler));

        await client.ResolveAsync([1], CancellationToken.None);
        await client.ResolveAsync([1, 156], CancellationToken.None);

        Assert.Equal(2, handler.Calls);
        Assert.DoesNotContain("156", handler.Bodies[0]);
        Assert.Contains("156", handler.Bodies[1]);
        Assert.DoesNotContain("\"userIds\":[1,", handler.Bodies[1]);   // 1 was not asked for again
    }

    [Fact]
    public async Task SplitsAboveTheBatchLimit()
    {
        // 100 is the endpoint's documented ceiling. A clan will not reach it today, but a request
        // that silently exceeded it would fail wholesale rather than degrade.
        var handler = new StubHandler((_, _) => Ok("""{"data":[]}"""));
        var client = new NameClient(new HttpClient(handler));

        await client.ResolveAsync([.. Enumerable.Range(1, 250).Select(i => (long)i)], CancellationToken.None);

        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task AFailedCallYieldsNoNamesRatherThanThrowing()
    {
        // Names are decoration on a leaderboard that works without them. A failure here must cost
        // the names and nothing else — never the poll, never the reporting.
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var client = new NameClient(new HttpClient(handler));

        var names = await client.ResolveAsync([1, 156], CancellationToken.None);

        Assert.Empty(names);
    }

    [Fact]
    public async Task ATransportFailureYieldsNoNamesRatherThanThrowing()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("no network"));
        var client = new NameClient(new HttpClient(handler));

        Assert.Empty(await client.ResolveAsync([1], CancellationToken.None));
    }

    [Fact]
    public async Task AFailureIsNotCachedAsAnAnswer()
    {
        // Caching a failure would mean one rate-limited minute costs the names for the whole
        // session, with nothing anywhere saying why the leaderboard is numbers.
        var fail = true;
        var handler = new StubHandler((request, body) => fail
            ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            : Echo(request, body));
        var client = new NameClient(new HttpClient(handler));

        Assert.Empty(await client.ResolveAsync([156], CancellationToken.None));

        fail = false;
        var names = await client.ResolveAsync([156], CancellationToken.None);

        Assert.Equal("builderman", names[156]);
    }

    [Fact]
    public async Task AnEmptyRequestAsksNothing()
    {
        var handler = new StubHandler(Echo);
        var client = new NameClient(new HttpClient(handler));

        Assert.Empty(await client.ResolveAsync([], CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task AnEmptyAskAfterWarmingReturnsEmptyNotTheWholeCache()
    {
        // Fix round 1, a real defect: `if (ids.Count == 0) return _cache;` handed back every
        // name resolved so far, plus a live reference that kept growing — in a client whose whole
        // posture is holding as little about other people as it can. A battle poll returning zero
        // contributors mid-session is the plausible way to hit this for real.
        var handler = new StubHandler(Echo);
        var client = new NameClient(new HttpClient(handler));

        await client.ResolveAsync([1, 156, 261], CancellationToken.None);
        var empty = await client.ResolveAsync([], CancellationToken.None);

        Assert.Empty(empty);
    }

    [Fact]
    public async Task IdentifiesItselfInTheUserAgentOnTheActualRequest()
    {
        // Asserted against the header the handler RECEIVED, not against the constant. A version of
        // this test that read ClanClient.UserAgent directly would pass with the header never set.
        var handler = new StubHandler(Echo);
        var client = new NameClient(new HttpClient(handler));

        await client.ResolveAsync([1], CancellationToken.None);

        Assert.Contains("UrScore", handler.UserAgents[0]);
    }

    [Fact]
    public async Task ACallerRequestedCancellationPropagatesRatherThanBecomingAnEmptyResult()
    {
        // Same rule as ClanClient: a stop the user asked for is not a failure to hide behind an
        // empty dictionary — it must reach the caller as the exception it is. The token is already
        // cancelled before the call starts, so cancellationToken.IsCancellationRequested is true
        // by the time FetchAsync's catch runs, and the `when` guard must let it through.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var client = new NameClient(new HttpClient(new NeverRespondsHandler()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ResolveAsync([1], cts.Token));
    }

    [Fact]
    public async Task AnInternalTimeoutBecomesAnAbsenceOfNamesNotAnUnhandledCancellation()
    {
        // Mirrors ClanClientTests.ASlowEndpointIsAMissNotAnUnhandledCancellation: a merely slow
        // endpoint throws the same exception SHAPE as a caller's own cancellation — nobody asked
        // to stop, but the exception looks identical without checking which token fired. A short
        // HttpClient.Timeout reproduces that in milliseconds rather than waiting out the real
        // 15-second bound, and the caller's own token (CancellationToken.None) is never cancelled.
        var http = new HttpClient(new NeverRespondsHandler()) { Timeout = TimeSpan.FromMilliseconds(50) };
        var client = new NameClient(http);

        var names = await client.ResolveAsync([1], CancellationToken.None);

        Assert.Empty(names);
    }

    [Fact]
    public async Task ARowWhoseIdOverflowsALongIsSkippedRatherThanFabricated()
    {
        // JsonNav.TryUserId exists because a bare (long) cast on a double turns 1e20 into
        // long.MaxValue — fabricating an id nobody sent rather than rejecting the row. This is the
        // third file in this codebase to carry that call, after the defect was found and fixed
        // twice elsewhere. Requesting long.MaxValue itself (rather than some ordinary id) means
        // the batch-narrowing above cannot be the reason this test passes — only TryUserId
        // rejecting the row before it ever becomes a candidate can make it pass.
        var handler = new StubHandler((_, _) => Ok("""
            {"data":[{"hasVerifiedBadge":true,"id":1e20,"name":"Ghost","displayName":"Ghost"}]}
            """));
        var client = new NameClient(new HttpClient(handler));

        var names = await client.ResolveAsync([long.MaxValue], CancellationToken.None);

        Assert.Empty(names);
    }

    [Fact]
    public async Task MalformedJsonYieldsNoNamesRatherThanThrowing()
    {
        var handler = new StubHandler((_, _) => Ok("not json"));
        var client = new NameClient(new HttpClient(handler));

        Assert.Empty(await client.ResolveAsync([1], CancellationToken.None));
    }

    [Fact]
    public async Task ADataFieldThatIsNotAnArrayYieldsNoNamesRatherThanThrowing()
    {
        var handler = new StubHandler((_, _) => Ok("""{"data":"unexpected"}"""));
        var client = new NameClient(new HttpClient(handler));

        Assert.Empty(await client.ResolveAsync([1], CancellationToken.None));
    }

    [Fact]
    public async Task AResponseWithNoDataFieldYieldsNoNamesRatherThanThrowing()
    {
        var handler = new StubHandler((_, _) => Ok("""{"somethingElse":true}"""));
        var client = new NameClient(new HttpClient(handler));

        Assert.Empty(await client.ResolveAsync([1], CancellationToken.None));
    }
}
