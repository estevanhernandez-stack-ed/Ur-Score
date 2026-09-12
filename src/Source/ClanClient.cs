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
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(UserAgent);

            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            // Saved BEFORE the status check: a 500 carrying a body that explains itself is exactly
            // the thing worth having on disk.
            SaveRaw(label, body);

            return response.IsSuccessStatusCode
                ? (body, null)
                : (null, $"The {label} request returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
        catch (OperationCanceledException)
        {
            throw;   // a stop is not a failure to report
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
