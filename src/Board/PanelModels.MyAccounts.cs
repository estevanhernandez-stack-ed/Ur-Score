using System.Globalization;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

public sealed record AccountLineModel(
    long UserId, string Name, string Value, string InGroup, string Change, bool Sent, bool Stalled, bool Missing,
    string? Avatar = null, string Note = "")
{
    /// <summary>Why this row has no numbers, in the recipe's own words (plan A44). Empty for a row that was read.</summary>
    public bool HasNote => Note.Length > 0;
}

public sealed record AccountGroupModel(string Heading, IReadOnlyList<AccountLineModel> Rows);

public sealed record MyAccountsModel(PanelHead Head, string ValueColumn, string GroupColumn, IReadOnlyList<AccountGroupModel> Groups);

/// <summary>
/// My accounts (spec §9.4): your accounts by one stat, grouped by clan, with rank, change and what was sent. One of the panel-type files
/// <see cref="PanelModels"/> was split into on 2026-09-22 (S1-13.15); the shared helpers stay in PanelModels.cs.
/// </summary>
public static partial class PanelModels
{
    /// <summary>What My accounts' dot means, said where the dot is (backlog S1-F.5).</summary>
    public const string SentLegend = "● sent to RoRoRo in the last read";

    public static MyAccountsModel MyAccounts(LiveBoard live, ScoreBookReader reader, PanelSettings settings)
    {
        var title = PanelText.Title(PanelType.MyAccounts, null, live.Installed);
        if (live.FindRecipe(settings.Recipe) is not { } installed) return new MyAccountsModel(StaleSource(live, settings, title), "", "", []);

        var recipe = installed.Recipe;
        if (settings.Stat is null || RecipeStats.Find(recipe, settings.Stat) is not { } stat)
        {
            return new MyAccountsModel(new PanelHead(title, Stale: PanelText.StaleStat), "", "", []);
        }

        var group = RecipeWords.Group(recipe);
        var zone = live.Time.LocalTimeZone;
        var assigned = new HashSet<long>();
        var groups = new List<AccountGroupModel>();
        var overdue = false;
        var remembered = false;

        foreach (var source in SourcesYoursIn(live, recipe))
        {
            overdue |= live.IsOverdue(source);
            remembered |= live.IsRemembered(source.Id);
            var snapshot = live.SnapshotOf(source.Id);
            if (snapshot?.Rows is not { } rows) continue;

            var mine = live.Accounts
                .Where(a => a.RobloxUserId != 0 && !assigned.Contains(a.RobloxUserId) && rows.Any(r => r.UserId == a.RobloxUserId))
                .ToList();
            if (mine.Count == 0) continue;

            // Counted here only for a reading of this session, which holds every row. A remembered one holds your own
            // accounts alone, so InGroup answers it from the book instead (review C1).
            var ranks = snapshot.RememberedAt is null ? Ranking.Competition(rows, stat.Key) : null;
            var period = snapshot.Period?.Value;
            var since = Since(recipe, live.Now);
            var series = mine.ToDictionary(a => a.RobloxUserId, a => reader.Series(source.Id, a.RobloxUserId, stat.Key, period, since));

            var lines = new List<(double? Value, AccountLineModel Line)>();
            foreach (var account in mine)
            {
                assigned.Add(account.RobloxUserId);
                var value = ValueOf(rows.First(r => r.UserId == account.RobloxUserId), stat.Key);
                var others = series.Where(kv => kv.Key != account.RobloxUserId).Select(kv => kv.Value);
                // Sent by this source's last read, of this stat. The session's remembered sends outlive the read that made
                // them, so reading those kept a dot lit through reads with RoRoRo closed or Send off (backlog S1-F.5).
                var sent = snapshot.SentThisRead.Contains((account.AccountId, stat.Key));

                lines.Add((value, new AccountLineModel(
                    account.RobloxUserId,
                    account.DisplayName,
                    PanelText.Value(value, stat.Format, zone),
                    InGroup(snapshot, ranks, account.RobloxUserId, stat.Key, value),
                    RecentChange(series[account.RobloxUserId], stat.Format),
                    sent,
                    Records.Stalled(series[account.RobloxUserId], others),
                    value is null,
                    live.AvatarFor(account.RobloxUserId))));
            }

            var heading = PanelText.StarredName(live.SourceName(source), source.Role);
            groups.Add(new AccountGroupModel(heading, MissingLast(lines)));
        }

        // Every account left over sat under "Not in a watched clan": before the first read, when that was true of none of
        // them yet, and for one a watched clan held, when it was the opposite of true (backlog S1-13.4). Each is headed by
        // what is known of it instead, in the words Setup › Your accounts uses (PanelText.NotFound).
        var rest = live.Accounts.Where(a => a.RobloxUserId == 0 || !assigned.Contains(a.RobloxUserId)).ToList();
        if (rest.Count > 0)
        {
            var ofRecipe = live.Sources.Where(s => s.Enabled && string.Equals(s.Recipe, recipe.Slug, StringComparison.Ordinal)).ToList();
            // A watched clan's rows are never remembered (A40), so this is what was actually read.
            var inWatched = ofRecipe
                .Where(s => s.Role == SourceRole.Watch)
                .SelectMany(s => live.LiveOf(s.Id)?.Rows ?? [])
                .Select(r => r.UserId)
                .ToHashSet();
            var groupsWord = RecipeWords.GroupsLower(recipe);
            // Idle is asked of this session's reading alone: a remembered one is no read, between periods or otherwise.
            var notFound = PanelText.NotFound(
                recipe.Inputs.Count > 0 ? $"Not in a watched {group}" : "Not in the last read", group, groupsWord, RecipeWords.Period(recipe),
                inHand: ofRecipe.Count(s => live.SnapshotOf(s.Id)?.Rows is not null),
                readNow: ofRecipe.Count(s => live.LiveOf(s.Id)?.Rows is not null),
                idleNow: ofRecipe.Count(s => PanelText.ReadIdle(live.LiveOf(s.Id))),
                sources: ofRecipe.Count);

            string Heading(HostAccount account) =>
                account.RobloxUserId == 0 ? PanelText.NotMatched
                : inWatched.Contains(account.RobloxUserId) ? PanelText.OnlyWatched(groupsWord)
                : notFound;

            // Watched first (they are somewhere), then the rest, then the ones nothing can place yet.
            foreach (var heading in new[] { PanelText.OnlyWatched(groupsWord), notFound, PanelText.NotMatched })
            {
                var under = rest.Where(a => Heading(a) == heading).ToList();
                if (under.Count > 0) groups.Add(Leftovers(live, heading, under));
            }
        }

        return new MyAccountsModel(
            new PanelHead(title, $"by {RecipeWords.Lower(stat.Label)}", Overdue: overdue, Note: SentLegend, Remembered: remembered),
            stat.Label, $"In {group}", groups);
    }

