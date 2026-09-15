using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

public class ImportTextTests
{
    // No "headline": the real parser refuses one on a group-list last step ("A headline can only be
    // read from a list-form last step."), which the brief's scratch stand-in did not enforce.
    private const string GroupList = """
        {
          "recipe": 1, "name": "Top groups", "credit": "Test data.", "metricId": "test.points", "valueLabel": "Points",
          "everySeconds": 180,
          "steps": [ { "url": "https://example.test/top", "rows": "data.top", "groupName": "name", "value": "points", "rank": "rank" } ]
        }
        """;

    [Fact]
    public void EveryHeadlineItemIsListedAsKept()
    {
        var clan = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

        Assert.Equal(new[] { "Clan place", "Clan points" }, ImportText.Kept(clan).ToArray());
        Assert.Equal("Every read keeps these headline items, and the stats you tick for your own accounts only.", ImportText.KeptNote(clan));
    }

    [Fact]
    public void ARecipeWithNoHeadlineKeepsOnlyTheTickedStats()
    {
        var profile = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

        Assert.Empty(ImportText.Kept(profile));
        Assert.Equal("Every read keeps the stats you tick, for your own accounts only.", ImportText.KeptNote(profile));
    }

    [Fact]
    public void ARecipeWithInputsSaysWhereToPickThem()
    {
        var clan = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;
        var profile = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

        Assert.Equal("After you import it, pick your clan in Setup › Clans.", ImportText.InputsNote(clan));
        Assert.Equal("", ImportText.InputsNote(profile));
    }

    [Fact]
    public void AGroupListKeepsNothing()
    {
        var parsed = RecipeParser.Parse(GroupList);
        Assert.True(parsed.Ok, string.Join(" ", parsed.Problems));

        Assert.Empty(ImportText.Kept(parsed.Recipe!));
        Assert.Equal("Nothing from this recipe is kept. Its rows are groups, shown live only.", ImportText.KeptNote(parsed.Recipe!));
    }
}
