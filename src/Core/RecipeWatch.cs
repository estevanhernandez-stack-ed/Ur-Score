using System.Security.Cryptography;
using System.Text;
using Grpc.Core;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Core;

/// <summary>
/// Everything the window renders from one cycle. <see cref="Rows"/> carries every row the recipe
/// read, the user's own and everyone else's, for the leaderboard; only the user's own are ever
/// reported, through <see cref="ReportPolicy"/>.
/// </summary>
public sealed record RecipeSnapshot(
    WatchState State,
    string? Detail,
    IReadOnlyList<AccountLine> Accounts,
    IReadOnlyList<HostAccount> Unresolved,
    int RowsSeen,
    string? Context = null,
    IReadOnlyList<RecipeRow>? Rows = null,
    IReadOnlyList<HeadlineValue>? Headline = null);

/// <summary>
/// One cycle: ask RoRoRo for the user's accounts, read the recipe, keep the rows that are the
/// user's, and hand each value to RoRoRo through the report policy. Carries every guarantee
/// <c>ScoreWatch</c> earned: one cycle at a time, raw values in UTC, no backlog when RoRoRo returns,
/// and a named capability when consent is declined.
/// </summary>
public sealed class RecipeWatch(
    IRecipeEngine engine,
    IHostClient host,
    IKeyStore keys,
    ReportPolicy policy,
    Recipe recipe,
    IReadOnlyDictionary<string, string> inputs)
{
    private readonly Dictionary<Guid, AccountLine> _lines = [];

    /// <summary>A timer tick and a Test now click must never both report the same observation.</summary>
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    private string? _context;

    /// <summary>A stop that retrying cannot fix, and what would release it (plan Ruling 6).</summary>
    private (RecipeSnapshot Snapshot, string KeyFingerprint)? _held;

    public ReportPolicy Policy => policy;

    public Recipe Recipe => recipe;

    public void UpdatePolicy(string metricId, IReadOnlySet<Guid> allowedSubjects)
    {
        policy = policy.With(metricId, allowedSubjects);
    }

    /// <summary>
    /// A different recipe or different inputs mean every remembered value belongs to something else,
    /// so they are cleared. The same recipe and inputs, reloaded, keep them.
    /// </summary>
    public void UpdateRecipe(Recipe newRecipe, IReadOnlyDictionary<string, string> newInputs)
    {
        var same = string.Equals(newRecipe.Slug, recipe.Slug, StringComparison.Ordinal)
                   && newInputs.Count == inputs.Count
                   && newInputs.All(kv => inputs.TryGetValue(kv.Key, out var v) && string.Equals(v, kv.Value, StringComparison.Ordinal));

        recipe = newRecipe;
        inputs = new Dictionary<string, string>(newInputs, StringComparer.Ordinal);
        _held = null;

        if (!same)
        {
            _lines.Clear();
            _context = null;
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
        if (_held is { } held && held.KeyFingerprint == KeyFingerprint())
        {
            return held.Snapshot;
        }

        _held = null;

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
                return Snapshot(WatchState.Rejected, RejectedMessage("host.queries.accounts"), 0, []);
            }
        }

        var unresolved = AccountMap.Unresolved(accounts);
        var map = AccountMap.Build(accounts);

        var reading = await engine.ReadAsync(recipe, inputs, [.. map.Keys], cancellationToken).ConfigureAwait(false);

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
            // Idle means nothing is live, so no context is current. The remembered values are the
            // finished thing's final numbers and stay readable until something new replaces them.
            if (reading.Outcome == ReadingOutcome.Idle) _context = null;

            var snapshot = Snapshot(state, reading.Detail, 0, unresolved);
            if (state is WatchState.KeyRejected or WatchState.SignInRequired)
            {
                _held = (snapshot, KeyFingerprint());
            }

            return snapshot;
        }

        if (_context is not null && reading.Context != _context) _lines.Clear();
        _context = reading.Context;

        var seen = reading.RowsSeen;
        var mine = reading.Rows
            .Where(r => map.ContainsKey(r.UserId))
            .Select(r => (Subject: map[r.UserId], r.Value))
            .ToList();

        if (!hostUp)
        {
            // Nothing is fetched from the host and nothing is queued, so there is nothing to replay
            // when it comes back.
            return Snapshot(WatchState.HostDown, "RoRoRo is not running. Still watching; nothing is being sent.", seen, unresolved, reading);
        }

        if (mine.Count == 0)
        {
            return Snapshot(WatchState.NoMatches, $"Read {seen} row(s); none of them are your accounts.", seen, unresolved, reading);
        }

        var observedAt = DateTimeOffset.UtcNow;

        foreach (var (subject, value) in mine)
        {
            try
            {
                // Raw and unmodified, through the only route out.
                var sent = await policy.SendAsync(host, subject, policy.MetricId, value, observedAt, cancellationToken)
                    .ConfigureAwait(false);

                if (sent) Remember(subject, accounts, value, observedAt);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.PermissionDenied)
            {
                return Snapshot(WatchState.Rejected, RejectedMessage("host.metrics.report"), seen, unresolved, reading);
            }
        }

        var detail = $"Reporting {mine.Count} of {seen} row(s).";
        if (reading.Detail is not null) detail += " " + reading.Detail;
        return Snapshot(WatchState.Reporting, detail, seen, unresolved, reading);
    }

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

    private void Remember(Guid subject, IReadOnlyList<HostAccount> accounts, double value, DateTimeOffset at)
    {
        var name = accounts.FirstOrDefault(a => a.AccountId == subject)?.DisplayName ?? subject.ToString();
        _lines[subject] = new AccountLine(name, subject, value, at);
    }

    private RecipeSnapshot Snapshot(
        WatchState state, string? detail, int seen, IReadOnlyList<HostAccount> unresolved, RecipeReading? reading = null) =>
        new(state, detail, [.. _lines.Values], unresolved, seen, reading?.Context ?? _context, reading?.Rows, reading?.Headline);
}
