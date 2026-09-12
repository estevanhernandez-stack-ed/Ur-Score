using Labs626.UrScore.Core;
using Labs626.UrScore.Host;

namespace UrScore.Tests;

public class ReportPolicyTests
{
    private static readonly Guid Allowed = Guid.Parse("9ad5e605-6b41-478c-add3-b916a31a5ab2");
    private static readonly Guid NotAllowed = Guid.Parse("88dc7685-3a36-4f93-b526-a9bff2d7da6c");

    private sealed class SpyClient : IHostClient
    {
        public List<(Guid Subject, string MetricId, double Value)> Sent { get; } = [];

        public Task<bool> IsReachableAsync(CancellationToken ct) => Task.FromResult(true);

        public Task<IReadOnlyList<HostAccount>> GetAccountsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<HostAccount>>([]);

        public Task ReportMetricAsync(
            Guid subject, string metricId, double value, DateTimeOffset observedAt, CancellationToken ct)
        {
            Sent.Add((subject, metricId, value));
            return Task.CompletedTask;
        }
    }

    private static ReportPolicy Policy() => new("clan.battle.points", new HashSet<Guid> { Allowed });

    [Fact]
    public async Task SendsWhatTheUserAskedFor()
    {
        var client = new SpyClient();
        var sent = await Policy().SendAsync(client, Allowed, "clan.battle.points", 4200,
            DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.True(sent);
        Assert.Single(client.Sent);
        Assert.Equal(4200, client.Sent[0].Value);
    }

    [Fact]
    public async Task DropsASubjectThatIsNotOnTheAllowList()
    {
        // The clan endpoint returns every contributor. This is the line between "reads other
        // people's numbers" and "sends other people's numbers".
        var client = new SpyClient();
        var sent = await Policy().SendAsync(client, NotAllowed, "clan.battle.points", 4200,
            DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(sent);
        Assert.Empty(client.Sent);
    }

    [Fact]
    public async Task DropsAMetricIdThatIsNotTheConfiguredOne()
    {
        // One id, not a family. A shape change that starts yielding new field names cannot invent
        // new metrics to send.
        var client = new SpyClient();
        var sent = await Policy().SendAsync(client, Allowed, "something.else", 4200,
            DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(sent);
        Assert.Empty(client.Sent);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public async Task DropsANonFiniteValue(double value)
    {
        // A NaN would poison the host's history silently, and arrives from a shape change far more
        // plausibly than from malice.
        var client = new SpyClient();
        var sent = await Policy().SendAsync(client, Allowed, "clan.battle.points", value,
            DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(sent);
        Assert.Empty(client.Sent);
    }

    [Fact]
    public void EveryDecisionCarriesAReason()
    {
        // The window shows these. "Dropped" with no reason is the silence this design exists to
        // avoid.
        var policy = Policy();

        Assert.Null(policy.Evaluate(Allowed, "clan.battle.points", 1).Reason);
        Assert.NotNull(policy.Evaluate(NotAllowed, "clan.battle.points", 1).Reason);
        Assert.NotNull(policy.Evaluate(Allowed, "other", 1).Reason);
        Assert.NotNull(policy.Evaluate(Allowed, "clan.battle.points", double.NaN).Reason);
    }

    [Fact]
    public void DescribesItselfInPlainWords()
    {
        // Rendered verbatim in the window, so the user can read what leaves without reading code.
        var description = Policy().Describe(totalAccounts: 8);

        Assert.Contains("1 of your 8 accounts", description);
        Assert.Contains("clan.battle.points", description);
    }

    [Fact]
    public void ReportMetricIsCalledFromTheReportPolicyAndNowhereElse()
    {
        // THE FENCE. The tests above prove the gate drops what it should; this proves nothing can
        // route around the gate. A later change that calls the client directly from the loop would
        // pass every other test in this file.
        var src = Path.Combine(RepoRoot(), "src");
        // Exact file NAMES, not suffixes. "IHostClient.cs".EndsWith("HostClient.cs") is true, so a
        // suffix filter would also exempt any future FakeHostClient.cs or ScoringHostClient.cs from
        // the one fence that keeps the report policy honest. Three files may say this word:
        // the policy, the interface that declares it, and the client that implements it.
        string[] permitted = ["ReportPolicy.cs", "HostClient.cs", "IHostClient.cs"];

        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !permitted.Contains(Path.GetFileName(f), StringComparer.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("ReportMetricAsync", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"These files call ReportMetricAsync directly: {string.Join(", ", offenders)}. "
            + "Every outbound report goes through ReportPolicy, which is what makes the report "
            + "policy shown in the window true rather than aspirational.");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ur-Score.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.False(dir is null, "Could not locate Ur-Score.csproj above the test assembly.");
        return dir!.FullName;
    }
}
