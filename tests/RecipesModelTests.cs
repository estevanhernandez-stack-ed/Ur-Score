using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

public class RecipesModelTests
{
    private const string GroupList = """
        {
          "recipe": 1, "name": "Top groups", "credit": "Test data.", "metricId": "test.points", "valueLabel": "Points",
          "everySeconds": 180,
          "steps": [ { "url": "https://example.test/top", "rows": "data.top", "groupName": "name", "value": "points", "rank": "rank" } ]
        }
        """;

    private static Recipe Clan => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

    private static Recipe Profile => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

    private static Source ClanSource(string id, string clan) =>
        new(id, Clan.Slug, new Dictionary<string, string> { ["clan"] = clan }, SourceRole.Mine);

    [Fact]
    public void AnItemNamesHostsIntervalSourcesAndIcon()
    {
        var items = RecipesModel.Items(
            [new InstalledRecipe(Clan, "", new RecipeState()), new InstalledRecipe(Profile, "", new RecipeState())],
            [ClanSource("s-00000001", "CCGP"), ClanSource("s-00000002", "K0i2")],
            slug => slug == Clan.Slug ? @"C:\cache\icon.png" : null);

        Assert.Equal("Pet Sim 99 clan battle points", items[0].Name);
        Assert.Equal("ps99.biggamesapi.io", items[0].Hosts);
        Assert.Equal($"Asks every {Clan.EffectiveEverySeconds} s", items[0].Every);
        Assert.Equal("2 clans", items[0].Sources);
        Assert.True(items[0].HasIcon);
        Assert.Equal("Remove Pet Sim 99 clan battle points", items[0].RemoveName);
        Assert.Equal("", items[1].Sources);
        Assert.False(items[1].HasIcon);
    }

    [Fact]
    public void SourcesTextCountsInTheRecipesWords()
    {
        Assert.Equal("No clans yet", RecipesModel.SourcesText(Clan, []));
        Assert.Equal("1 clan", RecipesModel.SourcesText(Clan, [ClanSource("s-00000001", "CCGP")]));
        Assert.Equal("Shown live, never kept", RecipesModel.SourcesText(RecipeParser.Parse(GroupList).Recipe!, []));
    }

    [Fact]
    public void RemovingSaysTheBookStays() =>
        Assert.Equal(
            "Remove Pet Sim 99 profile? It stops being read. Its score book stays on this PC, so importing it again carries on where it left off.",
            RecipesModel.ConfirmRemove(Profile));
}
