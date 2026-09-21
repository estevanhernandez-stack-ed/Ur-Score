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
}
