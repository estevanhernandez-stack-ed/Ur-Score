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
/// It also fetches the headshot of each of YOUR OWN accounts (<see cref="HeadshotsAsync"/>), for the rows that name
/// them. Same host, same handler, same User-Agent, same size cap, same picture-domain check. No other player's id is
/// ever passed in: <c>AppServices</c> asks with RoRoRo's list of your accounts and nothing else (plan A22).
/// </para>
/// <para>
/// Every request follows the recipe client's rules: the handler must be
/// <c>HttpRecipeTransport.CreateHandler()</c> (no redirects, no cookies), and each request carries Ur
/// Score's User-Agent. Never throws for anything but a stop the caller asked for; a failure costs the
/// icon, and the window keeps Ur Score's own.
/// </para>
/// </summary>
public sealed class IconClient : IAvatarSource, IIconSource
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

    /// <summary>Roblox's own supported headshot size, the nearest above the 20-40 px a row draws (plan A25).</summary>
    public const string HeadshotSize = "48x48";

    /// <summary>The endpoint's ceiling, and well above the 256 accounts RoRoRo's history limit allows.</summary>
    public const int HeadshotBatchLimit = 100;

    /// <summary>What a cached headshot is called in the icon cache, so it can never collide with a recipe's icon (plan A24).</summary>
    public const string AvatarPrefix = "avatar-";

    public static readonly TimeSpan CacheFor = TimeSpan.FromDays(7);

    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly string _cacheDirectory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _requestTimeout;

    /// <param name="requestTimeout">
    /// Bounds one whole request — headers and body both — the same way <c>HttpRecipeTransport</c>
    /// bounds a recipe fetch. Defaults to 30 seconds; a test may pass something shorter.
    /// </param>
    public IconClient(HttpMessageHandler handler, string cacheDirectory, Func<DateTimeOffset> clock, TimeSpan? requestTimeout = null)
    {
        _http = new HttpClient(handler);
        _cacheDirectory = cacheDirectory;
        _clock = clock;
        _requestTimeout = requestTimeout ?? DefaultRequestTimeout;
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
        if (KeyOf(iconText, recipeHosts) is not { } key) return null;

        var file = CachePath(key.Name);
        if (IsFresh(file)) return file;

        if (key.Address is { } address) return await DownloadAsync(address, file, cancellationToken).ConfigureAwait(false);

        var imageUrl = await ThumbnailUrlAsync(key.AssetId, cancellationToken).ConfigureAwait(false);
        return imageUrl is not null && IsPictureHost(imageUrl)
            ? await DownloadAsync(imageUrl, file, cancellationToken).ConfigureAwait(false)
            : null;
    }

    /// <summary>
    /// The picture already in the cache for <paramref name="iconText"/>, however old, or null (backlog V3-S.6). Never a request:
    /// it is how the window draws last session's picture before any read. The same rules as <see cref="ResolveAsync"/> decide
    /// what counts as an icon, so a text that would never be fetched never finds a file either.
    /// </summary>
    public string? CachedFile(string? iconText, IReadOnlySet<string> recipeHosts)
    {
        if (KeyOf(iconText, recipeHosts) is not { } key) return null;

        try
        {
            var file = CachePath(key.Name);
            return File.Exists(file) ? file : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>What an icon text is: an asset id, or an https address on one of the recipe's own hosts. Its cache name follows.</summary>
    private sealed record IconKey(string Name, long AssetId, Uri? Address);

    /// <summary>Null for anything that is not an asset id or an https address on one of <paramref name="recipeHosts"/>.</summary>
    private static IconKey? KeyOf(string? iconText, IReadOnlySet<string> recipeHosts)
    {
        var text = iconText?.Trim() ?? "";

        if (text.StartsWith(AssetScheme, StringComparison.OrdinalIgnoreCase)
            && long.TryParse(text[AssetScheme.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var assetId)
            && assetId > 0)
        {
            return new IconKey(assetId.ToString(CultureInfo.InvariantCulture), assetId, null);
        }

        if (Uri.TryCreate(text, UriKind.Absolute, out var address)
            && address.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(address.UserInfo)
            && recipeHosts.Contains(address.Host.ToLowerInvariant()))
        {
            return new IconKey("url-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(address.AbsoluteUri)))[..32], 0, address);
        }

        return null;
    }

    /// <summary>
    /// The cached headshot of each of <paramref name="userIds"/>, fetching the ones the cache has none younger than
    /// <see cref="CacheFor"/> for. Only your own accounts' ids are ever passed in (plan A22). A user Roblox has no
    /// finished picture for, and one whose picture is off Roblox's picture domain, is simply left out. Never throws for
    /// anything but a stop the caller asked for: a failure costs the pictures, and the rows keep their names.
    /// </summary>
    public async Task<IReadOnlyDictionary<long, string>> HeadshotsAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken)
    {
        var files = new Dictionary<long, string>();
        var wanted = userIds.Where(id => id > 0).Distinct().ToList();

        for (var offset = 0; offset < wanted.Count; offset += HeadshotBatchLimit)
        {
            var batch = wanted.Skip(offset).Take(HeadshotBatchLimit).ToList();
            var asked = batch.ToHashSet();

            var missing = new List<long>();
            foreach (var id in batch)
            {
                var cached = CachePath(AvatarKey(id));
                if (IsFresh(cached)) files[id] = cached;
                else missing.Add(id);
            }

            if (missing.Count == 0) continue;

            foreach (var (userId, url) in await HeadshotUrlsAsync(missing, cancellationToken).ConfigureAwait(false))
            {
                // Only what this batch asked for, so an answer carrying anyone else cannot reach the cache.
                if (!asked.Contains(userId) || !IsPictureHost(url)) continue;

                if (await DownloadAsync(url, CachePath(AvatarKey(userId)), cancellationToken).ConfigureAwait(false) is { } file)
                {
                    files[userId] = file;
                }
            }
        }

        return files;
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
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_requestTimeout);

            using var request = Get(address);
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
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
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_requestTimeout);

            using var request = Get(url);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            if (response.Content.Headers.ContentLength is > MaxBytes) return null;

            // ResponseHeadersRead means HttpClient stops enforcing any timeout once headers land, so
            // the same linked token that bounded SendAsync must bound every read of the body too —
            // otherwise a body that never finishes holds this open forever.
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxBytes) return null;
            }

            var bytes = buffer.ToArray();
            if (!IsPng(bytes) && !IsJpeg(bytes)) return null;

            Directory.CreateDirectory(_cacheDirectory);
            var partial = file + ".partial";
            await File.WriteAllBytesAsync(partial, bytes, timeout.Token).ConfigureAwait(false);
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

    private static string AvatarKey(long userId) => AvatarPrefix + userId.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// One batch ask for headshots. Only a row Roblox calls <c>Completed</c> has a picture to fetch: <c>Pending</c>,
    /// <c>Blocked</c> and the rest mean no icon this time, and no reason to spoil the batch for everyone else.
    /// </summary>
    private async Task<IReadOnlyList<(long UserId, Uri Url)>> HeadshotUrlsAsync(IReadOnlyList<long> userIds, CancellationToken cancellationToken)
    {
        var ids = string.Join(',', userIds.Select(id => id.ToString(CultureInfo.InvariantCulture)));
        var address = new Uri(
            $"https://{ThumbnailsHost}/v1/users/avatar-headshot?userIds={ids}&size={HeadshotSize}&format=Png&isCircular=false");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_requestTimeout);

            using var request = Get(address);
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return [];

            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);

            if (!JsonNav.TryGet(document.RootElement, "data", out var data) || data.ValueKind != JsonValueKind.Array) return [];

            var found = new List<(long, Uri)>();
            foreach (var row in data.EnumerateArray())
            {
                if (!JsonNav.TryGet(row, "state", out var state) || state.ValueKind != JsonValueKind.String
                    || !string.Equals(state.GetString(), "Completed", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!JsonNav.TryGet(row, "targetId", out var target) || !JsonNav.TryUserId(target, out var userId)) continue;
                if (!JsonNav.TryGet(row, "imageUrl", out var imageUrl) || imageUrl.ValueKind != JsonValueKind.String) continue;
                if (!Uri.TryCreate(imageUrl.GetString(), UriKind.Absolute, out var url)) continue;

                found.Add((userId, url));
            }

            return found;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static bool IsPng(byte[] bytes) =>
        bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

    private static bool IsJpeg(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
}
