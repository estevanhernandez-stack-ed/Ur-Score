using System.Net;
using System.Text;
using Labs626.UrScore.Source;

namespace UrScore.Tests;

public class ClanClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>
    /// Never completes on its own — only cancellation of the token handed to it ends the wait.
    /// Stands in for a merely slow endpoint: with a short <see cref="HttpClient.Timeout"/> set by
    /// the caller, that cancellation arrives in milliseconds instead of the real 30-second bound.
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

    [Fact]
    public async Task IdentifiesItselfInTheUserAgent()
    {
        // Polling someone else's API anonymously is rude, and makes this indistinguishable from a
        // scraper if they ever read their logs.
        var handler = new StubHandler(_ => Ok("""{ "data": { "configName": "B" } }"""));
        var client = new ClanClient(new HttpClient(handler), rawDirectory: null);

        await client.ActiveBattleAsync(CancellationToken.None);

        Assert.Contains(handler.Requests[0].Headers.UserAgent,
            h => h.ToString().Contains("UrScore", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EscapesTheClanNameIntoThePath()
    {
        // A slash, not a space: Uri auto-escapes a bare space to %20 in AbsoluteUri whether or not
        // we escape it ourselves, so a space cannot tell escaped from unescaped apart and an
        // earlier version of this test stayed green with the escaping deleted. A slash changes the
        // request's SHAPE if it is not escaped — it becomes another path segment.
        var handler = new StubHandler(_ => Ok("""{ "data": { "Battles": {} } }"""));
        var client = new ClanClient(new HttpClient(handler), rawDirectory: null);

        await client.ContributionsAsync("Noodle/Clan", "B", CancellationToken.None);

        Assert.EndsWith("/clan/Noodle%2FClan", handler.Requests[0].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task ANonSuccessStatusIsAMissNamingTheCode()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = new ClanClient(new HttpClient(handler), rawDirectory: null);

        var result = await client.ContributionsAsync("Ghost Clan", "B", CancellationToken.None);

        Assert.NotNull(result.Miss);
        Assert.Contains("404", result.Miss);

        // A refusal from the server is grouped with "could not reach it" — the remedy for both
        // is the same (wait), unlike a shape the parser could not read.
        Assert.True(result.MissIsTransport);
    }

    [Fact]
    public async Task A4xxOnTheClanCallNamesTheClanAsSentSoATypoIsVisible()
    {
        // F7: "clan name not found" was specified (spec §5's state table, docs/SMOKE.md) and
        // implemented nowhere — every non-2xx became the identical generic transport miss, so a
        // typo in the clan name read exactly like the whole endpoint being down. Verified live: a
        // made-up clan name returns 400 from the real endpoint, not 404 — this must not depend on
        // which 4xx it is, or on the vendor's error body saying anything about the name at all.
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var client = new ClanClient(new HttpClient(handler), rawDirectory: null);

        var result = await client.ContributionsAsync("Noodle Clan", "B", CancellationToken.None);

        Assert.Contains("Noodle Clan", result.Miss);
        Assert.Contains("400", result.Miss);
        Assert.True(result.MissIsTransport);
    }

    [Fact]
    public async Task A5xxOnTheClanCallDoesNotClaimTheClanWasNotFound()
    {
        // A 5xx is the vendor's own server failing, not a name lookup failing — must not be
        // misattributed to a typo the user did not make.
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = new ClanClient(new HttpClient(handler), rawDirectory: null);

        var result = await client.ContributionsAsync("Noodle Clan", "B", CancellationToken.None);

        Assert.DoesNotContain("was not found", result.Miss);
        Assert.Contains("500", result.Miss);
    }

    [Fact]
    public async Task ATransportFailureIsAMissNotAnException()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("no network"));
        var client = new ClanClient(new HttpClient(handler), rawDirectory: null);

        var probe = await client.ActiveBattleAsync(CancellationToken.None);

        Assert.NotNull(probe.Miss);
        Assert.True(probe.MissIsTransport);
    }

    [Fact]
    public async Task ASlowEndpointIsAMissNotAnUnhandledCancellation()
    {
        // A merely slow endpoint looks, to the exception type, exactly like HttpClient's own
        // request timeout firing — an OperationCanceledException with nobody having asked to
        // stop. Before the fix, the bare `catch (OperationCanceledException) { throw; }` rethrew
        // that as though the caller had cancelled. A short HttpClient.Timeout reproduces the same
        // exception shape in milliseconds, without waiting out ClanClient's own 30-second bound —
        // this is deliberately fast, not a multi-second sleep in the suite.
        var http = new HttpClient(new NeverRespondsHandler()) { Timeout = TimeSpan.FromMilliseconds(50) };
        var client = new ClanClient(http, rawDirectory: null);

        var probe = await client.ActiveBattleAsync(CancellationToken.None);

        Assert.NotNull(probe.Miss);
        Assert.True(probe.MissIsTransport);
    }

    [Fact]
    public async Task AParserMissLeavesTheTransportFlagFalse()
    {
        // The request succeeded; the body just was not the shape expected. Task 8 needs this to
        // read as "someone needs to look at the shape", not "wait and it'll come back" — the two
        // have nothing in common as remedies, so the flag must not be true here.
        var handler = new StubHandler(_ => Ok("""{ "data": { "notConfigName": "nope" } }"""));
        var client = new ClanClient(new HttpClient(handler), rawDirectory: null);

        var probe = await client.ActiveBattleAsync(CancellationToken.None);

        Assert.NotNull(probe.Miss);
        Assert.False(probe.MissIsTransport);
    }

    [Fact]
    public async Task KeepsTheRawResponseForDiagnostics()
    {
        // When the shape changes, whoever debugs it needs the bytes, not our summary of the bytes.
        var dir = Directory.CreateTempSubdirectory().FullName;
        var handler = new StubHandler(_ => Ok("""{ "data": { "configName": "B" } }"""));
        var client = new ClanClient(new HttpClient(handler), rawDirectory: dir);

        await client.ActiveBattleAsync(CancellationToken.None);

        var saved = Directory.GetFiles(dir);
        Assert.Single(saved);
        Assert.Contains("configName", File.ReadAllText(saved[0]));
    }

    [Fact]
    public async Task KeepsTheRawResponseWhenTheRequestFails()
    {
        // SaveRaw writes the body before the status check, so a 500 carrying an explanatory body
        // is kept rather than discarded. The success-only test above cannot prove that: the whole
        // point of keeping the raw response is for the calls that went wrong.
        var dir = Directory.CreateTempSubdirectory().FullName;
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(
                """{ "error": "clan service is down" }""", Encoding.UTF8, "application/json"),
        });
        var client = new ClanClient(new HttpClient(handler), rawDirectory: dir);

        await client.ActiveBattleAsync(CancellationToken.None);

        var saved = Directory.GetFiles(dir);
        Assert.Single(saved);
        Assert.Contains("clan service is down", File.ReadAllText(saved[0]));
    }
}