    /// <summary>
    /// <see cref="Records.Change"/>'s recent change ("+220K in 1h") written in the stat's format: a duration's rise reads
    /// as time ("+2h 0m in 3h"), and a date has no change.
    /// </summary>
    private static string RecentChange(IReadOnlyList<SeriesPoint> series, StatFormat format) =>
        format == StatFormat.Date ? Dash
        : Records.Movement(series) is not { } moved ? Records.NoEarlierRead
        : $"{PanelText.Change(moved.Delta, format)} in {StatText.Span(moved.Span)}";

    /// <summary>
    /// Your accounts under a heading with no value, rank or change, because no read of your own sources placed them. Each row's
    /// note comes from live.Snapshots, not live.SnapshotOf: a remembered snapshot never carries an Unavailable entry (A39), so
    /// this reads what was actually read, and says nothing at all before the first read.
    /// </summary>
    private static AccountGroupModel Leftovers(LiveBoard live, string heading, IEnumerable<HostAccount> accounts) =>
        new(heading, [.. accounts.OrderBy(a => a.DisplayName, StringComparer.Ordinal)
            .Select(a => new AccountLineModel(
                a.RobloxUserId, a.DisplayName, Dash, Dash, Dash, false, false, true, live.AvatarFor(a.RobloxUserId),
                PanelText.CannotRead(a.RobloxUserId, live.Installed, live.Sources, live.Snapshots, nameTheRecipe: false)))]);
}
