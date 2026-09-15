namespace Labs626.UrScore.Source;

/// <summary>The seam the app and the tests use for headshots, so neither needs the network. <see cref="IconClient"/> implements it.</summary>
public interface IAvatarSource
{
    Task<IReadOnlyDictionary<long, string>> HeadshotsAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken);
}

/// <summary>
/// The picture beside each of your own accounts, for as long as the app runs (plan A21-A23).
/// <para>
/// Every id it is given is one of YOUR accounts: <c>AppServices</c> asks with RoRoRo's list and nothing else, and only
/// what an ask asked for is ever kept. Nobody else's id reaches this class, so nobody else's picture can reach the disk.
/// </para>
/// <para>
/// An id is asked about ONCE a session, whatever comes back. A board redraws on every read; re-asking each time would be
/// twenty requests an hour against Roblox for a picture that doesn't move. A picture Roblox hasn't rendered yet, and one
/// that couldn't be fetched, cost the picture until the next start — never a retry loop under a redraw.
/// </para>
/// <para>
/// Never throws except for a stop the caller asked for, and those ids are left to be asked again. Locked, because
/// <c>AppServices</c> may ask from a fetching thread while the UI thread reads <see cref="Files"/>.
/// </para>
/// </summary>
public sealed class AvatarBook(IAvatarSource source)
{
    private readonly object _gate = new();
    private readonly Dictionary<long, string> _files = [];
    private readonly HashSet<long> _asked = [];

    /// <summary>A copy, safe to hand to a view model that outlives the next ask.</summary>
    public IReadOnlyDictionary<long, string> Files
    {
        get
        {
            lock (_gate) return new Dictionary<long, string>(_files);
        }
    }

    public string? FileFor(long userId)
    {
        lock (_gate) return _files.GetValueOrDefault(userId);
    }

    /// <summary>
    /// Asks for the pictures of the ids not asked about yet this session, and keeps what came back for those ids.
    /// True when the map changed, so the caller can redraw.
    /// </summary>
    public async Task<bool> AskAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken)
    {
        List<long> missing;
        lock (_gate)
        {
            missing = [.. userIds.Where(id => id > 0 && _asked.Add(id))];
        }

        if (missing.Count == 0) return false;

        IReadOnlyDictionary<long, string> found;
        try
        {
            found = await source.HeadshotsAsync(missing, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A stop the user asked for is not an answer: these ids are still unknown.
            lock (_gate)
            {
                foreach (var id in missing) _asked.Remove(id);
            }

            throw;
        }
        catch (Exception)
        {
            return false;
        }

        var wanted = missing.ToHashSet();
        lock (_gate)
        {
            var changed = false;
            foreach (var (id, file) in found)
            {
                if (!wanted.Contains(id)) continue;
                if (string.Equals(_files.GetValueOrDefault(id), file, StringComparison.Ordinal)) continue;

                _files[id] = file;
                changed = true;
            }

            return changed;
        }
    }

    /// <summary>Your accounts changed: an id that is no longer yours is forgotten, and asked about again if it comes back.</summary>
    public void Keep(IReadOnlySet<long> yours)
    {
        lock (_gate)
        {
            foreach (var gone in _files.Keys.Where(id => !yours.Contains(id)).ToList()) _files.Remove(gone);
            foreach (var gone in _asked.Where(id => !yours.Contains(id)).ToList()) _asked.Remove(gone);
        }
    }
}
