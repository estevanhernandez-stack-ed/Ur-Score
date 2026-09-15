using System.Globalization;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Book;

/// <summary>Which finals the book already holds, by recipe, inputs, period and account. Built from the book, updated as lines are planned.</summary>
public sealed class FinalsIndex
{
    private readonly object _gate = new();
    private readonly HashSet<(string Slug, string Inputs, string Period)> _clans = [];
    private readonly HashSet<(string Slug, string Inputs, string Period, long UserId)> _accounts = [];

    public void Add(BookLine line)
    {
        if (line.Kind != BookLine.KindFinal || line.Period is null) return;

        var inputs = Labs626.UrScore.Core.Source.KeyOf(line.Inputs);
        lock (_gate)
        {
            _clans.Add((line.Recipe.Slug, inputs, line.Period.Value));
            foreach (var id in line.Accounts.Keys)
            {
                if (long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var userId))
                {
                    _accounts.Add((line.Recipe.Slug, inputs, line.Period.Value, userId));
                }
            }
        }
    }

    public bool HasClan(string slug, string inputsKey, string period)
    {
        lock (_gate) return _clans.Contains((slug, inputsKey, period));
    }

    public bool HasAccount(string slug, string inputsKey, string period, long userId)
    {
        lock (_gate) return _accounts.Contains((slug, inputsKey, period, userId));
    }

    /// <summary>A line that can't be indexed (a corrupt one that slipped past <see cref="BookJson.TryParse"/>) is skipped, never the book.</summary>
    public static FinalsIndex Load(string root)
    {
        var index = new FinalsIndex();
        foreach (var slug in BookFiles.Slugs(root))
        {
            foreach (var line in BookFiles.ReadAll(root, slug))
            {
                try
                {
                    index.Add(line);
                }
                catch (Exception)
                {
                }
            }
        }

        return index;
    }
}

/// <summary>Score book spec §6.1.</summary>
public static class FinalsPlanner
{
    public static IReadOnlyList<BookLine> Plan(
        ReadContext context, RecipeReading reading, IReadOnlyDictionary<long, Guid> map, IReadOnlySet<string> tracked,
        FinalsIndex index, string? previousPeriod)
    {
        if (reading.Outcome != ReadingOutcome.Read || reading.Past.Count == 0 || context.Recipe.Period?.Past is null) return [];

        var slug = context.Recipe.Slug;
        var inputs = context.Source.InputsKey;
        var watch = context.Source.Role == SourceRole.Watch;
        var current = reading.Period?.Value;
        var currentEnded = reading.Period?.Ends is { } ends && ends <= context.At;

        var lines = new List<BookLine>();
        foreach (var past in reading.Past)
        {
            var isCurrent = past.Value == current;
            if (isCurrent && !currentEnded) continue;

            var trigger = isCurrent || past.Value == previousPeriod ? BookLine.TriggerEnded : BookLine.TriggerBackfill;

            if (!index.HasClan(slug, inputs, past.Value))
            {
                lines.Add(LineBuilder.Final(context, past, map, tracked, onlyUsers: null, trigger));
                continue;
            }

            if (watch) continue;

            // Only accounts that would actually be written, so an account with no tracked value never asks again.
            var missing = past.Rows
                .Where(r => map.ContainsKey(r.UserId)
                            && r.Values.Any(kv => tracked.Contains(kv.Key) && double.IsFinite(kv.Value))
                            && !index.HasAccount(slug, inputs, past.Value, r.UserId))
                .Select(r => r.UserId)
                .Distinct()
                .ToList();

            if (missing.Count > 0) lines.Add(LineBuilder.Final(context, past, map, tracked, missing, trigger));
        }

        return lines;
    }

    /// <summary>The current period's end has passed, or its final is already kept: no more reading lines for it.</summary>
    public static bool CurrentPeriodEnded(ReadContext context, RecipeReading reading, FinalsIndex index) =>
        reading.Period is { } period
        && ((period.Ends is { } ends && ends <= context.At)
            || index.HasClan(context.Recipe.Slug, context.Source.InputsKey, period.Value));
}
