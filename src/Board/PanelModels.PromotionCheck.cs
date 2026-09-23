using System.Globalization;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

public sealed record PromotionRow(string Name, string Value, string WouldPlace, bool Fits, bool Missing, string? Avatar = null);

public sealed record PromotionModel(PanelHead Head, string LowestLabel, string Lowest, string ValueColumn, IReadOnlyList<PromotionRow> Rows);

/// <summary>
/// Promotion check (spec §9.4): where each of your accounts would place in another clan now. Live only. One of the panel-type files
/// <see cref="PanelModels"/> was split into on 2026-09-22 (S1-13.15); the shared helpers stay in PanelModels.cs.
/// </summary>
public static partial class PanelModels
{
    /// <summary>Where each account in one source would place among another source's live rows (spec §9.4, §14). Live only.</summary>
    public static PromotionModel PromotionCheck(LiveBoard live, PanelSettings settings)
    {
        var title = PanelText.Title(PanelType.PromotionCheck, null, live.Installed);
        var installed = live.FindRecipe(settings.Recipe);
        var from = live.FindSource(settings.SourceId);
        var to = live.FindSource(settings.ToSourceId);

        if (installed is null || from is null || to is null) return new PromotionModel(StaleSource(live, settings, title), "", Dash, "", []);
        if (settings.Stat is null || RecipeStats.Find(installed.Recipe, settings.Stat) is not { } stat)
        {
            return new PromotionModel(new PanelHead(title, Stale: PanelText.StaleStat), "", Dash, "", []);
        }

        var fromName = live.SourceName(from);
        var toName = live.SourceName(to);
        var head = new PanelHead(title, $"{fromName} → {toName}", Overdue: live.IsOverdue(from) || live.IsOverdue(to),
            Note: $"Where each account would place if it were in {toName} now. Live only; other members' numbers are never saved.");
        var lowestLabel = $"{toName}'s lowest now";

        // Live only (plan A40): the book never kept another member's row, so a remembered snapshot cannot place anyone.
        var fromRows = live.LiveOf(from.Id)?.Rows;
        var toRows = live.LiveOf(to.Id)?.Rows;

        // A read that happened and brought nothing back is said as that, not as a wait for one (S1-F.6).
        string Why(Source missing, string missingName) =>
            live.LiveOf(missing.Id) is null ? $"Waiting for a read of {missingName}." : PanelText.NothingBack(missingName);

        if (toRows is null) return new PromotionModel(head with { Note = Why(to, toName) }, lowestLabel, Dash, stat.Label, []);

        var toValues = new List<(long UserId, double Value)>();
        foreach (var row in toRows)
        {
            if (ValueOf(row, stat.Key) is { } v) toValues.Add((row.UserId, v));
        }

        double? lowest = toValues.Count == 0 ? null : toValues.Min(r => r.Value);

        // Your side came back empty but theirs did not: their lowest is still true, so it still shows.
        if (fromRows is null) return new PromotionModel(head with { Note = Why(from, fromName) }, lowestLabel, PanelText.Full(lowest), stat.Label, []);

        var rows = new List<(double? Value, PromotionRow Row)>();

        foreach (var account in live.Accounts.Where(a => a.RobloxUserId != 0))
        {
            if (fromRows.FirstOrDefault(r => r.UserId == account.RobloxUserId) is not { } row) continue;

            if (ValueOf(row, stat.Key) is not { } value)
            {
                rows.Add((null, new PromotionRow(account.DisplayName, Dash, Dash, false, true, live.AvatarFor(account.RobloxUserId))));
                continue;
            }

            var others = toValues.Where(r => r.UserId != account.RobloxUserId).Select(r => r.Value).ToList();
            var place = Records.WouldPlace(value, others);
            var below = lowest is { } low && value < low;
            var text = place is not { } p ? Dash : below ? "below the lowest" : $"{PanelText.Ordinal(p.Place)} of {p.Of}";
            var fits = place is { } q && !below && q.Place <= others.Count;
            rows.Add((value, new PromotionRow(account.DisplayName, StatText.Abbrev(value), text, fits, false, live.AvatarFor(account.RobloxUserId))));
        }

        return new PromotionModel(head, lowestLabel, PanelText.Full(lowest), stat.Label, MissingLast(rows));
    }
}
