using System.Globalization;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

public enum PanelType { Standing, Race, MyAccounts, PromotionCheck, AccountCard, PastPeriods, Records, Top, ProfileStat, LiveLeaderboard, AccountsTable, Pace }

/// <summary>What a panel shows. Stage 1 fills these from the starter board; stage 2 saves them in boards.json.</summary>
public sealed record PanelSettings(
    string Recipe = "",
    string? SourceId = null,
    IReadOnlyList<string>? SourceIds = null,
    string? ToSourceId = null,
    string? Stat = null,
    long? UserId = null);

/// <summary>
/// Every panel's header: title, subtitle, role chip, overdue mark, and the stale message that replaces the body.
/// The chip is its source's role: <see cref="Chip"/> is the role's words and the panel colours the chip by the role,
/// so words and colour can never disagree.
/// </summary>
public sealed record PanelHead(
    string Title, string Subtitle = "", SourceRole? ChipRole = null, bool Overdue = false, string? Stale = null, string Note = "",
    bool Remembered = false)
{
    public string Chip => ChipRole is { } role ? PanelText.Chip(role) : "";

    public bool HasBody => Stale is null;

    public bool HasStale => Stale is not null;

    public bool HasChip => ChipRole is not null;

    public bool HasSubtitle => Subtitle.Length > 0;

    public bool HasNote => Note.Length > 0 && Stale is null;
}

