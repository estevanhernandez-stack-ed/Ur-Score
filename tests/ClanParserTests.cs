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
}
