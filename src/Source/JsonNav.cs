using System.Text.Json;

namespace Labs626.UrScore.Source;

/// <summary>
/// Finds a property on an object or inside one known wrapper, case-insensitively.
/// <para>
/// This exists because we do not actually know the shape. It was verified once, on 2026-09-09,
/// and the vendor's own published example disagreed with what came back. So rather than hardcode
/// a path and return nothing when it changes, every lookup checks the root and a short list of
/// wrapper keys this API is known to use, and reports what it saw when it fails.
/// </para>
/// </summary>
internal static class JsonNav
{
    /// <summary>Wrappers checked after the object itself. Ordered by how often this API uses them.</summary>
    private static readonly string[] Wrappers = ["data", "result", "response"];

    /// <summary>Every property name on an object, in document order. Empty for a non-object.</summary>
    public static IReadOnlyList<string> Keys(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
            ? [.. element.EnumerateObject().Select(p => p.Name)]
            : [];

    /// <summary>
    /// The object a lookup should be performed against: the root, or its wrapper when the root is
    /// only an envelope. Returns the root unchanged when no wrapper is present, so the caller's
    /// error message names the keys a human would see at the top of the response.
    /// <para>
    /// A wrapper whose value is explicitly <c>null</c> (e.g. <c>"data": null</c>) is still the
    /// unwrap target, not a reason to fall back to the root: that shape is this API's way of
    /// saying "nothing here," and the caller needs to see a null body to tell that apart from a
    /// response it failed to understand.
    /// </para>
    /// </summary>
    public static JsonElement Unwrap(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return root;

        foreach (var wrapper in Wrappers)
        {
            if (TryGet(root, wrapper, out var inner)
                && inner.ValueKind is JsonValueKind.Object or JsonValueKind.Null)
            {
                return inner;
            }
        }

        return root;
    }

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
}
