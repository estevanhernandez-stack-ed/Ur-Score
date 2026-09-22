using System.Globalization;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

public sealed record StandingModel(
    PanelHead Head, string Place, string PlaceSuffix, string TotalLabel, string Total, string Change,
    bool HasGap, string GapLabel, string Gap, double GapFill, bool HasAccounts, string Accounts, string PeriodLine)
{
    /// <summary>Which way <see cref="Change"/> went, so the panel paints a fall as one (backlog S1-13.14).</summary>
    public ChangeDirection ChangeDirection { get; init; }

    /// <summary>
    /// When this panel's period ends, for the clock that ticks beside <see cref="PeriodLine"/> between reads, or null
    /// with no period or no end. The panel keeps the instant, never a countdown worked out at read time: a number
    /// drawn three minutes ago would be three minutes wrong.
    /// </summary>
    public DateTimeOffset? Ends { get; init; }

    /// <summary>The picture of the clan this panel is about, or null while there is none: never the window's, never another clan's.</summary>
    public string? Icon { get; init; }

    /// <summary>
    /// This panel's recipe names an icon, so a picture belongs here: its space is kept while it isn't there yet or doesn't
    /// decode, and nothing moves when it lands (A26). Without one there is no slot at all.
    /// </summary>
    public bool HasIconSlot { get; init; }

    /// <summary>Whose picture it is, for a screen reader: "CCGP clan icon".</summary>
    public string IconName { get; init; } = "";
}

/// <summary>
/// Standing (spec §9.4): one clan's place, total, change and gap to the place above. One of the panel-type files
/// <see cref="PanelModels"/> was split into on 2026-09-22 (S1-13.15); the shared helpers stay in PanelModels.cs.
/// </summary>
public static partial class PanelModels
{
    public static StandingModel Standing(LiveBoard live, ScoreBookReader reader, PanelSettings settings)
    {
        var recipe = live.FindRecipe(settings.Recipe)?.Recipe;
        var title = PanelText.Title(PanelType.Standing, recipe, live.Installed);

        if (recipe is null || live.FindSource(settings.SourceId) is not { } source)
        {
            return new StandingModel(StaleSource(live, settings, title), Dash, "", "", Dash, "", false, "", "", 0, false, "", "");
        }

        var snapshot = live.SnapshotOf(source.Id);
        var name = live.SourceName(source);
        var totalId = TotalId(recipe);
        var place = HeadlineNumber(snapshot, PlaceId(recipe));
        var total = HeadlineNumber(snapshot, totalId);
        // Before the source's own period is known, "no period" reads the book as every period kept, not this one.
        var periodKnown = recipe.Period is null || snapshot?.Period is not null;
        // One series behind both the words and the colour, so the two can never tell different stories.
        IReadOnlyList<SeriesPoint>? totals = totalId is null || !periodKnown ? null : reader.HeadlineSeries(source.Id, totalId, snapshot?.Period?.Value);
        var change = totals is null ? Dash : Records.Change(totals, live.Now);
        var gap = Gap(live, name);

        var rows = snapshot?.Rows;
        // Your own rows are all a remembered snapshot has, so "4 of 4" would be a clan this never read (plan A40).
        var hasAccounts = source.Role != SourceRole.Watch && rows is not null && snapshot?.RememberedAt is null;
        var mine = rows?.Count(r => live.MyUserIds.Contains(r.UserId)) ?? 0;

        return new StandingModel(
            new PanelHead(title, name, live.ChipRole(source), live.IsOverdue(source), Remembered: live.IsRemembered(source.Id)),
            place is { } p ? PanelText.Ordinal((int)p) : Dash,
            recipe.Period is null || place is null ? "" : $"in the {RecipeWords.Period(recipe)}",
            recipe.Headline.FirstOrDefault(h => h.Id == totalId)?.Label ?? "Total",
            PanelText.Full(total),
            change,
            gap.Has, gap.Label, gap.Text, gap.Fill,
            hasAccounts,
            hasAccounts ? $"{mine} of {rows!.Count}" : "",
            PanelText.PeriodLine(snapshot?.Period, live.Now, null))
        {
            Ends = snapshot?.Period?.Ends,
            ChangeDirection = totals is null ? ChangeDirection.None : Records.Direction(totals),
            Icon = live.IconFor(source),
            HasIconSlot = recipe.Icon is not null,
            IconName = PanelText.IconName(name, recipe),
        };
    }

    /// <summary>
    /// The gap to the group just above, only when a group list holds both (spec §9.4). A list ranks ties as competitions do
    /// (11, 12, 12, 14), so the group above is the nearest one ranked HIGHER, never one this group ties with; and the list
    /// holds every group ranked between them exactly when that group's rank plus how many share it is this group's rank.
    /// 14th measures to 12th, each 12th to 11th, and 12, 14 with no 13th still shows none. Looking only for rank minus one
    /// hid the gap behind every tie and inside it (backlog S1-13.5).
    /// </summary>
    private static (bool Has, string Label, string Text, double Fill) Gap(LiveBoard live, string name)
    {
        foreach (var source in live.Sources.Where(s => s.Enabled))
        {
            if (live.FindRecipe(source.Recipe)?.Recipe is not { IsGroupList: true } recipe) continue;
            if (live.SnapshotOf(source.Id)?.Groups is not { Count: > 0 } groups) continue;

            var ordered = OrderGroups(groups, recipe.LastStep.Values[0].Id);
            var index = ordered.FindIndex(g => string.Equals(g.Row.Name, name, StringComparison.OrdinalIgnoreCase));
            if (index <= 0) continue;

            var here = ordered[index];
            var aboveIndex = ordered.FindLastIndex(index - 1, g => g.Rank < here.Rank);
            if (aboveIndex < 0) continue;

            var above = ordered[aboveIndex];
            if (above.Rank + ordered.Count(g => g.Rank == above.Rank) != here.Rank || above.Value is not { } a || here.Value is not { } h) continue;

            return (true, $"To {PanelText.Ordinal(above.Rank)}", $"{StatText.Abbrev(Math.Max(0, a - h))} behind", a <= 0 ? 0 : Math.Clamp(h / a, 0, 1));
        }

        return (false, "", "", 0);
    }
}
