using Labs626.UrScore.Composition;
using Labs626.UrScore.Core;

namespace UrScore.Tests;

/// <summary>
/// <see cref="AppServices.LabelInPlace"/> is the mapping <c>RecipeWatch</c>'s <c>writeLabel</c> seam calls
/// through (task 8's controller ruling): it decides, from what <see cref="RulesFile.ChangeLabel"/> reports,
/// whether a threat number may be sent under the name just written. Nothing else on the branch exercises the
/// no-label-no-send half of the ordering invariant (design §1) until this mapping exists, so every
/// <see cref="RuleWrite"/> case is pinned here, plus one test that drives a real refusal through
/// <see cref="RulesFile.ChangeLabel"/> rather than a stub.
/// </summary>
public sealed class AppServicesTests : IDisposable
{
    private const string Points = "clan.battle.points";

    private readonly TempDir.Scope _dir = TempDir.Create("urscore-appservices");

    public void Dispose() => _dir.Dispose();

    private string Rules(string contents)
    {
        var path = Path.Combine(_dir.Path, "metric-rules.json");
        File.WriteAllText(path, contents);
        return path;
    }

    [Theory]
    [InlineData(RuleWrite.Done, true)]
    [InlineData(RuleWrite.NotThere, true)]
    [InlineData(RuleWrite.AlreadyThere, true)]
    [InlineData(RuleWrite.CantWrite, false)]
    [InlineData(RuleWrite.CantOpen, false)]
    [InlineData(RuleWrite.NotJson, false)]
    [InlineData(RuleWrite.NotAList, false)]
    public void EachRuleWriteMapsToWhetherTheNumberMayBeSent(RuleWrite write, bool expected) =>
        Assert.Equal(expected, AppServices.LabelInPlace(write));

    [Fact]
    public void ARealRefusalFromChangeLabelMapsToFalse()
    {
        // The previous task's CantWrite test used a stub (_, _) => false, which pins nothing about the real
        // write path. This drives RulesFile.ChangeLabel itself against a row it can only refuse: a duplicate
        // "threshold" key makes JsonObject's lazy property lookup throw the instant ChangeLabel touches the row
        // (see RulesFileTests.ChangeLabelOnARowWithADuplicateKeyRefusesRatherThanCrashing), so ChangeLabel
        // reports CantWrite rather than let that exception escape to a background caller nothing is watching.
        var path = Rules($$"""
            [ { "metricId": "{{Points}}", "kind": "Level", "threshold": 40, "threshold": 40, "owner": "626labs.ur-score", "label": "first" } ]
            """);

        var write = RulesFile.ChangeLabel(path, Points, AlertKind.Level, "second");

        Assert.Equal(RuleWrite.CantWrite, write);
        Assert.False(AppServices.LabelInPlace(write));
    }

    /// <summary>
    /// <see cref="AppServices.WriteLabel(string, FieldMetric, string)"/> is the whole composition the watch's
    /// <c>writeLabel</c> seam runs: it binds a metric's <see cref="FieldMetric.MetricId"/> and
    /// <see cref="AlertKind.Level"/> to <see cref="RulesFile.ChangeLabel"/>, and answers through
    /// <see cref="AppServices.LabelInPlace"/>. Until the final review of this branch nothing called it, which is
    /// why the decoy row below is here: a rule under the metric's KEY rather than its id, owned by Ur Score and
    /// of the same kind. Bind the wrong one of the two and this file's real rule keeps its stale label while
    /// <see cref="RuleWrite.NotThere"/> still answers true — a threat number under last hour's chaser's name,
    /// silently and for good.
    /// </summary>
    [Fact]
    public void WritingAThreatMetricsLabelRenamesOurOwnLevelRuleForThatMetricId()
    {
        var metric = FieldMetrics.Find(FieldMetrics.ThreatGap)!;
        var path = Rules($$"""
            [
              { "metricId": "{{metric.Key}}", "kind": "Level", "threshold": 1, "owner": "626labs.ur-score", "label": "not this row" },
              { "metricId": "memory.warning", "kind": "Event" },
              { "metricId": "{{metric.MetricId}}", "kind": "Level", "threshold": 250000000, "alertWhenBelow": false, "owner": "626labs.ur-score", "label": "LXCC catching K0i2" }
            ]
            """);

        Assert.True(AppServices.WriteLabel(path, metric, "H8ER catching K0i2"));

        var read = RulesFile.Read(path);
        Assert.Equal("H8ER catching K0i2", read.OursFor(metric.MetricId, AlertKind.Level)?.Label);
        Assert.Equal("not this row", read.OursFor(metric.Key, AlertKind.Level)?.Label);
    }
}
