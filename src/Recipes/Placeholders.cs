using System.Text.RegularExpressions;

namespace Labs626.UrScore.Recipes;

/// <summary><c>{name}</c> tokens: inputs, taken variables, and <c>{userId}</c> in a per-account step.</summary>
internal static partial class Placeholders
{
    public const string UserId = "userId";

    [GeneratedRegex(@"\{([A-Za-z][A-Za-z0-9_]*)\}")]
    private static partial Regex Pattern();

    public static IReadOnlyList<string> Names(string text) =>
        [.. Pattern().Matches(text).Select(m => m.Groups[1].Value)];

    /// <summary>
    /// Percent-encoded when filling a url, as-is when filling a path (spec §3.5). Throws on a name
    /// with no value: the parser guarantees every placeholder is known, so a miss here is a bug, and
    /// a bug should be loud rather than a request to a half-filled address.
    /// </summary>
    public static string Fill(string template, IReadOnlyDictionary<string, string> values, bool encode) =>
        Pattern().Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            if (!values.TryGetValue(name, out var value))
            {
                throw new InvalidOperationException($"No value for {{{name}}} when filling '{template}'.");
            }

            return encode ? Uri.EscapeDataString(value) : value;
        });
}
