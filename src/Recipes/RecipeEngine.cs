using System.Globalization;
using System.Text;
using System.Text.Json;
using Labs626.UrScore.Source;

namespace Labs626.UrScore.Recipes;

public enum ReadingOutcome
{
    Read,
    NeedsInput,
    Idle,
    Unreachable,
    RateLimited,
    InputNotFound,
    SignInRequired,
    KeyMissing,
    KeyRejected,
    ShapeNotUnderstood,
}

/// <summary>
/// One row or account that was read: its Roblox user id, and every tracked stat found for it, by stat
/// key. A stat that missed for this row is simply absent. Equal when the id and every value match, so
/// tests can compare rows directly.
/// </summary>
public sealed record RecipeRow(long UserId, IReadOnlyDictionary<string, double> Values)
{
    public bool Equals(RecipeRow? other) =>
        other is not null
        && UserId == other.UserId
        && Values.Count == other.Values.Count
        && Values.All(kv => other.Values.TryGetValue(kv.Key, out var value) && value.Equals(kv.Value));

    public override int GetHashCode() => UserId.GetHashCode();

    public override string ToString() =>
        $"RecipeRow {{ UserId = {UserId}, Values = {string.Join(", ", Values.Select(kv => $"{kv.Key}={kv.Value.ToString(CultureInfo.InvariantCulture)}"))} }}";
}

/// <summary>A headline as read: its text for display, and its id and number for the score book.</summary>
public sealed record HeadlineValue(string Label, string? Text)
{
    public string Id { get; init; } = "";

    /// <summary>The value when it is a finite number, else null. The book keeps only this.</summary>
    public double? Number { get; init; }
}

/// <summary>What this reading belongs to, and when it starts and ends when the source says.</summary>
public sealed record ReadingPeriod(string Value, DateTimeOffset? Starts, DateTimeOffset? Ends);

/// <summary>The source's own snapshot time for what was read.</summary>
public sealed record AsOfStamp(DateTimeOffset Time, bool? Stale);

/// <summary>One group (a clan) from a group-list step. Never matched to an account.</summary>
public sealed record GroupRow(string Name, IReadOnlyDictionary<string, double> Values, int? Rank);

/// <summary>
/// One key under the recipe's <c>period.past</c>, read with the live paths. <see cref="Rows"/> is every
/// row, the user's and everyone else's; the score book keeps only the user's.
/// </summary>
public sealed record PastPeriodReading(string Value, IReadOnlyList<RecipeRow> Rows, IReadOnlyList<HeadlineValue> Headline, bool RowsReadable);

/// <summary>
/// What one run of a recipe found. <see cref="Rows"/> is every readable row, the user's own and
/// everyone else's; the watch decides which are the user's. <see cref="Context"/> changes when the
/// thing being read changes, such as a new clan battle.
/// <para>
/// Misses cost only what they touch (stats design §4): <see cref="Unavailable"/> is an account the
/// source says it cannot show, <see cref="StatMisses"/> is a stat that missed on every row or account
/// read, and <see cref="CellMisses"/> is one stat missing for one row while other rows had it.
/// </para>
/// </summary>
public sealed record RecipeReading(
    ReadingOutcome Outcome,
    string? Detail,
    IReadOnlyList<RecipeRow> Rows,
    IReadOnlyList<HeadlineValue> Headline,
    string? Context,
    int RowsSeen)
{
    /// <summary>Per-account steps only: a user id the source answered 404 for, or whose answer matched <c>unavailable</c>, and the message to show.</summary>
    public IReadOnlyDictionary<long, string> Unavailable { get; init; } = new Dictionary<long, string>();

    /// <summary>A stat key that missed on every row or account read this cycle, and the first miss, naming the keys present.</summary>
    public IReadOnlyDictionary<string, string> StatMisses { get; init; } = new Dictionary<string, string>();

    /// <summary>One stat missing for one user id while other rows or accounts had it.</summary>
    public IReadOnlyDictionary<(long UserId, string Stat), string> CellMisses { get; init; } = new Dictionary<(long UserId, string Stat), string>();

    /// <summary>The number-valued keys under the last step's <c>counters</c> path, from the first row or account that had them.</summary>
    public IReadOnlyList<string> CounterNames { get; init; } = [];

    /// <summary>The recipe's <c>icon</c> path read from the last step's response, as text, or null.</summary>
    public string? IconText { get; init; }

    public ReadingPeriod? Period { get; init; }

    /// <summary>A list step's <c>asOf</c>.</summary>
    public AsOfStamp? ListAsOf { get; init; }

    /// <summary>A per-account step's <c>asOf</c>, by user id.</summary>
    public IReadOnlyDictionary<long, AsOfStamp> AccountAsOf { get; init; } = new Dictionary<long, AsOfStamp>();

    /// <summary>A group-list step's rows.</summary>
    public IReadOnlyList<GroupRow> Groups { get; init; } = [];

    /// <summary>Every readable key under <c>period.past</c>, in the source's order.</summary>
    public IReadOnlyList<PastPeriodReading> Past { get; init; } = [];

    public static RecipeReading Stop(ReadingOutcome outcome, string detail) => new(outcome, detail, [], [], null, 0);
}

