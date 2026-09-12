namespace Labs626.UrScore.Core;

/// <summary>
/// A saved account, as the host hands it over.
/// <para>
/// <see cref="DisplayName"/> is for showing the user which of their accounts is which, and for
/// NOTHING else. The host masks it while streamer mode is on, so matching on it would pass every
/// test and fail at the only moment that matters: while someone streams a clan battle.
/// </para>
/// </summary>
public sealed record HostAccount(Guid AccountId, long RobloxUserId, string DisplayName);

/// <summary>The join between the clan's contributors and the user's own accounts.</summary>
public static class AccountMap
{
    /// <summary>
    /// Roblox user id to RoRoRo account id — the only join the contract offers, and the only one
    /// that survives streamer mode.
    /// </summary>
    public static IReadOnlyDictionary<long, Guid> Build(IEnumerable<HostAccount> accounts)
    {
        var map = new Dictionary<long, Guid>();

        foreach (var account in accounts)
        {
            // 0 is the contract's documented "not yet resolved", not an id.
            if (account.RobloxUserId == 0) continue;

            // First wins, rather than throwing on a duplicate the host permits.
            map.TryAdd(account.RobloxUserId, account.AccountId);
        }

        return map;
    }

    /// <summary>
    /// The accounts that cannot be watched yet, so the window can name them. Silence about these
    /// is the failure a user would never work out on their own.
    /// </summary>
    public static IReadOnlyList<HostAccount> Unresolved(IEnumerable<HostAccount> accounts) =>
        [.. accounts.Where(a => a.RobloxUserId == 0)];
}
