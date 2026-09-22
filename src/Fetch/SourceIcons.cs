using System.IO;
using System.Text.Json;

namespace Labs626.UrScore.Fetch;

/// <summary>The seam the app and the tests use for a source's icon, so neither needs the network. <see cref="IconClient"/> implements it.</summary>
public interface IIconSource
{
    /// <summary>The picture for an icon text, fetched when the cache has none fresh; null for anything refused or failed.</summary>
    Task<string?> ResolveAsync(string? iconText, IReadOnlySet<string> recipeHosts, CancellationToken cancellationToken);

    /// <summary>The picture already on disk for an icon text, however old, or null. Never a request.</summary>
    string? CachedFile(string? iconText, IReadOnlySet<string> recipeHosts);
}

/// <summary>
/// Which picture belongs to which source (backlog V3-S.7, V3-S.6, S1-14.1).
/// <para>
/// A picture is kept per SOURCE, never per recipe: three clans read on one recipe are three pictures, so the main clan's is
/// never replaced by whichever clan was read last. Which source's picture the window shows is decided elsewhere
/// (<c>IconChoice</c>), from the sources alone.
/// </para>
/// <para>
/// Each source's icon text is kept in <see cref="FileName"/> beside the pictures, so the next start draws each picture from
/// the cache before any read and without a request. The text is what the source's own response named (a picture id, or an
/// address on a host the recipe already contacts): a public picture of a clan, never a player's id. Only a text whose picture
/// was actually found is kept.
/// </para>
/// <para>
/// An icon text is asked about once a session per source. Never throws except for a stop the caller asked for, and then the
/// text is left to be asked again; a file that can't be read or written costs the pictures at the next start and nothing else.
/// Locked, because a fetch finishes off the UI thread while the UI thread reads <see cref="Files"/>.
/// </para>
/// </summary>
public sealed class SourceIcons(IIconSource pictures, string savedFile)
{
    /// <summary>Kept in the icon cache, so clearing the cache clears it too.</summary>
    public const string FileName = "source-icons.json";

    private readonly object _gate = new();
    private readonly object _saving = new();

    /// <summary>Each source's picture file.</summary>
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

    /// <summary>The icon text each of those pictures is, and what the saved file holds.</summary>
    private readonly Dictionary<string, string> _texts = new(StringComparer.Ordinal);

    /// <summary>The text each source was last asked about this session: a restored picture has not been asked about yet.</summary>
    private readonly Dictionary<string, string> _asked = new(StringComparer.Ordinal);

    /// <summary>A copy, safe to hand to a view model that outlives the next change.</summary>
    public IReadOnlyDictionary<string, string> Files
    {
        get
        {
            lock (_gate) return new Dictionary<string, string>(_files, StringComparer.Ordinal);
        }
    }

    public string? FileFor(string sourceId)
    {
        lock (_gate) return _files.GetValueOrDefault(sourceId);
    }

    /// <summary>
    /// At start: each saved source's picture, from the cache only (V3-S.6). <paramref name="hostsFor"/> gives a source's recipe
    /// hosts, or null for a source that is gone or whose recipe names no icon, and those come back with nothing. A picture no
    /// longer on disk comes back with nothing too, and the first read fetches it again. True when any picture came back.
    /// </summary>
    public bool Restore(Func<string, IReadOnlySet<string>?> hostsFor)
    {
        var restored = false;
        foreach (var (sourceId, text) in LoadSaved())
        {
            if (hostsFor(sourceId) is not { } hosts || Cached(text, hosts) is not { } file) continue;

            lock (_gate)
            {
                _files[sourceId] = file;
                _texts[sourceId] = text;
            }

            restored = true;
        }

        return restored;
    }

