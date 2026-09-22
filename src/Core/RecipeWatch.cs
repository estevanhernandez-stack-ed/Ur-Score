using System.Security.Cryptography;
using System.Text;
using Grpc.Core;
using Labs626.UrScore.Board;
using Labs626.UrScore.Book;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Core;

/// <summary>
/// Everything the window renders from one cycle. <see cref="Rows"/> carries every row the recipe
/// read, the user's own and everyone else's, for the leaderboard; only the user's own are ever
/// reported, through <see cref="ReportPolicy"/>, or kept, through <see cref="LineBuilder"/>.
/// <see cref="CellMisses"/> holds only the user's own accounts, so another member's id never travels
/// further than the leaderboard.
/// </summary>
public sealed record RecipeSnapshot(
    WatchState State,
    string? Detail,
    IReadOnlyList<AccountLine> Accounts,
    IReadOnlyList<HostAccount> Unresolved,
    int RowsSeen,
    string? Context = null,
    IReadOnlyList<RecipeRow>? Rows = null,
    IReadOnlyList<HeadlineValue>? Headline = null)
{
    public IReadOnlyDictionary<long, string> Unavailable { get; init; } = new Dictionary<long, string>();

    public IReadOnlyDictionary<string, string> StatMisses { get; init; } = new Dictionary<string, string>();

    public IReadOnlyDictionary<long, string> ClaimConflicts { get; init; } = new Dictionary<long, string>();

    public IReadOnlyDictionary<(long UserId, string Stat), string> CellMisses { get; init; } = new Dictionary<(long UserId, string Stat), string>();

    public IReadOnlyList<string> CounterNames { get; init; } = [];

    public string? IconText { get; init; }

    /// <summary>The slug of the recipe this cycle actually read, which the window checks against the one it now runs.</summary>
    public string RecipeSlug { get; init; } = "";

    /// <summary>The source this cycle read for, or empty for a watch without one.</summary>
    public string SourceId { get; init; } = "";

    /// <summary>Whether this cycle wrote a reading line to the score book.</summary>
    public bool Recorded { get; init; }

    /// <summary>
    /// When every number here came from the score book rather than a read, and when that reading was taken (plan
    /// A39). Null on everything a watch produces. It is the single mark: nothing pairs it with a flag that could
    /// disagree with it, and only <c>Remembered</c> ever sets it.
    /// </summary>
    public DateTimeOffset? RememberedAt { get; init; }

    /// <summary>
    /// Where each of your accounts placed among EVERY row its reading saw, by account and stat, as the score book
    /// kept it (plan A40, review C1). Only <c>Remembered</c> ever fills it, and only from what the book holds.
    /// <para>
    /// It exists because a remembered snapshot's <see cref="Rows"/> are your own accounts alone, so a place worked
    /// out from them would be a place in a group this never counted — "#1 of 4" where the reading said "#7 of 50".
    /// A live snapshot carries every row it read, so its panels count for themselves and this stays empty.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<(long UserId, string Stat), RankInGroup> RememberedRanks { get; init; } =
        new Dictionary<(long UserId, string Stat), RankInGroup>();

    /// <summary>Why this cycle kept nothing, when a book is attached and nothing was kept.</summary>
    public string? NotRecordingReason { get; init; }

    /// <summary>
    /// What THIS cycle sent to RoRoRo: one of your accounts and a stat key for each value that went. Empty for a cycle that
    /// sent nothing, whatever an earlier one sent. <see cref="Accounts"/> is the session's memory of sends and outlives the
    /// read that made them, so the board's sent dot, which means "went to RoRoRo just now", reads this (backlog S1-F.5).
    /// </summary>
    public IReadOnlySet<(Guid AccountId, string Stat)> SentThisRead { get; init; } = new HashSet<(Guid AccountId, string Stat)>();

    public ReadingPeriod? Period { get; init; }

    /// <summary>A group list's groups, shown live and never kept.</summary>
    public IReadOnlyList<GroupRow> Groups { get; init; } = [];
}

/// <summary>
/// One account's place as a reading counted it, and how many players it was counted among, "#7 of 49": the rows with a value
/// for that stat (backlog S1-6.9). A null count is a place kept before the book kept its field, shown as the place alone.
/// </summary>
public sealed record RankInGroup(int Rank, int? Of);

