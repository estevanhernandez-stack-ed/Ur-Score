using Labs626.UrScore.Core;

namespace Labs626.UrScore.Board;

using Source = Labs626.UrScore.Core.Source;

/// <summary>
/// Every change to boards and panels (spec §9.2, §9.3), as pure functions. An edit that doesn't apply returns
/// its input, the same instance, so a caller can tell nothing changed.
/// </summary>
public static class BoardEdits
{
    /// <summary>"Board 2", "Board 3"...: the first number past the board count that no board is named.</summary>
    public static string NextName(IReadOnlyList<BoardDef> boards)
    {
        var taken = boards.Select(b => b.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var number = boards.Count + 1;
        while (taken.Contains($"Board {number}")) number++;
        return $"Board {number}";
    }

    public static IReadOnlyList<BoardDef> Add(IReadOnlyList<BoardDef> boards, BoardDef board) => [.. boards, board];

    public static IReadOnlyList<BoardDef> Replace(IReadOnlyList<BoardDef> boards, BoardDef board) =>
        IndexOf(boards, board.Id) < 0 ? boards : [.. boards.Select(b => b.Id == board.Id ? board : b)];

    public static IReadOnlyList<BoardDef> Rename(IReadOnlyList<BoardDef> boards, string boardId, string name) =>
        BoardDefs.CleanName(name) is not { } clean || IndexOf(boards, boardId) < 0
            ? boards
            : [.. boards.Select(b => b.Id == boardId ? b with { Name = clean } : b)];

    /// <summary>R13: right after the original, "name copy", new panel ids, and nothing popped out.</summary>
    public static IReadOnlyList<BoardDef> Duplicate(IReadOnlyList<BoardDef> boards, string boardId)
    {
        var index = IndexOf(boards, boardId);
        if (index < 0) return boards;

        var original = boards[index];
        var copy = new BoardDef(
            BoardDefs.NewBoardId(),
            BoardDefs.CleanName($"{original.Name} copy") ?? BoardEdits.NextName(boards),
            [.. original.Panels.Select(p => p with { Id = BoardDefs.NewPanelId(), PopOut = null })]);

        var list = boards.ToList();
        list.Insert(index + 1, copy);
        return list;
    }

    /// <summary>R11: the last board stays.</summary>
    public static IReadOnlyList<BoardDef> Delete(IReadOnlyList<BoardDef> boards, string boardId) =>
        boards.Count <= 1 || IndexOf(boards, boardId) < 0 ? boards : [.. boards.Where(b => b.Id != boardId)];

    public static BoardDef AddPanel(BoardDef board, PanelType type, PanelSettings settings) =>
        board with { Panels = [.. board.Panels, new PanelDef(BoardDefs.NewPanelId(), type, BoardDefs.DefaultSize(type), settings)] };

    public static BoardDef RemovePanel(BoardDef board, string panelId) =>
        PanelIndex(board, panelId) < 0 ? board : board with { Panels = [.. board.Panels.Where(p => p.Id != panelId)] };

    /// <summary>
    /// Drops the panel before the panel now at <paramref name="insertionIndex"/>, counting the panel itself, so
    /// a drop on its own left or right half changes nothing (the index <see cref="BoardLayout.DropIndex"/> gives).
    /// </summary>
    public static BoardDef MoveTo(BoardDef board, string panelId, int insertionIndex)
    {
        var from = PanelIndex(board, panelId);
        if (from < 0) return board;

        var to = Math.Clamp(insertionIndex, 0, board.Panels.Count);
        if (to > from) to--;
        if (to == from) return board;

        var list = board.Panels.ToList();
        var panel = list[from];
        list.RemoveAt(from);
        list.Insert(to, panel);
        return board with { Panels = list };
    }

    /// <summary>Move earlier (-1) or later (+1), for the buttons beside the drag handle (R7).</summary>
    public static BoardDef MoveBy(BoardDef board, string panelId, int delta)
    {
        var from = PanelIndex(board, panelId);
        if (from < 0 || delta == 0) return board;

        return MoveTo(board, panelId, delta > 0 ? from + delta + 1 : from + delta);
    }

    public static BoardDef Resize(BoardDef board, string panelId, PanelSize size) =>
        Update(board, panelId, p => p with { Size = new PanelSize(Math.Clamp(size.Span, 1, BoardLayout.Columns), size.Tall) });

    public static BoardDef SetSettings(BoardDef board, string panelId, PanelSettings settings) =>
        Update(board, panelId, p => p with { Settings = settings });

    public static BoardDef PopOut(BoardDef board, string panelId, PopOutRect rect) =>
        Update(board, panelId, p => p with { PopOut = rect });

    public static BoardDef Return(BoardDef board, string panelId) =>
        Update(board, panelId, p => p with { PopOut = null });

    public static (BoardDef Board, PanelDef Panel)? Find(IReadOnlyList<BoardDef> boards, string panelId)
    {
        foreach (var board in boards)
        {
            if (board.Panels.FirstOrDefault(p => p.Id == panelId) is { } panel) return (board, panel);
        }

        return null;
    }

    /// <summary>Stage 1's ids, per board: "StandingPanel1", "StandingPanel2", "RacePanel1"... in board order.</summary>
    public static IReadOnlyList<string> AutomationIds(BoardDef board)
    {
        var counts = new Dictionary<PanelType, int>();
        var ids = new List<string>(board.Panels.Count);
        foreach (var panel in board.Panels)
        {
            counts[panel.Type] = counts.GetValueOrDefault(panel.Type) + 1;
            ids.Add($"{panel.Type}Panel{counts[panel.Type]}");
        }

        return ids;
    }

    /// <summary>The source the top bar's period line follows: the first panel's source that is still on, else the main.</summary>
    public static string? AnchorSourceId(BoardDef board, IReadOnlyList<Source> sources)
    {
        var enabled = sources.Where(s => s.Enabled).ToList();
        foreach (var panel in board.Panels)
        {
            var ids = new[] { panel.Settings.SourceId, panel.Settings.ToSourceId }.Concat(panel.Settings.SourceIds ?? Array.Empty<string>());
            foreach (var id in ids)
            {
                if (enabled.FirstOrDefault(s => s.Id == id) is { } found) return found.Id;
            }
        }

        return enabled.FirstOrDefault(s => s.Role == SourceRole.Main)?.Id;
    }

    private static BoardDef Update(BoardDef board, string panelId, Func<PanelDef, PanelDef> change) =>
        PanelIndex(board, panelId) < 0 ? board : board with { Panels = [.. board.Panels.Select(p => p.Id == panelId ? change(p) : p)] };

    private static int IndexOf(IReadOnlyList<BoardDef> boards, string boardId)
    {
        for (var i = 0; i < boards.Count; i++)
        {
            if (boards[i].Id == boardId) return i;
        }

        return -1;
    }

    private static int PanelIndex(BoardDef board, string panelId)
    {
        for (var i = 0; i < board.Panels.Count; i++)
        {
            if (board.Panels[i].Id == panelId) return i;
        }

        return -1;
    }
}
