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

public sealed record HeadlineValue(string Label, string? Text);

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
    public const string NothingTracked = "No stat is ticked to show or send. Choose some in Recipe settings.";

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
        var stats = RecipeStats.Offered(recipe, trackedStats).Where(stat => trackedStats.Contains(stat.Key)).ToList();
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
                return await ReadPerAccountAsync(recipe, step, stats, values, accountUserIds, label, Context(values, taken), cancellationToken)
                    .ConfigureAwait(false);
            }

            var (document, stop) = await FetchJsonAsync(recipe, step, values, label, cancellationToken).ConfigureAwait(false);
            if (stop is not null) return stop;

            using (document!)
            {
                if (isLast)
                {
                    return ReadList(recipe, step, stats, document!.RootElement, values, number, Context(values, taken));
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

                    if (Absent(step, result) is { } absent) return absent;

                    if (result.Outcome == PathOutcome.Nothing && step.IdleWithout == name)
                    {
                        return RecipeReading.Stop(ReadingOutcome.Idle, step.IdleMessage ?? "Nothing to read right now.");
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

    private async Task<(JsonDocument? Document, RecipeReading? Stop)> FetchJsonAsync(
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
                    $"This recipe needs your {declared.Label} key. Get one at {RecipeHosts.HostOf(declared.GetOneAt)}."));
            }

            if (!string.Equals(saved.Host, host, StringComparison.OrdinalIgnoreCase))
            {
                // Spec §7.2: a key is only ever sent to the host it is bound to. Checked here as well
                // as at import, because a recipe file on disk can be edited after it was imported.
                return (null, RecipeReading.Stop(ReadingOutcome.KeyMissing,
                    $"Your {declared.Label} key is saved for {saved.Host}, and this recipe would send it to {host}. It was not sent."));
            }

            if (declared.In == KeyPlacement.Header) headers[declared.Name] = saved.Value;
            else query.Add((declared.Name, saved.Value));
        }

        var address = AppendQuery(Placeholders.Fill(step.Url, values, encode: true), query);
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return (null, RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, $"The address for {host} is not valid once filled in."));
        }

        var fetched = await transport.GetAsync(uri, headers, label, cancellationToken).ConfigureAwait(false);

        var stop = Classify(recipe, step, fetched, host, values);
        if (stop is not null) return (null, stop);

        try
        {
            return (JsonDocument.Parse(fetched.Body ?? ""), null);
        }
        catch (JsonException ex)
        {
            return (null, RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, $"{host} did not return valid JSON: {ex.Message}"));
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

        var rowsPath = Placeholders.Fill(step.Rows!, values, encode: false);
        var rowsResult = RecipePath.Resolve(root, step.Rows!, values);

        if (rowsResult.Outcome == PathOutcome.Missing)
        {
            return (Absent(step, rowsResult) ?? RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, $"Step {number}: {rowsResult.Miss}"))
                with { IconText = icon };
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
                if (Absent(step, result) is { } absent) return absent with { IconText = icon };

                var miss = NumberAt(result, Placeholders.Fill(stat.Path, values, encode: false), "in this row", out var value);
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

        var headline = recipe.Headline
            .Select(h => new HeadlineValue(h.Label, TextAt(root, h.Path, values)))
            .ToList();

        return new RecipeReading(ReadingOutcome.Read, null, rows, headline, context, total)
        {
            StatMisses = tally.StatMisses(rows.Count),
            CellMisses = tally.CellMisses(rows.Count),
            CounterNames = counterNames ?? [],
            IconText = icon,
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

            var (document, stop) = await FetchJsonAsync(recipe, step, perRequest, label, cancellationToken).ConfigureAwait(false);
            if (stop is not null)
            {
                if (stop.Outcome == ReadingOutcome.InputNotFound)
                {
                    // Plan Ruling 5: one account's 404. The recipe's own words when it declares
                    // unavailable, else part 1's text naming the host.
                    unavailable[userId] = step.Unavailable?.Message ?? stop.Detail!;
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

                    var miss = NumberAt(result, Placeholders.Fill(stat.Path, perRequest, encode: false), $"for user id {userId}", out var value);
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
        };
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
