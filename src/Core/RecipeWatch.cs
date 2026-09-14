using System.Security.Cryptography;
using System.Text;
using Grpc.Core;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Core;

/// <summary>
/// Everything the window renders from one cycle. <see cref="Rows"/> carries every row the recipe
/// read, the user's own and everyone else's, for the leaderboard; only the user's own are ever
/// reported, through <see cref="ReportPolicy"/>. <see cref="CellMisses"/> holds only the user's own
/// accounts, so another member's id never travels further than the leaderboard.
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

    public IReadOnlyDictionary<(long UserId, string Stat), string> CellMisses { get; init; } = new Dictionary<(long UserId, string Stat), string>();

    public IReadOnlyList<string> CounterNames { get; init; } = [];

    public string? IconText { get; init; }

    /// <summary>The slug of the recipe this cycle actually read, which the window checks against the one it now runs.</summary>
    public string RecipeSlug { get; init; } = "";
}

/// <summary>
/// One cycle: ask RoRoRo for the user's accounts, read the recipe, keep the rows that are the
/// user's, and hand each sent stat to RoRoRo through the report policy. Carries every guarantee
/// <c>ScoreWatch</c> earned: one cycle at a time, raw values in UTC, no backlog when RoRoRo returns,
/// and a named capability when consent is declined.
/// </summary>
public sealed class RecipeWatch(
    IRecipeEngine engine,
    IHostClient host,
    IKeyStore keys,
    ReportPolicy policy,
    Recipe recipe,
    IReadOnlyDictionary<string, string> inputs,
    IReadOnlySet<string> trackedStats)
{
    internal const string RecipeChangedDetail = "The recipe changed while it was being read, so nothing was sent this time.";

    private readonly Dictionary<Guid, AccountLine> _lines = [];

    /// <summary>
    /// <see cref="UpdateRecipe"/> runs on the UI thread while a cycle runs on the pool. Every access to
    /// the recipe, inputs, tracked stats, <see cref="_lines"/>, <see cref="_context"/> and
    /// <see cref="_held"/> from both sides goes through this one lock. Never held across an await.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>A timer tick and a Test now click must never both report the same observation.</summary>
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    private string? _context;

    /// <summary>
    /// A stop that retrying cannot fix, and what would release it (plan Ruling 6). A null
    /// <c>KeyFingerprint</c> means only <see cref="UpdateRecipe"/> releases the hold — that is
    /// <see cref="ReadingOutcome.SignInRequired"/>, which no key change can fix. A non-null one is
    /// <see cref="ReadingOutcome.KeyRejected"/>, released when the saved keys change (Ruling E).
    /// </summary>
    private (RecipeSnapshot Snapshot, string? KeyFingerprint)? _held;

    public ReportPolicy Policy => policy;

    public Recipe Recipe => recipe;

    public void UpdatePolicy(IReadOnlyList<SentStat> sentStats, IReadOnlySet<Guid> allowedSubjects)
    {
        policy = policy.With(sentStats, allowedSubjects);
    }

    /// <summary>
    /// A different recipe or different inputs mean every remembered value belongs to something else,
    /// so they are cleared. The same recipe and inputs, reloaded, keep them. A change to which stats
    /// are tracked only changes what the next read asks for.
    /// </summary>
    public void UpdateRecipe(Recipe newRecipe, IReadOnlyDictionary<string, string> newInputs, IReadOnlySet<string> newTrackedStats)
    {
        lock (_gate)
        {
            var same = string.Equals(newRecipe.Slug, recipe.Slug, StringComparison.Ordinal)
                       && newInputs.Count == inputs.Count
                       && newInputs.All(kv => inputs.TryGetValue(kv.Key, out var v) && string.Equals(v, kv.Value, StringComparison.Ordinal));

            recipe = newRecipe;
            inputs = new Dictionary<string, string>(newInputs, StringComparer.Ordinal);
            trackedStats = new HashSet<string>(newTrackedStats, StringComparer.Ordinal);
            _held = null;

            if (!same)
            {
                _lines.Clear();
                _context = null;
            }
        }
    }

    public async Task<RecipeSnapshot> RunOnceAsync(CancellationToken cancellationToken)
    {
        await _oneAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RunOnceCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private async Task<RecipeSnapshot> RunOnceCoreAsync(CancellationToken cancellationToken)
    {
        // This cycle reads with what is current now, and only that. UpdateRecipe can swap all three
        // while the read is in flight; RecipeChanged catches it before anything is kept or sent.
        Recipe readRecipe;
        IReadOnlyDictionary<string, string> readInputs;
        IReadOnlySet<string> readTracked;
        (RecipeSnapshot Snapshot, string? KeyFingerprint)? heldNow;
        lock (_gate)
        {
            readRecipe = recipe;
            readInputs = inputs;
            readTracked = trackedStats;
            heldNow = _held;
        }

        if (heldNow is { } held && (held.KeyFingerprint is null || held.KeyFingerprint == KeyFingerprint()))
        {
            return held.Snapshot;
        }

        lock (_gate) _held = null;

        // Accounts first: a per-account recipe builds its requests from these ids. Read every cycle,
        // so an account added mid-session is watched without restarting anything.
        IReadOnlyList<HostAccount> accounts = [];
        var hostUp = await host.IsReachableAsync(cancellationToken).ConfigureAwait(false);
        if (hostUp)
        {
            try
            {
                accounts = await host.GetAccountsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.PermissionDenied)
            {
                return Snapshot(readRecipe, WatchState.Rejected, RejectedMessage("host.queries.accounts"), 0, []);
            }
        }

        var unresolved = AccountMap.Unresolved(accounts);
        var map = AccountMap.Build(accounts);

        var reading = await engine.ReadAsync(readRecipe, readInputs, [.. map.Keys], readTracked, cancellationToken).ConfigureAwait(false);

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

            lock (_gate)
            {
                // A stop read for a recipe that is no longer running must not hold the new one.
                if (RecipeChanged(readRecipe, readInputs)) return ChangedSnapshot(readRecipe, unresolved);

                // Idle means nothing is live, so no context is current. The remembered values are the
                // finished thing's final numbers and stay readable until something new replaces them.
                if (reading.Outcome == ReadingOutcome.Idle) _context = null;

                // An idle clan still has an icon: the engine reads it before deciding the clan sat out.
                var snapshot = Snapshot(readRecipe, state, reading.Detail, 0, unresolved) with { IconText = reading.IconText };
                if (state == WatchState.KeyRejected)
                {
                    _held = (snapshot, fingerprint);
                }
                else if (state == WatchState.SignInRequired)
                {
                    _held = (snapshot, null);
                }

                return snapshot;
            }
        }

        lock (_gate)
        {
            // Nor may its context clear, or stand in for, the new recipe's remembered values.
            if (RecipeChanged(readRecipe, readInputs)) return ChangedSnapshot(readRecipe, unresolved);

            if (_context is not null && reading.Context != _context) _lines.Clear();
            _context = reading.Context;
        }

        var seen = reading.RowsSeen;
        var mine = reading.Rows
            .Where(r => map.ContainsKey(r.UserId))
            .Select(r => (Subject: map[r.UserId], r.Values))
            .ToList();

        if (!hostUp)
        {
            // Nothing is fetched from the host and nothing is queued, so there is nothing to replay
            // when it comes back.
            return Snapshot(readRecipe, WatchState.HostDown, "RoRoRo is not running. Still watching; nothing is being sent.", seen, unresolved, reading, map);
        }

        if (mine.Count == 0)
        {
            var none = $"Read {seen} row(s); none of them are your accounts.";
            if (reading.Detail is not null) none += " " + reading.Detail;
            return Snapshot(readRecipe, WatchState.NoMatches, none, seen, unresolved, reading, map);
        }

        if (policy.SentStats.Count == 0)
        {
            var showing = $"Read {mine.Count} of {seen} row(s). No stat is set to send, so nothing went to RoRoRo.";
            if (reading.Detail is not null) showing += " " + reading.Detail;
            return Snapshot(readRecipe, WatchState.Showing, showing, seen, unresolved, reading, map);
        }

        // The policy is the one current now. A reading of a recipe that is no longer running would go
        // out under the new recipe's metric ids wherever the stat keys coincide, so it goes nowhere.
        lock (_gate)
        {
            if (RecipeChanged(readRecipe, readInputs)) return ChangedSnapshot(readRecipe, unresolved);
        }

        var observedAt = DateTimeOffset.UtcNow;

        foreach (var (subject, values) in mine)
        {
            try
            {
                // Raw and unmodified, through the only route out: one observation per sent stat this
                // account has a number for. The policy also checks the account's own Send.
                foreach (var stat in policy.SentStats)
                {
                    if (!values.TryGetValue(stat.Key, out var value)) continue;

                    var sent = await policy.SendAsync(host, subject, stat.MetricId, value, observedAt, cancellationToken)
                        .ConfigureAwait(false);

                    if (sent) Remember(subject, accounts, stat.Key, value, observedAt);
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.PermissionDenied)
            {
                return Snapshot(readRecipe, WatchState.Rejected, RejectedMessage("host.metrics.report"), seen, unresolved, reading, map);
            }
        }

        var detail = $"Reporting {mine.Count} of {seen} row(s).";
        if (reading.Detail is not null) detail += " " + reading.Detail;
        return Snapshot(readRecipe, WatchState.Reporting, detail, seen, unresolved, reading, map);
    }

    /// <summary>Whether the recipe or inputs a cycle read with have been replaced since. Call under <see cref="_gate"/>.</summary>
    private bool RecipeChanged(Recipe readRecipe, IReadOnlyDictionary<string, string> readInputs) =>
        !ReferenceEquals(recipe, readRecipe) || !ReferenceEquals(inputs, readInputs);

    /// <summary>Nothing sent, nothing kept, and no rows, so the window has nothing of the old recipe's to draw.</summary>
    private RecipeSnapshot ChangedSnapshot(Recipe readRecipe, IReadOnlyList<HostAccount> unresolved) =>
        Snapshot(readRecipe, WatchState.Showing, RecipeChangedDetail, 0, unresolved);

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

    private void Remember(Guid subject, IReadOnlyList<HostAccount> accounts, string statKey, double value, DateTimeOffset at)
    {
        var name = accounts.FirstOrDefault(a => a.AccountId == subject)?.DisplayName ?? subject.ToString();

        lock (_gate)
        {
            var values = _lines.TryGetValue(subject, out var line)
                ? new Dictionary<string, double>(line.LastValues, StringComparer.Ordinal)
                : new Dictionary<string, double>(StringComparer.Ordinal);

            values[statKey] = value;
            _lines[subject] = new AccountLine(name, subject, values, at);
        }
    }

    private RecipeSnapshot Snapshot(
        Recipe readRecipe, WatchState state, string? detail, int seen, IReadOnlyList<HostAccount> unresolved,
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
        };
    }
}
