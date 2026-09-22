using System.Globalization;
using Labs626.UrScore.Core;

namespace Labs626.UrScore.Book;

public sealed record SeriesPoint(DateTimeOffset T, double Value, DateTimeOffset? AsOf, bool Stale, int Off);

/// <summary>
/// One finished period as the book holds it. <paramref name="T"/> is when the book first learned it, not when
/// the period ran: a backfill writes a whole record in one cycle, so every entry from that cycle shares one T.
/// <paramref name="Kept"/> is where the line sits in the book, which is where the period sits in the record the
/// source keeps — a source lists its finished periods oldest first, so a larger <paramref name="Kept"/> is the
/// more recent period. It is the only ordering signal a final has: the record carries no dates and
/// <see cref="LineBuilder.Final"/> writes the period's name alone.
/// </summary>
public sealed record FinalEntry(
    string Period, DateTimeOffset T, int Kept, IReadOnlyDictionary<string, double> Headline, IReadOnlyDictionary<long, BookAccount> Accounts);

/// <summary>
/// The book as panels read it (score book spec §9.1). Keeps reading lines from the last
/// <see cref="KeepReadings"/> and every final (Ruling R5); counts every reading it loaded.
/// </summary>
public sealed class ScoreBookReader(string root, TimeProvider time)
{
    public static readonly TimeSpan KeepReadings = TimeSpan.FromDays(35);

    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private readonly Dictionary<string, SlugData> _slugs = new(StringComparer.Ordinal);

    /// <summary>
    /// One cutoff for the whole load, and a line older than it is never added, so nothing is pruned here. A line
    /// that can't be taken in (a corrupt one that slipped past <see cref="BookJson.TryParse"/>) is skipped, never
    /// the book — and COUNTED, so the caller can say so. Returns how many were skipped. A silent skip was the
    /// wrong kind of quiet: a book with a hundred unreadable lines loaded exactly like a book with none, and the
    /// numbers were simply lower with nothing anywhere explaining why (S1-F.10).
    /// </summary>
    public int Load(IEnumerable<string> slugs)
    {
        var cutoff = time.GetUtcNow() - KeepReadings;
        var skipped = 0;
        foreach (var slug in slugs.Distinct(StringComparer.Ordinal))
        {
            var data = new SlugData();
            foreach (var line in BookFiles.ReadAll(root, slug))
            {
                try
                {
                    data.Add(line, cutoff);
                }
                catch (Exception)
                {
                    skipped++;
                }
            }

            lock (_gate) _slugs[slug] = data;
        }

        return skipped;
    }

    public void Apply(BookLine line)
    {
        var cutoff = time.GetUtcNow() - KeepReadings;
        lock (_gate)
        {
            if (!_slugs.TryGetValue(line.Recipe.Slug, out var data)) _slugs[line.Recipe.Slug] = data = new SlugData();
            data.Add(line, cutoff);
            data.Prune(cutoff);
        }
    }

    public IReadOnlyList<SeriesPoint> Series(string sourceId, long userId, string stat, string? period, DateTimeOffset since)
    {
        var id = userId.ToString(CultureInfo.InvariantCulture);
        var points = Readings(sourceId, period, since)
            .Where(l => l.Accounts.TryGetValue(id, out var a) && a.V.ContainsKey(stat))
            .Select(l =>
            {
                var account = l.Accounts[id];
                return new SeriesPoint(l.T, account.V[stat], account.AsOf ?? l.AsOf, account.Stale ?? l.Stale ?? false, l.Off);
            });

        return Collapse(points);
    }

    /// <summary>
    /// One clan's points over time, from the rows a clans list kept by name (<see cref="GroupRows"/>). Empty for a
    /// clan that was never kept — the top of the board, your own and their neighbours are what a list keeps.
    /// </summary>
    public IReadOnlyList<SeriesPoint> GroupSeries(string sourceId, string groupName, string? period) =>
        Collapse(Readings(sourceId, period, DateTimeOffset.MinValue)
            .Where(l => l.Groups is not null && l.Groups.ContainsKey(groupName))
            .Select(l => new SeriesPoint(l.T, l.Groups![groupName], l.AsOf, l.Stale ?? false, l.Off)));

    /// <summary>Every clan the latest reading kept, best placed first: what a chart can offer to draw.</summary>
    public IReadOnlyList<(string Name, double Value)> GroupsLatest(string sourceId, string? period)
    {
        var last = Readings(sourceId, period, DateTimeOffset.MinValue).LastOrDefault(l => l.Groups is { Count: > 0 });
        return last is null ? [] : [.. last.Groups!.OrderByDescending(g => g.Value).Select(g => (g.Key, g.Value))];
    }

    public IReadOnlyList<SeriesPoint> HeadlineSeries(string sourceId, string headlineId, string? period) =>
        Collapse(Readings(sourceId, period, DateTimeOffset.MinValue)
            .Where(l => l.Headline.ContainsKey(headlineId))
            .Select(l => new SeriesPoint(l.T, l.Headline[headlineId], l.AsOf, l.Stale ?? false, l.Off)));

