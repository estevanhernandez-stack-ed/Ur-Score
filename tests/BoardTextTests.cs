using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

public class BoardTextTests
{
    private static readonly Source MainClan = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
    private static readonly Source AltClan = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);

    [Fact]
    public void TheTopLineGivesThePeriodAndTheNextRead()
    {
        var period = new ReadingPeriod("AutumnBattle", null, Now.AddHours(76));
        var live = Live([MainClan], [Installed(Clan, "value")],
            new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = Snapshot(MainClan.Id, [], period: period) },
            running: true, lastRead: new Dictionary<string, DateTimeOffset> { [MainClan.Id] = Now.AddMinutes(-1) });

        Assert.Equal(PanelText.PeriodLine(period, Now, Now.AddMinutes(-1).AddSeconds(Clan.EffectiveEverySeconds)), BoardText.TopLine(live, MainClan.Id));
    }

    [Fact]
    public void WithoutAPeriodTheTopLineSaysHowOftenItReads()
    {
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);
        var live = Live([profile], [Installed(Profile, "diamonds")], new Dictionary<string, RecipeSnapshot>());

        Assert.Equal($"Reads every {StatText.Span(TimeSpan.FromSeconds(Profile.EffectiveEverySeconds))}", BoardText.TopLine(live, profile.Id));
    }

    [Fact]
    public void TheStateLineSaysStartedStoppedOrNamesASourceInTrouble()
    {
        var snaps = new Dictionary<string, RecipeSnapshot>
        {
            [MainClan.Id] = Snapshot(MainClan.Id, []),
            [AltClan.Id] = new RecipeSnapshot(WatchState.SourceUnreachable, "timed out", [], [], 0) { SourceId = AltClan.Id },
        };
        LiveBoard Board(bool running, params Source[] sources) => Live(sources, [Installed(Clan, "value")], snaps, running);

        Assert.Equal("Not started.", BoardText.StateLine(Board(false, MainClan, AltClan), everStarted: false));
        Assert.Equal("Stopped.", BoardText.StateLine(Board(false, MainClan, AltClan), everStarted: true));
        Assert.Equal("K0i2: Could not reach the data.", BoardText.StateLine(Board(true, MainClan, AltClan), everStarted: true));
        Assert.Equal("Reading 1 source.", BoardText.StateLine(Board(true, MainClan), everStarted: true));
    }

    [Fact]
    public void RoRoRoBeingDownTakesTheDetailLine()
    {
        var down = new Dictionary<string, RecipeSnapshot>
        {
            [MainClan.Id] = new RecipeSnapshot(WatchState.HostDown, null, [], [], 0) { SourceId = MainClan.Id },
        };

        Assert.Equal("RoRoRo is not running. Still reading and keeping your scores; nothing is being sent.",
            BoardText.DetailLine(Live([MainClan], [Installed(Clan, "value")], down), "over budget"));
        Assert.Equal("over budget",
            BoardText.DetailLine(Live([MainClan], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot>()), "over budget"));
    }

    [Fact]
    public void TheEmptyStatesUseTheSpecsWords()
    {
        var noRecipes = BoardText.EmptyState(BoardEmpty.NoRecipes, null);
        var noStats = BoardText.EmptyState(BoardEmpty.NoStats, Clan);
        var noSources = BoardText.EmptyState(BoardEmpty.NoSources, Clan);

        Assert.Equal(("Import a recipe to start", "Import recipe…"), (noRecipes.Line, noRecipes.Button));
        Assert.Equal(("No stats turned on yet", "Choose stats"), (noStats.Line, noStats.Button));
        Assert.Equal(("Choose your main clan", "Choose your main clan"), (noSources.Line, noSources.Button));
    }

    [Fact]
    public void TheAttributionCreditsRecipesThatAreRead() =>
        Assert.Equal(Clan.Credit, BoardText.Attribution(Live([MainClan], [Installed(Clan, "value"), Installed(Profile, "diamonds")], new Dictionary<string, RecipeSnapshot>())));

    /// <summary>
    /// Two recipes read at once credit the same source, and the clan recipe's whole credit is the first sentence of
    /// the profile recipe's. Whole-string Distinct cannot see that, so the footer said "Data from Big Games' public
    /// Pet Simulator 99 API." twice on the owner's own board (backlog V3-S.4, seen on screen 2026-09-15). A sentence
    /// is said once, and the ones that only the second recipe carries still get said.
    /// </summary>
    [Fact]
    public void ASentenceTwoRecipesShareIsCreditedOnce()
    {
        var profile = SourceOf("s-00000003", Profile, "estehernandez", SourceRole.Mine);
        var live = Live([MainClan, profile], [Installed(Clan, "value"), Installed(Profile, "diamonds")], new Dictionary<string, RecipeSnapshot>());

        var said = BoardText.Attribution(live);

        Assert.Equal(
            "Data from Big Games' public Pet Simulator 99 API. Each account must be linked on db.biggames.io with its Profile view public.",
            said);
        Assert.Equal(1, said.Split("Data from Big Games'").Length - 1);
    }

    [Fact]
    public void NoRecipesShowsOverEveryBoardAndAStartersStatesOnlyOnATabThatFollowsIt()
    {
        var noStats = StarterBoards.All([Installed(Clan)], [MainClan]);
        var following = BoardDefs.Following(noStats[0]);
        var saved = new BoardDef("b-00000001", "Rivals", []);
        var withPanel = saved with
        {
            Panels = [new PanelDef("p-00000001", PanelType.Standing, new PanelSize(3), new PanelSettings(Clan.Slug, SourceId: MainClan.Id))],
        };

        Assert.Equal(BoardEmpty.NoRecipes, BoardText.EmptyFor(StarterBoards.All([], []), withPanel));
        Assert.Equal(BoardEmpty.NoStats, BoardText.EmptyFor(noStats, following));
        Assert.Equal(BoardEmpty.NoPanels, BoardText.EmptyFor(noStats, following, editing: true));
        Assert.Equal(BoardEmpty.NoPanels, BoardText.EmptyFor(noStats, saved));
        Assert.Equal(BoardEmpty.None, BoardText.EmptyFor(noStats, withPanel));
    }

    [Fact]
    public void WhyYourBoardsArentSavedComesBeforeRoRoRoBeingDownAndTheBudget()
    {
        var down = Live([MainClan], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot>
        {
            [MainClan.Id] = new RecipeSnapshot(WatchState.HostDown, null, [], [], 0) { SourceId = MainClan.Id },
        });
        var up = Live([MainClan], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot>());

        Assert.Equal("Your boards file couldn't be read.", BoardText.DetailLine(down, "over budget", "Your boards file couldn't be read."));
        Assert.Equal("Your boards file couldn't be read.", BoardText.DetailLine(up, "over budget", "Your boards file couldn't be read."));
        Assert.Equal(BoardText.HostDown, BoardText.DetailLine(down, "over budget", boardsProblem: null));
        Assert.Equal("over budget", BoardText.DetailLine(up, "over budget", boardsProblem: null));
        Assert.Equal("", BoardText.DetailLine(up, null, boardsProblem: null));
    }

    [Fact]
    public void ABoardChangeThatWasntSavedSaysWhyInPlainWords()
    {
        Assert.Equal("Your change to the boards wasn't saved: another program has your boards file open. Close it, then try again.",
            BoardText.BoardsNotSaved(new IOException("sharing", unchecked((int)0x80070020))));
        Assert.Equal("Your change to the boards wasn't saved: the disk is full.",
            BoardText.BoardsNotSaved(new IOException("full", unchecked((int)0x80070070))));
        Assert.Equal("Your change to the boards wasn't saved: Windows didn't let Ur Score write to its data folder.",
            BoardText.BoardsNotSaved(new UnauthorizedAccessException("denied")));
        Assert.Equal("Your change to the boards wasn't saved: The device is not ready.",
            BoardText.BoardsNotSaved(new IOException("The device is not ready.")));
    }

    [Fact]
    public void ASavedBoardWithNoPanelsSaysSo() =>
        Assert.Equal(
            ("This board has no panels yet", "Add panels from the gallery, then arrange them with Edit board.", "Add panel"),
            BoardText.EmptyState(BoardEmpty.NoPanels, Clan));

    [Fact]
    public void InEditModeAnEmptyBoardSaysWhatToDoFromThere() =>
        Assert.Equal(
            ("This board has no panels yet", "Add panels from the gallery with Add panel, then press Done.", "Add panel"),
            BoardText.EmptyState(BoardEmpty.NoPanels, Clan, editing: true));

    [Fact]
    public void AnUnexpectedSaveFailureSaysSoWithoutItsMessage() =>
        Assert.Equal("Your change to the boards wasn't saved: something unexpected went wrong.",
            BoardText.BoardsNotSaved(new InvalidOperationException("p-1 at C:\\somewhere")));

    [Fact]
    public void TheStateLineSaysTheNumbersOnScreenAreTheLastOnesItRead()
    {
        var kept = Snapshot(MainClan.Id, [Row(Main.RobloxUserId, 4200)]) with { RememberedAt = Now.AddHours(-3) };
        var live = Live([MainClan], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot>(),
            remembered: new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = kept });

        Assert.Equal("Not started. The numbers on screen are the last ones Ur Score read, from 3h ago.",
            BoardText.StateLine(live, everStarted: false));
    }

    [Fact]
    public void TheStateLineTakesTheOldestRememberedReadingSoItNeverSoundsFresherThanItIs()
    {
        var snaps = new Dictionary<string, RecipeSnapshot>
        {
            [MainClan.Id] = Snapshot(MainClan.Id, []) with { RememberedAt = Now.AddMinutes(-20) },
            [AltClan.Id] = Snapshot(AltClan.Id, []) with { RememberedAt = Now.AddDays(-2) },
        };
        var live = Live([MainClan, AltClan], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot>(), remembered: snaps);

        Assert.EndsWith("from 2d ago.", BoardText.StateLine(live, everStarted: true));
    }

    /// <summary>
    /// Review I1. A failing read is exactly when the numbers beside it go stale, so the branch that names a source in
    /// trouble is the last one that may drop the sentence. A41's promise is that this mark cannot be forgotten.
    /// </summary>
    [Fact]
    public void ASourceInTroubleNeverSwallowsTheSentenceAboutTheNumbersOnScreen()
    {
        var snaps = new Dictionary<string, RecipeSnapshot>
        {
            [AltClan.Id] = new RecipeSnapshot(WatchState.SourceUnreachable, "timed out", [], [], 0) { SourceId = AltClan.Id },
        };
        var kept = Snapshot(MainClan.Id, [Row(Main.RobloxUserId, 4200)]) with { RememberedAt = Now.AddHours(-3) };
        var live = Live([MainClan, AltClan], [Installed(Clan, "value")], snaps,
            running: true, remembered: new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = kept });

        Assert.Equal("K0i2: Could not reach the data. The numbers on screen are the last ones Ur Score read, from 3h ago.",
            BoardText.StateLine(live, everStarted: true));

        // And every other branch that can return while remembered numbers are drawn says it too.
        Assert.EndsWith("from 3h ago.", BoardText.StateLine(live with { Running = false }, everStarted: true));
        Assert.EndsWith("from 3h ago.", BoardText.StateLine(live, everStarted: false));
    }

    /// <summary>
    /// Review round 2: the source whose own read failed is still drawing its remembered numbers, so the line has to
    /// carry both — the fault, and how old the numbers beside it are.
    /// </summary>
    [Fact]
    public void ASourceWhoseOwnReadFailedStillSaysHowOldTheNumbersItIsDrawingAre()
    {
        var failed = new RecipeSnapshot(WatchState.SourceUnreachable, "timed out", [], [], 0) { SourceId = MainClan.Id };
        var kept = Snapshot(MainClan.Id, [Row(Main.RobloxUserId, 4200)]) with { RememberedAt = Now.AddHours(-3) };
        var live = Live([MainClan], [Installed(Clan, "value")],
            new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = failed },
            running: true, remembered: new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = kept });

        Assert.Equal("CCGP: Could not reach the data. The numbers on screen are the last ones Ur Score read, from 3h ago.",
            BoardText.StateLine(live, everStarted: true));
    }

    [Fact]
    public void ARememberedSnapshotIsNeverAStateUrScoreIsIn()
    {
        // Nothing in this map describes what is happening now, so the state line must not read one as a fault.
        var kept = new RecipeSnapshot(WatchState.SourceUnreachable, "timed out", [], [], 0) { SourceId = MainClan.Id, RememberedAt = Now.AddHours(-1) };
        var live = Live([MainClan], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot>(),
            running: true, remembered: new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = kept });

        Assert.StartsWith("Reading 1 source.", BoardText.StateLine(live, everStarted: true));
    }
}
