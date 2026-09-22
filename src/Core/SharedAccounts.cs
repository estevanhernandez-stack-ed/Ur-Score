using System.IO;
using System.Text.Json;
using Grpc.Core;
using Labs626.UrScore.Host;

namespace Labs626.UrScore.Core;

/// <summary>
/// <c>accounts.json</c>: the user's own RoRoRo accounts as last listed, so reading and recording go on while
/// RoRoRo is closed (score book spec §5.5). Only the user's accounts; never a key or cookie.
/// </summary>
public sealed class AccountsCache(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static string DefaultPath => AppPaths.Default.Accounts;

    public IReadOnlyList<HostAccount> Load()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<List<HostAccount>>(File.ReadAllText(path), Options) ?? [] : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Save(IReadOnlyList<HostAccount> accounts)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(accounts, Options));
        File.Move(temp, path, overwrite: true);
    }

    public DateTimeOffset? SavedAt() => File.Exists(path) ? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero) : null;
}

/// <summary>What one account fetch found. <see cref="ListedAt"/> is when RoRoRo last listed these accounts.</summary>
public sealed record AccountList(IReadOnlyList<HostAccount> Accounts, bool HostUp, bool FromCache, bool Denied, DateTimeOffset? ListedAt);

/// <summary>
/// One account fetch shared by every watch within <see cref="Window"/>, so five sources are one question
/// to RoRoRo, not five (score book spec §4.2).
/// </summary>
public sealed class SharedAccounts(IHostClient host, AccountsCache cache, TimeProvider time)
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _one = new(1, 1);

    private DateTimeOffset _fetchedAt;

    public AccountList? Last { get; private set; }

    /// <summary>
    /// Raised inside <see cref="GetAsync"/> each time a new list is taken (not for a reuse within the window),
    /// after <see cref="Last"/> is set and before the caller gets it, on the caller's thread. So an allow list
    /// can follow an account RoRoRo just listed before the read that fetched it sends (F8).
    /// </summary>
    public event Action<AccountList>? Listed;

    public async Task<AccountList> GetAsync(CancellationToken cancellationToken)
    {
        await _one.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = time.GetUtcNow();
            if (Last is not null && now - _fetchedAt < Window) return Last;

            AccountList list;
            if (await host.IsReachableAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    var accounts = await host.GetAccountsAsync(cancellationToken).ConfigureAwait(false);
                    TrySave(accounts);
                    list = new AccountList(accounts, HostUp: true, FromCache: false, Denied: false, ListedAt: now);
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.PermissionDenied)
                {
                    list = new AccountList([], HostUp: true, FromCache: false, Denied: true, ListedAt: Last?.ListedAt);
                }
                catch (Exception ex) when (ex is RpcException or IOException && !cancellationToken.IsCancellationRequested)
                {
                    // RoRoRo answered the probe, then the list failed (unavailable, a deadline, a broken pipe): to a
                    // read that is RoRoRo not answering, so the saved list stands in and reading and recording go on
                    // (score book spec §5.4, §5.5). Only the caller's own cancellation ends the fetch.
                    //
                    // The transport's failures and no other: the gRPC call's RpcException and the pipe's IOException.
                    // This caught everything, so a bug in the client was answered with the saved accounts and looked
                    // exactly like RoRoRo being closed (S1-F.9). Anything else leaves as what it is, reaches the
                    // read's own "last read failed" line, and is named there by type.
                    list = FromSaved();
                }
            }
            else
            {
                list = FromSaved();
            }

            Last = list;
            _fetchedAt = now;
            RaiseListed(list);
            return list;
        }
        finally
        {
            _one.Release();
        }
    }

    /// <summary>The accounts RoRoRo last listed, from <c>accounts.json</c>, for when RoRoRo isn't answering.</summary>
    private AccountList FromSaved() =>
        new(cache.Load(), HostUp: false, FromCache: true, Denied: false, ListedAt: Last?.ListedAt ?? cache.SavedAt());

    /// <summary>Each subscriber on its own: one that throws costs neither the others nor the fetch.</summary>
    private void RaiseListed(AccountList list)
    {
        if (Listed is not { } listed) return;

        foreach (var handler in listed.GetInvocationList().Cast<Action<AccountList>>())
        {
            try
            {
                handler(list);
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>A cache that can't be written costs only the fallback, never the read.</summary>
    private void TrySave(IReadOnlyList<HostAccount> accounts)
    {
        try
        {
            cache.Save(accounts);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
