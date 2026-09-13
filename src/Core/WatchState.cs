namespace Labs626.UrScore.Core;

/// <summary>
/// What Ur Score is doing, in terms a user can act on. Separate states instead of a shared "error",
/// because causes that look identical from outside need different things done: waiting fixes an
/// unreachable source and a rate limit, never a typo, a missing key or a changed shape.
/// </summary>
public enum WatchState
{
    /// <summary>The source could not be reached, or answered with a server error. Waiting is the remedy.</summary>
    SourceUnreachable,

    /// <summary>The response was not a shape the recipe describes. Someone must look.</summary>
    ShapeNotUnderstood,

    /// <summary>Rows came back, and none of them are the user's accounts.</summary>
    NoMatches,

    /// <summary>Working. At least one account matched and its value went to RoRoRo.</summary>
    Reporting,

    /// <summary>RoRoRo is not running. Reading continues; reporting is held.</summary>
    HostDown,

    /// <summary>RoRoRo refused us: a declined capability, or a rejected handshake.</summary>
    Rejected,

    /// <summary>The recipe has an input the user has not filled in. Nothing is polled.</summary>
    NeedsInput,

    /// <summary>The source says a value the user entered matches nothing. Waiting will not fix it.</summary>
    InputNotFound,

    /// <summary>The source says there is nothing to read right now, such as no clan battle running. Normal.</summary>
    SourceIdle,

    /// <summary>The source asked us to slow down. The next poll tries again.</summary>
    RateLimited,

    /// <summary>The source wants a signed-in session, which recipes never have. Held until the recipe or inputs change.</summary>
    SignInRequired,

    /// <summary>The recipe needs a key that is not saved, or that is saved for a different host.</summary>
    KeyMissing,

    /// <summary>The source refused the saved key. Held until a key changes.</summary>
    KeyRejected,

    /// <summary>Your accounts were read and are on the board, and no stat is set to send. Nothing went to RoRoRo.</summary>
    Showing,
}

/// <summary>One of the user's accounts, as the window lists it: the last value sent for each stat key, and when.</summary>
public sealed record AccountLine(
    string DisplayName, Guid AccountId, IReadOnlyDictionary<string, double> LastValues, DateTimeOffset? LastReportedUtc);
