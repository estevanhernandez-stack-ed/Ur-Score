namespace Labs626.UrScore.Board;

/// <summary>One change that can be taken back: the board as it was before it, and what it was, for the toast.</summary>
public sealed record UndoStep(BoardDef Before, string What);

/// <summary>
/// BC6: board-level undo. A snapshot of the board is pushed before every change that goes through ChangeBoard; boards are
/// immutable records, so a snapshot is a reference. One history per board, the latest <see cref="Depth"/> steps, for
/// this session only. Pop-out-only changes are never pushed (the caller passes no label for them).
/// </summary>
public sealed class BoardUndo
{
    public const int Depth = 20;

    private readonly Dictionary<string, List<UndoStep>> _stacks = new(StringComparer.Ordinal);

    public void Push(BoardDef before, string what)
    {
        if (!_stacks.TryGetValue(before.Id, out var stack)) _stacks[before.Id] = stack = [];
        stack.Add(new UndoStep(before, what));
        if (stack.Count > Depth) stack.RemoveAt(0);
    }

    public UndoStep? Peek(string boardId) => _stacks.TryGetValue(boardId, out var stack) && stack.Count > 0 ? stack[^1] : null;

    public UndoStep? Pop(string boardId)
    {
        if (Peek(boardId) is not { } step) return null;
        _stacks[boardId].RemoveAt(_stacks[boardId].Count - 1);
        return step;
    }

    public int Count(string boardId) => _stacks.TryGetValue(boardId, out var stack) ? stack.Count : 0;

    public void Clear(string boardId) => _stacks.Remove(boardId);

    public void ClearAll() => _stacks.Clear();

    /// <summary>Which history an undo reads: the draft's while arranging, whatever the saved one holds (Review Focus 5).</summary>
    public static BoardUndo Target(bool arranging, BoardUndo draft, BoardUndo saved) => arranging ? draft : saved;

    /// <summary>
    /// The count Done (n) shows: the draft's steps, but only while the draft is drawn differently from the board as
    /// arranging began (<see cref="BoardEdits.Changed"/>). A panel moved right then back left is two steps and no change,
    /// so Done shows none, as Cancel then leaves without asking (BC5, BC6).
    /// </summary>
    public static int DoneCount(BoardDef? atEdit, BoardDef? draft, BoardUndo history) =>
        atEdit is { } b && draft is { } d && BoardEdits.Changed(b, d) ? history.Count(d.Id) : 0;

    /// <summary>
    /// What an undo of a saved board puts back: the snapshot's panels, under the board's name as it is
    /// (<paramref name="now"/>) and with each panel's pop-out as <paramref name="boards"/> has it. A rename and a pop-out
    /// are not undoable changes (spec §5.1), so undoing a panel change must not also take back either.
    /// </summary>
    public static BoardDef Restorable(UndoStep step, BoardDef now, IReadOnlyList<BoardDef> boards) =>
        BoardEdits.CarryPopOuts(step.Before with { Name = now.Name }, boards);
}
