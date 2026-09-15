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

    /// <summary>Whether a draft has anything to save: a panel added, removed, moved, resized or re-set, or a new name. Where a pop-out sits doesn't count.</summary>
    public static bool Changed(BoardDef before, BoardDef after) =>
        BoardDefs.Key(before) != BoardDefs.Key(after) || before.Name != after.Name;

    /// <summary>
    /// Done in edit mode: the boards with the draft in its board's place, when the draft is drawn differently from
    /// the board as editing began. Compared with that, not with the boards now, so an untouched draft writes nothing
    /// even when the following starter was rebuilt meanwhile (R1). Nothing to write, or no board to replace, returns
    /// <paramref name="boards"/> itself.
    /// <para>
    /// Edit mode doesn't edit pop-outs (R19): both sides take every panel's pop-out as it is now
    /// (<see cref="CarryPopOuts"/>), so a pop-out returned, moved or opened while editing is neither undone nor
    /// counted as a change.
    /// </para>
    /// </summary>
    public static IReadOnlyList<BoardDef> Finish(IReadOnlyList<BoardDef> boards, BoardDef atEdit, BoardDef draft)
    {
        var carried = CarryPopOuts(draft, boards);
        return Changed(CarryPopOuts(atEdit, boards), carried) ? Replace(boards, carried) : boards;
    }

    /// <summary>
    /// The draft with each panel's pop-out as the same panel has it on its board in <paramref name="boards"/> now; a
    /// panel that board doesn't have (one added in the draft) is not out. No such board, or nothing to carry, returns
    /// <paramref name="draft"/> itself.
    /// </summary>
    public static BoardDef CarryPopOuts(BoardDef draft, IReadOnlyList<BoardDef> boards)
    {
        if (boards.FirstOrDefault(b => b.Id == draft.Id) is not { } board) return draft;

        var now = new Dictionary<string, PopOutRect?>(StringComparer.Ordinal);
        foreach (var panel in board.Panels) now.TryAdd(panel.Id, panel.PopOut);

        var changed = false;
        var panels = draft.Panels.Select(panel =>
        {
            var current = now.GetValueOrDefault(panel.Id);
            if (panel.PopOut == current) return panel;

            changed = true;
            return panel with { PopOut = current };
        }).ToList();

        return changed ? draft with { Panels = panels } : draft;
    }

    /// <summary>
    /// The panels that have a pop-out window (R18, R20): every panel with a <c>popout</c> on every board, in board
    /// order, except one the draft being edited has removed from its board.
    /// </summary>
    public static IReadOnlyList<PanelDef> PoppedOut(IReadOnlyList<BoardDef> boards, BoardDef? draft) =>
        [.. boards.SelectMany(board => board.Panels.Where(panel =>
            panel.PopOut is not null && (draft is null || draft.Id != board.Id || draft.Panels.Any(p => p.Id == panel.Id))))];

    /// <summary>
    /// Where each pop-out window sits now, by panel id, onto the boards. Only a panel still out takes a place, so a
    /// window closing late never pops its panel back out. Nothing moved returns <paramref name="boards"/> itself.
    /// </summary>
    public static IReadOnlyList<BoardDef> PlacePopOuts(IReadOnlyList<BoardDef> boards, IReadOnlyDictionary<string, PopOutRect> places)
    {
        var changed = false;
        var placed = boards.Select(board =>
        {
            var moved = board;
            foreach (var panel in board.Panels)
            {
                if (panel.PopOut is { } at && places.TryGetValue(panel.Id, out var place) && place != at) moved = PopOut(moved, panel.Id, place);
            }

            changed |= !ReferenceEquals(moved, board);
            return moved;
        }).ToList();

        return changed ? placed : boards;
    }

    /// <summary>
    /// After a panel is removed in edit mode, whose ⋯ takes keyboard focus (R7): the nearest later panel's, else the
    /// nearest earlier one's, else none (the window then focuses + Add panel). Never a Remove, so holding Enter can't
    /// remove panel after panel; a popped-out panel's slot has no ⋯, so it is passed over.
    /// </summary>
    public static string? FocusAfterRemove(BoardDef board, string panelId)
    {
        var index = PanelIndex(board, panelId);
        if (index < 0) return null;

        var later = board.Panels.Skip(index + 1).FirstOrDefault(p => p.PopOut is null);
        var earlier = board.Panels.Take(index).LastOrDefault(p => p.PopOut is null);
        return (later ?? earlier)?.Id;
    }

    /// <summary>Which tool a resize came from, so focus goes back to it: Tall keeps the span and flips Tall; the size box picks a span.</summary>
    public static bool IsTallTick(PanelSize before, PanelSize after) => before.Span == after.Span && before.Tall != after.Tall;

    public static IReadOnlyList<BoardDef> Add(IReadOnlyList<BoardDef> boards, BoardDef board) => [.. boards, board];

    public static IReadOnlyList<BoardDef> Replace(IReadOnlyList<BoardDef> boards, BoardDef board) =>
        IndexOf(boards, board.Id) < 0 ? boards : [.. boards.Select(b => b.Id == board.Id ? board : b)];

    /// <summary>A blank name, a board that isn't there, or the name it already has changes nothing, so nothing is written (R1).</summary>
    public static IReadOnlyList<BoardDef> Rename(IReadOnlyList<BoardDef> boards, string boardId, string name)
    {
        var index = IndexOf(boards, boardId);
        return index < 0 || BoardDefs.CleanName(name) is not { } clean || string.Equals(boards[index].Name, clean, StringComparison.Ordinal)
            ? boards
            : [.. boards.Select(b => b.Id == boardId ? b with { Name = clean } : b)];
    }

    /// <summary>R13: right after the original, "name copy", new panel ids, and nothing popped out. A long name is cut to leave room for " copy".</summary>
    public static IReadOnlyList<BoardDef> Duplicate(IReadOnlyList<BoardDef> boards, string boardId)
    {
        var index = IndexOf(boards, boardId);
        if (index < 0) return boards;

        const string Copy = " copy";
        var original = boards[index];
        var room = BoardDefs.MaxNameLength - Copy.Length;
        var name = original.Name.Length > room ? original.Name[..room].TrimEnd() : original.Name;
        var copy = new BoardDef(
            BoardDefs.NewBoardId(),
            BoardDefs.CleanName(name + Copy) ?? NextName(boards),
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
    /// A popped-out panel isn't moved until it is back (R19); others still move past it.
    /// </summary>
    public static BoardDef MoveTo(BoardDef board, string panelId, int insertionIndex)
    {
        var from = PanelIndex(board, panelId);
        if (from < 0 || board.Panels[from].PopOut is not null) return board;

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

    /// <summary>Span kept within the grid. A popped-out panel isn't resized until it is back (R19).</summary>
    public static BoardDef Resize(BoardDef board, string panelId, PanelSize size)
    {
        var index = PanelIndex(board, panelId);
        return index < 0 || board.Panels[index].PopOut is not null
            ? board
            : Update(board, panelId, p => p with { Size = new PanelSize(Math.Clamp(size.Span, 1, BoardLayout.Columns), size.Tall) });
    }

    /// <summary>Settings equal to the panel's own, as a settings form closed with nothing changed builds, change nothing.</summary>
    public static BoardDef SetSettings(BoardDef board, string panelId, PanelSettings settings)
    {
        var index = PanelIndex(board, panelId);
        return index < 0 || SameSettings(board.Panels[index].Settings, settings)
            ? board
            : Update(board, panelId, p => p with { Settings = settings });
    }

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

    /// <summary>A panel's stage 1 id ("StandingPanel2") on whichever board holds it; null when no board does.</summary>
    public static string? AutomationIdOf(IReadOnlyList<BoardDef> boards, string panelId) =>
        Find(boards, panelId) is { } found ? AutomationIds(found.Board)[PanelIndex(found.Board, panelId)] : null;

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

    /// <summary>Record equality compares a race's list by instance; this compares its ids in order, with no list the same as an empty one.</summary>
    private static bool SameSettings(PanelSettings a, PanelSettings b) =>
        a with { SourceIds = null } == b with { SourceIds = null }
        && (a.SourceIds ?? Array.Empty<string>()).SequenceEqual(b.SourceIds ?? Array.Empty<string>(), StringComparer.Ordinal);

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
