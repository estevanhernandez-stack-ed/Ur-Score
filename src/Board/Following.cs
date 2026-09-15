namespace Labs626.UrScore.Board;

/// <summary>
/// The starter tabs that follow your sources (D2–D4). A board with <see cref="BoardDef.Follows"/> is rebuilt from its
/// starter on every read and shows only while the starter has panels. The first change to that board writes it as it
/// is, and it follows no more; every other following tab keeps following.
/// </summary>
public static class Following
{
    /// <summary>
    /// The boards the window shows, in order. With nothing saved every starter follows. When nothing would show, one
    /// following board carries the first-run empty state (D4).
    /// </summary>
    public static IReadOnlyList<BoardDef> Shown(IReadOnlyList<BoardDef>? saved, IReadOnlyList<StarterBoard> starters)
    {
        var all = All(saved, starters);
        var visible = all.Where(b => b.Follows is null || b.Panels.Count > 0).ToList();
        if (visible.Count > 0) return visible;

        var followed = starters.Where(s => all.Any(b => b.Follows == StarterBoards.KeyOf(s.Name))).ToList();
        return [BoardDefs.Following(StarterBoards.EmptyState(followed.Count > 0 ? followed : starters))];
    }

    /// <summary>
    /// What <c>boards.json</c> holds after <paramref name="edited"/>, a changed copy of <see cref="Shown"/>. A following board
    /// drawn as its starter draws it now stays a following entry, with no panels; a changed one (a panel, a size, a
    /// setting, a name, a pop-out) is written as it is and follows no more, and so is one handed over following
    /// nothing (a draft kept after its starter went empty, <see cref="BoardEdits.Finish"/>). A following board that was
    /// showing and is gone was deleted; one hidden because its starter had nothing to show was never on screen, and
    /// keeps following.
    /// </summary>
    public static IReadOnlyList<BoardDef> ToSave(IReadOnlyList<BoardDef>? saved, IReadOnlyList<StarterBoard> starters, IReadOnlyList<BoardDef> edited)
    {
        var all = All(saved, starters);
        var following = all.Where(b => b.Follows is not null).ToDictionary(b => b.Id, StringComparer.Ordinal);
        var shown = Shown(saved, starters).Select(b => b.Id).ToHashSet(StringComparer.Ordinal);

        var result = edited
            .Select(board => board.Follows is not null && following.TryGetValue(board.Id, out var built) && !BoardEdits.Changed(built, board)
                ? Entry(built)
                : board with { Follows = null })
            .ToList();

        foreach (var hidden in all.Where(b => b.Follows is not null && !shown.Contains(b.Id)))
        {
            if (result.All(b => b.Id != hidden.Id)) result.Add(Entry(hidden));
        }

        return result;
    }

    /// <summary>Saved boards with each following entry rebuilt from its starter; one whose id a saved board already has is dropped.</summary>
    private static IReadOnlyList<BoardDef> All(IReadOnlyList<BoardDef>? saved, IReadOnlyList<StarterBoard> starters)
    {
        if (saved is null) return [.. starters.Select(BoardDefs.Following)];

        var taken = saved.Where(b => b.Follows is null).Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
        var boards = new List<BoardDef>();
        foreach (var board in saved)
        {
            if (board.Follows is null)
            {
                boards.Add(board);
                continue;
            }

            if (StarterBoards.Named(starters, board.Follows) is not { } starter) continue;

            var built = BoardDefs.Following(starter);
            if (taken.Add(built.Id)) boards.Add(built);
        }

        return boards;
    }

    /// <summary>A following board as <c>boards.json</c> keeps it: its id, its starter's name, and no panels.</summary>
    private static BoardDef Entry(BoardDef built) => new(built.Id, built.Name, [], built.Follows);
}
