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

public sealed record RecipeRow(long UserId, double Value);

public sealed record HeadlineValue(string Label, string? Text);

/// <summary>
/// What one run of a recipe found. <see cref="Rows"/> is every readable row, the user's own and
/// everyone else's; the watch decides which are the user's. <see cref="Context"/> changes when the
/// thing being read changes, such as a new clan battle.
/// </summary>
public sealed record RecipeReading(
    ReadingOutcome Outcome,
    string? Detail,
    IReadOnlyList<RecipeRow> Rows,
    IReadOnlyList<HeadlineValue> Headline,
    string? Context,
    int RowsSeen)
{
    public static RecipeReading Stop(ReadingOutcome outcome, string detail) => new(outcome, detail, [], [], null, 0);
}

/// <summary>The seam <c>RecipeWatch</c> is tested against.</summary>
public interface IRecipeEngine
{
    Task<RecipeReading> ReadAsync(
        Recipe recipe, IReadOnlyDictionary<string, string> inputs, IReadOnlyCollection<long> accountUserIds,
        CancellationToken cancellationToken);
}

/// <summary>
/// Runs a recipe's steps in order (spec §4.1). Reads; never decides what happens with what it read.
/// </summary>
public sealed class RecipeEngine(IRecipeTransport transport, IKeyStore keys) : IRecipeEngine
{
    public async Task<RecipeReading> ReadAsync(
        Recipe recipe, IReadOnlyDictionary<string, string> inputs, IReadOnlyCollection<long> accountUserIds,
        CancellationToken cancellationToken)
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

        var taken = new List<string>();

        for (var index = 0; index < recipe.Steps.Count; index++)
        {
            var step = recipe.Steps[index];
            var number = index + 1;
            var label = $"{recipe.Slug}-step{number}";
            var isLast = index == recipe.Steps.Count - 1;

            if (isLast && step.PerAccount)
            {
                return await ReadPerAccountAsync(recipe, step, values, accountUserIds, label, Context(values, taken), cancellationToken)
                    .ConfigureAwait(false);
            }

            var (document, stop) = await FetchJsonAsync(recipe, step, values, label, cancellationToken).ConfigureAwait(false);
            if (stop is not null) return stop;

            using (document!)
            {
                if (isLast)
                {
                    return ReadList(recipe, step, document!.RootElement, values, number, Context(values, taken));
                }

                foreach (var (name, pathTemplate) in step.Take)
                {
                    var path = Placeholders.Fill(pathTemplate, values, encode: false);
                    var result = RecipePath.Resolve(document!.RootElement, path);

                    if (result.Outcome == PathOutcome.Found && RecipePath.AsText(result.Value) is { Length: > 0 } text)
                    {
                        values[name] = text;
                        taken.Add(name);
                        continue;
                    }

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
        Recipe recipe, RecipeStep step, JsonElement root, IReadOnlyDictionary<string, string> values, int number, string? context)
    {
        var rowsPath = Placeholders.Fill(step.Rows!, values, encode: false);
        var rowsResult = RecipePath.Resolve(root, rowsPath);

        if (rowsResult.Outcome == PathOutcome.Missing)
        {
            return RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, $"Step {number}: {rowsResult.Miss}");
        }

        if (rowsResult.Outcome == PathOutcome.Nothing || rowsResult.Value.ValueKind != JsonValueKind.Array)
        {
            var what = rowsResult.Outcome == PathOutcome.Nothing ? "empty" : "not a list";
            return RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, $"Step {number}: '{rowsPath}' is {what}, so there are no rows to read.");
        }

        var userIdPath = Placeholders.Fill(step.UserId!, values, encode: false);
        var valuePath = Placeholders.Fill(step.Value!, values, encode: false);
        var rows = new List<RecipeRow>();
        string? firstProblem = null;
        var total = 0;

        foreach (var row in rowsResult.Value.EnumerateArray())
        {
            total++;
            var problem = ReadRow(row, userIdPath, valuePath, out var parsed);
            if (problem is null) rows.Add(parsed);
            else firstProblem ??= problem;
        }

        if (total > 0 && rows.Count == 0)
        {
            return RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, $"None of the {total} rows could be read: {firstProblem}");
        }

        var headline = recipe.Headline
            .Select(h =>
            {
                var result = RecipePath.Resolve(root, Placeholders.Fill(h.Path, values, encode: false));
                return new HeadlineValue(h.Label, result.Outcome == PathOutcome.Found ? RecipePath.AsText(result.Value) : null);
            })
            .ToList();

        return new RecipeReading(ReadingOutcome.Read, null, rows, headline, context, total);
    }

    private static string? ReadRow(JsonElement row, string userIdPath, string valuePath, out RecipeRow parsed)
    {
        parsed = new RecipeRow(0, 0);

        var id = RecipePath.Resolve(row, userIdPath, "this row");
        if (id.Outcome != PathOutcome.Found) return id.Miss ?? $"'{userIdPath}' was empty in this row.";
        if (!JsonNav.TryUserId(id.Value, out var userId)) return $"'{userIdPath}' is not a whole number a user id can be.";

        var value = RecipePath.Resolve(row, valuePath, "this row");
        if (value.Outcome != PathOutcome.Found) return value.Miss ?? $"'{valuePath}' was empty in this row.";
        if (!JsonNav.TryNumber(value.Value, out var number))
        {
            return value.Value.ValueKind == JsonValueKind.String
                ? $"'{valuePath}' is text in this row, not a number."
                : $"'{valuePath}' is not a finite number in this row.";
        }

        parsed = new RecipeRow(userId, number);
        return null;
    }

    private async Task<RecipeReading> ReadPerAccountAsync(
        Recipe recipe, RecipeStep step, Dictionary<string, string> values, IReadOnlyCollection<long> accountUserIds,
        string label, string? context, CancellationToken cancellationToken)
    {
        var ids = accountUserIds.Where(id => id > 0).Distinct().ToList();
        var rows = new List<RecipeRow>();
        string? firstProblem = null;

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
                    firstProblem ??= stop.Detail;
                    continue;
                }

                return stop;
            }

            using (document!)
            {
                var valuePath = Placeholders.Fill(step.Value!, values, encode: false);
                var result = RecipePath.Resolve(document!.RootElement, valuePath);

                if (result.Outcome != PathOutcome.Found)
                {
                    firstProblem ??= result.Miss ?? $"'{valuePath}' was empty for user id {userId}.";
                    continue;
                }

                if (!JsonNav.TryNumber(result.Value, out var number))
                {
                    firstProblem ??= $"'{valuePath}' is not a finite number for user id {userId}.";
                    continue;
                }

                rows.Add(new RecipeRow(userId, number));
            }
        }

        if (ids.Count > 0 && rows.Count == 0)
        {
            return RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, $"None of your {ids.Count} accounts could be read: {firstProblem}");
        }

        var detail = rows.Count < ids.Count
            ? $"{ids.Count - rows.Count} of your accounts could not be read: {firstProblem}"
            : null;

        return new RecipeReading(ReadingOutcome.Read, detail, rows, [], context, ids.Count);
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
}
