using System.IO;
using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

/// <summary>
/// What the board window is in the middle of, or last asked for, which the state line says over what reading is doing. The line
/// is worked out from this on every redraw, never written once, so a redraw in the middle of a wait can't wipe what it said
/// (backlog S1-14.3, S1-14.5).
/// </summary>
/// <param name="Starting">Start was pressed and is asking RoRoRo for your accounts, for up to <c>AppServices.AccountsWait</c>.</param>
/// <param name="Testing">A Test now read is in flight.</param>
/// <param name="AskedReadAt">When you last asked for a read by hand: Test now, or Setup reading a source once.</param>
/// <param name="StoppedAt">When reading last stopped; null if it never ran.</param>
/// <param name="Failed">Start, Stop or Test now went wrong in a way nothing else names. Said until the next press.</param>
public readonly record struct BoardActivity(
    bool Starting = false, bool Testing = false, DateTimeOffset? AskedReadAt = null, DateTimeOffset? StoppedAt = null, bool Failed = false);

/// <summary>The board window's own lines: the top line, the state line, the empty states (spec §8).</summary>
public static class BoardText
{
    public const string HostDown = "RoRoRo is not running. Still reading and keeping your scores; nothing is being sent.";

    /// <summary>
    /// The same news while Ur Score is stopped. "Still reading" was true only while running, so a stopped board said it
    /// was reading on the detail line and that it was not on the state line, on the same screen.
    /// </summary>
    public const string HostDownStopped = "RoRoRo wasn't running at the last read, so nothing was sent.";

    public const string Starting = "Starting. Asking RoRoRo for your accounts…";

    public const string ReadingOnce = "Reading every source once…";

    public const string Unexpected = "Something unexpected went wrong.";

    /// <summary>The detail under <see cref="Unexpected"/>. The exception itself goes to the trail, never onto the board.</summary>
    public const string UnexpectedDetail = "Setup › Diagnostics has the details.";

    /// <summary>"AutumnBattle · ends in 3d · next read in 2m", or "Reads every 30m · next read in 12m" without a period.</summary>
    /// <summary>When the top line's own period ends, for the clock ticking beside it, or null with no period or end.</summary>
    public static DateTimeOffset? TopEnds(LiveBoard live, string? anchorSourceId) =>
        Anchor(live, anchorSourceId) is { } anchor ? live.SnapshotOf(anchor.Source.Id)?.Period?.Ends : null;

    /// <summary>The source the top line speaks for: the board's anchor, else the first switched-on source that has a recipe.</summary>
    private static (Source Source, Recipe Recipe)? Anchor(LiveBoard live, string? anchorSourceId)
    {
        var source = live.FindSource(anchorSourceId) ?? live.Sources.FirstOrDefault(s => s.Enabled);
        return source is not null && live.FindRecipe(source.Recipe)?.Recipe is { } recipe ? (source, recipe) : null;
    }

    public static string TopLine(LiveBoard live, string? anchorSourceId)
    {
        if (Anchor(live, anchorSourceId) is not { } anchor) return "";
        var (source, recipe) = anchor;

        DateTimeOffset? next = live.Running && live.LastRead.TryGetValue(source.Id, out var last)
            ? last.AddSeconds(recipe.EffectiveEverySeconds)
            : null;

        if (live.SnapshotOf(source.Id)?.Period is { } period) return PanelText.PeriodLine(period, live.Now, next);

        var every = $"Reads every {StatText.Span(TimeSpan.FromSeconds(recipe.EffectiveEverySeconds))}";
        return next is { } due ? $"{every} · {PanelText.NextRead(due, live.Now)}" : every;
    }

