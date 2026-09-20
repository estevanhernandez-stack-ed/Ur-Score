using Labs626.UrScore.Book;
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

    /// <summary>
    /// A clans list says what it keeps. It used to say "nothing from this recipe is kept", which stopped being
    /// true in 0.3.10 and stayed on the consent screen until 0.5.3 — the one screen whose job is telling you what
    /// you are agreeing to. The owner's direction, 2026-09-20: we are holding it, so say so.
    /// </summary>
    [Fact]
    public void AGroupListSaysWhatItKeeps()
    {
        var parsed = RecipeParser.Parse(GroupList);
        Assert.True(parsed.Ok, string.Join(" ", parsed.Problems));

        var kept = ImportText.Kept(parsed.Recipe!);
        Assert.Equal(
            [
                "Where yours stands in the list, and what the place above it holds",
                "How the whole field is doing: the leader, the top ten, the average and the bottom ten",
                $"The top {GroupRows.Top} by name, with the places either side of yours",
            ],
            kept);

        var note = ImportText.KeptNote(parsed.Recipe!);
        Assert.Equal("Every read keeps these. A list like this holds no players, so no player is kept.", note);
        Assert.DoesNotContain("Nothing from this recipe is kept", note, StringComparison.Ordinal);

        // A clans list's line carries its period and the source's own stamp too, so the screen names them.
        var withDetails = parsed.Recipe! with
        {
            Period = new RecipePeriod("season", "starts", "ends", "data.history"),
            Steps = [parsed.Recipe!.LastStep with { AsOf = new RecipeAsOf("data.updated", "data.stale") }],
        };
        var more = ImportText.Kept(withDetails);
        Assert.Contains("Which season each read belongs to", more);
        Assert.Contains("When the season starts and ends", more);
        Assert.Contains("When the source last updated the numbers, and whether it calls them stale", more);
    }
}
