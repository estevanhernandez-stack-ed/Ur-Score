using System.IO;
using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

/// <summary>The board window's own lines: the top line, the state line, the empty states (spec §8).</summary>
public static class BoardText
{
    public const string HostDown = "RoRoRo is not running. Still reading and keeping your scores; nothing is being sent.";

    /// <summary>"AutumnBattle · ends in 3d · next read in 2m", or "Reads every 30m · next read in 12m" without a period.</summary>
    public static string TopLine(LiveBoard live, string? anchorSourceId)
    {
        var source = live.FindSource(anchorSourceId) ?? live.Sources.FirstOrDefault(s => s.Enabled);
        if (source is null || live.FindRecipe(source.Recipe)?.Recipe is not { } recipe) return "";

        DateTimeOffset? next = live.Running && live.LastRead.TryGetValue(source.Id, out var last)
            ? last.AddSeconds(recipe.EffectiveEverySeconds)
            : null;

        if (live.SnapshotOf(source.Id)?.Period is { } period) return PanelText.PeriodLine(period, live.Now, next);

        var every = $"Reads every {StatText.Span(TimeSpan.FromSeconds(recipe.EffectiveEverySeconds))}";
        return next is { } due ? $"{every} · {PanelText.NextRead(due, live.Now)}" : every;
    }

    public static string StateLine(LiveBoard live, bool everStarted)
    {
        // Plan A41: whatever else this line says, it says so while any panel is drawing numbers from the score book.
        var remembered = live.OldestRemembered is { } oldest ? " " + RememberedLine(oldest, live.Now) : "";

        if (!live.Running) return (everStarted ? "Stopped." : "Not started.") + remembered;

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
    /// Plan A41: how old the numbers on screen are, from the OLDEST reading behind any of them, so the line can never
    /// sound fresher than the worst thing it covers.
    /// </summary>
    public static string RememberedLine(DateTimeOffset oldest, DateTimeOffset now) =>
        $"The numbers on screen are the last ones Ur Score read, from {StatText.Span(now - oldest)} ago.";

    public static string DetailLine(LiveBoard live, string? budgetWarning) =>
        live.Snapshots.Values.Any(s => s.State == WatchState.HostDown) ? HostDown : budgetWarning ?? "";

    /// <summary>
    /// The detail line on the board: why your boards aren't saved or aren't showing comes first (R3), since a
    /// change that silently didn't happen is worse; then RoRoRo being down, then the budget warning.
    /// </summary>
    public static string DetailLine(LiveBoard live, string? budgetWarning, string? boardsProblem) =>
        boardsProblem ?? DetailLine(live, budgetWarning);

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
    /// Which empty state a board shows: no recipes over every board; a starter's own state on a tab that follows it
    /// (D4); a board with no panels, including a following tab being edited; else none.
    /// </summary>
    public static BoardEmpty EmptyFor(IReadOnlyList<StarterBoard> starters, BoardDef board, bool editing = false) =>
        starters.Any(s => s.Empty == BoardEmpty.NoRecipes) ? BoardEmpty.NoRecipes
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
            BoardEmpty.NoPanels => ("This board has no panels yet",
                editing ? "Add panels from the gallery with Add panel, then press Done." : "Add panels from the gallery, then arrange them with Edit board.",
                "Add panel"),
            _ => ("", "", ""),
        };
    }

    private static bool Healthy(WatchState state) =>
        state is WatchState.Reporting or WatchState.Showing or WatchState.NoMatches or WatchState.HostDown or WatchState.SourceIdle;
}
