using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Host;

namespace UrScore.Tests;

/// <summary>
/// The numbers a clans list sends about YOUR clan's standing, and the one gate they pass. The owner's ask of
/// 2026-09-20: a clan leader says "set an alert on that number", and every member sets the same one — so the ids
/// are fixed, and what carries them has no account on it.
/// </summary>
public class FieldMetricsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 18, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Ends = Now.AddHours(10);

    private static Dictionary<string, double> Standing(
        double mine = 8_000_000_000, double rank = 10, double? above = 8_900_000_000,
        double? members = 74, double? capacity = 75, double? contributors = 63)
    {
        var summary = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [FieldSummary.Mine] = mine,
            [FieldSummary.MineRank] = rank,
            [FieldSummary.Leader] = 21_000_000_000,
            [FieldSummary.Clans] = 100,
        };

        if (above is { } them)
        {
            summary[FieldSummary.Above] = them;
            summary[FieldSummary.GapAbove] = them - mine;
        }

        if (members is { } m) summary[FieldSummary.MineMembers] = m;
        if (capacity is { } c) summary[FieldSummary.MineCapacity] = c;
        if (contributors is { } k) summary[FieldSummary.MineContributors] = k;
        return summary;
    }

    /// <summary>An hour of readings, oldest first, rising by <paramref name="perHour"/>.</summary>
    private static IReadOnlyList<SeriesPoint> Rising(double to, double perHour) =>
    [
        new(Now.AddHours(-1), to - perHour, null, false, 0),
        new(Now, to, null, false, 0),
    ];

    private static double Value(IReadOnlyList<FieldMetricValue> values, string key) =>
        values.Single(v => v.Metric.Key == key).Value;

    private static bool Has(IReadOnlyList<FieldMetricValue> values, string key) =>
        values.Any(v => v.Metric.Key == key);

    [Fact]
    public void EveryNumberIsOfferedUnderItsOwnFixedId()
    {
        var ids = FieldMetrics.All.Select(m => m.MetricId).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(FieldMetrics.All, m => Assert.StartsWith("clan.standing.", m.MetricId, StringComparison.Ordinal));

        // Never the id an account's clan points already go out under: one number, one meaning.
        Assert.DoesNotContain("clan.battle.points", ids);
    }

    [Fact]
    public void AReadSaysWhereYouStandAndWhatTheRosterIsDoing()
    {
        var values = FieldMetrics.Of(Standing(), Rising(8e9, 260_000_000), Rising(8.9e9, 200_000_000), Now, Ends);

        Assert.Equal(8_000_000_000, Value(values, FieldMetrics.Points));
        Assert.Equal(10, Value(values, FieldMetrics.Place));
        Assert.Equal(900_000_000, Value(values, FieldMetrics.GapAbove));
        Assert.Equal(1, Value(values, FieldMetrics.FreeSlots));
        Assert.Equal(11, Value(values, FieldMetrics.IdleMembers));
    }

    /// <summary>The owner's metric: the gap spread over the time left, PLUS whatever the clan above is doing.</summary>
    [Fact]
    public void WhatItWouldTakeToPassThemCountsTheirPaceToo()
    {
        var values = FieldMetrics.Of(Standing(), Rising(8e9, 260_000_000), Rising(8.9e9, 200_000_000), Now, Ends);

        // 900M over ten hours is 90M an hour, on top of the 200M they are making.
        Assert.Equal(290_000_000, Value(values, FieldMetrics.PaceNeeded), 0.5);
    }

    [Fact]
    public void WithNoPaceForThePlaceAboveItSaysNothingRatherThanGuess()
    {
        IReadOnlyList<SeriesPoint> tooShort = [new(Now, 8.9e9, null, false, 0)];
        var values = FieldMetrics.Of(Standing(), Rising(8e9, 260_000_000), tooShort, Now, Ends);

        Assert.False(Has(values, FieldMetrics.PaceNeeded));
        Assert.True(Has(values, FieldMetrics.GapAbove));
    }

    [Fact]
    public void LeadingIsAGapOfNothingAndNoChase()
    {
        var values = FieldMetrics.Of(
            Standing(mine: 21e9, rank: 1, above: null), Rising(21e9, 260_000_000), [], Now, Ends);

        Assert.Equal(0, Value(values, FieldMetrics.GapAbove));
        Assert.False(Has(values, FieldMetrics.PaceNeeded));
    }

    [Fact]
    public void OnceTheBattleIsOverThereIsNoPaceToNeed()
    {
        var values = FieldMetrics.Of(
            Standing(), Rising(8e9, 260_000_000), Rising(8.9e9, 200_000_000), Now, Now.AddMinutes(-1));

        Assert.False(Has(values, FieldMetrics.PaceNeeded));
    }

    /// <summary>
    /// Contributors can EXCEED members: it counts everyone who has scored in this battle, including people who
    /// have since left the clan. Measured on the owner's board 2026-09-20 — 72 members, 73 contributors — and
    /// until 0.5.3 that case returned nothing at all, so idle-members had never once been sent on the very board
    /// it was written for. Floored at none: with at least as many scorers as members, no member is known to be
    /// sitting on zero, and that is an answer, not a silence.
    /// </summary>
    [Fact]
    public void MoreScorersThanMembersIsNoneOnZero()
    {
        var values = FieldMetrics.Of(
            Standing(members: 72, capacity: 75, contributors: 73),
            Rising(8e9, 260_000_000), Rising(8.9e9, 200_000_000), Now, Ends);

        Assert.Equal(0, Value(values, FieldMetrics.IdleMembers));
        Assert.Equal(3, Value(values, FieldMetrics.FreeSlots));
    }

    /// <summary>A capacity under its own member count is a bad read, not a negative slot.</summary>
    [Fact]
    public void ACapacityBelowTheRosterSaysNothing()
    {
        var values = FieldMetrics.Of(
            Standing(members: 75, capacity: 70, contributors: 60),
            Rising(8e9, 260_000_000), Rising(8.9e9, 200_000_000), Now, Ends);

        Assert.False(Has(values, FieldMetrics.FreeSlots));
        Assert.Equal(15, Value(values, FieldMetrics.IdleMembers));
    }

    /// <summary>A list that carried no member counts must not read as a full clan with everyone scoring.</summary>
    [Fact]
    public void ACountTheListNeverCarriedIsNotSentAsZero()
    {
        var values = FieldMetrics.Of(
            Standing(members: null, capacity: null, contributors: null),
            Rising(8e9, 260_000_000), Rising(8.9e9, 200_000_000), Now, Ends);

        Assert.False(Has(values, FieldMetrics.FreeSlots));
        Assert.False(Has(values, FieldMetrics.IdleMembers));
        Assert.True(Has(values, FieldMetrics.Points));
    }

    /// <summary>With none of your clans in the read there is no standing at all, and nothing is invented.</summary>
    [Fact]
    public void NoneOfYoursInTheReadSendsNothing()
    {
        var field = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [FieldSummary.Leader] = 21e9,
            [FieldSummary.Clans] = 100,
        };

        Assert.Empty(FieldMetrics.Of(field, [], [], Now, Ends));
    }

    private sealed class SpyClient : IHostClient
    {
        public List<(Guid Subject, string MetricId, double Value)> Sent { get; } = [];

        public Task<bool> IsReachableAsync(CancellationToken ct) => Task.FromResult(true);

        public Task<IReadOnlyList<HostAccount>> GetAccountsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<HostAccount>>([]);

        public Task ReportMetricAsync(Guid subject, string metricId, double value, DateTimeOffset observedAt, CancellationToken ct)
        {
            Sent.Add((subject, metricId, value));
            return Task.CompletedTask;
        }
    }

    private static readonly FieldMetric PlaceMetric = FieldMetrics.Find(FieldMetrics.Place)!;

    private static readonly FieldMetric GapMetric = FieldMetrics.Find(FieldMetrics.GapAbove)!;

    /// <summary>No account, deliberately: RoRoRo keys Guid.Empty globally and words the alert without a player.</summary>
    [Fact]
    public async Task ATickedNumberGoesOutWithNoAccountOnIt()
    {
        var client = new SpyClient();
        var policy = new ReportPolicy([], new HashSet<Guid>(), [PlaceMetric]);

        Assert.True(await policy.SendFieldAsync(client, PlaceMetric, 10, DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.Equal((Guid.Empty, "clan.standing.place", 10d), client.Sent.Single());
        Assert.Equal(1, policy.Sent);
    }

    [Fact]
    public async Task AnUntickedNumberIsDroppedAndCounted()
    {
        var client = new SpyClient();
        var policy = new ReportPolicy([], new HashSet<Guid>(), [PlaceMetric]);

        Assert.False(await policy.SendFieldAsync(client, GapMetric, 900, DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.Empty(client.Sent);
        Assert.Equal(1, policy.Dropped);
    }

    /// <summary>The id is the catalogue's, never the caller's: a made-up one is refused rather than forwarded.</summary>
    [Fact]
    public async Task AnIdThisVersionDoesNotOfferIsRefused()
    {
        var client = new SpyClient();
        var forged = PlaceMetric with { MetricId = "clan.battle.points" };
        var policy = new ReportPolicy([], new HashSet<Guid>(), [forged]);

        Assert.False(await policy.SendFieldAsync(client, forged, 10, DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.Empty(client.Sent);
    }

    [Fact]
    public void TheWindowSaysTheseGoWithNoAccountAttached()
    {
        var described = new ReportPolicy([], new HashSet<Guid>(), [PlaceMetric, GapMetric])
            .Describe(totalAccounts: 8, resolveNames: false);

        Assert.Contains("clan.standing.place", described, StringComparison.Ordinal);
        Assert.Contains("with no account attached", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// A label Ur Score computes and a label a human typed are different kinds of thing, and the catalogue says
    /// which. The six pre-threat metrics are named deliberately: later threat metrics will legitimately set
    /// ManagedLabel: true, so scanning the whole catalogue would break this assertion once they land. Every metric
    /// shipped before threats is a human's to word; nothing silently becomes managed.
    /// </summary>
    [Fact]
    public void OnlyMetricsThatSaySoHaveALabelUrScoreMaintains()
    {
        Assert.False(FieldMetrics.Find(FieldMetrics.Points)!.ManagedLabel);
        Assert.False(FieldMetrics.Find(FieldMetrics.Place)!.ManagedLabel);
        Assert.False(FieldMetrics.Find(FieldMetrics.GapAbove)!.ManagedLabel);
        Assert.False(FieldMetrics.Find(FieldMetrics.PaceNeeded)!.ManagedLabel);
        Assert.False(FieldMetrics.Find(FieldMetrics.FreeSlots)!.ManagedLabel);
        Assert.False(FieldMetrics.Find(FieldMetrics.IdleMembers)!.ManagedLabel);
    }
}
