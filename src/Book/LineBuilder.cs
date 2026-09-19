using System.Globalization;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Book;

/// <summary>Everything a line needs about the read that isn't in the reading.</summary>
public sealed record ReadContext(Labs626.UrScore.Core.Source Source, Recipe Recipe, string RecipeHash, string Trigger, DateTimeOffset At, int OffsetMinutes);

/// <summary>
/// Score book spec §5.2, §5.3 and §5.6. The only place a reading becomes a line, and so the only place the
/// privacy rules for the book live:
/// - accounts come only from rows whose id is in the user's map;
/// - a watch source writes no account;
/// - a group list writes nothing;
/// - headline values are finite numbers that aren't another row's user id.
/// </summary>
public static class LineBuilder
{
    public static BookLine? Reading(ReadContext context, RecipeReading reading, IReadOnlyDictionary<long, Guid> map, IReadOnlySet<string> tracked)
    {
        var recipe = context.Recipe;
        if (reading.Outcome != ReadingOutcome.Read) return null;

        // A clans list keeps the field's shape, never its register: four numbers and a count, no clan named
        // (the owner's ruling, 2026-09-19). No account is matched on a group list, so none can be written.
        if (recipe.IsGroupList)
        {
            var field = FieldSummary.Of(reading.Groups, recipe.LastStep.Values.FirstOrDefault()?.Id ?? "");
            return field.Count == 0
                ? null
                : new BookLine(
                    BookLine.Version, BookLine.KindRead, context.At, context.OffsetMinutes, context.Trigger,
                    new BookRecipeRef(recipe.Slug, context.RecipeHash), context.Source.Id, RoleText(context.Source.Role),
                    Inputs(context.Source), Period(reading.Period), field, [],
                    new Dictionary<string, BookAccount>(StringComparer.Ordinal),
                    null, reading.ListAsOf?.Time, reading.ListAsOf?.Stale);
        }

        var stats = tracked.Order(StringComparer.Ordinal).ToList();
        var headline = Headline(reading.Headline, OtherIds(reading.Rows, map));
        var accounts = new Dictionary<string, BookAccount>(StringComparer.Ordinal);
        var unavail = new List<string>();

        if (context.Source.Role != SourceRole.Watch)
        {
            var list = !recipe.LastStep.PerAccount;
            foreach (var (id, account) in Accounts(reading.Rows, map, tracked, stats, list, onlyUsers: null))
            {
                accounts[id] = reading.AccountAsOf.TryGetValue(long.Parse(id, CultureInfo.InvariantCulture), out var stamp)
                    ? account with { AsOf = stamp.Time, Stale = stamp.Stale }
                    : account;
            }

            unavail.AddRange(reading.Unavailable.Keys.Where(map.ContainsKey).Order().Select(Id));
        }

        if (headline.Count == 0 && accounts.Count == 0 && unavail.Count == 0) return null;

        return new BookLine(
            BookLine.Version, BookLine.KindRead, context.At, context.OffsetMinutes, context.Trigger,
            new BookRecipeRef(recipe.Slug, context.RecipeHash), context.Source.Id, RoleText(context.Source.Role),
            Inputs(context.Source), Period(reading.Period), headline, stats, accounts,
            unavail.Count == 0 ? null : unavail, reading.ListAsOf?.Time, reading.ListAsOf?.Stale);
    }

    public static BookLine Final(
        ReadContext context, PastPeriodReading past, IReadOnlyDictionary<long, Guid> map, IReadOnlySet<string> tracked,
        IReadOnlyCollection<long>? onlyUsers, string trigger)
    {
        var stats = tracked.Order(StringComparer.Ordinal).ToList();
        var accounts = context.Source.Role == SourceRole.Watch
            ? new Dictionary<string, BookAccount>(StringComparer.Ordinal)
            : Accounts(past.Rows, map, tracked, stats, list: true, onlyUsers).ToDictionary(a => a.Id, a => a.Account, StringComparer.Ordinal);

        return new BookLine(
            BookLine.Version, BookLine.KindFinal, context.At, context.OffsetMinutes, trigger,
            new BookRecipeRef(context.Recipe.Slug, context.RecipeHash), context.Source.Id, RoleText(context.Source.Role),
            Inputs(context.Source), new BookPeriod(past.Value), Headline(past.Headline, OtherIds(past.Rows, map)), stats, accounts);
    }

    /// <summary>
    /// The user's rows with at least one tracked finite value, with competition ranks on list recipes. <c>of</c> is the row
    /// count (spec §5.2); <c>ranked</c> is, per stat, how many rows had a value and so were in that rank's field (backlog
    /// S1-6.9). A count, never an id or a value of anyone else's.
    /// </summary>
    private static IEnumerable<(string Id, BookAccount Account)> Accounts(
        IReadOnlyList<RecipeRow> rows, IReadOnlyDictionary<long, Guid> map, IReadOnlySet<string> tracked,
        IReadOnlyList<string> stats, bool list, IReadOnlyCollection<long>? onlyUsers)
    {
        var ranks = list ? stats.ToDictionary(s => s, s => Ranking.Competition(rows, s), StringComparer.Ordinal) : null;

        foreach (var row in rows.Where(r => map.ContainsKey(r.UserId) && (onlyUsers is null || onlyUsers.Contains(r.UserId))))
        {
            var values = row.Values
                .Where(kv => tracked.Contains(kv.Key) && double.IsFinite(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            if (values.Count == 0) continue;

            Dictionary<string, int>? rank = null;
            Dictionary<string, int>? ranked = null;
            if (ranks is not null)
            {
                var placed = ranks.Where(r => r.Value.ContainsKey(row.UserId)).ToList();
                if (placed.Count > 0)
                {
                    rank = placed.ToDictionary(r => r.Key, r => r.Value[row.UserId], StringComparer.Ordinal);
                    ranked = placed.ToDictionary(r => r.Key, r => r.Value.Count, StringComparer.Ordinal);
                }
            }

            yield return (Id(row.UserId), new BookAccount(values, rank, list ? rows.Count : null, Ranked: ranked));
        }
    }

    private static HashSet<long> OtherIds(IReadOnlyList<RecipeRow> rows, IReadOnlyDictionary<long, Guid> map) =>
        rows.Select(r => r.UserId).Where(id => !map.ContainsKey(id)).ToHashSet();

    /// <summary>Numbers only, and never a number that is another row's user id (score book spec §5.6).</summary>
    private static Dictionary<string, double> Headline(IReadOnlyList<HeadlineValue> headline, HashSet<long> otherIds) =>
        headline
            .Where(h => h.Id.Length > 0 && h.Number is { } n && double.IsFinite(n)
                        && !(n == Math.Floor(n) && n is > 0 and < long.MaxValue && otherIds.Contains((long)n)))
            .GroupBy(h => h.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Number!.Value, StringComparer.Ordinal);

    private static Dictionary<string, string> Inputs(Labs626.UrScore.Core.Source source) => new(source.Inputs, StringComparer.Ordinal);

    private static BookPeriod? Period(ReadingPeriod? period) => period is null ? null : new BookPeriod(period.Value, period.Starts, period.Ends);

    private static string RoleText(SourceRole role) => role.ToString().ToLowerInvariant();

    private static string Id(long userId) => userId.ToString(CultureInfo.InvariantCulture);
}
