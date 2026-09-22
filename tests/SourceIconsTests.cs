using System.Text.Json;
using System.Net.Http;
using Labs626.UrScore.Fetch;

namespace UrScore.Tests;

/// <summary>
/// Which picture belongs to which source. V3-S.7: a picture is kept per source, so one clan's read never replaces another's.
/// V3-S.6: each source's picture comes back from the cache at start, with no request. S1-14.1: a source that is gone, or whose
/// recipe no longer names an icon, loses its picture now and at the next start. Nothing here ever throws but a stop.
/// </summary>
public sealed class SourceIconsTests : IDisposable
{
    private const string Ccgp = "s-00000001";
    private const string K0i2 = "s-00000002";
    private const string CcgpIcon = "rbxassetid://14976358748";
    private const string K0i2Icon = "rbxassetid://7000000001";

    private static readonly IReadOnlySet<string> Hosts = new HashSet<string> { "ps99.example" };

    private readonly TempDir.Scope _dir = TempDir.Create("urscore-source-icons");

    public void Dispose() => _dir.Dispose();

    private string SavedFile => Path.Combine(_dir.Path, SourceIcons.FileName);

    /// <summary>The picture service, with no network: what a fetch answers, and what is already on disk.</summary>
    private sealed class FakePictures : IIconSource
    {
        public List<string> Asked { get; } = [];

        public Func<string, string?> Answer { get; set; } = FileOf;

        public Dictionary<string, string> OnDisk { get; } = new(StringComparer.Ordinal);

        public TaskCompletionSource<string?>? Hold { get; set; }

        public static string FileOf(string text) => $@"C:\cache\{text[(text.LastIndexOf('/') + 1)..]}.png";

        public Task<string?> ResolveAsync(string? iconText, IReadOnlySet<string> recipeHosts, CancellationToken cancellationToken)
        {
            Asked.Add(iconText!);
            cancellationToken.ThrowIfCancellationRequested();
            return Hold is { } hold ? hold.Task : Task.FromResult(Answer(iconText!));
        }

        public string? CachedFile(string? iconText, IReadOnlySet<string> recipeHosts) => OnDisk.GetValueOrDefault(iconText!);
    }

    private SourceIcons Icons(FakePictures pictures) => new(pictures, SavedFile);

    // ---- V3-S.7: per source ----

    [Fact]
    public async Task EachSourceKeepsItsOwnPictureWhicheverWasReadLast()
    {
        var icons = Icons(new FakePictures());

        Assert.True(await icons.ApplyAsync(Ccgp, CcgpIcon, Hosts, CancellationToken.None));
        Assert.True(await icons.ApplyAsync(K0i2, K0i2Icon, Hosts, CancellationToken.None));

        Assert.Equal(FakePictures.FileOf(CcgpIcon), icons.FileFor(Ccgp));
        Assert.Equal(FakePictures.FileOf(K0i2Icon), icons.FileFor(K0i2));
        Assert.Equal(2, icons.Files.Count);
    }

    [Fact]
    public async Task ASourceIsAskedAboutTheSameIconOnceASession()
    {
        var pictures = new FakePictures();
        var icons = Icons(pictures);

        Assert.True(await icons.ApplyAsync(Ccgp, CcgpIcon, Hosts, CancellationToken.None));
        Assert.False(await icons.ApplyAsync(Ccgp, CcgpIcon, Hosts, CancellationToken.None));

        // Another source with the same picture is its own question, and gets its own answer.
        Assert.True(await icons.ApplyAsync(K0i2, CcgpIcon, Hosts, CancellationToken.None));
        Assert.Equal(new[] { CcgpIcon, CcgpIcon }, pictures.Asked.ToArray());
    }

    [Fact]
    public async Task ANewerIconForTheSameSourceWinsOverOneStillBeingFetched()
    {
        var firstFetch = new TaskCompletionSource<string?>();
        var pictures = new FakePictures { Hold = firstFetch };
        var icons = Icons(pictures);

        var slow = icons.ApplyAsync(Ccgp, "rbxassetid://1", Hosts, CancellationToken.None);
        pictures.Hold = null;
        Assert.True(await icons.ApplyAsync(Ccgp, CcgpIcon, Hosts, CancellationToken.None));

        // The first fetch lands last. It is no longer this source's icon, so it replaces nothing.
        firstFetch.SetResult(@"C:\cache\1.png");
        Assert.False(await slow);
        Assert.Equal(FakePictures.FileOf(CcgpIcon), icons.FileFor(Ccgp));
    }

