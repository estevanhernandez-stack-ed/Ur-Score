using System.Net;
using System.Text;
using Labs626.UrScore.Source;

namespace UrScore.Tests;

public class NameClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
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
            Bodies.Add(request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return respond(request);
        }
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private const string ThreeNames = """
        {"data":[{"hasVerifiedBadge":true,"id":1,"name":"Roblox","displayName":"Roblox"},
                 {"hasVerifiedBadge":true,"id":156,"name":"builderman","displayName":"builderman"},
                 {"hasVerifiedBadge":false,"id":261,"name":"Shedletsky","displayName":"Shedletsky"}]}
        """;

    [Fact]
    public async Task ResolvesAWholeBatchInOneCall()
    {
        // A clan battle returns around 75 contributors and the endpoint takes 100 ids, so a whole
        // clan is one request. Anything that made this N requests would be the defect.
        var handler = new StubHandler(_ => Ok(ThreeNames));
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
        var handler = new StubHandler(_ => Ok(ThreeNames));
        var client = new NameClient(new HttpClient(handler));

        await client.ResolveAsync([1, 156, 261], CancellationToken.None);
        var second = await client.ResolveAsync([1, 156], CancellationToken.None);

        Assert.Equal(1, handler.Calls);              // no second request at all
        Assert.Equal("Roblox", second[1]);           // served from cache
    }

    [Fact]
    public async Task AsksOnlyForTheNewOnesWhenSomeAreCached()
    {
        var handler = new StubHandler(_ => Ok(ThreeNames));
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
        var handler = new StubHandler(_ => Ok("""{"data":[]}"""));
        var client = new NameClient(new HttpClient(handler));

        await client.ResolveAsync([.. Enumerable.Range(1, 250).Select(i => (long)i)], CancellationToken.None);

        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task AFailedCallYieldsNoNamesRatherThanThrowing()
    {
        // Names are decoration on a leaderboard that works without them. A failure here must cost
        // the names and nothing else — never the poll, never the reporting.
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var client = new NameClient(new HttpClient(handler));

        var names = await client.ResolveAsync([1, 156], CancellationToken.None);

        Assert.Empty(names);
    }

    [Fact]
    public async Task ATransportFailureYieldsNoNamesRatherThanThrowing()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("no network"));
        var client = new NameClient(new HttpClient(handler));

        Assert.Empty(await client.ResolveAsync([1], CancellationToken.None));
    }

    [Fact]
    public async Task AFailureIsNotCachedAsAnAnswer()
    {
        // Caching a failure would mean one rate-limited minute costs the names for the whole
        // session, with nothing anywhere saying why the leaderboard is numbers.
        var fail = true;
        var handler = new StubHandler(_ => fail
            ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            : Ok(ThreeNames));
        var client = new NameClient(new HttpClient(handler));

        Assert.Empty(await client.ResolveAsync([156], CancellationToken.None));

        fail = false;
        var names = await client.ResolveAsync([156], CancellationToken.None);

        Assert.Equal("builderman", names[156]);
    }

    [Fact]
    public async Task AnEmptyRequestAsksNothing()
    {
        var handler = new StubHandler(_ => Ok(ThreeNames));
        var client = new NameClient(new HttpClient(handler));

        Assert.Empty(await client.ResolveAsync([], CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task IdentifiesItselfInTheUserAgentOnTheActualRequest()
    {
        // Asserted against the header the handler RECEIVED, not against the constant. A version of
        // this test that read ClanClient.UserAgent directly would pass with the header never set.
        var handler = new StubHandler(_ => Ok(ThreeNames));
        var client = new NameClient(new HttpClient(handler));

        await client.ResolveAsync([1], CancellationToken.None);

        Assert.Contains("UrScore", handler.UserAgents[0]);
    }
}
