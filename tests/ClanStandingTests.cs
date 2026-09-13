using Labs626.UrScore.Source;

namespace UrScore.Tests;

public class ClanStandingTests
{
    private const string ClanJson = """
        {
          "status": "ok",
          "data": {
            "Name": "Noodle Clan",
            "Battles": {
              "ArcadeBattle2026": {
                "Points": 91234,
                "Place": 1187,
                "PointContributions": [
                  { "UserID": 111, "Points": 4200 },
                  { "UserID": 222, "Points": 3150.5 }
                ]
              }
            }
          }
        }
        """;

    [Fact]
    public void ReadsPlaceAndPoints()
    {
        // The two keys this plugin used to throw away. Place is the clan's standing out of some
        // 169,161 clans, and it was previously only ever seen as filler in an error-message test.
        var standing = ClanStanding.Read(ClanJson, "ArcadeBattle2026");

        Assert.Equal(1187, standing.Place);
        Assert.Equal(91234, standing.Points);
    }

    [Fact]
    public void AMissingPlaceIsNullRatherThanZero()
    {
        // Zero would render as "1st place", which is a lie rather than an absence. A battle can be
        // live before the standing is populated.
        var json = """{ "data": { "Battles": { "B": { "Points": 5 } } } }""";
        var standing = ClanStanding.Read(json, "B");

        Assert.Null(standing.Place);
        Assert.Equal(5, standing.Points);
    }

    [Theory]
    // A double cannot hold 1e20 exactly; a bare cast to long silently yields long.MaxValue — a
    // fabricated rank, not the rank that was sent. JsonNav.TryUserId exists one file over for
    // exactly this failure mode.
    [InlineData("1e20")]
    // There is no negative place, and a bare cast would happily produce one.
    [InlineData("-5")]
    // Not a whole number. A bare cast truncates 1187.9 to 1187 silently instead of rejecting it.
    [InlineData("1187.9")]
    public void APlaceThatIsNotAValidRankIsNullRatherThanFabricated(string placeLiteral)
    {
        var standing = ClanStanding.Read(PlaceJson(placeLiteral), "B");

        Assert.Null(standing.Place);
    }

    private static string PlaceJson(string placeLiteral) =>
        $$"""
        { "data": { "Battles": { "B": { "Place": {{placeLiteral}} } } } }
        """;

    [Fact]
    public void AnUnreadableResponseIsAllNullsAndNotAnException()
    {
        // This runs alongside the parser, which already reports shape failures properly. Standing
        // is decoration on top of that, so it goes quiet rather than competing to complain.
        var malformed = ClanStanding.Read("{ not json", "B");
        Assert.Null(malformed.Place);
        Assert.Null(malformed.Points);

        var noBattles = ClanStanding.Read("""{"data":{}}""", "B");
        Assert.Null(noBattles.Place);
        Assert.Null(noBattles.Points);
    }

    [Fact]
    public void RanksByPointsDescending()
    {
        var ranked = ClanStanding.Rank(
            [new(111, 100), new(222, 900), new(333, 500)], mine: new HashSet<long>());

        Assert.Equal([222L, 333L, 111L], ranked.Select(r => r.UserId));
        Assert.Equal([1, 2, 3], ranked.Select(r => r.Position));
    }

    [Fact]
    public void MarksTheUsersOwnAccounts()
    {
        var ranked = ClanStanding.Rank(
            [new(111, 100), new(222, 900)], mine: new HashSet<long> { 111 });

        Assert.False(ranked[0].IsMine);          // 222, the higher score
        Assert.True(ranked[1].IsMine);           // 111, the user's
    }

    [Fact]
    public void TiedScoresGetDistinctPositionsAndAStableOrder()
    {
        // Two members on the same points is ordinary. Sharing a position number would make the
        // list read as though a row were missing, and an unstable sort would make the leaderboard
        // reshuffle every three minutes for no reason the user could see.
        var first = ClanStanding.Rank([new(111, 500), new(222, 500), new(333, 500)], new HashSet<long>());
        var again = ClanStanding.Rank([new(111, 500), new(222, 500), new(333, 500)], new HashSet<long>());

        Assert.Equal([1, 2, 3], first.Select(r => r.Position));
        Assert.Equal(first.Select(r => r.UserId), again.Select(r => r.UserId));
    }

    [Fact]
    public void TiedScoresOrderTheSameRegardlessOfHowTheyArrived()
    {
        // The input order is what must vary. Calling Rank twice with the SAME list proves only
        // that a pure function is pure — the earlier version of this test did exactly that and
        // stayed green with the tie-break deleted. These contributions arrive from a remote JSON
        // array whose ordering nobody guarantees, so arriving reversed is the real case.
        var forwards = ClanStanding.Rank(
            [new(111, 500), new(222, 500), new(333, 500)], new HashSet<long>());
        var backwards = ClanStanding.Rank(
            [new(333, 500), new(222, 500), new(111, 500)], new HashSet<long>());

        Assert.Equal(forwards.Select(r => r.UserId), backwards.Select(r => r.UserId));
        Assert.Equal([1, 2, 3], forwards.Select(r => r.Position));
    }

    [Fact]
    public void AnEmptyContributionListRanksToNothing()
    {
        Assert.Empty(ClanStanding.Rank([], new HashSet<long>()));
    }
}