/// <summary>The seam <c>RecipeWatch</c> is tested against.</summary>
public interface IRecipeEngine
{
    /// <summary>
    /// Reads only <paramref name="trackedStats"/> (stat keys with Show or Send ticked). One response
    /// per row or per account serves all of them, so tracking more stats never adds a request.
    /// </summary>
    Task<RecipeReading> ReadAsync(
        Recipe recipe, IReadOnlyDictionary<string, string> inputs, IReadOnlyCollection<long> accountUserIds,
        IReadOnlySet<string> trackedStats, CancellationToken cancellationToken);
}

/// <summary>
/// Runs a recipe's steps in order (spec §4.1). Reads; never decides what happens with what it read.
/// </summary>
public sealed class RecipeEngine(IRecipeTransport transport, IKeyStore keys) : IRecipeEngine
{
    public const string NothingTracked = "No stat is ticked to show or send. Choose some in Setup › Stats.";

    public async Task<RecipeReading> ReadAsync(
        Recipe recipe, IReadOnlyDictionary<string, string> inputs, IReadOnlyCollection<long> accountUserIds,
        IReadOnlySet<string> trackedStats, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var input in recipe.Inputs)
        {
            if (!inputs.TryGetValue(input.Id, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return RecipeReading.Stop(ReadingOutcome.NeedsInput, $"Set {input.Label} to start.");
            }

            values[input.Id] = value.Trim();
        }

        // Recipe order, so "the first miss" means the same thing every cycle.
        // A group list has nothing to tick: it reads every value it declares.
        var stats = recipe.IsGroupList
            ? RecipeStats.Offered(recipe, []).ToList()
            : RecipeStats.Offered(recipe, trackedStats).Where(stat => trackedStats.Contains(stat.Key)).ToList();
        if (stats.Count == 0)
        {
            return RecipeReading.Stop(ReadingOutcome.NeedsInput, NothingTracked);
        }

        var taken = new List<string>();

        for (var index = 0; index < recipe.Steps.Count; index++)
        {
            var step = recipe.Steps[index];
            var number = index + 1;
            var label = $"{recipe.Slug}-step{number}";
            var isLast = index == recipe.Steps.Count - 1;

            if (isLast && step.PerAccount)
            {
                return (await ReadPerAccountAsync(recipe, step, stats, values, accountUserIds, label, Context(values, taken), cancellationToken)
                    .ConfigureAwait(false)) with { Period = PeriodOf(recipe, values) };
            }

            var (document, stop, _) = await FetchJsonAsync(recipe, step, values, label, cancellationToken).ConfigureAwait(false);
            if (stop is not null) return stop;

            using (document!)
            {
                if (isLast)
                {
                    return ReadList(recipe, step, stats, document!.RootElement, values, number, Context(values, taken))
                        with { Period = PeriodOf(recipe, values) };
                }

                foreach (var (name, pathTemplate) in step.Take)
                {
                    var result = RecipePath.Resolve(document!.RootElement, pathTemplate, values);
                    var path = Placeholders.Fill(pathTemplate, values, encode: false);

                    if (result.Outcome == PathOutcome.Found && RecipePath.AsText(result.Value) is { Length: > 0 } text)
                    {
                        values[name] = text;
                        taken.Add(name);
                        continue;
                    }

                    // Ruling R2: a take named only by the period's start or end is optional.
                    if (recipe.Period is { } optional && (name == optional.Starts || name == optional.Ends))
                    {
                        continue;
                    }

                    // V3-S.1: an idle stop still carries its past periods, so a source between periods
                    // backfills. A shape miss below does not: see WithPastAsync.
                    if (Absent(step, result) is { } absent)
                    {
                        return await WithPastAsync(recipe, absent, index + 1, stats, values, cancellationToken).ConfigureAwait(false);
                    }

                    if (result.Outcome == PathOutcome.Nothing && step.IdleWithout == name)
                    {
                        var idle = RecipeReading.Stop(ReadingOutcome.Idle, step.IdleMessage ?? "Nothing to read right now.");
                        return await WithPastAsync(recipe, idle, index + 1, stats, values, cancellationToken).ConfigureAwait(false);
                    }

                    return RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, result.Outcome switch
                    {
                        PathOutcome.Missing => $"Step {number}: {result.Miss}",
                        PathOutcome.Nothing => $"Step {number}: '{path}' was empty.",
                        _ => $"Step {number}: '{path}' is not a value an address can use.",
                    });
                }
            }
        }