    [Fact]
    public async Task AnIconThatCannotBeFetchedCostsOnlyThatSourcesPicture()
    {
        var pictures = new FakePictures();
        var icons = Icons(pictures);
        await icons.ApplyAsync(Ccgp, CcgpIcon, Hosts, CancellationToken.None);

        pictures.Answer = _ => null;
        Assert.False(await icons.ApplyAsync(K0i2, K0i2Icon, Hosts, CancellationToken.None));

        Assert.Null(icons.FileFor(K0i2));
        Assert.Equal(FakePictures.FileOf(CcgpIcon), icons.FileFor(Ccgp));
    }

    /// <summary>
    /// A fetch that THROWS is asked again on the next read; a fetch that ANSWERS "no picture" is not. The two
    /// were treated alike, so a network blip on the first read of a session cost the source its picture until
    /// its icon text changed — which for a clan is never (S1-14.7). "No picture" is an answer and retrying it
    /// every read would be a burst for nothing; an exception is not an answer, and the text is still unknown.
    /// Same rule the cancellation branch already applied, now applied to the failure it was written next to.
    /// </summary>
    [Fact]
    public async Task AFetchThatThrowsIsAskedAgainNextReadWhereOneThatAnswersNoIsNot()
    {
        var pictures = new FakePictures();
        var icons = Icons(pictures);

        pictures.Answer = _ => throw new HttpRequestException("blip");
        Assert.False(await icons.ApplyAsync(Ccgp, CcgpIcon, Hosts, CancellationToken.None));
        pictures.Answer = FakePictures.FileOf;
        Assert.True(await icons.ApplyAsync(Ccgp, CcgpIcon, Hosts, CancellationToken.None));
        Assert.Equal(2, pictures.Asked.Count);
        Assert.Equal(FakePictures.FileOf(CcgpIcon), icons.FileFor(Ccgp));

        // The control: an answer of "no picture" is remembered, and the same text is not asked about again.
        pictures.Answer = _ => null;
        Assert.False(await icons.ApplyAsync(K0i2, K0i2Icon, Hosts, CancellationToken.None));
        Assert.False(await icons.ApplyAsync(K0i2, K0i2Icon, Hosts, CancellationToken.None));
        Assert.Equal(3, pictures.Asked.Count);
    }

    [Fact]
    public async Task AStopYouAskedForLeavesTheIconToBeAskedAgain()
    {
        var pictures = new FakePictures();
        var icons = Icons(pictures);
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => icons.ApplyAsync(Ccgp, CcgpIcon, Hosts, stop.Token));

