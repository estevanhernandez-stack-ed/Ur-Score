using System.Globalization;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

/// <summary>How panels write numbers, places and times (spec §9.6: change states its span; a missing value is a dash).</summary>
public static class PanelText
{
    public const string StaleStat = "This panel's stat was removed.";

    public static string Ordinal(int place)
    {
        var tens = place % 100;
        var suffix = tens is >= 11 and <= 13 ? "th" : (place % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return place.ToString("N0", CultureInfo.InvariantCulture) + suffix;
    }

    /// <summary>Every digit: "14,020,550". A missing value is a dash, never 0.</summary>
    public static string Full(double? value) => value is { } v ? StatText.Number(v) : StatText.Dash;

    /// <summary>Abbreviated: "12.4M".</summary>
    public static string Short(double? value) =>
        value is not { } v ? StatText.Dash : v < 0 ? "-" + StatText.Abbrev(-v) : StatText.Abbrev(v);

    /// <summary>A change with its sign: "+220K", "-1.5K".</summary>
    public static string Signed(double? value) =>
        value is not { } v ? StatText.Dash : v < 0 ? "-" + StatText.Abbrev(-v) : "+" + StatText.Abbrev(v);

    public static string Chip(SourceRole role) => role switch
    {
        SourceRole.Main => "★ main",
        SourceRole.Mine => "yours",
        _ => "watching",
    };

    public static string Ago(DateTimeOffset? then, DateTimeOffset now) =>
        then is { } at ? $"{StatText.Span(now - at)} ago" : "never";

    public static string NextRead(DateTimeOffset due, DateTimeOffset now) =>
        due <= now ? "next read due" : $"next read in {StatText.Span(due - now)}";

    /// <summary>"AutumnBattle · ends in 3d · next read in 2m" (spec §8's top bar line).</summary>
    public static string PeriodLine(ReadingPeriod? period, DateTimeOffset now, DateTimeOffset? nextRead)
    {
        var parts = new List<string>();
        if (period is not null)
        {
            parts.Add(period.Value);
            if (period.Ends is { } ends) parts.Add(ends > now ? $"ends in {StatText.Span(ends - now)}" : "ended");
        }

        if (nextRead is { } next) parts.Add(NextRead(next, now));
        return string.Join(" · ", parts);
    }

    public static string StaleSource(string group) => $"This panel's {group} was removed.";
}
