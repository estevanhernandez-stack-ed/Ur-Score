using Labs626.UrScore.Source;

namespace UrScore.Tests;

public class ClanParserTests
{
    // The shape verified live 2026-09-09. Wrapped in "data", which is this API's house style.
    private const string ActiveBattleJson = """
        { "status": "ok", "data": { "configName": "ArcadeBattle2026", "startTime": 1757000000 } }
        """;

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
                  { "UserID": 222, "Points": 3150.5 },
                  { "UserID": 333, "Points": 0 }
                ]
              }
            }
          }
        }
        """;

    [Fact]
    public void FindsTheActiveBattleConfigName()
    {
        var probe = ClanParser.ActiveBattle(ActiveBattleJson);
        Assert.Null(probe.Miss);
        Assert.Equal("ArcadeBattle2026", probe.ConfigName);
    }

    [Fact]
    public void FindsConfigNameAtTheRootToo()
    {
        // Not every endpoint on this API wraps in "data", and we are not betting the feature on
        // which ones do.
        var probe = ClanParser.ActiveBattle("""{ "configName": "ArcadeBattle2026" }""");
        Assert.Null(probe.Miss);
        Assert.Equal("ArcadeBattle2026", probe.ConfigName);
    }

    [Fact]
    public void NoBattleRunningIsNotAMiss()
    {
        // The common case, most of the time. It must be distinguishable from a shape we failed to
        // understand, because one is normal and the other needs someone to look.
        var probe = ClanParser.ActiveBattle("""{ "status": "ok", "data": null }""");
        Assert.Null(probe.Miss);
        Assert.Null(probe.ConfigName);
    }

    [Fact]
    public void ReadsEveryContribution()
    {
        var result = ClanParser.Contributions(ClanJson, "ArcadeBattle2026");
        Assert.Null(result.Miss);
        Assert.Equal(3, result.Contributions.Count);
        Assert.Equal(111, result.Contributions[0].UserId);
        Assert.Equal(4200, result.Contributions[0].Points);
        Assert.Equal(3150.5, result.Contributions[1].Points);
    }

    [Fact]
    public void ToleratesFieldCasing()
    {
        // A casing change alone must not take the feature down.
        var json = """
            { "data": { "battles": { "B": { "pointcontributions": [ { "userId": 7, "points": 12 } ] } } } }
            """;
        var result = ClanParser.Contributions(json, "B");
        Assert.Null(result.Miss);
        Assert.Equal(7, result.Contributions[0].UserId);
        Assert.Equal(12, result.Contributions[0].Points);
    }

    [Fact]
    public void AMissingBattlesKeyNamesTheKeysThatWereThere()
    {
        // The exact failure the vendor's own stale docs produce: their legacy README shows
        // Contribution.Battle[], and the live response has no Contribution key at all.
        var json = """{ "data": { "Name": "Noodle Clan", "Contribution": { "Battle": [] } } }""";
        var result = ClanParser.Contributions(json, "ArcadeBattle2026");

        Assert.Empty(result.Contributions);
        Assert.NotNull(result.Miss);
        Assert.Contains("Battles", result.Miss);
        Assert.Contains("Contribution", result.Miss);   // names what WAS there
        Assert.Contains("Name", result.Miss);
    }

    [Fact]
    public void AMissingBattleConfigListsTheBattlesPresent()
    {
        var json = """{ "data": { "Battles": { "SomeOtherBattle": {} } } }""";
        var result = ClanParser.Contributions(json, "ArcadeBattle2026");

        Assert.NotNull(result.Miss);
        Assert.Contains("ArcadeBattle2026", result.Miss);
        Assert.Contains("SomeOtherBattle", result.Miss);
    }

    [Fact]
    public void AMissingContributionsKeyListsThatBattlesKeys()
    {
        var json = """{ "data": { "Battles": { "B": { "Points": 5, "Place": 9 } } } }""";
        var result = ClanParser.Contributions(json, "B");

        Assert.NotNull(result.Miss);
        Assert.Contains("PointContributions", result.Miss);
        Assert.Contains("Points", result.Miss);
        Assert.Contains("Place", result.Miss);
    }

    [Fact]
    public void AnEmptyContributionListIsRealAndNotAMiss()
    {
        // A battle nobody has scored in yet. Distinct from every shape failure above, because the
        // user should be told "no contributions yet", not "we could not read the response".
        var json = """{ "data": { "Battles": { "B": { "PointContributions": [] } } } }""";
        var result = ClanParser.Contributions(json, "B");

        Assert.Null(result.Miss);
        Assert.Empty(result.Contributions);
    }

    [Fact]
    public void MalformedJsonIsAMissNotAnException()
    {
        var result = ClanParser.Contributions("{ not json", "B");
        Assert.NotNull(result.Miss);
        Assert.Empty(result.Contributions);
    }

    [Fact]
    public void RowsMissingEitherFieldAreSkippedWithoutLosingTheRest()
    {
        // One bad row costs only itself — the same rule the host's own rules-file parser follows.
        var json = """
            { "data": { "Battles": { "B": { "PointContributions": [
                { "UserID": 1, "Points": 10 },
                { "Points": 20 },
                { "UserID": 3 },
                { "UserID": 4, "Points": 40 }
            ] } } } }
            """;
        var result = ClanParser.Contributions(json, "B");

        Assert.Null(result.Miss);
        Assert.Equal(2, result.Contributions.Count);
        Assert.Equal(1, result.Contributions[0].UserId);
        Assert.Equal(4, result.Contributions[1].UserId);
    }

    [Fact]
    public void AllRowsRejectedIsAMissNotACleanEmptyList()
    {
        // Schema drift where every row fails its own check (here: Points becomes a nested object)
        // must not read as "a battle nobody has scored in" — that is the one outcome this parser
        // must never produce. Mixing rows that fail for different reasons is deliberate: the old
        // code returned a clean empty list regardless of why every row failed.
        var json = """
            { "data": { "Battles": { "B": { "PointContributions": [
                { "UserID": 1, "Points": { "amount": 10 } },
                { "UserID": 2, "Points": { "amount": 20 } }
            ] } } } }
            """;
        var result = ClanParser.Contributions(json, "B");

        Assert.Empty(result.Contributions);
        Assert.NotNull(result.Miss);
        // Both keys ARE present here — the failure is that Points holds a nested object, not a
        // number. Naming "keys present: UserID, Points" would send a user hunting for a missing
        // key that isn't missing; the message must instead say which field's VALUE was wrong.
        Assert.Contains("'Points' was present but not a finite number", result.Miss);
    }

    [Fact]
    public void AUserIdBeyondDoublePrecisionIsRejectedNotFabricated()
    {
        // A double cannot hold 1e20 exactly; casting it to long silently yields long.MaxValue.
        // That is a fabricated id, not the id that was sent, and the row must be dropped rather
        // than reported against the wrong account (or none).
        var json = """
            { "data": { "Battles": { "B": { "PointContributions": [
                { "UserID": 1e20, "Points": 5 }
            ] } } } }
            """;
        var result = ClanParser.Contributions(json, "B");

        Assert.NotNull(result.Miss);
        Assert.Empty(result.Contributions);
        // UserID is present — the failure is that its value is not a whole number a user id can
        // be. The message must name that, not just list the keys (which would mislead: they are
        // both there).
        Assert.Contains("'UserID' was present but not a whole number", result.Miss);
    }

    [Fact]
    public void AMissingUserIdOnTheOnlyRowNamesTheKeysThatWereThere()
    {
        // The genuine "a key is absent" case, which must not regress now that value-shaped
        // failures get their own wording: when UserID itself is missing, naming the keys that
        // WERE on the row is still the right answer.
        var json = """
            { "data": { "Battles": { "B": { "PointContributions": [
                { "Points": 20 }
            ] } } } }
            """;
        var result = ClanParser.Contributions(json, "B");

        Assert.Empty(result.Contributions);
        Assert.NotNull(result.Miss);
        Assert.Contains("no 'UserID'", result.Miss);
        Assert.Contains("Points", result.Miss);
    }

    [Fact]
    public void AnObjectWrapperWinsOverANullSiblingWrapper()
    {
        // Returning on the first wrapper that is EITHER an object or null would let a response
        // carrying both "data": null and a populated "result" report "no battle running" while
        // the real payload sat unread one key over.
        var probe = ClanParser.ActiveBattle("""{ "data": null, "result": { "configName": "X" } }""");

        Assert.Null(probe.Miss);
        Assert.Equal("X", probe.ConfigName);
    }

    [Fact]
    public void ToleratesCasingOnTheBattleKeyToo()
    {
        // ToleratesFieldCasing varies every key except the one used to look the battle up. A
        // regression that made only the battle-name lookup case-sensitive would slip past it.
        var json = """
            { "data": { "Battles": { "arcadebattle2026": { "PointContributions": [
                { "UserID": 1, "Points": 10 }
            ] } } } }
            """;
        var result = ClanParser.Contributions(json, "ArcadeBattle2026");

        Assert.Null(result.Miss);
        Assert.Equal(1, result.Contributions[0].UserId);
    }
}
