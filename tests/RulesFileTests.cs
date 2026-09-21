using System.Text.Json;
using Labs626.UrScore.Core;

namespace UrScore.Tests;

/// <summary>
/// RoRoRo's rules file is shared and hand-editable. These tests pin the three promises the design makes about it: every
/// rule Ur Score doesn't own is kept, the file is backed up before each write, and a file that isn't a readable list is
/// never touched. They also pin that a rule is read exactly as RoRoRo's parser reads it (plan A2).
/// </summary>
public sealed class RulesFileTests : IDisposable
{
    private const string Points = "clan.battle.points";

    private static readonly AlertSpec Stops = new(AlertKind.Rate, 100, 10, AlertWhenBelow: true, "Points");
    private static readonly AlertSpec Crosses = new(AlertKind.Level, 40, 0, AlertWhenBelow: false, "Rank");

    private readonly TempDir.Scope _dir = TempDir.Create("urscore-rules");

    public void Dispose() => _dir.Dispose();

    private string Rules(string? contents = null)
    {
        var path = Path.Combine(_dir.Path, "metric-rules.json");
        if (contents is not null) File.WriteAllText(path, contents);
        return path;
    }

    private static JsonElement[] Rows(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return [.. document.RootElement.EnumerateArray().Select(r => r.Clone())];
    }

    [Fact]
    public void NoFileHasNoRulesAndNoProblem()
    {
        var read = RulesFile.Read(Rules());

        Assert.Equal(RulesProblem.None, read.Problem);
        Assert.False(read.Exists);
        Assert.Empty(read.Rules);
    }

    [Fact]
    public void EveryRuleForAMetricIsListedWithItsKindOwnerAndLabel()
    {
        var path = Rules($$"""
            [
              { "metricId": "{{Points}}", "kind": "Rate", "threshold": 100, "windowMinutes": 10, "alertWhenBelow": true, "owner": "626labs.ur-score" },
              { "metricId": "other.metric", "kind": "Event" },
              { "MetricId": "{{Points}}", "KIND": "level", "Threshold": 5000, "alertWhenBelow": false },
              { "metricId": "{{Points}}", "kind": "Level", "threshold": 3, "owner": "someone.else", "label": "  Points  " },
              { "metricId": "{{Points}}", "kind": "event", "owner": "", "label": null },
              { "metricId": "{{Points}}", "kind": "Rate", "threshold": 250, "windowMinutes": 15, "owner": "626labs.ur-score", "label": "Points" }
            ]
            """);

        var read = RulesFile.Read(path);

        Assert.Equal(
            new[]
            {
                new AlertRule(0, Points, AlertKind.Rate, 100, 10, true, RuleOwner.UrScore, null),
                new AlertRule(2, Points, AlertKind.Level, 5000, 0, false, RuleOwner.You, null),
                new AlertRule(3, Points, AlertKind.Level, 3, 0, true, RuleOwner.AnotherPlugin, "Points"),
                new AlertRule(4, Points, AlertKind.Event, 0, 0, true, RuleOwner.You, null),
                new AlertRule(5, Points, AlertKind.Rate, 250, 15, true, RuleOwner.UrScore, "Points"),
            },
            read.For(Points));
        Assert.Equal(new AlertRule(0, Points, AlertKind.Rate, 100, 10, true, RuleOwner.UrScore, null), read.OursFor(Points, AlertKind.Rate));
        Assert.Null(read.OursFor(Points, AlertKind.Level));
    }

    [Fact]
    public void ARowRoRoRoWouldSkipCostsOnlyItselfAndIsCounted()
    {
        // RoRoRo reads a label as text: a label that is a number, a flag or an object makes it skip the whole row, and so does
        // a field whose earlier spelling has the wrong type even when a later one is right.
        var path = Rules($$"""
            [
              "not a rule",
              { "metricId": 12345, "kind": "Rate" },
              { "kind": "Rate", "threshold": 1 },
              { "metricId": "{{Points}}", "kind": "Telepathy", "threshold": 1 },
              { "metricId": "{{Points}}", "kind": "Rate", "threshold": "100" },
              { "metricId": "{{Points}}", "kind": "Rate", "threshold": null, "windowMinutes": 5 },
              { "metricId": "{{Points}}", "kind": "Level", "threshold": 1, "alertWhenBelow": "yes" },
              { "metricId": "{{Points}}", "kind": "Level", "threshold": 1, "label": 5 },
              { "metricId": "{{Points}}", "kind": "Level", "threshold": 1, "label": true },
              { "metricId": "{{Points}}", "kind": "Level", "threshold": 1, "label": { "text": "Points" } },
              { "metricId": "{{Points}}", "kind": "Rate", "threshold": "100", "Threshold": 100, "windowMinutes": 10 },
              { "metricId": "{{Points}}", "kind": "Rate", "threshold": 100, "windowMinutes": 10, "owner": "626labs.ur-score" }
            ]
            """);

        var read = RulesFile.Read(path);

        Assert.Equal(RulesProblem.None, read.Problem);
        Assert.Equal(11, Assert.Single(read.For(Points)).Index);
        Assert.Equal(8, read.SkippedFor(Points));
    }

