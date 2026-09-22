using System.Net;
using System.Text;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.Fetch;

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

    /// <summary>A body stream whose read never completes on its own, honouring cancellation like a real network read would.</summary>
    private sealed class NeverCompletingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0; // Unreachable: the delay above only ever ends by throwing.
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// A body stream that serves <paramref name="prefix"/> and then keeps serving bytes forever,
    /// never signalling end of stream, non-seekable so <see cref="StreamContent"/> reports no
    /// Content-Length. <see cref="TotalRead"/> proves how far the reader actually got.
    /// </summary>
    private sealed class CountingEndlessStream(byte[] prefix) : Stream
    {
        private int _prefixSent;

        public long TotalRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int written;
            if (_prefixSent < prefix.Length)
            {
                written = Math.Min(count, prefix.Length - _prefixSent);
                Array.Copy(prefix, _prefixSent, buffer, offset, written);
                _prefixSent += written;
            }
            else
            {
                written = count;
            }

            TotalRead += written;
            return Task.FromResult(written);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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

    /// <summary>
    /// <see cref="HttpCompletionOption.ResponseHeadersRead"/> means the body is read after
    /// <c>HttpClient</c>'s own timeout has already been satisfied by the headers, so the picture fetch
    /// must bound the body read itself — the same way <c>HttpRecipeTransport.GetAsync</c> bounds a
    /// recipe fetch with a linked, cancel-after token.
    /// </summary>
    [Fact]
    public async Task AFetchWhoseBodyNeverFinishesIsAbandonedAfterTheRequestTimeout()
    {
        var handler = new RouteHandler()
            .On(ThumbnailsUrl, () => Json(Thumbnail("Completed", ImageUrl)))
            .On(ImageUrl, () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new NeverCompletingStream()) });

        var client = new IconClient(handler, _dir, () => Now, TimeSpan.FromMilliseconds(200));

        Assert.Null(await client.ResolveAsync($"rbxassetid://{AssetId}", RecipeHostSet, CancellationToken.None));
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

    /// <summary>
    /// Backlog V3-S.6: the window draws last session's picture before any read, so the lookup behind that is the cache alone.
    /// Age only decides whether a READ asks Roblox again; a picture on disk is still the picture until then.
    /// </summary>
    [Fact]
    public void APictureAlreadyOnDiskIsFoundHoweverOldAndNothingIsAsked()
    {
        Directory.CreateDirectory(_dir);
        var cached = Path.Combine(_dir, $"{AssetId}.png");
        File.WriteAllBytes(cached, Png);
        File.SetLastWriteTimeUtc(cached, Now.AddDays(-30).UtcDateTime);
        var handler = new RouteHandler();

        Assert.Equal(cached, Client(handler).CachedFile($" rbxassetid://{AssetId} ", RecipeHostSet));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task TheCacheLookupFindsTheSamePictureAFetchKeptAndOnlyUnderTheSameRules()
    {
        const string url = "https://ps99.example/icons/clan.png";
        var handler = new RouteHandler().On(url, () => Bytes(Jpeg));
        var client = Client(handler);
        var file = await client.ResolveAsync(url, RecipeHostSet, CancellationToken.None);

        Assert.NotNull(file);
        Assert.Equal(file, client.CachedFile(url, RecipeHostSet));

        // The same address from a recipe that doesn't contact that host is refused here too, picture on disk or not.
        Assert.Null(client.CachedFile(url, new HashSet<string> { "other.example" }));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("rbxassetid://14976358748")]
    [InlineData("rbxassetid://0")]
    [InlineData("rbxassetid://avatar-101")]
    [InlineData("not an icon")]
    [InlineData("")]
    [InlineData(null)]
    public void TheCacheLookupFindsNothingWithNoPictureOnDiskOrForATextThatIsNotAnIcon(string? text)
    {
        // An avatar sits in the same folder under its own prefix, and no icon text can ever name it.
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(Path.Combine(_dir, IconClient.AvatarPrefix + "101.png"), Png);

        Assert.Null(Client(new RouteHandler()).CachedFile(text, RecipeHostSet));
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

    /// <summary>
    /// The oversize test above uses <see cref="ByteArrayContent"/>, which always sets Content-Length,
    /// so it never reaches the streaming cap in the read loop. A chunked body with no Content-Length —
    /// this one never even ends — is the one that would slip past a Content-Length-only check.
    /// </summary>
    [Fact]
    public async Task AStreamedBodyWithNoContentLengthIsStillCappedWhileReading()
    {
        var stream = new CountingEndlessStream(Png);
        var handler = new RouteHandler()
            .On(ThumbnailsUrl, () => Json(Thumbnail("Completed", ImageUrl)))
            .On(ImageUrl, () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) });

        var file = await Client(handler).ResolveAsync($"rbxassetid://{AssetId}", RecipeHostSet, CancellationToken.None);

        Assert.Null(file);
        Assert.False(File.Exists(Path.Combine(_dir, $"{AssetId}.png")));
        Assert.False(File.Exists(Path.Combine(_dir, $"{AssetId}.png.partial")));
        Assert.InRange(stream.TotalRead, IconClient.MaxBytes, IconClient.MaxBytes + 200_000);
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

    private const string HeadshotsUrl =
        "https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds=101,201&size=48x48&format=Png&isCircular=false";

    private const string HeadshotUrl = "https://tr.rbxcdn.com/AVATAR-101/48/48/AvatarHeadshot/Png/noFilter";

    private static string Headshots(params (long UserId, string State, string Url)[] rows) =>
        "{ \"data\": [ " + string.Join(", ", rows.Select(r =>
            $"{{ \"targetId\": {r.UserId}, \"state\": \"{r.State}\", \"imageUrl\": \"{r.Url}\" }}")) + " ] }";

    [Fact]
    public async Task YourAccountsPicturesComeBackInOneRequestAndAreCachedByUserId()
    {
        var handler = new RouteHandler()
            .On(HeadshotsUrl, () => Json(Headshots((101, "Completed", HeadshotUrl), (201, "Pending", ""))))
            .On(HeadshotUrl, () => Bytes(Png));

        var files = await Client(handler).HeadshotsAsync(new long[] { 101, 201 }, CancellationToken.None);

        // A picture Roblox hasn't rendered yet is no icon this time, and costs nobody else theirs.
        Assert.Equal(new long[] { 101 }, files.Keys.ToArray());
        Assert.Equal(Path.Combine(_dir, "avatar-101.png"), files[101]);
        Assert.Equal(Png, File.ReadAllBytes(files[101]));
        Assert.Equal(new[] { HeadshotsUrl, HeadshotUrl }, handler.Requests.Select(r => r.RequestUri!.AbsoluteUri).ToArray());
        Assert.All(handler.Requests, r => Assert.Contains("UrScore", r.Headers.UserAgent.ToString()));
    }

    [Fact]
    public async Task ACachedPictureYoungerThanSevenDaysIsUsedWithoutAsking()
    {
        Directory.CreateDirectory(_dir);
        var cached = Path.Combine(_dir, "avatar-101.png");
        File.WriteAllBytes(cached, Png);
        File.SetLastWriteTimeUtc(cached, Now.AddDays(-6).UtcDateTime);
        var handler = new RouteHandler();

        var files = await Client(handler).HeadshotsAsync(new long[] { 101 }, CancellationToken.None);

        Assert.Equal(cached, files[101]);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task APictureOffRobloxsOwnDomainIsRefusedAndOnlyTheIdsAskedForAreFetched()
    {
        var handler = new RouteHandler()
            .On(HeadshotsUrl, () => Json(Headshots(
                (101, "Completed", "https://evil.example/headshot.png"),
                (201, "Completed", HeadshotUrl),
                (999, "Completed", HeadshotUrl))))
            .On("https://evil.example/headshot.png", () => Bytes(Png))
            .On(HeadshotUrl, () => Bytes(Png));

        var files = await Client(handler).HeadshotsAsync(new long[] { 101, 201 }, CancellationToken.None);

        Assert.Equal(new long[] { 201 }, files.Keys.ToArray());
        Assert.Equal(new[] { HeadshotsUrl, HeadshotUrl }, handler.Requests.Select(r => r.RequestUri!.AbsoluteUri).ToArray());
        Assert.False(File.Exists(Path.Combine(_dir, "avatar-999.png")));
        Assert.False(File.Exists(Path.Combine(_dir, "avatar-101.png")));
    }

    [Fact]
    public async Task APictureServiceThatAnswersWithNothingUsefulCostsOnlyThePictures()
    {
        var handler = new RouteHandler().On(HeadshotsUrl, () => Json("{ \"data\": \"not a list\" }"));

        Assert.Empty(await Client(handler).HeadshotsAsync(new long[] { 101, 201 }, CancellationToken.None));
        Assert.Single(handler.Requests);
    }
}
