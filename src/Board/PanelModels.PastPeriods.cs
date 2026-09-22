using System.Globalization;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

public sealed record PastRow(string Period, string Place, string Total, string YourBest);

public sealed record PastPeriodsModel(PanelHead Head, string PeriodColumn, IReadOnlyList<PastRow> Rows);

/// <summary>
/// Past periods (spec §9.4): finished periods newest first, with place, total and your best account. One of the panel-type files
/// <see cref="PanelModels"/> was split into on 2026-09-22 (S1-13.15); the shared helpers stay in PanelModels.cs.
/// </summary>
public static partial class PanelModels
{
    public static PastPeriodsModel PastPeriods(LiveBoard live, ScoreBookReader reader, PanelSettings settings)
    {
        var installed = live.FindRecipe(settings.Recipe);
        var title = PanelText.Title(PanelType.PastPeriods, installed?.Recipe, live.Installed);
        if (installed is null || live.FindSource(settings.SourceId) is not { } source)
        {
            return new PastPeriodsModel(StaleSource(live, settings, title), "", []);
        }

        var recipe = installed.Recipe;
        var placeId = PlaceId(recipe);
        var totalId = TotalId(recipe);

        // Newest first, said out loud rather than inherited from whatever order the reader happens to return.
        // Two signals, and only two. T is when the book learned a period, so it separates cycles: a period
        // written later finished later, because a record only ever grows at its end. It cannot separate a
        // backfill, where one cycle writes the whole record and every entry carries the same T. There, Kept
        // does: the source lists its finished periods oldest first, so the last one it handed over is the most
        // recent (verified 2026-09-16 against the live record for the user's own group and that source's
        // published schedule, which agree key for key). No final carries a date of its own — the record gives
        // none — so no row is placed as though we knew when it ran. The reader already returns one entry per
        // period, so there is nothing to regroup here (backlog S1-13.9).
        var rows = reader.Finals(recipe.Slug, source.InputsKey)
            .OrderByDescending(f => f.T)
            .ThenByDescending(f => f.Kept)
            .Select(f =>
            {
                string best = Dash;
                if (settings.Stat is { } stat)
                {
                    double? top = null;
                    long holder = 0;
                    foreach (var (userId, account) in f.Accounts)
                    {
                        if (!account.V.TryGetValue(stat, out var v) || (top is { } t && v <= t)) continue;
                        top = v;
                        holder = userId;
                    }

                    if (top is { } value) best = $"{StatText.Abbrev(value)} · {live.AccountName(holder)}";
                }

                return new PastRow(
                    f.Period,
                    placeId is not null && f.Headline.TryGetValue(placeId, out var place) ? PanelText.Ordinal((int)place) : Dash,
                    totalId is not null && f.Headline.TryGetValue(totalId, out var total) ? StatText.Abbrev(total) : Dash,
                    best);
            })
            .ToList();

        // V3-S.1: "No finished battles kept yet" read as "this clan has never been in one". The truth is
        // that Ur Score hasn't read them, and the next read fills them in, idle source or not.
        var note = rows.Count == 0
            ? $"Ur Score hasn't read this {RecipeWords.Group(recipe)}'s finished {RecipeWords.Periods(recipe)} yet. "
              + $"The next read fills them in from the {RecipeWords.Group(recipe)}'s own record."
            : $"Filled in from the {RecipeWords.Group(recipe)}'s own record.";

        return new PastPeriodsModel(
            new PanelHead(title, live.SourceName(source), live.ChipRole(source), Note: note),
            RecipeWords.Capital(RecipeWords.Period(recipe)), rows);
    }
}