/// <summary>
/// One cycle for one source: ask for the user's accounts, read the recipe, keep the user's rows in the score
/// book, and hand each sent stat to RoRoRo through the report policy. Carries every guarantee
/// <c>ScoreWatch</c> earned: one cycle at a time, raw values in UTC, no backlog when RoRoRo returns, and a
/// named capability when consent is declined.
/// </summary>
/// <param name="writeLabel">
/// Puts a rival clan's name on the rule a managed-label number fires (<see cref="FieldMetric.ManagedLabel"/>),
/// and answers whether the label is IN PLACE: written just now, already saying exactly this, or (Ur Score having
/// no rule on the metric at all) not something any alert could fire under. Null is a build with no writer wired,
/// which is the same answer as a refusal — see <see cref="SendFieldAsync"/> for why a false answer stops the
/// number going out.
/// <para>
/// The caller owns the rules path and so owns the mapping from <see cref="RuleWrite"/> to that bool
/// (<c>AppServices.LabelInPlace</c>, task 8's controller ruling). <see cref="RuleWrite.Done"/>,
/// <see cref="RuleWrite.NotThere"/> and <see cref="RuleWrite.AlreadyThere"/> are true; every other answer —
/// <see cref="RuleWrite.CantWrite"/> above all — is false, because it leaves a rule whose label names somebody
/// else. The full reasoning for each case, including why NotThere is true for a different reason than this
/// plan first gave, lives on that one mapping rather than repeated at every caller.
/// </para>
/// </param>
/// <param name="myGroups">
/// The clans you set up, by name, IN ORDER — the order <see cref="Labs626.UrScore.Core.SourceRules.MyClanNames"/>
/// returns them in, which is the order the Setup pages read too. The order is part of the contract, not an
/// accident: the label names the first of yours, and so does <c>AlertsPage.Clan()</c>, so the two agree only if
/// both take the first of the SAME ordered answer. Until the final review of this branch, 2026-09-20, this seam
/// handed over an <c>IReadOnlySet</c> and <see cref="SendFieldAsync"/> took its <c>FirstOrDefault</c>: a
/// <c>HashSet</c>'s enumeration order is not contractual, so the two agreed by coincidence of runtime behaviour
/// and the stated reason for the guess was false. Everything that asks "is this clan one of mine?" folds this
/// list into a case-insensitive set of its own, as it always did.
/// </param>
public sealed class RecipeWatch(
    IRecipeEngine engine,
    IHostClient host,
    IKeyStore keys,
    ReportPolicy policy,
    Recipe recipe,
    IReadOnlyDictionary<string, string> inputs,
    IReadOnlySet<string> trackedStats,
    IScoreBook? book = null,
    Source? source = null,
    SharedAccounts? sharedAccounts = null,
    string recipeText = "",
    AccountClaims? claims = null,
    FinalsIndex? finals = null,
    TimeProvider? time = null,
    Func<IReadOnlyList<string>>? myGroups = null,
    Func<FieldMetric, string, bool>? writeLabel = null)
{
    internal const string RecipeChangedDetail = "The recipe changed while it was being read, so nothing was sent this time.";

    internal const string RecipeChangedMidSendDetail = "The recipe changed while this reading was being sent, so the rest of it was not sent.";

    internal const string WatchOnlyDetail = "Watching only: nothing is sent, and no account is kept.";

    internal const string NotRecordingNoAccounts = "None of your accounts were in this read.";

    /// <summary>
    /// Your accounts were in this read, and another source of the recipe read them first, so that one keeps them (ruling R6).
    /// Saying none were here named a cause that wasn't the cause (backlog S1-6.8).
    /// </summary>
    internal const string NotRecordingKeptElsewhere = "Your accounts in this read are kept by another source of this recipe, which read them first.";

    internal const string NotRecordingEnded = "It has ended, and its final result is saved.";

    /// <summary>Kept for the state a group list can still be in: read, but with no clan carrying the value it lists by.</summary>
    internal const string NotRecordingNoField = "This read brought no clan with a number, so there was nothing to keep.";

    internal const string NotRecordingNothingRead = "Nothing was read this time.";

    internal const string NotRecordingNoText = "The recipe text isn't known, so nothing is kept.";

    /// <summary>
    /// Said on the board whenever a threat number is held back. Names the cause, because the rules file is the
    /// user's own to look at, and says what the alternative would have been, because "held back" on its own reads
    /// like a fault rather than a decision taken on purpose (V3-S.35).
    /// </summary>
    internal static string HeldBackDetail(int held) =>
        $" {held} clan number(s) held back: the alert name couldn't be written, and sending them would have named the wrong clan.";

    /// <summary>
    /// This watch's clock, and the only one it is allowed to read. Every observation, book line and metric
    /// timestamp comes from here.
    /// <para>
    /// The account-stat reports used <c>DateTimeOffset.UtcNow</c> directly while everything else already took the
    /// injected provider, so a test with a fixed clock got fabricated timestamps on one path and real wall-clock
    /// ones on another, from the SAME read (S1-6.7). That is worse than either alone: RoRoRo keys a rate window
    /// on the observation time, so two halves of one reading disagreeing about when it happened is not a tidiness
    /// problem, it is a reading that cannot be reasoned about.
    /// </para>
    /// </summary>
    private DateTimeOffset Now => (time ?? TimeProvider.System).GetUtcNow();

    private readonly Dictionary<Guid, AccountLine> _lines = [];

    /// <summary>
    /// <see cref="UpdateRecipe"/> and <see cref="UpdateSource"/> run on the UI thread while a cycle runs on
    /// the pool. Every access from both sides to these goes through this one lock: the recipe, its text,
    /// inputs, tracked stats, source, <see cref="_lines"/>, <see cref="_context"/>,
    /// <see cref="_previousPeriod"/> and <see cref="_held"/>. So does every read of the policy that must
    /// match a recipe check. Never held across an await.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>A timer tick and a Test now click must never both report the same observation.</summary>
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    private string? _context;

    /// <summary>The period the last successful read belonged to, so a period that just ended is an "ended" final.</summary>
    private string? _previousPeriod;

    /// <summary>
    /// A stop that retrying cannot fix, and what would release it (plan Ruling 6). A null
    /// <c>KeyFingerprint</c> means only <see cref="UpdateRecipe"/> releases the hold — that is
    /// <see cref="ReadingOutcome.SignInRequired"/>, which no key change can fix. A non-null one is
    /// <see cref="ReadingOutcome.KeyRejected"/>, released when the saved keys change (Ruling E).
    /// </summary>
    private (RecipeSnapshot Snapshot, string? KeyFingerprint)? _held;

    /// <summary>
    /// This session's clans-list readings: your clan's points, and the points of the place above it, so a chase
    /// has two paces to work with. Only the last <see cref="Pace.LongestCurrent"/> is kept — a pace older than
    /// that is not a current one — and nothing here outlives the process, so after a restart the numbers that
    /// need a pace go quiet until there are <see cref="Pace.Shortest"/> of readings again.
    /// </summary>
    private readonly List<SeriesPoint> _fieldMine = [];

    private readonly List<SeriesPoint> _fieldAbove = [];

    /// <summary>
    /// Each tracked chaser's recent points, by clan name. Separate from <see cref="_fieldAbove"/> because that one
    /// tracks a POSITION and clears itself on a fall, which is what a change of occupant looks like from a
    /// position. A threat is tracked by name, where that rule is both wrong and unnecessary: the identity is the
    /// key rather than something inferred, and a fall is a correction or a bad read, not a new clan (design §4).
    /// A fall still costs a window of silence rather than a wrong pace, because <see cref="Pace.Over"/> refuses a
    /// window that lost ground — which is the right answer for a bad read, and is the whole of what that rule was
    /// buying here anyway.
    /// <para>
    /// Names that leave the band are dropped, so a clan that drops out and comes back does not pair up a reading
    /// from before it left with one from after as though nothing had happened. Kept to
    /// <see cref="Pace.LongestCurrent"/> like the two above, and gone at the end of the process like them too.
    /// </para>
    /// </summary>
    private readonly Dictionary<string, List<SeriesPoint>> _fieldBehind = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// How many clans below us are watched for a threat. The one directly behind is not the answer: a clan three
    /// places back going much faster passes us sooner, and naming the nearest by PLACE would be a confident false
    /// statement on a phone (design §4). Five is the owner's ruling for how wide that band is.
    /// </summary>
    private const int ThreatBand = 5;

    public ReportPolicy Policy => policy;

    public Recipe Recipe => recipe;

    public Source? Source
    {
        get
        {
            lock (_gate) return source;
        }
    }

    public void UpdatePolicy(
        IReadOnlyList<SentStat> sentStats, IReadOnlySet<Guid> allowedSubjects,
        IReadOnlyList<FieldMetric>? sentFieldMetrics = null)
    {
        lock (_gate) policy = policy.With(sentStats, allowedSubjects, sentFieldMetrics);
    }

    /// <summary>A role change (make main, watch a clan) applies to the next cycle, with no new watch.</summary>
    public void UpdateSource(Source newSource)
    {
        lock (_gate) source = newSource;
    }

    /// <summary>
    /// A different recipe or different inputs mean every remembered value belongs to something else,
    /// so they are cleared. The same recipe and inputs, reloaded, keep them. A change to which stats
    /// are tracked only changes what the next read asks for.
    /// </summary>
    public void UpdateRecipe(Recipe newRecipe, IReadOnlyDictionary<string, string> newInputs, IReadOnlySet<string> newTrackedStats, string? newRecipeText = null)
    {
        lock (_gate)
        {
            var slugChanged = !string.Equals(newRecipe.Slug, recipe.Slug, StringComparison.Ordinal);
            var same = !slugChanged
                       && newInputs.Count == inputs.Count
                       && newInputs.All(kv => inputs.TryGetValue(kv.Key, out var v) && string.Equals(v, kv.Value, StringComparison.Ordinal));

            recipe = newRecipe;
            inputs = new Dictionary<string, string>(newInputs, StringComparer.Ordinal);
            trackedStats = new HashSet<string>(newTrackedStats, StringComparer.Ordinal);

            // A different slug with no text supplied must not keep the old recipe's bytes under the
            // new slug (fix round 1, finding 2): Record hashes and writes whatever recipeText holds,
            // so an unset text for a changed recipe reads as "unknown" here, never as the previous
            // recipe's text.
            var textChanged = newRecipeText is not null && !string.Equals(newRecipeText, recipeText, StringComparison.Ordinal);
            recipeText = newRecipeText ?? (slugChanged ? "" : recipeText);

            // A held "sign in" or "key rejected" stop is released by a change the site could answer differently
            // to — another recipe, other inputs, new recipe text — and not by a change to which stats are
            // tracked, which the site never sees. Releasing it on every update cost one more rejected request
            // per tick (S1-14.8).
            if (!same || textChanged) _held = null;

            if (!same)
            {
                _lines.Clear();
                _context = null;
                _previousPeriod = null;
            }
        }
    }

    public async Task<RecipeSnapshot> RunOnceAsync(CancellationToken cancellationToken, string trigger = BookLine.TriggerTimer)
    {
        await _oneAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RunOnceCoreAsync(trigger, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private async Task<RecipeSnapshot> RunOnceCoreAsync(string trigger, CancellationToken cancellationToken)
    {
        // This cycle reads with what is current now, and only that. UpdateRecipe can swap these while the
        // read is in flight; RecipeChanged catches it before anything is kept or sent.
        Recipe readRecipe;
        IReadOnlyDictionary<string, string> readInputs;
        IReadOnlySet<string> readTracked;
        string readText;
        Source? readSource;
        (RecipeSnapshot Snapshot, string? KeyFingerprint)? heldNow;
        lock (_gate)
        {
            readRecipe = recipe;
            readInputs = inputs;
            readTracked = trackedStats;
            readText = recipeText;
            readSource = source;
            heldNow = _held;
        }

        if (heldNow is { } held && (held.KeyFingerprint is null || held.KeyFingerprint == KeyFingerprint()))
        {
            return held.Snapshot;
        }

        lock (_gate) _held = null;

        // Accounts first: a per-account recipe builds its requests from these ids. Read every cycle (or from
        // the shared list), so an account added mid-session is watched without restarting anything.
        IReadOnlyList<HostAccount> accounts = [];
        bool hostUp;
        if (sharedAccounts is not null)
        {
            var list = await sharedAccounts.GetAsync(cancellationToken).ConfigureAwait(false);
            if (list.Denied) return Snapshot(readRecipe, readSource, WatchState.Rejected, RejectedMessage("host.queries.accounts"), 0, []);

            hostUp = list.HostUp;
            accounts = list.Accounts;
        }
        else
        {
            hostUp = await host.IsReachableAsync(cancellationToken).ConfigureAwait(false);
            if (hostUp)
            {
                try
                {
                    accounts = await host.GetAccountsAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.PermissionDenied)
                {
                    return Snapshot(readRecipe, readSource, WatchState.Rejected, RejectedMessage("host.queries.accounts"), 0, []);
                }
            }
        }

        var watchOnly = readSource?.Role == SourceRole.Watch;
        var unresolved = AccountMap.Unresolved(accounts);
        var map = watchOnly || readRecipe.IsGroupList ? new Dictionary<long, Guid>() : AccountMap.Build(accounts);

        var reading = await engine.ReadAsync(readRecipe, readInputs, [.. map.Keys], readTracked, cancellationToken).ConfigureAwait(false);

        // The fetch honours the token; nothing after it used to. So a source removed while its fetch was in
        // flight had that fetch cancelled cleanly, but one removed a moment AFTER the fetch returned went on to
        // record to the book and send to RoRoRo as though it were still wanted (S1-8.3). This is the last point
        // at which a read can be stopped before it has any effect, and a removal is the caller's way of saying
        // stop, so it is honoured here as well.
        cancellationToken.ThrowIfCancellationRequested();

        WatchState? stopped = reading.Outcome switch
        {
            ReadingOutcome.NeedsInput => WatchState.NeedsInput,
            ReadingOutcome.Idle => WatchState.SourceIdle,
            ReadingOutcome.Unreachable => WatchState.SourceUnreachable,
            ReadingOutcome.RateLimited => WatchState.RateLimited,
            ReadingOutcome.InputNotFound => WatchState.InputNotFound,
            ReadingOutcome.SignInRequired => WatchState.SignInRequired,
            ReadingOutcome.KeyMissing => WatchState.KeyMissing,
            ReadingOutcome.KeyRejected => WatchState.KeyRejected,
            ReadingOutcome.ShapeNotUnderstood => WatchState.ShapeNotUnderstood,
            _ => null,
        };

        if (stopped is { } state)
        {
            var fingerprint = state == WatchState.KeyRejected ? KeyFingerprint() : null;
            RecipeSnapshot snapshot;

            lock (_gate)
            {
                // A stop read for a recipe that is no longer running must not hold the new one.
                if (RecipeChanged(readRecipe, readInputs)) return ChangedSnapshot(readRecipe, readSource, unresolved);

                // Idle means nothing is live, so no context is current. The remembered values are the
                // finished thing's final numbers and stay readable until something new replaces them.
                if (reading.Outcome == ReadingOutcome.Idle) _context = null;

                // An idle clan still has an icon: the engine reads it before deciding the clan sat out.
                snapshot = Snapshot(readRecipe, readSource, state, reading.Detail, 0, unresolved) with
                {
                    IconText = reading.IconText,
                    NotRecordingReason = book is null ? null : reading.Detail ?? NotRecordingNothingRead,
                };

                if (state == WatchState.KeyRejected)
                {
                    _held = (snapshot, fingerprint);
                }
                else if (state == WatchState.SignInRequired)
                {
                    _held = (snapshot, null);
                }
            }

            // V3-S.1: an idle source that handed its finished periods over still backfills them. No
            // reading line — nothing was read — so the snapshot's "not recording" reason stands.
            if (reading.Outcome == ReadingOutcome.Idle && reading.Past.Count > 0)
            {
                Backfill(readRecipe, readInputs, readText, readTracked, readSource, trigger, reading,
                    OwnedMap(readRecipe, readSource, reading, map));
            }

            return snapshot;
        }

        lock (_gate)
        {
            // Nor may its context clear, or stand in for, the new recipe's remembered values.
            if (RecipeChanged(readRecipe, readInputs)) return ChangedSnapshot(readRecipe, readSource, unresolved);

            if (_context is not null && reading.Context != _context) _lines.Clear();
            _context = reading.Context;
        }

        var seen = reading.RowsSeen;

        if (readRecipe.IsGroupList)
        {
            // The field's own numbers are kept (FieldSummary): no clan is named and no account is matched. What
            // goes out is the clan-and-field numbers the user ticked, each with no subject at all (FieldMetrics).
            var (fieldRecorded, fieldReason) = RecordField(readRecipe, readText, readSource, trigger, reading);

            var field = $"Read {reading.Groups.Count} groups.";
            try
            {
                var (sentField, heldField) = await SendFieldAsync(readRecipe, readInputs, reading, hostUp, cancellationToken).ConfigureAwait(false);
                if (sentField > 0) field += $" Sent {sentField} clan number(s) to RoRoRo.";
                if (heldField > 0) field += HeldBackDetail(heldField);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.PermissionDenied)
            {
                return Snapshot(readRecipe, readSource, WatchState.Rejected, RejectedMessage("host.metrics.report"), seen, unresolved, reading, map);
            }

            return Snapshot(readRecipe, readSource, WatchState.Showing, field, seen, unresolved, reading, map) with
            {
                Recorded = fieldRecorded,
                NotRecordingReason = fieldReason,
            };
        }

        // Ruling R6: an account two sources of this recipe both saw belongs to the one that claimed it first.
        var conflicts = new Dictionary<long, string>();
        var owned = OwnedMap(readRecipe, readSource, reading, map, conflicts);

        var (recorded, notRecording) = Record(readRecipe, readInputs, readText, readTracked, readSource, trigger, reading, map, owned);

        // What this cycle sends, filled as each send goes, so every snapshot below names only this read's sends (S1-F.5).
        var sentNow = new HashSet<(Guid AccountId, string Stat)>();
        RecipeSnapshot Kept(RecipeSnapshot snapshot) =>
            snapshot with { Recorded = recorded, NotRecordingReason = notRecording, SentThisRead = sentNow, ClaimConflicts = conflicts };

        var mine = reading.Rows
            .Where(r => owned.ContainsKey(r.UserId))
            .Select(r => (Subject: owned[r.UserId], r.Values))
            .ToList();

        if (watchOnly)
        {
            return Kept(Snapshot(readRecipe, readSource, WatchState.Showing, WatchOnlyDetail, seen, unresolved, reading, map));
        }

        if (!hostUp)
        {
            // Nothing is fetched from the host and nothing is queued, so there is nothing to replay
            // when it comes back. The book above kept the reading anyway.
            return Kept(Snapshot(readRecipe, readSource, WatchState.HostDown, "RoRoRo is not running. Still watching; nothing is being sent.", seen, unresolved, reading, map));
        }

        if (mine.Count == 0)
        {
            var none = conflicts.Count > 0
                ? $"Read {seen} row(s); your accounts in this read were already claimed by another source of this recipe."
                : $"Read {seen} row(s); none of them are your accounts.";
            if (reading.Detail is not null) none += " " + reading.Detail;
            return Kept(Snapshot(readRecipe, readSource, WatchState.NoMatches, none, seen, unresolved, reading, map));
        }

        if (policy.SentStats.Count == 0)
        {
            var showing = $"Read {mine.Count} of {seen} row(s). No stat is set to send, so nothing went to RoRoRo.";
            if (reading.Detail is not null) showing += " " + reading.Detail;
            return Kept(Snapshot(readRecipe, readSource, WatchState.Showing, showing, seen, unresolved, reading, map));
        }

        // One fixed list for the whole loop. A stat unticked mid-loop is refused by the policy each send
        // captures, which checks its own metric ids.
        IReadOnlyList<SentStat> stats;
        lock (_gate) stats = policy.SentStats;

        var observedAt = Now;

        foreach (var (subject, values) in mine)
        {
            try
            {
                // Raw and unmodified, through the only route out: one observation per sent stat this
                // account has a number for. The policy also checks the account's own Send.
                foreach (var stat in stats)
                {
                    if (!values.TryGetValue(stat.Key, out var value)) continue;

                    // The recipe can be replaced during any send's await. A reading of a recipe that is
                    // no longer running would go out under the new recipe's metric ids wherever the stat
                    // keys coincide, so each send re-checks and takes the policy in the same section.
                    // The first send's check also covers a change during the read.
                    ReportPolicy current;
                    lock (_gate)
                    {
                        if (RecipeChanged(readRecipe, readInputs))
                            return Kept(Snapshot(readRecipe, readSource, WatchState.Showing, RecipeChangedMidSendDetail, seen, unresolved));
                        current = policy;
                    }

                    var sent = await current.SendAsync(host, subject, stat.MetricId, value, observedAt, cancellationToken)
                        .ConfigureAwait(false);

                    if (!sent) continue;

                    sentNow.Add((subject, stat.Key));
                    Remember(readRecipe, readInputs, subject, accounts, stat.Key, value, observedAt);
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.PermissionDenied)
            {
                return Kept(Snapshot(readRecipe, readSource, WatchState.Rejected, RejectedMessage("host.metrics.report"), seen, unresolved, reading, map));
            }
        }

        var detail = $"Reporting {mine.Count} of {seen} row(s).";
        if (reading.Detail is not null) detail += " " + reading.Detail;
        return Kept(Snapshot(readRecipe, readSource, WatchState.Reporting, detail, seen, unresolved, reading, map));
    }

    /// <summary>Ruling R6. Accounts not in this read's rows (a private profile) need no claim.</summary>
    private IReadOnlyDictionary<long, Guid> OwnedMap(Recipe readRecipe, Source? readSource, RecipeReading reading, IReadOnlyDictionary<long, Guid> map,
        Dictionary<long, string>? conflicts = null)
    {
        if (claims is null || readSource is null || map.Count == 0) return map;

        var window = TimeSpan.FromSeconds(readRecipe.EffectiveEverySeconds * 2);
        var inRows = reading.Rows.Select(r => r.UserId).ToHashSet();
        var owned = new Dictionary<long, Guid>();
        foreach (var (userId, accountId) in map)
        {
            if (!inRows.Contains(userId) || claims.TryClaim(readRecipe.Slug, userId, readSource.Id, window, out var owner))
                owned.Add(userId, accountId);
            else if (conflicts is not null && owner is not null)
                conflicts.Add(userId, owner);
        }

        return owned;
    }

    /// <summary>
    /// Score book spec §5.4 and §6: finals first, from the same response, then this read's line, unless its
    /// period has already ended. Returns whether a reading line was kept, and why not.
    /// </summary>
    /// <summary>
    /// The clans-list half of <see cref="Record"/>: one line of <see cref="FieldSummary"/> numbers, so a later read can
    /// say how fast the field was going. No rows, no accounts, no clan name, and no send — a group list never had any.
    /// </summary>
    /// <summary>
    /// The clan-and-field numbers this read can say, each sent under its fixed id with no account attached.
    /// Returns how many went.
    /// <para>
    /// The place above is a POSITION, so its points fall whenever you gain a place and the clan holding it
    /// changes. That is not a pace, it is a different clan, so the series starts again from this read rather
    /// than averaging two clans together.
    /// </para>
    /// </summary>
    private async Task<(int Sent, int Held)> SendFieldAsync(
        Recipe readRecipe, IReadOnlyDictionary<string, string> readInputs, RecipeReading reading, bool hostUp,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FieldMetric> wanted;
        lock (_gate) wanted = policy.SentFieldMetrics;
        if (wanted.Count == 0 || !hostUp) return (0, 0);

        var valueKey = readRecipe.LastStep.Values.FirstOrDefault()?.Id ?? "";

        // Asked once and shared, so the field's numbers, the band behind and the name on the label all come from
        // one answer rather than three reads of a list that can change between them. The ordered list is what is
        // asked for; the set below is folded from it, and the label's clan is taken from the list itself.
        var mineInOrder = myGroups?.Invoke();
        var ours = MineAsSet(mineInOrder);
        var summary = FieldSummary.Of(reading.Groups, valueKey, ours);
        if (summary.Count == 0) return (0, 0);

        var now = Now;
        Remember(_fieldMine, summary, FieldSummary.Mine, now);
        Remember(_fieldAbove, summary, FieldSummary.Above, now);

        var behind = FieldSummary.Behind(reading.Groups, valueKey, ours, ThreatBand);
        RememberBehind(behind, now);

        List<SeriesPoint> mine, above;
        Dictionary<string, List<SeriesPoint>> chasers;
        lock (_gate)
        {
            mine = [.. _fieldMine];
            above = [.. _fieldAbove];
            chasers = _fieldBehind.ToDictionary(kv => kv.Key, kv => new List<SeriesPoint>(kv.Value), StringComparer.OrdinalIgnoreCase);
        }

        var ends = reading.Period?.Ends;
        var values = FieldMetrics.Of(summary, mine, above, now, ends);

        // The soonest by TIME, not the nearest by place, and null when no chaser has a readable pace yet. Both
        // threat numbers are halves of this ONE answer, so neither can ever name a different clan than the other:
        // there is one name in play per read. Whether both GO is a separate question the loop below answers one
        // metric at a time — each writes its own rule's label and a refusal stops only its own number — so one
        // may go out while the other is held back. That is safe (each number that goes is under a label written
        // for it this cycle), which is why it is left alone; the earlier note here claimed a joint guarantee the
        // loop does not provide (final review of this branch, 2026-09-20).
        var threat = FieldMetrics.SoonestThreat(
            behind, mine, name => chasers.TryGetValue(name, out var series) ? series : [], now, ends);
        if (threat is not null)
        {
            values =
            [
                .. values,
                new FieldMetricValue(FieldMetrics.Find(FieldMetrics.ThreatGap)!, threat.Gap),
                new FieldMetricValue(FieldMetrics.Find(FieldMetrics.ThreatHours)!, threat.Hours),
            ];
        }

        // Your own clan's name for the label: the FIRST of the ordered list SourceRules.MyClanNames returns, which
        // is the same answer AlertsPage.Clan() takes for the same purpose (controller ruling, task 8). That
        // sentence is only true because this seam now hands over the ordered list — it used to hand over a
        // HashSet, whose enumeration order is not contractual, so the two agreed by coincidence and the ruling
        // rested on a false reason (final review of this branch, 2026-09-20).
        //
        // It is a guess, but not because the answer is unknowable: FieldSummary.Behind picks the best placed of
        // your clans and measures every Gap from it, so the read HAS decided which one these numbers are about —
        // it just doesn't return the name, and pulling it out is a change to a shipped method the six older clan
        // numbers also go through. So with several clans of yours the label may name a different one of yours
        // than the numbers are about. The THREAT's name, which is the whole point of the label, is unaffected.
        // No clan at all is fine: ThreatLabel then says "catching up" and names nobody.
        var ourClanName = mineInOrder is { Count: > 0 } ? mineInOrder[0] : null;
        var sent = 0;
        var held = 0;

        foreach (var metric in wanted)
        {
            if (values.FirstOrDefault(v => string.Equals(v.Metric.Key, metric.Key, StringComparison.Ordinal)) is not { } value) continue;

            // Re-taken each send, like the account loop: a number unticked mid-cycle is refused by the policy
            // the send actually uses, not by the list this loop started with.
            ReportPolicy current;
            lock (_gate)
            {
                if (RecipeChanged(readRecipe, readInputs)) break;
                current = policy;
            }

            // THE ORDERING RULE (design §1, read out of the host's source rather than assumed). RoRoRo captures
            // the rule — label included — at the observation, and words the push from that captured object up to
            // five seconds later. So the name goes on the rule BEFORE the number goes out, with nothing between
            // the two: report first and the alert ships under the PREVIOUS chaser's name.
            //
            // And a write that refuses is a refusal to SEND. RulesFile.ChangeLabel answers CantWrite rather than
            // guess on a row it cannot edit safely, precisely so a wrong name never ships; honouring that here is
            // the other half of the same invariant. A threat number under a stale name is a confident false
            // statement on a phone mid-battle, which is worse than silence (owner's ruling, 2026-09-20).
            //
            // "Nothing between the two" has to mean nothing from ANOTHER WATCH either, which is what
            // <see cref="ManagedLabelGate"/> below is for.
            if (metric.ManagedLabel)
            {
                if (threat is null || writeLabel is null) continue;

                var label = FieldMetrics.ThreatLabel(threat.Name, ourClanName);
                if (await SendUnderManagedLabelAsync(current, metric, value, label, now, cancellationToken).ConfigureAwait(false)) sent++;
                else held++;
                continue;
            }

            if (await current.SendFieldAsync(host, value.Metric, value.Value, now, cancellationToken).ConfigureAwait(false)) sent++;
        }

        return (sent, held);
    }

    /// <summary>
    /// Held across a managed label's write and the send that rides on it, so two watches in ONE PROCESS cannot
    /// interleave there.
    /// <para>
    /// WHY IT EXISTS. <c>AppServices.CreateWatch</c> builds one watch per source, and the two threat metric ids are
    /// FIXED rather than per source (<see cref="FieldMetrics"/>: one id, so forty members can set the same alert).
    /// With two enabled clans-list recipes both sending field metrics, two watches rewrite the SAME rule row on
    /// their own timers, and watch B's <c>ChangeLabel</c> can land inside watch A's write-then-report pair. The
    /// host then words A's number under B's rival's name — precisely the failure this feature exists to prevent.
    /// Found by the final review of this branch, 2026-09-20.
    /// </para>
    /// <para>
    /// WHAT IT PROTECTS. This process's label writes and the sends that follow them, for metrics that declare
    /// <see cref="FieldMetric.ManagedLabel"/> and nothing else: the six older clan numbers and every account stat
    /// go as they always did, and no watch waits on another to read, record or send those. Sends are on a
    /// multi-minute poll, so serialising this pair costs nothing anybody can see.
    /// </para>
    /// <para>
    /// WHAT IT CANNOT PROTECT. It does not and cannot control when RoRoRo READS the file: the host captures the
    /// rule at the observation and words the push from that capture up to five seconds later, and no lock here
    /// reaches that. Nor is it a lock on <c>metric-rules.json</c> — a second copy of Ur Score, another plugin, or
    /// somebody editing the file by hand is outside it entirely (which is why <see cref="RulesFile"/> writes
    /// through a temporary file and a single swap). It closes the window this program opens between its own
    /// write and its own send, which is the only window this program owns.
    /// </para>
    /// </summary>
    private static readonly SemaphoreSlim ManagedLabelGate = new(1, 1);

    /// <summary>
    /// The write-then-report pair for one managed-label number, run to itself: the label is written and the
    /// number reported with no other watch's write in between (<see cref="ManagedLabelGate"/>). Returns whether
    /// the number went — false for a label that could not be written, which is a refusal to send, not an error.
    /// </summary>
    private async Task<bool> SendUnderManagedLabelAsync(
        ReportPolicy current, FieldMetric metric, FieldMetricValue value, string label, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await ManagedLabelGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (writeLabel is null) return false;

            // Tried and refused is the case worth counting. The caller's own "no writer wired at all" guard means
            // this is reached only when there IS one, so a false here is a rules file that could not be edited
            // safely — something the user can go and look at — and not a build that never had the seam (V3-S.35).
            if (!writeLabel(metric, label))
            {
                current.HeldForItsLabel();
                return false;
            }

            return await current.SendFieldAsync(host, value.Metric, value.Value, now, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ManagedLabelGate.Release();
        }
    }

    /// <summary>
    /// Your clans folded into the set every "is this one of mine?" question is asked through, matched however the
    /// name was typed — Setup takes what you type and a clans list has its own casing. The seam hands over an
    /// ordered list because the label needs the first of it (see <see cref="SendFieldAsync"/>); everything that
    /// only asks about membership wants this, and gets exactly what the composition root used to build itself.
    /// </summary>
    private static IReadOnlySet<string>? MineAsSet(IReadOnlyList<string>? mine) =>
        mine is null ? null : new HashSet<string>(mine, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// This read's band of chasers, each kept under its own name in <see cref="_fieldBehind"/>, and every name no
    /// longer in the band dropped.
    /// <para>
    /// Deliberately NOT <see cref="Remember"/>: that one clears the series whenever the value falls, which is
    /// right for a position whose points falling means somebody else holds it now, and wrong for a name, where a
    /// fall is a correction or a bad read (design §4). A band with nothing in it therefore empties the store
    /// rather than leaving names behind, which is the same answer <see cref="Remember"/> gives when this read
    /// carries no number for its key: whatever is there is about something else now.
    /// </para>
    /// </summary>
    private void RememberBehind(IReadOnlyList<FieldSummary.BehindClan> behind, DateTimeOffset now)
    {
        lock (_gate)
        {
            var inBand = new HashSet<string>(behind.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var name in _fieldBehind.Keys.Where(n => !inBand.Contains(n)).ToList()) _fieldBehind.Remove(name);

            foreach (var clan in behind)
            {
                if (!_fieldBehind.TryGetValue(clan.Name, out var series)) _fieldBehind[clan.Name] = series = [];

                series.Add(new SeriesPoint(now, clan.Value, null, false, 0));
                series.RemoveAll(p => now - p.T > Pace.LongestCurrent);
            }
        }
    }

    /// <summary>One reading of a field number, dropping anything older than a current pace may reach back.</summary>
    private void Remember(List<SeriesPoint> series, IReadOnlyDictionary<string, double> summary, string key, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!summary.TryGetValue(key, out var value))
            {
                // No number this read: whatever is there is about something else now.
                series.Clear();
                return;
            }

            // A fall means the position changed hands. The old clan's points are not this one's history.
            if (series.Count > 0 && value < series[^1].Value) series.Clear();

            series.Add(new SeriesPoint(now, value, null, false, 0));
            series.RemoveAll(p => now - p.T > Pace.LongestCurrent);
        }
    }

    private (bool Recorded, string? Reason) RecordField(
        Recipe readRecipe, string readText, Source? readSource, string trigger, RecipeReading reading)
    {
        if (book is null || readSource is null) return (false, null);
        if (string.IsNullOrWhiteSpace(readText)) return (false, NotRecordingNoText);

        var line = LineBuilder.Reading(
            ContextFor(readRecipe, readText, readSource, trigger), reading, new Dictionary<long, Guid>(), new HashSet<string>(),
            MineAsSet(myGroups?.Invoke()));
        if (line is null) return (false, NotRecordingNoField);

        book.Append(line, readText);
        return (true, null);
    }

    private (bool Recorded, string? Reason) Record(
        Recipe readRecipe, IReadOnlyDictionary<string, string> readInputs, string readText, IReadOnlySet<string> readTracked, Source? readSource, string trigger,
        RecipeReading reading, IReadOnlyDictionary<long, Guid> map, IReadOnlyDictionary<long, Guid> owned)
    {
        if (book is null || readSource is null) return (false, null);

        // Fix round 1, finding 2: an unknown recipe text (never given, and cleared on a slug change
        // by UpdateRecipe) must not be hashed and written under a recipe it doesn't belong to.
        if (string.IsNullOrWhiteSpace(readText)) return (false, NotRecordingNoText);

        var context = ContextFor(readRecipe, readText, readSource, trigger);

        KeepFinals(context, reading, readTracked, owned, readText);

        lock (_gate)
        {
            // Fix round 1, finding 1: a recipe or inputs swap that lands between the top-of-cycle
            // RecipeChanged check and here must not resurrect its period as "previous" for whatever
            // now runs under the new recipe. Mirrors the guard Remember already takes.
            if (reading.Period is { } period && !RecipeChanged(readRecipe, readInputs)) _previousPeriod = period.Value;
        }

        if (finals is not null && FinalsPlanner.CurrentPeriodEnded(context, reading, finals)) return (false, NotRecordingEnded);

        var line = LineBuilder.Reading(context, reading, owned, readTracked);
        if (line is null)
        {
            // Your account in these rows but not in what this source owns: a claim took it, so it wasn't absent (S1-6.8).
            var keptElsewhere = reading.Rows.Any(r => map.ContainsKey(r.UserId) && !owned.ContainsKey(r.UserId));
            return (false, keptElsewhere ? NotRecordingKeptElsewhere : NotRecordingNoAccounts);
        }

        book.Append(line, readText);
        return (true, null);
    }

    /// <summary>
    /// V3-S.1: the finals half of <see cref="Record"/> on its own, for a stop that read no rows but still
    /// handed over its finished periods. No reading line (nothing was read) and no previous-period update
    /// (nothing is live to become the previous one), so the snapshot's "not recording" reason still stands.
    /// </summary>
    private void Backfill(
        Recipe readRecipe, IReadOnlyDictionary<string, string> readInputs, string readText, IReadOnlySet<string> readTracked,
        Source? readSource, string trigger, RecipeReading reading, IReadOnlyDictionary<long, Guid> owned)
    {
        if (book is null || finals is null || readSource is null || string.IsNullOrWhiteSpace(readText)) return;

        // The same guard Record's write takes: a stop read under a recipe that has since been replaced
        // must not write finals under whatever is running now.
        lock (_gate)
        {
            if (RecipeChanged(readRecipe, readInputs)) return;
        }

        KeepFinals(ContextFor(readRecipe, readText, readSource, trigger), reading, readTracked, owned, readText);
    }

    private ReadContext ContextFor(Recipe readRecipe, string readText, Source readSource, string trigger)
    {
        var at = Now;
        return new ReadContext(readSource, readRecipe, BookFiles.Hash(readText), trigger, at, (int)TimeZoneInfo.Local.GetUtcOffset(at).TotalMinutes);
    }

    /// <summary>Score book spec §6: every final this read makes due, written once and remembered in the index.</summary>
    private void KeepFinals(
        ReadContext context, RecipeReading reading, IReadOnlySet<string> readTracked, IReadOnlyDictionary<long, Guid> owned, string readText)
    {
        if (finals is null || book is null) return;

        string? previous;
        lock (_gate) previous = _previousPeriod;

        foreach (var final in FinalsPlanner.Plan(context, reading, owned, readTracked, finals, previous))
        {
            finals.Add(final);
            book.Append(final, readText);
        }
    }

    /// <summary>Whether the recipe or inputs a cycle read with have been replaced since. Call under <see cref="_gate"/>.</summary>
    private bool RecipeChanged(Recipe readRecipe, IReadOnlyDictionary<string, string> readInputs) =>
        !ReferenceEquals(recipe, readRecipe) || !ReferenceEquals(inputs, readInputs);

    /// <summary>Nothing sent, nothing kept, and no rows, so the window has nothing of the old recipe's to draw.</summary>
    private RecipeSnapshot ChangedSnapshot(Recipe readRecipe, Source? readSource, IReadOnlyList<HostAccount> unresolved) =>
        Snapshot(readRecipe, readSource, WatchState.Showing, RecipeChangedDetail, 0, unresolved);

    /// <summary>
    /// Verified against the running host: the Plugins page's only consent control is Remove. There is
    /// no per-capability re-grant, and an existing consent record is never re-prompted.
    /// </summary>
    internal static string RejectedMessage(string capability) =>
        $"RoRoRo refused this: {capability} is not granted. There is no per-capability re-grant — "
        + "remove Ur Score from RoRoRo's Plugins page and reinstall it to be asked again.";

    private string KeyFingerprint() =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\u0001', keys.Values().Order(StringComparer.Ordinal)))));

    private void Remember(
        Recipe readRecipe, IReadOnlyDictionary<string, string> readInputs,
        Guid subject, IReadOnlyList<HostAccount> accounts, string statKey, double value, DateTimeOffset at)
    {
        var name = accounts.FirstOrDefault(a => a.AccountId == subject)?.DisplayName ?? subject.ToString();

        lock (_gate)
        {
            // A reading sent just before its recipe was replaced must not come back into the lines
            // UpdateRecipe cleared for the new one.
            if (RecipeChanged(readRecipe, readInputs)) return;

            var values = _lines.TryGetValue(subject, out var line)
                ? new Dictionary<string, double>(line.LastValues, StringComparer.Ordinal)
                : new Dictionary<string, double>(StringComparer.Ordinal);

            values[statKey] = value;
            _lines[subject] = new AccountLine(name, subject, values, at);
        }
    }

    private RecipeSnapshot Snapshot(
        Recipe readRecipe, Source? readSource, WatchState state, string? detail, int seen, IReadOnlyList<HostAccount> unresolved,
        RecipeReading? reading = null, IReadOnlyDictionary<long, Guid>? map = null)
    {
        IReadOnlyList<AccountLine> lines;
        string? context;
        lock (_gate)
        {
            lines = [.. _lines.Values];
            context = _context;
        }

        return new(state, detail, lines, unresolved, seen, reading?.Context ?? context, reading?.Rows, reading?.Headline)
        {
            Unavailable = reading?.Unavailable ?? new Dictionary<long, string>(),
            StatMisses = reading?.StatMisses ?? new Dictionary<string, string>(),
            CellMisses = reading is null || map is null
                ? new Dictionary<(long UserId, string Stat), string>()
                : reading.CellMisses.Where(cell => map.ContainsKey(cell.Key.UserId)).ToDictionary(cell => cell.Key, cell => cell.Value),
            CounterNames = reading?.CounterNames ?? [],
            IconText = reading?.IconText,
            RecipeSlug = readRecipe.Slug,
            SourceId = readSource?.Id ?? "",
            Period = reading?.Period,
            Groups = reading?.Groups ?? [],
        };
    }
}
