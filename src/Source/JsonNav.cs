using System.Text.Json;

namespace Labs626.UrScore.Source;

/// <summary>
/// Case-insensitive property lookup and forgiving number reading, shared by the recipe reader and
/// the username lookup. A casing change in a response must not read as a missing field.
/// </summary>
internal static class JsonNav
{
    /// <summary>Every property name on an object, in document order. Empty for a non-object.</summary>
    public static IReadOnlyList<string> Keys(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
            ? [.. element.EnumerateObject().Select(p => p.Name)]
            : [];

    /// <summary>Case-insensitive single-level property lookup.</summary>
    public static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object) return false;

        // TryGetProperty is case-SENSITIVE; a casing change in the response would otherwise read
        // as a missing field, which is the silent-empty failure this whole file exists to avoid.
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A number from any of the shapes this API has been seen to use for one: a JSON number, or a
    /// string holding one. Returns false for anything else, including a non-finite value.
    /// </summary>
    public static bool TryNumber(JsonElement element, out double value)
    {
        value = 0;

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var n))
        {
            value = n;
        }
        else if (element.ValueKind == JsonValueKind.String
                 && double.TryParse(element.GetString(), out var parsed))
        {
            value = parsed;
        }
        else
        {
            return false;
        }

        // A non-finite value would poison the host's history silently. Refuse it here as well as
        // at the report policy, because two gates on the same class of nonsense is cheap.
        return double.IsFinite(value);
    }

    /// <summary>
    /// A Roblox user id, which must be a whole number a long can hold exactly. A double happily
    /// holds 1e20, and casting that to long yields long.MaxValue — fabricating an id rather than
    /// rejecting the row. 2^53 is the largest integer a double represents exactly, so anything
    /// above it cannot be trusted to be the number that was sent.
    /// </summary>
    public static bool TryUserId(JsonElement element, out long id)
    {
        id = 0;
        if (!TryNumber(element, out var raw)) return false;
        if (raw < 0 || raw > 9007199254740992d || Math.Floor(raw) != raw) return false;

        id = (long)raw;
        return true;
    }
}
