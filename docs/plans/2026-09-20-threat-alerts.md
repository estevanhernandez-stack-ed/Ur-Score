# Threat Alerts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Alert the owner, on their phone and by name, when a clan behind them is about to take their place.

**Architecture:** Two stages. Stage A gives Ur Score a named capability — a rule whose label it maintains — with the host's ordering rule enforced in one place and a carve-out so automatic rewrites never eat the rules file's backup. Stage B computes the threat from the live read and writes the name onto the label before the number is reported.

**Tech Stack:** C# 13 / .NET 10, WPF, xUnit. No new dependencies.

**Spec:** `docs/2026-09-20-threat-alerts-design.md` — read it first, especially §1 (what the host allows) and §4 (track by name, and do not name the wrong one).

## Global Constraints

- Branch is `master`. Work happens on `fix/one-meaning-of-your-clans` unless told otherwise. Never tag; releasing is the owner's call.
- Build: `dotnet build Ur-Score.csproj -c Release -warnaserror`. Test: `dotnet test tests/Ur-Score.Tests.csproj -c Release`. The test project cannot be built standalone. Compare test counts against the previous run; do not trust a green tick alone.
- **Ordering rule, from the host's code:** write the label, THEN report the number. The host binds a rule's label at the observation, not at the push. Never report-then-rename.
- **No label, no send.** If the label cannot be written, the number is not reported. A threat number under a stale name is a confident false statement, which is worse than silence.
- Metric ids are FIXED and shared across the clan. Only labels are local.
- Every popup themed; no stock `MessageBox`. Built UI is screenshot-compared before it is called done.
- A named threat must be the soonest by TIME, never the nearest by place.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/Core/FieldMetrics.cs` | Metric catalogue; gains `ManagedLabel`, the two threat ids, and the threat computation. |
| `src/Book/FieldSummary.cs` | Gains `Behind`, the clans below yours from one live read. |
| `src/Core/RulesFile.cs` | Gains `ChangeLabel`, the managed-label write that preserves the backup. |
| `src/Core/RecipeWatch.cs` | Keeps per-name series; writes the label before reporting. |
| `src/UI/Setup/AlertCards.cs` | Shows a managed label as live and not editable. |

---

### Task 1: A metric can declare that Ur Score maintains its label

**Files:**
- Modify: `src/Core/FieldMetrics.cs:9`
- Test: `tests/FieldMetricsTests.cs`

**Interfaces:**
- Produces: `FieldMetric(string Key, string Label, string MetricId, string What, bool ManagedLabel = false)`.

- [ ] **Step 1: Write the failing test**

```csharp
/// <summary>
/// A label Ur Score computes and a label a human typed are different kinds of thing, and the catalogue says
/// which. Every metric shipped before threats is a human's to word; nothing silently becomes managed.
/// </summary>
[Fact]
public void OnlyMetricsThatSaySoHaveALabelUrScoreMaintains()
{
    Assert.All(FieldMetrics.All, m => Assert.False(m.ManagedLabel));
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --filter "FullyQualifiedName~OnlyMetricsThatSaySo"`
Expected: FAIL to compile — `FieldMetric` has no `ManagedLabel`.

- [ ] **Step 3: Add the flag**

```csharp
/// <param name="ManagedLabel">
/// Ur Score writes this number's label and keeps it current; a human never types it. The marker lives here
/// rather than in rules.json because it is nobody else's business: the host's RuleRow has no such field and
/// never reads one.
/// </param>
public sealed record FieldMetric(string Key, string Label, string MetricId, string What, bool ManagedLabel = false);
```

- [ ] **Step 4: Run it and watch it pass**

Expected: PASS. Run the whole suite and compare the count with the previous run.

- [ ] **Step 5: Commit**

```bash
git add src/Core/FieldMetrics.cs tests/FieldMetricsTests.cs
git commit -m "feat: a metric can declare that Ur Score maintains its label"
```

---

### Task 2: A managed-label write leaves the backup alone

**Files:**
- Modify: `src/Core/RulesFile.cs:127` (beside `Change`), `src/Core/RulesFile.cs:320` (`Write`)
- Test: `tests/RulesFileTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `RulesFile.ChangeLabel(string path, string metricId, AlertKind kind, string label) -> RuleWrite`.

The backup is per FILE, not per rule: `File.Replace(temp, path, path + BackupSuffix)` swaps the whole file's
undo point on every successful write. Automatic rewrites would therefore consume the undo point for rules the
owner typed by hand, which is the carve-out the owner ruled for on 2026-09-20.

- [ ] **Step 1: Write the failing test**

```csharp
/// <summary>
/// An automatic rewrite must not spend the owner's undo point. The backup is the whole file's, so a label
/// Ur Score rewrites on its own would otherwise push a hand-typed rule out of the backup within two writes.
/// </summary>
[Fact]
public void ChangingAManagedLabelLeavesTheBackupAsItWas()
{
    var path = NewRulesFile(Rule("clan.standing.threat-hours", label: "first"));

    Assert.Equal(RuleWrite.Done, RulesFile.Change(path, "clan.standing.threat-hours", Spec("second")));
    var backupAfterOrdinaryWrite = File.ReadAllText(path + RulesFile.BackupSuffix);

    Assert.Equal(RuleWrite.Done, RulesFile.ChangeLabel(path, "clan.standing.threat-hours", AlertKind.Below, "third"));

    Assert.Contains("third", File.ReadAllText(path), StringComparison.Ordinal);
    Assert.Equal(backupAfterOrdinaryWrite, File.ReadAllText(path + RulesFile.BackupSuffix));
}
```

`NewRulesFile`, `Rule` and `Spec` are this file's existing helpers; use them as the neighbouring tests do.
`BackupSuffix` must be made `internal` (it is `private const` today) so the test can name it rather than
hard-coding `".bak"`.

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --filter "FullyQualifiedName~ChangingAManagedLabel"`
Expected: FAIL to compile — no `ChangeLabel`.

- [ ] **Step 3: Add the managed write**

Give `Write` an opt-out for the backup, defaulting to today's behaviour so no existing caller changes:

```csharp
private static RuleWrite Write(
    string path, Func<JsonArray, RulesRead, (RuleWrite Outcome, bool Changed)> edit, bool keepBackup = false)
```

and at the swap:

```csharp
// A rewrite Ur Score made on its own must not spend the owner's undo point: the backup is the whole file's,
// so an automatic write would otherwise push a hand-typed rule out of it within two changes.
if (exists && keepBackup) File.Replace(temp, path, destinationBackupFileName: null);
else if (exists) File.Replace(temp, path, path + BackupSuffix);
else File.Move(temp, path, overwrite: false);
```

Then the entry point, which changes ONLY the label and only on a rule Ur Score owns:

```csharp
/// <summary>
/// Rewrites just the label of Ur Score's own rule, leaving every other field and the file's backup as they
/// were. This is the only way a label Ur Score maintains is written, so the ordering rule (§1 of the design:
/// label first, then the number) has one place to hold rather than every future call site to remember.
/// </summary>
public static RuleWrite ChangeLabel(string path, string metricId, AlertKind kind, string label)
{
    GuardId(metricId);
    return Write(path, (rules, read) =>
    {
        if (read.OursFor(metricId, kind) is not { } ours) return (RuleWrite.NotThere, false);
        if (string.Equals(ours.Label, label, StringComparison.Ordinal)) return (RuleWrite.Done, false);

        rules[ours.Index]!["label"] = label;
        return (RuleWrite.Done, true);
    }, keepBackup: true);
}
```

- [ ] **Step 4: Run it and watch it pass**

Expected: PASS. Then run `RulesFileTests` and `RulesFileParityTests` whole — the parity test exists to catch a
writer drifting from a reader.

- [ ] **Step 5: Commit**

```bash
git add src/Core/RulesFile.cs tests/RulesFileTests.cs
git commit -m "feat: ChangeLabel rewrites a managed label without spending the backup"
```

---

### Task 3: Setup shows a managed label as live, and will not let it be typed

**Files:**
- Modify: `src/UI/Setup/AlertCards.cs:141`
- Test: `tests/AlertCardsTests.cs`

**Interfaces:**
- Consumes: `FieldMetric.ManagedLabel` from Task 1.

- [ ] **Step 1: Write the failing test**

```csharp
/// <summary>
/// "Why does my alert keep renaming itself" is the support question this prevents. A managed label is shown
/// as live and is not offered for editing, so a human's typing and Ur Score's writing can never collide.
/// </summary>
[Fact]
public void AManagedLabelIsShownAsLiveAndNotOfferedForEditing()
{
    var card = CardFor(FieldMetrics.ThreatHours);

    Assert.False(card.LabelIsYours);
    Assert.Contains("names the clan closest behind", card.LabelNote, StringComparison.Ordinal);
}

[Fact]
public void AnOrdinaryClanNumbersLabelIsStillYours()
{
    Assert.True(CardFor(FieldMetrics.Points).LabelIsYours);
}
```

`CardFor` is a small helper to add at the top of the test class, built the way the neighbouring tests build a
view: call `AlertCards.Build(installed, rules, clan)` and take the card whose metric id matches.

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --filter "FullyQualifiedName~AManagedLabelIsShown"`
Expected: FAIL — no `LabelIsYours` on the card.

- [ ] **Step 3: Carry it on the card**

Add `LabelIsYours` and `LabelNote` to the card record, set from the metric's `ManagedLabel`, and bind the
label editor's enabled state to `LabelIsYours` in the Alerts page XAML. The note reads:

> Ur Score keeps this name current: it names the clan closest behind you, so it changes as they do.

- [ ] **Step 4: Run it and watch it pass**

Expected: PASS, plus the whole `AlertCardsTests` and `AlertsPageFenceTests` classes.

- [ ] **Step 5: Commit**

```bash
git add src/UI/Setup/AlertCards.cs src/UI/Setup/AlertsPage.xaml tests/AlertCardsTests.cs
git commit -m "feat: a managed label reads as live in Setup and cannot be typed over"
```

---

### Task 4: The clans behind yours, from one live read

**Files:**
- Modify: `src/Book/FieldSummary.cs`
- Test: `tests/FieldSummaryTests.cs`

**Interfaces:**
- Produces: `record BehindClan(string Name, double Value)` and
  `FieldSummary.Behind(IReadOnlyList<GroupRow> groups, string valueKey, IReadOnlySet<string>? mine, int count) -> IReadOnlyList<BehindClan>`.

This reads the LIVE groups, not the book. The whole field is present at send time, which is why the design
needed no change to `GroupRows.Keep` (§4).

- [ ] **Step 1: Write the failing test**

```csharp
/// <summary>
/// The clans just below the best placed of yours, best first, with none of yours among them. Ranked by the
/// value itself, never by the rank the list handed over, as every other field number is.
/// </summary>
[Fact]
public void BehindTakesTheClansBelowTheBestPlacedOfYours()
{
    IReadOnlyList<GroupRow> groups =
    [
        Row("Leader", 900), Row("K0i2", 500), Row("H8ER", 400), Row("LXCC", 300), Row("R0W", 200), Row("Tail", 100),
    ];

    var behind = FieldSummary.Behind(groups, "points", Mine("K0i2"), count: 3);

    Assert.Equal(["H8ER", "LXCC", "R0W"], behind.Select(b => b.Name));
    Assert.Equal(400, behind[0].Value);
}

/// <summary>With none of yours in the read there is nobody to be behind, which is nothing rather than the tail.</summary>
[Fact]
public void BehindIsEmptyWhenNoneOfYoursIsInTheRead()
{
    IReadOnlyList<GroupRow> groups = [Row("Leader", 900), Row("H8ER", 400)];

    Assert.Empty(FieldSummary.Behind(groups, "points", Mine("K0i2"), count: 3));
}
```

`Row` and `Mine` are this file's existing helpers.

- [ ] **Step 2: Run it and watch it fail**

Expected: FAIL to compile — no `Behind`.

- [ ] **Step 3: Implement**

```csharp
/// <summary>One clan below yours in a live read: its name, because a threat is tracked by name, and its value.</summary>
public sealed record BehindClan(string Name, double Value);

/// <summary>
/// The <paramref name="count"/> clans placed just below the best placed of yours, best first.
/// <para>
/// By NAME, because a threat has to be. A position's points on the way up reveal nothing about a change of
/// occupant, so the clear-on-fall rule that makes <see cref="Above"/> safe has no equivalent below (design §4).
/// </para>
/// </summary>
public static IReadOnlyList<BehindClan> Behind(
    IReadOnlyList<GroupRow> groups, string valueKey, IReadOnlySet<string>? mine, int count)
{
    if (mine is null || count <= 0) return [];

    var ranked = groups
        .Select(g => (g.Name, Value: g.Values.TryGetValue(valueKey, out var v) ? v : (double?)null))
        .Where(g => g.Value is not null)
        .OrderByDescending(g => g.Value!.Value)
        .ToList();

    var at = ranked.FindIndex(g => mine.Contains(g.Name));
    if (at < 0) return [];

    return [.. ranked.Skip(at + 1).Where(g => !mine.Contains(g.Name)).Take(count)
        .Select(g => new BehindClan(g.Name, g.Value!.Value))];
}
```

- [ ] **Step 4: Run it and watch it pass**

- [ ] **Step 5: Commit**

```bash
git add src/Book/FieldSummary.cs tests/FieldSummaryTests.cs
git commit -m "feat: FieldSummary.Behind names the clans below yours in a live read"
```

---

### Task 5: The soonest threat by time, never the nearest by place

**Files:**
- Modify: `src/Core/FieldMetrics.cs`
- Test: `tests/FieldMetricsTests.cs`

**Interfaces:**
- Consumes: `BehindClan` (Task 4).
- Produces: `record ThreatValue(string Name, double Gap, double Hours)` and
  `FieldMetrics.SoonestThreat(IReadOnlyList<BehindClan> behind, IReadOnlyList<SeriesPoint> mine, Func<string, IReadOnlyList<SeriesPoint>> seriesOf, DateTimeOffset now) -> ThreatValue?`.

**This is the task the spec exists for.** A clan three places back with a hotter pace passes us sooner than the
one directly behind. Naming the immediate follower and calling it soonest would be a confident false statement
on a phone mid-battle.

- [ ] **Step 1: Write the failing test**

```csharp
/// <summary>
/// The threat named is the soonest by TIME, not the nearest by place. H8ER is closer but barely moving; R0W is
/// further back and much faster, and R0W is the one that takes our place first. Naming H8ER here would be a
/// confident false statement on a phone mid-battle, which is worse than sending nothing at all.
/// </summary>
[Fact]
public void TheThreatNamedIsTheSoonestByTimeNotTheNearestByPlace()
{
    var now = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
    IReadOnlyList<BehindClan> behind = [new("H8ER", 900), new("R0W", 500)];

    var threat = FieldMetrics.SoonestThreat(
        behind,
        Series(now, 1_000, 1_000),                       // ours: flat
        name => name == "H8ER" ? Series(now, 890, 900)   // 10/hr: never catches us in time
                               : Series(now, 300, 500),  // 200/hr: passes us in about an hour
        now);

    Assert.NotNull(threat);
    Assert.Equal("R0W", threat!.Name);
    Assert.Equal(500, threat.Gap);
    Assert.InRange(threat.Hours, 0.9, 1.1);
}

/// <summary>
/// With no pace for a chaser there is no crossing to work out, and a guess is not an answer. This matches how
/// the place-above pace already refuses rather than inventing a number.
/// </summary>
[Fact]
public void AChaserWithNoPaceYetIsNoThreatRatherThanAGuess()
{
    var now = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    Assert.Null(FieldMetrics.SoonestThreat(
        [new("H8ER", 500)], Series(now, 1_000, 1_000), _ => [], now));
}
```

`Series(now, first, last)` is a helper to add: two `SeriesPoint`s an hour apart ending at `now`, so a pace is
readable. `Pace.Shortest` governs how much is enough; check it and space the points to clear it.

- [ ] **Step 2: Run it and watch it fail**

Expected: FAIL to compile — no `SoonestThreat`.

- [ ] **Step 3: Implement**

```csharp
/// <summary>A clan behind us that is closing: its name, how far back it is, and how long until it passes us.</summary>
public sealed record ThreatValue(string Name, double Gap, double Hours);

/// <summary>
/// The clan behind us that takes our place SOONEST, by time rather than by place.
/// <para>
/// A clan three places back going much faster passes us before the one directly behind, so the nearest by
/// place is the wrong answer and a confident wrong name is worse than silence (design §4). A chaser whose
/// pace cannot be read yet is no threat rather than a guess, matching <see cref="Needed"/>.
/// </para>
/// </summary>
public static ThreatValue? SoonestThreat(
    IReadOnlyList<BehindClan> behind,
    IReadOnlyList<SeriesPoint> mine,
    Func<string, IReadOnlyList<SeriesPoint>> seriesOf,
    DateTimeOffset now)
{
    var since = now - Pace.LongestCurrent;
    if (Pace.Over(mine, since, Pace.LongestCurrent) is not { } ours) return null;

    ThreatValue? soonest = null;
    foreach (var clan in behind)
    {
        if (Pace.Over(seriesOf(clan.Name), since, Pace.LongestCurrent) is not { } theirs) continue;

        var closing = theirs.PerHour - ours.PerHour;
        if (closing <= 0) continue;

        var hours = clan.Gap() / closing;
        if (!double.IsFinite(hours) || hours < 0) continue;
        if (soonest is null || hours < soonest.Hours) soonest = new ThreatValue(clan.Name, clan.Gap(), hours);
    }

    return soonest;
}
```

`clan.Gap()` above is shorthand: the gap is `mine's latest value - clan.Value`, so pass the owner's latest
points in or compute it at the call site and put the number on `BehindClan`. Pick one and keep it consistent —
the test asserts `Gap == 500` for a chaser on 500 against our 1,000.

- [ ] **Step 4: Run it and watch it pass**

- [ ] **Step 5: Commit**

```bash
git add src/Core/FieldMetrics.cs tests/FieldMetricsTests.cs
git commit -m "feat: the named threat is the soonest by time, not the nearest by place"
```

---

### Task 6: The two threat numbers, and the name on the label

**Files:**
- Modify: `src/Core/FieldMetrics.cs`
- Test: `tests/FieldMetricsTests.cs`

**Interfaces:**
- Produces: `FieldMetrics.ThreatGap = "threat-gap"`, `FieldMetrics.ThreatHours = "threat-hours"`,
  `FieldMetrics.ThreatLabel(string threat, string? clan) -> string`.

- [ ] **Step 1: Write the failing test**

```csharp
/// <summary>
/// Both threat numbers carry a label Ur Score maintains, because the name in them changes. Every other
/// clan number stays the owner's to word.
/// </summary>
[Fact]
public void BothThreatNumbersDeclareAManagedLabel()
{
    Assert.True(FieldMetrics.Find(FieldMetrics.ThreatHours)!.ManagedLabel);
    Assert.True(FieldMetrics.Find(FieldMetrics.ThreatGap)!.ManagedLabel);
    Assert.Equal("clan.standing.threat-hours", FieldMetrics.Find(FieldMetrics.ThreatHours)!.MetricId);
}

/// <summary>
/// The label names the threat and your clan, mirroring 0.5.3's "K0i2 clan points": the id stays shared so a
/// clan leader can say "set an alert on that number", and only the wording is local to each phone.
/// </summary>
[Fact]
public void TheLabelNamesTheThreatAndYourClan()
{
    Assert.Equal("H8ER catching K0i2", FieldMetrics.ThreatLabel("H8ER", "K0i2"));
    Assert.Equal("H8ER catching up", FieldMetrics.ThreatLabel("H8ER", null));
}
```

- [ ] **Step 2: Run it and watch it fail**

- [ ] **Step 3: Add the metrics and the label**

Add to `All`, both with `ManagedLabel: true`:

- `threat-gap` / `clan.standing.threat-gap` — "How far behind the clan closest to taking your place is."
- `threat-hours` / `clan.standing.threat-hours` — "How long until the clan behind you takes your place, at
  both your current paces. Set it to alert below six hours to hear about it while you can still answer."

- [ ] **Step 4: Run it and watch it pass**

- [ ] **Step 5: Commit**

---

### Task 7: Write the label, then report the number

**Files:**
- Modify: `src/Core/RecipeWatch.cs:111` (constructor), `:169` (series fields), `:527` (`SendFieldAsync`)
- Test: `tests/RecipeWatchTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: `RecipeWatch(..., Func<FieldMetric, string, bool>? writeLabel = null)` — returns true when the
  label is in place, written or already correct.

**The ordering rule lives here.** The host binds a rule's label at the observation, so a number reported
before its label is written ships under the previous name.

- [ ] **Step 1: Write the failing test**

```csharp
/// <summary>
/// The label is written BEFORE the number is reported, because the host binds a rule's label at the
/// observation and not at the push: report first and the alert ships under the previous chaser's name.
/// And if the label cannot be written, nothing is reported at all — a threat number under a stale name is a
/// confident false statement, which is worse than silence.
/// </summary>
[Fact]
public async Task TheLabelIsWrittenBeforeTheNumberIsReported()
{
    var order = new List<string>();
    var watch = WatchWith(
        writeLabel: (_, label) => { order.Add("label:" + label); return true; },
        onReport: (id, _) => order.Add("report:" + id));

    await watch.ReadOnceAsync(BattleWithChaser("H8ER"), CancellationToken.None);

    var label = order.FindIndex(s => s.StartsWith("label:", StringComparison.Ordinal));
    var report = order.FindIndex(s => s == "report:clan.standing.threat-hours");
    Assert.True(label >= 0 && report > label, string.Join(" then ", order));
}

[Fact]
public async Task NoLabelMeansNoSend()
{
    var reported = new List<string>();
    var watch = WatchWith(writeLabel: (_, _) => false, onReport: (id, _) => reported.Add(id));

    await watch.ReadOnceAsync(BattleWithChaser("H8ER"), CancellationToken.None);

    Assert.DoesNotContain("clan.standing.threat-hours", reported);
}
```

`WatchWith` and `BattleWithChaser` are helpers to add, built from the existing `RecipeWatchTests` doubles.

- [ ] **Step 2: Run it and watch it fail**

- [ ] **Step 3: Implement**

Add the per-name store beside the existing two, with its own comment saying why the clear-on-fall rule does
not apply:

```csharp
/// <summary>
/// Each tracked chaser's recent points, by clan name. Separate from <see cref="_fieldAbove"/> because that
/// one tracks a POSITION and clears itself on a fall, which is how a change of occupant looks from a
/// position. A threat is tracked by name, where a fall means a correction rather than a new clan, and the
/// identity is the key rather than something inferred. Names that leave the band are dropped.
/// </summary>
private readonly Dictionary<string, List<SeriesPoint>> _fieldBehind = new(StringComparer.OrdinalIgnoreCase);
```

In `SendFieldAsync`, after the existing `Remember` calls: take `FieldSummary.Behind(reading.Groups, valueKey,
myGroups?.Invoke(), count: 5)`, remember each by name, drop names no longer in the band, then
`FieldMetrics.SoonestThreat(...)`. When there is a threat and either threat metric is ticked:

```csharp
// The host binds a rule's label at the observation, not at the push, so the name must be on disk before the
// number goes out. A failed write is a refusal to send, not a send under the old name.
if (writeLabel is null || !writeLabel(metric, FieldMetrics.ThreatLabel(threat.Name, ourClanName))) continue;
```

- [ ] **Step 4: Run it and watch it pass**

Run the whole suite and compare the count.

- [ ] **Step 5: Commit**

```bash
git add src/Core/RecipeWatch.cs tests/RecipeWatchTests.cs
git commit -m "feat: the threat's name is written to the label before the number is reported"
```

---

### Task 8: Wire it up and close the row

**Files:**
- Modify: `src/Composition/AppServices.cs` (pass `writeLabel` from `RulesPath`), `docs/backlog.md`

- [ ] **Step 1: Pass the writer in**

`AppServices` owns `RulesPath`; give `RecipeWatch` a `writeLabel` that calls
`RulesFile.ChangeLabel(RulesPath, metric.MetricId, AlertKind.Below, label)` and treats `RuleWrite.NotThere` as
success — no rule for this metric means nothing to keep current, and the send is then refused by the policy
anyway because the metric is not ticked.

- [ ] **Step 2: Run the whole suite and compare counts**

- [ ] **Step 3: Screenshot-compare the Alerts page**

The managed card is new UI. Batch it with any other outstanding screenshot work: one app launch, announced to
`rororoblox-69` first, closed straight after, because the installed plugin holds the single-instance lock while
a battle is recording.

- [ ] **Step 4: Close V3-S.32**

Mark it FIXED with what landed, recount the backlog AS ROWS between "## Open, and you'd notice" and "# 0.3.2",
and check the summary list still names the same items as the rows.

- [ ] **Step 5: Commit**

---

## Self-Review

**Spec coverage.** §1 host constraints → Tasks 2 and 7 (ordering rule, one enforcement point). §2 managed
labels → Tasks 1, 2, 3. §2 backup carve-out → Task 2. §3 the numbers → Task 6. §4 by name and not the wrong
one → Tasks 4, 5. §5 the label → Task 6. §6 testing → every task leads with its test; the "soonest by time"
test in Task 5 is the one the spec calls out. §7 out of scope → nothing here batches several threats or puts
the name on the board.

**Known gap, deliberate.** The batcher splits a grouped push when a label changes mid-window (§1). Not
addressed: it needs the single-threat case to exist before it can be judged, and it degrades to two pushes
rather than a wrong one.

**Open question for the implementer, not a placeholder.** Task 5's `Gap` can be carried on `BehindClan` at
construction or computed in `SoonestThreat` from our latest points. Task 4's record currently carries only
`Name` and `Value`. Pick one, make the test in Task 5 agree, and do not leave both.
