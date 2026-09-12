using Labs626.UrScore.Source;

namespace Labs626.UrScore.Core;

/// <summary>
/// What the plugin is doing, in terms a user can act on.
/// <para>
/// Nine values instead of a shared "error" (round 3 added <see cref="ClanNotFound"/> — a wrong
/// clan name used to collapse into <see cref="SourceUnreachable"/>, whose own doc promises waiting
/// as the remedy, which is false for a typo). Five different causes otherwise look identical from
/// the outside: no clan by that name, a clan that does not exist at all, a clan with no battle
/// running, a battle whose response we could not read, and contributors none of whom are yours.
/// One of those is normal and four need different things done about them.
/// </para>
/// </summary>
public enum WatchState
{
    /// <summary>No clan name configured. Nothing is polled.</summary>
    Idle,

    /// <summary>The endpoint could not be reached, or refused us for a reason other than the clan
    /// not existing. Waiting is the remedy.</summary>
    SourceUnreachable,

    /// <summary>
    /// The vendor's own response says this clan name does not exist (a 400 or 404 on the clan
    /// call, specifically). Distinct from <see cref="SourceUnreachable"/> on purpose: waiting will
    /// never fix a typo, so the remedy the two states imply must not be the same one.
    /// </summary>
    ClanNotFound,

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
/// <para>
/// <see cref="Contributions"/> and <see cref="Standing"/> carry the WHOLE clan response — every
/// contributor, not just the user's own — and the clan's place and total, for the dashboard (spec
/// §6.1). <see cref="ScoreWatch"/> already has both in hand from the one contributions fetch it
/// makes each cycle; this is that data riding along rather than the window re-reading a diagnostics
/// file to get a second copy of it. Null/empty when no battle was fetched this cycle (mirrors
/// <see cref="Battle"/>'s own null-on-<see cref="WatchState.NoBattle"/> contract).
/// </para>
/// </summary>
public sealed record WatchSnapshot(
    WatchState State,
    string? Detail,
    IReadOnlyList<AccountLine> Accounts,
    IReadOnlyList<HostAccount> Unresolved,
    int ContributorsSeen,
    string? Battle = null,
    IReadOnlyList<Contribution>? Contributions = null,
    ClanStanding? Standing = null);