    [Fact]
    public void AFieldWrittenTwiceReadsAsItsLastOccurrenceAsRoRoRoReadsIt()
    {
        // RoRoRo matches names ignoring case and the last one written wins, so the sentence must show the number it judges.
        var path = Rules($$"""
            [
              { "metricId": "other.metric", "MetricId": "{{Points}}", "kind": "Level", "KIND": "Rate", "threshold": 1, "Threshold": 5,
                "windowMinutes": 10, "WINDOWMINUTES": 20, "alertWhenBelow": false, "AlertWhenBelow": true,
                "owner": "someone.else", "Owner": "626labs.ur-score", "label": "First", "Label": "Last" },
              { "metricId": "{{Points}}", "kind": "Level", "threshold": 3, "threshold": 7, "owner": "626labs.ur-score" }
            ]
            """);

        var read = RulesFile.Read(path);

        Assert.Empty(read.For("other.metric"));
        Assert.Equal(
            new[]
            {
                new AlertRule(0, Points, AlertKind.Rate, 5, 20, true, RuleOwner.UrScore, "Last"),
                new AlertRule(1, Points, AlertKind.Level, 7, 0, true, RuleOwner.UrScore, null),
            },
            read.For(Points));

        // A row with a name written twice is kept as it is by a write, and Change can still rewrite one.
        Assert.Equal(RuleWrite.Done, RulesFile.Change(path, Points, Crosses));
        Assert.Equal(
            new[]
            {
                new AlertRule(0, Points, AlertKind.Rate, 5, 20, true, RuleOwner.UrScore, "Last"),
                new AlertRule(1, Points, AlertKind.Level, 40, 0, false, RuleOwner.UrScore, "Rank"),
            },
            RulesFile.Read(path).For(Points));
    }

    [Theory]
    [InlineData("{ not json", RulesProblem.NotJson, RuleWrite.NotJson)]
    [InlineData("", RulesProblem.NotJson, RuleWrite.NotJson)]
    [InlineData("""{ "metricId": "clan.battle.points" }""", RulesProblem.NotAList, RuleWrite.NotAList)]
    public void AFileThatIsNotAListOfRulesIsNeverWritten(string contents, RulesProblem problem, RuleWrite refused)
    {
        // Invalid JSON here is more likely a half-finished hand edit than corruption: replacing it would destroy work.
        var path = Rules(contents);

        Assert.Equal(problem, RulesFile.Read(path).Problem);
        Assert.Equal(refused, RulesFile.TurnOn(path, Points, Stops));
        Assert.Equal(refused, RulesFile.Change(path, Points, Stops));
        Assert.Equal(refused, RulesFile.Remove(path, Points, AlertKind.Rate));
        Assert.Equal(contents, File.ReadAllText(path));
        Assert.False(File.Exists(path + RulesFile.BackupSuffix));
    }

    [Fact]
    public void AFileSomeoneHoldsOpenCantBeReadAndNothingChanges()
    {
        var path = Rules("[]");
        File.WriteAllText(path + RulesFile.BackupSuffix, "an older backup");
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(RulesProblem.CantOpen, RulesFile.Read(path).Problem);
            Assert.Equal(RuleWrite.CantOpen, RulesFile.TurnOn(path, Points, Stops));
        }

