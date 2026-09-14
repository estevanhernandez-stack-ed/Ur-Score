using System.Net;
using System.Text;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class RecipeTransportTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class NeverRespondsHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class ThrowingHandler(string message) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException(message);
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static readonly Dictionary<string, string> NoHeaders = [];

    [Fact]
    public async Task IdentifiesItselfOnTheActualRequest()
    {
        var handler = new StubHandler(_ => Respond(HttpStatusCode.OK, "{}"));
        await new HttpRecipeTransport(new HttpClient(handler), null, Redactor.None)
            .GetAsync(new Uri("https://example.com/a"), NoHeaders, "t", CancellationToken.None);

        Assert.Contains("UrScore", handler.Requests[0].Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task SendsTheHeadersItIsGiven()
    {
        var handler = new StubHandler(_ => Respond(HttpStatusCode.OK, "{}"));
        await new HttpRecipeTransport(new HttpClient(handler), null, Redactor.None)
            .GetAsync(new Uri("https://example.com/a"), new Dictionary<string, string> { ["x-api-key"] = "k123456" }, "t", CancellationToken.None);

        Assert.Equal("k123456", handler.Requests[0].Headers.GetValues("x-api-key").Single());
    }

    [Fact]
    public async Task RefusesPlainHttpWithoutMakingARequest()
    {
        var handler = new StubHandler(_ => Respond(HttpStatusCode.OK, "{}"));
        var result = await new HttpRecipeTransport(new HttpClient(handler), null, Redactor.None)
            .GetAsync(new Uri("http://example.com/a"), NoHeaders, "t", CancellationToken.None);

        Assert.Empty(handler.Requests);
        Assert.False(result.Answered);
        Assert.Equal("Refused to contact example.com: recipes may only use https.", result.Error);
    }

    [Fact]
    public async Task AFailingStatusStillCarriesItsBody()
    {
        var handler = new StubHandler(_ => Respond(HttpStatusCode.BadRequest, """{"error":"no such clan"}"""));
        var result = await new HttpRecipeTransport(new HttpClient(handler), null, Redactor.None)
            .GetAsync(new Uri("https://example.com/a"), NoHeaders, "t", CancellationToken.None);

        Assert.True(result.Answered);
        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Status);
        Assert.Contains("no such clan", result.Body);
    }

    [Fact]
    public async Task SavesTheRawBodyBeforeTheStatusCheckAndRedacted()
    {
        var dir = Path.Combine(Path.GetTempPath(), "urscore-transport-" + Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new StubHandler(_ => Respond(HttpStatusCode.InternalServerError, """{"echo":"abc123secret"}"""));
            await new HttpRecipeTransport(new HttpClient(handler), dir, new Redactor(() => ["abc123secret"]))
                .GetAsync(new Uri("https://example.com/a"), NoHeaders, "petsim-step2", CancellationToken.None);

            var saved = File.ReadAllText(Path.Combine(dir, "petsim-step2.json"));
            Assert.Contains("[key hidden]", saved);
            Assert.DoesNotContain("abc123secret", saved);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ASlowEndpointIsAnErrorNotACancellation()
    {
        var client = new HttpClient(new NeverRespondsHandler()) { Timeout = TimeSpan.FromMilliseconds(50) };
        var result = await new HttpRecipeTransport(client, null, Redactor.None)
            .GetAsync(new Uri("https://example.com/a"), NoHeaders, "t", CancellationToken.None);

        Assert.False(result.Answered);
        Assert.StartsWith("Could not reach example.com:", result.Error);
    }

    [Fact]
    public async Task AStopTheCallerAskedForPropagates()
    {
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new HttpRecipeTransport(new HttpClient(new NeverRespondsHandler()), null, Redactor.None)
                .GetAsync(new Uri("https://example.com/a"), NoHeaders, "t", stop.Token));
    }

    [Fact]
    public void TheRecipeHandlerNeverFollowsRedirectsOrKeepsCookies()
    {
        using var handler = HttpRecipeTransport.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
    }

    [Fact]
    public async Task ATransportErrorIsRedacted()
    {
        var result = await new HttpRecipeTransport(new HttpClient(new ThrowingHandler("bad url ?key=abc123secret")), null,
                new Redactor(() => ["abc123secret"]))
            .GetAsync(new Uri("https://example.com/a"), NoHeaders, "t", CancellationToken.None);

        Assert.DoesNotContain("abc123secret", result.Error);
        Assert.Contains("[key hidden]", result.Error);
    }
}
