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
/// anyone. Here it is one gate, three checks, stated in the window and held by a fence.
/// </para>
/// <para>
/// Everything that fails a check is dropped and counted. Other members' ids and points are read,
/// compared, and dropped: never reported, never written, never logged.
/// </para>
/// </summary>
public sealed class ReportPolicy(string metricId, IReadOnlySet<Guid> allowedSubjects)
{
    public string MetricId { get; } = metricId;

    public IReadOnlySet<Guid> AllowedSubjects { get; } = allowedSubjects;

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

    /// <summary>Rendered verbatim in the window, so what leaves is readable without reading code.</summary>
    public string Describe(int totalAccounts) =>
        $"Ur Score sends points for {AllowedSubjects.Count} of your {totalAccounts} accounts, "
        + $"as {MetricId}. Nothing else leaves this plugin.";
}
