using System.Globalization;
using Labs626.UrScore.Core;

namespace Labs626.UrScore.Book;

public sealed record SeriesPoint(DateTimeOffset T, double Value, DateTimeOffset? AsOf, bool Stale, int Off);

public sealed record FinalEntry(string Period, DateTimeOffset T, IReadOnlyDictionary<string, double> Headline, IReadOnlyDictionary<long, BookAccount> Accounts);

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

    public void Load(IEnumerable<string> slugs)
    {
        var cutoff = time.GetUtcNow() - KeepReadings;
        foreach (var slug in slugs.Distinct(StringComparer.Ordinal))
        {
            var data = new SlugData();
            foreach (var line in BookFiles.ReadAll(root, slug)) data.Add(line, cutoff);

            lock (_gate) _slugs[slug] = data;
        }
    }

    public void Apply(BookLine line)
    {
        var cutoff = time.GetUtcNow() - KeepReadings;
        lock (_gate)
        {
            if (!_slugs.TryGetValue(line.Recipe.Slug, out var data)) _slugs[line.Recipe.Slug] = data = new SlugData();
            data.Add(line, cutoff);
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
            .Where(x => x.line.Period is not null && Labs626.UrScore.Core.Source.KeyOf(x.line.Inputs) == inputsKey)
            .GroupBy(x => x.line.Period!.Value, StringComparer.Ordinal)
            .Select(g =>
            {
                var first = g.OrderBy(x => x.order).First();
                var accounts = new Dictionary<long, BookAccount>();
                foreach (var (line, _) in g.OrderBy(x => x.order))
                {
                    foreach (var (key, account) in line.Accounts)
                    {
                        if (long.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var userId)) accounts.TryAdd(userId, account);
                    }
                }

                return (Entry: new FinalEntry(g.Key, g.Min(x => x.line.T), first.line.Headline, accounts), first.order);
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
        public List<BookLine> Readings { get; } = [];

        public List<BookLine> Finals { get; } = [];

        public int ReadingCount { get; private set; }

        public DateTimeOffset? First { get; private set; }

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
            if (line.T >= cutoff) Readings.Add(line);
        }
    }
}