    /// <summary>
    /// A read of <paramref name="sourceId"/> named <paramref name="iconText"/>: that source's picture, and nobody else's. A
    /// text already asked about this session asks nothing. When the picture can't be fetched, one already on disk for that
    /// same text still stands; with none, the source has no picture. True when the source's picture changed, so the caller
    /// can redraw.
    /// </summary>
    public async Task<bool> ApplyAsync(string sourceId, string iconText, IReadOnlySet<string> recipeHosts, CancellationToken cancellationToken)
    {
        var text = iconText.Trim();
        lock (_gate)
        {
            if (string.Equals(_asked.GetValueOrDefault(sourceId), text, StringComparison.Ordinal)) return false;
            _asked[sourceId] = text;
        }

        string? file;
        var failed = false;
        try
        {
            file = await pictures.ResolveAsync(text, recipeHosts, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A stop the user asked for is not an answer: this text is still unknown.
            lock (_gate)
            {
                if (string.Equals(_asked.GetValueOrDefault(sourceId), text, StringComparison.Ordinal)) _asked.Remove(sourceId);
            }

            throw;
        }
        catch (Exception)
        {
            // Not an answer either. "No picture for this text" is an answer and is remembered so the same text
            // is not asked about every read; a fetch that THREW has said nothing about the text, and remembering
            // it as asked cost a source its picture for the whole session on one network blip (S1-14.7). The ask
            // is forgotten at the end, once this attempt has fallen back to whatever the cache holds.
            file = null;
            failed = true;
        }

        // Offline, or older than the cache counts as fresh: the picture this very text already has is still this source's.
        file ??= Cached(text, recipeHosts);

        bool changed, textChanged;
        lock (_gate)
        {
            // A newer text arrived while this one was fetching, or the source was forgotten: this answer is no longer its.
            if (!string.Equals(_asked.GetValueOrDefault(sourceId), text, StringComparison.Ordinal)) return false;

            var before = _files.GetValueOrDefault(sourceId);
            var textBefore = _texts.GetValueOrDefault(sourceId);
            if (file is null)
            {
                _files.Remove(sourceId);
                _texts.Remove(sourceId);
            }
            else
            {
                _files[sourceId] = file;
                _texts[sourceId] = text;
            }

            changed = !string.Equals(before, file, StringComparison.Ordinal);
            textChanged = !string.Equals(textBefore, _texts.GetValueOrDefault(sourceId), StringComparison.Ordinal);

            // A fetch that threw leaves the text unknown, so the next read asks again — after this attempt has
            // applied whatever the cache held, which is why the forget is here and not in the catch (S1-14.7).
            if (failed) _asked.Remove(sourceId);
        }

        if (textChanged) Save();
        return changed;
    }

    /// <summary>
    /// Only <paramref name="sourceIds"/> may keep a picture: every other source is gone or its recipe no longer names an icon
    /// (S1-14.1). It loses its picture now and at the next start, and is asked about again if it comes back. True when anything
    /// was forgotten.
    /// </summary>
    public bool Keep(IReadOnlySet<string> sourceIds)
    {
        bool forgot;
        lock (_gate)
        {
            var gone = _files.Keys.Concat(_texts.Keys).Concat(_asked.Keys).Where(id => !sourceIds.Contains(id)).Distinct().ToList();
            forgot = gone.Any(id => _files.ContainsKey(id) || _texts.ContainsKey(id));
            foreach (var id in gone)
            {
                _files.Remove(id);
                _texts.Remove(id);
                _asked.Remove(id);
            }
        }

        if (forgot) Save();
        return forgot;
    }

    private string? Cached(string text, IReadOnlySet<string> hosts)
    {
        try
        {
            return pictures.CachedFile(text, hosts);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private IReadOnlyList<(string SourceId, string Text)> LoadSaved()
    {
        try
        {
            if (!File.Exists(savedFile)) return [];

            using var document = JsonDocument.Parse(File.ReadAllText(savedFile));
            if (document.RootElement.ValueKind != JsonValueKind.Object) return [];

            return [.. document.RootElement.EnumerateObject()
                .Where(p => p.Name.Trim().Length > 0 && p.Value.ValueKind == JsonValueKind.String && p.Value.GetString()!.Trim().Length > 0)
                .Select(p => (p.Name, p.Value.GetString()!.Trim()))];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Whole or not at all. Serialized, and each write takes the newest map, so the last write on disk is the newest.</summary>
    private void Save()
    {
        lock (_saving)
        {
            try
            {
                Dictionary<string, string> texts;
                lock (_gate) texts = new Dictionary<string, string>(_texts, StringComparer.Ordinal);

                Directory.CreateDirectory(Path.GetDirectoryName(savedFile)!);
                var partial = savedFile + ".partial";
                File.WriteAllText(partial, JsonSerializer.Serialize(texts));
                File.Move(partial, savedFile, overwrite: true);
            }
            catch (Exception)
            {
                // The pictures still show this session; the next start fetches them on its first read instead.
            }
        }
    }
}
