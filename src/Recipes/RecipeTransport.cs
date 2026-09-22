using System.IO;
using System.Net.Http;
using Labs626.UrScore.Fetch;

namespace Labs626.UrScore.Recipes;

/// <summary>
/// One GET. <see cref="Body"/> and <see cref="Status"/> are set whenever the server answered,
/// success or not; <see cref="Error"/> only when it never did.
/// </summary>
public sealed record FetchResult(int? Status, string? Body, string? Error)
{
    public bool Answered => Status is not null;

    public bool Succeeded => Status is >= 200 and < 300;
}

/// <summary>The seam the engine is tested against, so no test needs a network.</summary>
public interface IRecipeTransport
{
    Task<FetchResult> GetAsync(
        Uri url, IReadOnlyDictionary<string, string> headers, string label, CancellationToken cancellationToken);
}

/// <summary>
/// The only code in Ur Score that opens a connection for a recipe. Carries over every rule the
/// retired <c>ClanClient</c> learned: a 30-second bound of its own, a stop the caller asked for
/// propagates while our own timeout becomes an error, and the raw body is saved before the status
/// check because a failing body that explains itself is the thing worth having.
/// </summary>
public sealed class HttpRecipeTransport(HttpClient http, string? rawDirectory, Redactor redactor) : IRecipeTransport
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The handler every real recipe client must use. Redirects are never followed: a 3xx would send
    /// the request, its inputs, user ids and keys to a host the import screen never named (spec §6.1,
    /// §7.2). Cookies are off so one source cannot set state another request carries.
    /// </summary>
    public static SocketsHttpHandler CreateHandler() => new() { AllowAutoRedirect = false, UseCookies = false };

    public async Task<FetchResult> GetAsync(
        Uri url, IReadOnlyDictionary<string, string> headers, string label, CancellationToken cancellationToken)
    {
        // The parser refuses anything but https; this is the second gate, on the code that can connect.
        if (url.Scheme != Uri.UriSchemeHttps)
        {
            return new FetchResult(null, null, $"Refused to contact {url.Host}: recipes may only use https.");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(UrScoreIdentity.UserAgent);
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }

            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

            SaveRaw(label, body);
            return new FetchResult((int)response.StatusCode, body, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new FetchResult(null, null, redactor.Redact($"Could not reach {url.Host}: {ex.Message}"));
        }
    }

    /// <summary>One file per label, overwritten each poll, redacted, never transmitted.</summary>
    private void SaveRaw(string label, string body)
    {
        if (rawDirectory is null) return;

        try
        {
            Directory.CreateDirectory(rawDirectory);
            var safe = string.Concat(label.Select(c => char.IsLetterOrDigit(c) || c is '-' or '.' ? c : '-'));
            File.WriteAllText(Path.Combine(rawDirectory, $"{safe}.json"), redactor.Redact(body));
        }
        catch (Exception)
        {
            // Diagnostics must never break the thing they diagnose.
        }
    }
}
