using Labs626.UrScore.Board;

namespace UrScore.Tests;

/// <summary>BC6: every board change can be taken back, per board, for the session.</summary>
public class BoardUndoTests
{
    private static BoardDef Board(string id, int panels) =>
        new(id, id, [.. Enumerable.Range(0, panels).Select(i => new PanelDef($"p-{i}", PanelType.Standing, new PanelSize(), new PanelSettings("r")))]);

    [Fact]
    public void TheLastChangeComesBackFirst()
    {
        var undo = new BoardUndo();
        undo.Push(Board("b-1", 1), "Added Race");
        undo.Push(Board("b-1", 2), "Moved Race");

        Assert.Equal("Moved Race", undo.Pop("b-1")!.What);
        Assert.Equal("Added Race", undo.Pop("b-1")!.What);
        Assert.Null(undo.Pop("b-1"));
    }

    [Fact]
    public void EachBoardHasItsOwnHistory()
    {
        var undo = new BoardUndo();
        undo.Push(Board("b-1", 1), "one");
        undo.Push(Board("b-2", 1), "two");

        Assert.Equal("two", undo.Peek("b-2")!.What);
        Assert.Equal(1, undo.Count("b-1"));
        undo.Clear("b-2");
        Assert.Equal(0, undo.Count("b-2"));
        Assert.Equal(1, undo.Count("b-1"));
    }

    [Fact]
    public void ItKeepsTheLatestTwenty()
    {
        var undo = new BoardUndo();
        for (var i = 0; i < 25; i++) undo.Push(Board("b-1", 1), $"step {i}");

        Assert.Equal(BoardUndo.Depth, undo.Count("b-1"));
        Assert.Equal("step 24", undo.Peek("b-1")!.What);
    }

    [Fact]
    public void PeekTakesNothing()
    {
        var undo = new BoardUndo();
        undo.Push(Board("b-1", 1), "one");
        undo.Peek("b-1");

        Assert.Equal(1, undo.Count("b-1"));
    }

    [Fact]
    public void WhileArrangingOnlyTheDraftIsUndone()
    {
        var draft = new BoardUndo();
        var saved = new BoardUndo();
        saved.Push(Board("b-1", 1), "an earlier change");

        Assert.Same(draft, BoardUndo.Target(arranging: true, draft, saved));
        Assert.Null(BoardUndo.Target(arranging: true, draft, saved).Pop("b-1"));
        Assert.Equal(1, saved.Count("b-1"));
        Assert.Same(saved, BoardUndo.Target(arranging: false, draft, saved));
    }
}
