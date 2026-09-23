using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

public class BoardEditsTests
{
    private static BoardDef BoardOf(string id, params PanelType[] types) => new(id, id,
        [.. types.Select((type, i) => new PanelDef($"p-{id}-{i + 1}", type, BoardDefs.DefaultSize(type), new PanelSettings(Clan.Slug)))]);

    private static string[] Ids(BoardDef board) => [.. board.Panels.Select(p => p.Id)];

    [Fact]
    public void ANewBoardTakesTheFirstFreeNumber()
    {
        IReadOnlyList<BoardDef> boards = [BoardOf("b-1") with { Name = "Battle" }, BoardOf("b-2") with { Name = "board 3" }];

        Assert.Equal("Board 4", BoardEdits.NextName(boards));
        Assert.Equal("Board 2", BoardEdits.NextName([BoardOf("b-1") with { Name = "Battle" }]));
    }

    [Fact]
    public void AddAndReplaceKeepTheOrder()
    {
        IReadOnlyList<BoardDef> boards = [BoardOf("b-1"), BoardOf("b-2")];

        var added = BoardEdits.Add(boards, BoardOf("b-3"));
        var replaced = BoardEdits.Replace(added, BoardOf("b-2") with { Name = "Rivals" });

        Assert.Equal(new[] { "b-1", "b-2", "b-3" }, added.Select(b => b.Id).ToArray());
        Assert.Equal("Rivals", replaced[1].Name);
    }

    [Theory]
    [InlineData(null, null, "Board 3")]
    [InlineData("", null, "Board 3")]
    [InlineData("  \t ", null, "Board 3")]
    [InlineData("Board 3", null, "Board 3")]
    [InlineData("  Rivals  ", null, "Rivals")]
    [InlineData(null, "Battle", "Battle")]
    [InlineData("  \t ", "Alts", "Alts")]
    [InlineData("Board 3", "Battle", "Battle")]
    [InlineData("  Board 3  ", "Alts", "Alts")]
    [InlineData("board 3", "Battle", "board 3")]
    [InlineData("  Rivals  ", "Battle", "Rivals")]
    public void NewBoardNamingKeepsCustomTextElseUsesTheChosenDefault(string? typed, string? starter, string expected) =>
        Assert.Equal(expected, BoardEdits.NewBoardName(typed, "Board 3", starter));

    [Theory]
    [InlineData(null)]
    [InlineData("Battle")]
    public void NewBoardNamingUsesTheExistingLengthLimitBeforeChoosingAFallback(string? starter)
    {
        var typed = new string('A', BoardDefs.MaxNameLength - 1) + " rest";

        Assert.Equal(new string('A', BoardDefs.MaxNameLength - 1), BoardEdits.NewBoardName(typed, "Board 3", starter));
        Assert.Equal(starter ?? new string('B', BoardDefs.MaxNameLength),
            BoardEdits.NewBoardName(new string('B', BoardDefs.MaxNameLength + 1), new string('B', BoardDefs.MaxNameLength), starter));
    }

    [Fact]
    public void RenameTrimsAndABlankNameChangesNothing()
    {
        IReadOnlyList<BoardDef> boards = [BoardOf("b-1"), BoardOf("b-2")];

        Assert.Equal("Rivals", BoardEdits.Rename(boards, "b-2", "  Rivals ")[1].Name);
        Assert.Same(boards, BoardEdits.Rename(boards, "b-2", "   "));
        Assert.Same(boards, BoardEdits.Rename(boards, "b-9", "Rivals"));
    }

    [Fact]
    public void RenamingToTheNameItHasChangesNothing()
    {
        IReadOnlyList<BoardDef> boards = [BoardOf("b-1"), BoardOf("b-2")];

        Assert.Same(boards, BoardEdits.Rename(boards, "b-2", "b-2"));
        Assert.Same(boards, BoardEdits.Rename(boards, "b-2", "  b-2 "));
        Assert.Equal("B-2", BoardEdits.Rename(boards, "b-2", "B-2")[1].Name);
    }