        Assert.True(await icons.ApplyAsync(Ccgp, CcgpIcon, Hosts, CancellationToken.None));
        Assert.Equal(2, pictures.Asked.Count);
    }

    // ---- V3-S.6: back at start ----

    [Fact]
    public async Task EachSourcesPictureComesBackAtStartFromTheCacheWithNoRequest()
    {
        var before = Icons(new FakePictures());
        await before.ApplyAsync(Ccgp, CcgpIcon, Hosts, CancellationToken.None);
        await before.ApplyAsync(K0i2, K0i2Icon, Hosts, CancellationToken.None);

        var pictures = new FakePictures();
        pictures.OnDisk[CcgpIcon] = @"C:\cache\ccgp.png";
        pictures.OnDisk[K0i2Icon] = @"C:\cache\k0i2.png";
        var next = Icons(pictures);

        Assert.True(next.Restore(_ => Hosts));

        Assert.Equal(@"C:\cache\ccgp.png", next.FileFor(Ccgp));
        Assert.Equal(@"C:\cache\k0i2.png", next.FileFor(K0i2));
        Assert.Empty(pictures.Asked);
    }

    [Fact]
    public async Task NothingComesBackForASourceThatMayNoLongerHaveAnIconOrWhosePictureIsGone()
    {
        var before = Icons(new FakePictures());
        await before.ApplyAsync(Ccgp, CcgpIcon, Hosts, CancellationToken.None);
        await before.ApplyAsync(K0i2, K0i2Icon, Hosts, CancellationToken.None);

        // CCGP's picture was removed from the cache; K0i2 is gone from the sources (no hosts for it).
        var pictures = new FakePictures();
        pictures.OnDisk[K0i2Icon] = @"C:\cache\k0i2.png";
        var next = Icons(pictures);

        Assert.False(next.Restore(id => id == K0i2 ? null : Hosts));
        Assert.Empty(next.Files);
    }

    [Fact]
    public async Task ARestoredPictureStaysWhenTheSameIconCannotBeFetchedAgain()
    {
        await Icons(new FakePictures()).ApplyAsync(Ccgp, CcgpIcon, Hosts, CancellationToken.None);

        // Older than the cache keeps fresh, and the service is unreachable: it is still this clan's picture.
        var pictures = new FakePictures { Answer = _ => null };
        pictures.OnDisk[CcgpIcon] = @"C:\cache\ccgp.png";
        var icons = Icons(pictures);
        icons.Restore(_ => Hosts);

        Assert.False(await icons.ApplyAsync(Ccgp, CcgpIcon, Hosts, CancellationToken.None));
        Assert.Equal(@"C:\cache\ccgp.png", icons.FileFor(Ccgp));
    }

    [Fact]
    public async Task ANewIconThatCannotBeFetchedTakesTheOldPictureAwayNowAndAtTheNextStart()
    {
        await Icons(new FakePictures()).ApplyAsync(Ccgp, "rbxassetid://1", Hosts, CancellationToken.None);

        var pictures = new FakePictures { Answer = _ => null };
        pictures.OnDisk["rbxassetid://1"] = @"C:\cache\1.png";
        var icons = Icons(pictures);
        icons.Restore(_ => Hosts);

        Assert.True(await icons.ApplyAsync(Ccgp, CcgpIcon, Hosts, CancellationToken.None));
        Assert.Null(icons.FileFor(Ccgp));

        var next = Icons(pictures);
        Assert.False(next.Restore(_ => Hosts));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("{ \"s-00000001\": 5 }")]
    public void ASavedFileThatIsMissingOrBrokenRestoresNothingAndNeverThrows(string? text)
    {
        if (text is not null) File.WriteAllText(SavedFile, text);
        var pictures = new FakePictures();
        pictures.OnDisk[CcgpIcon] = @"C:\cache\ccgp.png";

        Assert.False(Icons(pictures).Restore(_ => Hosts));
    }

    // ---- S1-14.1: forgotten ----

    [Fact]
    public async Task ASourceThatIsGoneOrLostItsIconIsForgottenNowAndAtTheNextStart()
    {
        var pictures = new FakePictures();
        var icons = Icons(pictures);
        await icons.ApplyAsync(Ccgp, CcgpIcon, Hosts, CancellationToken.None);
        await icons.ApplyAsync(K0i2, K0i2Icon, Hosts, CancellationToken.None);

        Assert.True(icons.Keep(new HashSet<string> { Ccgp }));
        Assert.False(icons.Keep(new HashSet<string> { Ccgp }));
        Assert.Null(icons.FileFor(K0i2));

        pictures.OnDisk[CcgpIcon] = @"C:\cache\ccgp.png";
        pictures.OnDisk[K0i2Icon] = @"C:\cache\k0i2.png";
        var next = Icons(pictures);
        next.Restore(_ => Hosts);
        Assert.Equal(new[] { Ccgp }, next.Files.Keys.ToArray());

        // If it comes back, it is asked about again rather than remembered as already asked.
        Assert.True(await icons.ApplyAsync(K0i2, K0i2Icon, Hosts, CancellationToken.None));
        Assert.Equal(3, pictures.Asked.Count);
    }

    [Fact]
    public async Task AFetchThatLandsAfterItsSourceWasForgottenPutsNothingBack()
    {
        var pictures = new FakePictures { Hold = new TaskCompletionSource<string?>() };
        var icons = Icons(pictures);

        var slow = icons.ApplyAsync(K0i2, K0i2Icon, Hosts, CancellationToken.None);
        icons.Keep(new HashSet<string> { Ccgp });
        pictures.Hold.SetResult(FakePictures.FileOf(K0i2Icon));

        Assert.False(await slow);
        Assert.Empty(icons.Files);
    }

    // ---- what reaches the disk ----

    [Fact]
    public async Task TheSavedFileHoldsEachSourcesIdAndItsIconTextAndNothingElse()
    {
        var icons = Icons(new FakePictures());
        await icons.ApplyAsync(Ccgp, $"  {CcgpIcon} ", Hosts, CancellationToken.None);
        await icons.ApplyAsync(K0i2, K0i2Icon, Hosts, CancellationToken.None);

        var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SavedFile))!;

        Assert.Equal(new Dictionary<string, string> { [Ccgp] = CcgpIcon, [K0i2] = K0i2Icon }, saved);
    }
}
