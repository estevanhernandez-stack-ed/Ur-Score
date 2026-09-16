using System.Globalization;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Book;

// The bare name Source would find the Labs626.UrScore.Source namespace from in here; LineBuilder writes it out in full
// for the same reason.
using Source = Labs626.UrScore.Core.Source;

/// <summary>
/// The last numbers the score book kept, as a snapshot the panels can draw before the first read of the session lands
/// (plan A37-A42). The reverse of <see cref="LineBuilder"/>.
/// <para>
/// It inherits that class's privacy rules for free: the book only ever held YOUR accounts and the source's own
/// headline, so nothing here can put another member back on screen. It also cannot invent one — every field a
/// snapshot has that the book has no answer for is left empty.
/// </para>
/// <para>
/// Never a read and never a report. These snapshots live in their own map, never in <c>_latest</c>, and reach the
/// window only through <c>LiveBoard.SnapshotOf</c> — so the state line, Diagnostics and every Setup page go on
/// seeing only what was actually read. Every one of them carries <see cref="RecipeSnapshot.RememberedAt"/>.
/// </para>
/// </summary>
public static class Remembered
{
    /// <summary>The last reading each enabled source kept, by source id. A source with nothing usable kept is absent.</summary>
    public static IReadOnlyDictionary<string, RecipeSnapshot> ForSources(
        ScoreBookReader reader, IReadOnlyList<Source> sources, IReadOnlyList<InstalledRecipe> installed, IReadOnlySet<long> yourUserIds)
    {
        var map = new Dictionary<string, RecipeSnapshot>(StringComparer.Ordinal);

        foreach (var source in sources.Where(s => s.Enabled))
        {
            if (installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, source.Recipe, StringComparison.Ordinal))?.Recipe is not { } recipe) continue;
            if (reader.LastReading(source.Id) is not { } line) continue;
            if (From(line, source, recipe, yourUserIds) is { } snapshot) map[source.Id] = snapshot;
        }

        return map;
    }

    /// <summary>
    /// One kept reading as a snapshot, or null when it no longer describes this source: a final rather than a
    /// reading, another recipe, other inputs, or nothing left in it that this recipe still offers (plan A42).
    /// </summary>
    public static RecipeSnapshot? From(BookLine line, Source source, Recipe recipe, IReadOnlySet<long> yourUserIds)
    {
        if (line.Kind != BookLine.KindRead) return null;
        if (!string.Equals(line.Recipe.Slug, recipe.Slug, StringComparison.Ordinal)) return null;

        // The same clan, the same inputs: a line written for another one is about something else entirely. The recipe
        // HASH is deliberately not compared — an update doesn't make yesterday's number untrue, and a stat the new
        // recipe can't name is dropped below anyway.
        if (!string.Equals(Source.KeyOf(line.Inputs), source.InputsKey, StringComparison.Ordinal)) return null;

        var rows = new List<RecipeRow>();
        var ranks = new Dictionary<(long UserId, string Stat), RankInGroup>();
        foreach (var (id, account) in line.Accounts)
        {
            // An id RoRoRo isn't listing as yours right now never comes back, whatever the book holds.
            if (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var userId) || !yourUserIds.Contains(userId)) continue;

            var values = account.V
                .Where(kv => RecipeStats.Find(recipe, kv.Key) is not null && double.IsFinite(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            if (values.Count == 0) continue;

            rows.Add(new RecipeRow(userId, values));

            // The place the reading itself worked out, among every row it saw, and how many that was (review C1).
            // Carried rather than dropped because the alternative downstream is a place worked out from YOUR rows
            // alone — "#1 of 4" of a group this never counted. A line that kept no place gives none, and the panel
            // then shows nothing, which is the honest answer.
            if (account.Of is not { } of || of <= 0 || account.Rank is not { } kept) continue;

            foreach (var (stat, rank) in kept)
            {
                if (values.ContainsKey(stat)) ranks[(userId, stat)] = new RankInGroup(rank, of);
            }
        }

        var headline = recipe.Headline
            .Where(h => h.Id.Length > 0 && line.Headline.ContainsKey(h.Id))
            .Select(h => new HeadlineValue(h.Label, line.Headline[h.Id].ToString(CultureInfo.InvariantCulture))
            {
                Id = h.Id,
                Number = line.Headline[h.Id],
            })
            .ToList();

        if (rows.Count == 0 && headline.Count == 0) return null;

        // State is never read for one of these (plan A38); Showing is the honest one of the fourteen if it ever were.
        return new RecipeSnapshot(WatchState.Showing, null, [], [], rows.Count, null, rows, headline)
        {
            SourceId = source.Id,
            Period = line.Period is { } period ? new ReadingPeriod(period.Value, period.Starts, period.Ends) : null,
            RememberedAt = line.T,
            RememberedRanks = ranks,
        };
    }
}
