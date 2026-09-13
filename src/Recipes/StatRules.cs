namespace Labs626.UrScore.Recipes;

/// <summary>
/// Whether a set of stat choices may be saved, checked when Import or Save is pressed (stats design
/// §5.2). Pure, so the refusals are testable without the settings screen.
/// </summary>
public static class StatRules
{
    public const string TickOne = "Tick at least one stat to show or send.";

    /// <summary>Every reason these choices cannot be saved for this recipe, or none.</summary>
    public static IReadOnlyList<string> Problems(
        Recipe recipe, IReadOnlyDictionary<string, StatChoice> proposed, IEnumerable<InstalledRecipe> installed)
    {
        var ticked = RecipeStats.Offered(recipe, proposed.Keys)
            .Where(stat => proposed.TryGetValue(stat.Key, out var choice) && (choice.Show || choice.Send))
            .Select(stat => (Stat: stat, Choice: proposed[stat.Key]))
            .ToList();

        if (ticked.Count == 0) return [TickOne];

        var problems = new List<string>();

        foreach (var (stat, _) in ticked.Where(t => string.IsNullOrWhiteSpace(t.Choice.MetricId)))
        {
            problems.Add($"Give {stat.Label} a name RoRoRo uses.");
        }

        var named = ticked.Where(t => !string.IsNullOrWhiteSpace(t.Choice.MetricId)).ToList();

        // Two stats feeding one series make any rule on it meaningless.
        foreach (var group in named.GroupBy(t => t.Choice.MetricId.Trim(), StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            problems.Add($"{string.Join(" and ", group.Select(t => t.Stat.Label))} use the same name, {group.Key}. Give each stat its own.");
        }

        var sent = named.Where(t => t.Choice.Send).Select(t => t.Choice.MetricId.Trim()).ToHashSet(StringComparer.Ordinal);

        foreach (var other in installed.Where(i => !string.Equals(i.Recipe.Slug, recipe.Slug, StringComparison.Ordinal)))
        {
            foreach (var theirs in other.State.SentStats(other.Recipe).Where(s => sent.Contains(s.MetricId)))
            {
                problems.Add($"{theirs.MetricId} is already sent by {other.Recipe.Name}. Give this stat a different name.");
            }
        }

        return problems;
    }
}
