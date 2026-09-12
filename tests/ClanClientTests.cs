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
        // Clan names contain spaces. Unescaped, the request is malformed and the failure that
        // comes back reads exactly like "no clan by that name".
        var handler = new StubHandler(_ => Ok("""{ "data": { "Battles": {} } }"""));
        var client = new ClanClient(new HttpClient(handler), rawDirectory: null);

        await client.ContributionsAsync("Noodle Clan", "B", CancellationToken.None);

        // RequestUri.ToString() unescapes %20 back to a literal space for display — it is not
        // what goes out over the wire. AbsoluteUri is the escaped form that actually gets sent,
        // which is the thing this test exists to check.
        Assert.Contains("Noodle%20Clan", handler.Requests[0].RequestUri!.AbsoluteUri);
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
    public async Task ATransportFailureIsAMissNotAnException()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("no network"));
        var client = new ClanClient(new HttpClient(handler), rawDirectory: null);

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
