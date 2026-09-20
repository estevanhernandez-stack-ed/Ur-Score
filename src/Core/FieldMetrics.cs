using Labs626.UrScore.Board;
using Labs626.UrScore.Book;

namespace Labs626.UrScore.Core;

/// <summary>One clan-and-field number a rule can be set on, under a fixed id every copy of Ur Score sends it as.</summary>
/// <param name="Key">How the tick is stored in the clans list's state.</param>
/// <param name="What">One line, shown beside the tick, and the same sentence a clan leader repeats to the clan.</param>
public sealed record FieldMetric(string Key, string Label, string MetricId, string What);

/// <summary>A metric and the number this read has for it. Only what a read could actually work out is here.</summary>
public sealed record FieldMetricValue(FieldMetric Metric, double Value);

/// <summary>
/// The numbers about your clan's standing that a clans-list read can send to RoRoRo, so an alert can fire on
/// them.
/// <para>
/// These exist because of what the owner asked for on 2026-09-20: a clan leader wants to say "set an alert on
/// that number", and have every member set the same one. So the ids here are FIXED — not pinned per install the
/// way <see cref="Labs626.UrScore.Recipes.SentStat"/> ids are — and they are the same on every machine. The tick
/// is per install; the name is not.
/// </para>
/// <para>
/// WHY A WATCHED LIST MAY SEND AT ALL. Spec §3.5 says a group list never sends, and §4.1 says a watched source
/// never sends. Both rules are about ACCOUNTS: a clans list is other people's clans and a watch is somebody
/// else's account, and neither may be reported as one of yours. Nothing here is an account. Every number below
/// is about the position YOUR clan holds — its points, its place, how far the place above is — and it goes out
/// with no subject at all, which RoRoRo keys globally. No other clan is named and no player is involved, so the
/// rules those sections exist to enforce are untouched.
/// </para>
/// <para>
/// Pace is deliberately NOT one of these. RoRoRo derives a rate from a rising number itself, so a Rate rule on
/// <c>clan.standing.points</c> is your clan's pace, worked out by the side that already knows how. What RoRoRo
/// cannot work out is what catching the place above would take, because that needs the other clan's pace as
/// well as yours — so that one is computed here.
/// </para>
/// </summary>
public static class FieldMetrics
{
    public const string Points = "points";
    public const string Place = "place";
    public const string GapAbove = "gap-above";
    public const string PaceNeeded = "pace-needed";
    public const string FreeSlots = "free-slots";
    public const string IdleMembers = "idle-members";

    /// <summary>Every number that can be ticked, in the order the section lists them.</summary>
    public static IReadOnlyList<FieldMetric> All { get; } =
    [
        new(Points, "Clan points", "clan.standing.points",
            "Your clan's points in this battle. A rate rule on it is your clan's pace, per minute."),
        new(Place, "Clan place", "clan.standing.place",
            "Where your clan stands in the list, 1 being the leader."),
        new(GapAbove, "Points behind the place above", "clan.standing.gap-above",
            "How far ahead the clan one place above you is. Zero while you lead."),
        new(PaceNeeded, "Points an hour needed to pass them", "clan.standing.pace-needed",
            "What you would have to make every hour, at the pace they are going, to take the place above before the battle ends."),
        new(FreeSlots, "Free clan slots", "clan.standing.free-slots",
            "Room left in your clan: its capacity less its members."),
        new(IdleMembers, "Members on zero", "clan.standing.idle-members",
            "Members of your clan who have not scored in this battle."),
    ];

    public static FieldMetric? Find(string key) =>
        All.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.Ordinal));

    /// <summary>The ticked keys this catalogue still offers, in catalogue order, however they were stored.</summary>
    public static IReadOnlyList<FieldMetric> Offered(IEnumerable<string> keys)
    {
        var ticked = keys.ToHashSet(StringComparer.Ordinal);
        return [.. All.Where(m => ticked.Contains(m.Key))];
    }

    /// <summary>
    /// What one clans-list read can say about your clan's standing.
    /// <para>
    /// A number missing from <paramref name="summary"/> produces no value rather than a zero: a list that did not
    /// carry member counts must never read as a clan with no free slots, and a read with none of your clans in it
    /// must never read as last place. The only number invented is the gap while you lead, which is nothing to make
    /// up and is genuinely zero.
    /// </para>
    /// </summary>
    /// <param name="mine">Your clan's points over time, for the chase.</param>
    /// <param name="above">
    /// The points of the place above yours over time. A position, not a clan, so <see cref="Pace"/> refuses the
    /// window whenever it fell — which is what a change of place looks like from here.
    /// </param>
    public static IReadOnlyList<FieldMetricValue> Of(
        IReadOnlyDictionary<string, double> summary,
        IReadOnlyList<SeriesPoint> mine,
        IReadOnlyList<SeriesPoint> above,
        DateTimeOffset now,
        DateTimeOffset? ends)
    {
        var values = new List<FieldMetricValue>();
        if (!summary.TryGetValue(FieldSummary.Mine, out var points)) return values;

        Add(Points, points);
        if (summary.TryGetValue(FieldSummary.MineRank, out var rank)) Add(Place, rank);

        // No place above is a gap of nothing, and nothing to work out a pace for.
        var gap = summary.GetValueOrDefault(FieldSummary.GapAbove, 0);
        Add(GapAbove, gap);
        if (gap > 0 && ends is { } end) Add(PaceNeeded, Needed(gap, mine, above, now, end));

        // Room in the clan. A capacity under its own member count is a bad read, not a negative slot, so it says nothing.
        if (summary.TryGetValue(FieldSummary.MineCapacity, out var capacity)
            && summary.TryGetValue(FieldSummary.MineMembers, out var members)
            && capacity >= members)
        {
            Add(FreeSlots, capacity - members);
        }

        // Members on zero, floored at none. Contributors can EXCEED members, because it counts everyone who has
        // scored in this battle including people who have since left the clan — measured on the owner's own board
        // 2026-09-20: 72 members, 73 contributors. Until 0.5.3 that case returned nothing at all, so this number
        // had never once been sent on the board it was written for. Floored rather than dropped: with at least as
        // many scorers as members, no member is known to be sitting on zero, and that is the answer, not a silence.
        if (summary.TryGetValue(FieldSummary.MineMembers, out var roster)
            && summary.TryGetValue(FieldSummary.MineContributors, out var scored))
        {
            Add(IdleMembers, Math.Max(0, roster - scored));
        }

        return values;

        void Add(string key, double? value)
        {
            if (value is { } number && double.IsFinite(number)) values.Add(new FieldMetricValue(Find(key)!, number));
        }
    }

    /// <summary>Null until both paces can be read: with no pace for the place above, what it would take is a guess.</summary>
    private static double? Needed(
        double gap, IReadOnlyList<SeriesPoint> mine, IReadOnlyList<SeriesPoint> above, DateTimeOffset now, DateTimeOffset end)
    {
        var since = now - Pace.LongestCurrent;
        if (Pace.Over(mine, since, Pace.LongestCurrent) is not { } ours) return null;
        if (Pace.Over(above, since, Pace.LongestCurrent) is not { } theirs) return null;

        return Pace.Chase(gap, ours.PerHour, theirs.PerHour, end - now, best: null).Needed;
    }

}
