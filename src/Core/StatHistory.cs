using System.Globalization;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Core;

/// <summary>
/// The window's own memory of the last value read for each account and stat, so a cell can show what
/// changed since. Display only, like <see cref="PointsRate"/>: never reported, never saved, and
/// cleared whenever the thing being read changes.
/// </summary>
public sealed class StatHistory
{
    private readonly Dictionary<(Guid Account, string Stat), PointsSample> _previous = [];

    /// <summary>Remembers this read and returns the one before it, or null when there was none.</summary>
    public PointsSample? Record(Guid account, string stat, double value, DateTimeOffset at)
    {
        _previous.TryGetValue((account, stat), out var previous);
        _previous[(account, stat)] = new PointsSample(value, at);
        return previous;
    }

    public void Clear() => _previous.Clear();
}

/// <summary>How the window writes stat values. Part 2a's plain form; abbreviation and spans are part 2b.</summary>
public static class StatText
{
    public const string Dash = "—";

    public static string Number(double value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>The value, and the change since the earlier read when there was one: "4,200 (+300)".</summary>
    public static string Cell(double value, PointsSample? previous)
    {
        if (previous is null) return Number(value);

        var change = value - previous.Points;
        return $"{Number(value)} ({(change < 0 ? "-" : "+")}{Number(Math.Abs(change))})";
    }

    /// <summary>The last value sent for each sent stat: the number alone for one stat, labelled for several.</summary>
    public static string LastSent(IReadOnlyList<SentStat> sent, IReadOnlyDictionary<string, double> lastValues)
    {
        var present = sent.Where(stat => lastValues.ContainsKey(stat.Key)).ToList();
        if (present.Count == 0) return Dash;
        if (sent.Count == 1) return Number(lastValues[present[0].Key]);

        return string.Join(" · ", present.Select(stat => $"{stat.Label} {Number(lastValues[stat.Key])}"));
    }

    /// <summary>The recipe's unavailable message for this account, else which shown stats it couldn't read, else nothing.</summary>
    public static string Note(string? unavailable, IReadOnlyList<string> missedLabels) =>
        unavailable ?? (missedLabels.Count == 0 ? "" : $"can't read {string.Join(", ", missedLabels)}");

    private static readonly (double Divisor, string Suffix)[] Units = [(1e12, "T"), (1e9, "B"), (1e6, "M"), (1e3, "K")];

    /// <summary>Short numbers for panels: 950, 1.23K, 220K, 12.4M, 9.17B. A value that would round up to 1000 of a unit moves to the next.</summary>
    public static string Abbrev(double value)
    {
        var sign = value < 0 ? "-" : "";
        var abs = Math.Abs(value);

        foreach (var (divisor, suffix) in Units)
        {
            if (abs < divisor * 0.9995) continue;

            var scaled = abs / divisor;
            var format = scaled >= 99.95 ? "0" : scaled >= 9.995 ? "0.#" : "0.##";
            return sign + scaled.ToString(format, CultureInfo.InvariantCulture) + suffix;
        }

        return sign + abs.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>How long a change took: under an hour in minutes, under two days in hours, else days.</summary>
    public static string Span(TimeSpan span)
    {
        if (span < TimeSpan.FromHours(1)) return $"{Math.Max(1, (int)Math.Round(span.TotalMinutes))}m";
        if (span < TimeSpan.FromHours(48)) return $"{(int)Math.Round(span.TotalHours)}h";
        return $"{(int)Math.Round(span.TotalDays)}d";
    }
}
