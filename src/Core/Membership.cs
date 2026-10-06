using System.IO;
using System.Text.Json;

namespace Labs626.UrScore.Core;

/// <summary>Which of your own Roblox ids one clan's members list held, and when it was read.</summary>
public sealed record ClanMembers(IReadOnlySet<long> UserIds, DateTimeOffset ReadAt);

/// <summary>
/// Who is in each clan, from its members list rather than from who scored this battle (backlog V3-S.20): per source id, the
/// Roblox ids of YOUR accounts the list held. The board, Setup's clan rows and Setup › Your accounts group by it, and fall
/// back to battle contributions for a clan whose list hasn't been read.
/// <para>
/// Kept in <c>membership.json</c> so the board groups correctly on open, before the first refresh. Only your own ids are
/// kept, here as well as in <see cref="Recipes.MemberLists"/>: <see cref="Set"/> drops anything not in the ids it is told
/// are yours, so a caller that got the intersection wrong still can't write a stranger's id (README "What leaves your
/// machine"). A file that can't be read is no membership, and the next read writes over it.
/// </para>
/// </summary>
public sealed class Membership
{
    /// <summary>One members read per clan per this long, at most (one GET each); a failed try waits as long as a read.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, ClanMembers> _clans;

    /// <summary>When each source was last tried, read or not; this session only, so a restart asks again.</summary>
    private readonly Dictionary<string, DateTimeOffset> _tried = new(StringComparer.Ordinal);

    public Membership(string path)
    {
        _path = path;
        _clans = Load(path);
    }

    /// <summary>Each read clan's members list, as of its read.</summary>
    public IReadOnlyDictionary<string, ClanMembers> All
    {
        get
        {
            lock (_gate) return new Dictionary<string, ClanMembers>(_clans, StringComparer.Ordinal);
        }
    }

    /// <summary>Just the ids, per source id: what the board and Setup group by.</summary>
    public IReadOnlyDictionary<string, IReadOnlySet<long>> Ids
    {
        get
        {
            lock (_gate) return _clans.ToDictionary(kv => kv.Key, kv => kv.Value.UserIds, StringComparer.Ordinal);
        }
    }

    /// <summary>True with nothing kept or tried for the source, or once <see cref="Every"/> has passed since the later of the two.</summary>
    public bool IsDue(string sourceId, DateTimeOffset now)
    {
        lock (_gate)
        {
            var last = _clans.TryGetValue(sourceId, out var kept) ? kept.ReadAt : (DateTimeOffset?)null;
            if (_tried.TryGetValue(sourceId, out var tried) && (last is null || tried > last)) last = tried;
            return last is null || now - last.Value >= Every;
        }
    }

    /// <summary>A read was asked for at <paramref name="at"/>, whatever came of it: the cadence counts the try, not the answer.</summary>
    public void Tried(string sourceId, DateTimeOffset at)
    {
        lock (_gate) _tried[sourceId] = at;
    }

    /// <summary>
    /// Keeps what a read found for <paramref name="sourceId"/> (your ids only), forgets sources not in <paramref name="keep"/>, and
    /// writes the file. A write that fails throws after memory is updated, so this session groups by the read either way.
    /// </summary>
    public void Set(string sourceId, IEnumerable<long> userIds, DateTimeOffset readAt, IReadOnlySet<long> yours, IEnumerable<string> keep)
    {
        Dictionary<string, ClanMembers> next;
        lock (_gate)
        {
            var kept = keep.ToHashSet(StringComparer.Ordinal);
            next = _clans.Where(kv => kept.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            next[sourceId] = new ClanMembers(userIds.Where(yours.Contains).ToHashSet(), readAt);
            _clans = next;
            _tried[sourceId] = readAt;
        }

        Save(next);
    }

    private void Save(IReadOnlyDictionary<string, ClanMembers> clans)
    {
        var file = new MembershipFile(clans.ToDictionary(
            kv => kv.Key, kv => new MembershipEntry([.. kv.Value.UserIds.Order()], kv.Value.ReadAt), StringComparer.Ordinal));
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(file, Options));
        File.Move(temp, _path, overwrite: true);
    }

    private static Dictionary<string, ClanMembers> Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new Dictionary<string, ClanMembers>(StringComparer.Ordinal);

            var file = JsonSerializer.Deserialize<MembershipFile>(File.ReadAllText(path), Options);
            return (file?.Clans ?? new Dictionary<string, MembershipEntry>())
                .Where(kv => kv.Value is not null)
                .ToDictionary(kv => kv.Key, kv => new ClanMembers((kv.Value.UserIds ?? []).ToHashSet(), kv.Value.ReadAt), StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return new Dictionary<string, ClanMembers>(StringComparer.Ordinal);
        }
    }

    private sealed record MembershipFile(Dictionary<string, MembershipEntry>? Clans);

    private sealed record MembershipEntry(long[]? UserIds, DateTimeOffset ReadAt);
}
