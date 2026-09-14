using System.Net;
using System.Text;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.Source;

namespace UrScore.Tests;

public class IconClientTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "urscore-icons-" + Guid.NewGuid().ToString("N"));

    private static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    private const string AssetId = "14976358748";

    private const string ThumbnailsUrl = "https://thumbnails.roblox.com/v1/assets?assetIds=14976358748&size=150x150&format=Png";

    private const string ImageUrl = "https://tr.rbxcdn.com/180DAY-abc/150/150/Image/Png/noFilter";

    private static readonly IReadOnlySet<string> RecipeHostSet = new HashSet<string> { "ps99.example" };

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private sealed class RouteHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.Ordinal);

        public List<HttpRequestMessage> Requests { get; } = [];

        public RouteHandler On(string url, Func<HttpResponseMessage> respond)
        {
            _routes[url] = respond;
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_routes.TryGetValue(request.RequestUri!.AbsoluteUri, out var respond)
                ? respond()
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private static string Thumbnail(string state, string imageUrl) =>
        $$"""{ "data": [ { "targetId": 14976358748, "state": "{{state}}", "imageUrl": "{{imageUrl}}", "version": "TN3" } ] }""";

    private IconClient Client(RouteHandler handler) => new(handler, _dir, () => Now);

    [Fact]
    public async Task AnAssetIdIsLookedUpWithRobloxThenFetchedAndCached()
    {
        var handler = new RouteHandler()
            .On(ThumbnailsUrl, () => Json(Thumbnail("Completed", ImageUrl)))
            .On(ImageUrl, () => Bytes(Png));

        var file = await Client(handler).ResolveAsync($"rbxassetid://{AssetId}", RecipeHostSet, CancellationToken.None);

        Assert.Equal(Path.Combine(_dir, $"{AssetId}.png"), file);
        Assert.Equal(Png, File.ReadAllBytes(file!));
        Assert.Equal(new[] { ThumbnailsUrl, ImageUrl }, handler.Requests.Select(r => r.RequestUri!.AbsoluteUri).ToArray());
        Assert.All(handler.Requests, r => Assert.Contains("UrScore", r.Headers.UserAgent.ToString()));
    }

    [Fact]
    public async Task ACachedIconYoungerThanSevenDaysIsUsedWithoutAsking()
    {
        Directory.CreateDirectory(_dir);
        var cached = Path.Combine(_dir, $"{AssetId}.png");
        File.WriteAllBytes(cached, Png);
        File.SetLastWriteTimeUtc(cached, Now.AddDays(-6).UtcDateTime);
        var handler = new RouteHandler();

        Assert.Equal(cached, await Client(handler).ResolveAsync($"rbxassetid://{AssetId}", RecipeHostSet, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AnIconOlderThanSevenDaysIsFetchedAgain()
    {
        Directory.CreateDirectory(_dir);
        var cached = Path.Combine(_dir, $"{AssetId}.png");
        File.WriteAllBytes(cached, Jpeg);
        File.SetLastWriteTimeUtc(cached, Now.AddDays(-8).UtcDateTime);
        var handler = new RouteHandler()
            .On(ThumbnailsUrl, () => Json(Thumbnail("Completed", ImageUrl)))
            .On(ImageUrl, () => Bytes(Png));

        await Client(handler).ResolveAsync($"rbxassetid://{AssetId}", RecipeHostSet, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(Png, File.ReadAllBytes(cached));
    }

    [Theory]
    [InlineData("https://evil.example/icon.png")]
    [InlineData("https://notrbxcdn.com/icon.png")]
    [InlineData("http://tr.rbxcdn.com/icon.png")]
    public async Task AnImageOffRobloxsPictureDomainIsRefusedBeforeItIsFetched(string imageUrl)
    {
        var handler = new RouteHandler()
            .On(ThumbnailsUrl, () => Json(Thumbnail("Completed", imageUrl)))
            .On(imageUrl, () => Bytes(Png));

        Assert.Null(await Client(handler).ResolveAsync($"rbxassetid://{AssetId}", RecipeHostSet, CancellationToken.None));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AThumbnailThatIsNotCompletedIsNotFetched()
    {
        var handler = new RouteHandler().On(ThumbnailsUrl, () => Json(Thumbnail("Pending", ImageUrl)));

        Assert.Null(await Client(handler).ResolveAsync($"rbxassetid://{AssetId}", RecipeHostSet, CancellationToken.None));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ARedirectIsNeverFollowed()
    {
        var handler = new RouteHandler()
            .On(ThumbnailsUrl, () => Json(Thumbnail("Completed", ImageUrl)))
            .On(ImageUrl, () => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://evil.example/icon.png") } });

        Assert.Null(await Client(handler).ResolveAsync($"rbxassetid://{AssetId}", RecipeHostSet, CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);
        Assert.False(Directory.Exists(_dir) && Directory.EnumerateFiles(_dir).Any());
    }

    [Fact]
    public async Task AnHttpsIconOnOneOfTheRecipesHostsIsFetchedUnderAHashedName()
    {
        const string url = "https://ps99.example/icons/clan.png";
        var handler = new RouteHandler().On(url, () => Bytes(Jpeg));

        var file = await Client(handler).ResolveAsync(url, RecipeHostSet, CancellationToken.None);

        Assert.NotNull(file);
        Assert.StartsWith("url-", Path.GetFileName(file));
        Assert.Equal(Jpeg, File.ReadAllBytes(file!));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("https://other.example/icons/clan.png")]
    [InlineData("http://ps99.example/icons/clan.png")]
    [InlineData("rbxassetid://not-a-number")]
    [InlineData("Clan icon")]
    [InlineData("")]
    public async Task AnythingElseIsIgnoredWithoutARequest(string iconText)
    {
        var handler = new RouteHandler();

        Assert.Null(await Client(handler).ResolveAsync(iconText, RecipeHostSet, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AnImageOverOneMegabyteIsRefused()
    {
        var big = new byte[IconClient.MaxBytes + 1];
        Png.CopyTo(big, 0);
        var handler = new RouteHandler()
            .On(ThumbnailsUrl, () => Json(Thumbnail("Completed", ImageUrl)))
            .On(ImageUrl, () => Bytes(big));

        Assert.Null(await Client(handler).ResolveAsync($"rbxassetid://{AssetId}", RecipeHostSet, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(_dir, $"{AssetId}.png")));
    }

    [Fact]
    public async Task SomethingThatIsNotAPngOrJpegIsRefused()
    {
        var handler = new RouteHandler()
            .On(ThumbnailsUrl, () => Json(Thumbnail("Completed", ImageUrl)))
            .On(ImageUrl, () => Bytes(Encoding.UTF8.GetBytes("<html>not a picture</html>")));

        Assert.Null(await Client(handler).ResolveAsync($"rbxassetid://{AssetId}", RecipeHostSet, CancellationToken.None));
    }

    [Fact]
    public void ARecipesOwnHostsAreItsStepsAndItsSearchLists()
    {
        var clan = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;
        Assert.Equal(new[] { "ps99.biggamesapi.io" }, RecipeHosts.ContactedBy(clan).ToArray());
    }
}
