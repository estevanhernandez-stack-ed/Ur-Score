using Labs626.UrScore.Fetch;

namespace UrScore.Tests;

/// <summary>
/// The pictures beside your own accounts. These pin the three promises: an id is asked for once a session, only the ids
/// an ask asked for are kept, and nothing here ever throws — a failure costs the pictures and nothing else.
/// </summary>
public class AvatarBookTests
{
    private sealed class FakeAvatars : IAvatarSource
    {
        public List<long[]> Asks { get; } = [];

        public Func<IReadOnlyCollection<long>, IReadOnlyDictionary<long, string>> Answer { get; set; } =
            ids => ids.ToDictionary(id => id, id => $@"C:\cache\avatar-{id}.png");

        public Exception? Throws { get; set; }

        public Task<IReadOnlyDictionary<long, string>> HeadshotsAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken)
        {
            Asks.Add([.. userIds]);
            cancellationToken.ThrowIfCancellationRequested();
            return Throws is { } ex
                ? Task.FromException<IReadOnlyDictionary<long, string>>(ex)
                : Task.FromResult(Answer(userIds));
        }
    }

    private static string[] Asked(FakeAvatars source) => [.. source.Asks.Select(a => string.Join(",", a))];

    [Fact]
    public async Task EachOfYourAccountsIsAskedForOnceASession()
    {
        var source = new FakeAvatars();
        var book = new AvatarBook(source);

        Assert.True(await book.AskAsync(new long[] { 101, 201, 101 }, CancellationToken.None));
        Assert.False(await book.AskAsync(new long[] { 101, 201 }, CancellationToken.None));
        Assert.True(await book.AskAsync(new long[] { 101, 202 }, CancellationToken.None));

        Assert.Equal(new[] { "101,201", "202" }, Asked(source));
        Assert.Equal(@"C:\cache\avatar-202.png", book.FileFor(202));
        Assert.Null(book.FileFor(999));
    }

    [Fact]
    public async Task AnIdWithNoRobloxUserIsNeverAskedAbout()
    {
        var source = new FakeAvatars();

        Assert.False(await new AvatarBook(source).AskAsync(new long[] { 0 }, CancellationToken.None));
        Assert.Empty(source.Asks);
    }

    [Fact]
    public async Task OnlyTheIdsAnAskAskedForAreKept()
    {
        // Roblox only ever answers for the ids sent, so this is inert against the real service. Keeping "asked for" and
        // "known" the same set is the point: a picture for anyone else must have nowhere to land.
        var source = new FakeAvatars
        {
            Answer = _ => new Dictionary<long, string> { [101] = "mine.png", [999] = "someone-else.png" },
        };
        var book = new AvatarBook(source);

        Assert.True(await book.AskAsync(new long[] { 101 }, CancellationToken.None));

        Assert.Equal(new long[] { 101 }, book.Files.Keys.Order().ToArray());
    }

    [Fact]
    public async Task APictureThatCouldNotBeFetchedCostsNothingElseAndIsNotRetriedInALoop()
    {
        var source = new FakeAvatars { Throws = new InvalidOperationException("no network") };
        var book = new AvatarBook(source);

        Assert.False(await book.AskAsync(new long[] { 101 }, CancellationToken.None));
        Assert.Empty(book.Files);

        source.Throws = null;
        Assert.False(await book.AskAsync(new long[] { 101 }, CancellationToken.None));
        Assert.Single(source.Asks);
    }

    [Fact]
    public async Task AStopYouAskedForLeavesThoseIdsToBeAskedAgain()
    {
        var source = new FakeAvatars();
        var book = new AvatarBook(source);
        using var stopped = new CancellationTokenSource();
        await stopped.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => book.AskAsync(new long[] { 101 }, stopped.Token));

        Assert.True(await book.AskAsync(new long[] { 101 }, CancellationToken.None));
        Assert.Equal(new[] { "101", "101" }, Asked(source));
    }

    [Fact]
    public async Task AnAccountThatIsNoLongerYoursIsForgotten()
    {
        var source = new FakeAvatars();
        var book = new AvatarBook(source);
        await book.AskAsync(new long[] { 101, 201 }, CancellationToken.None);

        book.Keep(new HashSet<long> { 101 });

        Assert.Equal(new long[] { 101 }, book.Files.Keys.Order().ToArray());
        Assert.True(await book.AskAsync(new long[] { 101, 201 }, CancellationToken.None));
        Assert.Equal(new[] { "101,201", "201" }, Asked(source));
    }
}
