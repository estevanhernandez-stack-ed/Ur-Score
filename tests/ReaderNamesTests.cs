using Labs626.UrScore.Games;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class ReaderNamesTests
{
    private static IReadOnlyList<InstalledRecipe> Shipped { get; } =
        [.. BuiltInRecipes.All.Select(b => new InstalledRecipe(RecipeParser.Parse(b.Text).Recipe!, b.Text, new RecipeState()))];

    private static string Name(string slug) => ReaderNames.For(slug, GameCatalog.BuiltIn, Shipped);

    [Fact]
    public void AReaderIsCalledByItsMode()
    {
        Assert.Equal("Battle", Name("pet-sim-99-clan-battle-points"));
        Assert.Equal("Profile", Name("pet-sim-99-profile"));
    }

    [Fact]
    public void AClansListInAModeWithAnotherReaderSaysSo() =>
        Assert.Equal("Battle · clans list", Name("pet-sim-99-top-clans"));

    [Fact]
    public void AClansListAloneInItsModeIsJustTheMode()
    {
        var alone = new GameCatalog([new GameDef("g", "G", [new ModeDef("g", "top", "Top", "", ["pet-sim-99-top-clans"], null, null, null, true, [])])]);
        Assert.Equal("Top", ReaderNames.For("pet-sim-99-top-clans", alone, Shipped));
    }

    [Fact]
    public void AnOrphanKeepsItsSlug() => Assert.Equal("somebody-elses-recipe", Name("somebody-elses-recipe"));
}