/// <summary>Everything live a panel may use. Other players' rows live here in memory only.</summary>
public sealed record LiveBoard(
    IReadOnlyList<Source> Sources,
    IReadOnlyList<InstalledRecipe> Installed,
    IReadOnlyDictionary<string, RecipeSnapshot> Snapshots,
    IReadOnlyDictionary<string, DateTimeOffset> LastRead,
    IReadOnlyList<HostAccount> Accounts,
    TimeProvider Time,
    bool Running,
    IReadOnlyDictionary<long, string>? Avatars = null,
    IReadOnlyDictionary<string, RecipeSnapshot>? Remembered = null,
    IReadOnlyDictionary<string, string>? Icons = null)
{
    public DateTimeOffset Now => Time.GetUtcNow();

    /// <summary>
    /// Your own Roblox user ids, one set per board. It was built on every access, and two of the accesses sit in
    /// per-row lambdas — "is this row one of mine?" — so a fifty-row leaderboard built fifty sets to draw itself
    /// (S1-13.10). Cached against the account list it was built from rather than in a plain field, because a
    /// record's <c>with</c> copies every field: a copy given other accounts must answer for those, not for the
    /// list the original was asked about. The race is benign — two threads build the same set and one wins.
    /// </summary>
    public IReadOnlySet<long> MyUserIds
    {
        get
        {
            if (_mine is { } cached && ReferenceEquals(cached.Of, Accounts)) return cached.Ids;

            var ids = UserIdsOf(Accounts);
            _mine = (Accounts, ids);
            return ids;
        }
    }

    private (IReadOnlyList<HostAccount> Of, IReadOnlySet<long> Ids)? _mine;

    /// <summary>Your own Roblox user ids from a list of your accounts; an account with no id (0) has none.</summary>
    public static IReadOnlySet<long> UserIdsOf(IReadOnlyList<HostAccount> accounts) =>
        accounts.Where(a => a.RobloxUserId != 0).Select(a => a.RobloxUserId).ToHashSet();

    public Source? FindSource(string? id) =>
        id is null ? null : Sources.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));

    public InstalledRecipe? FindRecipe(string? slug) =>
        slug is null ? null : Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, slug, StringComparison.Ordinal));

    /// <summary>
    /// What a panel draws for a source: the reading from this session, else the last one the score book kept (plan
    /// A38). A remembered one carries <see cref="RecipeSnapshot.RememberedAt"/>; a panel that needs another member's
    /// row takes <see cref="LiveOf"/> instead (plan A40).
    /// <para>
    /// A read that FAILED has replaced nothing, so it does not take the remembered numbers with it (review round 2).
    /// A source that cannot be reached is exactly when the last numbers are worth most, and going blank there would
    /// leave the board worse off than before it remembered anything. The failed reading is still what
    /// <see cref="LiveOf"/> answers with, so the state line goes on naming the fault while the numbers beside it stay
    /// honestly marked as remembered. Only a reading that came back clears them.
    /// </para>
    /// </summary>
    public RecipeSnapshot? SnapshotOf(string sourceId)
    {
        var live = Snapshots.GetValueOrDefault(sourceId);
        return live is not null && BroughtNumbers(live) ? live : Remembered?.GetValueOrDefault(sourceId) ?? live;
    }

    /// <summary>The reading from this session alone. What Ur Score is DOING is only ever answered from this one.</summary>
    public RecipeSnapshot? LiveOf(string sourceId) => Snapshots.GetValueOrDefault(sourceId);

    /// <summary>
    /// The role a panel's chip wears for a source. A clan added under "your accounts are in" is called yours only while the
    /// read in hand doesn't contradict it: when this session's reading has members and none of them is one of your
    /// accounts, it is a clan you are watching, and the chip says so rather than "yours" above "Your accounts 0 of 57". With
    /// no members read (not yet read, or between battles) there is no evidence either way, so it keeps what you chose.
    /// Only a live reading can prove "none of yours": a remembered one holds your own accounts alone (plan A40). Deciding
    /// membership from the clan's roster, which is there between battles too, is the larger change in the backlog.
    /// </summary>
    public SourceRole ChipRole(Source source) =>
        source.Role == SourceRole.Mine
        && LiveOf(source.Id)?.Rows is { Count: > 0 } rows
        && !rows.Any(r => MyUserIds.Contains(r.UserId))
            ? SourceRole.Watch
            : source.Role;

    /// <summary>
    /// Whether a reading came back with numbers at all. A read that failed carries its state and its reason and
    /// nothing else — <see cref="RecipeSnapshot.Rows"/> and <see cref="RecipeSnapshot.Headline"/> are both null,
    /// because no reading was ever attached to it — so it replaces nothing a panel is drawing.
    /// </summary>
    private static bool BroughtNumbers(RecipeSnapshot snapshot) =>
        snapshot.Rows is not null || snapshot.Headline is not null || snapshot.Groups.Count > 0;

    public bool IsRemembered(string sourceId) => SnapshotOf(sourceId)?.RememberedAt is not null;

    /// <summary>
    /// The oldest reading behind anything on screen, so a line about them never claims they are fresher than the
    /// oldest one a panel is showing. Null once every enabled source has been read this session.
    /// </summary>
    public DateTimeOffset? OldestRemembered =>
        Sources.Where(s => s.Enabled).Select(s => SnapshotOf(s.Id)?.RememberedAt).Min();

    /// <summary>The source's main input value ("CCGP"), else its recipe's name.</summary>
    public string SourceName(Source source) => NameOf(source, FindRecipe(source.Recipe)?.Recipe);

    /// <summary><see cref="SourceName"/> for a caller that already has the source's recipe, or knows it has none.</summary>
    public static string NameOf(Source source, Recipe? recipe)
    {
        if (recipe is not null && RecipeWords.MainInput(recipe) is { } input
            && source.Inputs.TryGetValue(input.Id, out var value) && value.Trim().Length > 0)
        {
            return value.Trim();
        }

        return recipe?.Name ?? source.Recipe;
    }

    public string AccountName(long userId) =>
        Accounts.FirstOrDefault(a => a.RobloxUserId == userId && userId != 0)?.DisplayName ?? "One of your accounts";

    /// <summary>
    /// The cached picture for one of YOUR accounts, or null. An id that isn't yours has none, whatever the map holds:
    /// the leaderboard and Top show other members by name only, and this is the second of the two checks (plan A22).
    /// </summary>
    public string? AvatarFor(long userId) =>
        userId != 0 && MyUserIds.Contains(userId) ? Avatars?.GetValueOrDefault(userId) : null;

    /// <summary>
    /// The cached picture of <paramref name="source"/> itself, or null (backlog V3-S.7): kept per source, so it is that clan's
    /// and never another's. A recipe that no longer names an icon has none, whatever the map still holds.
    /// </summary>
    public string? IconFor(Source source) =>
        FindRecipe(source.Recipe)?.Recipe.Icon is null ? null : Icons?.GetValueOrDefault(source.Id);

    /// <summary>Spec §9.6: only while reading runs, and only once the source has been read.</summary>
    public bool IsOverdue(Source source) =>
        Running
        && LastRead.TryGetValue(source.Id, out var last)
        && FindRecipe(source.Recipe) is { } installed
        && Records.Overdue(last, installed.Recipe.EffectiveEverySeconds, Now);
}

public sealed record FactModel(string Label, string Value);

/// <summary>
/// The ten panels' view models (spec §9.4), built from live snapshots and the score book. Pure: no WPF, no
/// disk, no clock but <see cref="LiveBoard.Time"/>. Live-only panels take no reader, so they cannot reach
/// the book.
/// </summary>
public static partial class PanelModels
{
    private const string Dash = StatText.Dash;

    /// <summary>The rise from the first to the last reading, or null with fewer than two.</summary>
    public static double? Gain(IReadOnlyList<SeriesPoint> points) =>
        points.Count < 2 ? null : points[^1].Value - points[0].Value;

