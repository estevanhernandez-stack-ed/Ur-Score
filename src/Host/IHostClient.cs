using Labs626.UrScore.Core;

namespace Labs626.UrScore.Host;

/// <summary>
/// Everything this plugin can do to RoRoRo, which is deliberately three things.
/// <para>
/// An interface rather than a concrete client so the watch loop is testable without a running
/// host — and so the report policy's fence has a single named method to guard.
/// </para>
/// </summary>
public interface IHostClient
{
    Task<bool> IsReachableAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<HostAccount>> GetAccountsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Do not call this. Call <c>ReportPolicy.SendAsync</c>, which calls this.
    /// <see cref="UrScore.Tests.ReportPolicyTests"/> fails the build if anything else does.
    /// </summary>
    Task ReportMetricAsync(
        Guid subject, string metricId, double value, DateTimeOffset observedAt,
        CancellationToken cancellationToken);
}
