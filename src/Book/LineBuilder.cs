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
    /// <param name="myGroups">
    /// Your own clans' names, for a group list: with them the line also says where you stand and what the place
    /// above you holds. Without them, only the field's own numbers are kept.
    /// </param>
    public static BookLine? Reading(
        ReadContext context, RecipeReading reading, IReadOnlyDictionary<long, Guid> map, IReadOnlySet<string> tracked,
        IReadOnlySet<string>? myGroups = null)
    {
        var recipe = context.Recipe;
        if (reading.Outcome != ReadingOutcome.Read) return null;

        // A clans list keeps the field's shape and the part of its register a chart can use: the summary numbers,
        // and the top clans by name with the places either side of yours (GroupRows). The 2026-09-19 ruling that
        // named no clan at all was reversed by the owner on 2026-09-20 — "these are game stats ... it's just game
        // stuff" — bounded by "I don't want it to store too much". No account is matched on a group list, so none
        // can be written, and no player's id or value is in one to begin with.
        if (recipe.IsGroupList)
        {
            var valueKey = recipe.LastStep.Values.FirstOrDefault()?.Id ?? "";
            var field = FieldSummary.Of(reading.Groups, valueKey, myGroups);
            return field.Count == 0
                ? null
                : new BookLine(
                    BookLine.Version, BookLine.KindRead, context.At, context.OffsetMinutes, context.Trigger,
                    new BookRecipeRef(recipe.Slug, context.RecipeHash), context.Source.Id, RoleText(context.Source.Role),
                    Inputs(context.Source), Period(reading.Period), field, [],
                    new Dictionary<string, BookAccount>(StringComparer.Ordinal),
                    null, reading.ListAsOf?.Time, reading.ListAsOf?.Stale,
                    GroupRows.Keep(reading.Groups, valueKey, myGroups, recipe.GroupsAreClans));
        }

        var stats = tracked.Order(StringComparer.Ordinal).ToList();
        var headline = Headline(reading.Headline, OtherIds(reading, map));
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

    /// <summary>
    /// Every user id this READING mentions that is not one of yours: the rows, the accounts the source said it
    /// could not show, and the ones a stat missed for. Until 2026-09-21 only the rows were looked at, so an id
    /// the reading knew about through the other two was not guarded against (S1-6.10).
    /// </summary>
    private static HashSet<long> OtherIds(RecipeReading reading, IReadOnlyDictionary<long, Guid> map) =>
        [.. reading.Rows.Select(row => row.UserId)
            .Concat(reading.Unavailable.Keys)
            .Concat(reading.CellMisses.Keys.Select(key => key.UserId))
            .Where(id => !map.ContainsKey(id))];

    private static HashSet<long> OtherIds(IReadOnlyList<RecipeRow> rows, IReadOnlyDictionary<long, Guid> map) =>
        [.. rows.Select(row => row.UserId).Where(id => !map.ContainsKey(id))];

    /// <summary>
    /// Numbers only, and never a number that is a user id this reading mentioned for somebody who is not you
    /// (score book spec §5.6).
    /// <para>
    /// BEST EFFORT, and the limit is worth stating plainly rather than leaving somebody to assume otherwise. A
    /// stranger's id can only be recognised by having SEEN it, because nothing about the number gives it away: a
    /// Roblox user id and a clan's battle points are both plain integers in the billions, so any rule based on
    /// the shape or size of the number would throw away real points. So this catches an id the reading itself
    /// surfaced and cannot catch one it did not — a recipe whose headline path points straight at, say, a
    /// clan leader's id would keep that number, because the reading never mentions the leader anywhere else.
    /// </para>
    /// <para>
    /// The control for that case is consent, not detection: the import screen lists every headline a recipe will
    /// keep before it is installed. It lists the recipe author's LABELS, though, not the paths behind them, so it
    /// is a real control and not an airtight one. The shipped recipes declare two headlines, Clan place and Clan
    /// points, and neither is an id (S1-6.10).
    /// </para>
    /// </summary>
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
