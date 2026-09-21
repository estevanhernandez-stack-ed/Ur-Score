using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Book;

/// <summary>
/// Which clans of a list are kept by name, and which are not.
/// <para>
/// The owner's ruling of 2026-09-20 replaced the one of 2026-09-19: a battle board is public game standings, so
/// keeping other clans by name is fine — "it's not as secure as financial information, it's just game stuff" — but
/// not at any size. A hundred clans every five minutes is about 9 MB over a thirteen-day battle; this keeps what a
/// chart can actually use: the top of the board, your own clans, and the <see cref="Neighbours"/> places either side
/// of each of them, because those are the ones you are racing. Around 3 MB when the top 25 covers the band, a third
/// more when it does not.
/// </para>
/// <para>
/// <see cref="FieldSummary"/> still rides on the same line and still covers the whole field, so the bands (leader,
/// top ten, average, bottom ten) stay true even for the clans no longer kept by name.
/// </para>
/// </summary>
public static class GroupRows
{
    /// <summary>How much of the board is kept by name. The chart shows ten or twenty; this leaves room to choose.</summary>
    public const int Top = 25;

    /// <summary>
    /// How many places either side of each of your clans are kept: the ones a change of place is decided against.
    /// <para>
    /// The race chart derives its band from this rather than naming a number of its own. It used to name one, and
    /// the two disagreed — the chart drew three either side while the book kept one, so a clan placed below the top
    /// 25 was charted with four of its six neighbours simply absent and nothing saying so (V3-S.34). The writer owns
    /// the number because the reader cannot draw what was never written.
    /// </para>
    /// </summary>
    public const int Neighbours = 3;

    /// <summary>
    /// The clans worth keeping from one read, by name, or nothing when the read brought none with that value —
    /// and nothing at all unless <paramref name="groupsAreClans"/>, the recipe's own claim that its groups are
    /// clans and not people (V3-S.25). Ranked by the value itself, as <see cref="FieldSummary"/> ranks it, never
    /// by the rank the list handed over.
    /// </summary>
    public static IReadOnlyDictionary<string, double> Keep(
        IReadOnlyList<GroupRow> groups, string valueKey, IReadOnlySet<string>? mine, bool groupsAreClans)
    {
        var kept = new Dictionary<string, double>(StringComparer.Ordinal);
        if (groups.Count == 0 || string.IsNullOrEmpty(valueKey)) return kept;

        // The recipe has to SAY its groups are clans before one name goes to disk, because "group" is only ever
        // whatever its groupName points at and a list of PLAYERS has the identical shape. FieldSummary rides the
        // same line and covers the whole field either way, so the bands stay true with nothing here named.
        if (!groupsAreClans) return kept;

        var ranked = groups
            .Select(g => (g.Name, Value: g.Values.TryGetValue(valueKey, out var value) ? value : (double?)null))
            .Where(g => g.Value is not null)
            .OrderByDescending(g => g.Value!.Value)
            .ToList();

        foreach (var (name, value) in ranked.Take(Top)) kept[name] = value!.Value;

        var yours = mine is null ? null : new HashSet<string>(mine, StringComparer.OrdinalIgnoreCase);
        if (yours is null) return kept;

        // Each of your clans, with the places above and below it: a chase needs both ends of itself, and as many
        // of them as the chart is going to draw.
        for (var at = 0; at < ranked.Count; at++)
        {
            if (!yours.Contains(ranked[at].Name)) continue;

            var first = Math.Max(0, at - Neighbours);
            var last = Math.Min(ranked.Count - 1, at + Neighbours);
            for (var near = first; near <= last; near++)
            {
                kept[ranked[near].Name] = ranked[near].Value!.Value;
            }
        }

        return kept;
    }
}
