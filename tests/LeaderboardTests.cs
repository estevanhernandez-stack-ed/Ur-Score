using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class LeaderboardTests
{
    private static RecipeRow Row(long userId, double value) => RecipeEngineTests.Row(userId, value);

    [Fact]
    public void RanksByValueHighestFirst()
    {
        var ranked = Leaderboard.Rank([Row(111, 10), Row(222, 500), Row(333, 40)], new HashSet<long>(), "value");
        Assert.Equal(new long[] { 222, 333, 111 }, ranked.Select(r => r.UserId).ToArray());
        Assert.Equal(new[] { 1, 2, 3 }, ranked.Select(r => r.Position).ToArray());
    }

    [Fact]
    public void MarksTheUsersOwnAccounts()
    {
        var ranked = Leaderboard.Rank([Row(111, 10), Row(222, 500)], new HashSet<long> { 111 }, "value");
        Assert.True(ranked.Single(r => r.UserId == 111).IsMine);
        Assert.False(ranked.Single(r => r.UserId == 222).IsMine);
    }

    [Fact]
    public void TiedValuesGetDistinctPositionsInAStableOrder()
    {
        // Sharing a position reads as a missing row, and an unstable sort reshuffles every poll.
        var ranked = Leaderboard.Rank([Row(333, 500), Row(111, 500), Row(222, 500)], new HashSet<long>(), "value");
        Assert.Equal(new long[] { 111, 222, 333 }, ranked.Select(r => r.UserId).ToArray());
        Assert.Equal(new[] { 1, 2, 3 }, ranked.Select(r => r.Position).ToArray());
    }

    [Fact]
    public void NothingRanksToNothing() => Assert.Empty(Leaderboard.Rank([], new HashSet<long>(), "value"));

    [Fact]
    public void RanksByTheChosenStatAndPutsRowsWithoutItLast()
    {
        // A missing number is not a zero: a row with a negative level still ranks above no level.
        RecipeRow[] rows =
        [
            new(111, new Dictionary<string, double> { ["points"] = 900 }),
            new(222, new Dictionary<string, double> { ["points"] = 5, ["level"] = -1 }),
            new(333, new Dictionary<string, double> { ["points"] = 50, ["level"] = 3 }),
        ];

        var ranked = Leaderboard.Rank(rows, new HashSet<long>(), "level");

        Assert.Equal(new long[] { 333, 222, 111 }, ranked.Select(r => r.UserId).ToArray());
        Assert.Equal(900, ranked[2].Values["points"]);
    }
}