    [Fact]
    public void DuplicateGoesRightAfterWithNewIdsAndNoPopOuts()
    {
        var original = BoardOf("b-1", PanelType.Standing, PanelType.Race) with { Name = "Battle" };
        original = BoardEdits.PopOut(original, "p-b-1-2", new PopOutRect(10, 10, 360, 300));
        IReadOnlyList<BoardDef> boards = [original, BoardOf("b-2")];

        var result = BoardEdits.Duplicate(boards, "b-1");

        Assert.Equal(3, result.Count);
        var copy = result[1];
        Assert.Equal("Battle copy", copy.Name);
        Assert.NotEqual("b-1", copy.Id);
        Assert.Equal(original.Panels.Select(p => (p.Type, p.Size, p.Settings)), copy.Panels.Select(p => (p.Type, p.Size, p.Settings)));
        Assert.Empty(copy.Panels.Select(p => p.Id).Intersect(Ids(original)));
        Assert.All(copy.Panels, p => Assert.Null(p.PopOut));
        Assert.Equal("b-2", result[2].Id);
    }

    [Theory]
    [InlineData("A name that is thirty-five letters!", "A name that is thirty-five letters! copy")]
    [InlineData("A board name that is forty letters long", "A board name that is forty letters copy")]
    [InlineData("Rivals and friends of the main clan ....", "Rivals and friends of the main clan copy")]
    public void ADuplicateOfALongNameStillEndsInCopy(string name, string expected)
    {
        Assert.True(name.Length <= BoardDefs.MaxNameLength);
        IReadOnlyList<BoardDef> boards = [BoardOf("b-1") with { Name = name }];

        var copy = BoardEdits.Duplicate(boards, "b-1")[1];

        Assert.Equal(expected, copy.Name);
        Assert.True(copy.Name.Length <= BoardDefs.MaxNameLength);
    }

