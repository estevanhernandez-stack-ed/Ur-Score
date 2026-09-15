namespace Labs626.UrScore.UI;

/// <summary>Which of the board's buttons take a press right now.</summary>
public readonly record struct BoardButtonStates(bool StartStop, bool TestNow, bool EmptyState, bool DeleteBoard = false);

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
    /// <param name="boards">How many boards there are; the last one can't be deleted (R11).</param>
    public static BoardButtonStates For(bool loaded, bool running, bool starting, bool testing, bool importing, int boards = 1) => new(
        StartStop: loaded && !starting && (running || !testing),
        TestNow: loaded && !starting && !testing,
        EmptyState: !importing,
        DeleteBoard: boards > 1);
}
