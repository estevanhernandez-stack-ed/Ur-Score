using System.IO;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Core;
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

    // ---- The two things Profile needs (accounts from RoRoRo; each linked on the site with its Profile view public) ----

    private const string ProfileSlug = "pet-sim-99-profile";

    private static readonly Source ProfileSource = new("s-profile1", ProfileSlug, new Dictionary<string, string>(), SourceRole.Mine);

    private static HostAccount Account(long userId, string name) => new(Guid.NewGuid(), userId, name);

    private static readonly IReadOnlyList<HostAccount> Eight =
        [.. Enumerable.Range(1, 8).Select(i => Account(i, $"Alt{i}"))];

    /// <summary>A live Profile read: these ids came back with data, these were unavailable for these reasons.</summary>
    private static Dictionary<string, RecipeSnapshot> Read(long[] read, params (long Id, UnavailableReason Why)[] unavailable) =>
        new()
        {
            [ProfileSource.Id] = new RecipeSnapshot(WatchState.Showing, null, [], [], read.Length + unavailable.Length, null,
                [.. read.Select(id => new RecipeRow(id, new Dictionary<string, double> { ["rank"] = 1 }))])
            {
                Unavailable = unavailable.ToDictionary(u => u.Id, u => $"message for {u.Id}"),
                UnavailableReasons = unavailable.ToDictionary(u => u.Id, u => u.Why),
            },
        };

    private static GameModeRow ProfileRow(
        IReadOnlyList<HostAccount> accounts, IReadOnlyDictionary<string, RecipeSnapshot> latest, ModeSwitches? switches = null) =>
        GameModel.For(Game, switches ?? Switches(), Shipped, accounts, [ProfileSource], latest).Modes.Single(m => m.Id == "profile");

    [Theory]
    [InlineData(8, "Ur Score uses the 8 accounts saved in RoRoRo.")]
    [InlineData(1, "Ur Score uses the 1 account saved in RoRoRo.")]
    [InlineData(0, "Add your Roblox accounts in RoRoRo first. Ur Score reads the list from there; it never signs in to anything itself.")]
    public void TheAccountsLineSaysHowManyAccountsRoRoRoHolds(int count, string line)
    {
        var row = GameModel.For(Game, Switches(), Shipped, [.. Eight.Take(count)], [], new Dictionary<string, RecipeSnapshot>());

        Assert.Equal(line, row.AccountsLine);
    }

    [Fact]
    public void ProfileCountsLinkedAccountsAndNamesTheUnlinkedAndThePrivateApart()
    {
        // 1-5 read; 6 and 7 not linked (404); 8 linked but private. Linked counts the private one: it IS linked.
        var status = ProfileRow(Eight, Read([1, 2, 3, 4, 5], (6, UnavailableReason.NotFound), (7, UnavailableReason.NotFound),
            (8, UnavailableReason.Declared))).LinkStatus!;

        Assert.Equal(new[]
        {
            "6 of 8 accounts are linked.",
            "Not linked: Alt6, Alt7.",
            "Linked, but the Profile view is private: Alt8.",
        }, status.Lines);
        Assert.True(status.ShowButton);
        Assert.Equal(new ModeLink("Link on db.biggames.io", new Uri("https://db.biggames.io")), status.Link);
    }

    [Fact]
    public void EveryAccountLinkedSaysSoAndHidesTheButton()
    {
        var all = ProfileRow(Eight, Read([1, 2, 3, 4, 5, 6, 7, 8])).LinkStatus!;
        var one = ProfileRow([Eight[0]], Read([1])).LinkStatus!;

        Assert.Equal(new[] { "All 8 accounts are linked." }, all.Lines);
        Assert.Equal(new[] { "Your account is linked." }, one.Lines);
        Assert.False(all.ShowButton || one.ShowButton);
    }

    [Fact]
    public void OneLinkedAccountOfSeveralReadsInTheSingular()
    {
        var status = ProfileRow([.. Eight.Take(2)], Read([1], (2, UnavailableReason.NotFound))).LinkStatus!;

        Assert.Equal(new[] { "1 of 2 accounts is linked.", "Not linked: Alt2." }, status.Lines);
    }

    /// <summary>
    /// A 400 is neither "not linked" nor "private" (the source's own trouble with that account), so it is counted in neither
    /// group and not in the total: the line never claims to know what it doesn't.
    /// </summary>
    [Fact]
    public void AnAccountTheSourceRefusedForAnotherReasonIsInNeitherGroup()
    {
        var status = ProfileRow([.. Eight.Take(3)], Read([1], (2, UnavailableReason.NotFound), (3, UnavailableReason.BadRequest))).LinkStatus!;

        Assert.Equal(new[] { "1 of 2 accounts is linked.", "Not linked: Alt2." }, status.Lines);
    }

    [Fact]
    public void BeforeTheFirstProfileReadItSaysNotReadYetAndOffersTheLink()
    {
        var status = ProfileRow(Eight, new Dictionary<string, RecipeSnapshot>()).LinkStatus!;

        Assert.Equal(new[] { "Not read yet." }, status.Lines);
        Assert.True(status.ShowButton);
    }

    /// <summary>The score book's remembered numbers say nothing about linking: only a live read does.</summary>
    [Fact]
    public void ARememberedSnapshotIsNotARead()
    {
        var latest = Read([1, 2, 3, 4, 5, 6, 7, 8]);
        latest[ProfileSource.Id] = latest[ProfileSource.Id] with { RememberedAt = DateTimeOffset.UnixEpoch };

        Assert.Equal(new[] { "Not read yet." }, ProfileRow(Eight, latest).LinkStatus!.Lines);
    }

    [Fact]
    public void AReadThatFailedWholeSaysItCouldNotTell()
    {
        var latest = new Dictionary<string, RecipeSnapshot>
        {
            [ProfileSource.Id] = new RecipeSnapshot(WatchState.SourceUnreachable, "Could not reach the source.", [], [], 0),
        };

        var status = ProfileRow(Eight, latest).LinkStatus!;

        Assert.Equal(new[] { "The last read couldn't tell which accounts are linked." }, status.Lines);
        Assert.True(status.ShowButton);
    }

    [Fact]
    public void TheLinkStatusIsHiddenWhenProfileIsOffAndBattleNeverHasOne()
    {
        var latest = Read([1], (2, UnavailableReason.NotFound));

        Assert.Null(ProfileRow(Eight, latest, Switches(("pet-sim-99/profile", false))).LinkStatus);
        Assert.Null(ProfileRow(Eight, latest, Switches(("pet-sim-99", false))).LinkStatus);
        Assert.Null(GameModel.For(Game, Switches(), Shipped, Eight, [ProfileSource], latest).Modes.Single(m => m.Id == "battle").LinkStatus);
    }

    /// <summary>The button's words and address are the manifest's: a mode whose manifest says otherwise gets exactly that.</summary>
    [Fact]
    public void TheButtonsTextAndAddressComeFromTheManifest()
    {
        var link = new ModeLink("Somewhere else", new Uri("https://example.com/x"));
        var game = Game with { Modes = [.. Game.Modes.Select(m => m.Id == "profile" ? m with { Link = link } : m)] };

        var row = GameModel.For(game, Switches(), Shipped, Eight, [ProfileSource], new Dictionary<string, RecipeSnapshot>());

        Assert.Equal(link, row.Modes.Single(m => m.Id == "profile").LinkStatus!.Link);
    }
}
