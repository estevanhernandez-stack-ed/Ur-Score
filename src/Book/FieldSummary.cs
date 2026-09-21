using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Book;

/// <summary>
/// What a clans list leaves in the score book: four numbers about the field, and how many clans they cover.
/// <para>
/// A group list is a hundred other clans, and the owner's ruling of 2026-09-19 is that none of them is named on
/// disk. These numbers name a position in the field instead — the leader, the top ten, the bottom ten, the whole
/// field — so a later read can say how fast the field was going without keeping a register of who was in it.
/// Your own clan's points and place are not here: its own source already records them.
/// </para>
/// <para>
/// They ride as headline numbers, the same shape <c>clan-points</c> already uses, so the book's format, its reader
/// and its series need nothing new.
/// </para>
/// </summary>
public static class FieldSummary
{
    public const string Leader = "field-leader";
    public const string Top10 = "field-top10";
    public const string Average = "field-avg";
    public const string Bottom10 = "field-bottom10";

    /// <summary>How many clans the numbers above cover, so a short read can't read as the field collapsing.</summary>
    public const string Clans = "field-clans";

    /// <summary>Your own clan's points as this read saw them, beside the field's, so a gap is read from one instant.</summary>
    public const string Mine = "field-mine";

    /// <summary>The place your clan holds, worked out from the points in this read.</summary>
    public const string MineRank = "field-mine-rank";

    /// <summary>
    /// The points of the place directly above yours. A position, never a clan: whoever holds it, the series keeps
    /// meaning "the one to catch". It is what a catch-up pace needs to survive a restart.
    /// </summary>
    public const string Above = "field-above";

    /// <summary>How far the place above is ahead, from the same read, so the gap never mixes two instants.</summary>
    public const string GapAbove = "field-gap-above";

    /// <summary>Your clan's members, and the most it can hold: the difference is a slot you can move an alt into.</summary>
    public const string MineMembers = "field-mine-members";

    public const string MineCapacity = "field-mine-capacity";

    /// <summary>How many of your clan's members have scored in this battle. Members minus this is the roster sitting out.</summary>
    public const string MineContributors = "field-mine-contributors";

    /// <summary>The ids a list carries these counts under, when it carries them at all.</summary>
    private const string MembersId = "members", CapacityId = "capacity", ContributorsId = "contributors";

    private const int Cohort = 10;

    /// <summary>
    /// The summary of one clans-list read, or nothing when the read brought no clan with that value.
    /// <para>
    /// Ranked by the value itself, never by the rank the list handed over: measured on the live board on
    /// 2026-09-19, the top-100 list's own rank disagreed with its own points — rank 10 held more than rank 9.
    /// A list shorter than ten clans is its own top and bottom, and <see cref="Clans"/> says how many that was.
    /// </para>
    /// </summary>
    /// <param name="mine">
    /// The names of your own clans, so the read can say where you stand in the field. Your own clan may be named —
    /// the ruling is about everyone else's — but only its number is kept, and the place above you is kept as a
    /// position. With none of yours in the list, only the field's own numbers are.
    /// </param>
    public static IReadOnlyDictionary<string, double> Of(
        IReadOnlyList<GroupRow> groups, string valueKey, IReadOnlySet<string>? mine = null)
    {
        if (groups.Count == 0 || string.IsNullOrEmpty(valueKey)) return new Dictionary<string, double>(StringComparer.Ordinal);

        var ranked = groups
            .Select(g => (g.Name, Row: g, Value: g.Values.TryGetValue(valueKey, out var value) ? value : (double?)null))
            .Where(g => g.Value is not null)
            .OrderByDescending(g => g.Value!.Value)
            .ToList();

        if (ranked.Count == 0) return new Dictionary<string, double>(StringComparer.Ordinal);

        var points = ranked.Select(g => g.Value!.Value).ToList();
        var summary = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [Leader] = points[0],
            [Top10] = points.Take(Cohort).Average(),
            [Average] = points.Average(),
            [Bottom10] = points.TakeLast(Cohort).Average(),
            [Clans] = points.Count,
        };

        // The best placed of your clans is the one the catch-up numbers are about. Matched however the name was
        // typed: Setup takes what you type, and the list has its own casing.
        var yours = mine is null ? null : new HashSet<string>(mine, StringComparer.OrdinalIgnoreCase);
        var at = yours is null ? -1 : ranked.FindIndex(g => yours.Contains(g.Name));
        if (at < 0) return summary;

        summary[Mine] = points[at];
        summary[MineRank] = at + 1;

        // Only what the list actually carried: an absent count must never read as a full clan or an idle one.
        foreach (var (id, key) in new[] { (MembersId, MineMembers), (CapacityId, MineCapacity), (ContributorsId, MineContributors) })
        {
            if (ranked[at].Row.Values.TryGetValue(id, out var count)) summary[key] = count;
        }
        if (at > 0)
        {
            summary[Above] = points[at - 1];
            summary[GapAbove] = points[at - 1] - points[at];
        }

        return summary;
    }

    /// <summary>
    /// One clan below yours in a live read: its name, because a threat is tracked by name; its value; and how
    /// far behind you it is.
    /// </summary>
    /// <param name="Name">The clan's name, because a threat is tracked by name.</param>
    /// <param name="Value">The clan's points in this read.</param>
    /// <param name="Gap">
    /// Our points minus theirs, from the same read a threat needs — how far behind us this clan is. Computed
    /// here rather than by the caller: <see cref="Behind"/> already has to find our row to know where "below"
    /// starts, so it is the only place that knows both numbers (owner's ruling, 2026-09-20).
    /// </param>
    public sealed record BehindClan(string Name, double Value, double Gap);

    /// <summary>
    /// The <paramref name="count"/> clans placed just below the best placed of yours, best first.
    /// <para>
    /// By NAME, because a threat has to be. A position's points on the way up reveal nothing about a change of
    /// occupant, so the clear-on-fall rule that makes <see cref="Above"/> safe has no equivalent below (design §4).
    /// </para>
    /// </summary>
    public static IReadOnlyList<BehindClan> Behind(
        IReadOnlyList<GroupRow> groups, string valueKey, IReadOnlySet<string>? mine, int count)
    {
        if (mine is null || count <= 0) return [];

        var ranked = groups
            .Select(g => (g.Name, Value: g.Values.TryGetValue(valueKey, out var v) ? v : (double?)null))
            .Where(g => g.Value is not null)
            .OrderByDescending(g => g.Value!.Value)
            .ToList();

        // Matched however the name was typed, the same fold and for the same reason as Of's `yours` (line 94):
        // Setup takes what you type, and the list has its own casing. A method must not be correct only by
        // accident of what comparer the caller's set happens to use — both places below that ask "is this one of
        // mine?" go through this same normalized set, so the two sibling methods answer that question identically.
        var yours = new HashSet<string>(mine, StringComparer.OrdinalIgnoreCase);
        var at = ranked.FindIndex(g => yours.Contains(g.Name));
        if (at < 0) return [];

        var ours = ranked[at].Value!.Value;

        return [.. ranked.Skip(at + 1).Where(g => !yours.Contains(g.Name)).Take(count)
            .Select(g => new BehindClan(g.Name, g.Value!.Value, ours - g.Value!.Value))];
    }
}
