namespace Labs626.UrScore.Core;

/// <summary>
/// What the plugin is doing, in terms a user can act on.
/// <para>
/// Eight values instead of a shared "error", because four different causes otherwise look
/// identical from the outside: no clan by that name, a clan with no battle running, a battle whose
/// response we could not read, and contributors none of whom are yours. One of those is normal and
/// three need different things done about them.
/// </para>
/// </summary>
public enum WatchState
{
    /// <summary>No clan name configured. Nothing is polled.</summary>
    Idle,

    /// <summary>The endpoint could not be reached, or refused us. Waiting is the remedy.</summary>
    SourceUnreachable,

    /// <summary>No battle is live. Normal, frequent, and explicitly not an error.</summary>
    NoBattle,

    /// <summary>A battle is live and the response was not a shape we understood. Someone must look.</summary>
    ShapeNotUnderstood,

    /// <summary>Contributors came back, and none of them are the user's accounts.</summary>
    NoMatches,

    /// <summary>Working. At least one account matched and its number went to RoRoRo.</summary>
    Reporting,

    /// <summary>RoRoRo is not running. Polling continues; reporting is held.</summary>
    HostDown,

    /// <summary>RoRoRo refused us — a revoked capability, or a rejected handshake.</summary>
    Rejected,
}

/// <summary>One of the user's accounts, as the window lists it.</summary>
public sealed record AccountLine(
    string DisplayName, Guid AccountId, double? LastValue, DateTimeOffset? LastReportedUtc);

/// <summary>
/// Everything the window renders from one cycle.
/// <para>
/// <see cref="Unresolved"/> is data rather than a state, deliberately: accounts with no Roblox user
/// id yet coexist with <see cref="WatchState.Reporting"/>, so three can report while a fourth
/// waits. Making it a state would hide the reporting.
/// </para>
/// </summary>
public sealed record WatchSnapshot(
    WatchState State,
    string? Detail,
    IReadOnlyList<AccountLine> Accounts,
    IReadOnlyList<HostAccount> Unresolved,
    int ContributorsSeen,
    string? Battle = null);
