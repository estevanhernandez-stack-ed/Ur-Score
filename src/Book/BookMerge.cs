using Labs626.UrScore.Core;

namespace Labs626.UrScore.Book;

using Source = Labs626.UrScore.Core.Source;

/// <summary>
/// What bringing in another PC's book would do: the lines to append, under this PC's own source ids, and what was
/// left alone. <paramref name="NotSetUp"/> names the clans the other machine read that this one does not follow, so
/// they can be added and the book brought in again.
/// </summary>
public sealed record BookMergePlan(
    IReadOnlyList<BookLine> Lines, int Added, int AlreadyHere, IReadOnlyList<string> NotSetUp);

/// <summary>
/// Joining a score book from another PC to this one.
/// <para>
/// Every machine mints its own source ids, so the same clan arrives under an id this one has never seen. A line is
/// matched to a local source by what the source IS — its recipe and the inputs it was set up with, the clan name —
/// never by the id, and it is rewritten to the local id so the two machines' readings become one series rather than
/// two that each look like half a battle.
/// </para>
/// <para>
/// Nothing is invented: a clan this PC does not follow is reported by name and its readings left where they are.
/// </para>
/// </summary>
public static class BookMerge
{
    /// <param name="incoming">Lines read from the other PC's book.</param>
    /// <param name="sources">This PC's sources, which decide where a line can go.</param>
    /// <param name="here">This PC's own lines, so a reading already here is not added twice.</param>
    public static BookMergePlan Plan(
        IReadOnlyList<BookLine> incoming, IReadOnlyList<Source> sources, IReadOnlyList<BookLine> here)
    {
        var lines = new List<BookLine>();
        var notSetUp = new List<string>();
        var already = 0;

        var mine = here.Select(Key).ToHashSet(StringComparer.Ordinal);

        foreach (var line in incoming)
        {
            if (Match(line, sources) is not { } source)
            {
                foreach (var name in line.Inputs.Values.Where(v => !string.IsNullOrWhiteSpace(v)))
                {
                    if (!notSetUp.Contains(name, StringComparer.OrdinalIgnoreCase)) notSetUp.Add(name);
                }

                continue;
            }

            var moved = line with { Source = source.Id };
            if (!mine.Add(Key(moved)))
            {
                already++;
                continue;
            }

            lines.Add(moved);
        }

        return new BookMergePlan(lines, lines.Count, already, notSetUp);
    }

    /// <summary>The local source a line belongs to: same recipe, same inputs, however the other PC typed them.</summary>
    private static Source? Match(BookLine line, IReadOnlyList<Source> sources) =>
        sources.FirstOrDefault(s =>
            string.Equals(s.Recipe, line.Recipe.Slug, StringComparison.Ordinal)
            && s.Inputs.Count == line.Inputs.Count
            && s.Inputs.All(input =>
                line.Inputs.TryGetValue(input.Key, out var value)
                && string.Equals(value, input.Value, StringComparison.OrdinalIgnoreCase)));

    /// <summary>What makes a reading the same reading: one source, one instant, one kind.</summary>
    private static string Key(BookLine line) =>
        $"{line.Source}|{line.Kind}|{line.T.UtcTicks}|{line.Period?.Value}";
}
