using System.Text;

namespace Labs626.UrScore.Recipes;

/// <summary>
/// One stat as Ur Score handles it after parsing: a recipe value, or a counter the user picked.
/// <see cref="Key"/> is the value's id, or <c>counter:</c> plus the counter's name (stats design §5.1).
/// </summary>
public sealed record RecipeStat(string Key, string Label, string Path, string SuggestedMetricId, bool Sum);

/// <summary>
/// Stat keys and what they stand for. Pure: the engine uses it to find a tracked stat's path, and the
/// settings screen uses it to list and search what a recipe offers.
/// </summary>
public static class RecipeStats
{
    /// <summary>How many counter names a search shows at once.</summary>
    public const int MatchLimit = 8;

    public static string CounterKey(string name) => RecipeCounters.KeyPrefix + name;

    public static bool IsCounterKey(string key, out string name)
    {
        name = key.StartsWith(RecipeCounters.KeyPrefix, StringComparison.Ordinal) ? key[RecipeCounters.KeyPrefix.Length..] : "";
        return name.Length > 0;
    }

    /// <summary>
    /// A counter name that can become a stat. A dot would split its path in two (spec §3.2), and an
    /// all-digit name is more likely someone's user id than a statistic, so neither is offered.
    /// </summary>
    public static bool CanPick(string name) =>
        !string.IsNullOrWhiteSpace(name) && !name.Contains('.') && !name.All(char.IsAsciiDigit);

    /// <summary>Spec §3.2: lowercase, spaces to hyphens, anything outside a-z, 0-9 and hyphen dropped.</summary>
    public static string Slug(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name.ToLowerInvariant())
        {
            if (c == ' ') builder.Append('-');
            else if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-') builder.Append(c);
        }

        return builder.Length == 0 ? "stat" : builder.ToString();
    }

    /// <summary>The stat a key names in this recipe, or null when the recipe no longer offers it.</summary>
    public static RecipeStat? Find(Recipe recipe, string key)
    {
        var step = recipe.LastStep;
        var value = step.Values.FirstOrDefault(v => string.Equals(v.Id, key, StringComparison.Ordinal));
        if (value is not null) return new RecipeStat(value.Id, value.Label, value.Path, value.MetricId, value.Sum);

        if (step.Counters is { } counters && IsCounterKey(key, out var name) && CanPick(name))
        {
            return new RecipeStat(key, name, $"{counters.Path}.{name}", counters.MetricIdPrefix + Slug(name), Sum: true);
        }

        return null;
    }

    /// <summary>Every recipe value in recipe order, then each picked counter this recipe can still read, by key.</summary>
    public static IReadOnlyList<RecipeStat> Offered(Recipe recipe, IEnumerable<string> pickedKeys)
    {
        var values = recipe.LastStep.Values.Select(v => Find(recipe, v.Id)!);
        var counters = pickedKeys
            .Where(key => IsCounterKey(key, out _))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(key => Find(recipe, key))
            .OfType<RecipeStat>();

        return [.. values, .. counters];
    }

    /// <summary>Names containing the query, ignoring case, in the order the source gave them, skipping ones already picked.</summary>
    public static IReadOnlyList<string> MatchCounterNames(IReadOnlyList<string> names, string query, IEnumerable<string> pickedKeys)
    {
        query = query.Trim();
        if (query.Length == 0) return [];

        var picked = pickedKeys.ToHashSet(StringComparer.Ordinal);
        return [.. names
            .Where(CanPick)
            .Where(name => name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Where(name => !picked.Contains(CounterKey(name)))
            .Distinct(StringComparer.Ordinal)
            .Take(MatchLimit)];
    }
}
