using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Book;

/// <summary>
/// Which clans of a list are kept by name, and which are not.
/// <para>
/// The owner's ruling of 2026-09-20 replaced the one of 2026-09-19: a battle board is public game standings, so
/// keeping other clans by name is fine — "it's not as secure as financial information, it's just game stuff" — but
/// not at any size. A hundred clans every five minutes is about 9 MB over a thirteen-day battle; this keeps what a
/// chart can actually use, which is about 3 MB: the top of the board, your own clans, and the place either side of
/// each of them, because those are the ones you are racing.
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
    /// The clans worth keeping from one read, by name, or nothing when the read brought none with that value.
    /// Ranked by the value itself, as <see cref="FieldSummary"/> ranks it, never by the rank the list handed over.
    /// </summary>
    public static IReadOnlyDictionary<string, double> Keep(
        IReadOnlyList<GroupRow> groups, string valueKey, IReadOnlySet<string>? mine)
    {
        var kept = new Dictionary<string, double>(StringComparer.Ordinal);
        if (groups.Count == 0 || string.IsNullOrEmpty(valueKey)) return kept;

        var ranked = groups
            .Select(g => (g.Name, Value: g.Values.TryGetValue(valueKey, out var value) ? value : (double?)null))
            .Where(g => g.Value is not null)
            .OrderByDescending(g => g.Value!.Value)
            .ToList();

        foreach (var (name, value) in ranked.Take(Top)) kept[name] = value!.Value;

        var yours = mine is null ? null : new HashSet<string>(mine, StringComparer.OrdinalIgnoreCase);
        if (yours is null) return kept;

        // Each of your clans, with the place above and the place below it: a chase needs both ends of itself.
        for (var at = 0; at < ranked.Count; at++)
        {
            if (!yours.Contains(ranked[at].Name)) continue;

            for (var near = Math.Max(0, at - 1); near <= Math.Min(ranked.Count - 1, at + 1); near++)
            {
                kept[ranked[near].Name] = ranked[near].Value!.Value;
            }
        }

        return kept;
    }
}
