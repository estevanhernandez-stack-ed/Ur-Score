using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace Labs626.UrScore.Source;

/// <summary>The seam the window and the tests use, so neither needs the network.</summary>
public interface INameSource
{
    Task<IReadOnlyDictionary<long, string>> ResolveAsync(
        IReadOnlyCollection<long> ids, CancellationToken cancellationToken);
}

/// <summary>
/// Roblox user ids to usernames, so a leaderboard reads as a roster rather than a column of
/// numbers.
/// <para>
/// The plugin's SECOND outbound endpoint, and the first carrying other people's identifiers. Spec
/// §6.1 states why that is acceptable rather than leaving it implied: ids Roblox issued go to
/// Roblox for names Roblox publishes, and they arrived from a public endpoint in the first place.
/// The user can switch it off (`Settings.ResolveNames`), and with it off the plugin makes exactly
/// one outbound call per poll and none about anybody else.
/// </para>
/// <para>
/// Cached for the life of the process and never re-asked. Usernames change rarely and a clan
/// battle is a few hours; re-resolving 75 of them every three minutes would be twenty requests an
/// hour against someone else's service for data that does not move. A FAILURE is never cached —
/// one rate-limited minute must not cost the names for the whole session.
/// </para>
/// <para>
/// Never throws. Names are decoration on a leaderboard that works without them, so a failure here
/// costs the names and nothing else: not the poll, not the ranking, and certainly not the
/// reporting.
/// </para>
/// </summary>
public sealed class NameClient(HttpClient http) : INameSource
{
    public const string Endpoint = "https://users.roblox.com/v1/users";

    /// <summary>The endpoint's ceiling. A clan battle's ~75 contributors fit in one request.</summary>
    public const int BatchLimit = 100;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly Dictionary<long, string> _cache = [];

    public async Task<IReadOnlyDictionary<long, string>> ResolveAsync(
        IReadOnlyCollection<long> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0) return _cache;

        var missing = ids.Where(id => !_cache.ContainsKey(id)).Distinct().ToList();

        for (var offset = 0; offset < missing.Count; offset += BatchLimit)
        {
            var batch = missing.Skip(offset).Take(BatchLimit).ToList();
            var wanted = batch.ToHashSet();
            foreach (var (id, name) in await FetchAsync(batch, cancellationToken).ConfigureAwait(false))
            {
                // Cache only what THIS batch asked for. Anything else in the body — an id we did
                // not request, echoed back for whatever reason — is not trusted into the cache
                // just because the endpoint said so; that keeps "asked for" and "known" the same
                // set, which is what lets a later request tell a truly-unseen id apart from one
                // this cache should already have an opinion on.
                if (wanted.Contains(id)) _cache[id] = name;
            }
        }

        // Only what was asked for, so a caller cannot accidentally render the whole cache.
        return ids.Where(_cache.ContainsKey).Distinct().ToDictionary(id => id, id => _cache[id]);
    }

    private async Task<IReadOnlyList<(long Id, string Name)>> FetchAsync(
        IReadOnlyList<long> batch, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                // excludeBannedUsers false: a banned member's points are still in the battle, and a
                // gap in the leaderboard would read as a bug.
                Content = JsonContent.Create(new { userIds = batch, excludeBannedUsers = false }),
            };
            request.Headers.UserAgent.ParseAdd(ClanClient.UserAgent);

            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return [];

            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return Parse(body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Same rule as ClanClient: a stop the user asked for propagates; our own timeout does
            // not, and becomes an absence of names.
            throw;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static IReadOnlyList<(long Id, string Name)> Parse(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!JsonNav.TryGet(document.RootElement, "data", out var data)
                || data.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var names = new List<(long, string)>();
            foreach (var row in data.EnumerateArray())
            {
                if (!JsonNav.TryGet(row, "id", out var idElement)) continue;
                if (!JsonNav.TryGet(row, "name", out var nameElement)) continue;
                if (!JsonNav.TryUserId(idElement, out var id)) continue;

                var name = nameElement.ValueKind == JsonValueKind.String ? nameElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) continue;

                names.Add((id, name));
            }

            return names;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