    [Fact]
    public void TheLastBoardCantBeDeleted()
    {
        IReadOnlyList<BoardDef> one = [BoardOf("b-1")];
        IReadOnlyList<BoardDef> two = [BoardOf("b-1"), BoardOf("b-2")];

        Assert.Same(one, BoardEdits.Delete(one, "b-1"));
        Assert.Equal(new[] { "b-2" }, BoardEdits.Delete(two, "b-1").Select(b => b.Id).ToArray());
        Assert.Same(two, BoardEdits.Delete(two, "b-9"));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    public void OnlyAnEmptyFollowingStarterRefusesDuplication(bool follows, bool populated, bool allowed)
    {
        var original = BoardOf("b-1", populated ? [PanelType.Standing] : []) with { Follows = follows ? "battle" : null };
        IReadOnlyList<BoardDef> boards = [original];

        var result = BoardEdits.Duplicate(boards, original.Id);

        Assert.Equal(allowed, BoardEdits.CanDuplicate(original));
        if (!allowed)
        {
            Assert.Same(boards, result);
            Assert.Same(original, Assert.Single(result));
            return;
        }

        Assert.Equal(2, result.Count);
        Assert.Same(original, result[0]);
        Assert.Null(result[1].Follows);
        Assert.NotEqual(original.Id, result[1].Id);
        Assert.Equal(original.Panels.Count, result[1].Panels.Count);
    }

    [Fact]
    public void AnAddedPanelGoesLastAtItsDefaultSize()
    {
        var board = BoardEdits.AddPanel(BoardOf("b-1", PanelType.Standing), PanelType.LiveLeaderboard, new PanelSettings(Clan.Slug, SourceId: "s-1"));

        var added = board.Panels[^1];
        Assert.Equal(PanelType.LiveLeaderboard, added.Type);
        Assert.Equal(new PanelSize(PanelSize.Wide), added.Size);
        Assert.Equal("s-1", added.Settings.SourceId);
        Assert.Matches("^p-[0-9a-f]{8}$", added.Id);
    }

    [Fact]
    public void MovingCountsThePanelItselfInTheInsertionIndex()
    {
        var board = BoardOf("b", PanelType.Standing, PanelType.Race, PanelType.Records, PanelType.Top);
        string a = "p-b-1", b = "p-b-2", c = "p-b-3", d = "p-b-4";

        Assert.Equal(new[] { b, c, a, d }, Ids(BoardEdits.MoveTo(board, a, 3)));
        Assert.Equal(new[] { d, a, b, c }, Ids(BoardEdits.MoveTo(board, d, 0)));
        var atEnd = BoardEdits.MoveTo(board, a, 99);
        Assert.Equal(new[] { b, c, d, a }, Ids(atEnd));
        Assert.Equal(new[] { a, b, c, d }, Ids(BoardEdits.MoveTo(atEnd, a, 0)));
        Assert.Same(board, BoardEdits.MoveTo(board, b, 2));
        Assert.Same(board, BoardEdits.MoveTo(board, b, 1));
        Assert.Equal(new[] { a, c, b, d }, Ids(BoardEdits.MoveBy(board, c, -1)));
        Assert.Equal(new[] { a, c, b, d }, Ids(BoardEdits.MoveBy(board, b, 1)));
        Assert.Same(board, BoardEdits.MoveBy(board, a, -1));
        Assert.Same(board, BoardEdits.MoveBy(board, d, 1));
        Assert.Same(board, BoardEdits.MoveTo(board, "p-gone", 0));
    }

    [Fact]
    public void RemoveResizeSettingsAndPopOutsChangeOnlyTheirPanel()
    {
        var board = BoardOf("b", PanelType.Standing, PanelType.Race);
        var rect = new PopOutRect(1400, 80, 360, 300);

        Assert.Equal(new[] { "p-b-2" }, Ids(BoardEdits.RemovePanel(board, "p-b-1")));
        Assert.Equal(new PanelSize(12, Tall: true), BoardEdits.Resize(board, "p-b-2", new PanelSize(40, Tall: true)).Panels[1].Size);
        Assert.Equal(new PanelSize(1), BoardEdits.Resize(board, "p-b-2", new PanelSize(0)).Panels[1].Size);
        Assert.Equal("s-9", BoardEdits.SetSettings(board, "p-b-1", PanelType.Standing, new PanelSettings(Clan.Slug, SourceId: "s-9")).Panels[0].Settings.SourceId);

        var popped = BoardEdits.PopOut(board, "p-b-2", rect);
        Assert.Equal(rect, popped.Panels[1].PopOut);
        Assert.Null(popped.Panels[0].PopOut);
        Assert.Null(BoardEdits.Return(popped, "p-b-2").Panels[1].PopOut);
        Assert.Same(board, BoardEdits.RemovePanel(board, "p-gone"));
    }

    /// <summary>
    /// Settings are applied to the panel they were built for, and a panel's form is built from its TYPE: settings
    /// meant for a Race landing on a Standing by id alone would be a Standing reading a Race's fields. Nothing
    /// reaches that today — the ⋯ form is modal and a panel's type never changes under it — so this is the
    /// invariant stated where it is relied on, and a throw is how a future caller finds out (S2-6.7).
    /// </summary>
    [Fact]
    public void SettingsAreRefusedForAPanelOfAnotherType()
    {
        var board = BoardOf("b", PanelType.Standing, PanelType.Race);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            BoardEdits.SetSettings(board, "p-b-1", PanelType.Race, new PanelSettings(Clan.Slug, SourceIds: ["s-1", "s-2"])));

        Assert.Contains("Standing", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Race", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsThatDontChangeChangeNothing()
    {
        var race = new PanelSettings(Clan.Slug, SourceIds: ["s-1", "s-2"]);
        var board = BoardOf("b", PanelType.Standing) with
        {
            Panels = [new PanelDef("p-b-1", PanelType.Race, new PanelSize(), race)],
        };

        // A settings form closed with nothing changed builds equal settings in a new list; nothing is written.
        Assert.Same(board, BoardEdits.SetSettings(board, "p-b-1", PanelType.Race, new PanelSettings(Clan.Slug, SourceIds: ["s-1", "s-2"])));
        Assert.Same(board, BoardEdits.SetSettings(board, "p-gone", PanelType.Race, new PanelSettings(Clan.Slug, SourceIds: ["s-2", "s-1"])));
        Assert.Equal(new[] { "s-2", "s-1" },
            BoardEdits.SetSettings(board, "p-b-1", PanelType.Race, new PanelSettings(Clan.Slug, SourceIds: ["s-2", "s-1"])).Panels[0].Settings.SourceIds);
        Assert.NotSame(board, BoardEdits.SetSettings(board, "p-b-1", PanelType.Race, race with { Stat = "value" }));
    }

    [Fact]
    public void APanelIsFoundOnWhicheverBoardHoldsIt()
    {
        IReadOnlyList<BoardDef> boards = [BoardOf("b-1", PanelType.Standing), BoardOf("b-2", PanelType.Top, PanelType.Records)];

        var found = BoardEdits.Find(boards, "p-b-2-2");

        Assert.True(found.HasValue);
        var (onBoard, panel) = found.GetValueOrDefault();
        Assert.Equal("b-2", onBoard.Id);
        Assert.Equal(PanelType.Records, panel.Type);
        Assert.Null(BoardEdits.Find(boards, "p-gone"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingAndEmptyRaceListsAreTheSameSettingsInEitherDirection(bool savedEmpty)
    {
        var saved = new PanelSettings(Clan.Slug, SourceIds: savedEmpty ? [] : null);
        var replacement = saved with { SourceIds = savedEmpty ? null : [] };
        var board = BoardOf("b", PanelType.Race) with
        {
            Panels = [new PanelDef("p-b-1", PanelType.Race, new PanelSize(), saved)],
        };

        var unchanged = BoardEdits.SetSettings(board, "p-b-1", PanelType.Race, replacement);

        Assert.Same(board, unchanged);
        Assert.Same(saved, unchanged.Panels[0].Settings);
        Assert.Equal(BoardDefs.Key(board), BoardDefs.Key(board with
        {
            Panels = [board.Panels[0] with { Settings = replacement }],
        }));
        var changed = BoardEdits.SetSettings(board, "p-b-1", PanelType.Race, replacement with { Recipe = "another-recipe" });
        Assert.NotSame(board, changed);
        Assert.Equal("another-recipe", changed.Panels[0].Settings.Recipe);
        Assert.Equal(Clan.Slug, board.Panels[0].Settings.Recipe);
    }

    [Fact]
    public void AutomationIdsCountEachTypeInBoardOrder() =>
        Assert.Equal(
            new[] { "StandingPanel1", "RacePanel1", "StandingPanel2", "ProfileStatPanel1" },
            BoardEdits.AutomationIds(BoardOf("b", PanelType.Standing, PanelType.Race, PanelType.Standing, PanelType.ProfileStat)).ToArray());

    [Fact]
    public void APanelsAutomationIdIsTheOneItHasOnWhicheverBoardHoldsIt()
    {
        IReadOnlyList<BoardDef> boards = [BoardOf("b-1", PanelType.Standing), BoardOf("b-2", PanelType.Top, PanelType.Standing, PanelType.Standing)];

        Assert.Equal("StandingPanel1", BoardEdits.AutomationIdOf(boards, "p-b-1-1"));
        Assert.Equal("StandingPanel2", BoardEdits.AutomationIdOf(boards, "p-b-2-3"));
        Assert.Equal("TopPanel1", BoardEdits.AutomationIdOf(boards, "p-b-2-1"));
        Assert.Null(BoardEdits.AutomationIdOf(boards, "p-gone"));
    }

    /// <summary>
    /// Two boards, each with a Standing panel as its first: on the BOARD both are "StandingPanel1", which is fine
    /// because a board shows one board at a time. In their own pop-out windows they are not, because both windows
    /// can be open at once, and two live windows carrying the same id broke every walk that looked a pop-out up by
    /// it (S2-P.16). The pop-out id is the board's id and the slot's, so the two differ and each still ends in the
    /// slot id a walk asks for. This is the exact collision, not a stand-in: same type, same position, two boards.
    /// </summary>
    [Fact]
    public void TwoBoardsFirstStandingPanelsShareASlotIdButNotAPopOutId()
    {
        IReadOnlyList<BoardDef> boards = [BoardOf("b-1", PanelType.Standing), BoardOf("b-2", PanelType.Standing)];

        Assert.Equal(BoardEdits.AutomationIdOf(boards, "p-b-1-1"), BoardEdits.AutomationIdOf(boards, "p-b-2-1"));

        var one = BoardEdits.PopOutAutomationId(boards, "p-b-1-1");
        var two = BoardEdits.PopOutAutomationId(boards, "p-b-2-1");
        Assert.Equal("b-1/StandingPanel1", one);
        Assert.Equal("b-2/StandingPanel1", two);
        Assert.NotEqual(one, two);
        Assert.Null(BoardEdits.PopOutAutomationId(boards, "p-gone"));
    }

    [Fact]
    public void TheAnchorIsTheFirstPanelsSourceThatIsStillOn()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var alt = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
        var board = new BoardDef("b", "b",
        [
            new PanelDef("p-1", PanelType.MyAccounts, new PanelSize(6), new PanelSettings(Clan.Slug, Stat: "value")),
            new PanelDef("p-2", PanelType.Standing, new PanelSize(3), new PanelSettings(Clan.Slug, SourceId: "s-gone")),
            new PanelDef("p-3", PanelType.Race, new PanelSize(6), new PanelSettings(Clan.Slug, SourceIds: new List<string> { alt.Id, main.Id })),
        ]);

        Assert.Equal(alt.Id, BoardEdits.AnchorSourceId(board, [main, alt], [Installed(Clan)]));
        Assert.Equal(main.Id, BoardEdits.AnchorSourceId(board, [main, alt with { Enabled = false }], [Installed(Clan)]));
        Assert.Equal(main.Id, BoardEdits.AnchorSourceId(board with { Panels = [] }, [alt, main], [Installed(Clan)]));
        Assert.Null(BoardEdits.AnchorSourceId(board with { Panels = [] }, [alt], [Installed(Clan)]));
    }

    [Theory]
    [InlineData(PanelType.MyAccounts)]
    [InlineData(PanelType.Records)]
    public void ARecipeOnlyPanelAnchorsBeforeAGroupList(PanelType type)
    {
        var own = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Mine);
        var top = SourceOf("s-00000002", TopList, null, SourceRole.Watch);
        var board = new BoardDef("b", "Battle",
        [
            new PanelDef("p-1", PanelType.Top, new PanelSize(6), new PanelSettings(TopList.Slug, SourceId: top.Id)),
            new PanelDef("p-2", type, new PanelSize(6), new PanelSettings(Clan.Slug, Stat: "value")),
        ]);

        Assert.Equal(own.Id, BoardEdits.AnchorSourceId(board, [top, own], [Installed(Clan), Installed(TopList)]));
    }

    [Theory]
    [InlineData(true, true, true, true, "s-own")]
    [InlineData(false, true, true, true, "s-main")]
    [InlineData(true, false, true, true, "s-main")]
    [InlineData(false, true, false, true, "s-top")]
    [InlineData(false, true, false, false, null)]
    public void AnchorFallbackSkipsDisabledAndMissingRecipes(bool ownEnabled, bool ownInstalled, bool mainEnabled, bool topEnabled, string? expected)
    {
        var own = SourceOf("s-own", Clan, "CCGP", SourceRole.Mine) with { Enabled = ownEnabled };
        var main = SourceOf("s-main", Profile, null, SourceRole.Main) with { Enabled = mainEnabled };
        var top = SourceOf("s-top", TopList, null, SourceRole.Watch) with { Enabled = topEnabled };
        var board = new BoardDef("b", "b",
        [
            new PanelDef("p-top", PanelType.Top, new PanelSize(6), new PanelSettings(TopList.Slug, SourceId: top.Id)),
            new PanelDef("p-own", PanelType.MyAccounts, new PanelSize(6), new PanelSettings(Clan.Slug, Stat: "value")),
        ]);
        var installed = new[] { Installed(TopList), Installed(Profile), Installed(Clan) }
            .Where(item => ownInstalled || item.Recipe.Slug != Clan.Slug).ToList();

        Assert.Equal(expected, BoardEdits.AnchorSourceId(board, [top, main, own], installed));
    }

    [Fact]
    public void AnExplicitOwnSourceWinsOverAnEarlierGroupListAndRecipeOnlyPanel()
    {
        var own = SourceOf("s-own", Clan, "CCGP", SourceRole.Mine);
        var profile = SourceOf("s-profile", Profile, null, SourceRole.Mine);
        var top = SourceOf("s-top", TopList, null, SourceRole.Watch);
        var board = new BoardDef("b", "b",
        [
            new PanelDef("p-top", PanelType.Top, new PanelSize(6), new PanelSettings(TopList.Slug, SourceId: top.Id)),
            new PanelDef("p-profile", PanelType.Records, new PanelSize(6), new PanelSettings(Profile.Slug, Stat: "diamonds")),
            new PanelDef("p-own", PanelType.Standing, new PanelSize(6), new PanelSettings(Clan.Slug, SourceId: own.Id)),
        ]);

        Assert.Equal(own.Id, BoardEdits.AnchorSourceId(board, [top, profile, own], [Installed(TopList), Installed(Profile), Installed(Clan)]));
    }

    [Fact]
    public void ABattleStarterWithNoOwnSourceCanStillAnchorItsTopList()
    {
        var recipe = Profile with { Period = Clan.Period };
        var top = SourceOf("s-top", TopList, null, SourceRole.Watch);
        var installed = new[] { Installed(recipe, "diamonds"), Installed(TopList) };
        var starter = StarterBoards.Build(installed, [top], StarterBoards.Battle);

        Assert.Equal(BoardEmpty.None, starter.Empty);
        Assert.Contains(starter.Panels, panel => panel.Type == PanelType.MyAccounts);
        Assert.Equal(top.Id, BoardEdits.AnchorSourceId(BoardDefs.FromStarter(starter, freshIds: false), [top], installed));
    }

    [Fact]
    public void DoneHasSomethingToSaveOnlyWhenTheBoardIsDrawnDifferently()
    {
        var board = BoardOf("b", PanelType.Standing, PanelType.Race);

        Assert.False(BoardEdits.Changed(board, board with { Panels = [.. board.Panels] }));
        Assert.True(BoardEdits.Changed(board, BoardEdits.MoveBy(board, "p-b-2", -1)));
        Assert.True(BoardEdits.Changed(board, BoardEdits.Resize(board, "p-b-1", new PanelSize(6, Tall: true))));
        Assert.True(BoardEdits.Changed(board, board with { Name = "Other" }));

        var popped = BoardEdits.PopOut(board, "p-b-1", new PopOutRect(1, 2, 300, 200));
        Assert.False(BoardEdits.Changed(popped, BoardEdits.PopOut(popped, "p-b-1", new PopOutRect(50, 60, 400, 300))));
    }

    [Fact]
    public void DoneComparesTheDraftWithTheBoardAsEditingBegan()
    {
        // The following starter is rebuilt from your sources while you edit: a clan added in Setup adds a panel.
        var atEdit = BoardOf("b-starter", PanelType.Standing);
        IReadOnlyList<BoardDef> rebuilt = [BoardOf("b-starter", PanelType.Standing, PanelType.Race)];

        Assert.Same(rebuilt, BoardEdits.Finish(rebuilt, atEdit, atEdit with { Panels = [.. atEdit.Panels] }));

        var resized = BoardEdits.Resize(atEdit, "p-b-starter-1", new PanelSize(PanelSize.Wide));
        Assert.Equal(new[] { resized }, BoardEdits.Finish(rebuilt, atEdit, resized));

        IReadOnlyList<BoardDef> gone = [BoardOf("b-other")];
        Assert.Same(gone, BoardEdits.Finish(gone, atEdit, resized));
    }

    [Fact]
    public void DoneKeepsAChangedDraftOfAFollowingTabWhoseStarterWentEmptyWhileEditing()
    {
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);
        var ticked = StarterBoards.All([Installed(Profile, "diamonds")], [profile]);
        var atEdit = Assert.Single(Following.Shown(null, ticked));
        var draft = BoardEdits.RemovePanel(atEdit, atEdit.Panels[1].Id);

        // Every stat unticked in Setup while editing: Alts has nothing to show, so it isn't among the boards now.
        var unticked = StarterBoards.All([Installed(Profile)], [profile]);
        var boards = Following.Shown(null, unticked);
        Assert.DoesNotContain(boards, b => b.Id == atEdit.Id);

        // The change is kept as a board of its own, and shows once saved.
        var finished = BoardEdits.Finish(boards, atEdit, draft);
        var kept = Assert.Single(finished, b => b.Id == atEdit.Id);
        Assert.Null(kept.Follows);
        Assert.Equal(draft.Panels, kept.Panels);
        var saved = Following.ToSave(null, unticked, finished);
        Assert.Equal(draft.Panels.Count, Assert.Single(Following.Shown(saved, unticked), b => b.Id == atEdit.Id).Panels.Count);

        // Every panel removed: still kept, a board that says it has no panels rather than a tab that vanishes.
        var emptied = Following.ToSave(null, unticked, BoardEdits.Finish(boards, atEdit, draft with { Panels = [] }));
        var shownEmpty = Assert.Single(Following.Shown(emptied, unticked), b => b.Id == atEdit.Id);
        Assert.Equal(BoardEmpty.NoPanels, Labs626.UrScore.UI.BoardText.EmptyFor(unticked, shownEmpty));

        // A draft with no change still writes nothing.
        Assert.Same(boards, BoardEdits.Finish(boards, atEdit, atEdit with { Panels = [.. atEdit.Panels] }));
    }

    [Fact]
    public void AfterARemoveFocusGoesToTheNextPanelElseThePreviousElseNone()
    {
        var board = BoardOf("b", PanelType.Standing, PanelType.Race, PanelType.Top);

        Assert.Equal("p-b-2", BoardEdits.FocusAfterRemove(board, "p-b-1"));
        Assert.Equal("p-b-3", BoardEdits.FocusAfterRemove(board, "p-b-2"));
        Assert.Equal("p-b-2", BoardEdits.FocusAfterRemove(board, "p-b-3"));
        Assert.Null(BoardEdits.FocusAfterRemove(BoardOf("b", PanelType.Standing), "p-b-1"));
        Assert.Null(BoardEdits.FocusAfterRemove(board, "p-gone"));

        // Focus goes to a panel's ⋯, never to another Remove: a popped-out panel's slot has no ⋯, so it is passed over.
        var rect = new PopOutRect(10, 10, 360, 300);
        var four = BoardOf("b", PanelType.Standing, PanelType.Race, PanelType.Top, PanelType.Records);
        Assert.Equal("p-b-4", BoardEdits.FocusAfterRemove(BoardEdits.PopOut(four, "p-b-3", rect), "p-b-2"));
        Assert.Equal("p-b-1", BoardEdits.FocusAfterRemove(BoardEdits.PopOut(BoardEdits.PopOut(four, "p-b-3", rect), "p-b-4", rect), "p-b-2"));
        Assert.Equal("p-b-3", BoardEdits.FocusAfterRemove(BoardEdits.PopOut(board, "p-b-2", rect), "p-b-1"));
        Assert.Null(BoardEdits.FocusAfterRemove(BoardEdits.PopOut(BoardOf("b", PanelType.Standing, PanelType.Race), "p-b-1", rect), "p-b-2"));
    }

    [Fact]
    public void APoppedOutPanelIsNotMovedOrResizedUntilItIsBack()
    {
        var board = BoardEdits.PopOut(BoardOf("b", PanelType.Standing, PanelType.Race, PanelType.Top), "p-b-2", new PopOutRect(10, 10, 360, 300));

        Assert.Same(board, BoardEdits.MoveTo(board, "p-b-2", 0));
        Assert.Same(board, BoardEdits.MoveBy(board, "p-b-2", 1));
        Assert.Same(board, BoardEdits.Resize(board, "p-b-2", new PanelSize(PanelSize.Wide, Tall: true)));

        // Its neighbours still move past it, and it can still be removed.
        Assert.Equal(new[] { "p-b-2", "p-b-1", "p-b-3" }, Ids(BoardEdits.MoveBy(board, "p-b-1", 1)));
        Assert.Equal(new[] { "p-b-1", "p-b-3" }, Ids(BoardEdits.RemovePanel(board, "p-b-2")));
    }

    [Fact]
    public void EveryPanelWithAPopOutHasAWindowExceptOneTheDraftRemoved()
    {
        var rect = new PopOutRect(10, 10, 360, 300);
        var first = BoardEdits.PopOut(BoardOf("b-1", PanelType.Standing, PanelType.Race), "p-b-1-2", rect);
        var second = BoardEdits.PopOut(BoardOf("b-2", PanelType.Top, PanelType.Records), "p-b-2-1", rect);
        IReadOnlyList<BoardDef> boards = [first, second];

        Assert.Equal(new[] { "p-b-1-2", "p-b-2-1" }, BoardEdits.PoppedOut(boards, draft: null).Select(p => p.Id).ToArray());
        Assert.Equal(new[] { "p-b-1-2", "p-b-2-1" }, BoardEdits.PoppedOut(boards, BoardEdits.MoveBy(first, "p-b-1-1", 1)).Select(p => p.Id).ToArray());
        Assert.Equal(new[] { "p-b-2-1" }, BoardEdits.PoppedOut(boards, BoardEdits.RemovePanel(first, "p-b-1-2")).Select(p => p.Id).ToArray());
    }

    [Fact]
    public void DoneKeepsEveryPopOutAsItIsNowNotAsItWasWhenEditingBegan()
    {
        var at = new PopOutRect(10, 10, 360, 300);
        var atEdit = BoardEdits.PopOut(BoardOf("b", PanelType.Standing, PanelType.Race, PanelType.Top), "p-b-1", at);

        // Returned by its window while editing, and nothing else changed: nothing to write.
        IReadOnlyList<BoardDef> returned = [BoardEdits.Return(atEdit, "p-b-1")];
        Assert.Same(returned, BoardEdits.Finish(returned, atEdit, atEdit));

        // Returned, and the draft moved another panel: the move is saved, and the return isn't undone.
        var moved = BoardEdits.MoveBy(atEdit, "p-b-3", -1);
        var saved = BoardEdits.Finish(returned, atEdit, moved);
        Assert.Equal(new[] { "p-b-1", "p-b-3", "p-b-2" }, Ids(saved[0]));
        Assert.All(saved[0].Panels, p => Assert.Null(p.PopOut));

        // Its window moved, and another opened, while editing: both kept as they are now.
        var elsewhere = new PopOutRect(900, 40, 400, 320);
        IReadOnlyList<BoardDef> now = [BoardEdits.PopOut(BoardEdits.PopOut(atEdit, "p-b-1", elsewhere), "p-b-2", at)];
        Assert.Same(now, BoardEdits.Finish(now, atEdit, atEdit));
        var resized = BoardEdits.Finish(now, atEdit, BoardEdits.Resize(atEdit, "p-b-3", new PanelSize(PanelSize.Wide)));
        Assert.Equal(new[] { elsewhere, at, null }, resized[0].Panels.Select(p => p.PopOut).ToArray());
        Assert.Equal(new PanelSize(PanelSize.Wide), resized[0].Panels[2].Size);

        // A popped-out panel removed in the draft goes, window and all.
        Assert.Equal(new[] { "p-b-2", "p-b-3" }, Ids(BoardEdits.Finish(now, atEdit, BoardEdits.RemovePanel(atEdit, "p-b-1"))[0]));
    }

    [Fact]
    public void CarryingPopOutsTakesEachPanelsFromItsBoardNow()
    {
        var rect = new PopOutRect(10, 10, 360, 300);
        var draft = BoardEdits.AddPanel(BoardOf("b", PanelType.Standing, PanelType.Race), PanelType.Top, new PanelSettings(Clan.Slug));
        IReadOnlyList<BoardDef> boards = [BoardEdits.PopOut(BoardOf("b", PanelType.Standing, PanelType.Race), "p-b-2", rect)];

        var carried = BoardEdits.CarryPopOuts(draft, boards);

        Assert.Equal(new[] { null, rect, null }, carried.Panels.Select(p => p.PopOut).ToArray());
        Assert.Same(carried, BoardEdits.CarryPopOuts(carried, boards));
        Assert.Same(draft, BoardEdits.CarryPopOuts(draft, [BoardOf("b-other")]));
    }

    [Fact]
    public void WhereTheWindowsSitIsSavedOnlyForPanelsStillOut()
    {
        var at = new PopOutRect(10, 10, 360, 300);
        var there = new PopOutRect(500, 200, 420, 310);
        IReadOnlyList<BoardDef> boards =
        [
            BoardEdits.PopOut(BoardOf("b-1", PanelType.Standing, PanelType.Race), "p-b-1-1", at),
            BoardEdits.PopOut(BoardOf("b-2", PanelType.Top), "p-b-2-1", at),
        ];

        var placed = BoardEdits.PlacePopOuts(boards, new Dictionary<string, PopOutRect> { ["p-b-2-1"] = there, ["p-b-1-2"] = there, ["p-gone"] = there });

        Assert.Equal(at, placed[0].Panels[0].PopOut);
        Assert.Null(placed[0].Panels[1].PopOut);
        Assert.Equal(there, placed[1].Panels[0].PopOut);
        Assert.Same(boards[0], placed[0]);
        Assert.Same(boards, BoardEdits.PlacePopOuts(boards, new Dictionary<string, PopOutRect> { ["p-b-1-1"] = at, ["p-b-1-2"] = there }));
    }
}
