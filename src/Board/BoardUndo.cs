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
}
