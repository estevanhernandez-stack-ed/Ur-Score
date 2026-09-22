using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

public class BoardTextTests
{
    [Fact]
    public void TheTopLineGivesThePeriodAndTheNextRead()
    {
        var period = new ReadingPeriod("AutumnBattle", null, Now.AddHours(76));
        var live = Live([MainClan], [Installed(Clan, "value")],
            new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = Snapshot(MainClan.Id, [], period: period) },
            running: true, lastRead: new Dictionary<string, DateTimeOffset> { [MainClan.Id] = Now.AddMinutes(-1) });

        Assert.Equal(PanelText.PeriodLine(period, Now, Now.AddMinutes(-1).AddSeconds(Clan.EffectiveEverySeconds)), BoardText.TopLine(live, MainClan.Id));
        // The top bar's own clock ticks this end down; the line above never speaks of it.
        Assert.Equal(period.Ends, BoardText.TopEnds(live, MainClan.Id));
        Assert.DoesNotContain("ends in", BoardText.TopLine(live, MainClan.Id), StringComparison.Ordinal);
    }

    /// <summary>A recipe that reads without a period has no end to count down, and the line stands alone.</summary>
    [Fact]
    public void WithoutAPeriodThereIsNothingToCountDown()
    {
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);
        var live = Live([profile], [Installed(Profile, "diamonds")], new Dictionary<string, RecipeSnapshot>());

        Assert.Null(BoardText.TopEnds(live, profile.Id));
        Assert.Null(BoardText.TopEnds(live, "no-such-source"));
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

        // Running: Ur Score really is still reading, so it may say so.
        Assert.Equal("RoRoRo is not running. Still reading and keeping your scores; nothing is being sent.",
            BoardText.DetailLine(Live([MainClan], [Installed(Clan, "value")], down, running: true), "over budget"));
        // Stopped: it is not reading, and the state line says so, so the detail line may not claim otherwise.
        Assert.Equal("RoRoRo wasn't running at the last read, so nothing was sent.",
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
        Assert.Equal(BoardText.HostDownStopped, BoardText.DetailLine(down, "over budget", boardsProblem: null));
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

    /// <summary>
    /// Backlog S1-14.3. Start asks RoRoRo for your accounts before it reads, for up to AppServices.AccountsWait, and nothing on
    /// screen said so: the button went grey and the line went on saying "Not started." The line says what it is waiting for, and
    /// because it is worked out from what the window is doing rather than written once, a redraw in the middle keeps saying it.
    /// </summary>
    [Fact]
    public void WhileStartWaitsForRoRoRoTheLineSaysSo()
    {
        var live = Live([MainClan], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot>());

        Assert.Equal("Starting. Asking RoRoRo for your accounts…", BoardText.StateLine(live, everStarted: false, new BoardActivity(Starting: true)));
        Assert.Equal("Starting. Asking RoRoRo for your accounts…", BoardText.StateLine(live, everStarted: true, new BoardActivity(Starting: true)));
    }

    /// <summary>
    /// Test now said "Reading every source once…" by writing the line once, and the first redraw (any read landing, or the 20 s
    /// clock) put "Not started." back while the read was still going.
    /// </summary>
    [Fact]
    public void WhileTestNowReadsTheLineSaysSoWhateverElseIsTrue()
    {
        var trouble = new Dictionary<string, RecipeSnapshot>
        {
            [AltClan.Id] = new RecipeSnapshot(WatchState.SourceUnreachable, "timed out", [], [], 0) { SourceId = AltClan.Id },
        };
        var testing = new BoardActivity(Testing: true);

        Assert.Equal("Reading every source once…",
            BoardText.StateLine(Live([MainClan, AltClan], [Installed(Clan, "value")], trouble), everStarted: false, testing));
        Assert.Equal("Reading every source once…",
            BoardText.StateLine(Live([MainClan, AltClan], [Installed(Clan, "value")], trouble, running: true), everStarted: true, testing));
    }

    /// <summary>
    /// Backlog S1-14.5. While stopped the line only ever said "Not started." or "Stopped.", so what a Test now found was never said
    /// where you pressed it. It now says what the read you asked for found: a source in trouble by name, else what they all found,
    /// else how many answered.
    /// </summary>
    [Fact]
    public void StoppedTheLineSaysWhatTheReadYouAskedForFound()
    {
        var asked = new BoardActivity(AskedReadAt: Now.AddMinutes(-2));
        var justNow = new Dictionary<string, DateTimeOffset> { [MainClan.Id] = Now.AddMinutes(-1), [AltClan.Id] = Now.AddMinutes(-1) };
        var reported = Snapshot(MainClan.Id, [Row(Main.RobloxUserId, 4200)]);
        var unreachable = new RecipeSnapshot(WatchState.SourceUnreachable, "timed out", [], [], 0) { SourceId = AltClan.Id };

        var trouble = Live([MainClan, AltClan], [Installed(Clan, "value")],
            new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = reported, [AltClan.Id] = unreachable }, lastRead: justNow);
        Assert.Equal("Not started. Last read of K0i2: Could not reach the data.", BoardText.StateLine(trouble, everStarted: false, asked));

        var one = Live([MainClan], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = reported }, lastRead: justNow);
        Assert.Equal("Not started. Last read: Reported to RoRoRo.", BoardText.StateLine(one, everStarted: false, asked));

        var idle = new RecipeSnapshot(WatchState.SourceIdle, "No clan battle running", [], [], 0) { SourceId = AltClan.Id };
        var two = Live([MainClan, AltClan], [Installed(Clan, "value")],
            new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = reported, [AltClan.Id] = idle }, lastRead: justNow);
        Assert.Equal("Not started. Last read: 2 sources answered.", BoardText.StateLine(two, everStarted: false, asked));

        var bothIdle = Live([MainClan, AltClan], [Installed(Clan, "value")],
            new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = idle with { SourceId = MainClan.Id }, [AltClan.Id] = idle }, lastRead: justNow);
        Assert.Equal("Not started. Last read: There was nothing to read.", BoardText.StateLine(bothIdle, everStarted: false, asked));
    }

    /// <summary>
    /// The news is only for a read you asked for since reading stopped. Pressing Stop still says "Stopped." and nothing else, a
    /// timed read that lands just after Stop isn't news you asked for, and a source read before the ask isn't part of its answer.
    /// </summary>
    [Fact]
    public void AfterStopTheLineSaysOnlyStoppedUntilYouAskForARead()
    {
        var reported = Snapshot(MainClan.Id, [Row(Main.RobloxUserId, 4200)]);
        var unreachable = new RecipeSnapshot(WatchState.SourceUnreachable, "timed out", [], [], 0) { SourceId = AltClan.Id };
        LiveBoard Board(DateTimeOffset mainAt, DateTimeOffset altAt) => Live([MainClan, AltClan], [Installed(Clan, "value")],
            new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = reported, [AltClan.Id] = unreachable },
            lastRead: new Dictionary<string, DateTimeOffset> { [MainClan.Id] = mainAt, [AltClan.Id] = altAt });

        var testedWhileRunning = new BoardActivity(AskedReadAt: Now.AddMinutes(-5), StoppedAt: Now.AddMinutes(-1));
        Assert.Equal("Stopped.", BoardText.StateLine(Board(Now.AddMinutes(-4), Now.AddMinutes(-4)), everStarted: true, testedWhileRunning));
        Assert.Equal("Stopped.", BoardText.StateLine(Board(Now.AddSeconds(-30), Now.AddSeconds(-30)), everStarted: true, testedWhileRunning));

        var testedSinceStop = new BoardActivity(AskedReadAt: Now.AddSeconds(-20), StoppedAt: Now.AddMinutes(-1));
        Assert.Equal("Stopped. Last read: Reported to RoRoRo.",
            BoardText.StateLine(Board(Now.AddSeconds(-10), Now.AddMinutes(-4)), everStarted: true, testedSinceStop));
        Assert.Equal("Stopped. Last read of K0i2: Could not reach the data.",
            BoardText.StateLine(Board(Now.AddSeconds(-10), Now.AddSeconds(-10)), everStarted: true, testedSinceStop));
    }

    /// <summary>
    /// Start, Stop and Test now showed "Something unexpected went wrong." and then drew the line again in their own finally,
    /// so it was never on screen. Said until the next press, in plain words; what went wrong is in Diagnostics' trail.
    /// </summary>
    [Fact]
    public void AFailureIsSaidOnTheLineAndItsDetailPointsAtDiagnostics()
    {
        var failed = new BoardActivity(Failed: true);
        var live = Live([MainClan], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot>(), running: true);

        Assert.Equal("Something unexpected went wrong.", BoardText.StateLine(live, everStarted: true, failed));
        Assert.Equal("Something unexpected went wrong.", BoardText.StateLine(live with { Running = false }, everStarted: true, failed));
        Assert.Equal("Setup › Diagnostics has the details.", BoardText.UnexpectedDetail);
    }

    /// <summary>
    /// A note from something you pressed (a failure's detail, an import's result) keeps the detail line until something replaces
    /// it; only why your boards weren't saved comes before it (R3), and RoRoRo being down and the budget wait behind it.
    /// </summary>
    [Fact]
    public void ANoteKeepsTheDetailLineOverRoRoRoBeingDownAndTheBudget()
    {
        var down = Live([MainClan], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot>
        {
            [MainClan.Id] = new RecipeSnapshot(WatchState.HostDown, null, [], [], 0) { SourceId = MainClan.Id },
        });

        Assert.Equal("Imported Clan.", BoardText.DetailLine(down, "over budget", boardsProblem: null, note: "Imported Clan."));
        Assert.Equal("Your boards file couldn't be read.", BoardText.DetailLine(down, "over budget", "Your boards file couldn't be read.", note: "Imported Clan."));
        Assert.Equal(BoardText.HostDownStopped, BoardText.DetailLine(down, "over budget", boardsProblem: null, note: null));
    }

    /// <summary>A41: every new branch the line can take still carries the sentence about remembered numbers.</summary>
    [Fact]
    public void TheSentenceAboutRememberedNumbersRidesEveryNewBranch()
    {
        var kept = Snapshot(MainClan.Id, [Row(Main.RobloxUserId, 4200)]) with { RememberedAt = Now.AddHours(-3) };
        var unreachable = new RecipeSnapshot(WatchState.SourceUnreachable, "timed out", [], [], 0) { SourceId = AltClan.Id };
        var live = Live([MainClan, AltClan], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot> { [AltClan.Id] = unreachable },
            lastRead: new Dictionary<string, DateTimeOffset> { [AltClan.Id] = Now.AddMinutes(-1) },
            remembered: new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = kept });

        foreach (var activity in new[] { new BoardActivity(Starting: true), new BoardActivity(Testing: true), new BoardActivity(Failed: true), new BoardActivity(AskedReadAt: Now.AddMinutes(-2)) })
        {
            Assert.EndsWith("The numbers on screen are the last ones Ur Score read, from 3h ago.", BoardText.StateLine(live, everStarted: false, activity));
        }

        Assert.Equal("Not started. Last read of K0i2: Could not reach the data. The numbers on screen are the last ones Ur Score read, from 3h ago.",
            BoardText.StateLine(live, everStarted: false, new BoardActivity(AskedReadAt: Now.AddMinutes(-2))));
    }

    /// <summary>
    /// Backlog S1-14.2. A score book that couldn't be read left Start and Test now off for the whole session, with an exception's
    /// own message as the only reason and no way to try again. The line says why in plain words, the board says what that stops
    /// and offers Try again. The board's heading says what the failure stops, not the failure again: the state line above
    /// already says it, and the 2026-09-17 smoke showed the two reading as one sentence twice (backlog V3-S.21).
    /// </summary>
    [Fact]
    public void AScoreBookThatCouldNotBeReadSaysWhyInPlainWordsAndOffersTryAgain()
    {
        Assert.Equal("Reading your score book…", BoardText.BookStateLine(unread: false));
        Assert.Equal("Your score book couldn't be read.", BoardText.BookStateLine(unread: true));

        Assert.Equal("Another program has a score book file open. Close it, then press Try again.",
            BoardText.BookUnread(new IOException("sharing", unchecked((int)0x80070020))));
        Assert.Equal("Windows didn't let Ur Score read its data folder.", BoardText.BookUnread(new UnauthorizedAccessException("denied")));
        Assert.Equal("The device is not ready.", BoardText.BookUnread(new IOException("The device is not ready.")));
        Assert.Equal("Something unexpected went wrong. Setup › Diagnostics has the details.",
            BoardText.BookUnread(new InvalidOperationException("Sequence contains no elements at C:\\somewhere")));

        var (line, detail, button) = BoardText.EmptyState(BoardEmpty.BookUnread, Clan);
        Assert.Equal(
            ("Start and Test now are off", "They come back once Ur Score can read your score book. The line above says what stopped it.", "Try again"),
            (line, detail, button));
        Assert.DoesNotContain("be read", line, StringComparison.Ordinal);
    }

    /// <summary>An unread book covers every board, over any other empty state, and goes as soon as the book is read.</summary>
    [Fact]
    public void AnUnreadScoreBookIsTheEmptyStateOverEveryBoard()
    {
        var starters = StarterBoards.All([Installed(Clan)], [MainClan]);
        var withPanel = new BoardDef("b-00000001", "Rivals", [new PanelDef("p-00000001", PanelType.Standing, new PanelSize(3), new PanelSettings(Clan.Slug, SourceId: MainClan.Id))]);

        Assert.Equal(BoardEmpty.BookUnread, BoardText.EmptyFor(starters, withPanel, bookUnread: true));
        Assert.Equal(BoardEmpty.BookUnread, BoardText.EmptyFor(StarterBoards.All([], []), withPanel, editing: true, bookUnread: true));
        Assert.Equal(BoardEmpty.None, BoardText.EmptyFor(starters, withPanel, bookUnread: false));
    }
}
