using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Labs626.UrScore.Source;

/// <summary>
/// Turns a recipe's icon text into a picture file on disk, or nothing (stats design §3.3).
/// <para>
/// The second named exemption from the hostname fence, beside <c>NameClient</c>: Roblox's own
/// thumbnails service behind an Ur Score feature, not a stat source. The picture itself comes from
/// wherever Roblox's answer says, and only when that host is on Roblox's picture domain.
/// </para>
/// <para>
/// Every request follows the recipe client's rules: the handler must be
/// <c>HttpRecipeTransport.CreateHandler()</c> (no redirects, no cookies), and each request carries Ur
/// Score's User-Agent. Never throws for anything but a stop the caller asked for; a failure costs the
/// icon, and the window keeps Ur Score's own.
/// </para>
/// </summary>
public sealed class IconClient
{
    public const string ThumbnailsHost = "thumbnails.roblox.com";

    /// <summary>
    /// Roblox's picture domain, which an image host must equal or end with. Written in parts so the
    /// hostname fence, which pins this file to <see cref="ThumbnailsHost"/>, does not read a suffix check
    /// as a second host Ur Score contacts on its own. <c>NoHostnameFenceTests</c> pins the value.
    /// </summary>
    public const string PictureDomain = "rbxcdn" + "." + "com";

    /// <summary>The picture host Roblox answered with when this was verified (2026-09-13), for the import screen only.</summary>
    public const string PictureHostShown = "tr." + PictureDomain;

    public const string AssetScheme = "rbxassetid://";

    public const int MaxBytes = 1024 * 1024;

    public static readonly TimeSpan CacheFor = TimeSpan.FromDays(7);

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly string _cacheDirectory;
    private readonly Func<DateTimeOffset> _clock;

    public IconClient(HttpMessageHandler handler, string cacheDirectory, Func<DateTimeOffset> clock)
    {
        _http = new HttpClient(handler) { Timeout = RequestTimeout };
        _cacheDirectory = cacheDirectory;
        _clock = clock;
    }

    public static string DefaultCacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "626labs.ur-score", "icon-cache");

    /// <summary>
    /// The cached picture for <paramref name="iconText"/>, fetching it first when the cache has none
    /// younger than <see cref="CacheFor"/>. Null for anything that is not an asset id or an https
    /// address on one of <paramref name="recipeHosts"/>, and for any failed or refused fetch.
    /// </summary>
    public async Task<string?> ResolveAsync(string? iconText, IReadOnlySet<string> recipeHosts, CancellationToken cancellationToken)
    {
        var text = iconText?.Trim() ?? "";

        if (text.StartsWith(AssetScheme, StringComparison.OrdinalIgnoreCase)
            && long.TryParse(text[AssetScheme.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var assetId)
            && assetId > 0)
        {
            var file = CachePath(assetId.ToString(CultureInfo.InvariantCulture));
            if (IsFresh(file)) return file;

            var imageUrl = await ThumbnailUrlAsync(assetId, cancellationToken).ConfigureAwait(false);
            return imageUrl is not null && IsPictureHost(imageUrl)
                ? await DownloadAsync(imageUrl, file, cancellationToken).ConfigureAwait(false)
                : null;
        }

        if (Uri.TryCreate(text, UriKind.Absolute, out var address)
            && address.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(address.UserInfo)
            && recipeHosts.Contains(address.Host.ToLowerInvariant()))
        {
            var key = "url-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(address.AbsoluteUri)))[..32];
            var file = CachePath(key);
            if (IsFresh(file)) return file;

            return await DownloadAsync(address, file, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>https, and a host equal to the picture domain or ending with a dot and the picture domain.</summary>
    public static bool IsPictureHost(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(url.UserInfo)
        && (string.Equals(url.Host, PictureDomain, StringComparison.OrdinalIgnoreCase)
            || url.Host.EndsWith("." + PictureDomain, StringComparison.OrdinalIgnoreCase));

    private string CachePath(string key) => Path.Combine(_cacheDirectory, key + ".png");

    private bool IsFresh(string file) =>
        File.Exists(file) && _clock() - new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero) < CacheFor;

    private async Task<Uri?> ThumbnailUrlAsync(long assetId, CancellationToken cancellationToken)
    {
        var address = new Uri(
            $"https://{ThumbnailsHost}/v1/assets?assetIds={assetId.ToString(CultureInfo.InvariantCulture)}&size=150x150&format=Png");

        try
        {
            using var request = Get(address);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);

            if (!JsonNav.TryGet(document.RootElement, "data", out var data)
                || data.ValueKind != JsonValueKind.Array
                || data.GetArrayLength() == 0)
            {
                return null;
            }

            var first = data[0];
            if (!JsonNav.TryGet(first, "state", out var state) || state.ValueKind != JsonValueKind.String
                || !string.Equals(state.GetString(), "Completed", StringComparison.Ordinal))
            {
                return null;
            }

            return JsonNav.TryGet(first, "imageUrl", out var imageUrl)
                   && imageUrl.ValueKind == JsonValueKind.String
                   && Uri.TryCreate(imageUrl.GetString(), UriKind.Absolute, out var url)
                ? url
                : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>At most <see cref="MaxBytes"/>, PNG or JPEG by its first bytes, written whole or not at all.</summary>
    private async Task<string?> DownloadAsync(Uri url, string file, CancellationToken cancellationToken)
    {
        try
        {
            using var request = Get(url);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            if (response.Content.Headers.ContentLength is > MaxBytes) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxBytes) return null;
            }

            var bytes = buffer.ToArray();
            if (!IsPng(bytes) && !IsJpeg(bytes)) return null;

            Directory.CreateDirectory(_cacheDirectory);
            var partial = file + ".partial";
            await File.WriteAllBytesAsync(partial, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(partial, file, overwrite: true);
            File.SetLastWriteTimeUtc(file, _clock().UtcDateTime);
            return file;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static HttpRequestMessage Get(Uri url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(UrScoreIdentity.UserAgent);
        return request;
    }

    private static bool IsPng(byte[] bytes) =>
        bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

    private static bool IsJpeg(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
}
