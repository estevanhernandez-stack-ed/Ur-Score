using System.Text.Json;
using Labs626.UrScore.Source;

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

public readonly record struct PathResult(PathOutcome Outcome, JsonElement Value, string? Miss);

/// <summary>
/// Dot-separated keys, matched case-insensitively (spec §3.5). Placeholders are filled by the caller
/// before this sees the path. A key containing a literal dot is not addressable in format 1.
/// </summary>
public static class RecipePath
{
    public static PathResult Resolve(JsonElement root, string path, string rootName = "the response")
    {
        var current = root;
        var walked = new List<string>();

        foreach (var segment in path.Split('.'))
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
                var keys = JsonNav.Keys(current);
                return new PathResult(PathOutcome.Missing, default,
                    $"No '{segment}' in {Where(walked, rootName)}. Keys present: "
                    + $"{(keys.Count == 0 ? "none" : string.Join(", ", keys))}.");
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

    /// <summary>A value as text for use in a later address or path: strings as-is, numbers as written.</summary>
    public static string? AsText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null,
    };

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
