using Labs626.UrScore.Core;
using Labs626.UrScore.Games;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

/// <summary>Setup's page list and where it opens (spec "Setup UI > Page list"; PRD "The game page", "First run").</summary>
public class SetupPagesTests
{
    private const string ClanBattle = "pet-sim-99-clan-battle-points";
    private const string TopClans = "pet-sim-99-top-clans";

    private static IReadOnlyList<InstalledRecipe> Shipped { get; } =
        [.. BuiltInRecipes.All.Select(b => new InstalledRecipe(RecipeParser.Parse(b.Text).Recipe!, b.Text, new RecipeState()))];

    private static ModeSwitches Switches(params (string Key, bool On)[] set) =>
        new(GameCatalog.BuiltIn, set.ToDictionary(s => s.Key, s => s.On));

    private static Source Of(string slug, string id = "s-00000001") =>
        new(id, slug, new Dictionary<string, string> { ["clan"] = "CCGP" }, SourceRole.Main);

    [Fact]
    public void EachGameHasAPageBeforeTheFixedOnesAndThereIsNoRecipesOrClansPage()
    {
        var pages = SetupPages.For(GameCatalog.BuiltIn);

        Assert.Equal(new[] { "Pet Sim 99", "Your accounts", "Stats", "Alerts", "Score book", "Diagnostics" }, pages.Select(p => p.Title).ToArray());
        Assert.Equal("game:pet-sim-99", pages[0].Id);
        Assert.Equal("pet-sim-99", pages[0].GameId);
        Assert.Equal("Pet Sim 99", pages[0].ToString());
        Assert.All(pages.Skip(1), p => Assert.Null(p.GameId));
    }

    [Fact]
    public void TheGamePageHelperNamesTheGamesPage()
    {
        Assert.Equal("game:pet-sim-99", SetupPages.GamePage("pet-sim-99"));
        Assert.Equal("pet-sim-99", SetupPages.GameIdOf("game:pet-sim-99"));
        Assert.Null(SetupPages.GameIdOf(SetupPages.Stats));

        // No game at all (every manifest failed to parse): Diagnostics is where that is said.
        Assert.Equal(SetupPages.Diagnostics, SetupPages.GamePage(null));
    }

    /// <summary>
    /// First run is the game page while an on mode that asks has no source for its asking reader. The decoy is a source of
    /// the OTHER reader in Battle (the clans list): a check of "any source of the mode" would call that picked.
    /// </summary>
    [Fact]
    public void FirstRunOpensTheGamePageUntilTheAskingReaderHasASource()
    {
        var catalog = GameCatalog.BuiltIn;

        Assert.Equal("game:pet-sim-99", SetupPages.FirstRunPage(catalog, Switches(), Shipped, []));
        Assert.Equal("game:pet-sim-99", SetupPages.FirstRunPage(catalog, Switches(), Shipped, [Of(TopClans)]));
        Assert.Null(SetupPages.FirstRunPage(catalog, Switches(), Shipped, [Of(ClanBattle)]));

        Assert.Equal(("pet-sim-99", ClanBattle), SetupPages.FirstRun(catalog, Switches(), Shipped, []));
    }

    /// <summary>An upgrader without Battle (A5) is never nagged to pick a clan: only an on mode can ask.</summary>
    [Fact]
    public void AnOffModeOrAnOffGameNeverAsksOnFirstRun()
    {
        var catalog = GameCatalog.BuiltIn;

        Assert.Null(SetupPages.FirstRunPage(catalog, Switches(("pet-sim-99/battle", false)), Shipped, []));
        Assert.Null(SetupPages.FirstRunPage(catalog, Switches(("pet-sim-99", false)), Shipped, []));
    }

    [Fact]
    public void SetupStartsOnTheFirstRunPageElseTheFirstPage()
    {
        var catalog = GameCatalog.BuiltIn;
        var none = new GameCatalog([]);

        Assert.Equal("game:pet-sim-99", SetupPages.StartPage(catalog, Switches(), Shipped, []));
        Assert.Equal("game:pet-sim-99", SetupPages.StartPage(catalog, Switches(), Shipped, [Of(ClanBattle)]));
        Assert.Equal(SetupPages.Accounts, SetupPages.StartPage(none, new ModeSwitches(none, null), Shipped, []));
    }
}
