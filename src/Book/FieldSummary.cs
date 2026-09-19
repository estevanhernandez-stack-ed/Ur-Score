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

    private const int Cohort = 10;

    /// <summary>
    /// The summary of one clans-list read, or nothing when the read brought no clan with that value.
    /// <para>
    /// Ranked by the value itself, never by the rank the list handed over: measured on the live board on
    /// 2026-09-19, the top-100 list's own rank disagreed with its own points — rank 10 held more than rank 9.
    /// A list shorter than ten clans is its own top and bottom, and <see cref="Clans"/> says how many that was.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<string, double> Of(IReadOnlyList<GroupRow> groups, string valueKey)
    {
        if (groups.Count == 0 || string.IsNullOrEmpty(valueKey)) return new Dictionary<string, double>(StringComparer.Ordinal);

        var points = groups
            .Select(g => g.Values.TryGetValue(valueKey, out var value) ? value : (double?)null)
            .OfType<double>()
            .OrderByDescending(value => value)
            .ToList();

        if (points.Count == 0) return new Dictionary<string, double>(StringComparer.Ordinal);

        return new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [Leader] = points[0],
            [Top10] = points.Take(Cohort).Average(),
            [Average] = points.Average(),
            [Bottom10] = points.TakeLast(Cohort).Average(),
            [Clans] = points.Count,
        };
    }
}