    public static string StateLine(LiveBoard live, bool everStarted, BoardActivity activity = default)
    {
        // Plan A41: whatever else this line says, it says so while any panel is drawing numbers from the score book.
        var remembered = live.OldestRemembered is { } oldest ? " " + RememberedLine(oldest, live.Now) : "";

        if (activity.Failed) return Unexpected + remembered;
        if (activity.Starting) return Starting + remembered;
        if (activity.Testing) return ReadingOnce + remembered;

        if (!live.Running) return (everStarted ? "Stopped." : "Not started.") + LastReadNews(live, activity) + remembered;

        var enabled = live.Sources.Where(s => s.Enabled).ToList();
        if (enabled.Count == 0) return "Running, with nothing to read yet." + remembered;

        foreach (var source in enabled)
        {
            // The reading from this session only: a remembered snapshot is not a state Ur Score is in (plan A38).
            if (live.LiveOf(source.Id) is { } snapshot && !Healthy(snapshot.State))
            {
                // The sentence rides this branch too (review I1). A read that is failing is exactly when the numbers
                // beside it are stale, so this is the last branch that may drop the one mark nobody has to remember.
                return $"{live.SourceName(source)}: {DiagnosticsModel.StateText(snapshot.State)}" + remembered;
            }
        }

        return (enabled.Count == 1 ? "Reading 1 source." : $"Reading {enabled.Count} sources.") + remembered;
    }

    /// <summary>
    /// Backlog S1-14.5: what the read you asked for while reading was stopped found, after "Not started." or "Stopped.". Only a
    /// read asked for since the last Stop, and only the sources it read, so pressing Stop still says "Stopped." alone and a timed
    /// read that lands just after it isn't mistaken for an answer. A source in trouble is named first, as the running line does;
    /// else what every source found, when they found the same; else how many answered.
    /// </summary>
    private static string LastReadNews(LiveBoard live, BoardActivity activity)
    {
        if (activity.AskedReadAt is not { } asked || (activity.StoppedAt is { } stopped && asked < stopped)) return "";

        var read = new List<(Source Source, WatchState State)>();
        foreach (var source in live.Sources.Where(s => s.Enabled))
        {
            if (live.LiveOf(source.Id) is { } snapshot && live.LastRead.TryGetValue(source.Id, out var at) && at >= asked)
            {
                read.Add((source, snapshot.State));
            }
        }

        if (read.Count == 0) return "";

        foreach (var (source, state) in read)
        {
            if (!Healthy(state)) return $" Last read of {live.SourceName(source)}: {DiagnosticsModel.StateText(state)}";
        }

        var found = read.Select(r => DiagnosticsModel.PastStateText(r.State)).Distinct(StringComparer.Ordinal).ToList();
        return found.Count == 1 ? $" Last read: {found[0]}" : $" Last read: {read.Count} sources answered.";
    }

    /// <summary>The state line before the score book is read: while it is being read, and once it couldn't be (S1-14.2).</summary>
    public static string BookStateLine(bool unread) => unread ? "Your score book couldn't be read." : "Reading your score book…";

    /// <summary>
    /// Why the score book couldn't be read, in plain words (S1-14.2), the way <see cref="BoardsNotSaved"/> says a board that
    /// wasn't saved: an unknown IO reason is Windows' own sentence, and anything else only that it was unexpected. The exception
    /// itself goes to the trail.
    /// </summary>
    public static string BookUnread(Exception ex)
    {
        const int SharingViolation = unchecked((int)0x80070020), LockViolation = unchecked((int)0x80070021);

        return ex switch
        {
            UnauthorizedAccessException => "Windows didn't let Ur Score read its data folder.",
            IOException { HResult: SharingViolation or LockViolation } => "Another program has a score book file open. Close it, then press Try again.",
            IOException => ex.Message,
            _ => $"{Unexpected} {UnexpectedDetail}",
        };
    }

    /// <summary>
    /// Plan A41: how old the numbers on screen are, from the OLDEST reading behind any of them, so the line can never
    /// sound fresher than the worst thing it covers.
    /// </summary>
    public static string RememberedLine(DateTimeOffset oldest, DateTimeOffset now) =>
        $"The numbers on screen are the last ones Ur Score read, from {StatText.Span(now - oldest)} ago.";

