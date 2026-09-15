using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

public class SetupPagesTests
{
    private static InstalledRecipe Clan => new(RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!, "", new RecipeState());

    private static InstalledRecipe Profile => new(RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!, "", new RecipeState());

    [Fact]
    public void ARecipeWithInputsGetsAClansPageBeforeTheFixedPages()
    {
        var clan = Clan;

        var pages = SetupPages.For([clan, Profile]);

        Assert.Equal(new[] { "Clans", "Your accounts", "Stats", "Recipes", "Alerts", "Score book", "Diagnostics" }, pages.Select(p => p.Title).ToArray());
        Assert.Equal(SetupPages.ClansId(clan.Recipe.Slug), pages[0].Id);
        Assert.Equal(clan.Recipe.Slug, pages[0].RecipeSlug);
        Assert.Equal("Clans", pages[0].ToString());
    }

    [Fact]
    public void FirstRunOpensTheClansPageOfARecipeWithInputsAndNoSources()
    {
        var clan = Clan;
        var source = new Source("s-00000001", clan.Recipe.Slug, new Dictionary<string, string> { ["clan"] = "CCGP" }, SourceRole.Main);

        Assert.Equal(SetupPages.ClansId(clan.Recipe.Slug), SetupPages.FirstRunPage([clan, Profile], []));
        Assert.Null(SetupPages.FirstRunPage([clan, Profile], [source]));
        Assert.Null(SetupPages.FirstRunPage([Profile], []));
    }

    [Fact]
    public void WithNoRecipesSetupStartsOnRecipes()
    {
        Assert.Equal(SetupPages.Recipes, SetupPages.StartPage([], []));
        Assert.Equal(SetupPages.Accounts, SetupPages.StartPage([Profile], []));
    }
}
