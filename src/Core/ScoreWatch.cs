using Grpc.Core;
using Labs626.UrScore.Host;
using Labs626.UrScore.Source;

namespace Labs626.UrScore.Core;

/// <summary>
/// One cycle: ask what battle is live, read the contributions, keep the ones that are the user's,
/// and hand each number to RoRoRo through the report policy.
/// <para>
/// <see cref="RunOnceAsync"/> is the whole loop. The timer that calls it repeatedly lives in the
/// window, so every state this can reach is reachable from a test with no clock and no network.
/// </para>
/// </summary>
public sealed class ScoreWatch(
    IClanSource source, IHostClient host, ReportPolicy policy, Settings settings)
{
    private readonly Dictionary<Guid, AccountLine> _lines = [];

    /// <summary>
    /// One cycle at a time. Task 9 drives this from a poll timer AND a "Test now" button, and two
    /// overlapping runs were shown to send the same observation twice while the policy's counter
    /// recorded one — its accounting disagreeing with what the host received. Waiting rather than
    /// skipping, so "Test now" always actually tests: the second run costs one extra request
    /// against a three-minute server cache, which serves it from the same bytes.
    /// </summary>
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    private string? _battle;

    public ReportPolicy Policy => policy;

    public async Task<WatchSnapshot> RunOnceAsync(CancellationToken cancellationToken)
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

    private async Task<WatchSnapshot> RunOnceCoreAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.ClanName))
        {
            return Snapshot(WatchState.Idle, "No clan name set yet.", 0, []);
        }

        var battle = await source.ActiveBattleAsync(cancellationToken).ConfigureAwait(false);

        if (battle.Miss is not null)
        {
            return Snapshot(
                battle.MissIsTransport ? WatchState.SourceUnreachable : WatchState.ShapeNotUnderstood,
                battle.Miss, 0, []);
        }

        if (battle.ConfigName is null)
        {
            // Clear it: Battle means "the battle that is live right now", and a stale name here
            // would let a caller believe one is running between battles. The remembered LINES are
            // deliberately not cleared — they are the finished battle's final numbers and stay
            // readable until a new battle replaces them.
            _battle = null;
            return Snapshot(WatchState.NoBattle, "No clan battle is running right now.", 0, []);
        }

        // A new battle means every remembered value belongs to a finished one. Keeping them
        // would show last week's points beside this week's, with nothing marking which is
        // which — the reviewer reproduced exactly that across three cycles.
        if (_battle is not null && _battle != battle.ConfigName) _lines.Clear();
        _battle = battle.ConfigName;

        var contributions = await source
            .ContributionsAsync(settings.ClanName, battle.ConfigName, cancellationToken)
            .ConfigureAwait(false);

        if (contributions.Miss is not null)
        {
            return Snapshot(
                contributions.MissIsTransport ? WatchState.SourceUnreachable : WatchState.ShapeNotUnderstood,
                contributions.Miss, 0, []);
        }

        var seen = contributions.Contributions.Count;

        // Read every cycle rather than once: an account added mid-session should start being
        // watched without anyone restarting anything.
        IReadOnlyList<HostAccount> accounts = [];
        var hostUp = await host.IsReachableAsync(cancellationToken).ConfigureAwait(false);
        if (hostUp)
        {
            accounts = await host.GetAccountsAsync(cancellationToken).ConfigureAwait(false);
        }

        var unresolved = AccountMap.Unresolved(accounts);
        var map = AccountMap.Build(accounts);

        var mine = contributions.Contributions
            .Where(c => map.ContainsKey(c.UserId))
            .Select(c => (Subject: map[c.UserId], c.Points))
            .ToList();

        if (!hostUp)
        {
            // Polling continues so the window stays useful, but NOTHING is fetched from the host
            // and nothing is queued. The account map is empty here rather than warm — and that
            // emptiness is load-bearing: there is literally nothing to replay when the host comes
            // back, which is why a stale observation cannot be sent minutes after it was read.
            return Snapshot(WatchState.HostDown,
                "RoRoRo is not running. Still watching; nothing is being sent.", seen, unresolved,
                contributions.Contributions, contributions.Standing);
        }

        if (mine.Count == 0)
        {
            return Snapshot(WatchState.NoMatches,
                $"Read {seen} contributor(s); none of them are your accounts.", seen, unresolved,
                contributions.Contributions, contributions.Standing);
        }

        var observedAt = DateTimeOffset.UtcNow;

        foreach (var (subject, points) in mine)
        {
            try
            {
                // Raw and unmodified, through the only route out. A points drop from a battle reset
                // goes as observed: the host treats a decrease as an unmeasurable window, and
                // smoothing it here would manufacture a catastrophic rate that never happened.
                var sent = await policy
                    .SendAsync(host, subject, settings.MetricId, points, observedAt, cancellationToken)
                    .ConfigureAwait(false);

                if (sent) Remember(subject, accounts, points, observedAt);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.PermissionDenied)
            {
                // The user revoked consent in RoRoRo. Stop, say which capability, and do not retry
                // in a loop against a decision they made deliberately.
                return Snapshot(WatchState.Rejected,
                    "RoRoRo refused the report: host.metrics.report is not granted. "
                    + "Re-grant it in RoRoRo under Plugins.", seen, unresolved,
                    contributions.Contributions, contributions.Standing);
            }
        }

        return Snapshot(WatchState.Reporting,
            $"Reporting {mine.Count} of {seen} contributor(s).", seen, unresolved,
            contributions.Contributions, contributions.Standing);
    }

    private void Remember(
        Guid subject, IReadOnlyList<HostAccount> accounts, double value, DateTimeOffset at)
    {
        var name = accounts.FirstOrDefault(a => a.AccountId == subject)?.DisplayName ?? subject.ToString();
        _lines[subject] = new AccountLine(name, subject, value, at);
    }

    private WatchSnapshot Snapshot(
        WatchState state, string? detail, int seen, IReadOnlyList<HostAccount> unresolved,
        IReadOnlyList<Contribution>? contributions = null, ClanStanding? standing = null) =>
        new(state, detail, [.. _lines.Values], unresolved, seen, _battle, contributions, standing);
}