        Assert.Equal("[]", File.ReadAllText(path));
        Assert.Equal("an older backup", File.ReadAllText(path + RulesFile.BackupSuffix));
    }

    [Fact]
    public void AFileThatCanBeReadButNotReplacedSaysSoAndChangesNothing()
    {
        // The swap itself fails here (the file can be read but not replaced). The previous backup is the only undo point for
        // the last write that did land, so a failed write must leave it exactly as it was, not overwrite it.
        var before = $$"""[ { "metricId": "{{Points}}", "kind": "Rate", "threshold": 100, "windowMinutes": 10, "owner": "626labs.ur-score" } ]""";
        var path = Rules(before);
        File.WriteAllText(path + RulesFile.BackupSuffix, "an older backup");
        var bytes = File.ReadAllBytes(path);
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(RuleWrite.CantWrite, RulesFile.Change(path, Points, Stops with { Threshold = 250 }));
        }

        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal("an older backup", File.ReadAllText(path + RulesFile.BackupSuffix));
        Assert.False(File.Exists(path + ".ur-score-writing"));
    }

    [Fact]
    public void ABackupSomeoneHoldsOpenFailsTheWriteAndChangesNothing()
    {
        var path = Rules($$"""[ { "metricId": "{{Points}}", "kind": "Rate", "threshold": 100, "windowMinutes": 10, "owner": "626labs.ur-score" } ]""");
        File.WriteAllText(path + RulesFile.BackupSuffix, "an older backup");
        var bytes = File.ReadAllBytes(path);
        using (new FileStream(path + RulesFile.BackupSuffix, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(RuleWrite.CantWrite, RulesFile.Change(path, Points, Stops with { Threshold = 250 }));
        }

        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal("an older backup", File.ReadAllText(path + RulesFile.BackupSuffix));
        Assert.False(File.Exists(path + ".ur-score-writing"));
    }

    [Fact]
    public void TurningOnAStopsClimbingAlertWritesARuleRoRoRoReadsWithOwnerAndLabel()
    {
        var path = Path.Combine(_dir.Path, "ROROROblox", "metric-rules.json");

        Assert.Equal(RuleWrite.Done, RulesFile.TurnOn(path, Points, Stops));

        var rule = Assert.Single(Rows(path));
        Assert.Equal(
            new[] { "metricId", "kind", "threshold", "windowMinutes", "alertWhenBelow", "owner", "label" },
            rule.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(Points, rule.GetProperty("metricId").GetString());
        Assert.Equal("Rate", rule.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Number, rule.GetProperty("threshold").ValueKind);
        Assert.Equal(100, rule.GetProperty("threshold").GetDouble());
        Assert.Equal(10, rule.GetProperty("windowMinutes").GetDouble());
        Assert.True(rule.GetProperty("alertWhenBelow").GetBoolean());
        Assert.Equal("626labs.ur-score", rule.GetProperty("owner").GetString());
        Assert.Equal("Points", rule.GetProperty("label").GetString());
        Assert.Equal(new[] { "metric-rules.json" }, Directory.GetFiles(Path.GetDirectoryName(path)!).Select(f => Path.GetFileName(f)).ToArray());
    }

    [Fact]
    public void TurningOnACrossesANumberAlertWritesItsDirectionAndNoWindow()
    {
        var path = Rules("[]");

        Assert.Equal(RuleWrite.Done, RulesFile.TurnOn(path, "ps99.rank", Crosses));

        var rule = Assert.Single(Rows(path));
        Assert.Equal("Level", rule.GetProperty("kind").GetString());
        Assert.Equal(40, rule.GetProperty("threshold").GetDouble());
        Assert.False(rule.GetProperty("alertWhenBelow").GetBoolean());
        Assert.False(rule.TryGetProperty("windowMinutes", out _));
        Assert.Equal("Rank", rule.GetProperty("label").GetString());
        Assert.Equal(new AlertRule(0, "ps99.rank", AlertKind.Level, 40, 0, false, RuleOwner.UrScore, "Rank"), Assert.Single(RulesFile.Read(path).Rules));
    }

    [Fact]
    public void TurningOnKeepsEveryOtherRuleAndEveryFieldItHas()
    {
        // The file is shared: clobbering someone's memory-warning rule while adding ours would be invisible until the alert
        // they relied on stopped arriving. A rule you wrote for the same stat doesn't stop Ur Score adding its own (A5).
        var path = Rules($$"""
            [
              { "metricId": "memory.warning", "kind": "Level", "threshold": 0.5, "alertWhenBelow": true, "note": "mine" },
              { "metricId": "{{Points}}", "kind": "Rate", "threshold": 7, "windowMinutes": 30 },
              { "metricId": "{{Points}}", "kind": "Level", "threshold": 9, "owner": "someone.else", "extra": [1, 2] }
            ]
            """);

        Assert.Equal(RuleWrite.Done, RulesFile.TurnOn(path, Points, Stops));

        var rows = Rows(path);
        Assert.Equal(4, rows.Length);
        Assert.Equal("mine", rows[0].GetProperty("note").GetString());
        Assert.Equal(0.5, rows[0].GetProperty("threshold").GetDouble());
        Assert.Equal(7, rows[1].GetProperty("threshold").GetDouble());
        Assert.False(rows[1].TryGetProperty("owner", out _));
        Assert.Equal("[1,2]", JsonSerializer.Serialize(rows[2].GetProperty("extra")));
        Assert.Equal("626labs.ur-score", rows[3].GetProperty("owner").GetString());
    }

    [Fact]
    public void AStatHasOneOfEachKindFromUrScore()
    {
        var path = Rules("[]");
        Assert.Equal(RuleWrite.Done, RulesFile.TurnOn(path, Points, Stops));
        var after = File.ReadAllText(path);

        Assert.Equal(RuleWrite.AlreadyThere, RulesFile.TurnOn(path, Points, Stops with { Threshold = 5 }));
        Assert.Equal(after, File.ReadAllText(path));

        Assert.Equal(RuleWrite.Done, RulesFile.TurnOn(path, Points, Crosses));
        Assert.Equal(new[] { AlertKind.Rate, AlertKind.Level }, RulesFile.Read(path).For(Points).Select(r => r.Kind).ToArray());
    }

    [Fact]
    public void ChangeRewritesUrScoresRuleInPlaceAndLabelsTheRuleVersion031Added()
    {
        var path = Rules($$"""
            [
              { "metricId": "memory.warning", "kind": "Event" },
              { "metricId": "{{Points}}", "kind": "Rate", "threshold": 100, "windowMinutes": 10, "alertWhenBelow": true, "owner": "626labs.ur-score" },
              { "metricId": "{{Points}}", "kind": "Rate", "threshold": 7, "windowMinutes": 30 }
            ]
            """);

        Assert.Equal(RuleWrite.Done, RulesFile.Change(path, Points, Stops with { Threshold = 250, WindowMinutes = 15 }));

        var read = RulesFile.Read(path);
        Assert.Equal(3, read.Rules.Count);
        Assert.Equal("memory.warning", read.Rules[0].MetricId);
        Assert.Equal(new AlertRule(1, Points, AlertKind.Rate, 250, 15, true, RuleOwner.UrScore, "Points"), read.OursFor(Points, AlertKind.Rate));
        Assert.Equal(new AlertRule(2, Points, AlertKind.Rate, 7, 30, true, RuleOwner.You, null), read.Rules[2]);
    }

    [Fact]
    public void AChangeThatChangesNothingWritesNothing()
    {
        // Rewriting an identical rule would still drop the file's comments and formatting, and replace the only backup
        // with a copy of the same rules.
        var before = $$"""
            // Tuned by hand.
            [
              { "metricId": "memory.warning", "kind": "Event" },
              { "metricId": "{{Points}}", "kind": "Rate", "threshold": 100, "windowMinutes": 10, "alertWhenBelow": true, "owner": "626labs.ur-score", "label": "Points" },
            ]
            """;
        var path = Rules(before);
        File.WriteAllText(path + RulesFile.BackupSuffix, "an older backup");
        var bytes = File.ReadAllBytes(path);

        Assert.Equal(RuleWrite.Done, RulesFile.Change(path, Points, Stops));

        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal("an older backup", File.ReadAllText(path + RulesFile.BackupSuffix));
        Assert.False(File.Exists(path + ".ur-score-writing"));
    }

    [Fact]
    public void ChangeAndRemoveNeverTouchARuleUrScoreDoesNotOwn()
    {
        var before = $$"""
            [
              { "metricId": "{{Points}}", "kind": "Rate", "threshold": 7, "windowMinutes": 30 },
              { "metricId": "{{Points}}", "kind": "Level", "threshold": 9, "owner": "someone.else" }
            ]
            """;
        var path = Rules(before);

        Assert.Equal(RuleWrite.NotThere, RulesFile.Change(path, Points, Stops));
        Assert.Equal(RuleWrite.NotThere, RulesFile.Remove(path, Points, AlertKind.Rate));
        Assert.Equal(RuleWrite.NotThere, RulesFile.Remove(path, Points, AlertKind.Level));
        Assert.Equal(before, File.ReadAllText(path));
        Assert.False(File.Exists(path + RulesFile.BackupSuffix));
    }

    [Fact]
    public void ChangeAndRemoveWithNoFileCreateNothing()
    {
        var path = Rules();

        Assert.Equal(RuleWrite.NotThere, RulesFile.Change(path, Points, Stops));
        Assert.Equal(RuleWrite.NotThere, RulesFile.Remove(path, Points, AlertKind.Rate));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void RemoveDeletesExactlyTheFirstUrScoreRuleOfThatKind()
    {
        var path = Rules($$"""
            [
              { "metricId": "{{Points}}", "kind": "Rate", "threshold": 7, "windowMinutes": 30 },
              { "metricId": "{{Points}}", "kind": "Rate", "threshold": 100, "windowMinutes": 10, "owner": "626labs.ur-score" },
              { "metricId": "{{Points}}", "kind": "Level", "threshold": 40, "owner": "626labs.ur-score" },
              { "metricId": "{{Points}}", "kind": "Rate", "threshold": 200, "windowMinutes": 10, "owner": "626labs.ur-score" }
            ]
            """);

        Assert.Equal(RuleWrite.Done, RulesFile.Remove(path, Points, AlertKind.Rate));

        var read = RulesFile.Read(path);
        Assert.Equal(new[] { 7d, 40d, 200d }, read.Rules.Select(r => r.Threshold).ToArray());
        Assert.Equal(new AlertRule(2, Points, AlertKind.Rate, 200, 10, true, RuleOwner.UrScore, null), read.OursFor(Points, AlertKind.Rate));
    }

    [Fact]
    public void EveryWriteBacksUpTheFileAsItWasJustBefore()
    {
        // Asserting the backup does NOT hold the new rule is what tells "backed up first" from "backed up last". Comparing bytes
        // shows the backup is the file itself, comments and formatting included, not a re-serialised copy.
        var path = Rules("""
            // Mine, by hand.
            [ { "metricId": "memory.warning", "kind": "Event" }, ]
            """);
        var beforeFirst = File.ReadAllBytes(path);

        Assert.Equal(RuleWrite.Done, RulesFile.TurnOn(path, Points, Stops));
        Assert.Equal(beforeFirst, File.ReadAllBytes(path + RulesFile.BackupSuffix));
        Assert.DoesNotContain(Points, File.ReadAllText(path + RulesFile.BackupSuffix));

        var beforeSecond = File.ReadAllBytes(path);
        Assert.Equal(RuleWrite.Done, RulesFile.TurnOn(path, Points, Crosses));
        Assert.Equal(beforeSecond, File.ReadAllBytes(path + RulesFile.BackupSuffix));
        Assert.False(File.Exists(path + ".ur-score-writing"));
    }

    [Fact]
    public void ChangingAManagedLabelLeavesTheBackupAsItWas()
    {
        // The backup is per FILE, not per rule (see EveryWriteBacksUpTheFileAsItWasJustBefore). An automatic
        // rewrite of a label Ur Score maintains must not spend the owner's undo point, or two background
        // rewrites in a row would push a hand-typed rule out of the backup with the owner never having
        // touched Setup themselves (the owner's ruling of 2026-09-20).
        var path = Rules($$"""
            [ { "metricId": "{{Points}}", "kind": "Level", "threshold": 40, "alertWhenBelow": false, "owner": "626labs.ur-score", "label": "first" } ]
            """);

        // Contrast first: an ordinary Change DOES replace the backup, exactly as every other write does.
        File.WriteAllText(path + RulesFile.BackupSuffix, "an older backup");
        Assert.Equal(RuleWrite.Done, RulesFile.Change(path, Points, Crosses with { Label = "second" }));
        Assert.NotEqual("an older backup", File.ReadAllText(path + RulesFile.BackupSuffix));

        // Now the managed path: it changes only the label, and the sentinel backup below survives untouched.
        File.WriteAllText(path + RulesFile.BackupSuffix, "an older backup");
        Assert.Equal(RuleWrite.Done, RulesFile.ChangeLabel(path, Points, AlertKind.Level, "third"));

        Assert.Equal("third", RulesFile.Read(path).OursFor(Points, AlertKind.Level)?.Label);
        Assert.Equal("an older backup", File.ReadAllText(path + RulesFile.BackupSuffix));
    }

    [Fact]
    public void ChangeLabelOnARowWithADuplicateKeyRefusesRatherThanCrashing()
    {
        // System.Text.Json.Nodes.JsonObject builds its property lookup lazily, on first indexer access, and that
        // build throws ArgumentException the instant it finds ANY duplicate key in the object — even one that has
        // nothing to do with the field being touched. Change never hits this because it replaces the whole row
        // (a JsonArray index assignment, not a JsonObject property access); ChangeLabel edits one field of the
        // existing row on purpose, so it must not let that internal exception escape uncaught to a background
        // caller nothing is watching.
        var path = Rules($$"""
            [ { "metricId": "{{Points}}", "kind": "Level", "threshold": 40, "threshold": 40, "owner": "626labs.ur-score", "label": "first" } ]
            """);
        var before = File.ReadAllBytes(path);

        Assert.Equal(RuleWrite.CantWrite, RulesFile.ChangeLabel(path, Points, AlertKind.Level, "second"));

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + RulesFile.BackupSuffix));
        Assert.False(File.Exists(path + ".ur-score-writing"));
    }

    [Fact]
    public void ChangeLabelOnARowWithACaseVariantLabelDuplicateRefusesRatherThanNamingTheWrongClan()
    {
        // RulesFile.LastText and RoRoRo's own parser both match field names ignoring case and keep the LAST one
        // written. JsonObject's indexer is case-SENSITIVE, so writing "label" on a row that also carries "Label"
        // lands on one key while the reader keeps reading the other — silently, with no exception, unlike the
        // exact-duplicate case above. Nothing here throws, which is exactly why it needs its own guard: a rewrite
        // that "succeeds" but doesn't take effect would leave RoRoRo naming the PREVIOUS chaser on someone's phone
        // mid-battle. Refusing is the safe failure: no label written means no push at all (the no-label-no-send
        // rule), silence rather than a wrong name (the owner's ruling of 2026-09-20).
        var path = Rules($$"""
            [ { "metricId": "{{Points}}", "kind": "Level", "threshold": 40, "alertWhenBelow": false, "owner": "626labs.ur-score", "label": "first", "Label": "first-dup" } ]
            """);
        var before = File.ReadAllBytes(path);
        File.WriteAllText(path + RulesFile.BackupSuffix, "an older backup");

        Assert.Equal(RuleWrite.CantWrite, RulesFile.ChangeLabel(path, Points, AlertKind.Level, "second"));

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal("an older backup", File.ReadAllText(path + RulesFile.BackupSuffix));
        Assert.False(File.Exists(path + ".ur-score-writing"));
    }

    [Fact]
    public void ABlankMetricIdOrAnEventKindIsRefusedBeforeAnyFileIsTouched()
    {
        var path = Rules();

        Assert.Throws<ArgumentException>(() => RulesFile.TurnOn(path, "  ", Stops));
        Assert.Throws<ArgumentException>(() => RulesFile.Change(path, Points, Stops with { Kind = AlertKind.Event }));
        Assert.Throws<ArgumentException>(() => RulesFile.Remove(path, "", AlertKind.Rate));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void TheRulesFileIsRoRoRosUnlessAFullPathOverridesIt()
    {
        var roRoRo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ROROROblox", "metric-rules.json");
        var scratch = Path.Combine(_dir.Path, "metric-rules.json");

        Assert.Equal(roRoRo, RulesFile.DefaultPath);
        Assert.Equal(roRoRo, RulesFile.ResolvePath(null));
        Assert.Equal(roRoRo, RulesFile.ResolvePath("   "));
        Assert.Equal(roRoRo, RulesFile.ResolvePath("metric-rules.json"));
        Assert.Equal(scratch, RulesFile.ResolvePath($"  {scratch} "));
        Assert.Equal(roRoRo, RulesFile.ResolvePath(Path.GetPathRoot(scratch)));
        Assert.Equal(roRoRo, RulesFile.ResolvePath(_dir.Path + Path.DirectorySeparatorChar));
        Assert.Equal("UR_SCORE_RULES_FILE", RulesFile.PathVariable);
    }

    [Fact]
    public void APathThatNamesNoFileIsRefusedWithoutThrowing()
    {
        // A drive root, or a folder ending in a separator, names no file to write: that must come back as a result, not an
        // unhandled throw from the folder it would create.
        // Safe to aim at the real drive root: it is refused before any file is opened or created.
        var root = Path.GetPathRoot(_dir.Path)!;
        var folder = _dir.Path + Path.DirectorySeparatorChar;

        Assert.Equal(RuleWrite.CantWrite, RulesFile.TurnOn(root, Points, Stops));
        Assert.False(File.Exists(root + ".ur-score-writing"));
        Assert.Equal(RuleWrite.CantWrite, RulesFile.TurnOn(folder, Points, Stops));
        Assert.Empty(Directory.GetFileSystemEntries(_dir.Path));
    }
}
