using Labs626.UrScore.Games;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class ModeLinesTests
{
    private static IReadOnlyList<InstalledRecipe> Shipped { get; } =
        [.. BuiltInRecipes.All.Select(b => new InstalledRecipe(RecipeParser.Parse(b.Text).Recipe!, b.Text, new RecipeState()))];

    private static ModeDef Mode(string key) => GameCatalog.BuiltIn.Find(key)!;

    [Fact]
    public void BattleReadsItsHostsEveryThreeMinutes() =>
        // The clan-battle recipe asks every 180 s and the top-clans one every 300 s; the shortest wins. Its clan icon adds
        // the two Roblox picture hosts after the game's host (A4).
        Assert.Equal(
            "Reads ps99.biggamesapi.io, thumbnails.roblox.com, tr.rbxcdn.com every 3 min",
            ModeLines.Reads(Mode("pet-sim-99/battle"), Shipped));

    [Fact]
    public void ProfileReadsEveryThirtyMinutes() =>
        Assert.Equal("Reads ps99.biggamesapi.io every 30 min", ModeLines.Reads(Mode("pet-sim-99/profile"), Shipped));

    [Fact]
    public void ProfileSendsEveryAccountsUserIdAndBattleSendsNothingPerAccount()
    {
        Assert.Equal("Sends the Roblox user id of every account in your RoRoRo list.", ModeLines.Sends(Mode("pet-sim-99/profile"), Shipped));
        Assert.Null(ModeLines.Sends(Mode("pet-sim-99/battle"), Shipped));
    }

    [Fact]
    public void AModeWithNoInstalledRecipeHasNoLines()
    {
        Assert.Null(ModeLines.Reads(Mode("pet-sim-99/profile"), []));
        Assert.Null(ModeLines.Sends(Mode("pet-sim-99/profile"), []));
    }

    [Fact]
    public void AnIntervalBelowTheRunnersFloorShowsTheFloor()
    {
        var fast = Shipped.Single(i => i.Recipe.Slug == "pet-sim-99-profile");
        var tenSeconds = fast with { Recipe = fast.Recipe with { EverySeconds = 10 } };
        Assert.Equal("Reads ps99.biggamesapi.io every 1 min", ModeLines.Reads(Mode("pet-sim-99/profile"), [tenSeconds]));
    }
}
