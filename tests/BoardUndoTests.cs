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
    public void AnUndoRestoresThePanelsButKeepsTheNameAndPopOutsAsTheyAreNow()
    {
        // The snapshot: two panels, the second one wide, under the old name, nothing out.
        var before = Board("b-1", 2);
        before = before with { Panels = [before.Panels[0], before.Panels[1] with { Size = new PanelSize(PanelSize.Wide) }] };
        var step = new UndoStep(before, "Resized Race");

        // Now: renamed since (not undoable, spec 5.1), a third panel added, and the first panel popped out.
        var rect = new PopOutRect(10, 10, 360, 300);
        var now = Board("b-1", 3) with { Name = "Battle" };
        now = now with { Panels = [now.Panels[0] with { PopOut = rect }, now.Panels[1], now.Panels[2]] };
        IReadOnlyList<BoardDef> boards = [now, Board("b-2", 1)];

        var restored = BoardUndo.Restorable(step, now, boards);

        Assert.Equal("Battle", restored.Name);
        Assert.Equal(new[] { "p-0", "p-1" }, restored.Panels.Select(p => p.Id).ToArray());
        Assert.Equal(new PanelSize(PanelSize.Wide), restored.Panels[1].Size);
        Assert.Equal(new[] { rect, null }, restored.Panels.Select(p => p.PopOut).ToArray());
        Assert.Equal("b-1", restored.Id);
    }

    [Fact]
    public void DoneCountsItsStepsOnlyWhileTheDraftDiffers()
    {
        // Moved right, then back left: two steps, nothing changed, so Done shows no count (and Cancel doesn't ask).
        var atEdit = Board("b-1", 2);
        var history = new BoardUndo();
        history.Push(atEdit, "Moved p-0");
        history.Push(atEdit with { Panels = [atEdit.Panels[1], atEdit.Panels[0]] }, "Moved p-0");

        Assert.Equal(0, BoardUndo.DoneCount(atEdit, atEdit with { Panels = [.. atEdit.Panels] }, history));
        Assert.Equal(2, BoardUndo.DoneCount(atEdit, Board("b-1", 1), history));
        Assert.Equal(0, BoardUndo.DoneCount(null, Board("b-1", 1), history));
        Assert.Equal(0, BoardUndo.DoneCount(atEdit, null, history));
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
