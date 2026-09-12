using System.IO;
using System.Net.Http;
using System.Reflection;

namespace Labs626.UrScore.Source;

/// <summary>The seam the watch loop is tested against, so no test needs a network.</summary>
public interface IClanSource
{
    Task<BattleProbe> ActiveBattleAsync(CancellationToken cancellationToken);

    Task<ContributionsResult> ContributionsAsync(
        string clanName, string configName, CancellationToken cancellationToken);
}

/// <summary>
/// The two calls, and nothing else.
/// <para>
/// THIS IS THE ONLY FILE THAT NAMES THE VENDOR, and that is the whole reason this plugin exists
/// separately from RoRoRo. The host ships no endpoint, no field path and no vendor name, and a
/// fence in the host repo fails its build if that ever changes. A plugin is where this belongs.
/// </para>
/// </summary>
public sealed class ClanClient(HttpClient http, string? rawDirectory) : IClanSource
{
    private const string BaseUrl = "https://ps99.biggamesapi.io/api";

    /// <summary>Identifies us to the service being polled.</summary>
    public static string UserAgent { get; } =
        $"UrScore/{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0"} (RoRoRo plugin)";

    /// <summary>
    /// One line of credit for the window (spec §6.1, "Attribution"). A window on one person's
    /// machine is not the vendor's terms' "public display" and does not trigger their attribution
    /// clause, but the alternative — showing someone else's clan data with no word on where it came
    /// from — is a tool presenting someone else's work as its own, and the cost of saying so is one
    /// line.
    /// <para>
    /// Lives HERE rather than in the window's own code so this stays the only file naming the
    /// vendor: the window reads this constant instead of holding its own copy of the name.
    /// </para>
    /// </summary>
    public const string Attribution =
        "Clan battle data comes from Big Games' public Pet Simulator 99 API (ps99.biggamesapi.io). "
        + "Ur Score is not made by, endorsed by, or affiliated with Big Games or Roblox.";

    /// <summary>
    /// Bounds one request. Two calls per poll against a three-minute cadence, so the framework's
    /// default 100 seconds is far too long — a slow endpoint would eat most of the interval before
    /// giving up.
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public async Task<BattleProbe> ActiveBattleAsync(CancellationToken cancellationToken)
    {
        var (body, error) = await GetAsync($"{BaseUrl}/activeClanBattle", "active-battle", cancellationToken)
            .ConfigureAwait(false);

        return error is not null
            ? new BattleProbe(null, error, MissIsTransport: true)
            : ClanParser.ActiveBattle(body!);
    }

    public async Task<ContributionsResult> ContributionsAsync(
        string clanName, string configName, CancellationToken cancellationToken)
    {
        var url = $"{BaseUrl}/clan/{Uri.EscapeDataString(clanName)}";
        var (body, error) = await GetAsync(url, "clan", cancellationToken).ConfigureAwait(false);

        return error is not null
            ? new ContributionsResult([], error, MissIsTransport: true)
            : ClanParser.Contributions(body!, configName);
    }

    private async Task<(string? Body, string? Error)> GetAsync(
        string url, string label, CancellationToken cancellationToken)
    {
        try
        {
            // Linked, so a caller's stop still stops us, and CancelAfter gives us our own bound
            // regardless of the HttpClient we were handed.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(UserAgent);

            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

            // Saved BEFORE the status check: a 500 carrying a body that explains itself is exactly
            // the thing worth having on disk.
            SaveRaw(label, body);

            return response.IsSuccessStatusCode
                ? (body, null)
                : (null, $"The {label} request returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
        // Guarded on the CALLER's token, not the linked one. A stop the user asked for is not a
        // failure to report and must propagate; our own timeout is a transport failure and must
        // fall through to the catch below and become a Miss. Before this guard, a merely slow
        // endpoint threw out of the poll loop dressed as a deliberate stop.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A DNS failure, a TLS failure, a dropped connection. To the window they are one thing:
            // we could not reach it. The message carries the detail.
            return (null, $"Could not reach the {label} endpoint: {ex.Message}");
        }
    }

    /// <summary>
    /// One file per call, overwritten each poll. Never transmitted, and never rotated into a
    /// growing pile — so "what did it actually send me" is always answerable and never a disk leak.
    /// </summary>
    private void SaveRaw(string label, string body)
    {
        if (rawDirectory is null) return;

        try
        {
            Directory.CreateDirectory(rawDirectory);
            File.WriteAllText(Path.Combine(rawDirectory, $"{label}.json"), body);
        }
        catch (Exception)
        {
            // Diagnostics must never break the thing they diagnose.
        }
    }
}