    /// <summary>
    /// A window's gain: from the latest reading at or before <paramref name="since"/> to the last reading.
    /// When nothing was read that early, falls back to the series' own first reading and states the real
    /// span covered, rather than silently understating a shorter history as the full window (spec §9.4).
    /// Written in the stat's format; a date has no gain, and fewer than two readings is <paramref name="none"/>.
    /// </summary>
    private static string WindowGain(IReadOnlyList<SeriesPoint> series, DateTimeOffset since, StatFormat format = StatFormat.Number, string none = "no earlier read")
    {
        if (format == StatFormat.Date) return Dash;
        if (series.Count < 2) return none;

        var last = series[^1];
        SeriesPoint? baseline = null;
        for (var i = series.Count - 2; i >= 0; i--)
        {
            if (series[i].T <= since)
            {
                baseline = series[i];
                break;
            }
        }

        var from = baseline ?? series[0];
        var text = PanelText.Change(last.Value - from.Value, format);
        return baseline is null ? $"{text} in {StatText.Span(last.T - from.T)}" : text;
    }

    internal static PanelHead StaleSource(LiveBoard live, PanelSettings settings, string title) =>
        new(title, Stale: PanelText.StaleSource(live.FindRecipe(settings.Recipe)?.Recipe is { } recipe ? RecipeWords.Group(recipe) : "source"));

    /// <summary>A record's own reading: abbreviated for a number ("12.4M"), written out for a duration or date (a value fact, not a gain — those don't further abbreviate).</summary>
    private static string ShortValue(double value, StatFormat format, TimeZoneInfo zone) =>
        format == StatFormat.Number ? PanelText.Short(value) : PanelText.Value(value, format, zone);

    private static IEnumerable<Source> SourcesYoursIn(LiveBoard live, Recipe recipe) =>
        live.Sources
            .Where(s => s.Enabled && s.Role != SourceRole.Watch && string.Equals(s.Recipe, recipe.Slug, StringComparison.Ordinal))
            .OrderBy(s => s.Role == SourceRole.Main ? 0 : 1);

    private static string? PlaceId(Recipe recipe) => recipe.Headline.FirstOrDefault(h => !h.Sum)?.Id;

    private static string? TotalId(Recipe recipe) => recipe.Headline.FirstOrDefault(h => h.Sum)?.Id;

    private static double? HeadlineNumber(RecipeSnapshot? snapshot, string? id) =>
        id is null ? null : snapshot?.Headline?.FirstOrDefault(h => h.Id == id)?.Number;

    /// <summary>
    /// One account's place among the rows its source read that have the stat — "#7 of 49" — or a dash when there is no
    /// honest answer (plan A40, review C1). The one door for a rank, so neither panel can grow its own.
    /// <para>
    /// A reading from this session carries every row, so <paramref name="live"/> is counted from it, and N is how many rows
    /// it ranked: a row with no value is in neither (backlog S1-6.9). Counting every row made a member with no points part
    /// of a "#1 of 4" beside Promotion check's field of 3.
    /// </para>
    /// <para>
    /// A REMEMBERED reading carries your own accounts alone, and a place worked out from those would read "#1 of 4" of a
    /// group this never counted — so it is answered from what the reading itself kept (<see cref="RecipeSnapshot.RememberedRanks"/>),
    /// and from nothing else. A line that kept no place shows none: an empty "In clan" is honest, "#1 of 4" is not. A line
    /// that kept a place but not its field shows the place alone.
    /// </para>
    /// </summary>
    private static string InGroup(
        RecipeSnapshot snapshot, IReadOnlyDictionary<long, int>? live, long userId, string stat, double? value)
    {
        if (value is null) return Dash;

        if (snapshot.RememberedAt is not null)
        {
            if (!snapshot.RememberedRanks.TryGetValue((userId, stat), out var kept)) return Dash;
            return kept.Of is { } of ? $"#{kept.Rank} of {of}" : $"#{kept.Rank}";
        }

        return live is not null && live.TryGetValue(userId, out var rank) ? $"#{rank} of {live.Count}" : Dash;
    }

    private static double? ValueOf(RecipeRow row, string stat) => row.Values.TryGetValue(stat, out var value) ? value : null;

    /// <summary>A recipe with a period shows the current period; one without shows the last 30 days (spec §9.1).</summary>
    private static DateTimeOffset Since(Recipe recipe, DateTimeOffset now) => recipe.Period is null ? now.AddDays(-30) : DateTimeOffset.MinValue;

    /// <summary>Highest first; a missing value sorts last and never counts as zero (spec §9.6).</summary>
    private static IReadOnlyList<T> MissingLast<T>(IEnumerable<(double? Value, T Row)> rows) =>
        [.. rows.OrderBy(r => r.Value is null).ThenByDescending(r => r.Value ?? 0).Select(r => r.Row)];
}