        return RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, "The recipe has no steps.");
    }

    /// <summary>
    /// Stats design §3.2: a key that came whole from a placeholder, missing from an object that exists,
    /// is the source saying "not in this one", and the step's <c>absentMessage</c> reads it as idle.
    /// Every other miss stays a changed shape.
    /// </summary>
    private static RecipeReading? Absent(RecipeStep step, PathResult result) =>
        result.Outcome == PathOutcome.Missing && result.MissedAtPlaceholder && step.AbsentMessage is { } message
            ? RecipeReading.Stop(ReadingOutcome.Idle, message)
            : null;

    /// <summary>
    /// V3-S.1: <c>period.past</c> is its own path into the last step's response and never waits on the
    /// live period resolving, so an idle stop runs the steps it has left purely to reach that response
    /// and read the past there through the same <see cref="PastAt"/> the success path uses. The reading
    /// stays idle; only its past is added. Anything in the way — a later step whose address needs the
    /// value we never got, a fetch that stops, a last step with no list to read — leaves the stop
    /// exactly as it was. Only an idle stop comes here: a response we could not parse is not a response
    /// to mine, so every other stop still returns nothing.
    /// </summary>
    private async Task<RecipeReading> WithPastAsync(
        Recipe recipe, RecipeReading idle, int from, IReadOnlyList<RecipeStat> stats,
        IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken)
    {
        if (recipe.Period?.Past is null || from >= recipe.Steps.Count) return idle;

        var carried = new Dictionary<string, string>(values, StringComparer.Ordinal);

        for (var index = from; index < recipe.Steps.Count; index++)
        {
            var step = recipe.Steps[index];
            var isLast = index == recipe.Steps.Count - 1;

            // A step whose address needs a value we never got cannot be asked at all, and Fill throws
            // rather than send a half-filled address.
            if (Placeholders.Names(step.Url).Any(name => !carried.ContainsKey(name))) return idle;
            if (isLast && (step.PerAccount || step.Rows is null || step.UserId is null)) return idle;

            // The same label as the success path, so the raw save stays one file per step either way.
            var (document, stop, _) = await FetchJsonAsync(recipe, step, carried, $"{recipe.Slug}-step{index + 1}", cancellationToken)
                .ConfigureAwait(false);
            if (stop is not null) return idle;

            using (document!)
            {
                if (isLast)
                {
                    var past = PastAt(recipe, step, stats, document!.RootElement, carried);
                    return past.Count == 0 ? idle : idle with { Past = past };
                }

                // A take that misses here is not a second miss to report: the stop is already told.
                foreach (var (name, pathTemplate) in step.Take)
                {
                    var result = RecipePath.Resolve(document!.RootElement, pathTemplate, carried);
                    if (result.Outcome == PathOutcome.Found && RecipePath.AsText(result.Value) is { Length: > 0 } text)
                    {
                        carried[name] = text;
                    }
                }
            }
        }

        return idle;
    }

    /// <summary><c>Status</c> is the raw HTTP status behind a stop, when there was one, so a caller that
    /// cares about the difference between a 400 and a 404 (spec §3.2) does not have to reparse <c>Stop.Detail</c>.</summary>
    private async Task<(JsonDocument? Document, RecipeReading? Stop, int? Status)> FetchJsonAsync(
        Recipe recipe, RecipeStep step, IReadOnlyDictionary<string, string> values, string label,
        CancellationToken cancellationToken)
    {
        var host = RecipeHosts.HostOf(step.Url);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var query = new List<(string Name, string Value)>();

        foreach (var keyId in step.UseKeys)
        {
            var declared = recipe.Keys.First(k => k.Id == keyId);
            var saved = keys.Find(keyId);

            if (saved is null)
            {
                return (null, RecipeReading.Stop(ReadingOutcome.KeyMissing,
                    $"This recipe needs your {declared.Label} key. Get one at {RecipeHosts.HostOf(declared.GetOneAt)}."), null);
            }

            if (!string.Equals(saved.Host, host, StringComparison.OrdinalIgnoreCase))
            {
                // Spec §7.2: a key is only ever sent to the host it is bound to. Checked here as well
                // as at import, because a recipe file on disk can be edited after it was imported.
                return (null, RecipeReading.Stop(ReadingOutcome.KeyMissing,
                    $"Your {declared.Label} key is saved for {saved.Host}, and this recipe would send it to {host}. It was not sent."), null);
            }

            if (declared.In == KeyPlacement.Header) headers[declared.Name] = saved.Value;
            else query.Add((declared.Name, saved.Value));
        }

        var address = AppendQuery(Placeholders.Fill(step.Url, values, encode: true), query);
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return (null, RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, $"The address for {host} is not valid once filled in."), null);
        }

        var fetched = await transport.GetAsync(uri, headers, label, cancellationToken).ConfigureAwait(false);

        var stop = Classify(recipe, step, fetched, host, values);
        if (stop is not null) return (null, stop, fetched.Status);

        try
        {
            return (JsonDocument.Parse(fetched.Body ?? ""), null, fetched.Status);
        }
        catch (JsonException ex)
        {
            return (null, RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, $"{host} did not return valid JSON: {ex.Message}"), fetched.Status);
        }
    }

    /// <summary>Spec §4.3, one branch per row of its table.</summary>
    private static RecipeReading? Classify(
        Recipe recipe, RecipeStep step, FetchResult fetched, string host, IReadOnlyDictionary<string, string> values)
    {
        if (!fetched.Answered)
        {
            return RecipeReading.Stop(ReadingOutcome.Unreachable, fetched.Error ?? $"Could not reach {host}.");
        }

        if (fetched.Succeeded) return null;

        var status = fetched.Status!.Value;

        if (status is >= 300 and < 400)
        {
            return RecipeReading.Stop(ReadingOutcome.Unreachable,
                $"{host} redirected to another address. Recipes never follow redirects, so nothing was sent there.");
        }

        if (status == 429)
        {
            return RecipeReading.Stop(ReadingOutcome.RateLimited, $"{host} asked us to slow down. Trying again next poll.");
        }

        if (status is 401 or 403)
        {
            if (step.UseKeys.Count > 0)
            {
                var labels = string.Join(", ", step.UseKeys.Select(id => recipe.Keys.First(k => k.Id == id).Label));
                return RecipeReading.Stop(ReadingOutcome.KeyRejected, $"{host} rejected your {labels} key. Change it to try again.");
            }

            return RecipeReading.Stop(ReadingOutcome.SignInRequired, $"{host} requires signing in, which recipes cannot do.");
        }

        if (status is 400 or 404)
        {
            // Plan Ruling 5: in a per-account step this is one account's problem, and the caller
            // treats InputNotFound from that step as costing only that account.
            if (step.PerAccount && values.TryGetValue(Placeholders.UserId, out var userId))
            {
                return RecipeReading.Stop(ReadingOutcome.InputNotFound, $"{host} has nothing for user id {userId}.");
            }

            var names = Placeholders.Names(step.Url);
            var input = recipe.Inputs.FirstOrDefault(i => names.Contains(i.Id));
            if (input is not null)
            {
                return RecipeReading.Stop(ReadingOutcome.InputNotFound,
                    $"{host} found nothing for '{values[input.Id]}' ({input.Label}). Check the spelling.");
            }
        }

        return RecipeReading.Stop(ReadingOutcome.Unreachable, $"{host} returned {status}.");
    }

    private static RecipeReading ReadList(
        Recipe recipe, RecipeStep step, IReadOnlyList<RecipeStat> stats, JsonElement root,
        IReadOnlyDictionary<string, string> values, int number, string? context)
    {
        // Read first, so a clan that sits out a battle still shows its icon beside the idle message.
        var icon = recipe.Icon is null ? null : TextAt(root, recipe.Icon, values);

        if (step.GroupName is not null) return ReadGroups(step, stats, root, values, number, context);

        var rowsPath = Placeholders.Fill(step.Rows!, values, encode: false);
        var rowsResult = RecipePath.Resolve(root, step.Rows!, values);

        if (rowsResult.Outcome == PathOutcome.Missing)
        {
            // V3-S.1: a clan that sat out this period and a clan between periods are the same case to a
            // reader, and this response is the one the past lives in. A shape miss keeps returning nothing.
            if (Absent(step, rowsResult) is { } absent)
            {
                return absent with { IconText = icon, Past = PastAt(recipe, step, stats, root, values) };
            }

            return RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, $"Step {number}: {rowsResult.Miss}") with { IconText = icon };
        }

        if (rowsResult.Outcome == PathOutcome.Nothing || rowsResult.Value.ValueKind != JsonValueKind.Array)
        {
            var what = rowsResult.Outcome == PathOutcome.Nothing ? "empty" : "not a list";
            return RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, $"Step {number}: '{rowsPath}' is {what}, so there are no rows to read.")
                with { IconText = icon };
        }

        var userIdPath = Placeholders.Fill(step.UserId!, values, encode: false);
        var tally = new StatTally(stats);
        var rows = new List<RecipeRow>();
        IReadOnlyList<string>? counterNames = null;
        string? firstProblem = null;
        var total = 0;

        foreach (var row in rowsResult.Value.EnumerateArray())
        {
            total++;

            var id = RecipePath.Resolve(row, userIdPath, "this row");
            if (id.Outcome != PathOutcome.Found)
            {
                firstProblem ??= id.Miss ?? $"'{userIdPath}' was empty in this row.";
                continue;
            }

            if (!JsonNav.TryUserId(id.Value, out var userId))
            {
                firstProblem ??= $"'{userIdPath}' is not a whole number a user id can be.";
                continue;
            }

            var found = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var stat in stats)
            {
                var result = RecipePath.Resolve(row, stat.Path, values, "this row");
                if (Absent(step, result) is { } absent)
                {
                    return absent with { IconText = icon, Past = PastAt(recipe, step, stats, root, values) };
                }

                var miss = StatNumberAt(result, stat, Placeholders.Fill(stat.Path, values, encode: false), "in this row", out var value);
                if (miss is null)
                {
                    found[stat.Key] = value;
                    tally.Found(stat.Key);
                }
                else
                {
                    firstProblem ??= miss;
                    tally.Missed(userId, stat.Key, miss);
                }
            }

            rows.Add(new RecipeRow(userId, found));
            if (step.Counters is not null) counterNames ??= CounterNamesAt(row, step.Counters, values);
        }

        if (total > 0 && (rows.Count == 0 || tally.EveryStatMissed(rows.Count)))
        {
            return RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, $"None of the {total} rows could be read: {firstProblem}")
                with { IconText = icon };
        }

        var headline = HeadlineAt(recipe, root, values);

        return new RecipeReading(ReadingOutcome.Read, null, rows, headline, context, total)
        {
            StatMisses = tally.StatMisses(rows.Count),
            CellMisses = tally.CellMisses(rows.Count),
            CounterNames = counterNames ?? [],
            IconText = icon,
            ListAsOf = step.AsOf is null ? null : AsOfAt(root, step.AsOf, values),
            Past = PastAt(recipe, step, stats, root, values),
        };
    }

    private async Task<RecipeReading> ReadPerAccountAsync(
        Recipe recipe, RecipeStep step, IReadOnlyList<RecipeStat> stats, Dictionary<string, string> values,
        IReadOnlyCollection<long> accountUserIds, string label, string? context, CancellationToken cancellationToken)
    {
        var ids = accountUserIds.Where(id => id > 0).Distinct().ToList();
        var tally = new StatTally(stats);
        var rows = new List<RecipeRow>();
        var unavailable = new Dictionary<long, string>();
        var asOf = new Dictionary<long, AsOfStamp>();
        IReadOnlyList<string>? counterNames = null;
        string? firstMiss = null;
        string? firstUnavailable = null;

        // In turn, never all at once: N accounts must not become N concurrent requests (spec §12).
        foreach (var userId in ids)
        {
            var perRequest = new Dictionary<string, string>(values, StringComparer.Ordinal)
            {
                [Placeholders.UserId] = userId.ToString(CultureInfo.InvariantCulture),
            };

            var (document, stop, status) = await FetchJsonAsync(recipe, step, perRequest, label, cancellationToken).ConfigureAwait(false);
            if (stop is not null)
            {
                if (stop.Outcome == ReadingOutcome.InputNotFound)
                {
                    // Plan Ruling 5: one account's 400 or 404 costs only that account either way.
                    // Spec §3.2 / controller ruling (fix round 1): only a 404 means "this account
                    // isn't there", which the recipe's own unavailable message may describe. A 400
                    // usually means something else went wrong for this account, so it keeps part 1's
                    // text naming the host regardless of what the recipe declares.
                    unavailable[userId] = status == 404 ? step.Unavailable?.Message ?? stop.Detail! : stop.Detail!;
                    firstUnavailable ??= unavailable[userId];
                    continue;
                }

                return stop;
            }

            using (document!)
            {
                var root = document!.RootElement;

                if (step.Unavailable is { } rule
                    && RecipePath.Resolve(root, rule.Path, perRequest) is { Outcome: PathOutcome.Found } said
                    && rule.Matches(said.Value))
                {
                    unavailable[userId] = rule.Message;
                    firstUnavailable ??= rule.Message;
                    continue;
                }

                var found = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (var stat in stats)
                {
                    var result = RecipePath.Resolve(root, stat.Path, perRequest);
                    if (Absent(step, result) is { } absent) return absent;

                    var miss = StatNumberAt(result, stat, Placeholders.Fill(stat.Path, perRequest, encode: false), $"for user id {userId}", out var value);
                    if (miss is null)
                    {
                        found[stat.Key] = value;
                        tally.Found(stat.Key);
                    }
                    else
                    {
                        firstMiss ??= miss;
                        tally.Missed(userId, stat.Key, miss);
                    }
                }

                rows.Add(new RecipeRow(userId, found));
                if (step.AsOf is not null && AsOfAt(root, step.AsOf, perRequest) is { } stamp) asOf[userId] = stamp;
                if (step.Counters is not null) counterNames ??= CounterNamesAt(root, step.Counters, perRequest);
            }
        }

        if (tally.EveryStatMissed(rows.Count))
        {
            return RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, $"None of your {ids.Count} accounts could be read: {firstMiss}");
        }

        var detail = unavailable.Count > 0
            ? $"{unavailable.Count} of your accounts could not be read: {firstUnavailable}"
            : null;

        return new RecipeReading(ReadingOutcome.Read, detail, rows, [], context, ids.Count)
        {
            Unavailable = unavailable,
            StatMisses = tally.StatMisses(rows.Count),
            CellMisses = tally.CellMisses(rows.Count),
            CounterNames = counterNames ?? [],
            AccountAsOf = asOf,
        };
    }

    private static List<HeadlineValue> HeadlineAt(Recipe recipe, JsonElement root, IReadOnlyDictionary<string, string> values) =>
        [.. recipe.Headline.Select(h =>
        {
            var result = RecipePath.Resolve(root, h.Path, values);
            var found = result.Outcome == PathOutcome.Found;
            double? number = found && JsonNav.TryNumber(result.Value, out var n) ? n : null;
            return new HeadlineValue(h.Label, found ? RecipePath.AsText(result.Value) : null) { Id = h.Id, Number = number };
        })];

    private static AsOfStamp? AsOfAt(JsonElement root, RecipeAsOf asOf, IReadOnlyDictionary<string, string> values)
    {
        if (TimeText.Parse(TextAt(root, asOf.Time, values)) is not { } time) return null;

        bool? stale = null;
        if (asOf.Stale is not null && RecipePath.Resolve(root, asOf.Stale, values) is { Outcome: PathOutcome.Found } said
            && said.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            stale = said.Value.GetBoolean();
        }

        return new AsOfStamp(time, stale);
    }

    private static ReadingPeriod? PeriodOf(Recipe recipe, IReadOnlyDictionary<string, string> values)
    {
        if (recipe.Period is not { } period || !values.TryGetValue(period.Value, out var value)) return null;

        DateTimeOffset? At(string? take) => take is not null && values.TryGetValue(take, out var text) ? TimeText.Parse(text) : null;
        return new ReadingPeriod(value, At(period.Starts), At(period.Ends));
    }

    /// <summary>
    /// Score book spec §6.2: each key under <c>period.past</c>, read with the live rows, value and headline
    /// paths and the period placeholder set to that key. No miss here stops anything; a key whose rows
    /// can't be read still has its headline.
    /// </summary>
    private static IReadOnlyList<PastPeriodReading> PastAt(
        Recipe recipe, RecipeStep step, IReadOnlyList<RecipeStat> stats, JsonElement root, IReadOnlyDictionary<string, string> values)
    {
        if (recipe.Period is not { Past: { } pastPath } period) return [];

        var past = RecipePath.Resolve(root, pastPath, values);
        if (past.Outcome != PathOutcome.Found || past.Value.ValueKind != JsonValueKind.Object) return [];

        var readings = new List<PastPeriodReading>();
        foreach (var property in past.Value.EnumerateObject())
        {
            // Ruling R3: an all-digit key is far likelier a player's id than a period's name.
            if (property.Name.Length == 0 || property.Name.All(char.IsAsciiDigit)) continue;

            var keyed = new Dictionary<string, string>(values, StringComparer.Ordinal) { [period.Value] = property.Name };
            var rowsResult = RecipePath.Resolve(root, step.Rows!, keyed);
            var readable = rowsResult.Outcome == PathOutcome.Found && rowsResult.Value.ValueKind == JsonValueKind.Array;

            var rows = new List<RecipeRow>();
            if (readable)
            {
                foreach (var row in rowsResult.Value.EnumerateArray())
                {
                    var id = RecipePath.Resolve(row, Placeholders.Fill(step.UserId!, keyed, encode: false), "this row");
                    if (id.Outcome != PathOutcome.Found || !JsonNav.TryUserId(id.Value, out var userId)) continue;

                    rows.Add(new RecipeRow(userId, ValuesAt(row, stats, keyed)));
                }
            }

            readings.Add(new PastPeriodReading(property.Name, rows, HeadlineAt(recipe, root, keyed), readable));
        }

        return readings;
    }

    private static RecipeReading ReadGroups(
        RecipeStep step, IReadOnlyList<RecipeStat> stats, JsonElement root, IReadOnlyDictionary<string, string> values, int number, string? context)
    {
        var rowsPath = Placeholders.Fill(step.Rows!, values, encode: false);
        var rowsResult = RecipePath.Resolve(root, step.Rows!, values);
        if (rowsResult.Outcome != PathOutcome.Found || rowsResult.Value.ValueKind != JsonValueKind.Array)
        {
            return RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, $"Step {number}: '{rowsPath}' is not a list, so there are no groups to read.");
        }

        var groups = new List<GroupRow>();
        var total = 0;
        foreach (var row in rowsResult.Value.EnumerateArray())
        {
            total++;
            if (TextAt(row, step.GroupName!, values) is not { Length: > 0 } name) continue;

            int? rank = step.Rank is not null
                        && NumberAt(RecipePath.Resolve(row, step.Rank, values, "this row"), step.Rank, "in this row", out var r) is null
                ? (int)r
                : null;

            groups.Add(new GroupRow(name, ValuesAt(row, stats, values), rank));
        }

        if (total > 0 && groups.Count == 0)
        {
            return RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, $"None of the {total} groups had a name at '{step.GroupName}'.");
        }

        return new RecipeReading(ReadingOutcome.Read, null, [], [], context, total)
        {
            Groups = groups,
            ListAsOf = step.AsOf is null ? null : AsOfAt(root, step.AsOf, values),
        };
    }

    /// <summary>Every tracked stat this row or group has a number for (stats design §4): a miss just leaves the key out.</summary>
    private static Dictionary<string, double> ValuesAt(JsonElement row, IReadOnlyList<RecipeStat> stats, IReadOnlyDictionary<string, string> values)
    {
        var found = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var stat in stats)
        {
            if (StatNumberAt(RecipePath.Resolve(row, stat.Path, values, "this row"), stat, stat.Path, "in this row", out var value) is null)
            {
                found[stat.Key] = value;
            }
        }

        return found;
    }

    /// <summary>Why a stat has no number here, or null with the number.</summary>
    private static string? NumberAt(PathResult result, string path, string where, out double number)
    {
        number = 0;
        if (result.Outcome != PathOutcome.Found) return result.Miss ?? $"'{path}' was empty {where}.";
        if (JsonNav.TryNumber(result.Value, out number)) return null;

        return result.Value.ValueKind == JsonValueKind.String
            ? $"'{path}' is text {where}, not a number."
            : $"'{path}' is not a finite number {where}.";
    }

    /// <summary>
    /// Why a stat has no number here, or null with its number: the value itself, or for a counting stat (D9) how
    /// many entries its object or list holds. Only the count is read, never the keys.
    /// </summary>
    private static string? StatNumberAt(PathResult result, RecipeStat stat, string path, string where, out double number)
    {
        if (!stat.Count) return NumberAt(result, path, where, out number);

        number = 0;
        if (result.Outcome != PathOutcome.Found) return result.Miss ?? $"'{path}' was empty {where}.";

        switch (result.Value.ValueKind)
        {
            case JsonValueKind.Object:
                number = result.Value.EnumerateObject().Count();
                return null;
            case JsonValueKind.Array:
                number = result.Value.GetArrayLength();
                return null;
            default:
                return $"'{path}' is not a list or an object {where}, so its entries can't be counted.";
        }
    }

    private static string? TextAt(JsonElement root, string pathTemplate, IReadOnlyDictionary<string, string> values)
    {
        var result = RecipePath.Resolve(root, pathTemplate, values);
        return result.Outcome == PathOutcome.Found ? RecipePath.AsText(result.Value) : null;
    }

    /// <summary>Stats design §4: the keys under the counters path whose values are numbers and could be picked.</summary>
    private static IReadOnlyList<string>? CounterNamesAt(JsonElement root, RecipeCounters counters, IReadOnlyDictionary<string, string> values)
    {
        var result = RecipePath.Resolve(root, counters.Path, values);
        if (result.Outcome != PathOutcome.Found || result.Value.ValueKind != JsonValueKind.Object) return null;

        return [.. result.Value.EnumerateObject()
            .Where(property => JsonNav.TryNumber(property.Value, out _) && RecipeStats.CanPick(property.Name))
            .Select(property => property.Name)];
    }

    private static string AppendQuery(string url, List<(string Name, string Value)> query)
    {
        if (query.Count == 0) return url;

        var builder = new StringBuilder(url);
        var separator = url.Contains('?') ? '&' : '?';
        foreach (var (name, value) in query)
        {
            builder.Append(separator).Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(value));
            separator = '&';
        }

        return builder.ToString();
    }

    private static string? Context(IReadOnlyDictionary<string, string> values, List<string> taken) =>
        taken.Count == 0 ? null : string.Join("; ", taken.Select(name => $"{name}={values[name]}"));

    /// <summary>
    /// Which stats were found anywhere this cycle, and each miss. A stat found nowhere is a stat-wide
    /// miss, reported once; its per-row misses are not repeated as cells.
    /// </summary>
    private sealed class StatTally(IReadOnlyList<RecipeStat> stats)
    {
        private readonly HashSet<string> _found = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _firstMiss = new(StringComparer.Ordinal);
        private readonly Dictionary<(long UserId, string Stat), string> _cells = [];

        public void Found(string key) => _found.Add(key);

        public void Missed(long userId, string key, string miss)
        {
            _firstMiss.TryAdd(key, miss);
            _cells[(userId, key)] = miss;
        }

        public bool EveryStatMissed(int rowsRead) => rowsRead > 0 && stats.All(stat => !_found.Contains(stat.Key));

        public IReadOnlyDictionary<string, string> StatMisses(int rowsRead) => rowsRead == 0
            ? new Dictionary<string, string>()
            : stats.Where(stat => !_found.Contains(stat.Key)).ToDictionary(stat => stat.Key, stat => _firstMiss[stat.Key], StringComparer.Ordinal);

        public IReadOnlyDictionary<(long UserId, string Stat), string> CellMisses(int rowsRead) =>
            _cells.Where(cell => rowsRead > 0 && _found.Contains(cell.Key.Stat)).ToDictionary(cell => cell.Key, cell => cell.Value);
    }
}
