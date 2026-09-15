using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class SearchListsTests
{
    private static readonly RecipeSearch Search = new("https://example.test/api/list", "data");

    private sealed class CountingTransport(Func<FetchResult> answer) : IRecipeTransport
    {
        public int Calls;

        public Task<FetchResult> GetAsync(Uri url, IReadOnlyDictionary<string, string> headers, string label, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(answer());
        }
    }

    [Fact]
    public async Task TheListIsReadOnceAndKeptForTheSession()
    {
        var transport = new CountingTransport(() => new FetchResult(200, """{"data":["CCGP","K0i2","CCGP",7,"",null]}""", null));
        var lists = new SearchLists(transport);

        var first = await lists.GetAsync(Search, CancellationToken.None);
        var second = await lists.GetAsync(Search, CancellationToken.None);

        Assert.Null(first.Problem);
        Assert.Equal(new[] { "CCGP", "K0i2" }, first.Names.ToArray());
        Assert.Same(first, second);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task AFailedReadIsNotKeptSoTheNextAskTriesAgain()
    {
        var reachable = false;
        var transport = new CountingTransport(() => reachable
            ? new FetchResult(200, """{"data":["CCGP"]}""", null)
            : new FetchResult(null, null, "Could not reach example.test: timed out"));
        var lists = new SearchLists(transport);

        var failed = await lists.GetAsync(Search, CancellationToken.None);
        reachable = true;
        var retried = await lists.GetAsync(Search, CancellationToken.None);

        Assert.Equal("Could not reach example.test: timed out", failed.Problem);
        Assert.Empty(failed.Names);
        Assert.Equal(new[] { "CCGP" }, retried.Names.ToArray());
        Assert.Equal(2, transport.Calls);
    }

    [Theory]
    [InlineData(500, """{"data":["CCGP"]}""", "example.test answered 500.")]
    [InlineData(200, """{"data":{"a":1}}""", "example.test did not return a list at 'data'.")]
    [InlineData(200, "not json", "example.test did not return valid JSON.")]
    [InlineData(200, """{"data":[]}""", "example.test returned an empty list.")]
    public async Task ABadAnswerSaysWhatWasWrong(int status, string body, string problem)
    {
        var lists = new SearchLists(new CountingTransport(() => new FetchResult(status, body, null)));

        var result = await lists.GetAsync(Search, CancellationToken.None);

        Assert.Equal(problem, result.Problem);
        Assert.Empty(result.Names);
    }

    [Fact]
    public void MatchesPutExactThenStartsWithThenAnywhereAndStopAtEight()
    {
        string[] names = ["Accursed", "cc", "CCKings", "CCGP", .. Enumerable.Range(1, 10).Select(i => $"Xcc{i}")];

        var matches = SearchLists.Match(names, " cC ");

        Assert.Equal(new[] { "cc", "CCKings", "CCGP", "Accursed", "Xcc1", "Xcc2", "Xcc3", "Xcc4" }, matches.ToArray());
    }

    [Fact]
    public void ABlankQueryMatchesNothing() => Assert.Empty(SearchLists.Match(["CCGP"], "   "));
}
