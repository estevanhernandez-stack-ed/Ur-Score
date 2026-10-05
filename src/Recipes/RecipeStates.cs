namespace Labs626.UrScore.Recipes;

/// <summary>
/// The state a built-in reader runs with (games-and-modes, Readers.Compose). Pure: the caller loads the saved state
/// and this decides what it means, so every branch is testable without a data folder.
/// </summary>
public static class RecipeStates
{
    /// <summary>
    /// A saved state with stats is used as is. A saved state without stats (a 0.6.3 install, or a legacy
    /// <c>metricIdOverride</c>) keeps its inputs, excluded accounts, counter names and sent fields and takes its stats from
    /// <see cref="RecipeState.ChoicesForUpdate"/>, so a legacy Send is not lost now that no import window re-ticks it (A8). With no legacy choices either it is seeded like no state.
    /// No saved state seeds the recipe's suggested ticks, plus Show on each of <paramref name="extraShows"/> (a mode's
    /// <c>shows</c>, A1: the clan-battle recipe marks no value <c>show</c>), under the stat's suggested metric id. Send is
    /// never seeded. Keys the recipe does not offer are ignored. Extra shows never touch a saved state that has stats or legacy choices.
    /// </summary>
    public static RecipeState Effective(Recipe recipe, RecipeState? saved, IReadOnlyCollection<string>? extraShows = null)
    {
        if (saved is not null)
        {
            if (saved.Stats is not null) return saved;
            // Stats null and no legacy choices: a state written for its inputs alone (a clan typed, nothing ticked) is as
            // unseeded as no file, so it takes the seed and keeps what it did hold. Legacy choices still win when present.
            if (saved.ChoicesForUpdate.Count > 0)
                return saved with { Stats = new Dictionary<string, StatChoice>(saved.ChoicesForUpdate, StringComparer.Ordinal) };
        }

        var stats = Seed(recipe, extraShows);
        return saved is null ? new RecipeState(Stats: stats) : saved with { Stats = stats };
    }

    private static Dictionary<string, StatChoice> Seed(Recipe recipe, IReadOnlyCollection<string>? extraShows)
    {
        var stats = new Dictionary<string, StatChoice>(RecipeStats.SuggestedChoices(recipe), StringComparer.Ordinal);
        foreach (var key in extraShows ?? [])
        {
            if (RecipeStats.Find(recipe, key) is not { } stat) continue;
            stats[key] = stats.TryGetValue(key, out var existing)
                ? existing with { Show = true }
                : new StatChoice(Show: true, MetricId: stat.SuggestedMetricId);
        }

        return stats;
    }
}