    public static string DetailLine(LiveBoard live, string? budgetWarning) =>
        live.Snapshots.Values.Any(s => s.State == WatchState.HostDown) ? (live.Running ? HostDown : HostDownStopped) : budgetWarning ?? "";

    /// <summary>
    /// The detail line on the board: why your boards aren't saved or aren't showing comes first (R3), since a
    /// change that silently didn't happen is worse; then a note from something you pressed (a failure's detail, an import's
    /// result), which stays until something you do replaces it (S1-12.4); then RoRoRo being down, then the budget warning.
    /// </summary>
    public static string DetailLine(LiveBoard live, string? budgetWarning, string? boardsProblem, string? note = null) =>
        boardsProblem ?? note ?? DetailLine(live, budgetWarning);

    /// <summary>What Delete… asks before the board goes, for the themed confirmation to draw.</summary>
    /// <summary>
    /// Whether closing would silence something: a recipe with a send tick or a field metric on and at least one of its
    /// clans switched on. Only then does closing the board ask; with nothing sending it closes as it always has.
    /// </summary>
    public static bool Sending(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources) =>
        installed.Any(i =>
            (i.State.StatChoices.Values.Any(c => c.Send) || i.State.FieldMetricKeys.Count > 0)
            && sources.Any(s => s.Enabled && string.Equals(s.Recipe, i.Recipe.Slug, StringComparison.Ordinal)));

    /// <summary>Closing the board ends Ur Score; while something is sending, that silences the phone alerts, so it asks first.</summary>
    public static Confirm CloseWhileSending { get; } = new(
        "Close Ur Score",
        "Close Ur Score? While it is closed nothing is read or recorded, and nothing reaches RoRoRo, so your phone alerts stop.",
        "Close",
        "Close Ur Score") { CancelButton = "Keep running" };

    public static Confirm DeleteBoardQuestion(BoardDef board) => new(
        "Delete board",
        $"Delete the {board.Name} board? Its panels go with it. Your score book isn't touched.",
        "Delete",
        $"Delete the {board.Name} board");

    /// <summary>
    /// A board change that couldn't be written, in plain words. No stack; an unknown IO reason is Windows' own
    /// sentence. Anything that isn't IO says only that it was unexpected: its message was never meant for you.
    /// </summary>
    public static string BoardsNotSaved(Exception ex)
    {
        const string NotSaved = "Your change to the boards wasn't saved: ";
        const int SharingViolation = unchecked((int)0x80070020), LockViolation = unchecked((int)0x80070021);
        const int DiskFull = unchecked((int)0x80070070), HandleDiskFull = unchecked((int)0x80070027);

        return ex switch
        {
            UnauthorizedAccessException => NotSaved + "Windows didn't let Ur Score write to its data folder.",
            IOException { HResult: SharingViolation or LockViolation } => NotSaved + "another program has your boards file open. Close it, then try again.",
            IOException { HResult: DiskFull or HandleDiskFull } => NotSaved + "the disk is full.",
            IOException => NotSaved + ex.Message,
            _ => NotSaved + "something unexpected went wrong.",
        };
    }

    /// <summary>
    /// Each recipe being read credits its data (spec §6.1 of the first design), and a sentence two recipes share is
    /// said once. Deduplicating whole credits is not enough: recipes for the same service open with the same
    /// sentence and then add their own, so the shared opening was printed once per recipe (backlog V3-S.4). Splitting
    /// on the sentence break — a full stop followed by a space — leaves a host name inside a sentence intact, because
    /// the stops within one are not followed by a space.
    /// </summary>
    public static string Attribution(LiveBoard live) =>
        string.Join(" ", live.Installed
            .Where(i => live.Sources.Any(s => s.Enabled && string.Equals(s.Recipe, i.Recipe.Slug, StringComparison.Ordinal)))
            .SelectMany(i => Sentences(i.Recipe.Credit))
            .Distinct(StringComparer.Ordinal));

