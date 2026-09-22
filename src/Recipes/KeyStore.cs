using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Labs626.UrScore.Core;

namespace Labs626.UrScore.Recipes;

/// <summary>A user's key for one source, bound to the one host it may be sent to (spec §7.2).</summary>
public sealed record SavedKey(string Id, string Host, string Value);

/// <summary>The seam the engine and the import review are tested against.</summary>
public interface IKeyStore
{
    SavedKey? Find(string keyId);

    void Save(string keyId, string host, string value);

    bool Remove(string keyId);

    /// <summary>Every saved value, for <see cref="Redactor"/>. Never shown.</summary>
    IReadOnlyCollection<string> Values();
}

/// <summary>
/// Keys encrypted with Windows DPAPI for the current user, the same protection RoRoRo gives its
/// Discord and phone settings (spec §7.1). Stored by key id, each bound on first save to one host
/// and never rebound silently (plan Ruling 2).
/// </summary>
public sealed class KeyStore(string path) : IKeyStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("626labs.ur-score.keys.v1");

    private readonly Lock _gate = new();

    public static string DefaultPath => AppPaths.Default.Keys;

    public SavedKey? Find(string keyId)
    {
        lock (_gate)
        {
            return Load().FirstOrDefault(k => string.Equals(k.Id, keyId, StringComparison.Ordinal));
        }
    }

    public void Save(string keyId, string host, string value)
    {
        value = value.Trim();
        if (value.Length < Redactor.MinimumLength)
        {
            throw new ArgumentException(
                $"That key is {value.Length} characters. A real API key is at least {Redactor.MinimumLength}.",
                nameof(value));
        }

        host = host.ToLowerInvariant();

        lock (_gate)
        {
            var keys = Load();
            var existing = keys.FirstOrDefault(k => string.Equals(k.Id, keyId, StringComparison.Ordinal));
            if (existing is not null && !string.Equals(existing.Host, host, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Your '{keyId}' key is saved for {existing.Host}. Remove it before saving one for {host}.");
            }

            keys.RemoveAll(k => string.Equals(k.Id, keyId, StringComparison.Ordinal));
            keys.Add(new SavedKey(keyId, host, value));
            Write(keys);
        }
    }

    public bool Remove(string keyId)
    {
        lock (_gate)
        {
            var keys = Load();
            if (keys.RemoveAll(k => string.Equals(k.Id, keyId, StringComparison.Ordinal)) == 0) return false;
            Write(keys);
            return true;
        }
    }

    public IReadOnlyCollection<string> Values()
    {
        lock (_gate)
        {
            return [.. Load().Select(k => k.Value)];
        }
    }

    private List<SavedKey> Load()
    {
        try
        {
            if (!File.Exists(path)) return [];
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<List<SavedKey>>(plain) ?? [];
        }
        catch (Exception)
        {
            // Another Windows user's file, or corruption. No keys: recipes then report KeyMissing and
            // name the key, which is a remedy. A crash is not.
            return [];
        }
    }

    private void Write(List<SavedKey> keys)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var cipher = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(keys), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, cipher);
    }
}
