using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class LeaderboardTests
{
    [Fact]
    public void RanksByValueHighestFirst()
    {
        var ranked = Leaderboard.Rank([new(111, 10), new(222, 500), new(333, 40)], new HashSet<long>());
        Assert.Equal(new long[] { 222, 333, 111 }, ranked.Select(r => r.UserId).ToArray());
        Assert.Equal(new[] { 1, 2, 3 }, ranked.Select(r => r.Position).ToArray());
    }

    [Fact]
    public void MarksTheUsersOwnAccounts()
    {
        var ranked = Leaderboard.Rank([new(111, 10), new(222, 500)], new HashSet<long> { 111 });
        Assert.True(ranked.Single(r => r.UserId == 111).IsMine);
        Assert.False(ranked.Single(r => r.UserId == 222).IsMine);
    }

    [Fact]
    public void TiedValuesGetDistinctPositionsInAStableOrder()
    {
        // Sharing a position reads as a missing row, and an unstable sort reshuffles every poll.
        var ranked = Leaderboard.Rank([new(333, 500), new(111, 500), new(222, 500)], new HashSet<long>());
        Assert.Equal(new long[] { 111, 222, 333 }, ranked.Select(r => r.UserId).ToArray());
        Assert.Equal(new[] { 1, 2, 3 }, ranked.Select(r => r.Position).ToArray());
    }

    [Fact]
    public void NothingRanksToNothing() => Assert.Empty(Leaderboard.Rank([], new HashSet<long>()));
}
