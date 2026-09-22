using System.Globalization;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

public sealed record ProfileRow(string Name, string Value, string Today, string Week, string Note, bool Missing)
{
    public bool HasNote => Note.Length > 0;
}

public sealed record ProfileStatModel(PanelHead Head, string ValueColumn, IReadOnlyList<ProfileRow> Rows);

/// <summary>
/// Profile stat (spec §9.4): your accounts with a value, today's gain and the seven-day gain, for a recipe with no period. One of the panel-type files
/// <see cref="PanelModels"/> was split into on 2026-09-22 (S1-13.15); the shared helpers stay in PanelModels.cs.
/// </summary>
public static partial class PanelModels
{
    public static ProfileStatModel ProfileStat(LiveBoard live, ScoreBookReader reader, PanelSettings settings)
    {
        var title = PanelText.Title(PanelType.ProfileStat, null, live.Installed);
        if (live.FindRecipe(settings.Recipe) is not { } installed) return new ProfileStatModel(StaleSource(live, settings, title), "", []);

        var recipe = installed.Recipe;
        if (settings.Stat is null || RecipeStats.Find(recipe, settings.Stat) is not { } stat)
        {
            return new ProfileStatModel(new PanelHead(title, Stale: PanelText.StaleStat), "", []);
        }

        // A pinned source that's gone is stale: falling back drew another source's numbers under the settings you chose, with
        // nothing on screen to say so (backlog S1-13.7). Only an unpinned panel reads "the recipe's source", the recipe's first
        // source that is on, else its first, which is the Accounts table's rule (D17) and what it was asked for.
        var source = settings.SourceId is { } pinned ? live.FindSource(pinned) : PanelForms.FirstSourceOfRecipe(live, recipe.Slug);
        if (source is null) return new ProfileStatModel(StaleSource(live, settings, title), "", []);

        var snapshot = live.SnapshotOf(source.Id);
        var now = live.Now;
        var midnight = PanelText.Midnight(now, live.Time.LocalTimeZone);

        var rows = new List<(double? Value, ProfileRow Row)>();
        foreach (var account in live.Accounts.Where(a => a.RobloxUserId != 0))
        {
            var row = snapshot?.Rows?.FirstOrDefault(r => r.UserId == account.RobloxUserId);
            var value = row is null ? null : ValueOf(row, stat.Key);
            var unavailable = snapshot?.Unavailable.GetValueOrDefault(account.RobloxUserId);
            var missed = snapshot?.CellMisses.GetValueOrDefault((account.RobloxUserId, stat.Key));
            // The full history, unclipped: a window that starts mid-series must still see what came before it.
            var series = reader.Series(source.Id, account.RobloxUserId, stat.Key, null, DateTimeOffset.MinValue);

            rows.Add((value, new ProfileRow(
                account.DisplayName,
                PanelText.Value(value, stat.Format, live.Time.LocalTimeZone),
                WindowGain(series, midnight, stat.Format),
                WindowGain(series, now.AddDays(-7), stat.Format),
                // The recipe's own sentence when it has one. Otherwise, a value this read did not bring back says so
                // plainly: "can't read" named no cause (S1-13.7), but the recorded miss is a path and a list of keys,
                // which is true and unreadable. That detail stays in Setup > Diagnostics.
                unavailable ?? (value is null && missed is not null ? PanelText.NotInLastRead : ""),
                value is null)));
        }

        // A source that is off is never read, so its dashes say so rather than wait for a read that won't come.
        return new ProfileStatModel(
            new PanelHead(title, stat.Label, Overdue: live.IsOverdue(source), Note: source.Enabled ? "" : PanelText.SwitchedOff(live.SourceName(source)),
                Remembered: live.IsRemembered(source.Id)),
            stat.Label, MissingLast(rows));
    }
}
