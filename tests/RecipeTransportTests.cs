using System.Net;
using System.Text;
using System.Text.RegularExpressions;
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
    public void NoTransportInSrcKeepsRawResponses()
    {
        // A response body holds every row the source returned, other players' ids and values included, and other
        // players never reach disk. The optional raw directory stays for tests; the app always passes null.
        var src = Path.Combine(RepoRoot(), "src");
        var constructions = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => ConstructorArguments(File.ReadAllText(f), "HttpRecipeTransport")
                .Select(args => (Relative: Path.GetRelativePath(src, f), Args: args)))
            .ToList();

        Assert.True(constructions.Count >= 2, $"Expected the app's and --try's transports; found {constructions.Count}.");
        var keeping = constructions
            .Where(c => c.Args.Count < 2 || c.Args[1] is not ("null" or "rawDirectory: null"))
            .Select(c => $"{c.Relative}: ({string.Join(", ", c.Args)})")
            .ToList();
        Assert.True(keeping.Count == 0, $"These transports may keep raw responses: {string.Join("; ", keeping)}. Pass rawDirectory: null.");
    }

    /// <summary>The top-level arguments of every <c>new [Qualifier.]Type(...)</c> in <paramref name="text"/>, trimmed.</summary>
    private static IEnumerable<IReadOnlyList<string>> ConstructorArguments(string text, string type)
    {
        foreach (Match match in Regex.Matches(text, @"\bnew\s+(?:[\w.]+\.)?" + Regex.Escape(type) + @"\s*\("))
        {
            var args = new List<string>();
            var current = new StringBuilder();
            var depth = 0;
            for (var i = match.Index + match.Length; i < text.Length; i++)
            {
                var c = text[i];
                if (c is '(' or '[' or '{') depth++;
                else if (c is ')' or ']' or '}')
                {
                    if (depth == 0)
                    {
                        args.Add(current.ToString().Trim());
                        break;
                    }

                    depth--;
                }
                else if (c == ',' && depth == 0)
                {
                    args.Add(current.ToString().Trim());
                    current.Clear();
                    continue;
                }

                current.Append(c);
            }

            yield return args;
        }
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
    public async Task ATransportErrorIsRedacted()
    {
        var result = await new HttpRecipeTransport(new HttpClient(new ThrowingHandler("bad url ?key=abc123secret")), null,
                new Redactor(() => ["abc123secret"]))
            .GetAsync(new Uri("https://example.com/a"), NoHeaders, "t", CancellationToken.None);

        Assert.DoesNotContain("abc123secret", result.Error);
        Assert.Contains("[key hidden]", result.Error);
    }
}
