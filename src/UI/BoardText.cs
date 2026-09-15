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
        if (!live.Running) return everStarted ? "Stopped." : "Not started.";

        var enabled = live.Sources.Where(s => s.Enabled).ToList();
        if (enabled.Count == 0) return "Running, with nothing to read yet.";

        foreach (var source in enabled)
        {
            if (live.SnapshotOf(source.Id) is { } snapshot && !Healthy(snapshot.State))
            {
                return $"{live.SourceName(source)}: {DiagnosticsModel.StateText(snapshot.State)}";
            }
        }

        return enabled.Count == 1 ? "Reading 1 source." : $"Reading {enabled.Count} sources.";
    }

    public static string DetailLine(LiveBoard live, string? budgetWarning) =>
        live.Snapshots.Values.Any(s => s.State == WatchState.HostDown) ? HostDown : budgetWarning ?? "";

    /// <summary>Each recipe being read credits its data (spec §6.1 of the first design).</summary>
    public static string Attribution(LiveBoard live) =>
        string.Join(" ", live.Installed
            .Where(i => live.Sources.Any(s => s.Enabled && string.Equals(s.Recipe, i.Recipe.Slug, StringComparison.Ordinal)))
            .Select(i => i.Recipe.Credit)
            .Distinct(StringComparer.Ordinal));

    /// <summary>
    /// Which empty state a board shows (R1): no recipes over every board; the starter's own states while the
    /// board still follows your sources; a saved board with no panels; else none.
    /// </summary>
    public static BoardEmpty EmptyFor(StarterBoard starter, bool followsStarter, BoardDef board) =>
        starter.Empty == BoardEmpty.NoRecipes ? BoardEmpty.NoRecipes
        : followsStarter ? starter.Empty
        : board.Panels.Count == 0 ? BoardEmpty.NoPanels
        : BoardEmpty.None;

    public static (string Line, string Detail, string Button) EmptyState(BoardEmpty empty, Recipe? recipe)
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
                "Add panels from the gallery, then arrange them with Edit board.",
                ""),
            _ => ("", "", ""),
        };
    }

    private static bool Healthy(WatchState state) =>
        state is WatchState.Reporting or WatchState.Showing or WatchState.NoMatches or WatchState.HostDown or WatchState.SourceIdle;
}
