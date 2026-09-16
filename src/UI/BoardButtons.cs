namespace Labs626.UrScore.UI;

/// <summary>Which of the board's buttons, tabs and tab menu items take a press right now.</summary>
public readonly record struct BoardButtonStates(
    bool StartStop,
    bool TestNow,
    bool EmptyState,
    bool DeleteBoard = false,
    bool Tabs = true,
    bool AddBoard = true,
    bool RenameBoard = true,
    bool DuplicateBoard = true,
    bool EditBoard = true,
    bool AddPanel = false,
    bool Done = false);

/// <summary>
/// The board's buttons are disabled for exactly the time a press would be ignored, and never longer. Stop is
/// never held up by a Test now read: stopping only ends the timed loops, and the read goes on with its own token.
/// </summary>
public static class BoardButtons
{
    /// <param name="loaded">The score book has been read; nothing runs before then.</param>
    /// <param name="running">The watch is running, so Start/Stop reads Stop.</param>
    /// <param name="starting">Start was pressed and is still asking RoRoRo for accounts.</param>
    /// <param name="testing">A Test now read is in flight.</param>
    /// <param name="importing">The empty state's Import recipe… is in flight.</param>
    /// <param name="boards">
    /// How many boards there are; the last one can't be deleted (R11). The default of 1 is for a press guard that
    /// only asks about Start/Stop or Test now, and it leaves Delete off; anything that reads DeleteBoard passes the real count.
    /// </param>
    /// <param name="editing">
    /// Edit mode is on (R8): one draft at a time, so the tabs, + Board and the tab menu wait for Done, and Edit board
    /// gives way to + Add panel and Done. Reading, Stop and the empty state don't wait.
    /// </param>
    public static BoardButtonStates For(bool loaded, bool running, bool starting, bool testing, bool importing, int boards = 1, bool editing = false) => new(
        StartStop: loaded && !starting && (running || !testing),
        TestNow: loaded && !starting && !testing,
        EmptyState: !importing,
        DeleteBoard: boards > 1 && !editing,
        Tabs: !editing,
        AddBoard: !editing,
        RenameBoard: !editing,
        DuplicateBoard: !editing,
        EditBoard: !editing,
        AddPanel: editing,
        Done: editing);

    /// <summary>
    /// Whether the board starts reading by itself as it opens (plan A33). Off unless you turned it on, and then only
    /// when pressing Start yourself would have done something: the book is read, nothing runs yet, a recipe is
    /// installed, a source is on, and Setup isn't about to open on a recipe that has no source.
    /// <para>
    /// It ends with the Start button's own gate rather than restating it, so the two can't drift apart and leave the
    /// board starting itself while the button for the same thing is disabled.
    /// </para>
    /// </summary>
    public static bool StartsOnOpen(bool startOnOpen, bool loaded, bool running, int installed, bool anySourceOn, bool firstRunPage) =>
        startOnOpen && !running && installed > 0 && anySourceOn && !firstRunPage
        && For(loaded, running, starting: false, testing: false, importing: false).StartStop;
}
