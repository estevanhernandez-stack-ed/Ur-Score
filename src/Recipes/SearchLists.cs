using System.Text.Json;

namespace Labs626.UrScore.Recipes;

/// <summary>An input's search list as read, or why it couldn't be.</summary>
public sealed record SearchListResult(IReadOnlyList<string> Names, string? Problem);

/// <summary>
/// The one path that reads an input's <c>search</c> list (spec §7.1). Goes through the shared recipe
/// transport, so the request is spaced per host and uses the no-redirect, no-cookie handler. A list read
/// successfully is kept in memory for the session and never written anywhere. A failure is not kept, so
/// the next ask tries again. The list's address is fixed (the parser refuses placeholders), so nothing
/// about you is sent.
/// </summary>
public sealed class SearchLists(IRecipeTransport transport)
{
    public const int MatchLimit = 8;

    private readonly Dictionary<string, Task<SearchListResult>> _cache = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public Task<SearchListResult> GetAsync(RecipeSearch search, CancellationToken cancellationToken)
    {
        var key = search.Url + "\u001f" + search.List;
        Task<SearchListResult> task;

        lock (_gate)
        {
            if (!_cache.TryGetValue(key, out var cached) || Failed(cached))
            {
                // Not tied to the caller's token: a page closing mid-read must not spoil the list for the next one.
                cached = FetchAsync(search);
                _cache[key] = cached;
            }

            task = cached;
        }

        return task.WaitAsync(cancellationToken);
    }

    /// <summary>An exact match first, then names starting with the query, then names containing it, each in list order.</summary>
    public static IReadOnlyList<string> Match(IReadOnlyList<string> names, string? query, int limit = MatchLimit)
    {
        var trimmed = query?.Trim() ?? "";
        if (trimmed.Length == 0 || limit <= 0) return [];

        var exact = new List<string>();
        var starts = new List<string>();
        var inside = new List<string>();

        foreach (var name in names)
        {
            if (name.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                exact.Add(name);
            }
            else if (name.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                if (starts.Count < limit) starts.Add(name);
            }
            else if (inside.Count < limit && name.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                inside.Add(name);
            }
        }

        return [.. exact.Concat(starts).Concat(inside).Take(limit)];
    }

    private static bool Failed(Task<SearchListResult> task) =>
        task.IsCompleted && (!task.IsCompletedSuccessfully || task.Result.Problem is not null);

    private async Task<SearchListResult> FetchAsync(RecipeSearch search)
    {
        var host = RecipeHosts.HostOf(search.Url);
        if (!Uri.TryCreate(search.Url, UriKind.Absolute, out var address))
        {
            return new SearchListResult([], $"The list address for {host} is not valid.");
        }

        try
        {
            var fetched = await transport.GetAsync(address, new Dictionary<string, string>(), "search-list", CancellationToken.None)
                .ConfigureAwait(false);

            if (!fetched.Answered) return new SearchListResult([], fetched.Error ?? $"Could not reach {host}.");
            if (!fetched.Succeeded) return new SearchListResult([], $"{host} answered {fetched.Status}.");

            using var document = JsonDocument.Parse(fetched.Body ?? "");
            var found = RecipePath.Resolve(document.RootElement, search.List);
            if (found.Outcome != PathOutcome.Found || found.Value.ValueKind != JsonValueKind.Array)
            {
                return new SearchListResult([], $"{host} did not return a list at '{search.List}'.");
            }

            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in found.Value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } name && seen.Add(name)) names.Add(name);
            }

            return names.Count == 0
                ? new SearchListResult([], $"{host} returned an empty list.")
                : new SearchListResult(names, null);
        }
        catch (JsonException)
        {
            return new SearchListResult([], $"{host} did not return valid JSON.");
        }
        catch (Exception ex)
        {
            return new SearchListResult([], $"Could not read the list from {host} ({ex.GetType().Name}).");
        }
    }
}
