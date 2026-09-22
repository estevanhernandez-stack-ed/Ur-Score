using System.Globalization;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

/// <summary>
/// Accounts table (spec §9.4): your accounts side by side, a column per shown stat, sorted by any column. The records live in AccountsTableModel.cs. One of the panel-type files
/// <see cref="PanelModels"/> was split into on 2026-09-22 (S1-13.15); the shared helpers stay in PanelModels.cs.
/// </summary>
public static partial class PanelModels
{
    /// <summary>
    /// Your accounts side by side (the Alts tab): a column per shown stat sorted as this session asks (D15), the sorted
    /// column's change today and over 7 days from the book (D16), a totals row and an account that couldn't be read
    /// saying why (D17). Only your own accounts: other rows a list recipe read are never looked at.
    /// </summary>
    public static AccountsTableModel AccountsTable(
        LiveBoard live, ScoreBookReader reader, PanelSettings settings, AccountSort? sort = null, long? pickedUserId = null)
    {
        var title = PanelText.Title(PanelType.AccountsTable, null, live.Installed);
        if (live.FindRecipe(settings.Recipe) is not { } installed) return new AccountsTableModel(StaleSource(live, settings, title), [], []);

        var recipe = installed.Recipe;

        // A pinned source that's gone is stale; an unpinned table reads the recipe's first source that is on, else its first (D17).
        var source = settings.SourceId is { } pinned ? live.FindSource(pinned) : PanelForms.FirstSourceOfRecipe(live, recipe.Slug);
        if (source is null) return new AccountsTableModel(StaleSource(live, settings, title), [], []);

        var stats = installed.State.ShownStats(recipe);
        var snapshot = live.SnapshotOf(source.Id);
        var zone = live.Time.LocalTimeZone;

        var effective = sort is { } asked && (asked.Key == AccountSort.NameKey || stats.Any(s => s.Key == asked.Key))
            ? asked
            : stats.Count > 0 ? new AccountSort(stats[0].Key, Descending: true) : new AccountSort(AccountSort.NameKey, Descending: false);
        var sorted = stats.FirstOrDefault(s => s.Key == effective.Key);
        var withChange = sorted is { Format: not StatFormat.Date };
        var byName = effective.Key == AccountSort.NameKey;

        var columns = new List<AccountColumn> { new(AccountSort.NameKey, "Account", AccountColumnKind.Name, byName, byName && effective.Descending) };
        foreach (var stat in stats)
        {
            var isSorted = stat.Key == effective.Key;
            columns.Add(new AccountColumn(stat.Key, stat.Label, AccountColumnKind.Stat, isSorted, isSorted && effective.Descending));
            if (!isSorted || !withChange) continue;

            columns.Add(new AccountColumn(stat.Key, "Today", AccountColumnKind.Today));
            columns.Add(new AccountColumn(stat.Key, "7 days", AccountColumnKind.Week));
        }

        var midnight = PanelText.Midnight(live.Now, zone);
        var read = live.Accounts
            .Where(a => a.RobloxUserId != 0)
            .DistinctBy(a => a.RobloxUserId)
            .Select(a => (Account: a, Row: snapshot?.Rows?.FirstOrDefault(r => r.UserId == a.RobloxUserId)))
            .ToList();

        var rows = new List<(double? Sort, AccountRow Row)>();
        foreach (var (account, row) in read)
        {
            IReadOnlyList<SeriesPoint> series = sorted is not null && withChange
                ? reader.Series(source.Id, account.RobloxUserId, sorted.Key, null, DateTimeOffset.MinValue)
                : [];
            var changeFormat = sorted?.Format ?? StatFormat.Number;

            var cells = columns.Select(column => column.Kind switch
            {
                AccountColumnKind.Name => account.DisplayName,
                AccountColumnKind.Today => WindowGain(series, midnight, changeFormat, Dash),
                AccountColumnKind.Week => WindowGain(series, live.Now.AddDays(-7), changeFormat, Dash),
                _ => PanelText.Value(row is null ? null : ValueOf(row, column.Key), StatOf(stats, column.Key).Format, zone),
            }).ToList();

            rows.Add((sorted is null || row is null ? null : ValueOf(row, sorted.Key), new AccountRow(
                account.RobloxUserId,
                account.DisplayName,
                cells,
                snapshot?.Unavailable.GetValueOrDefault(account.RobloxUserId) ?? "",
                Missing: row is null,
                Picked: account.RobloxUserId == pickedUserId,
                Avatar: live.AvatarFor(account.RobloxUserId))));
        }

        var names = StringComparer.OrdinalIgnoreCase;
        IEnumerable<(double? Sort, AccountRow Row)> ordered = byName
            ? effective.Descending ? rows.OrderByDescending(r => r.Row.Name, names) : rows.OrderBy(r => r.Row.Name, names)
            : effective.Descending
                ? rows.OrderBy(r => r.Sort is null).ThenByDescending(r => r.Sort ?? 0).ThenBy(r => r.Row.Name, names)
                : rows.OrderBy(r => r.Sort is null).ThenBy(r => r.Sort ?? 0).ThenBy(r => r.Row.Name, names);
        var list = ordered.Select(r => r.Row).ToList();

        if (stats.Count > 0 && list.Count > 0)
        {
            var totals = columns.Select(column =>
            {
                if (column.Kind == AccountColumnKind.Name) return "Total";
                if (column.Kind != AccountColumnKind.Stat || StatOf(stats, column.Key) is not { Sum: true, Format: not StatFormat.Date } stat) return "";

                var values = read.Where(x => x.Row is not null).Select(x => ValueOf(x.Row!, stat.Key)).OfType<double>().ToList();
                return values.Count == 0 ? Dash : PanelText.Value(values.Sum(), stat.Format, zone);
            }).ToList();
            list.Add(new AccountRow(0, "Total", totals, "", Missing: false, Picked: false, IsTotal: true));
        }

        // A source that is switched off is never read, so the table says so instead of waiting for a read that won't come.
        var note = stats.Count == 0 ? "Tick Show on a stat to fill this panel."
            : snapshot is not null ? ""
            : source.Enabled ? "Waiting for the first read."
            : PanelText.SwitchedOff(live.SourceName(source));
        return new AccountsTableModel(
            new PanelHead(title, live.SourceName(source), Overdue: live.IsOverdue(source), Note: note, Remembered: live.IsRemembered(source.Id)),
            columns, list);
    }

    private static RecipeStat StatOf(IReadOnlyList<RecipeStat> stats, string key) => stats.First(s => s.Key == key);
}
