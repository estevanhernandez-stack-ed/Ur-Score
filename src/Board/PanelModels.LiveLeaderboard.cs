using System.Globalization;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

public sealed record LeaderRow(string Position, string Name, IReadOnlyList<string> Cells, bool Yours);

public sealed record LeaderboardModel(PanelHead Head, IReadOnlyList<string> Columns, IReadOnlyList<LeaderRow> Rows);

/// <summary>
/// Live leaderboard (spec §9.4): every row a read brought back, your accounts marked. Never saved. One of the panel-type files
/// <see cref="PanelModels"/> was split into on 2026-09-22 (S1-13.15); the shared helpers stay in PanelModels.cs.
/// </summary>
public static partial class PanelModels
{
    /// <summary>Every row of a source live, your accounts marked (spec §9.4). Other members' names come from memory only.</summary>
    public static LeaderboardModel LiveLeaderboard(LiveBoard live, PanelSettings settings, IReadOnlyDictionary<long, string> names)
    {
        var title = PanelText.Title(PanelType.LiveLeaderboard, null, live.Installed);
        var installed = live.FindRecipe(settings.Recipe);
        if (installed is null || live.FindSource(settings.SourceId) is not { } source)
        {
            return new LeaderboardModel(StaleSource(live, settings, title), [], []);
        }

        var shown = installed.State.ShownStats(installed.Recipe);
        var head = new PanelHead(title, live.SourceName(source), live.ChipRole(source), live.IsOverdue(source), Note: "Live only. Never saved.");
        if (shown.Count == 0) return new LeaderboardModel(head with { Note = "Tick Show on a stat to fill this panel." }, [], []);

        IReadOnlyList<string> columns = [.. shown.Select(s => s.Label)];
        // Live only (plan A40): every row but yours is memory alone, so a remembered snapshot would show you by yourself.
        if (live.LiveOf(source.Id)?.Rows is not { } rows)
        {
            // Empty after a read this session is said, not left blank (S1-F.6); before one, "Live only" is all there is to say.
            return new LeaderboardModel(live.LiveOf(source.Id) is null ? head : head with { Note = $"{head.Note} {PanelText.NothingBack()}" }, columns, []);
        }

        var ranked = Leaderboard.Rank(rows, live.MyUserIds, shown[0].Key);
        var zone = live.Time.LocalTimeZone;
        return new LeaderboardModel(head, columns, [.. ranked.Select(r => new LeaderRow(
            r.Position.ToString(CultureInfo.InvariantCulture),
            r.IsMine ? live.AccountName(r.UserId) : names.GetValueOrDefault(r.UserId) ?? $"Member {r.UserId}",
            [.. shown.Select(s => PanelText.Value(r.Values.TryGetValue(s.Key, out var v) ? v : null, s.Format, zone))],
            r.IsMine))]);
    }
}
