using Labs626.UrScore.Board;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class RecipeWordsTests
{
    private static Recipe Clan => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

    private static Recipe Profile => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

    [Fact]
    public void AClanRecipeSpeaksOfClansAndBattles()
    {
        Assert.Equal("clan", RecipeWords.Group(Clan));
        Assert.Equal("Clans", RecipeWords.Groups(Clan));
        Assert.Equal("clans", RecipeWords.GroupsLower(Clan));
        Assert.Equal("battle", RecipeWords.Period(Clan));
        Assert.Equal("battles", RecipeWords.Periods(Clan));
        Assert.Equal("clan", RecipeWords.MainInput(Clan)!.Id);
    }

    [Fact]
    public void ARecipeWithNoInputAndNoPeriodUsesPlainWords()
    {
        Assert.Null(RecipeWords.MainInput(Profile));
        Assert.Equal("source", RecipeWords.Group(Profile));
        Assert.Equal("Sources", RecipeWords.Groups(Profile));
        Assert.Equal("period", RecipeWords.Period(Profile));
    }

    [Theory]
    [InlineData("battle", "battle")]
    [InlineData("seasonName", "season name")]
    [InlineData("battle_id", "battle id")]
    [InlineData("  ", "period")]
    public void ATakeNameReadsAsWords(string take, string words) => Assert.Equal(words, RecipeWords.Spaced(take));

    [Theory]
    [InlineData("Clan", "clan")]
    [InlineData("NFT", "NFT")]
    [InlineData("", "")]
    public void LowerLeavesAcronymsAlone(string text, string lower) => Assert.Equal(lower, RecipeWords.Lower(text));

    [Fact]
    public void CapitalRaisesTheFirstLetterOnly() => Assert.Equal("Battle race", RecipeWords.Capital("battle race"));
}
