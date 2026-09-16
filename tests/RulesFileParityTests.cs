using Labs626.UrScore.Core;

namespace UrScore.Tests;

/// <summary>
/// Pins, one case each, the RoRoRo parsing behaviours Ur Score's rules reader copies. RoRoRo's parser is the private
/// <c>Options</c> and <c>RuleRow</c> in <c>src/ROROROblox.App/Metrics/LocalFileMetricRuleSource.cs</c>, and each case here was
/// checked against it as of RoRoRo commit dc44992 (branch feat/metric-alert-wording). Ur Score can't reference that assembly,
/// so the copy in <see cref="RulesFile"/> is kept honest by hand. When RoRoRo changes its row or its options, re-check every
/// case against RoRoRo, then change <see cref="RulesFile"/>'s copy and these together. A case that fails here means an alert
/// card would describe a rule differently from the one RoRoRo judges.
/// </summary>
public sealed class RulesFileParityTests : IDisposable
{
    private const string Points = "clan.battle.points";

    private readonly TempDir.Scope _dir = TempDir.Create("urscore-parity");

    public void Dispose() => _dir.Dispose();

    private RulesRead ReadRow(string row)
    {
        var path = Path.Combine(_dir.Path, "metric-rules.json");
        File.WriteAllText(path, $"[ {row} ]");
        return RulesFile.Read(path);
    }

    [Fact]
    public void PropertyNamesMatchIgnoringCase()
    {
        // RoRoRo: PropertyNameCaseInsensitive = true.
        var read = ReadRow($$"""{ "METRICID": "{{Points}}", "Kind": "Level", "THRESHOLD": 5, "WindowMinutes": 3, "ALERTWHENBELOW": false, "LABEL": "Points" }""");

        Assert.Equal(new AlertRule(0, Points, AlertKind.Level, 5, 3, false, RuleOwner.You, "Points"), Assert.Single(read.Rules));
    }

    [Fact]
    public void ADuplicatedPropertyTakesTheLast()
    {
        // RoRoRo: System.Text.Json assigns each occurrence in order, so the last one written is the one it judges.
        var read = ReadRow($$"""{ "metricId": "{{Points}}", "kind": "Level", "threshold": 1, "threshold": 7 }""");

        Assert.Equal(7, Assert.Single(read.Rules).Threshold);
    }

    [Fact]
    public void ALabelThatIsNotTextSkipsTheRow()
    {
        // RoRoRo: Label is string?, so a number (or a flag, or an object) fails the row's Deserialize and the row is skipped.
        var read = ReadRow($$"""{ "metricId": "{{Points}}", "kind": "Level", "threshold": 1, "label": 5 }""");

        Assert.Empty(read.Rules);
        Assert.Equal(1, read.SkippedFor(Points));
    }

    [Fact]
    public void AThresholdTooBigForANumberReadsAsInfinity()
    {
        // RoRoRo: Threshold is double, and System.Text.Json reads 1e400 as positive infinity rather than refusing the row.
        var read = ReadRow($$"""{ "metricId": "{{Points}}", "kind": "Level", "threshold": 1e400 }""");

        Assert.Equal(double.PositiveInfinity, Assert.Single(read.Rules).Threshold);
    }

    [Fact]
    public void AnUnknownFieldIsIgnored()
    {
        // RoRoRo: RuleRow has no catch-all, and unmapped members are skipped by default, so a hand-added field costs nothing.
        var read = ReadRow($$"""{ "metricId": "{{Points}}", "kind": "Level", "threshold": 2, "note": { "why": [1, "two"] } }""");

        Assert.Equal(new AlertRule(0, Points, AlertKind.Level, 2, 0, true, RuleOwner.You, null), Assert.Single(read.Rules));
    }

    [Fact]
    public void KindMatchesIgnoringCase()
    {
        // RoRoRo: Enum.TryParse<MetricRuleKind>(kind, ignoreCase: true).
        var read = ReadRow($$"""{ "metricId": "{{Points}}", "kind": "lEvEl", "threshold": 2 }""");

        Assert.Equal(AlertKind.Level, Assert.Single(read.Rules).Kind);
    }
}
