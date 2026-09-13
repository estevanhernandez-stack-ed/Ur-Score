using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class HistoryBudgetTests
{
    private static readonly Guid One = Guid.Parse("9ad5e605-6b41-478c-add3-b916a31a5ab2");
    private static readonly Guid Two = Guid.Parse("88dc7685-3a36-4f93-b526-a9bff2d7da6c");

    [Fact]
    public void TheCountIsSendingAccountsTimesSentStatsSummedAcrossRecipes() =>
        Assert.Equal(5 * 3 + 5 * 1 + 0 * 4, HistoryBudget.Count([(5, 3), (5, 1), (0, 4)]));

    [Fact]
    public void AnInstalledRecipesShareUsesItsOwnSendTicks()
    {
        var profileText = RecipeParserTests.Fixture("petsim99-profile.recipe.json");
        var profile = new InstalledRecipe(RecipeParser.Parse(profileText).Recipe!, profileText, new RecipeState(
            ExcludedAccountIds: [Two.ToString()],
            Stats: new Dictionary<string, StatChoice>
            {
                ["diamonds"] = new(Send: true, MetricId: "ps99.diamonds"),
                ["rank"] = new(Send: true, MetricId: "ps99.rank"),
                ["eggs"] = new(Show: true, MetricId: "ps99.eggs-hatched"),
            }));
        var clanText = RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json");
        var clan = new InstalledRecipe(RecipeParser.Parse(clanText).Recipe!, clanText, new RecipeState());

        Assert.Equal(new[] { (1, 2), (2, 0) }, HistoryBudget.Installed([profile, clan], [One, Two]).ToArray());
        Assert.Equal(new[] { (2, 0) }, HistoryBudget.Installed([profile, clan], [One, Two], exceptSlug: profile.Recipe.Slug).ToArray());
    }

    [Fact]
    public void UnderTheWarningTheLineStatesTheCountAndItsBasis()
    {
        var check = HistoryBudget.Check([(5, 3)], before: (5, 4), after: (5, 5), accountsKnown: true);

        Assert.True(check.Allowed);
        Assert.Equal(40, check.Count);
        Assert.Equal("40 of RoRoRo's 256 history slots: accounts with Send on times stats with Send on, across your installed recipes.", check.Line);
    }

    [Fact]
    public void FromTwoHundredTheLineWarnsThatOtherPluginsShareTheSlots()
    {
        var check = HistoryBudget.Check([(10, 15)], before: (10, 4), after: (10, 5), accountsKnown: true);

        Assert.True(check.Allowed);
        Assert.Equal(200, check.Count);
        Assert.EndsWith(" Other plugins share these slots, so leave room for them.", check.Line);
    }

    [Fact]
    public void ExactlyTheLimitIsAllowed() =>
        Assert.True(HistoryBudget.Check([(8, 16)], before: (8, 15), after: (8, 16), accountsKnown: true).Allowed);

    [Fact]
    public void AChangePastTheLimitIsRefusedAndSaysWhy()
    {
        var check = HistoryBudget.Check([(8, 16)], before: (8, 16), after: (8, 17), accountsKnown: true);

        Assert.False(check.Allowed);
        Assert.Equal(264, check.Count);
        Assert.StartsWith("Not allowed: that would use 264 of RoRoRo's 256 history slots.", check.Line);
    }

    [Fact]
    public void LoweringACountThatIsAlreadyTooHighIsAllowed() =>
        Assert.True(HistoryBudget.Check([(10, 20)], before: (10, 8), after: (10, 7), accountsKnown: true).Allowed);

    [Fact]
    public void WithNoAccountsKnownYetTheLineSaysSo()
    {
        var check = HistoryBudget.Check([], before: (0, 0), after: (0, 3), accountsKnown: false);

        Assert.True(check.Allowed);
        Assert.Equal("0 of RoRoRo's 256 history slots so far. RoRoRo hasn't been reached yet, so your accounts count as 0 until it is.", check.Line);
    }
}
