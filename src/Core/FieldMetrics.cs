using Labs626.UrScore.Board;
using Labs626.UrScore.Book;

namespace Labs626.UrScore.Core;

/// <summary>One clan-and-field number a rule can be set on, under a fixed id every copy of Ur Score sends it as.</summary>
/// <param name="Key">How the tick is stored in the clans list's state.</param>
/// <param name="What">One line, shown beside the tick, and the same sentence a clan leader repeats to the clan.</param>
/// <param name="ManagedLabel">
/// Ur Score writes this number's label and keeps it current; a human never types it. The marker lives here
/// rather than in rules.json because it is nobody else's business: the host's RuleRow has no such field and
/// never reads one.
/// </param>
public sealed record FieldMetric(string Key, string Label, string MetricId, string What, bool ManagedLabel = false);

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
    public const string ThreatGap = "threat-gap";
    public const string ThreatHours = "threat-hours";

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
        new(ThreatGap, "Threat gap", "clan.standing.threat-gap",
            "How far behind the clan closest to taking your place is.", ManagedLabel: true),
        new(ThreatHours, "Threat hours", "clan.standing.threat-hours",
            "How long until the clan behind you takes your place, at both your current paces. Set it to alert "
            + "below six hours to hear about it while you can still answer.", ManagedLabel: true),
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

    /// <summary>The clan behind us that takes our place SOONEST, by time rather than by place.</summary>
    /// <param name="Name">The chaser's name.</param>
    /// <param name="Gap">How far back it is, our latest points minus theirs.</param>
    /// <param name="Hours">How long until its pace, held against ours, closes that gap.</param>
    public sealed record ThreatValue(string Name, double Gap, double Hours);

    /// <summary>
    /// The label that carries a rival's name to the phone: "H8ER catching K0i2", or "H8ER catching up" with no
    /// clan of your own to name.
    /// <para>
    /// Mirrors <c>AlertCards.ClanLabel</c> (src/UI/Setup/AlertCards.cs), which does the same job for YOUR
    /// clan's name and lets forty members set one shared metric id while each phone shows its own clan. That
    /// split works because a fixed id can carry a label that is rewritten locally. This is the same split for a
    /// name that is not merely local but LIVE: the id (<c>clan.standing.threat-hours</c> /
    /// <c>clan.standing.threat-gap</c>) never changes, but the clan closing on you this hour is not the clan that
    /// was closing on you last hour, so the label is rewritten every time <see cref="SoonestThreat"/> names
    /// someone new. A metric carries no name of its own — the label is the only channel a rival's name can ride
    /// to RoRoRo at all, and it can only ride it because the label is ours to keep rewriting.
    /// </para>
    /// </summary>
    /// <param name="threat">The chaser's name, from <see cref="ThreatValue.Name"/>.</param>
    /// <param name="clan">Your own clan's name, or null when it is not known.</param>
    public static string ThreatLabel(string threat, string? clan) =>
        string.IsNullOrWhiteSpace(clan) ? $"{threat} catching up" : $"{threat} catching {clan.Trim()}";

    /// <summary>
    /// The clan behind us that takes our place SOONEST, by time rather than by place.
    /// <para>
    /// A clan three places back going much faster passes us before the one directly behind, so the nearest by
    /// place is the wrong answer, and a confident wrong name is worse than silence on a phone mid-battle (design
    /// §4). A chaser whose pace cannot be read yet is no threat rather than a guess, matching <see cref="Needed"/>.
    /// </para>
    /// <para>
    /// <paramref name="ends"/> caps the answer: a crossing worked out to fall after the battle ends is not a
    /// threat, because the pass can never happen — it is fiction produced by extrapolating a pace past the clock
    /// running out. This mirrors <see cref="Pace.Chase"/>, which nulls its <c>catchIn</c> the same way once it
    /// exceeds the time left (src/Board/Pace.cs), and <see cref="Needed"/>, which refuses to answer at all
    /// without a battle end. With <paramref name="ends"/> null there is no cap.
    /// </para>
    /// </summary>
    /// <param name="behind">The clans below us, best placed first, each with its own precomputed gap.</param>
    /// <param name="mine">Our own points over time, for our pace.</param>
    /// <param name="seriesOf">Looks up a chaser's points over time by name, for its pace.</param>
    /// <param name="now">When this read happened.</param>
    /// <param name="ends">When the battle ends, or null when there is no end to cap a crossing against.</param>
    public static ThreatValue? SoonestThreat(
        IReadOnlyList<FieldSummary.BehindClan> behind,
        IReadOnlyList<SeriesPoint> mine,
        Func<string, IReadOnlyList<SeriesPoint>> seriesOf,
        DateTimeOffset now,
        DateTimeOffset? ends)
    {
        var since = now - Pace.LongestCurrent;
        if (Pace.Over(mine, since, Pace.LongestCurrent) is not { } ours) return null;

        ThreatValue? soonest = null;
        foreach (var clan in behind)
        {
            if (Pace.Over(seriesOf(clan.Name), since, Pace.LongestCurrent) is not { } theirs) continue;

            // theirs - ours: the direction a clan behind us has to gain to become a clan ahead of us. This is the
            // opposite of Pace.Chase's `closing` (mine - theirs, us catching them) — a threat is them catching us.
            var closing = theirs.PerHour - ours.PerHour;
            if (closing <= 0) continue;

            var hours = clan.Gap / closing;
            if (!double.IsFinite(hours) || hours < 0) continue;

            // A crossing after the battle ends never happens; it is an artifact of extrapolating a pace past the
            // clock, not a threat (controller ruling, this task). Compared in hours, never by building the
            // instant: two clans at near-identical paces differ only by floating-point noise, so `closing` can
            // land near zero and drive `hours` past what DateTimeOffset can represent — `now.AddHours(hours)`
            // throws ArgumentOutOfRangeException in that case. This comparison is the same decision in double
            // arithmetic and cannot throw (review finding, this task).
            if (ends is { } end && hours > (end - now).TotalHours) continue;

            if (soonest is null || hours < soonest.Hours) soonest = new ThreatValue(clan.Name, clan.Gap, hours);
        }

        return soonest;
    }
}
