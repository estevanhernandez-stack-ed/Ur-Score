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

    [Theory]
    [InlineData(true, "Name lookups are on")]
    [InlineData(false, "Name lookups are off")]
    public void DescribesItselfInPlainWords(bool resolveNames, string expectedNameLookupPhrase)
    {
        // Rendered verbatim in the window, so the user can read what leaves without reading code.
        // Both wordings of the name-lookup disclosure (residual from F6) are covered — this is the
        // one sentence the window actually shows, so both states it can be in must be guarded here.
        var description = Policy().Describe(totalAccounts: 8, resolveNames);

        Assert.Contains("1 of your 8 accounts", description);
        Assert.Contains("clan.battle.points", description);
        Assert.Contains(expectedNameLookupPhrase, description);
    }

    [Theory]
    [InlineData(true, "Name lookups are on")]
    [InlineData(false, "Name lookups are off")]
    public void DescribeDropsOfYourNWhenTheTotalCannotBeRight(bool resolveNames, string expectedNameLookupPhrase)
    {
        // A caller passing a total smaller than the allow list is a bug somewhere else. Clamping
        // would hide that bug behind a plausible-looking "3 of your 2 accounts"; dropping the
        // comparison instead says only what is still true.
        var description = Policy().Describe(totalAccounts: 0, resolveNames);

        Assert.DoesNotContain("of your", description);
        Assert.Contains("1 accounts", description);
        Assert.Contains("clan.battle.points", description);
        Assert.Contains(expectedNameLookupPhrase, description);
    }

    [Fact]
    public async Task CountersTrackSentAndDroppedSeparately()
    {
        // Sent/Dropped are what the window shows to say "it is working". Deleting the increments
        // entirely still passes every other test in this file, so the counters need their own.
        var client = new SpyClient();
        var policy = Policy();

        await policy.SendAsync(client, Allowed, "clan.battle.points", 1, DateTimeOffset.UtcNow,
            CancellationToken.None);
        await policy.SendAsync(client, NotAllowed, "clan.battle.points", 1, DateTimeOffset.UtcNow,
            CancellationToken.None);
        await policy.SendAsync(client, Allowed, "something.else", 1, DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Equal(1, policy.Sent);
        Assert.Equal(2, policy.Dropped);
    }

    [Fact]
    public async Task WithCarriesSentAndDroppedForwardRatherThanResettingThem()
    {
        // F2: ScoreWatch.UpdatePolicy calls this instead of the window constructing a whole new
        // ReportPolicy (which is what a rebuilt ScoreWatch used to do every cycle). If With reset
        // the counts, "a rising number here is worth someone looking" would still be a counter
        // that could never rise past whatever happened since the last allow-list change.
        var client = new SpyClient();
        var policy = Policy();

        await policy.SendAsync(client, Allowed, "clan.battle.points", 1, DateTimeOffset.UtcNow,
            CancellationToken.None);
        await policy.SendAsync(client, NotAllowed, "clan.battle.points", 1, DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Equal(1, policy.Sent);
        Assert.Equal(1, policy.Dropped);

        var widened = policy.With("clan.battle.points", new HashSet<Guid> { Allowed, NotAllowed });

        Assert.Equal(1, widened.Sent);
        Assert.Equal(1, widened.Dropped);
        Assert.Contains(NotAllowed, widened.AllowedSubjects);

        // And the widened policy actually enforces the new list, not just remembers old counts.
        var sent = await widened.SendAsync(client, NotAllowed, "clan.battle.points", 1,
            DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.True(sent);
        Assert.Equal(2, widened.Sent);
    }

    [Fact]
    public void ReportMetricIsCalledFromTheReportPolicyAndNowhereElse()
    {
        // THE FENCE. The tests above prove the gate drops what it should; this proves nothing can
        // route around the gate. A later change that calls the client directly from the loop would
        // pass every other test in this file.
        var src = Path.Combine(RepoRoot(), "src");

        // Paths relative to src/, not bare file names. A bare-name match would also exempt an
        // unrelated src/Somewhere/HostClient.cs — a different file that happens to share a name
        // with the one client this fence means to exempt. Not suffixes either: "IHostClient.cs"
        // .EndsWith("HostClient.cs") is true, so a suffix filter would also exempt any future
        // FakeHostClient.cs or ScoringHostClient.cs from the one fence that keeps the report policy
        // honest. Three files may say this word: the policy, the interface that declares it, and
        // the client that implements it.
        string[] permitted =
        [
            Path.Combine("Core", "ReportPolicy.cs"),
            Path.Combine("Host", "HostClient.cs"),
            Path.Combine("Host", "IHostClient.cs"),
        ];

        // Three needles, not one. The method name alone missed a proven bypass: string-concatenate
        // the name and reach it through reflection and the substring never appears contiguously.
        string[] needles = ["ReportMetricAsync", "typeof(IHostClient)", "GetMethod("];

        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !permitted.Contains(Path.GetRelativePath(src, f), StringComparer.Ordinal))
            .Where(f =>
            {
                var text = File.ReadAllText(f);
                return needles.Any(needle => text.Contains(needle, StringComparison.Ordinal));
            })
            .Select(f => Path.GetRelativePath(src, f))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"These files reach ReportMetricAsync directly or through reflection: "
            + $"{string.Join(", ", offenders)}. Every outbound report goes through ReportPolicy, "
            + "which is what makes the report policy shown in the window true rather than "
            + "aspirational.");
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
