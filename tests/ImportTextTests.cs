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
    public void HeadlineItemsAndPeriodDetailsAreListedAsKept()
    {
        var clan = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

        Assert.Equal(new[]
        {
            "Clan place", "Clan points", "Which battle each read belongs to", "When the battle starts and ends",
        }, ImportText.Kept(clan).ToArray());
        Assert.Equal("Every read keeps these details, and the stats you tick for your own accounts only.", ImportText.KeptNote(clan));
    }

    [Fact]
    public void AProfileWithoutHeadlinesStillDisclosesItsFreshness()
    {
        var profile = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

        Assert.Equal(new[] { "When the source last updated the numbers, and whether it calls them stale" }, ImportText.Kept(profile));
        Assert.Equal("Every read keeps these details, and the stats you tick for your own accounts only.", ImportText.KeptNote(profile));
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("starts", null, "When the season starts")]
    [InlineData(null, "ends", "When the season ends")]
    [InlineData("starts", "ends", "When the season starts and ends")]
    public void PeriodDisclosureUsesTheRecipesWordAndOnlyItsDeclaredDates(string? starts, string? ends, string? dates)
    {
        var parsed = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;
        var recipe = parsed with { Headline = [], Period = new RecipePeriod("season", starts, ends, null) };

        var expected = new List<string> { "Which season each read belongs to" };
        if (dates is not null) expected.Add(dates);

        Assert.Equal(expected, ImportText.Kept(recipe));
        Assert.Equal("Every read keeps these details, and the stats you tick for your own accounts only.", ImportText.KeptNote(recipe));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FreshnessIsDisclosedForListAndPerAccountRecipes(bool perAccount, bool stale)
    {
        var parsed = RecipeParser.Parse(RecipeParserTests.Fixture(perAccount
            ? "petsim99-profile.recipe.json" : "petsim99-clan-battle.recipe.json")).Recipe!;
        var recipe = parsed with
        {
            Headline = [],
            Period = null,
            Steps = [.. parsed.Steps.Take(parsed.Steps.Count - 1), parsed.LastStep with
            {
                AsOf = new RecipeAsOf("data.updated", stale ? "data.stale" : null),
            }],
        };

        Assert.Equal(new[] { stale
            ? "When the source last updated the numbers, and whether it calls them stale"
            : "When the source last updated the numbers" }, ImportText.Kept(recipe));
        Assert.Equal("Every read keeps these details, and the stats you tick for your own accounts only.", ImportText.KeptNote(recipe));
    }

    [Fact]
    public void ARecipeWithoutExtraDetailsListsNone()
    {
        var parsed = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;
        var recipe = parsed with { Steps = [parsed.LastStep with { AsOf = null }] };

        Assert.Empty(ImportText.Kept(recipe));
        Assert.Equal("Every read keeps the stats you tick, for your own accounts only.", ImportText.KeptNote(recipe));
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
    public void TheImportScreenSaysWhichStatsStartTickedAndThatNothingIsSent()
    {
        var profile = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;
        var clan = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

        Assert.Equal(
            "The recipe suggests showing Diamonds, Eggs hatched, Player rank, Rebirths, Different pets hatched, Goals completed and Playtime, "
            + "so they start ticked. Untick any you don't want. Nothing is sent to RoRoRo unless you tick Send.",
            ImportText.SuggestedNote(profile));
        Assert.Equal("", ImportText.SuggestedNote(clan));
    }

    [Fact]
    public void AGroupListKeepsNothing()
    {
        var parsed = RecipeParser.Parse(GroupList);
        Assert.True(parsed.Ok, string.Join(" ", parsed.Problems));

        Assert.Empty(ImportText.Kept(parsed.Recipe!));
        Assert.Equal("Nothing from this recipe is kept. Its rows are groups, shown live only.", ImportText.KeptNote(parsed.Recipe!));

        var withDetails = parsed.Recipe! with
        {
            Period = new RecipePeriod("season", "starts", "ends", "data.history"),
            Steps = [parsed.Recipe!.LastStep with { AsOf = new RecipeAsOf("data.updated", "data.stale") }],
        };
        Assert.Empty(ImportText.Kept(withDetails));
        Assert.Equal("Nothing from this recipe is kept. Its rows are groups, shown live only.", ImportText.KeptNote(withDetails));
    }
}