    /// <summary>One credit's sentences, each keeping its own full stop.</summary>
    private static IEnumerable<string> Sentences(string credit) =>
        credit.Split(". ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.EndsWith('.') ? s : s + ".");

    /// <summary>
    /// What a panel's ⧉ is called. A board of five panels gave a screen reader five buttons named "Pop out" with no
    /// way to tell which was which, and an automation caller had to reach past the name to the panel's id to press
    /// the right one (owner's look, 2026-09-16). The panel's own title goes on the end, as it does on the popped-out
    /// slot's "Bring back {title}" and "Remove {title}" and the gallery's "Add {title}". A frame with no head behind
    /// it keeps the general word rather than no name at all.
    /// </summary>
    public static string PopOutName(string? title) =>
        string.IsNullOrWhiteSpace(title) ? "Pop out" : $"Pop out {title.Trim()}";

    /// <summary>As <see cref="PopOutName"/>, for the ⋯ beside it, which had the same one name on every panel.</summary>
    public static string PanelSettingsName(string? title) =>
        string.IsNullOrWhiteSpace(title) ? "Panel settings" : $"Settings for {title.Trim()}";

    public static string ChooseAnotherName(string? title) =>
        string.IsNullOrWhiteSpace(title) ? "Choose another" : $"Choose another for {title.Trim()}";

    /// <summary>As <see cref="PopOutName"/>, for ✕ in edit mode: five panels gave five buttons called "Remove panel".</summary>
    public static string RemovePanelName(string? title) =>
        string.IsNullOrWhiteSpace(title) ? "Remove panel" : $"Remove {title.Trim()}";

    /// <summary>
    /// Which empty state a board shows: no recipes over every board; a starter's own state on a tab that follows it
    /// (D4); a board with no panels, including a following tab being edited; else none.
    /// </summary>
    /// <param name="bookUnread">The score book couldn't be read, so nothing can run: that covers every board (S1-14.2).</param>
    public static BoardEmpty EmptyFor(IReadOnlyList<StarterBoard> starters, BoardDef board, bool editing = false, bool bookUnread = false) =>
        bookUnread ? BoardEmpty.BookUnread
        : starters.Any(s => s.Empty == BoardEmpty.NoRecipes) ? BoardEmpty.NoRecipes
        : !editing && StarterBoards.Named(starters, board.Follows) is { Empty: not BoardEmpty.None } starter ? starter.Empty
        : board.Panels.Count == 0 ? BoardEmpty.NoPanels
        : BoardEmpty.None;

    /// <param name="editing">In edit mode an empty board is told to press Done, not Edit board, which is where you are.</param>
    public static (string Line, string Detail, string Button) EmptyState(BoardEmpty empty, Recipe? recipe, bool editing = false)
    {
        var group = recipe is null ? "source" : RecipeWords.Group(recipe);
        return empty switch
        {
            BoardEmpty.NoRecipes => ("Import a recipe to start",
                "A recipe says where numbers are. Ur Score reads them, keeps them in your score book, and hands the ones you choose to RoRoRo.",
                "Import recipe…"),
            BoardEmpty.NoStats => ("No stats turned on yet",
                "Choose which stats to show and send, and the board fills in from the next read.",
                "Choose stats"),
            BoardEmpty.NoSources => ($"Choose your main {group}",
                "Type a few letters of its name in Setup, and Ur Score finds which of your accounts are in it.",
                $"Choose your main {group}"),
            // The state line above already says the book couldn't be read, and why: the board says what that stops (V3-S.21).
            BoardEmpty.BookUnread => ("Start and Test now are off",
                "They come back once Ur Score can read your score book. The line above says what stopped it.",
                "Try again"),
            BoardEmpty.NoPanels => ("This board has no panels yet",
                editing ? "Add panels from the gallery with Add panel, then press Done." : "Add panels from the gallery, then arrange them with Edit board.",
                "Add panel"),
            _ => ("", "", ""),
        };
    }

    private static bool Healthy(WatchState state) =>
        state is WatchState.Reporting or WatchState.Showing or WatchState.NoMatches or WatchState.HostDown or WatchState.SourceIdle;
}
