using System.Globalization;

namespace Labs626.UrScore.Recipes;

/// <summary>
/// What a reading belongs to (score book spec §3.1). <see cref="Value"/>, <see cref="Starts"/> and
/// <see cref="Ends"/> name takes from earlier steps; <see cref="Past"/> is a path in the last step's
/// response to an object whose keys are period values.
/// </summary>
public sealed record RecipePeriod(string Value, string? Starts, string? Ends, string? Past);

/// <summary>The source's own snapshot time, and whether it says the data is stale (score book spec §3.2).</summary>
public sealed record RecipeAsOf(string Time, string? Stale);

public static class TimeText
{
    /// <summary>Latest instant a time can name: the last second of year 9999.</summary>
    private const long MaxUnixSeconds = 253402300799;

    /// <summary>All digits is unix seconds; anything else must be ISO-8601. Returns UTC, or null.</summary>
    public static DateTimeOffset? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();

        if (text.All(char.IsAsciiDigit))
        {
            return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                   && seconds is > 0 and <= MaxUnixSeconds
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }
}

public static class PathRules
{
    /// <summary>
    /// A dot segment with no placeholder that is only digits. <c>RecipePath</c> walks objects by key and
    /// never indexes lists, so such a segment can only name an object key, and in these APIs that is a
    /// player's user id. A recipe must never point at a particular player (score book spec §3.6).
    /// </summary>
    public static bool HasLiteralNumber(string path) =>
        path.Split('.').Any(segment => segment.Length > 0 && !segment.Contains('{') && segment.All(char.IsAsciiDigit));
}
