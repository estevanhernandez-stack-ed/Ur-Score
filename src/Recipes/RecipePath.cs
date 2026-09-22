using System.Text.Json;
using Labs626.UrScore.Fetch;

namespace Labs626.UrScore.Recipes;

public enum PathOutcome
{
    /// <summary>The value is there.</summary>
    Found,

    /// <summary>
    /// A JSON null on the way, or an empty string at the end: the source saying "nothing here right
    /// now". This is what <c>idleWithout</c> treats as idle.
    /// </summary>
    Nothing,

    /// <summary>
    /// A key that is not there, or a step into something that is not an object. The shape changed,
    /// and this must never read as <see cref="Nothing"/> (plan Ruling 1).
    /// </summary>
    Missing,
}

/// <summary>
/// <see cref="MissedAtPlaceholder"/> is true only for a <see cref="PathOutcome.Missing"/> whose key
/// came, whole, from a placeholder such as <c>{battle}</c>, looked up in an object that exists. That is
/// the one miss a step's <c>absentMessage</c> may read as idle (stats design §3.2).
/// </summary>
public readonly record struct PathResult(PathOutcome Outcome, JsonElement Value, string? Miss, bool MissedAtPlaceholder = false);

/// <summary>
/// Dot-separated keys, matched case-insensitively (spec §3.5). A key containing a literal dot is not
/// addressable in format 1.
/// </summary>
public static class RecipePath
{
    /// <summary>A path whose placeholders the caller has already filled.</summary>
    public static PathResult Resolve(JsonElement root, string path, string rootName = "the response") =>
        Walk(root, [.. path.Split('.').Select(segment => (segment, false))], rootName);

    /// <summary>
    /// A path template, filled here so a miss can say whether its key came from a placeholder. Filled
    /// segment by segment and then split again on dots, so a placeholder value containing a dot walks
    /// exactly as it does through <see cref="Resolve(JsonElement, string, string)"/>; only a segment
    /// that stays one key is marked as having come from a placeholder.
    /// </summary>
    public static PathResult Resolve(
        JsonElement root, string template, IReadOnlyDictionary<string, string> values, string rootName = "the response")
    {
        var segments = new List<(string Key, bool FromPlaceholder)>();
        foreach (var part in template.Split('.'))
        {
            var filled = Placeholders.Fill(part, values, encode: false).Split('.');
            var whole = filled.Length == 1 && Placeholders.Names(part) is [var name] && part == $"{{{name}}}";
            segments.AddRange(filled.Select(key => (key, whole)));
        }

        return Walk(root, segments, rootName);
    }

    /// <summary>A value as text for use in a later address or path: strings as-is, numbers as written.</summary>
    public static string? AsText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null,
    };

    private static PathResult Walk(JsonElement root, IReadOnlyList<(string Key, bool FromPlaceholder)> segments, string rootName)
    {
        var current = root;
        var walked = new List<string>();

        foreach (var (segment, fromPlaceholder) in segments)
        {
            if (current.ValueKind == JsonValueKind.Null)
            {
                return new PathResult(PathOutcome.Nothing, default, null);
            }

            if (current.ValueKind != JsonValueKind.Object)
            {
                return new PathResult(PathOutcome.Missing, default,
                    $"{Where(walked, rootName)} is {Describe(current)}, not an object, so '{segment}' cannot be read from it.");
            }

            if (!JsonNav.TryGet(current, segment, out var next))
            {
                return new PathResult(PathOutcome.Missing, default,
                    $"No '{segment}' in {Where(walked, rootName)}. Keys present: {KeysText(current)}.", fromPlaceholder);
            }

            walked.Add(segment);
            current = next;
        }

        if (current.ValueKind == JsonValueKind.Null
            || (current.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(current.GetString())))
        {
            return new PathResult(PathOutcome.Nothing, default, null);
        }

        return new PathResult(PathOutcome.Found, current, null);
    }

    /// <summary>
    /// Keys present, for a miss message. All-digit keys are never listed by value: an object keyed
    /// by user id would otherwise copy other members' ids into DetailLine, the trail and diagnostics,
    /// which the Global Constraint forbids. They are counted instead.
    /// </summary>
    private static string KeysText(JsonElement element)
    {
        var keys = JsonNav.Keys(element);
        if (keys.Count == 0) return "none";

        var named = keys.Where(key => !key.All(char.IsAsciiDigit)).ToList();
        var numericCount = keys.Count - named.Count;

        if (numericCount == 0) return string.Join(", ", named);

        var suffix = numericCount == 1 ? "1 numeric key" : $"{numericCount} numeric keys";
        return named.Count == 0 ? suffix : $"{string.Join(", ", named)}, and {suffix}";
    }

    private static string Where(List<string> walked, string rootName) =>
        walked.Count == 0 ? rootName : $"'{string.Join('.', walked)}'";

    private static string Describe(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => "a list",
        JsonValueKind.String => "text",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "true or false",
        _ => element.ValueKind.ToString().ToLowerInvariant(),
    };
}
