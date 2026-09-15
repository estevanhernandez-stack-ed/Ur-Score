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

    [Fact]
    public void TheLastBoardCantBeDeleted()
    {
        IReadOnlyList<BoardDef> one = [BoardOf("b-1")];
        IReadOnlyList<BoardDef> two = [BoardOf("b-1"), BoardOf("b-2")];

        Assert.Same(one, BoardEdits.Delete(one, "b-1"));
        Assert.Equal(new[] { "b-2" }, BoardEdits.Delete(two, "b-1").Select(b => b.Id).ToArray());
        Assert.Same(two, BoardEdits.Delete(two, "b-9"));
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
        Assert.Equal("s-9", BoardEdits.SetSettings(board, "p-b-1", new PanelSettings(Clan.Slug, SourceId: "s-9")).Panels[0].Settings.SourceId);

        var popped = BoardEdits.PopOut(board, "p-b-2", rect);
        Assert.Equal(rect, popped.Panels[1].PopOut);
        Assert.Null(popped.Panels[0].PopOut);
        Assert.Null(BoardEdits.Return(popped, "p-b-2").Panels[1].PopOut);
        Assert.Same(board, BoardEdits.RemovePanel(board, "p-gone"));
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
        Assert.Same(board, BoardEdits.SetSettings(board, "p-b-1", new PanelSettings(Clan.Slug, SourceIds: ["s-1", "s-2"])));
        Assert.Same(board, BoardEdits.SetSettings(board, "p-gone", new PanelSettings(Clan.Slug, SourceIds: ["s-2", "s-1"])));
        Assert.Equal(new[] { "s-2", "s-1" },
            BoardEdits.SetSettings(board, "p-b-1", new PanelSettings(Clan.Slug, SourceIds: ["s-2", "s-1"])).Panels[0].Settings.SourceIds);
        Assert.NotSame(board, BoardEdits.SetSettings(board, "p-b-1", race with { Stat = "value" }));
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

    [Fact]
    public void AutomationIdsCountEachTypeInBoardOrder() =>
        Assert.Equal(
            new[] { "StandingPanel1", "RacePanel1", "StandingPanel2", "ProfileStatPanel1" },
            BoardEdits.AutomationIds(BoardOf("b", PanelType.Standing, PanelType.Race, PanelType.Standing, PanelType.ProfileStat)).ToArray());

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

        Assert.Equal(alt.Id, BoardEdits.AnchorSourceId(board, [main, alt]));
        Assert.Equal(main.Id, BoardEdits.AnchorSourceId(board, [main, alt with { Enabled = false }]));
        Assert.Equal(main.Id, BoardEdits.AnchorSourceId(board with { Panels = [] }, [alt, main]));
        Assert.Null(BoardEdits.AnchorSourceId(board with { Panels = [] }, [alt]));
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
}
