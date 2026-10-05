using System.IO;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Games;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

/// <summary>
/// The game page as data (spec "Setup UI > GamePage"): what each mode row says, which reader its clan section belongs to, and
/// when that section is dimmed. Pure, so the page's words are pinned without drawing it.
/// </summary>
public class GameModelTests
{
    private const string ClanBattle = "pet-sim-99-clan-battle-points";
    private const string TopClans = "pet-sim-99-top-clans";

    private static IReadOnlyList<InstalledRecipe> Shipped { get; } =
        [.. BuiltInRecipes.All.Select(b => new InstalledRecipe(RecipeParser.Parse(b.Text).Recipe!, b.Text, new RecipeState()))];

    private static GameDef Game => GameCatalog.BuiltIn.Games.Single();

    private static ModeSwitches Switches(params (string Key, bool On)[] set) =>
        new(GameCatalog.BuiltIn, set.ToDictionary(s => s.Key, s => s.On));

    [Fact]
    public void TheGameRowCarriesItsNameItsSwitchAndEveryModeInManifestOrder()
    {
        var row = GameModel.For(Game, Switches(), Shipped);

        Assert.Equal(("pet-sim-99", "Pet Sim 99", true), (row.Id, row.Name, row.IsOn));
        Assert.Equal(new[] { "Battle", "Profile" }, row.Modes.Select(m => m.Name).ToArray());
        Assert.Equal(new[] { "battle", "profile" }, row.Modes.Select(m => m.Id).ToArray());
        Assert.Equal(new[] { "pet-sim-99/battle", "pet-sim-99/profile" }, row.Modes.Select(m => m.Key).ToArray());
    }

    /// <summary>Every line is the manifest's or <see cref="ModeLines"/>' own: the page types nothing about what a mode reads.</summary>
    [Fact]
    public void EachModeSaysItsBlurbWhatItReadsWhatItSendsAndItsNote()
    {
        var row = GameModel.For(Game, Switches(), Shipped);
        var battle = row.Modes[0];
        var profile = row.Modes[1];

        Assert.Equal("Your clan's battle: place, race, pace and the top of the battle.", battle.Blurb);
        Assert.Equal("Reads ps99.biggamesapi.io, thumbnails.roblox.com, tr.rbxcdn.com every 3 min", battle.Reads);
        Assert.Null(battle.Sends);
        Assert.Null(battle.Note);

        Assert.Equal("Rank, diamonds, hatches and playtime for each of your accounts.", profile.Blurb);
        Assert.Equal("Reads ps99.biggamesapi.io every 30 min", profile.Reads);
        Assert.Equal("Sends the Roblox user id of every account in your RoRoRo list.", profile.Sends);
        Assert.Equal("Needs each account linked on db.biggames.io with its Profile view public.", profile.Note);
    }

    [Fact]
    public void BattleAsksThroughTheReaderThatHasTheClanInputAndProfileAsksNothing()
    {
        var row = GameModel.For(Game, Switches(), Shipped);

        Assert.Equal(ClanBattle, row.Modes[0].AskingSlug);
        Assert.Null(row.Modes[1].AskingSlug);
    }

    /// <summary>
    /// The decoy: Battle with its clans list read FIRST. "The mode's first reader" would hand the clan section the top-clans
    /// list, which has no clan input; the asking reader is the one whose inputs hold the mode's <c>asks</c>.
    /// </summary>
    [Fact]
    public void TheAskingReaderIsTheOneWithTheInputNotTheFirstRead()
    {
        var battle = Game.Modes[0] with { Reads = [TopClans, ClanBattle] };

        Assert.Equal(ClanBattle, GameModel.AskingSlug(battle, Shipped));
        Assert.Null(GameModel.AskingSlug(battle, [.. Shipped.Where(i => i.Recipe.Slug != ClanBattle)]));
    }

    [Fact]
    public void AnOnModeIsNotDimmed()
    {
        var battle = GameModel.For(Game, Switches(), Shipped).Modes[0];

        Assert.Equal((true, true), (battle.IsSet, battle.IsOn));
        Assert.Null(battle.DimmedLine);
    }

    /// <summary>The mode's own switch off: its clans stay in view, dimmed, with the one thing that brings them back.</summary>
    [Fact]
    public void AnOffModeThatAsksIsDimmedWithTheLineThatTurnsItOn()
    {
        var row = GameModel.For(Game, Switches(("pet-sim-99/battle", false)), Shipped);

        Assert.Equal((false, false), (row.Modes[0].IsSet, row.Modes[0].IsOn));
        Assert.Equal("Turn Battle on to read these clans.", row.Modes[0].DimmedLine);

        // Profile asks nothing, so it has no section to dim, and it stays on.
        Assert.Equal((true, true, null), (row.Modes[1].IsSet, row.Modes[1].IsOn, row.Modes[1].DimmedLine));
    }

    /// <summary>
    /// The game off with Battle's own switch still set: Battle remembers it is set (so turning the game on brings it back) but
    /// does not read, and the line names the switch that is actually off, the game's.
    /// </summary>
    [Fact]
    public void WithTheGameOffAModeKeepsItsOwnSwitchButIsDimmedByTheGames()
    {
        var row = GameModel.For(Game, Switches(("pet-sim-99", false)), Shipped);

        Assert.False(row.IsOn);
        Assert.Equal((true, false), (row.Modes[0].IsSet, row.Modes[0].IsOn));
        Assert.Equal("Turn Pet Sim 99 on to read these clans.", row.Modes[0].DimmedLine);
        Assert.Equal((true, false), (row.Modes[1].IsSet, row.Modes[1].IsOn));
    }

    /// <summary>
    /// A switch that couldn't be saved says why on the page, never as an exception (the settings file unreadable this session,
    /// or the write itself failing). The decoy is the long composition text: the page has its own shorter sentence for it.
    /// </summary>
    [Fact]
    public void ASwitchThatCouldNotBeSavedSaysWhyInPlainWords()
    {
        Assert.Equal(
            "Your settings file couldn't be read, so switches can't be saved this session.",
            GameModel.SwitchProblem(new InvalidOperationException(AppServices.SettingsNotWritten)));
        Assert.Equal("Could not save that: The disk is full.", GameModel.SwitchProblem(new IOException("The disk is full.")));
    }
}
