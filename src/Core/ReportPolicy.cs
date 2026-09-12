using Labs626.UrScore.Host;

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
/// Everything that fails a check is dropped and counted. Other members' ids and points are read,
/// compared, and dropped: never reported, never written, never logged.
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
public sealed class ReportPolicy(string metricId, IReadOnlySet<Guid> allowedSubjects)
{
    public string MetricId { get; } = metricId;

    /// <summary>
    /// A COPY, deliberately. Held by reference, a caller mutating the set afterwards would widen
    /// this gate with no new policy and no re-render — the window would still be showing the old
    /// count while a newly added account was being sent.
    /// </summary>
    public IReadOnlySet<Guid> AllowedSubjects { get; } = new HashSet<Guid>(allowedSubjects);

    /// <summary>Reports that passed. Shown in the window so "it is working" is a number.</summary>
    public int Sent { get; private set; }

    /// <summary>Reports the gate refused. A rising number here is worth someone looking.</summary>
    public int Dropped { get; private set; }

    public PolicyDecision Evaluate(Guid subject, string candidateMetricId, double value)
    {
        if (!string.Equals(candidateMetricId, MetricId, StringComparison.Ordinal))
        {
            return new PolicyDecision(false,
                $"Metric id '{candidateMetricId}' is not the configured '{MetricId}'.");
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
    /// Rendered verbatim in the window, so what leaves is readable without reading code.
    /// <para>
    /// The "of your N" half is dropped when the numbers cannot both be right. A caller passing a
    /// total smaller than the allow list is a bug somewhere else, and "3 of your 2 accounts" would
    /// make the user distrust a sentence whose whole job is to be trusted.
    /// </para>
    /// </summary>
    public string Describe(int totalAccounts)
    {
        var scope = totalAccounts >= AllowedSubjects.Count
            ? $"{AllowedSubjects.Count} of your {totalAccounts} accounts"
            : $"{AllowedSubjects.Count} accounts";

        return $"Ur Score sends points for {scope}, as {MetricId}. Nothing else leaves this plugin.";
    }
}
