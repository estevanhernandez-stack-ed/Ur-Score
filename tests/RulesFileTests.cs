using System.Text.Json;
using Labs626.UrScore.Core;

namespace UrScore.Tests;

public class RulesFileTests
{
    private const string MetricId = "clan.battle.points";

    private static string TempRules(string? contents)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "metric-rules.json");
        if (contents is not null) File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public void NoFileIsItsOwnState()
    {
        // Distinct from "a file with no rule for us", because the remedy differs: one needs a file
        // creating, the other needs a rule adding to a file the user already has.
        Assert.Equal(RuleState.NoFile, RulesFile.Inspect(TempRules(null), MetricId).State);
    }

    [Fact]
    public void AFileWithoutOurMetricIsItsOwnState()
    {
        var path = TempRules("""[ { "metricId": "something.else", "kind": "Level", "threshold": 1 } ]""");
        Assert.Equal(RuleState.NoRuleForMetric, RulesFile.Inspect(path, MetricId).State);
    }

    [Fact]
    public void AMistypedMetricIdDoesNotCrashInspectForTheRestOfTheFile()
    {
        // A hand-edited file can hold anything. GetValue<string> throws for a non-string value, so
        // a single mistyped row anywhere used to take down every call here, while the host's own
        // parser (LocalFileMetricRuleSource) tolerates the identical shape row by row and carries
        // on regardless of where in the file the bad row sits. We must be at least as forgiving as
        // the host on a file we do not own.
        var path = TempRules("""
            [
              { "metricId": 12345, "kind": "Event" },
              { "metricId": "other.one", "kind": "Event" }
            ]
            """);

        Assert.Equal(RuleState.NoRuleForMetric, RulesFile.Inspect(path, MetricId).State);
    }

    [Fact]
    public void ARuleWithNoOwnerIsTheUsersAndIsSaidSo()
    {
        // Hand-editing this file is a documented, supported path. A rule the user wrote is theirs,
        // and nothing here may touch it.
        var path = TempRules($$"""[ { "metricId": "{{MetricId}}", "kind": "Rate", "threshold": 100 } ]""");
        var status = RulesFile.Inspect(path, MetricId);

        Assert.Equal(RuleState.UserOwned, status.State);
        Assert.Null(status.Owner);
    }

    [Fact]
    public void ARuleOwnedByAnotherPluginIsLeftAloneAndNamed()
    {
        var path = TempRules($$"""
            [ { "metricId": "{{MetricId}}", "kind": "Rate", "threshold": 100, "owner": "someone.else" } ]
            """);
        var status = RulesFile.Inspect(path, MetricId);

        Assert.Equal(RuleState.OwnedByAnotherPlugin, status.State);
        Assert.Equal("someone.else", status.Owner);
    }

    [Fact]
    public void OurOwnRuleIsRecognised()
    {
        var path = TempRules(null);
        RulesFile.AddRule(path, MetricId, threshold: 100, windowMinutes: 10);

        var status = RulesFile.Inspect(path, MetricId);
        Assert.Equal(RuleState.OursIntact, status.State);
        Assert.Equal(RulesFile.Owner, status.Owner);
        Assert.Equal(100, status.Threshold);
    }

    [Fact]
    public void AddingPreservesEveryOtherRuleByteForByte()
    {
        // The file is shared. Clobbering someone's memory-warning rule while adding ours would be
        // the worst kind of bug: invisible until the alert they relied on stops arriving.
        var path = TempRules("""
            [
              { "metricId": "other.one", "kind": "Level", "threshold": 0.5, "alertWhenBelow": true },
              { "metricId": "other.two", "kind": "Event" }
            ]
            """);

        RulesFile.AddRule(path, MetricId, threshold: 100, windowMinutes: 10);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var ids = document.RootElement.EnumerateArray()
            .Select(r => r.GetProperty("metricId").GetString())
            .ToList();

        Assert.Equal(3, ids.Count);
        Assert.Contains("other.one", ids);
        Assert.Contains("other.two", ids);
        Assert.Contains(MetricId, ids);
    }

    [Fact]
    public void AddingNeverOverwritesARuleAlreadyThereForOurMetric()
    {
        // A threshold the user tuned by hand is theirs. Overwriting it is the one unrecoverable
        // thing this class could do.
        var path = TempRules($$"""[ { "metricId": "{{MetricId}}", "kind": "Rate", "threshold": 7 } ]""");

        var added = RulesFile.AddRule(path, MetricId, threshold: 100, windowMinutes: 10);

        Assert.False(added);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(7, document.RootElement[0].GetProperty("threshold").GetDouble());
        Assert.Single(document.RootElement.EnumerateArray());
    }

    [Fact]
    public void AddingBacksUpTheStateFromBeforeTheWrite()
    {
        // Asserting the backup does NOT contain our rule is the only thing that distinguishes
        // "backed up first" from "backed up last". The previous version asserted only that the
        // backup held the pre-existing rule, which stays true if the copy happens afterwards —
        // it passed with the backup moved below the write.
        var path = TempRules("""[ { "metricId": "other.one", "kind": "Event" } ]""");

        RulesFile.AddRule(path, MetricId, threshold: 100, windowMinutes: 10);

        var backup = File.ReadAllText(path + ".ur-score-backup");
        Assert.Contains("other.one", backup);
        Assert.DoesNotContain(MetricId, backup);
    }

    [Fact]
    public void TheRuleItWritesCarriesOurOwner()
    {
        // Provenance, not security. One file, several writers — this is how the next one can tell
        // whose rule is whose.
        var path = TempRules(null);
        RulesFile.AddRule(path, MetricId, threshold: 100, windowMinutes: 10);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("626labs.ur-score", document.RootElement[0].GetProperty("owner").GetString());
    }

    [Fact]
    public void TheRuleItWritesIsOneTheHostCanRead()
    {
        // The field names the host's own parser expects. A rule it cannot read is a rule that
        // never fires, and nothing anywhere would say so.
        var path = TempRules(null);
        RulesFile.AddRule(path, MetricId, threshold: 100, windowMinutes: 10);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var rule = document.RootElement[0];

        Assert.Equal(MetricId, rule.GetProperty("metricId").GetString());
        Assert.Equal("Rate", rule.GetProperty("kind").GetString());
        Assert.Equal(100, rule.GetProperty("threshold").GetDouble());
        Assert.Equal(10, rule.GetProperty("windowMinutes").GetInt32());
    }

    [Fact]
    public void APreviewIsWhatWillActuallyBeWritten()
    {
        // The preview is the consent. If it differs from the write, the click was uninformed.
        var path = TempRules(null);
        var preview = RulesFile.Preview(MetricId, threshold: 100, windowMinutes: 10);

        RulesFile.AddRule(path, MetricId, threshold: 100, windowMinutes: 10);

        using var written = JsonDocument.Parse(File.ReadAllText(path));
        using var previewed = JsonDocument.Parse($"[{preview}]");

        Assert.Equal(
            written.RootElement[0].GetProperty("owner").GetString(),
            previewed.RootElement[0].GetProperty("owner").GetString());
        Assert.Equal(
            written.RootElement[0].GetProperty("threshold").GetDouble(),
            previewed.RootElement[0].GetProperty("threshold").GetDouble());
    }

    [Fact]
    public void AnUnreadableFileIsNeverOverwritten()
    {
        // Invalid JSON here probably means a half-finished hand edit. Replacing it would destroy
        // work in progress; refusing and saying so costs the user one look at the file.
        var path = TempRules("{ not json");

        Assert.Equal(RuleState.Unreadable, RulesFile.Inspect(path, MetricId).State);
        Assert.False(RulesFile.AddRule(path, MetricId, threshold: 100, windowMinutes: 10));
        Assert.Equal("{ not json", File.ReadAllText(path));
    }
}
