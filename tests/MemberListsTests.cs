using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

/// <summary>
/// Name your clan once (0.7.0): one read of a clan's member list says which of YOUR accounts are in it. The list holds every
/// other member's id too, and those are compared and dropped: only the intersection with your ids ever leaves the read.
/// </summary>
public class MemberListsTests
{
    private static readonly RecipeMembers Members = new("https://example.test/api/clan/{clan}", "data.Members", "UserID", "data.Owner");

    private static readonly Dictionary<string, string> K0i2 = new() { ["clan"] = "K0i2" };

    private sealed class Transport(Func<Uri, FetchResult> answer) : IRecipeTransport
    {
        public readonly List<(Uri Url, string Label)> Asked = [];

        public Task<FetchResult> GetAsync(Uri url, IReadOnlyDictionary<string, string> headers, string label, CancellationToken ct)
        {
            Asked.Add((url, label));
            return Task.FromResult(answer(url));
        }
    }

    /// <summary>A clan of <paramref name="others"/> strangers (ids 5000 up), plus these members and this owner.</summary>
    internal static string Roster(int others, long owner, params long[] members)
    {
        var rows = Enumerable.Range(0, others).Select(i => 5000L + i).Concat(members)
            .Select(id => "{\"UserID\":" + id + ",\"PermissionLevel\":10,\"JoinTime\":1758000000}");
        return "{\"data\":{\"Owner\":" + owner + ",\"Members\":[" + string.Join(",", rows) + "]}}";
    }

    [Fact]
    public async Task OnlyYourOwnIdsComeBackAndTheOwnerCounts()
    {
        var transport = new Transport(_ => new FetchResult(200, Roster(70, owner: 101, 201, 301), null));

        var result = await new MemberLists(transport).FindAsync(Members, K0i2, new HashSet<long> { 101, 201, 999 }, CancellationToken.None);

        Assert.Null(result.Problem);
        Assert.Equal(new long[] { 101, 201 }, result.Found.Order().ToArray());
        var (url, label) = Assert.Single(transport.Asked);
        Assert.Equal("https://example.test/api/clan/K0i2", url.AbsoluteUri);
        Assert.Equal("members", label);
    }

    [Fact]
    public async Task TheClanNameIsEncodedIntoTheAddress()
    {
        var transport = new Transport(_ => new FetchResult(200, Roster(0, owner: 1), null));

        await new MemberLists(transport).FindAsync(Members, new Dictionary<string, string> { ["clan"] = "A B/C" }, new HashSet<long> { 1 }, CancellationToken.None);

        Assert.Equal("https://example.test/api/clan/A%20B%2FC", transport.Asked[0].Url.OriginalString);
    }

    [Fact]
    public async Task WithNoAccountsOfYoursNothingIsAsked()
    {
        var transport = new Transport(_ => throw new InvalidOperationException("asked"));

        var result = await new MemberLists(transport).FindAsync(Members, K0i2, new HashSet<long>(), CancellationToken.None);

        Assert.Empty(result.Found);
        Assert.Null(result.Problem);
        Assert.Empty(transport.Asked);
    }

    [Theory]
    [InlineData(404, "{}", "example.test answered 404.")]
    [InlineData(200, "not json", "example.test did not return valid JSON.")]
    [InlineData(200, """{"data":{"Members":{}}}""", "example.test did not return a list at 'data.Members'.")]
    public async Task AReadThatFailsSaysWhyAndFindsNobody(int status, string body, string problem)
    {
        var result = await new MemberLists(new Transport(_ => new FetchResult(status, body, null)))
            .FindAsync(Members, K0i2, new HashSet<long> { 101 }, CancellationToken.None);

        Assert.Empty(result.Found);
        Assert.Equal(problem, result.Problem);
    }

    [Fact]
    public async Task AnUnreachableHostSaysSo()
    {
        var result = await new MemberLists(new Transport(_ => new FetchResult(null, null, "Could not reach example.test: down")))
            .FindAsync(Members, K0i2, new HashSet<long> { 101 }, CancellationToken.None);

        Assert.Equal("Could not reach example.test: down", result.Problem);
    }

    [Fact]
    public async Task AMissingOwnerIsNotAProblem()
    {
        var result = await new MemberLists(new Transport(_ => new FetchResult(200, """{"data":{"Members":[{"UserID":201}]}}""", null)))
            .FindAsync(Members, K0i2, new HashSet<long> { 201 }, CancellationToken.None);

        Assert.Null(result.Problem);
        Assert.Equal(new long[] { 201 }, result.Found.ToArray());
    }
}