    public IReadOnlyList<FinalEntry> Finals(string slug, string inputsKey)
    {
        List<BookLine> finals;
        lock (_gate) finals = _slugs.TryGetValue(slug, out var data) ? [.. data.Finals] : [];

        return [.. finals
            .Select((line, order) => (line, order))
            .Where(x => x.line.Period is not null && Source.KeyOf(x.line.Inputs) == inputsKey)
            .GroupBy(x => x.line.Period!.Value, StringComparer.Ordinal)
            .Select(g =>
            {
                // Two clocks for one entry, on purpose. Its PLACE in the list is the earliest final, because that
                // is closest to when the period ended, and a correction written days later must not make a
                // battle jump to the top as though it had just finished (which is why S1-9.4 was not a defect).
                // Its CONTENT is the latest final, because a later final is a correction and the whole point of
                // a correction is to replace what it corrects; the reader used to keep the first and drop the
                // rest (S1-9.2). Latest by T rather than by file order: a "Bring in stats" merge appends another
                // machine's lines after this one's whatever their times, so position in the file says nothing.
                var first = g.OrderBy(x => x.line.T).ThenBy(x => x.order).First();
                var latestFirst = g.OrderByDescending(x => x.line.T).ThenByDescending(x => x.order).ToList();

                var accounts = new Dictionary<long, BookAccount>();
                foreach (var (line, _) in latestFirst)
                {
                    foreach (var (key, account) in line.Accounts)
                    {
                        if (long.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var userId)) accounts.TryAdd(userId, account);
                    }
                }

                return (Entry: new FinalEntry(g.Key, first.line.T, first.order, latestFirst[0].line.Headline, accounts), first.order);
            })
            .OrderByDescending(x => x.Entry.T)
            .ThenByDescending(x => x.order)
            .Select(x => x.Entry)];
    }

    public int Readings(string slug)
    {
        lock (_gate) return _slugs.TryGetValue(slug, out var data) ? data.ReadingCount : 0;
    }

    public DateTimeOffset? FirstReading(string slug)
    {
        lock (_gate) return _slugs.TryGetValue(slug, out var data) ? data.First : null;
    }

    /// <summary>
    /// The newest reading this source kept, or null. Finals are not readings, and a line older than
    /// <see cref="KeepReadings"/> was never loaded, so an untouched source eventually has nothing to give back.
    /// </summary>
    public BookLine? LastReading(string sourceId)
    {
        lock (_gate)
        {
            return _slugs.Values
                .SelectMany(d => d.Readings)
                .Where(l => string.Equals(l.Source, sourceId, StringComparison.Ordinal))
                .OrderBy(l => l.T)
                .LastOrDefault();
        }
    }

    public long Bytes(string slug) => BookFiles.Bytes(root, slug);

    private List<BookLine> Readings(string sourceId, string? period, DateTimeOffset since)
    {
        lock (_gate)
        {
            return [.. _slugs.Values
                .SelectMany(d => d.Readings)
                .Where(l => l.Source == sourceId && (period is null ? l.T >= since : l.Period?.Value == period))
                .OrderBy(l => l.T)];
        }
    }

    /// <summary>Ruling R4.</summary>
    private static List<SeriesPoint> Collapse(IEnumerable<SeriesPoint> points)
    {
        var kept = new List<SeriesPoint>();
        foreach (var point in points)
        {
            if (kept.Count > 0)
            {
                var last = kept[^1];
                var same = last.Value.Equals(point.Value)
                           && (last.AsOf is not null && point.AsOf is not null
                               ? last.AsOf == point.AsOf
                               : last.AsOf is null && point.AsOf is null && point.T - last.T < DuplicateWindow);
                if (same) continue;
            }

            kept.Add(point);
        }

        return kept;
    }

    private sealed class SlugData
    {
        /// <summary>The oldest reading in <see cref="Readings"/>, so <see cref="Prune"/> knows without scanning.</summary>
        private DateTimeOffset? _oldestKept;

        public List<BookLine> Readings { get; } = [];

        public List<BookLine> Finals { get; } = [];

        public int ReadingCount { get; private set; }

        public DateTimeOffset? First { get; private set; }

        /// <summary>Never prunes: a line older than <paramref name="cutoff"/> is only counted, not kept.</summary>
        public void Add(BookLine line, DateTimeOffset cutoff)
        {
            if (line.Kind == BookLine.KindFinal)
            {
                Finals.Add(line);
                return;
            }

            if (line.Kind != BookLine.KindRead) return;

            ReadingCount++;
            if (First is null || line.T < First) First = line.T;
            if (line.T < cutoff) return;

            Readings.Add(line);
            if (_oldestKept is null || line.T < _oldestKept) _oldestKept = line.T;
        }

        /// <summary>
        /// The cutoff moves forward with the clock, so a reading kept on an earlier call can age out later without
        /// ever being re-added. Only when the oldest kept reading is past the cutoff is the list walked at all:
        /// RemoveAll rather than trimming the front, since callers of Apply don't guarantee strict time order.
        /// </summary>
        public void Prune(DateTimeOffset cutoff)
        {
            if (_oldestKept is not { } oldest || oldest >= cutoff) return;

            Readings.RemoveAll(l => l.T < cutoff);
            _oldestKept = Readings.Count == 0 ? null : Readings.Min(l => l.T);
        }
    }
}
