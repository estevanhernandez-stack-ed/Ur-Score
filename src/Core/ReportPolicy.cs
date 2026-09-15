using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Core;

/// <summary>Allowed, or a reason. A drop with no reason would be the silence this avoids.</summary>
public sealed record PolicyDecision(bool Allowed, string? Reason);

/// <summary>
/// The only route from this plugin to RoRoRo, and the list of what may take it.
/// <para>
/// This exists because of what the source hands us. The clan endpoint returns EVERY contributor —
/// around seventy-five entries of other people's Roblox user ids and scores — and "own accounts
/// only" was otherwise a decision buried in code, invisible to the user and unverifiable by
/// anyone. Here it is one gate, three checks, stated in the window.
/// </para>
/// <para>
/// A report needs both of the user's ticks: the account's Send (the subject is on the allow list)
/// and the stat's Send (the metric id is one of <see cref="SentStats"/>). Everything that fails a
/// check is dropped and counted. Other members' ids and numbers are read, compared, and dropped:
/// never reported, never written, never logged.
/// </para>
/// <para>
/// WHAT THE FENCE ACTUALLY BUYS. It catches an ACCIDENT — a later change that wires the client
/// straight into the poll loop because the gate was not obvious. It does not stop a determined
/// author: a concatenated method name reached through reflection defeats a source scan, which was
/// demonstrated rather than theorised during review. Nothing short of a Roslyn analyser would do
/// better, and even that yields to reflection. That is an acceptable bound, because the adversary
/// here is a future refactor and not a hostile contributor — but the comment says so rather than
/// letting the next reader believe this is a security boundary.
/// </para>
/// </summary>
public sealed class ReportPolicy(
    IReadOnlyList<SentStat> sentStats, IReadOnlySet<Guid> allowedSubjects, int sent = 0, int dropped = 0)
{
    /// <summary>The stats with Send on, each with the metric id the user pinned. A copy, like the allow list.</summary>
    public IReadOnlyList<SentStat> SentStats { get; } = [.. sentStats];

    /// <summary>
    /// A COPY, deliberately. Held by reference, a caller mutating the set afterwards would widen
    /// this gate with no new policy and no re-render — the window would still be showing the old
    /// count while a newly added account was being sent.
    /// </summary>
    public IReadOnlySet<Guid> AllowedSubjects { get; } = new HashSet<Guid>(allowedSubjects);

    /// <summary>Reports that passed. Shown in the window so "it is working" is a number.</summary>
    public int Sent { get; private set; } = sent;

    /// <summary>Reports the gate refused. A rising number here is worth someone looking.</summary>
    public int Dropped { get; private set; } = dropped;

    /// <summary>
    /// A policy for changed sent stats or allow list, carrying the running <see cref="Sent"/> and
    /// <see cref="Dropped"/> counts forward rather than resetting them to zero.
    /// <para>
    /// Exists so <see cref="RecipeWatch"/> can be updated in place instead of reconstructed (F2): a
    /// reconstructed <c>RecipeWatch</c> gets a fresh <c>ReportPolicy</c> every cycle, which is
    /// exactly how "a rising number here is worth someone looking" became a counter that could
    /// never rise. <see cref="RecipeWatch.UpdatePolicy"/> is the only caller.
    /// </para>
    /// </summary>
    public ReportPolicy With(IReadOnlyList<SentStat> sentStats, IReadOnlySet<Guid> allowedSubjects) =>
        new(sentStats, allowedSubjects, Sent, Dropped);

    public PolicyDecision Evaluate(Guid subject, string candidateMetricId, double value)
    {
        if (!SentStats.Any(stat => string.Equals(stat.MetricId, candidateMetricId, StringComparison.Ordinal)))
        {
            return new PolicyDecision(false,
                $"Metric id '{candidateMetricId}' is not one of the stats set to send.");
        }

        if (!AllowedSubjects.Contains(subject))
        {
            // Deliberately does not name the subject. It would usually be another clan member, and
            // this class exists precisely so their id does not travel.
            return new PolicyDecision(false, "That account is not on the send list.");
        }

        if (!double.IsFinite(value))
        {
            return new PolicyDecision(false, $"The value was {value}, which is not a finite number.");
        }

        return new PolicyDecision(true, null);
    }

    /// <summary>
    /// The single call site of <see cref="IHostClient.ReportMetricAsync"/> in this program.
    /// Returns whether it was sent, so the caller can count without re-deciding.
    /// </summary>
    public async Task<bool> SendAsync(
        IHostClient client, Guid subject, string candidateMetricId, double value,
        DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        if (!Evaluate(subject, candidateMetricId, value).Allowed)
        {
            Dropped++;
            return false;
        }

        await client.ReportMetricAsync(subject, candidateMetricId, value, observedAt, cancellationToken)
            .ConfigureAwait(false);

        Sent++;
        return true;
    }

    /// <summary>
    /// Rendered verbatim in Setup › Alerts (<c>AlertsModel.Policies</c>), so what leaves is readable
    /// without reading code. Names every sent stat by its label and the metric id RoRoRo gets it
    /// under; it never assumes the number is points.
    /// <para>
    /// The "of your N" half is dropped when the numbers cannot both be right. A caller passing a
    /// total smaller than the allow list is a bug somewhere else, and "3 of your 2 accounts" would
    /// make the user distrust a sentence whose whole job is to be trusted. THIS is why the caller
    /// must pass the real count, not <c>Math.Max</c>'d against the allow list — that clamp is
    /// exactly the plausible-looking-wrong-number Task 5's review rejected in the first place, and
    /// re-clamping here would just move the mistake rather than fix it.
    /// </para>
    /// <para>
    /// Residual from F6: this used to end "Nothing else leaves this plugin" unconditionally, which
    /// <c>NameClient</c> contradicts every poll whenever <paramref name="resolveNames"/> is true —
    /// it POSTs other members' Roblox ids to Roblox to resolve usernames. The claim now covers only
    /// the report policy's real job (the pipe to RoRoRo), and a second sentence states plainly
    /// whether name lookups are currently on. Calling this from the one place it is shown is what
    /// keeps the window's copy of this sentence from drifting again.
    /// </para>
    /// </summary>
    public string Describe(int totalAccounts, bool resolveNames = true)
    {
        var scope = totalAccounts >= AllowedSubjects.Count
            ? $"{AllowedSubjects.Count} of your {totalAccounts} accounts"
            : $"{AllowedSubjects.Count} accounts";

        var nameLookups = resolveNames
            ? "Name lookups are on: other members' Roblox ids are sent to Roblox to resolve "
              + "usernames for the leaderboard. Set resolveNames to false in settings.json to stop it."
            : "Name lookups are off: no other member's Roblox id leaves this machine for any reason.";

        if (SentStats.Count == 0)
        {
            return "Ur Score sends nothing to RoRoRo: no stat is set to send. " + nameLookups;
        }

        var labels = JoinWithAnd([.. SentStats.Select(stat => stat.Label)]);
        var metricIds = JoinWithAnd([.. SentStats.Select(stat => stat.MetricId)]);
        return $"Ur Score sends {labels} for {scope}, as {metricIds}. Nothing else reaches RoRoRo. " + nameLookups;
    }

    private static string JoinWithAnd(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}",
    };
}
