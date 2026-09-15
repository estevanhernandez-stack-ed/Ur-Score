using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class RecipeStatsTests
{
    private static Recipe Profile => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

    private static Recipe Clan => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

    [Fact]
    public void AValueIsFoundByItsId() =>
        Assert.Equal(
            new RecipeStat("rank", "Player rank", "data.views.profile.data.Rank", "ps99.rank", Sum: false, Section: "Progression"),
            RecipeStats.Find(Profile, "rank"));

    [Fact]
    public void ACounterKeyReadsUnderTheCountersPath() =>
        Assert.Equal(
            new RecipeStat("counter:Huge Pets Opened", "Huge Pets Opened",
                "data.views.profile.data.Statistics.Huge Pets Opened", "ps99.stat.huge-pets-opened", Sum: true, Section: "Game statistics"),
            RecipeStats.Find(Profile, "counter:Huge Pets Opened"));

    [Fact]
    public void AKeyTheRecipeDoesNotOfferIsNoStat()
    {
        Assert.Null(RecipeStats.Find(Profile, "prestige"));
        Assert.Null(RecipeStats.Find(Clan, "counter:Huge Pets Opened"));
        Assert.Null(RecipeStats.Find(Profile, "counter:Pets.Huge"));
        Assert.Null(RecipeStats.Find(Profile, "counter:"));
    }

    [Fact]
    public void TheSuggestedValuesAreTheOnesMarkedShowInRecipeOrderAndNoneForAGroupList()
    {
        Assert.Equal(new[] { "diamonds", "eggs", "rank", "rebirths", "pets", "goals", "playtime" }, RecipeStats.Suggested(Profile).Select(v => v.Id).ToArray());
        Assert.Empty(RecipeStats.Suggested(Clan));

        // A group list is never shown per account, so even a value marked show suggests nothing, to the ticks and the note alike.
        var top = RecipeParser.Parse(BoardFixtures.TopListJson).Recipe!;
        var marked = top with { Steps = [top.LastStep with { Values = [.. top.LastStep.Values.Select(v => v with { Show = true })] }] };

        Assert.True(marked.IsGroupList);
        Assert.Empty(RecipeStats.Suggested(marked));
        Assert.Empty(Labs626.UrScore.UI.StatsTableModel.Suggested(marked));
        Assert.Equal("", Labs626.UrScore.UI.ImportText.SuggestedNote(marked));
    }

    [Theory]
    [InlineData("Huge Pets Opened", "huge-pets-opened")]
    [InlineData("Eggs (x2)", "eggs-x2")]
    [InlineData("Time_Played", "timeplayed")]
    [InlineData("!!!", "stat")]
    public void ASlugKeepsOnlyLowercaseLettersDigitsAndHyphens(string name, string slug) =>
        Assert.Equal(slug, RecipeStats.Slug(name));

    [Fact]
    public void OfferedListsValuesThenPickedCountersInKeyOrder()
    {
        var offered = RecipeStats.Offered(Profile, ["counter:Zones", "diamonds", "counter:Eggs Opened", "counter:Zones"]);

        string[] expected = [.. RecipeParserTests.ProfileIds, "counter:Eggs Opened", "counter:Zones"];
        Assert.Equal(expected, offered.Select(s => s.Key).ToArray());
    }

    [Fact]
    public void ASearchIsCaseInsensitiveShowsAtMostEightAndSkipsPickedNames()
    {
        string[] names = [.. Enumerable.Range(1, 12).Select(i => $"Pets Hatched {i}"), "Coins Spent"];

        var matches = RecipeStats.MatchCounterNames(names, " pets ", ["counter:Pets Hatched 1"]);

        Assert.Equal(new[] { "Pets Hatched 2", "Pets Hatched 3", "Pets Hatched 4", "Pets Hatched 5",
            "Pets Hatched 6", "Pets Hatched 7", "Pets Hatched 8", "Pets Hatched 9" }, matches.ToArray());
        Assert.Empty(RecipeStats.MatchCounterNames(names, "  ", []));
    }

    [Fact]
    public void NamesThatCannotBePickedNeverMatch()
    {
        string[] names = ["Pets.Huge", "123456", "Pets Hatched"];
        Assert.Equal(new[] { "Pets Hatched" }, RecipeStats.MatchCounterNames(names, "e", []).ToArray());
    }

    [Fact]
    public void ANameWithABraceCanNeverBePicked()
    {
        Assert.False(RecipeStats.CanPick("Pets {Rare}"));
        Assert.False(RecipeStats.CanPick("{userId}"));

        string[] names = ["Pets {Rare}", "Pets Hatched"];
        Assert.Equal(new[] { "Pets Hatched" }, RecipeStats.MatchCounterNames(names, "Pets", []).ToArray());

        Assert.Null(RecipeStats.Find(Profile, "counter:{battle}"));
    }
}
