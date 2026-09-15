# An Alerts page that tells you what to do — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ur Score v0.3.2: Setup › Alerts gives every stat you send a card whose alerts read as sentences in your words ("Alert me when an account's Points gains fewer than 100 a minute for 10 minutes."), which you add, change and remove in place. The card says what happened, in the theme; the next step in RoRoRo stays in view; there is no JSON on screen and no stock message box.

**Architecture:**
- **Rules file (`src/Core/RulesFile.cs`):** pure file operations on a path it is given. `Read` lists every rule exactly as RoRoRo's parser reads it (owner, kind, label; one bad row costs only itself). `TurnOn`, `Change` and `Remove` edit only Ur Score's own rule of a kind, keep every other rule and field, back the file up, write through a temporary file, and refuse a file that can't be opened, isn't JSON or isn't a list.
- **Pure model (`src/UI/Setup/AlertCards.cs`):** turns installed recipes plus a `RulesRead` into cards, sentences, marks and the kinds still addable. It checks what you typed and holds the page's one open editor as a record (`AlertsUi`). It also produces the rows the page binds, the result lines, and where keyboard focus goes next. Every decision a test can see lives here.
- **WPF, kept thin:** `AlertsPage` reads the file through `ISetupServices.RulesPath`, draws `AlertCards.Rows` into a `ui:RowList` of cards, writes through `RulesFile`, and moves focus to the control `AlertCards` names. The report policy card is unchanged. `RuleInventory` and the old one-button helper retire.

**Tech Stack:** .NET 10 WPF (`net10.0-windows`), xUnit 2.9, System.Text.Json (`JsonNode`). No new packages.

**Spec:** `docs/2026-09-15-alerts-card-design.md` (approved 2026-09-15, binding). RoRoRo's side, read-only: `ROROROblox/src/ROROROblox.App/Metrics/LocalFileMetricRuleSource.cs` (rows: `metricId`, `kind` Rate|Level|Event case-insensitive, `threshold`, `windowMinutes`, `alertWhenBelow`; unknown fields ignored; one bad row costs only itself), `ROROROblox/src/ROROROblox.Core/Metrics/MetricRule.cs`, and the companion wording spec `ROROROblox/docs/superpowers/specs/2026-09-15-metric-alert-wording-design.md` (reads the optional `label`). Execution rulings of `docs/plans/2026-09-14-score-book-stage-2.md` and `docs/plans/2026-09-15-default-views.md` still bind.

**Branch:** `feat/alerts-card` (at `bab5335` = master v0.3.1 plus the backlog docs and the design). **Deadline:** built, reviewed, walked and ready for the owner's release OK within about a day; the clan battle is Saturday 2026-09-19.

## Rulings made while planning

Recorded so a reviewer doesn't read them as drift. Each names what was decided, why, and the cost if it is wrong.

**The rules file**

- **A1. The rules path is injected, and the walk uses a scratch file.** Every `RulesFile` operation takes the path as a parameter; tests pass a file in a `TempDir`. The app resolves the path once, in `AppServices.RulesPath` (exposed on `ISetupServices`): `RulesFile.ResolvePath(Environment.GetEnvironmentVariable("UR_SCORE_RULES_FILE"))`. That is the variable's value when it is set to a fully qualified path, and `RulesFile.DefaultPath` otherwise. When the override is in use, the trail says so once at start. The smoke walk sets the variable to a scratch file under `%TEMP%`. It never backs up or restores RoRoRo's real `metric-rules.json`, and it fails if that file's SHA-256 (or its absence) differs after the walk. *Why:* backing up and restoring can't honour "never leave the owner's rules file changed" when a walk is killed mid-run, and while the walk runs, RoRoRo would read test rules that could ring the owner's phone. *Cost if wrong:* the shipped app honours an environment variable that only the walk sets. Anyone who sets it points Setup › Alerts at another file, and Diagnostics' trail shows that.
- **A2. A rule is what RoRoRo reads, nothing more.** A row counts only when it is an object with a non-blank string `metricId` and a `kind` that parses to Rate, Level or Event (ignoring case), and when `threshold` and `windowMinutes` are JSON numbers or absent (absent is 0) and `alertWhenBelow` is true, false or absent (absent is true). Field names are matched ignoring case. A row RoRoRo would skip is never listed as an alert, never changed and never removed. When its metric id is readable, its card counts it in the note. Metric ids match ordinally, as 0.3.1 did. *Cost if wrong:* a hand-typo'd rule shows only as a count, which matches what RoRoRo does with it (nothing).
- **A3. Owner.** Exactly `"626labs.ur-score"` is Ur Score's. A missing, null, blank or non-text owner is "yours"; any other text is "another plugin's". *Cost if wrong:* a hand rule stamped with a mistyped owner reads as another plugin's and stays untouched.
- **A4. The rule 0.3.1 added** (owner `626labs.ur-score`, kind Rate, no label) is Ur Score's stops-climbing alert for its metric. Reading never writes, so its label is added only when you press Save after Change. Every sentence on a card uses the stat's label from the recipe, even when a rule carries a different `label`. *Why:* the recipe is where Ur Score's words come from, and a recipe update renames the card at once. *Cost if wrong:* a label someone hand-edited into a rule isn't shown until the next Change overwrites it.
- **A5. "One of each kind", with duplicates.** For each metric and kind, the first Ur Score rule in file order is the managed one: it gets Change and Remove. A later Ur Score rule of the same kind is listed with the mark "a copy" and no buttons. Remove deletes only the managed one, so a hand-duplicated copy becomes managed on the next redraw and needs its own Remove. + Add an alert doesn't offer a kind Ur Score already has, and `TurnOn` refuses with `AlreadyThere` if one appeared since the page drew. A rule you or another plugin wrote never blocks Ur Score's own. *Cost if wrong:* someone who duplicated a rule by hand presses Remove twice.
- **A6. Event rules** are listed ("Alert me when an account's Rank changes.") and never added. An Event rule owned by Ur Score (hand-edited kind) shows Remove only. *Cost if wrong:* none known.
- **A7. What a write looks like.** Turn on appends one row at the end. A Rate row carries `metricId, kind, threshold, windowMinutes, alertWhenBelow: true, owner, label`; a Level row carries the same minus `windowMinutes`. Change replaces Ur Score's row whole, at the same index. Every other row keeps every field and value, unknown fields included. Comments and formatting are not kept, as in 0.3.1, and the backup keeps them. Text is written with relaxed escaping, so labels stay readable. *Cost if wrong:* a field someone added by hand to Ur Score's own row is dropped on Change (the backup has it).
- **A8. Backup and safety.** Before each write, the file is copied to `metric-rules.json.ur-score-backup` beside it, overwriting the last backup, so the backup is the state just before the latest write. A missing file gets no backup: Turn on creates the folder and file, while Change and Remove create nothing. Text goes to `metric-rules.json.ur-score-writing` and replaces the file in one move, so a crash never leaves half a file. A file that is locked, not JSON, or not a list is never written. *Cost if wrong:* undoing two writes back means redoing them on the page.
- **A9. `RuleInventory` retires.** Showing what the file says now replaces 0.3.1's "changed since Ur Score added it" drift line. Ur Score stops writing `written-rules.json` and leaves an existing one untouched on disk. *Cost if wrong:* a threshold you tuned by hand on Ur Score's rule shows its current number with no "changed" note.

**The model and the page**

- **A10. Cards.** Each metric id that any non-group recipe sends (Send ticked, with a name) gets one card, in recipe order. The label comes from the first recipe that sends it, as 0.3.1's "Rule for" list did. A sent stat whose sources are all watched still gets a card, because the report policy card already says it sends nothing. *Cost if wrong:* one card that can't fire until a source is main or mine.
- **A11. A stat you stop sending.** While Ur Score has a rule for its metric id, the stat keeps a card, placed after the sent cards and ordered by metric id. It lists every rule for that id and carries the note "You don't send {label} any more, so its alerts can't fire. Tick Send in Setup › Stats, or remove them.". It offers Remove only: no Change, no + Add an alert. Its label is the one recipe stat pinned to that metric id, else the first rule label, else the metric id itself. A metric id that has only your or another plugin's rules, and isn't sent, gets no card. *Cost if wrong:* when no recipe and no rule names the stat, the card title is a metric id.
- **A12. What you type.** A number is ASCII digits, optionally grouped with commas in threes, and optionally a dot with one or two decimals. Surrounding spaces are ignored. Anything else is refused on the card with nothing written:
  - "1,5" gets "Use a dot for decimals, like 1.5.";
  - three or more decimals get "Use at most two decimal places, like 1.25.";
  - anything else unreadable gets "Type a number, like 100.".

  Stops climbing needs a number above 0; crosses a number allows 0 (a sign isn't accepted). A number at or above 1,000,000,000,000,000 is refused. Sentences show thousands separators and up to two decimals, while the number box shows no separators.
  - *Minutes:* a choice of 10, 15 or 30. When you change a rule whose window is some other positive number, that number is offered too, so saving never quietly changes it.
  - *Direction:* "below" or "above", defaulting to below (RoRoRo's default).
  - *A new alert starts from:* 100 for 10 minutes for stops climbing; an empty number for crosses a number.

  *Cost if wrong:* someone whose locale writes "1.500" for fifteen hundred gets 1.5 plus a visible sentence, which they'd see before and after Turn on.
- **A13. One editor, one result.** The page has one editor open at a time: the kind choice, adding, or changing. Pressing another card's button closes it without saving. The result line also appears once on the page, on the card acted on, and stays until the next action. What happens after a click:
  - A problem with what you typed keeps the editor open with the reason under it.
  - A file that is locked or can't be written also keeps the editor open, so you can try again.
  - `AlreadyThere` and `NotThere` close the editor and say so on the card.
  - Remove asks no confirmation. The result line names what went, and Turn on brings it back.
  - When removing a stale stat's last alert takes its card away, the result shows under the cards.

  *Cost if wrong:* a stray Remove costs two clicks and a retype.
- **A14. Refresh while you type.** `ISetupServices.Changed` fires on every read and every book line. While an editor is open, a refresh redraws only the report policy card. Otherwise the cards are redrawn only when what they say changed (`AlertCards.Same`), so reads never steal focus or typed text. *Cost if wrong:* someone who edits the file by hand while an editor is open sees the change when the editor closes.
- **A15. Focus and keyboard.** Where focus goes:
  - After "+ Add an alert": the first kind button.
  - A kind button, or Change: the number box, with its text selected.
  - Turn on or Save: that alert's Change button.
  - Remove: + Add an alert, else the card's first Change (Remove on a stale card).
  - Cancel: the button that opened the editor.

  In the editor, Enter in the number box, or in a closed minutes or direction box, does Turn on or Save, and Escape cancels. Tab follows the sentence's order, then the buttons. The result is a text line in the card, named by its words; there is no live-region announcement. *Cost if wrong:* a Narrator user hears the result only when they move to it.
- **A16. The standing line** "Next, in RoRoRo: Settings › Alerts › turn on Metric alerts and choose where they go (desktop, Discord, phone)." shows while at least one sent stat's card lists an alert of any owner, since your own rules need Metric alerts too. It is hidden while the file can't be read, because then Ur Score can't tell. *Cost if wrong:* none known.
- **A17. The Stats table's rule line** (the import screen and Setup › Stats) keeps its place with new words: "No alert yet. Add one in Setup › Alerts.", "1 alert in RoRoRo. See it in Setup › Alerts.", "{n} alerts in RoRoRo. See them in Setup › Alerts.", or "RoRoRo's rules file can't be read right now. Setup › Alerts says why.". The file is read once when that screen loads, not on every tick. *Cost if wrong:* a line that's one screen-open stale.
- **A18. Accessible names carry the stat's label** ("Change the stops climbing alert for Diamonds"). Two sent stats with the same label get the same button names. *Cost if wrong:* a Narrator user can't tell those two cards apart by name alone; walks use distinct labels.

**Release**

- **A19. Version 0.3.2 is this work.** Knock-on changes:
  - The backlog remediation plan's wave 1 ships as **0.3.3**; the release step adds a banner saying so, and its Task 0 re-check covers this branch.
  - V3-S.10 loses the two Alerts message boxes (seven stock boxes left) and stays OPEN.
  - S1-12.5 stays OPEN, with its line moved.
  - `CHANGELOG.md` gains a 0.3.2 section; 0.3.1's missing section is left to wave 1's P35.

  *Cost if wrong:* version numbers in two docs.
- **A20. The live walk on the owner's own data is look-only.** Setup › Alerts is opened on the real `metric-rules.json` and screenshotted, and no button is pressed. Adding the label to the 0.3.1 rule with Change is the owner's own click. *Cost if wrong:* the owner's alerts keep RoRoRo 1.28's wording until they press Change once.

---

## Global Constraints

Carried from stages 1 and 2 and the default views plan:

- **No hostname literal in `src/`** except `NameClient.cs` (users.roblox.com) and `IconClient.cs` (thumbnails.roblox.com). `NoHostnameFenceTests` enforces it.
- **Other players never reach disk.** Nothing in this plan reads or writes players; rules carry metric ids and labels only.
- **Keys never reach disk.** `Redactor` masks keys in any text that could carry one; trail lines go through `AddTrail`.
- **One path to RoRoRo.** `ReportPolicy.SendAsync` stays the only caller of `IHostClient.ReportMetricAsync`. This plan changes no reporting.
- **Theme.** Themed brushes are referenced with `DynamicResource` only. No hex colours or literal brushes in `src/UI` XAML (`Transparent` is allowed), and no colour code in UI `.cs` (`ThemeFenceTests`). The only brushes are the nine `ThemeService` paints: `BgBrush`, `CyanBrush`, `MagentaBrush`, `WhiteBrush`, `MutedTextBrush`, `DividerBrush`, `RowBgBrush`, `RowHoverBrush`, `EdgeBrush`.
- **Every popup and toast is themed.** No `MessageBox.Show` and no unstyled `ToolTip` are added; the Alerts page has none (`AlertsPageFenceTests`). A problem is a themed line on the card.
- **Every `src/UI` list is a `ui:RowList`** (`RowListFenceTests`).
- **Ur Score's own text never names a game.** Stat labels come from the recipe.
- **Copy style:** sentence case, second person, specific, no emoji. No JSON on the Alerts page.
- **The rules file (design):** Ur Score keeps every rule it doesn't own, backs the file up before each write, and refuses a file that isn't valid JSON (or isn't a list, or can't be opened). It writes Rate and Level only. Every rule it writes carries `"owner": "626labs.ur-score"` and `"label"`, in the field names and JSON types RoRoRo's parser reads.
- **Build gate:** `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror` passes, and `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build` passes. Run both at the end of every task. Nullable and xUnit analyzer warnings are errors.
- **Commits:** one or more per task, message style `area: what changed`, ending with the line `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.
- **Pure first:** decisions live in `RulesFile` and `AlertCards`, with unit tests; the page decides nothing a test can't see.
- **Automation ids** that `tools/smoke` relies on change only with their scripts, in the same commit. Walk scripts are ASCII only (Windows PowerShell 5.1 reads BOM-less files as ANSI).
- **`System.IO` is not an implicit using in the app project**; a `src/` file using `File`, `Directory` or `Path` needs `using System.IO;`. A `Labs626.UrScore.UI` file that names the type `Source` needs `using Source = Labs626.UrScore.Core.Source;` after its `namespace` line (none in this plan does).
- **A running Ur Score locks `bin\Release`.** Close it before building.
- **No literal control characters** in code or docs.
- **Tests clean up after themselves:** temp folders come from `TempDir.Create` and are disposed.
- **Smoke walks never write RoRoRo's real `metric-rules.json`** (A1).
- **Merging, tagging and releasing are never pre-authorized.** Each waits for the owner's OK in the session.

---

## File structure

New files:

| File | Responsibility |
|---|---|
| `src/UI/Setup/AlertCards.cs` | `AlertStat`, `AlertLine`, `AlertCard`, `AlertsView`, `AlertDraft`, `AlertEditMode`, `AlertsUi`, `AlertTarget`, `AlertLineRow`, `AlertCardRow`, and `AlertCards` (build, words, drafts, checks, transitions, rows, focus, the Stats table line) |
| `tests/AlertCardsTests.cs` | `AlertCards` |
| `tests/AlertsPageFenceTests.cs` | No stock message box, tooltip or JSON serialising on the Alerts page |
| `tools/smoke/walk-alerts.ps1` | The Setup › Alerts walk against a scratch rules file |

Modified and deleted files, by task:

| Task | Files |
|---|---|
| 1 | `src/Core/RulesFile.cs` (new types and operations beside the old ones); `tests/RulesFileTests.cs` (replaced) |
| 2 | `src/UI/Setup/AlertCards.cs` (new); `tests/AlertCardsTests.cs` (new) |
| 3 | `src/Composition/ISetupServices.cs`, `src/Composition/AppServices.cs`, `src/UI/Setup/AlertsPage.xaml`, `src/UI/Setup/AlertsPage.xaml.cs`, `src/UI/Setup/AlertsModel.cs`, `src/UI/Setup/StatsPage.xaml.cs`, `src/UI/Setup/ImportFlow.cs`, `src/Core/RulesFile.cs` (old API removed); delete `src/Core/RuleInventory.cs`; tests `AlertsModelTests.cs` (trimmed), `AlertsPageFenceTests.cs` (new) |
| 4 | `tools/smoke/walk-alerts.ps1` (new), `tools/smoke/README.md` |
| Release | `manifest.json`, `Ur-Score.csproj`, `CHANGELOG.md`, `docs/backlog.md`, `docs/plans/2026-09-15-backlog-remediation.md` (banner), this plan (execution record) |

## Interface contract

Every task implements, or relies on, exactly these names. Everything else keeps the name it has at `bab5335`.

```csharp
// ---- Task 1 (Labs626.UrScore.Core, src/Core/RulesFile.cs) ----
public enum AlertKind { Rate, Level, Event }
public enum RuleOwner { UrScore, You, AnotherPlugin }
public enum RulesProblem { None, CantOpen, NotJson, NotAList }
public enum RuleWrite { Done, AlreadyThere, NotThere, CantOpen, NotJson, NotAList, CantWrite }
public sealed record AlertRule(int Index, string MetricId, AlertKind Kind, double Threshold, double WindowMinutes,
    bool AlertWhenBelow, RuleOwner Owner, string? Label);                                    // Index: the row's place in the file's list
public sealed record AlertSpec(AlertKind Kind, double Threshold, double WindowMinutes, bool AlertWhenBelow, string Label);
public sealed record RulesRead(RulesProblem Problem, bool Exists, IReadOnlyList<AlertRule> Rules, IReadOnlyList<string> SkippedMetricIds)
{
    public static RulesRead NoFile { get; }
    public static RulesRead Unusable(RulesProblem problem);
    public IReadOnlyList<AlertRule> For(string metricId);
    public int SkippedFor(string metricId);
    public AlertRule? OursFor(string metricId, AlertKind kind);                                // the managed one (A5)
}
public static class RulesFile
{
    public const string Owner = "626labs.ur-score";                                            // unchanged
    public const string PathVariable = "UR_SCORE_RULES_FILE";
    public const string BackupSuffix = ".ur-score-backup";
    public static string DefaultPath { get; }                                                   // unchanged
    public static string ResolvePath(string? overridePath);
    public static RulesRead Read(string path);
    public static RuleWrite TurnOn(string path, string metricId, AlertSpec spec);              // throws ArgumentException: blank id or Event
    public static RuleWrite Change(string path, string metricId, AlertSpec spec);              // throws ArgumentException: blank id or Event
    public static RuleWrite Remove(string path, string metricId, AlertKind kind);              // throws ArgumentException: blank id
}
// Task 3 removes RuleState, RuleStatus, RulesFile.Inspect/Preview/AddRule, and RuleInventory.

// ---- Task 2 (Labs626.UrScore.UI, src/UI/Setup/AlertCards.cs) ----
public sealed record AlertStat(string MetricId, string Label, bool Sent);
public sealed record AlertLine(AlertRule Rule, string Sentence, string Mark, bool Managed);
public sealed record AlertCard(AlertStat Stat, IReadOnlyList<AlertLine> Alerts, IReadOnlyList<AlertKind> CanAdd, string Note);
public sealed record AlertsView(IReadOnlyList<AlertCard> Cards, RulesProblem Problem, bool ShowNext);
public sealed class AlertDraft { public string Number { get; set; } public string Minutes { get; set; } public string Direction { get; set; } }
public enum AlertEditMode { None, ChoosingKind, Adding, Changing }
public sealed record AlertsUi(string? MetricId = null, AlertEditMode Mode = AlertEditMode.None, AlertKind Kind = AlertKind.Rate,
    AlertDraft? Draft = null, string Problem = "", string? ResultMetricId = null, string Result = "", bool ResultIsProblem = false)
{ public static AlertsUi Closed { get; } }
public sealed record AlertTarget(string MetricId, AlertKind Kind);
public sealed record AlertLineRow(string Sentence, string Mark, AlertTarget Target, bool ShowChange, bool ShowRemove, string ChangeName, string RemoveName)
{ public bool HasMark { get; } }
public sealed record AlertCardRow(/* see Task 2 Step 3 for the full parameter list */)
{ public bool HasNote { get; } public bool HasNoAlerts { get; } public bool ShowEditor { get; } public bool HasProblem { get; }
  public bool ShowResult { get; } public bool ShowResultProblem { get; } public IReadOnlyList<string> DirectionChoices { get; } }
public static partial class AlertCards
{
    public const double DefaultThreshold = 100, DefaultMinutes = 10, MaxNumber = 1e15;
    public const string Below = "below", Above = "above";
    public const string NextInRoRoRo, NoRecipe, NoSentStat, TypeANumber, UseADot, TwoDecimals, AboveZero, TooBig, ChooseMinutes, ChooseDirection;
    public static IReadOnlyList<double> Minutes { get; }                                        // [10, 15, 30]
    public static IReadOnlyList<string> DirectionChoices { get; }                               // [below, above]
    public static AlertsView Empty { get; }
    public static AlertsView Build(IReadOnlyList<InstalledRecipe> installed, RulesRead rules);
    public static bool Same(AlertsView a, AlertsView b);
    public static AlertCard? CardFor(AlertsView view, string metricId);
    public static AlertLine? Managed(AlertsView view, AlertTarget target);
    public static string Number(double value);   public static string Editable(double value);
    public static string Condition(AlertKind kind, string label, double threshold, double windowMinutes, bool below);
    public static string Sentence(AlertKind kind, string label, double threshold, double windowMinutes, bool below);
    public static string ProblemNote(RulesProblem problem);
    public static string Failed(RuleWrite outcome, string label);
    public static string AddName(string label);  public static string KindName(AlertKind kind, string label);
    public static string ChangeName(AlertKind kind, string label);  public static string RemoveName(AlertKind kind, string label);
    public static string NumberName(string label);  public static string MinutesName(string label);
    public static string DirectionName(string label);  public static string CancelName(string label);
    public static AlertDraft NewDraft(AlertKind kind);  public static AlertDraft DraftOf(AlertRule rule);
    public static IReadOnlyList<string> MinuteChoices(string? current);
    public static string ParseNumber(string? text, AlertKind kind, out double value);
    public static (AlertSpec? Spec, string Problem) Check(AlertKind kind, AlertDraft draft, string label);
    public static AlertsUi OpenAdd(string metricId);
    public static AlertsUi ChooseKind(AlertTarget target);
    public static AlertsUi OpenChange(AlertLine line);
    public static AlertsUi AfterWrite(AlertsUi ui, RuleWrite outcome, AlertSpec spec);
    public static AlertsUi AfterRemove(AlertTarget target, AlertLine line, string label, RuleWrite outcome);
    public static IReadOnlyList<AlertCardRow> Rows(AlertsView view, AlertsUi ui);
    public static string FocusName(AlertsView view, string metricId, AlertsUi ui, AlertKind kind);
    public static string FocusAfterCancel(AlertsView view, AlertsUi open);
    public static string EmptyLine(IReadOnlyList<InstalledRecipe> installed, AlertsView view);
    public static string OrphanResult(AlertsView view, AlertsUi ui);
    public static string StatLine(RulesRead rules, string metricId);
}

// ---- Task 3 (Labs626.UrScore.Composition) ----
// ISetupServices gains:  string RulesPath { get; }
// AlertsModel keeps PolicyItem, WatchOnly and Policies only.
```

## Automation ids

Stage 1's, stage 2's and default views' tables still hold. This plan adds or changes:

| Where | Automation ids and names |
|---|---|
| Setup › Alerts | `AlertCardList` (a list named "Your sent stats and their alerts"); under it `AlertsEmptyLine`, `AlertsResultLine` (a result whose card is gone), `AlertsNextLine`; `PolicyList`, `PolicyEmptyLine` unchanged |
| Inside a card | `AlertCardTitle`, `AlertCardNote`, `AlertNoAlertsLine`, `AlertEditorProblemLine`, `AlertResultLine`, `AlertResultProblemLine`; each alert `AlertSentence` and `AlertMark` |
| Buttons and inputs, by accessible name | "Add an alert for {label}", "Stops climbing, for {label}", "Crosses a number, for {label}", "Cancel, for {label}", "Turn on the alert for {label}", "Save the alert for {label}", "Change the stops climbing alert for {label}", "Remove the crosses a number alert for {label}" (and the other kind), the edit box "Number for {label}", the combo boxes "Minutes for {label}" and "Above or below for {label}" |
| Removed | `RuleStatBox`, `RuleLine`, `AddRuleButton`, `RulePreview` (no walk used them) |

---

### Task 1: Rules file operations: read every rule, and turn on, change or remove Ur Score's own

**Files:**
- Modify: `src/Core/RulesFile.cs` (usings, new types above the class, the class summary, new members; the old `Inspect`/`Preview`/`AddRule` stay until Task 3)
- Test: `tests/RulesFileTests.cs` (replaced whole)

**Interfaces:**
- Consumes: `TempDir.Create` (tests/TestDoubles.cs).
- Produces: `AlertKind`, `RuleOwner`, `RulesProblem`, `RuleWrite`, `AlertRule`, `AlertSpec`, `RulesRead`, `RulesFile.PathVariable/BackupSuffix/ResolvePath/Read/TurnOn/Change/Remove`, as in the interface contract.

- [ ] **Step 1: Write the failing tests**

Replace `tests/RulesFileTests.cs` with:

```csharp
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
              { "metricId": "{{Points}}", "kind": "event", "owner": "" },
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
        var path = Rules($$"""
            [
              "not a rule",
              { "metricId": 12345, "kind": "Rate" },
              { "kind": "Rate", "threshold": 1 },
              { "metricId": "{{Points}}", "kind": "Telepathy", "threshold": 1 },
              { "metricId": "{{Points}}", "kind": "Rate", "threshold": "100" },
              { "metricId": "{{Points}}", "kind": "Rate", "threshold": null, "windowMinutes": 5 },
              { "metricId": "{{Points}}", "kind": "Level", "threshold": 1, "alertWhenBelow": "yes" },
              { "metricId": "{{Points}}", "kind": "Rate", "threshold": 100, "windowMinutes": 10, "owner": "626labs.ur-score" }
            ]
            """);

        var read = RulesFile.Read(path);

        Assert.Equal(RulesProblem.None, read.Problem);
        Assert.Equal(7, Assert.Single(read.For(Points)).Index);
        Assert.Equal(4, read.SkippedFor(Points));
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
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(RulesProblem.CantOpen, RulesFile.Read(path).Problem);
            Assert.Equal(RuleWrite.CantOpen, RulesFile.TurnOn(path, Points, Stops));
        }

        Assert.Equal("[]", File.ReadAllText(path));
        Assert.False(File.Exists(path + RulesFile.BackupSuffix));
    }

    [Fact]
    public void AFileThatCanBeReadButNotReplacedSaysSoAndChangesNothing()
    {
        var before = $$"""[ { "metricId": "{{Points}}", "kind": "Rate", "threshold": 100, "windowMinutes": 10, "owner": "626labs.ur-score" } ]""";
        var path = Rules(before);
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(RuleWrite.CantWrite, RulesFile.Change(path, Points, Stops with { Threshold = 250 }));
        }

        Assert.Equal(before, File.ReadAllText(path));
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
        // Asserting the backup does NOT hold the new rule is what tells "backed up first" from "backed up last".
        var path = Rules("""[ { "metricId": "memory.warning", "kind": "Event" } ]""");

        Assert.Equal(RuleWrite.Done, RulesFile.TurnOn(path, Points, Stops));
        var backup = File.ReadAllText(path + RulesFile.BackupSuffix);
        Assert.Contains("memory.warning", backup);
        Assert.DoesNotContain(Points, backup);

        var beforeSecond = File.ReadAllText(path);
        Assert.Equal(RuleWrite.Done, RulesFile.TurnOn(path, Points, Crosses));
        Assert.Equal(beforeSecond, File.ReadAllText(path + RulesFile.BackupSuffix));
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
        Assert.Equal("UR_SCORE_RULES_FILE", RulesFile.PathVariable);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`
Expected: FAIL to compile (`AlertSpec`, `AlertKind`, `RulesFile.Read` and the others don't exist).

- [ ] **Step 3: The types**

In `src/Core/RulesFile.cs`, add `using System.Text.Encodings.Web;` after `using System.Text.Json;`. Then insert after the `RuleStatus` record (and before the `/// <summary>` of `RulesFile`):

```csharp
/// <summary>The kinds RoRoRo judges (its <c>MetricRuleKind</c>). Ur Score writes Rate and Level; Event is only ever read (A6).</summary>
public enum AlertKind { Rate, Level, Event }

/// <summary>Whose a rule is, by its <c>owner</c> field (A3).</summary>
public enum RuleOwner { UrScore, You, AnotherPlugin }

/// <summary>Why the rules file can't be used. None when it was read, or when there is no file.</summary>
public enum RulesProblem { None, CantOpen, NotJson, NotAList }

/// <summary>What a write did. Anything but Done changed nothing.</summary>
public enum RuleWrite { Done, AlreadyThere, NotThere, CantOpen, NotJson, NotAList, CantWrite }

/// <summary>One rule RoRoRo can read, at its <see cref="Index"/> in the file's list. A missing number reads as 0 and a missing direction as below, as RoRoRo reads them.</summary>
public sealed record AlertRule(
    int Index, string MetricId, AlertKind Kind, double Threshold, double WindowMinutes, bool AlertWhenBelow, RuleOwner Owner, string? Label);

/// <summary>What one Ur Score rule says: its kind, number, minutes (Rate) or direction (Level), under the stat's label.</summary>
public sealed record AlertSpec(AlertKind Kind, double Threshold, double WindowMinutes, bool AlertWhenBelow, string Label);

/// <summary>The rules file as read: its problem, whether it exists, every rule RoRoRo can read, and the metric ids of rows it would skip (A2).</summary>
public sealed record RulesRead(RulesProblem Problem, bool Exists, IReadOnlyList<AlertRule> Rules, IReadOnlyList<string> SkippedMetricIds)
{
    public static RulesRead NoFile { get; } = new(RulesProblem.None, false, [], []);

    public static RulesRead Unusable(RulesProblem problem) => new(problem, true, [], []);

    public IReadOnlyList<AlertRule> For(string metricId) =>
        [.. Rules.Where(r => string.Equals(r.MetricId, metricId, StringComparison.Ordinal))];

    public int SkippedFor(string metricId) =>
        SkippedMetricIds.Count(id => string.Equals(id, metricId, StringComparison.Ordinal));

    /// <summary>The Ur Score rule Change and Remove act on: the first of that kind for the metric, in file order (A5).</summary>
    public AlertRule? OursFor(string metricId, AlertKind kind) =>
        Rules.FirstOrDefault(r => r.Owner == RuleOwner.UrScore && r.Kind == kind && string.Equals(r.MetricId, metricId, StringComparison.Ordinal));
}
```

- [ ] **Step 4: The class summary**

Replace the `/// <summary>` block directly above `public static class RulesFile` (from "Reads RoRoRo's <c>metric-rules.json</c>, and adds exactly one rule" through its closing `/// </summary>`) with:

```csharp
/// <summary>
/// Reads RoRoRo's <c>metric-rules.json</c>, and turns on, changes and removes Ur Score's own rules in it, on an explicit click only.
/// <para>
/// The metric id is a silent-failure seam: it must match a rule in that file or nothing can ever alert, and a mismatch is
/// quiet on both sides. Leaving that to hand-editing JSON in another app's folder is a trap for a common Windows user, so
/// Setup › Alerts does it in your words.
/// </para>
/// <para>
/// EVERY WRITE IS FENCED. Explicit call only, never on startup. A rule is read exactly as RoRoRo's parser reads it, and a
/// row it would skip costs only itself. Only rules owned by <see cref="Owner"/> are ever changed or removed, and every other
/// rule keeps every field. The file is backed up before each write and replaced through a temporary file. A file that can't
/// be opened, isn't valid JSON or isn't a list is never written.
/// </para>
/// </summary>
```

- [ ] **Step 5: The operations**

Inside `RulesFile`, insert after the `DefaultPath` property:

```csharp
    /// <summary>The environment variable the Setup › Alerts walk sets to point Ur Score at a scratch rules file (A1).</summary>
    public const string PathVariable = "UR_SCORE_RULES_FILE";

    /// <summary>Beside the rules file: the file as it was just before Ur Score's latest write (A8).</summary>
    public const string BackupSuffix = ".ur-score-backup";

    private const string TempSuffix = ".ur-score-writing";

    /// <summary>Relaxed escaping keeps labels readable in a file people edit by hand; it is never shown in a browser.</summary>
    private static readonly JsonSerializerOptions EditOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>A fully qualified override (the walk's scratch file), else RoRoRo's own file (A1).</summary>
    public static string ResolvePath(string? overridePath) =>
        !string.IsNullOrWhiteSpace(overridePath) && Path.IsPathFullyQualified(overridePath.Trim())
            ? overridePath.Trim()
            : DefaultPath;

    /// <summary>Every rule RoRoRo can read in the file at <paramref name="path"/>. Never throws for a file problem: it says which.</summary>
    public static RulesRead Read(string path)
    {
        if (!File.Exists(path)) return RulesRead.NoFile;

        var (rules, problem) = Load(path);
        return rules is null ? RulesRead.Unusable(problem) : RowsOf(rules);
    }

    /// <summary>Adds Ur Score's rule of <paramref name="spec"/>'s kind for a metric, at the end. AlreadyThere when Ur Score has one (A5).</summary>
    public static RuleWrite TurnOn(string path, string metricId, AlertSpec spec)
    {
        Guard(metricId, spec.Kind);
        return Write(path, (rules, read) =>
        {
            if (read.OursFor(metricId, spec.Kind) is not null) return RuleWrite.AlreadyThere;
            rules.Add(Build(metricId, spec));
            return RuleWrite.Done;
        });
    }

    /// <summary>Rewrites Ur Score's rule of that kind in place, label included (A4, A7). NotThere when Ur Score has none.</summary>
    public static RuleWrite Change(string path, string metricId, AlertSpec spec)
    {
        Guard(metricId, spec.Kind);
        return Write(path, (rules, read) =>
        {
            if (read.OursFor(metricId, spec.Kind) is not { } ours) return RuleWrite.NotThere;
            rules[ours.Index] = Build(metricId, spec);
            return RuleWrite.Done;
        });
    }

    /// <summary>Deletes exactly the one Ur Score rule of that kind for the metric (A5). NotThere when Ur Score has none.</summary>
    public static RuleWrite Remove(string path, string metricId, AlertKind kind)
    {
        if (string.IsNullOrWhiteSpace(metricId)) throw new ArgumentException("A rule needs a metric id.", nameof(metricId));
        return Write(path, (rules, read) =>
        {
            if (read.OursFor(metricId, kind) is not { } ours) return RuleWrite.NotThere;
            rules.RemoveAt(ours.Index);
            return RuleWrite.Done;
        });
    }
```

and insert these private members at the end of the class, just before its closing `}`:

```csharp
    private static void Guard(string metricId, AlertKind kind)
    {
        if (string.IsNullOrWhiteSpace(metricId)) throw new ArgumentException("A rule needs a metric id.", nameof(metricId));
        if (kind == AlertKind.Event) throw new ArgumentException("Ur Score writes Rate and Level rules only.", nameof(kind));
    }

    /// <summary>The file's list, or why there isn't one.</summary>
    private static (JsonArray? Rules, RulesProblem Problem) Load(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, RulesProblem.CantOpen);
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text, documentOptions: ReadOptions);
        }
        catch (JsonException)
        {
            return (null, RulesProblem.NotJson);
        }

        return root is JsonArray rules ? (rules, RulesProblem.None) : (null, RulesProblem.NotAList);
    }

    /// <summary>Each row as RoRoRo's parser takes it (A2): a row it would skip is left out, and counted when its metric id is readable.</summary>
    private static RulesRead RowsOf(JsonArray array)
    {
        var rules = new List<AlertRule>();
        var skipped = new List<string>();

        for (var index = 0; index < array.Count; index++)
        {
            if (array[index] is not JsonObject row) continue;

            try
            {
                if (TextIn(row, "metricId") is not { } metricId || string.IsNullOrWhiteSpace(metricId)) continue;

                if (RuleAt(index, metricId, row) is { } rule) rules.Add(rule);
                else skipped.Add(metricId);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                // A row whose fields can't even be listed (a name written twice) is one RoRoRo can't use either.
            }
        }

        return new RulesRead(RulesProblem.None, true, rules, skipped);
    }

    private static AlertRule? RuleAt(int index, string metricId, JsonObject row)
    {
        if (!TryKind(row, out var kind)) return null;
        if (!TryNumber(row, "threshold", out var threshold)) return null;
        if (!TryNumber(row, "windowMinutes", out var window)) return null;
        if (!TryFlag(row, "alertWhenBelow", out var below)) return null;

        // RoRoRo turns the window into a TimeSpan and skips a row whose window can't be one.
        if (window > TimeSpan.MaxValue.TotalMinutes || window < TimeSpan.MinValue.TotalMinutes) return null;

        var owner = TextIn(row, "owner");
        var whose = string.IsNullOrWhiteSpace(owner) ? RuleOwner.You
            : string.Equals(owner, Owner, StringComparison.Ordinal) ? RuleOwner.UrScore
            : RuleOwner.AnotherPlugin;
        var label = TextIn(row, "label")?.Trim();

        return new AlertRule(index, metricId, kind, threshold, window, below, whose, string.IsNullOrEmpty(label) ? null : label);
    }

    /// <summary>A field by name ignoring case, as RoRoRo's parser matches it. False when the row has no such field.</summary>
    private static bool FieldIn(JsonObject row, string name, out JsonNode? value)
    {
        foreach (var pair in row)
        {
            if (!string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = pair.Value;
            return true;
        }

        value = null;
        return false;
    }

    private static string? TextIn(JsonObject row, string name) =>
        FieldIn(row, name, out var node) && node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static bool TryKind(JsonObject row, out AlertKind kind)
    {
        kind = AlertKind.Rate;
        return TextIn(row, "kind") is { } text && Enum.TryParse(text, ignoreCase: true, out kind) && Enum.IsDefined(kind);
    }

    /// <summary>Absent is 0; present must be a JSON number (a quoted or null number makes RoRoRo skip the row).</summary>
    private static bool TryNumber(JsonObject row, string name, out double number)
    {
        number = 0;
        if (!FieldIn(row, name, out var node)) return true;
        return node is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue(out number);
    }

    /// <summary>Absent is true (RoRoRo's default); present must be true or false.</summary>
    private static bool TryFlag(JsonObject row, string name, out bool flag)
    {
        flag = true;
        if (!FieldIn(row, name, out var node)) return true;
        if (node is not JsonValue value || value.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False)) return false;
        flag = value.GetValueKind() == JsonValueKind.True;
        return true;
    }

    /// <summary>
    /// Loads the list, lets <paramref name="edit"/> change it, and writes it back only when the edit says Done (A8): a copy of the
    /// file as it was goes beside it first, then the text goes to a temporary file that replaces the rules file in one move.
    /// </summary>
    private static RuleWrite Write(string path, Func<JsonArray, RulesRead, RuleWrite> edit)
    {
        var exists = File.Exists(path);
        JsonArray rules = [];
        var read = RulesRead.NoFile;

        if (exists)
        {
            var (loaded, problem) = Load(path);
            if (loaded is null)
            {
                return problem switch
                {
                    RulesProblem.CantOpen => RuleWrite.CantOpen,
                    RulesProblem.NotAList => RuleWrite.NotAList,
                    _ => RuleWrite.NotJson,
                };
            }

            rules = loaded;
            read = RowsOf(loaded);
        }

        var outcome = edit(rules, read);
        if (outcome != RuleWrite.Done) return outcome;

        string text;
        try
        {
            text = rules.ToJsonString(EditOptions);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return RuleWrite.NotJson;
        }

        var temp = path + TempSuffix;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (exists) File.Copy(path, path + BackupSuffix, overwrite: true);
            File.WriteAllText(temp, text);
            File.Move(temp, path, overwrite: true);
            return RuleWrite.Done;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // A temporary file left behind is harmless: nothing reads it, and the next write replaces it.
            }

            return RuleWrite.CantWrite;
        }
    }

    /// <summary>A rule in the host parser's own field names and JSON types (A7). A rule it can't read never fires, and nothing says so.</summary>
    private static JsonObject Build(string metricId, AlertSpec spec)
    {
        var rule = new JsonObject
        {
            ["metricId"] = metricId,
            ["kind"] = spec.Kind.ToString(),
            ["threshold"] = spec.Threshold,
        };
        if (spec.Kind == AlertKind.Rate) rule["windowMinutes"] = spec.WindowMinutes;
        rule["alertWhenBelow"] = spec.Kind == AlertKind.Rate || spec.AlertWhenBelow;
        rule["owner"] = Owner;
        if (!string.IsNullOrWhiteSpace(spec.Label)) rule["label"] = spec.Label.Trim();
        return rule;
    }
```

- [ ] **Step 6: Run the tests**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror` then `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~RulesFileTests"`
Expected: PASS. If `AFileThatCanBeReadButNotReplacedSaysSoAndChangesNothing` sees `Done`, Windows allowed the replace over an open handle on this machine. In that case, hold the stream with `FileShare.Read` and `FileAccess.Read` *and* set the file read-only (`File.SetAttributes(path, FileAttributes.ReadOnly)`, cleared in a `finally`); never weaken the assertion.

- [ ] **Step 7: The build gate**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror` then `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: both pass (`AlertsModelTests` still exercises the old `Inspect`, which Task 3 removes).

- [ ] **Step 8: Commit**

```bash
git add src/Core/RulesFile.cs tests/RulesFileTests.cs
git commit -m "rules: read every rule as RoRoRo does, and turn on, change or remove Ur Score's own with a backup

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: The Alerts model: cards, sentences, what you type, and each step of the inline editor

**Files:**
- Create: `src/UI/Setup/AlertCards.cs`
- Test: `tests/AlertCardsTests.cs`

**Interfaces:**
- Consumes: Task 1's `AlertKind`, `RuleOwner`, `RulesProblem`, `RuleWrite`, `AlertRule`, `AlertSpec`, `RulesRead`; `InstalledRecipe`, `RecipeState.SentStats/StatChoices`, `RecipeStats.Find` (unchanged).
- Produces: every Task 2 name in the interface contract. Task 3 binds `AlertCardRow`'s properties by these exact names.

- [ ] **Step 1: Write the failing tests**

Create `tests/AlertCardsTests.cs`:

```csharp
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

public class AlertCardsTests
{
    private const string Diamonds = "ps99.diamonds";
    private const string Rank = "ps99.rank";

    private static Recipe Profile => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

    /// <summary>The profile recipe with these value ids shown and sent under "ps99.{id}".</summary>
    private static InstalledRecipe Sending(params string[] keys) => new(Profile, "", new RecipeState(Stats: keys.ToDictionary(
        key => key, key => new StatChoice(Show: true, Send: true, MetricId: $"ps99.{key}"), StringComparer.Ordinal)));

    private static AlertRule Rule(int index, string metricId, AlertKind kind, double threshold, RuleOwner owner = RuleOwner.UrScore,
        double window = 0, bool below = true, string? label = null) => new(index, metricId, kind, threshold, window, below, owner, label);

    private static RulesRead RulesOf(params AlertRule[] rules) => new(RulesProblem.None, Exists: true, rules, []);

    [Fact]
    public void AlertsReadAsSentencesInYourWords()
    {
        Assert.Equal("Alert me when an account's Points gains fewer than 100 a minute for 10 minutes.", AlertCards.Sentence(AlertKind.Rate, "Points", 100, 10, true));
        Assert.Equal("Alert me when an account's Diamonds gains fewer than 1,500.5 a minute for 1 minute.", AlertCards.Sentence(AlertKind.Rate, "Diamonds", 1500.5, 1, true));
        Assert.Equal("Alert me when an account's Rank goes above 40.", AlertCards.Sentence(AlertKind.Level, "Rank", 40, 0, false));
        Assert.Equal("Alert me when an account's Rank goes below 0.25.", AlertCards.Sentence(AlertKind.Level, "Rank", 0.25, 0, true));
        Assert.Equal("Alert me when an account's Rank changes.", AlertCards.Sentence(AlertKind.Event, "Rank", 0, 0, true));
    }

    [Fact]
    public void EverySentStatHasACardListingItsAlertsByOwner()
    {
        var rules = RulesOf(
            Rule(0, Diamonds, AlertKind.Rate, 100, window: 10),
            Rule(1, Diamonds, AlertKind.Rate, 7, RuleOwner.You, window: 30),
            Rule(2, Diamonds, AlertKind.Level, 9, RuleOwner.AnotherPlugin, below: false),
            Rule(3, Diamonds, AlertKind.Rate, 200, window: 10, label: "Gems"),
            Rule(4, "memory.warning", AlertKind.Event, 0, RuleOwner.You));

        var view = AlertCards.Build([Sending("diamonds", "rank")], rules);

        Assert.Equal(new[] { new AlertStat(Diamonds, "Diamonds", true), new AlertStat(Rank, "Player rank", true) }, view.Cards.Select(c => c.Stat).ToArray());
        var diamonds = view.Cards[0];
        Assert.Equal(
            new[]
            {
                "Alert me when an account's Diamonds gains fewer than 100 a minute for 10 minutes.",
                "Alert me when an account's Diamonds gains fewer than 7 a minute for 30 minutes.",
                "Alert me when an account's Diamonds goes above 9.",
                "Alert me when an account's Diamonds gains fewer than 200 a minute for 10 minutes.",
            },
            diamonds.Alerts.Select(a => a.Sentence).ToArray());
        Assert.Equal(new[] { "", "yours", "another plugin's", "a copy" }, diamonds.Alerts.Select(a => a.Mark).ToArray());
        Assert.Equal(new[] { true, false, false, false }, diamonds.Alerts.Select(a => a.Managed).ToArray());
        Assert.Equal(new[] { AlertKind.Level }, diamonds.CanAdd);
        Assert.Empty(view.Cards[1].Alerts);
        Assert.Equal(new[] { AlertKind.Rate, AlertKind.Level }, view.Cards[1].CanAdd);
        Assert.True(view.ShowNext);
    }

    [Fact]
    public void TheNextStepInRoRoRoShowsOnlyWhileASentStatHasAnAlert()
    {
        Assert.False(AlertCards.Build([Sending("diamonds")], RulesOf()).ShowNext);
        Assert.True(AlertCards.Build([Sending("diamonds")], RulesOf(Rule(0, Diamonds, AlertKind.Level, 5, RuleOwner.You))).ShowNext);
        Assert.False(AlertCards.Build([Sending("rank")], RulesOf(Rule(0, Diamonds, AlertKind.Level, 5))).ShowNext);
        Assert.Equal("Next, in RoRoRo: Settings › Alerts › turn on Metric alerts and choose where they go (desktop, Discord, phone).", AlertCards.NextInRoRoRo);
    }

    [Fact]
    public void AStatYouNoLongerSendKeepsItsCardWhileUrScoreHasAnAlertForIt()
    {
        var installed = new InstalledRecipe(Profile, "", new RecipeState(Stats: new Dictionary<string, StatChoice>
        {
            ["diamonds"] = new(Show: true, Send: true, MetricId: Diamonds),
            ["rank"] = new(Show: true, MetricId: Rank),
        }));
        var rules = RulesOf(
            Rule(0, Rank, AlertKind.Level, 40, below: false),
            Rule(1, Rank, AlertKind.Rate, 3, RuleOwner.You, window: 10),
            Rule(2, "old.coins", AlertKind.Rate, 5, window: 10, label: "Coins"),
            Rule(3, "gone.metric", AlertKind.Level, 1),
            Rule(4, "yours.only", AlertKind.Level, 1, RuleOwner.You));

        var view = AlertCards.Build([installed], rules);

        Assert.Equal(
            new[]
            {
                new AlertStat(Diamonds, "Diamonds", true), new AlertStat("gone.metric", "gone.metric", false),
                new AlertStat("old.coins", "Coins", false), new AlertStat(Rank, "Player rank", false),
            },
            view.Cards.Select(c => c.Stat).ToArray());
        var rank = view.Cards[3];
        Assert.Equal("You don't send Player rank any more, so its alerts can't fire. Tick Send in Setup › Stats, or remove them.", rank.Note);
        Assert.Empty(rank.CanAdd);
        Assert.Equal(2, rank.Alerts.Count);

        var row = AlertCards.Rows(view, AlertsUi.Closed)[3];
        Assert.False(row.ShowAdd);
        Assert.Equal(new[] { (false, true), (false, false) }, row.Lines.Select(l => (l.ShowChange, l.ShowRemove)).ToArray());
    }

    [Theory]
    [InlineData(RulesProblem.CantOpen, "RoRoRo's rules file is locked or can't be opened, so Ur Score can't show or change alerts right now. Try again in a moment.")]
    [InlineData(RulesProblem.NotJson, "RoRoRo's rules file has a mistake in it, so Ur Score can't show or change alerts. Fix it by hand, then come back.")]
    [InlineData(RulesProblem.NotAList, "RoRoRo's rules file isn't a list of rules, so Ur Score can't show or change alerts. Fix it by hand, then come back.")]
    public void ARulesFileUrScoreCantUseIsSaidOnTheCardAndOffersNothing(RulesProblem problem, string note)
    {
        var view = AlertCards.Build([Sending("diamonds")], RulesRead.Unusable(problem));

        var card = Assert.Single(view.Cards);
        Assert.Equal(note, card.Note);
        Assert.Empty(card.CanAdd);
        Assert.False(view.ShowNext);
        Assert.Equal("", AlertCards.Rows(view, AlertsUi.Closed)[0].NoAlerts);
    }

    [Fact]
    public void RulesRoRoRoCantReadAreCountedOnTheirCard()
    {
        var one = new RulesRead(RulesProblem.None, true, [], [Diamonds]);
        var two = new RulesRead(RulesProblem.None, true, [], [Diamonds, Diamonds, Rank]);

        Assert.Equal("1 rule for Diamonds is written in a way RoRoRo can't read, so it never alerts. Ur Score leaves it as it is.",
            AlertCards.Build([Sending("diamonds")], one).Cards[0].Note);
        Assert.Equal("2 rules for Diamonds are written in a way RoRoRo can't read, so they never alert. Ur Score leaves them as they are.",
            AlertCards.Build([Sending("diamonds")], two).Cards[0].Note);
    }

    [Theory]
    [InlineData("100", AlertKind.Rate, 100d, "")]
    [InlineData(" 2,500.5 ", AlertKind.Rate, 2500.5, "")]
    [InlineData("1,000,000", AlertKind.Level, 1000000d, "")]
    [InlineData("0", AlertKind.Level, 0d, "")]
    [InlineData("0.25", AlertKind.Rate, 0.25, "")]
    [InlineData("", AlertKind.Level, 0d, "Type a number, like 100.")]
    [InlineData("lots", AlertKind.Rate, 0d, "Type a number, like 100.")]
    [InlineData("-5", AlertKind.Level, 0d, "Type a number, like 100.")]
    [InlineData("1e6", AlertKind.Rate, 0d, "Type a number, like 100.")]
    [InlineData("1,5", AlertKind.Rate, 0d, "Use a dot for decimals, like 1.5.")]
    [InlineData("1.255", AlertKind.Rate, 0d, "Use at most two decimal places, like 1.25.")]
    [InlineData("0", AlertKind.Rate, 0d, "Use a number above 0.")]
    [InlineData("1000000000000000", AlertKind.Level, 0d, "Use a number below 1,000,000,000,000,000.")]
    public void TheNumberYouTypeIsCheckedBeforeAnythingIsWritten(string typed, AlertKind kind, double expected, string problem)
    {
        Assert.Equal(problem, AlertCards.ParseNumber(typed, kind, out var value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void TurnOnBuildsTheRuleFromWhatYouFilledIn()
    {
        Assert.Equal((new AlertSpec(AlertKind.Rate, 250, 15, true, "Diamonds"), ""),
            AlertCards.Check(AlertKind.Rate, new AlertDraft { Number = "250", Minutes = "15" }, "Diamonds"));
        Assert.Equal((new AlertSpec(AlertKind.Level, 40, 0, false, "Player rank"), ""),
            AlertCards.Check(AlertKind.Level, new AlertDraft { Number = "40", Direction = AlertCards.Above }, "Player rank"));
        Assert.Equal(((AlertSpec?)null, "Choose how many minutes."),
            AlertCards.Check(AlertKind.Rate, new AlertDraft { Number = "250", Minutes = "" }, "Diamonds"));
        Assert.Equal(((AlertSpec?)null, "Choose above or below."),
            AlertCards.Check(AlertKind.Level, new AlertDraft { Number = "40", Direction = "sideways" }, "Player rank"));
        Assert.Equal(((AlertSpec?)null, "Type a number, like 100."),
            AlertCards.Check(AlertKind.Level, AlertCards.NewDraft(AlertKind.Level), "Player rank"));
    }

    [Fact]
    public void ANewAlertStartsFromTheUsualRuleAndChangeStartsFromTheRulesOwnValues()
    {
        var rate = AlertCards.NewDraft(AlertKind.Rate);
        Assert.Equal(("100", "10"), (rate.Number, rate.Minutes));
        var level = AlertCards.NewDraft(AlertKind.Level);
        Assert.Equal(("", AlertCards.Below), (level.Number, level.Direction));

        var changing = AlertCards.DraftOf(Rule(0, Diamonds, AlertKind.Rate, 1500.5, window: 20));
        Assert.Equal(("1500.5", "20"), (changing.Number, changing.Minutes));
        Assert.Equal(new[] { "10", "15", "20", "30" }, AlertCards.MinuteChoices(changing.Minutes));
        Assert.Equal(new[] { "10", "15", "30" }, AlertCards.MinuteChoices("15"));
        Assert.Equal(new[] { "10", "15", "30" }, AlertCards.MinuteChoices(null));
        Assert.Equal("10", AlertCards.DraftOf(Rule(0, Diamonds, AlertKind.Rate, 5)).Minutes);
        Assert.Equal(AlertCards.Above, AlertCards.DraftOf(Rule(0, Rank, AlertKind.Level, 40, below: false)).Direction);
    }

    [Fact]
    public void AddingAnAlertAsksWhichKindThenShowsItsSentenceToFillIn()
    {
        var view = AlertCards.Build([Sending("diamonds", "rank")], RulesOf(Rule(0, Diamonds, AlertKind.Rate, 100, window: 10)));

        var closed = AlertCards.Rows(view, AlertsUi.Closed)[0];
        Assert.True(closed.ShowAdd);
        Assert.Equal("Add an alert for Diamonds", closed.AddName);
        Assert.False(closed.ShowKinds || closed.ShowEditor);
        Assert.Equal(("Change the stops climbing alert for Diamonds", "Remove the stops climbing alert for Diamonds"), (closed.Lines[0].ChangeName, closed.Lines[0].RemoveName));

        var choosing = AlertCards.Rows(view, AlertCards.OpenAdd(Diamonds));
        Assert.False(choosing[0].ShowAdd);
        Assert.True(choosing[0].ShowKinds);
        Assert.False(choosing[0].ShowRateKind);
        Assert.True(choosing[0].ShowLevelKind);
        Assert.Equal("Crosses a number, for Diamonds", choosing[0].LevelKindName);
        Assert.True(choosing[1].ShowAdd);
        Assert.False(choosing[1].ShowKinds);

        var adding = AlertCards.ChooseKind(new AlertTarget(Diamonds, AlertKind.Level));
        var row = AlertCards.Rows(view, adding)[0];
        Assert.True(row.ShowLevelEditor);
        Assert.False(row.ShowRateEditor);
        Assert.Same(adding.Draft, row.Draft);
        Assert.Equal(("Turn on", "Turn on the alert for Diamonds", "Number for Diamonds"), (row.ConfirmText, row.ConfirmName, row.NumberName));
        Assert.Equal(new[] { AlertCards.Below, AlertCards.Above }, row.DirectionChoices);
    }

    [Fact]
    public void ChangeOpensTheSentenceInPlaceOfItsButtons()
    {
        var view = AlertCards.Build([Sending("diamonds")], RulesOf(Rule(0, Diamonds, AlertKind.Rate, 100, window: 20)));

        var changing = AlertCards.OpenChange(view.Cards[0].Alerts[0]);
        var row = AlertCards.Rows(view, changing)[0];

        Assert.Equal((AlertEditMode.Changing, AlertKind.Rate), (changing.Mode, changing.Kind));
        Assert.True(row.ShowRateEditor);
        Assert.Equal(("Save", "Save the alert for Diamonds"), (row.ConfirmText, row.ConfirmName));
        Assert.False(row.Lines[0].ShowChange || row.Lines[0].ShowRemove);
        Assert.False(row.ShowAdd);
        Assert.Equal(new[] { "10", "15", "20", "30" }, row.MinuteChoices);
    }

    [Fact]
    public void AfterTurnOnChangeOrRemoveTheCardSaysWhatHappened()
    {
        var spec = new AlertSpec(AlertKind.Rate, 250, 15, true, "Diamonds");
        var adding = AlertCards.ChooseKind(new AlertTarget(Diamonds, AlertKind.Rate));
        var changing = adding with { Mode = AlertEditMode.Changing };

        Assert.Equal(
            new AlertsUi(ResultMetricId: Diamonds, Result: "On. RoRoRo will alert you when an account's Diamonds gains fewer than 250 a minute for 15 minutes."),
            AlertCards.AfterWrite(adding, RuleWrite.Done, spec));
        Assert.Equal("Changed. RoRoRo will now alert you when an account's Diamonds gains fewer than 250 a minute for 15 minutes.",
            AlertCards.AfterWrite(changing, RuleWrite.Done, spec).Result);

        var locked = AlertCards.AfterWrite(adding, RuleWrite.CantWrite, spec);
        Assert.Equal(AlertEditMode.Adding, locked.Mode);
        Assert.Same(adding.Draft, locked.Draft);
        Assert.Equal("Ur Score couldn't save RoRoRo's rules file. It may be locked by another program. Nothing was changed; try again in a moment.", locked.Problem);

        Assert.Equal(
            new AlertsUi(ResultMetricId: Diamonds, Result: "That alert isn't in RoRoRo's rules file any more, so nothing was changed.", ResultIsProblem: true),
            AlertCards.AfterWrite(changing, RuleWrite.NotThere, spec));

        var line = new AlertLine(Rule(2, Rank, AlertKind.Level, 40), "", "", true);
        var target = new AlertTarget(Rank, AlertKind.Level);
        Assert.Equal("Removed. RoRoRo won't alert you when an account's Player rank goes below 40 any more.",
            AlertCards.AfterRemove(target, line, "Player rank", RuleWrite.Done).Result);
        Assert.True(AlertCards.AfterRemove(target, line, "Player rank", RuleWrite.CantOpen).ResultIsProblem);
    }

    [Fact]
    public void KeyboardFocusGoesWhereYourNextKeyPressIsUseful()
    {
        var none = AlertCards.Build([Sending("diamonds")], RulesOf());
        var rateOn = AlertCards.Build([Sending("diamonds")], RulesOf(Rule(0, Diamonds, AlertKind.Rate, 100, window: 10)));
        var bothOn = AlertCards.Build([Sending("diamonds")], RulesOf(Rule(0, Diamonds, AlertKind.Rate, 100, window: 10), Rule(1, Diamonds, AlertKind.Level, 4)));

        // + Add an alert, then a kind.
        Assert.Equal("Stops climbing, for Diamonds", AlertCards.FocusName(none, Diamonds, AlertCards.OpenAdd(Diamonds), AlertKind.Rate));
        Assert.Equal("Crosses a number, for Diamonds", AlertCards.FocusName(rateOn, Diamonds, AlertCards.OpenAdd(Diamonds), AlertKind.Rate));
        Assert.Equal("Number for Diamonds", AlertCards.FocusName(none, Diamonds, AlertCards.ChooseKind(new AlertTarget(Diamonds, AlertKind.Level)), AlertKind.Level));

        // After Turn on or Save: that alert's Change. After Remove: + Add an alert, else the first Change.
        Assert.Equal("Change the stops climbing alert for Diamonds", AlertCards.FocusName(rateOn, Diamonds, AlertsUi.Closed, AlertKind.Rate));
        Assert.Equal("Add an alert for Diamonds", AlertCards.FocusName(none, Diamonds, AlertsUi.Closed, AlertKind.Rate));
        Assert.Equal("Change the stops climbing alert for Diamonds", AlertCards.FocusName(bothOn, Diamonds, AlertsUi.Closed, AlertKind.Event));
        Assert.Equal("", AlertCards.FocusName(none, "gone.metric", AlertsUi.Closed, AlertKind.Rate));

        // Cancel goes back to the button that opened the editor.
        Assert.Equal("Add an alert for Diamonds", AlertCards.FocusAfterCancel(rateOn, AlertCards.ChooseKind(new AlertTarget(Diamonds, AlertKind.Level))));
        Assert.Equal("Change the stops climbing alert for Diamonds", AlertCards.FocusAfterCancel(rateOn, AlertCards.OpenChange(rateOn.Cards[0].Alerts[0])));
    }

    [Fact]
    public void ARedrawThatChangesNothingIsSkipped()
    {
        IReadOnlyList<InstalledRecipe> installed = [Sending("diamonds")];
        var a = AlertCards.Build(installed, RulesOf(Rule(0, Diamonds, AlertKind.Rate, 100, window: 10)));
        var b = AlertCards.Build(installed, RulesOf(Rule(0, Diamonds, AlertKind.Rate, 100, window: 10)));
        var c = AlertCards.Build(installed, RulesOf(Rule(0, Diamonds, AlertKind.Rate, 250, window: 10)));

        Assert.True(AlertCards.Same(a, b));
        Assert.False(AlertCards.Same(a, c));
        Assert.False(AlertCards.Same(a, AlertCards.Empty));
    }

    [Fact]
    public void TheStatsTableSaysHowManyAlertsAStatHas()
    {
        var rules = RulesOf(Rule(0, Diamonds, AlertKind.Rate, 100, window: 10), Rule(1, Rank, AlertKind.Rate, 1, RuleOwner.You), Rule(2, Rank, AlertKind.Level, 4));

        Assert.Equal("No alert yet. Add one in Setup › Alerts.", AlertCards.StatLine(rules, "ps99.eggs-hatched"));
        Assert.Equal("1 alert in RoRoRo. See it in Setup › Alerts.", AlertCards.StatLine(rules, Diamonds));
        Assert.Equal("2 alerts in RoRoRo. See them in Setup › Alerts.", AlertCards.StatLine(rules, Rank));
        Assert.Equal("RoRoRo's rules file can't be read right now. Setup › Alerts says why.", AlertCards.StatLine(RulesRead.Unusable(RulesProblem.NotJson), Diamonds));
    }

    [Fact]
    public void ThePageSaysWhatToDoWhenThereIsNoCard()
    {
        Assert.Equal("Import a recipe first.", AlertCards.EmptyLine([], AlertCards.Empty));
        Assert.Equal("No stat is sent to RoRoRo yet. Tick Send on a stat in Setup › Stats, and it gets a card here.",
            AlertCards.EmptyLine([Sending()], AlertCards.Build([Sending()], RulesOf())));
        Assert.Equal("", AlertCards.EmptyLine([Sending("diamonds")], AlertCards.Build([Sending("diamonds")], RulesOf())));
    }

    [Fact]
    public void AResultWhoseCardIsGoneShowsUnderTheCards()
    {
        var view = AlertCards.Build([Sending("diamonds")], RulesOf());
        var removed = new AlertsUi(ResultMetricId: Rank, Result: "Removed.");

        Assert.Equal("Removed.", AlertCards.OrphanResult(view, removed));
        Assert.Equal("", AlertCards.OrphanResult(view, removed with { ResultMetricId = Diamonds }));
        Assert.Equal("", AlertCards.OrphanResult(view, AlertsUi.Closed));
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`
Expected: FAIL to compile (`AlertCards`, `AlertsUi`, `AlertDraft` and the other Task 2 names don't exist).

- [ ] **Step 3: The model**

Create `src/UI/Setup/AlertCards.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

/// <summary>A stat that gets a card: one you send, or one you no longer send that Ur Score still has an alert for (A11).</summary>
public sealed record AlertStat(string MetricId, string Label, bool Sent);

/// <summary>One alert on a card, as a sentence. <see cref="Managed"/> marks the one Ur Score's Change and Remove act on (A5).</summary>
public sealed record AlertLine(AlertRule Rule, string Sentence, string Mark, bool Managed);

/// <summary>A stat's card: its alerts, the kinds + Add an alert can still offer, and a note (a stale stat, a file problem, unreadable rows).</summary>
public sealed record AlertCard(AlertStat Stat, IReadOnlyList<AlertLine> Alerts, IReadOnlyList<AlertKind> CanAdd, string Note);

/// <summary>Every card, why the rules file can't be used (when it can't), and whether the "Next, in RoRoRo" line shows (A16).</summary>
public sealed record AlertsView(IReadOnlyList<AlertCard> Cards, RulesProblem Problem, bool ShowNext);

/// <summary>What the inline editor holds while you type, bound to its controls. Text, so a half-typed number stays as typed.</summary>
public sealed class AlertDraft
{
    public string Number { get; set; } = "";

    public string Minutes { get; set; } = "";

    public string Direction { get; set; } = AlertCards.Below;
}

public enum AlertEditMode { None, ChoosingKind, Adding, Changing }

/// <summary>The page's one open editor and its one result line (A13). Never written anywhere.</summary>
public sealed record AlertsUi(
    string? MetricId = null, AlertEditMode Mode = AlertEditMode.None, AlertKind Kind = AlertKind.Rate, AlertDraft? Draft = null,
    string Problem = "", string? ResultMetricId = null, string Result = "", bool ResultIsProblem = false)
{
    public static AlertsUi Closed { get; } = new();
}

/// <summary>What a kind, Change or Remove button acts on.</summary>
public sealed record AlertTarget(string MetricId, AlertKind Kind);

/// <summary>One alert as the page draws it.</summary>
public sealed record AlertLineRow(
    string Sentence, string Mark, AlertTarget Target, bool ShowChange, bool ShowRemove, string ChangeName, string RemoveName)
{
    public bool HasMark => Mark.Length > 0;

    public override string ToString() => Sentence;
}

/// <summary>One card as the page draws it: every part's visibility, text and accessible name.</summary>
public sealed record AlertCardRow(
    string MetricId, string Title, string Note, IReadOnlyList<AlertLineRow> Lines, string LinesName, string NoAlerts,
    bool ShowAdd, string AddName,
    bool ShowKinds, bool ShowRateKind, bool ShowLevelKind, AlertTarget RateTarget, AlertTarget LevelTarget, string RateKindName, string LevelKindName,
    bool ShowRateEditor, bool ShowLevelEditor, AlertDraft? Draft, IReadOnlyList<string> MinuteChoices,
    string ConfirmText, string ConfirmName, string CancelName, string NumberName, string MinutesName, string DirectionName,
    string Problem, string Result, bool ResultIsProblem)
{
    public bool HasNote => Note.Length > 0;

    public bool HasNoAlerts => NoAlerts.Length > 0;

    public bool ShowEditor => ShowRateEditor || ShowLevelEditor;

    public bool HasProblem => Problem.Length > 0;

    public bool ShowResult => Result.Length > 0 && !ResultIsProblem;

    public bool ShowResultProblem => Result.Length > 0 && ResultIsProblem;

    public IReadOnlyList<string> DirectionChoices => AlertCards.DirectionChoices;

    public override string ToString() => Title;
}

/// <summary>
/// Setup › Alerts' cards (the alerts card design): every sent stat's alerts as sentences in your words, what + Add an alert
/// offers, what you typed checked before anything is written, the page's one open editor, and where keyboard focus goes.
/// Pure: the page reads the file, calls these, writes through <see cref="RulesFile"/> and draws.
/// </summary>
public static partial class AlertCards
{
    public const double DefaultThreshold = 100;
    public const double DefaultMinutes = 10;
    public const double MaxNumber = 1e15;
    public const string Below = "below";
    public const string Above = "above";

    public const string NextInRoRoRo = "Next, in RoRoRo: Settings › Alerts › turn on Metric alerts and choose where they go (desktop, Discord, phone).";
    public const string NoRecipe = "Import a recipe first.";
    public const string NoSentStat = "No stat is sent to RoRoRo yet. Tick Send on a stat in Setup › Stats, and it gets a card here.";
    public const string TypeANumber = "Type a number, like 100.";
    public const string UseADot = "Use a dot for decimals, like 1.5.";
    public const string TwoDecimals = "Use at most two decimal places, like 1.25.";
    public const string AboveZero = "Use a number above 0.";
    public const string TooBig = "Use a number below 1,000,000,000,000,000.";
    public const string ChooseMinutes = "Choose how many minutes.";
    public const string ChooseDirection = "Choose above or below.";

    private static readonly AlertKind[] Offered = [AlertKind.Rate, AlertKind.Level];

    public static IReadOnlyList<double> Minutes { get; } = [10, 15, 30];

    public static IReadOnlyList<string> DirectionChoices { get; } = [Below, Above];

    public static AlertsView Empty { get; } = new([], RulesProblem.None, false);

    // ---- cards ----

    /// <summary>A card per sent metric id in recipe order (A10), then one per metric id Ur Score has a rule for that you no longer send (A11).</summary>
    public static AlertsView Build(IReadOnlyList<InstalledRecipe> installed, RulesRead rules)
    {
        var recipes = installed.Where(i => !i.Recipe.IsGroupList).ToList();
        var sent = recipes
            .SelectMany(i => i.State.SentStats(i.Recipe))
            .GroupBy(s => s.MetricId, StringComparer.Ordinal)
            .Select(g => new AlertStat(g.Key, g.First().Label, Sent: true))
            .ToList();
        var sentIds = sent.Select(s => s.MetricId).ToHashSet(StringComparer.Ordinal);
        var stale = rules.Rules
            .Where(r => r.Owner == RuleOwner.UrScore && !sentIds.Contains(r.MetricId))
            .GroupBy(r => r.MetricId, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new AlertStat(g.Key, PinnedLabel(recipes, g.Key) ?? g.Select(r => r.Label).FirstOrDefault(l => l is not null) ?? g.Key, Sent: false));

        var cards = sent.Concat(stale).Select(stat => Card(stat, rules)).ToList();
        var showNext = rules.Problem == RulesProblem.None && cards.Any(c => c.Stat.Sent && c.Alerts.Count > 0);
        return new AlertsView(cards, rules.Problem, showNext);
    }

    /// <summary>Whether two views say the same thing, so a refresh that changes nothing doesn't redraw (A14).</summary>
    public static bool Same(AlertsView a, AlertsView b) =>
        a.Problem == b.Problem && a.ShowNext == b.ShowNext && a.Cards.Count == b.Cards.Count
        && a.Cards.Zip(b.Cards).All(pair =>
            pair.First.Stat == pair.Second.Stat && pair.First.Note == pair.Second.Note
            && pair.First.CanAdd.SequenceEqual(pair.Second.CanAdd) && pair.First.Alerts.SequenceEqual(pair.Second.Alerts));

    public static AlertCard? CardFor(AlertsView view, string metricId) =>
        view.Cards.FirstOrDefault(c => string.Equals(c.Stat.MetricId, metricId, StringComparison.Ordinal));

    /// <summary>The alert a Change or Remove button stands for: Ur Score's managed rule of that kind on that card.</summary>
    public static AlertLine? Managed(AlertsView view, AlertTarget target) =>
        CardFor(view, target.MetricId)?.Alerts.FirstOrDefault(a => a.Managed && a.Rule.Kind == target.Kind);

    private static AlertCard Card(AlertStat stat, RulesRead rules)
    {
        var managed = new HashSet<AlertKind>();
        var lines = new List<AlertLine>();
        foreach (var rule in rules.For(stat.MetricId))
        {
            var isManaged = rule.Owner == RuleOwner.UrScore && managed.Add(rule.Kind);
            var mark = rule.Owner switch
            {
                RuleOwner.You => "yours",
                RuleOwner.AnotherPlugin => "another plugin's",
                _ => isManaged ? "" : "a copy",
            };

            // The stat's label from the recipe, whatever the rule's own label says (A4).
            lines.Add(new AlertLine(rule, Sentence(rule.Kind, stat.Label, rule.Threshold, rule.WindowMinutes, rule.AlertWhenBelow), mark, isManaged));
        }

        IReadOnlyList<AlertKind> canAdd = stat.Sent && rules.Problem == RulesProblem.None ? [.. Offered.Where(k => !managed.Contains(k))] : [];
        return new AlertCard(stat, lines, canAdd, Note(stat, rules));
    }

    private static string Note(AlertStat stat, RulesRead rules)
    {
        if (rules.Problem != RulesProblem.None) return ProblemNote(rules.Problem);

        var parts = new List<string>();
        if (!stat.Sent) parts.Add($"You don't send {stat.Label} any more, so its alerts can't fire. Tick Send in Setup › Stats, or remove them.");

        var skipped = rules.SkippedFor(stat.MetricId);
        if (skipped == 1) parts.Add($"1 rule for {stat.Label} is written in a way RoRoRo can't read, so it never alerts. Ur Score leaves it as it is.");
        if (skipped > 1) parts.Add($"{skipped} rules for {stat.Label} are written in a way RoRoRo can't read, so they never alert. Ur Score leaves them as they are.");

        return string.Join(" ", parts);
    }

    /// <summary>The label of a recipe stat pinned to this metric id, sent or not.</summary>
    private static string? PinnedLabel(IEnumerable<InstalledRecipe> recipes, string metricId) =>
        recipes
            .SelectMany(i => i.State.StatChoices
                .Where(c => string.Equals(c.Value.MetricId.Trim(), metricId, StringComparison.Ordinal))
                .Select(c => RecipeStats.Find(i.Recipe, c.Key)?.Label))
            .FirstOrDefault(label => label is not null);

    // ---- words ----

    public static string Number(double value) => value.ToString("#,0.##", CultureInfo.InvariantCulture);

    public static string Editable(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>The part after "Alert me when", also used by the result lines.</summary>
    public static string Condition(AlertKind kind, string label, double threshold, double windowMinutes, bool below) => kind switch
    {
        AlertKind.Rate => $"an account's {label} gains fewer than {Number(threshold)} a minute for {Number(windowMinutes)} {(windowMinutes == 1 ? "minute" : "minutes")}",
        AlertKind.Level => $"an account's {label} goes {(below ? Below : Above)} {Number(threshold)}",
        _ => $"an account's {label} changes",
    };

    public static string Sentence(AlertKind kind, string label, double threshold, double windowMinutes, bool below) =>
        $"Alert me when {Condition(kind, label, threshold, windowMinutes, below)}.";

    public static string ProblemNote(RulesProblem problem) => problem switch
    {
        RulesProblem.CantOpen => "RoRoRo's rules file is locked or can't be opened, so Ur Score can't show or change alerts right now. Try again in a moment.",
        RulesProblem.NotJson => "RoRoRo's rules file has a mistake in it, so Ur Score can't show or change alerts. Fix it by hand, then come back.",
        RulesProblem.NotAList => "RoRoRo's rules file isn't a list of rules, so Ur Score can't show or change alerts. Fix it by hand, then come back.",
        _ => "",
    };

    public static string Failed(RuleWrite outcome, string label) => outcome switch
    {
        RuleWrite.AlreadyThere => $"Ur Score already has an alert of this kind for {label}, so nothing was added. Change that one instead.",
        RuleWrite.NotThere => "That alert isn't in RoRoRo's rules file any more, so nothing was changed.",
        RuleWrite.CantOpen => "RoRoRo's rules file is locked or can't be opened, so nothing was changed. Try again in a moment.",
        RuleWrite.CantWrite => "Ur Score couldn't save RoRoRo's rules file. It may be locked by another program. Nothing was changed; try again in a moment.",
        RuleWrite.NotJson => "RoRoRo's rules file has a mistake in it, so Ur Score won't change it. Nothing was changed. Fix it by hand, then come back.",
        RuleWrite.NotAList => "RoRoRo's rules file isn't a list of rules, so Ur Score won't change it. Nothing was changed.",
        _ => "",
    };

    private static string KindWords(AlertKind kind) => kind switch
    {
        AlertKind.Rate => "stops climbing",
        AlertKind.Level => "crosses a number",
        _ => "changes",
    };

    // ---- accessible names (A18) ----

    public static string AddName(string label) => $"Add an alert for {label}";

    public static string KindName(AlertKind kind, string label)
    {
        var words = KindWords(kind);
        return $"{char.ToUpperInvariant(words[0])}{words[1..]}, for {label}";
    }

    public static string ChangeName(AlertKind kind, string label) => $"Change the {KindWords(kind)} alert for {label}";

    public static string RemoveName(AlertKind kind, string label) => $"Remove the {KindWords(kind)} alert for {label}";

    public static string NumberName(string label) => $"Number for {label}";

    public static string MinutesName(string label) => $"Minutes for {label}";

    public static string DirectionName(string label) => $"Above or below for {label}";

    public static string CancelName(string label) => $"Cancel, for {label}";

    // ---- what you type (A12) ----

    public static AlertDraft NewDraft(AlertKind kind) => kind == AlertKind.Rate
        ? new AlertDraft { Number = Editable(DefaultThreshold), Minutes = Editable(DefaultMinutes) }
        : new AlertDraft();

    public static AlertDraft DraftOf(AlertRule rule) => new()
    {
        Number = Editable(rule.Threshold),
        Minutes = Editable(rule.WindowMinutes > 0 ? rule.WindowMinutes : DefaultMinutes),
        Direction = rule.AlertWhenBelow ? Below : Above,
    };

    /// <summary>10, 15 and 30, plus the minutes an existing rule already has, so saving never changes them unasked.</summary>
    public static IReadOnlyList<string> MinuteChoices(string? current)
    {
        var minutes = Minutes.ToList();
        if (double.TryParse(current, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) && value > 0 && !minutes.Contains(value))
        {
            minutes.Add(value);
        }

        return [.. minutes.Order().Select(Editable)];
    }

    [GeneratedRegex(@"^([0-9]{1,3}(,[0-9]{3})+|[0-9]+)(\.[0-9]{1,2})?$")]
    private static partial Regex PlainNumber();

    [GeneratedRegex(@"^[0-9]+,[0-9]{1,2}$")]
    private static partial Regex CommaDecimal();

    [GeneratedRegex(@"^([0-9]{1,3}(,[0-9]{3})+|[0-9]+)\.[0-9]{3,}$")]
    private static partial Regex LongDecimal();

    /// <summary>"" and the number, or why it can't be used. Digits only (ASCII), commas in threes, a dot and up to two decimals.</summary>
    public static string ParseNumber(string? text, AlertKind kind, out double value)
    {
        value = 0;
        var typed = (text ?? "").Trim();
        if (CommaDecimal().IsMatch(typed)) return UseADot;
        if (LongDecimal().IsMatch(typed)) return TwoDecimals;
        if (!PlainNumber().IsMatch(typed)) return TypeANumber;

        var number = double.Parse(typed.Replace(",", "", StringComparison.Ordinal), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        if (number >= MaxNumber) return TooBig;
        if (kind == AlertKind.Rate && number <= 0) return AboveZero;

        value = number;
        return "";
    }

    /// <summary>The rule Turn on or Save writes, or why it can't be written yet. Nothing is written by this.</summary>
    public static (AlertSpec? Spec, string Problem) Check(AlertKind kind, AlertDraft draft, string label)
    {
        var problem = ParseNumber(draft.Number, kind, out var threshold);
        if (problem.Length > 0) return (null, problem);

        if (kind == AlertKind.Rate)
        {
            return double.TryParse(draft.Minutes, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var minutes) && minutes > 0
                ? (new AlertSpec(kind, threshold, minutes, AlertWhenBelow: true, label), "")
                : (null, ChooseMinutes);
        }

        return draft.Direction is Below or Above
            ? (new AlertSpec(kind, threshold, 0, draft.Direction == Below, label), "")
            : (null, ChooseDirection);
    }

    // ---- the editor's steps (A13) ----

    public static AlertsUi OpenAdd(string metricId) => new(metricId, AlertEditMode.ChoosingKind);

    public static AlertsUi ChooseKind(AlertTarget target) => new(target.MetricId, AlertEditMode.Adding, target.Kind, NewDraft(target.Kind));

    public static AlertsUi OpenChange(AlertLine line) => new(line.Rule.MetricId, AlertEditMode.Changing, line.Rule.Kind, DraftOf(line.Rule));

    /// <summary>After Turn on or Save wrote (or didn't): the result on the card; a file that can't be opened or written keeps the editor open.</summary>
    public static AlertsUi AfterWrite(AlertsUi ui, RuleWrite outcome, AlertSpec spec)
    {
        var condition = Condition(spec.Kind, spec.Label, spec.Threshold, spec.WindowMinutes, spec.AlertWhenBelow);
        return outcome switch
        {
            RuleWrite.Done when ui.Mode == AlertEditMode.Changing => Said(ui.MetricId, $"Changed. RoRoRo will now alert you when {condition}.", false),
            RuleWrite.Done => Said(ui.MetricId, $"On. RoRoRo will alert you when {condition}.", false),
            RuleWrite.CantOpen or RuleWrite.CantWrite => ui with { Problem = Failed(outcome, spec.Label) },
            _ => Said(ui.MetricId, Failed(outcome, spec.Label), true),
        };
    }

    public static AlertsUi AfterRemove(AlertTarget target, AlertLine line, string label, RuleWrite outcome)
    {
        var rule = line.Rule;
        return outcome == RuleWrite.Done
            ? Said(target.MetricId, $"Removed. RoRoRo won't alert you when {Condition(rule.Kind, label, rule.Threshold, rule.WindowMinutes, rule.AlertWhenBelow)} any more.", false)
            : Said(target.MetricId, Failed(outcome, label), true);
    }

    private static AlertsUi Said(string? metricId, string result, bool problem) => new(ResultMetricId: metricId, Result: result, ResultIsProblem: problem);

    // ---- rows ----

    public static IReadOnlyList<AlertCardRow> Rows(AlertsView view, AlertsUi ui) => [.. view.Cards.Select(card => Row(card, ui))];

    private static AlertCardRow Row(AlertCard card, AlertsUi ui)
    {
        var id = card.Stat.MetricId;
        var label = card.Stat.Label;
        var mode = string.Equals(ui.MetricId, id, StringComparison.Ordinal) ? ui.Mode : AlertEditMode.None;
        var editing = mode is AlertEditMode.Adding or AlertEditMode.Changing;

        var lines = card.Alerts.Select(a =>
        {
            var underChange = mode == AlertEditMode.Changing && a.Managed && a.Rule.Kind == ui.Kind;
            return new AlertLineRow(
                a.Sentence, a.Mark, new AlertTarget(id, a.Rule.Kind),
                ShowChange: a.Managed && card.Stat.Sent && a.Rule.Kind != AlertKind.Event && !underChange,
                ShowRemove: a.Managed && !underChange,
                ChangeName(a.Rule.Kind, label), RemoveName(a.Rule.Kind, label));
        }).ToList();

        return new AlertCardRow(
            MetricId: id, Title: label, Note: card.Note, Lines: lines, LinesName: $"Alerts for {label}",
            NoAlerts: card.Alerts.Count == 0 && card.Note.Length == 0 ? "No alerts yet." : "",
            ShowAdd: mode == AlertEditMode.None && card.CanAdd.Count > 0, AddName: AddName(label),
            ShowKinds: mode == AlertEditMode.ChoosingKind,
            ShowRateKind: mode == AlertEditMode.ChoosingKind && card.CanAdd.Contains(AlertKind.Rate),
            ShowLevelKind: mode == AlertEditMode.ChoosingKind && card.CanAdd.Contains(AlertKind.Level),
            RateTarget: new AlertTarget(id, AlertKind.Rate), LevelTarget: new AlertTarget(id, AlertKind.Level),
            RateKindName: KindName(AlertKind.Rate, label), LevelKindName: KindName(AlertKind.Level, label),
            ShowRateEditor: editing && ui.Kind == AlertKind.Rate, ShowLevelEditor: editing && ui.Kind == AlertKind.Level,
            Draft: editing ? ui.Draft : null, MinuteChoices: MinuteChoices(editing ? ui.Draft?.Minutes : null),
            ConfirmText: mode == AlertEditMode.Changing ? "Save" : "Turn on",
            ConfirmName: mode == AlertEditMode.Changing ? $"Save the alert for {label}" : $"Turn on the alert for {label}",
            CancelName: CancelName(label), NumberName: NumberName(label), MinutesName: MinutesName(label), DirectionName: DirectionName(label),
            Problem: editing ? ui.Problem : "",
            Result: string.Equals(ui.ResultMetricId, id, StringComparison.Ordinal) ? ui.Result : "",
            ResultIsProblem: ui.ResultIsProblem);
    }

    // ---- focus (A15) ----

    /// <summary>The accessible name of the control keyboard focus goes to after the page redraws, or "" to leave it.</summary>
    public static string FocusName(AlertsView view, string metricId, AlertsUi ui, AlertKind kind)
    {
        if (CardFor(view, metricId) is not { } card) return "";
        var label = card.Stat.Label;

        if (string.Equals(ui.MetricId, metricId, StringComparison.Ordinal) && ui.Mode != AlertEditMode.None)
        {
            if (ui.Mode != AlertEditMode.ChoosingKind) return NumberName(label);
            return card.CanAdd.Count > 0 ? KindName(card.CanAdd[0], label) : CancelName(label);
        }

        if (card.Alerts.FirstOrDefault(a => a.Managed && a.Rule.Kind == kind) is { } acted && CanChange(card, acted)) return ChangeName(kind, label);
        if (card.CanAdd.Count > 0) return AddName(label);
        return card.Alerts.FirstOrDefault(a => a.Managed) is { } first
            ? CanChange(card, first) ? ChangeName(first.Rule.Kind, label) : RemoveName(first.Rule.Kind, label)
            : "";
    }

    /// <summary>Cancel goes back to the button that opened the editor: + Add an alert, or that alert's Change.</summary>
    public static string FocusAfterCancel(AlertsView view, AlertsUi open)
    {
        if (open.MetricId is not { } id || CardFor(view, id) is not { } card) return "";
        return open.Mode != AlertEditMode.Changing && card.CanAdd.Count > 0
            ? AddName(card.Stat.Label)
            : FocusName(view, id, AlertsUi.Closed, open.Kind);
    }

    private static bool CanChange(AlertCard card, AlertLine line) => card.Stat.Sent && line.Rule.Kind != AlertKind.Event;

    // ---- lines around the cards ----

    public static string EmptyLine(IReadOnlyList<InstalledRecipe> installed, AlertsView view) =>
        installed.Count == 0 ? NoRecipe : view.Cards.Count == 0 ? NoSentStat : "";

    /// <summary>A result whose card went away (the last alert of a stat you no longer send was removed) shows under the cards (A13).</summary>
    public static string OrphanResult(AlertsView view, AlertsUi ui) =>
        ui.ResultMetricId is { } id && ui.Result.Length > 0 && CardFor(view, id) is null ? ui.Result : "";

    /// <summary>The Stats table's line for a sent stat (A17).</summary>
    public static string StatLine(RulesRead rules, string metricId)
    {
        if (rules.Problem != RulesProblem.None) return "RoRoRo's rules file can't be read right now. Setup › Alerts says why.";

        return rules.For(metricId).Count switch
        {
            0 => "No alert yet. Add one in Setup › Alerts.",
            1 => "1 alert in RoRoRo. See it in Setup › Alerts.",
            var n => $"{n} alerts in RoRoRo. See them in Setup › Alerts.",
        };
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror` then `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~AlertCardsTests"`
Expected: PASS.

- [ ] **Step 5: The build gate**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror` then `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: both pass.

- [ ] **Step 6: Commit**

```bash
git add src/UI/Setup/AlertCards.cs tests/AlertCardsTests.cs
git commit -m "alerts: cards whose alerts read as sentences, checked input, and each step of the inline editor

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: The themed Alerts page, the rules path, and the old helper retired

**Files:**
- Modify: `src/Composition/ISetupServices.cs`, `src/Composition/AppServices.cs` (`RulesPath`, one trail line)
- Modify (replace whole): `src/UI/Setup/AlertsPage.xaml`, `src/UI/Setup/AlertsPage.xaml.cs`, `src/UI/Setup/AlertsModel.cs`
- Modify: `src/UI/Setup/StatsPage.xaml.cs` (`Load`), `src/UI/Setup/ImportFlow.cs` (the `ImportWindow` call)
- Modify: `src/Core/RulesFile.cs` (the old API removed)
- Delete: `src/Core/RuleInventory.cs`
- Test: `tests/AlertsPageFenceTests.cs` (new), `tests/AlertsModelTests.cs` (replace whole)

**Interfaces:**
- Consumes: Task 1's `RulesFile.Read/TurnOn/Change/Remove/ResolvePath/PathVariable/DefaultPath`, `AlertKind`, `RuleWrite`; Task 2's `AlertCards`, `AlertsView`, `AlertsUi`, `AlertEditMode`, `AlertTarget`, `AlertCardRow`, `AlertLineRow`, `AlertDraft`.
- Produces: `ISetupServices.RulesPath`; the automation ids and names in the table above (Task 4 walks them).

- [ ] **Step 1: Write the failing fence test**

Create `tests/AlertsPageFenceTests.cs`:

```csharp
namespace UrScore.Tests;

/// <summary>
/// Setup › Alerts says everything in the theme, on the card: no stock message box, no default tooltip (owner rule, backlog
/// V3-S.10), and no rule serialised to JSON for the screen (the alerts card design: "No JSON on screen").
/// </summary>
public class AlertsPageFenceTests
{
    [Fact]
    public void TheAlertsPageRaisesNoStockMessageBoxOrToolTipAndShowsNoJson()
    {
        var setup = Path.Combine(RepoRoot(), "src", "UI", "Setup");
        var page = File.ReadAllText(Path.Combine(setup, "AlertsPage.xaml")) + File.ReadAllText(Path.Combine(setup, "AlertsPage.xaml.cs"));

        Assert.DoesNotContain("MessageBox", page);
        Assert.DoesNotContain("ToolTip", page);
        Assert.DoesNotContain("ToJsonString", page);
        Assert.DoesNotContain("Preview", page);
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
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror` then `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~AlertsPageFenceTests"`
Expected: FAIL (`AlertsPage.xaml.cs` calls `MessageBox.Show` and `RulesFile.Preview`).

- [ ] **Step 3: The rules path**

`src/Composition/ISetupServices.cs`, after the `HostText` member:

```csharp
    /// <summary>The metric rules file Setup › Alerts reads and writes: RoRoRo's, unless a walk names a scratch copy (plan A1).</summary>
    string RulesPath { get; }
```

`src/Composition/AppServices.cs`, after the `HostText` property:

```csharp
    /// <summary>RoRoRo's rules file, or the full path <c>UR_SCORE_RULES_FILE</c> names: the Setup › Alerts walk's scratch copy (plan A1).</summary>
    public string RulesPath { get; } = RulesFile.ResolvePath(Environment.GetEnvironmentVariable(RulesFile.PathVariable));
```

and in the constructor, right after `Settings = Settings.Load();`:

```csharp
        // A walk's scratch rules file is never silent: Diagnostics' trail says which file alerts use.
        if (!string.Equals(RulesPath, RulesFile.DefaultPath, StringComparison.OrdinalIgnoreCase))
        {
            AddTrail($"RULES: alerts use the file {RulesFile.PathVariable} names, not RoRoRo's.");
        }
```

- [ ] **Step 4: The page**

Replace `src/UI/Setup/AlertsPage.xaml` with:

```xml
<UserControl x:Class="Labs626.UrScore.UI.AlertsPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="clr-namespace:Labs626.UrScore.UI">
    <UserControl.Resources>
        <!-- A word of an editable sentence, lined up with the boxes beside it. -->
        <Style x:Key="SentenceText" TargetType="TextBlock">
            <Setter Property="VerticalAlignment" Value="Center" />
            <Setter Property="Margin" Value="0,0,6,6" />
        </Style>

        <!-- One alert (plan A5, A6): its sentence, whose it is when it isn't Ur Score's own, and Change and Remove on the one Ur Score manages. -->
        <DataTemplate x:Key="AlertLineTemplate">
            <Grid Margin="0,0,0,6">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <TextBlock Grid.Column="0" AutomationProperties.AutomationId="AlertSentence" Text="{Binding Sentence}"
                           TextWrapping="Wrap" VerticalAlignment="Center" />
                <Border Grid.Column="1" BorderBrush="{DynamicResource EdgeBrush}" BorderThickness="1" CornerRadius="9"
                        Padding="7,1" Margin="10,0,0,0" VerticalAlignment="Center"
                        Visibility="{Binding HasMark, Converter={StaticResource BoolToVisible}}">
                    <TextBlock AutomationProperties.AutomationId="AlertMark" Text="{Binding Mark}" FontFamily="{StaticResource MonoFont}"
                               FontSize="10.5" Foreground="{DynamicResource MutedTextBrush}" />
                </Border>
                <StackPanel Grid.Column="2" Orientation="Horizontal" Margin="10,0,0,0" VerticalAlignment="Center">
                    <Button Content="Change" Tag="{Binding Target}" Click="OnChangeClick" Margin="0,0,8,0"
                            Visibility="{Binding ShowChange, Converter={StaticResource BoolToVisible}}"
                            AutomationProperties.Name="{Binding ChangeName}" />
                    <Button Content="Remove" Tag="{Binding Target}" Click="OnRemoveClick"
                            Visibility="{Binding ShowRemove, Converter={StaticResource BoolToVisible}}"
                            AutomationProperties.Name="{Binding RemoveName}" />
                </StackPanel>
            </Grid>
        </DataTemplate>

        <!-- One sent stat's card: its alerts, + Add an alert, the kind choice, the sentence to fill in, and what happened. -->
        <DataTemplate x:Key="AlertCardTemplate">
            <Border Style="{StaticResource Card}" Padding="12,10" Margin="0,0,0,12">
                <StackPanel>
                    <TextBlock AutomationProperties.AutomationId="AlertCardTitle" Text="{Binding Title}" FontWeight="SemiBold"
                               FontSize="14" Margin="0,0,0,6" />
                    <TextBlock AutomationProperties.AutomationId="AlertCardNote" Text="{Binding Note}" Style="{StaticResource Muted}"
                               Margin="0,0,0,6" Visibility="{Binding HasNote, Converter={StaticResource BoolToVisible}}" />
                    <ui:RowList ItemsSource="{Binding Lines}" ItemTemplate="{StaticResource AlertLineTemplate}"
                                AutomationProperties.Name="{Binding LinesName}" />
                    <TextBlock AutomationProperties.AutomationId="AlertNoAlertsLine" Text="{Binding NoAlerts}" Style="{StaticResource Muted}"
                               Margin="0,0,0,6" Visibility="{Binding HasNoAlerts, Converter={StaticResource BoolToVisible}}" />

                    <!-- Which kind? -->
                    <WrapPanel Margin="0,4,0,0" Visibility="{Binding ShowKinds, Converter={StaticResource BoolToVisible}}">
                        <TextBlock Text="Which kind of alert?" Style="{StaticResource SentenceText}" Margin="0,0,10,6" />
                        <Button Content="Stops climbing" Tag="{Binding RateTarget}" Click="OnKindClick" Margin="0,0,8,6"
                                Visibility="{Binding ShowRateKind, Converter={StaticResource BoolToVisible}}"
                                AutomationProperties.Name="{Binding RateKindName}" />
                        <Button Content="Crosses a number" Tag="{Binding LevelTarget}" Click="OnKindClick" Margin="0,0,8,6"
                                Visibility="{Binding ShowLevelKind, Converter={StaticResource BoolToVisible}}"
                                AutomationProperties.Name="{Binding LevelKindName}" />
                        <Button Content="Cancel" Click="OnCancelClick" Margin="0,0,0,6" AutomationProperties.Name="{Binding CancelName}" />
                    </WrapPanel>

                    <!-- Stops climbing, in words: the number and the minutes are the parts you fill in. -->
                    <WrapPanel Margin="0,4,0,0" Visibility="{Binding ShowRateEditor, Converter={StaticResource BoolToVisible}}">
                        <TextBlock Text="Alert me when an account's" Style="{StaticResource SentenceText}" />
                        <TextBlock Text="{Binding Title}" FontWeight="SemiBold" Style="{StaticResource SentenceText}" />
                        <TextBlock Text="gains fewer than" Style="{StaticResource SentenceText}" />
                        <TextBox Text="{Binding Draft.Number, UpdateSourceTrigger=PropertyChanged}" Width="110" Margin="0,0,6,6"
                                 KeyDown="OnEditorKeyDown" AutomationProperties.Name="{Binding NumberName}" />
                        <TextBlock Text="a minute for" Style="{StaticResource SentenceText}" />
                        <ComboBox ItemsSource="{Binding MinuteChoices}" SelectedItem="{Binding Draft.Minutes, Mode=OneWay}" Width="72"
                                  Margin="0,0,6,6" SelectionChanged="OnMinutesChanged" KeyDown="OnEditorKeyDown"
                                  AutomationProperties.Name="{Binding MinutesName}" />
                        <TextBlock Text="minutes." Style="{StaticResource SentenceText}" />
                    </WrapPanel>

                    <!-- Crosses a number, in words: above or below, and the number. -->
                    <WrapPanel Margin="0,4,0,0" Visibility="{Binding ShowLevelEditor, Converter={StaticResource BoolToVisible}}">
                        <TextBlock Text="Alert me when an account's" Style="{StaticResource SentenceText}" />
                        <TextBlock Text="{Binding Title}" FontWeight="SemiBold" Style="{StaticResource SentenceText}" />
                        <TextBlock Text="goes" Style="{StaticResource SentenceText}" />
                        <ComboBox ItemsSource="{Binding DirectionChoices}" SelectedItem="{Binding Draft.Direction, Mode=OneWay}" Width="90"
                                  Margin="0,0,6,6" SelectionChanged="OnDirectionChanged" KeyDown="OnEditorKeyDown"
                                  AutomationProperties.Name="{Binding DirectionName}" />
                        <TextBox Text="{Binding Draft.Number, UpdateSourceTrigger=PropertyChanged}" Width="110" Margin="0,0,2,6"
                                 KeyDown="OnEditorKeyDown" AutomationProperties.Name="{Binding NumberName}" />
                        <TextBlock Text="." Style="{StaticResource SentenceText}" />
                    </WrapPanel>

                    <TextBlock AutomationProperties.AutomationId="AlertEditorProblemLine" Text="{Binding Problem}" Style="{StaticResource Refusal}"
                               Margin="0,0,0,6" Visibility="{Binding HasProblem, Converter={StaticResource BoolToVisible}}" />
                    <StackPanel Orientation="Horizontal" Margin="0,2,0,0" Visibility="{Binding ShowEditor, Converter={StaticResource BoolToVisible}}">
                        <Button Content="{Binding ConfirmText}" Style="{StaticResource PrimaryButton}" Click="OnConfirmClick" Margin="0,0,8,0"
                                AutomationProperties.Name="{Binding ConfirmName}" />
                        <Button Content="Cancel" Click="OnCancelClick" AutomationProperties.Name="{Binding CancelName}" />
                    </StackPanel>

                    <Button Content="+ Add an alert" Tag="{Binding MetricId}" Click="OnAddClick" HorizontalAlignment="Left" Margin="0,6,0,0"
                            Visibility="{Binding ShowAdd, Converter={StaticResource BoolToVisible}}" AutomationProperties.Name="{Binding AddName}" />

                    <TextBlock AutomationProperties.AutomationId="AlertResultLine" Text="{Binding Result}" TextWrapping="Wrap" Margin="0,8,0,0"
                               Visibility="{Binding ShowResult, Converter={StaticResource BoolToVisible}}" />
                    <TextBlock AutomationProperties.AutomationId="AlertResultProblemLine" Text="{Binding Result}" Style="{StaticResource Refusal}"
                               Margin="0,8,0,0" Visibility="{Binding ShowResultProblem, Converter={StaticResource BoolToVisible}}" />
                </StackPanel>
            </Border>
        </DataTemplate>
    </UserControl.Resources>

    <StackPanel MaxWidth="820" HorizontalAlignment="Left">
        <TextBlock Text="Alerts" Style="{StaticResource Heading}" />
        <TextBlock Style="{StaticResource Muted}" Margin="0,4,0,14"
                   Text="RoRoRo does the judging. Every stat you send gets a card here: turn on the alerts you want, in your own words." />

        <TextBlock Text="YOUR ALERTS" Style="{StaticResource SectionLabel}" />
        <ui:RowList x:Name="AlertCardList" ItemTemplate="{StaticResource AlertCardTemplate}"
                    AutomationProperties.Name="Your sent stats and their alerts" />
        <TextBlock x:Name="AlertsEmptyLine" Style="{StaticResource Muted}" Margin="0,0,0,12" Visibility="Collapsed" />
        <TextBlock x:Name="AlertsResultLine" TextWrapping="Wrap" Margin="0,0,0,12" Visibility="Collapsed" />
        <TextBlock x:Name="AlertsNextLine" TextWrapping="Wrap" FontWeight="SemiBold" Margin="0,0,0,18" Visibility="Collapsed"
                   Text="{x:Static ui:AlertCards.NextInRoRoRo}" />

        <!-- The report policy, in the words ReportPolicy.Describe itself composes (F6), one card line per recipe. -->
        <Border Style="{StaticResource Card}" Padding="12,10">
            <StackPanel>
                <TextBlock Text="REPORT POLICY" Style="{StaticResource SectionLabel}" />
                <ui:RowList x:Name="PolicyList" AutomationProperties.Name="What each recipe sends to RoRoRo">
                    <ui:RowList.ItemTemplate>
                        <DataTemplate>
                            <StackPanel Margin="0,0,0,10">
                                <TextBlock Text="{Binding RecipeName}" FontWeight="SemiBold" />
                                <TextBlock Text="{Binding Line}" Style="{StaticResource Muted}" Margin="0,2,0,0" />
                                <TextBlock Text="{Binding Counts}" Style="{StaticResource Muted}" Margin="0,2,0,0" />
                            </StackPanel>
                        </DataTemplate>
                    </ui:RowList.ItemTemplate>
                </ui:RowList>
                <TextBlock x:Name="PolicyEmptyLine" Style="{StaticResource Muted}" Visibility="Collapsed"
                           Text="No recipe, so nothing is sent to RoRoRo." />
            </StackPanel>
        </Border>
    </StackPanel>
</UserControl>
```

Replace `src/UI/Setup/AlertsPage.xaml.cs` with:

```csharp
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Core;

namespace Labs626.UrScore.UI;

/// <summary>
/// Setup › Alerts: a card per sent stat whose alerts read as sentences and are edited in place, then the report policy card.
/// Every decision is <see cref="AlertCards"/>'s. This page reads the rules file, writes through <see cref="RulesFile"/>, draws
/// the rows and moves keyboard focus where <see cref="AlertCards"/> says (plan A13 to A15). It opens no window.
/// </summary>
public partial class AlertsPage : UserControl, ISetupPage
{
    private readonly ISetupServices _services;
    private AlertsView _view = AlertCards.Empty;
    private AlertsUi _ui = AlertsUi.Closed;
    private bool _drawn;

    public AlertsPage(ISetupServices services)
    {
        InitializeComponent();
        _services = services;
        Refresh();
    }

    public void Refresh()
    {
        var policies = AlertsModel.Policies(_services.Installed, _services.KnownAccounts, _services.Sources, _services.Settings.ResolveNames, _services.PolicyCounts);
        PolicyList.ItemsSource = policies;
        PolicyEmptyLine.Visibility = policies.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // A14: a read or a book line never redraws the cards under an open editor, and a redraw that changes nothing is skipped.
        if (_ui.Mode != AlertEditMode.None) return;

        var view = Read();
        if (_drawn && AlertCards.Same(_view, view))
        {
            DrawLines();
            return;
        }

        _view = view;
        Draw("");
    }

    private AlertsView Read() => AlertCards.Build(_services.Installed, RulesFile.Read(_services.RulesPath));

    private void Draw(string focus)
    {
        _drawn = true;
        AlertCardList.ItemsSource = AlertCards.Rows(_view, _ui);
        DrawLines();
        if (focus.Length > 0) _ = Dispatcher.InvokeAsync(() => FocusNamed(AlertCardList, focus), DispatcherPriority.Loaded);
    }

    private void DrawLines()
    {
        Show(AlertsEmptyLine, AlertCards.EmptyLine(_services.Installed, _view));
        Show(AlertsResultLine, AlertCards.OrphanResult(_view, _ui));
        AlertsNextLine.Visibility = _view.ShowNext ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string metricId) return;

        _view = Read();
        _ui = AlertCards.OpenAdd(metricId);
        Draw(AlertCards.FocusName(_view, metricId, _ui, AlertKind.Rate));
    }

    private void OnKindClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AlertTarget target) return;

        _ui = AlertCards.ChooseKind(target);
        Draw(AlertCards.FocusName(_view, target.MetricId, _ui, target.Kind));
    }

    private void OnChangeClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AlertTarget target || AlertCards.Managed(_view, target) is not { } line) return;

        _ui = AlertCards.OpenChange(line);
        Draw(AlertCards.FocusName(_view, target.MetricId, _ui, target.Kind));
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Cancel();

    private void Cancel()
    {
        var open = _ui;
        _ui = AlertsUi.Closed;
        _view = Read();
        Draw(AlertCards.FocusAfterCancel(_view, open));
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => Confirm();

    private void Confirm()
    {
        if (_ui.MetricId is not { } metricId || _ui.Draft is not { } draft || AlertCards.CardFor(_view, metricId) is not { } card) return;

        var (spec, problem) = AlertCards.Check(_ui.Kind, draft, card.Stat.Label);
        if (spec is null)
        {
            _ui = _ui with { Problem = problem };
            Draw(AlertCards.NumberName(card.Stat.Label));
            return;
        }

        var outcome = _ui.Mode == AlertEditMode.Changing
            ? RulesFile.Change(_services.RulesPath, metricId, spec)
            : RulesFile.TurnOn(_services.RulesPath, metricId, spec);
        _ui = AlertCards.AfterWrite(_ui, outcome, spec);
        _view = Read();
        Draw(AlertCards.FocusName(_view, metricId, _ui, spec.Kind));
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AlertTarget target
            || AlertCards.CardFor(_view, target.MetricId) is not { } card
            || AlertCards.Managed(_view, target) is not { } line)
        {
            return;
        }

        var outcome = RulesFile.Remove(_services.RulesPath, target.MetricId, target.Kind);
        _ui = AlertCards.AfterRemove(target, line, card.Stat.Label, outcome);
        _view = Read();
        Draw(AlertCards.FocusName(_view, target.MetricId, _ui, target.Kind));
    }

    /// <summary>Enter in the sentence does Turn on or Save; Escape cancels (A15). An open drop-down handles both keys itself first.</summary>
    private void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Confirm();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Cancel();
        }
    }

    // The boxes' selections are bound one way and copied into the draft here, so a box that is being rebuilt can never
    // push an empty choice back into what you picked.
    private void OnMinutesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { DataContext: AlertCardRow { Draft: { } draft }, SelectedItem: string minutes }) draft.Minutes = minutes;
    }

    private void OnDirectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { DataContext: AlertCardRow { Draft: { } draft }, SelectedItem: string direction }) draft.Direction = direction;
    }

    /// <summary>Moves keyboard focus to the visible control with this accessible name, selecting a number box's text (A15).</summary>
    private static bool FocusNamed(DependencyObject parent, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Control { IsVisible: true, Focusable: true } control && AutomationProperties.GetName(control) == name)
            {
                control.Focus();
                if (control is TextBox box) box.SelectAll();
                return true;
            }

            if (FocusNamed(child, name)) return true;
        }

        return false;
    }

    private static void Show(TextBlock line, string text)
    {
        line.Text = text;
        line.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
```

- [ ] **Step 5: The Alerts model keeps the policy card only**

Replace `src/UI/Setup/AlertsModel.cs` with:

```csharp
using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

using Source = Labs626.UrScore.Core.Source;

public sealed record PolicyItem(string RecipeName, string Line, string Counts);

/// <summary>
/// Setup › Alerts' report policy card (spec §7.5). The policy line is <see cref="ReportPolicy.Describe"/>'s own sentence.
/// The alert cards above it are <see cref="AlertCards"/>.
/// </summary>
public static class AlertsModel
{
    /// <summary>A recipe whose sources are all watched sends nothing, whatever its ticks say.</summary>
    public static string WatchOnly(Recipe recipe) => $"Nothing is sent to RoRoRo: you only watch its {RecipeWords.GroupsLower(recipe)}.";

    /// <summary>
    /// One report policy card line per sending recipe, in <see cref="ReportPolicy.Describe"/>'s words, with the
    /// allow list the running watches use (<see cref="ReportPolicies.Allowed"/>).
    /// </summary>
    public static IReadOnlyList<PolicyItem> Policies(
        IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<HostAccount> accounts, IReadOnlyList<Source> sources, bool resolveNames,
        Func<string, (int Sent, int Dropped)> counts) =>
        [.. installed.Where(i => !i.Recipe.IsGroupList).Select(i =>
        {
            var (sent, dropped) = counts(i.Recipe.Slug);
            var line = ReportPolicies.SendsByRole(i, sources)
                ? new ReportPolicy(i.State.SentStats(i.Recipe), ReportPolicies.Allowed(i, accounts, sources)).Describe(accounts.Count, resolveNames)
                : WatchOnly(i.Recipe);
            return new PolicyItem(i.Recipe.Name, line, $"Sent {sent}, dropped {dropped} this session.");
        })];
}
```

Replace `tests/AlertsModelTests.cs` with (the two policy tests, unchanged; the rule-sentence, choices and preview tests go with the code they tested, now covered by `RulesFileTests` and `AlertCardsTests`):

```csharp
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

public class AlertsModelTests
{
    private static readonly HostAccount Main = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), 101, "estehernandez");
    private static readonly HostAccount Alt = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), 201, "CElCPapa");

    private static Recipe Profile => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

    [Fact]
    public void ThePolicyLineIsReportPolicysOwnSentence()
    {
        var state = new RecipeState(
            ExcludedAccountIds: [Alt.AccountId.ToString()],
            Stats: new Dictionary<string, StatChoice> { ["diamonds"] = new(Send: true, MetricId: "ps99.diamonds") });

        var item = Assert.Single(AlertsModel.Policies([new InstalledRecipe(Profile, "", state)], [Main, Alt], [], resolveNames: false, _ => (3, 1)));

        var expected = new ReportPolicy([new SentStat("diamonds", "Diamonds", "ps99.diamonds")], new HashSet<Guid> { Main.AccountId }).Describe(2, false);
        Assert.Equal(new PolicyItem("Pet Sim 99 profile", expected, "Sent 3, dropped 1 this session."), item);
    }

    [Fact]
    public void ARecipeYouOnlyWatchSaysItSendsNothing()
    {
        var clan = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;
        var state = new RecipeState(Stats: new Dictionary<string, StatChoice> { ["value"] = new(Send: true, MetricId: "clan.battle.points") });
        var watched = new Source("s-00000001", clan.Slug, new Dictionary<string, string> { ["clan"] = "NovaForge" }, SourceRole.Watch);

        var item = Assert.Single(AlertsModel.Policies([new InstalledRecipe(clan, "", state)], [Main, Alt], [watched], resolveNames: false, _ => (0, 0)));

        Assert.Equal("Nothing is sent to RoRoRo: you only watch its clans.", item.Line);
    }
}
```

- [ ] **Step 6: The Stats table's rule line (A17)**

`src/UI/Setup/StatsPage.xaml.cs`: add `using Labs626.UrScore.Core;` after `using Labs626.UrScore.Composition;`. In `Load`, after `var recipe = installed.Recipe;` add `var rules = RulesFile.Read(_services.RulesPath);`, and replace the argument line `metricId => AlertsModel.RuleSentence(metricId).Text,` with:

```csharp
            metricId => AlertCards.StatLine(rules, metricId),
```

`src/UI/Setup/ImportFlow.cs`: add `using Labs626.UrScore.Core;` after `using Labs626.UrScore.Composition;`. Directly above `var window = new ImportWindow(` add `var rules = RulesFile.Read(services.RulesPath);`, and replace the argument line `metricId => AlertsModel.RuleSentence(metricId).Text,` with:

```csharp
                metricId => AlertCards.StatLine(rules, metricId),
```

- [ ] **Step 7: Retire the old helper**

In `src/Core/RulesFile.cs`, delete:
- the `RuleState` enum and its summary (the file's first type, "What RoRoRo's rules file currently says about our metric id"),
- the `RuleStatus` record and its summary,
- inside `RulesFile`: the `WriteOptions` field, and the methods `Inspect`, `Preview`, `AddRule`, `BuildRule`, `MetricIdOf`, `StringOf` and `DoubleOf`, each with its doc comment.

Keep `Owner`, `DefaultPath`, `ReadOptions` and everything Task 1 added. Then delete the file `src/Core/RuleInventory.cs` (`git rm src/Core/RuleInventory.cs`). Per A9, nothing writes `written-rules.json` any more, and no code deletes it.

Check nothing still names the old API:

Run: `git grep -n -E "RuleInventory|RuleState|RuleStatus|RulesFile\.(Inspect|Preview|AddRule)|RuleSentence|RuleChoice|MetricAlertsOff|DefaultWindowMinutes" -- src tests tools`
Expected: no output.

- [ ] **Step 8: The build gate**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror` then `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: both pass, `AlertsPageFenceTests`, `RowListFenceTests`, `ThemeFenceTests` and `NoHostnameFenceTests` included.

- [ ] **Step 9: A look at the page (controller)**

Quit Ur Score, then `dotnet build Ur-Score.csproj -c Release -warnaserror`. Set `$env:UR_SCORE_RULES_FILE` to a file in the session scratchpad, holding the three seed rules from Task 4 Step 1, and start `bin\Release\net10.0-windows\626labs.ur-score.exe` from that shell. With a recipe sending two stats, open Setup › Alerts and screenshot it in the dark and light themes (`tools/smoke/shot.ps1 -Title 'Setup'`). Check the following, and send anything off back to this task before Task 4:
- the cards, marks and buttons sit in the theme;
- the editor sentence wraps on one line at the default Setup width;
- the problem and result lines use magenta and the body text colour.

Remove the variable afterwards.

- [ ] **Step 10: Commit**

```bash
git add src/Composition/ISetupServices.cs src/Composition/AppServices.cs src/UI/Setup/AlertsPage.xaml src/UI/Setup/AlertsPage.xaml.cs src/UI/Setup/AlertsModel.cs src/UI/Setup/StatsPage.xaml.cs src/UI/Setup/ImportFlow.cs src/Core/RulesFile.cs tests/AlertsModelTests.cs tests/AlertsPageFenceTests.cs
git rm src/Core/RuleInventory.cs
git commit -m "setup: an Alerts page of cards and sentences, edited in place, with no message box and no JSON

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: The Setup › Alerts smoke walk, and the live walk

**Files:**
- Create: `tools/smoke/walk-alerts.ps1`
- Modify: `tools/smoke/README.md`

**Interfaces:**
- Consumes: Task 3's automation ids and names; `RulesFile.PathVariable` (`UR_SCORE_RULES_FILE`); the smoke helpers `Move-UrDataAside`, `Restore-UrData`, `Start-UrScore`, `Open-SetupPage`, `Complete-ClanImport`, `Find-All`, `Get-Check`, `Set-Tick`, `Set-ElementValue`, `Invoke-Element`, `Line`, `Wait-Line`, `Wait-Until`, `Get-AllTexts`, `Test-FocusWithin`, `Select-ComboItem`, `Check`, `Show-Results` (all unchanged at `bab5335`).
- Produces: the walk the release runs.

The walk never opens RoRoRo's real `metric-rules.json` for writing (A1). It points Ur Score at a scratch file through `UR_SCORE_RULES_FILE`, and its last step fails if the real file's bytes or its absence changed. The script is ASCII only: no `›` and no `—` (checks match with `-like` around them).

- [ ] **Step 1: The walk**

Create `tools/smoke/walk-alerts.ps1`:

```powershell
# Setup > Alerts on a clean data folder, against a scratch rules file (plan A1): RoRoRo's own metric-rules.json is never
# written, and the last step fails if its bytes change. Seeds the rule 0.3.1 added, one you wrote and another plugin's,
# imports the profile recipe sending Diamonds and Player rank, then: the cards read as sentences with no JSON and the
# next step in RoRoRo; Change a stops-climbing alert; add a crosses-a-number alert with a refused number first and Enter;
# Escape cancels; a locked file is said on the card and changes nothing; Remove; a stat you stop sending keeps its card
# with Remove only. RoRoRo running is optional.
. (Join-Path $PSScriptRoot 'uia-board.ps1')
$ErrorActionPreference = 'Stop'
$profileFixture = Join-Path $UrFixtures 'petsim99-profile.recipe.json'
$realRules = Join-Path $env:LOCALAPPDATA 'ROROROblox\metric-rules.json'
$scratchDir = Join-Path $env:TEMP "ur-score-smoke-rules-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
$scratch = Join-Path $scratchDir 'metric-rules.json'
$backup = $null
$lock = $null

function Get-RealRulesHash {
    if (Test-Path $realRules) { (Get-FileHash $realRules -Algorithm SHA256).Hash } else { 'absent' }
}

# The scratch file's rules, one object per rule (see Read-Sources for why this unrolls with foreach).
function Read-ScratchRules {
    $parsed = Get-Content $scratch -Raw | ConvertFrom-Json
    foreach ($r in $parsed) { $r }
}

# A control by type and accessible name that is on screen: a collapsed editor's twin is not.
function Get-Shown($root, $type, [string]$name) {
    Find-All $root $type | Where-Object { $_.Current.Name -eq $name -and -not $_.Current.IsOffscreen } | Select-Object -First 1
}

function Get-ComboValue($box) {
    $selection = $box.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
    if ($selection.Count -gt 0) { $selection[0].Current.Name } else { '' }
}

function Get-FocusedName { $f = $AE::FocusedElement; if ($f) { $f.Current.Name } else { '(none)' } }

$realBefore = Get-RealRulesHash
try {
    New-Item -ItemType Directory -Force $scratchDir | Out-Null
    Set-Content -Path $scratch -Encoding ASCII -Value @'
[
  { "metricId": "ps99.diamonds", "kind": "Rate", "threshold": 100, "windowMinutes": 10, "alertWhenBelow": true, "owner": "626labs.ur-score" },
  { "metricId": "ps99.rank", "kind": "Rate", "threshold": 3, "windowMinutes": 30 },
  { "metricId": "memory.warning", "kind": "Event", "owner": "someone.else" }
]
'@
    # Set before Ur Score starts, so the process inherits it.
    $env:UR_SCORE_RULES_FILE = $scratch
    $backup = Move-UrDataAside
    Start-UrScore | Out-Null

    Complete-ClanImport $profileFixture @() @('Diamonds', 'Player rank') | Out-Null
    Open-SetupPage 'Alerts' | Out-Null
    $rate100 = "Alert me when an account's Diamonds gains fewer than 100 a minute for 10 minutes."
    Wait-Until { @(Get-AllTexts (Get-SetupWindow)) -contains $rate100 } 15 | Out-Null

    # 1. The cards read as sentences: the rule 0.3.1 added is Ur Score's, yours is marked, no JSON, and the next step shows.
    $texts = @(Get-AllTexts (Get-SetupWindow))
    Check '1 The rule 0.3.1 added reads as a sentence in your words' ($texts -contains $rate100) ($texts -join ' | ')
    Check '1b ...with Change and Remove' ([bool](Get-Shown (Get-SetupWindow) $CT::Button 'Change the stops climbing alert for Diamonds') -and [bool](Get-Shown (Get-SetupWindow) $CT::Button 'Remove the stops climbing alert for Diamonds')) 'buttons'
    $yours = "Alert me when an account's Player rank gains fewer than 3 a minute for 30 minutes."
    Check '1c A rule you wrote is marked yours, with no Change' (($texts -contains $yours) -and ($texts -contains 'yours') -and -not (Get-Shown (Get-SetupWindow) $CT::Button 'Change the stops climbing alert for Player rank')) ($texts -join ' | ')
    $json = @($texts | Where-Object { $_ -match 'metricId|windowMinutes|alertWhenBelow|626labs|[{}]' })
    Check '1d No JSON on screen' ($json.Count -eq 0) ($json -join ' | ')
    $next = Line (Get-SetupWindow) 'AlertsNextLine'
    Check '1e The next step in RoRoRo is said' ($next -like 'Next, in RoRoRo: Settings*turn on Metric alerts and choose where they go (desktop, Discord, phone).') $next
    Check '1f Another plugin''s rule for a stat you do not send has no card' (-not ($texts -like '*memory.warning*')) 'absent'

    # 2. Change: focus in the number box, the minutes open on the rule's own, Save rewrites it in place with its label.
    Invoke-Element (Get-Shown (Get-SetupWindow) $CT::Button 'Change the stops climbing alert for Diamonds')
    Start-Sleep -Milliseconds 800
    $number = Get-Shown (Get-SetupWindow) $CT::Edit 'Number for Diamonds'
    Check '2 Change puts focus in the number box' (Test-FocusWithin $number) "focused='$(Get-FocusedName)'"
    $minutes = Get-Shown (Get-SetupWindow) $CT::ComboBox 'Minutes for Diamonds'
    Check '2b The minutes open on the rule''s own' ((Get-ComboValue $minutes) -eq '10') (Get-ComboValue $minutes)
    Set-ElementValue $number '250'
    Select-ComboItem $minutes '15'
    Invoke-Element (Get-Shown (Get-SetupWindow) $CT::Button 'Save the alert for Diamonds')
    $said = Wait-Line (Get-SetupWindow) 'AlertResultLine' '^Changed\.' 5
    Check '2c The card says what changed' ($said -eq "Changed. RoRoRo will now alert you when an account's Diamonds gains fewer than 250 a minute for 15 minutes.") $said
    $rules = @(Read-ScratchRules)
    Check '2d Rewritten in place with its label; the others are kept' ($rules.Count -eq 3 -and $rules[0].threshold -eq 250 -and $rules[0].windowMinutes -eq 15 -and $rules[0].label -eq 'Diamonds' -and $rules[0].owner -eq '626labs.ur-score' -and $rules[1].threshold -eq 3 -and -not $rules[1].owner -and $rules[2].owner -eq 'someone.else') ($rules | ConvertTo-Json -Compress)
    Check '2e The file was backed up first' ((Get-Content "$scratch.ur-score-backup" -Raw) -match '"threshold": 100,') 'backup'
    Check '2f Focus returns to Change' (Test-FocusWithin (Get-Shown (Get-SetupWindow) $CT::Button 'Change the stops climbing alert for Diamonds')) "focused='$(Get-FocusedName)'"

    # 3. + Add an alert asks which kind; a comma decimal is refused with nothing written; Enter turns it on.
    Invoke-Element (Get-Shown (Get-SetupWindow) $CT::Button 'Add an alert for Player rank')
    Start-Sleep -Milliseconds 800
    $stops = Get-Shown (Get-SetupWindow) $CT::Button 'Stops climbing, for Player rank'
    $crosses = Get-Shown (Get-SetupWindow) $CT::Button 'Crosses a number, for Player rank'
    Check '3 + Add an alert asks which kind, focus on the first' ([bool]$stops -and [bool]$crosses -and (Test-FocusWithin $stops)) "focused='$(Get-FocusedName)'"
    Invoke-Element $crosses
    Start-Sleep -Milliseconds 800
    $before = Get-Content $scratch -Raw
    Select-ComboItem (Get-Shown (Get-SetupWindow) $CT::ComboBox 'Above or below for Player rank') 'above'
    Set-ElementValue (Get-Shown (Get-SetupWindow) $CT::Edit 'Number for Player rank') '1,5'
    Invoke-Element (Get-Shown (Get-SetupWindow) $CT::Button 'Turn on the alert for Player rank')
    $problem = Wait-Line (Get-SetupWindow) 'AlertEditorProblemLine' '^Use a dot' 5
    Check '3b A comma decimal is refused and nothing is written' (($problem -eq 'Use a dot for decimals, like 1.5.') -and ((Get-Content $scratch -Raw) -eq $before)) $problem
    $number = Get-Shown (Get-SetupWindow) $CT::Edit 'Number for Player rank'
    Set-ElementValue $number '40'
    $number.SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    $said = Wait-Line (Get-SetupWindow) 'AlertResultLine' '^On\.' 5
    Check '3c Enter turns it on, and the card says so' ($said -eq "On. RoRoRo will alert you when an account's Player rank goes above 40.") $said
    $level = @(Read-ScratchRules) | Where-Object { $_.metricId -eq 'ps99.rank' -and $_.kind -eq 'Level' } | Select-Object -First 1
    Check '3d The rule has its direction, label and owner, and no window' ($level -and $level.threshold -eq 40 -and $level.alertWhenBelow -eq $false -and $level.label -eq 'Player rank' -and $level.owner -eq '626labs.ur-score' -and -not ($level.PSObject.Properties.Name -contains 'windowMinutes')) ($level | ConvertTo-Json -Compress)

    # 4. Escape closes an editor without writing, and focus goes back to + Add an alert.
    $before = Get-Content $scratch -Raw
    Invoke-Element (Get-Shown (Get-SetupWindow) $CT::Button 'Add an alert for Diamonds')
    Start-Sleep -Milliseconds 800
    Invoke-Element (Get-Shown (Get-SetupWindow) $CT::Button 'Crosses a number, for Diamonds')
    Start-Sleep -Milliseconds 800
    Check '4 Choosing a kind puts focus in the number box' (Test-FocusWithin (Get-Shown (Get-SetupWindow) $CT::Edit 'Number for Diamonds')) "focused='$(Get-FocusedName)'"
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Milliseconds 800
    $add = Get-Shown (Get-SetupWindow) $CT::Button 'Add an alert for Diamonds'
    Check '4b Escape closes it, writes nothing, and focus is on + Add an alert' ((-not (Get-Shown (Get-SetupWindow) $CT::Edit 'Number for Diamonds')) -and ((Get-Content $scratch -Raw) -eq $before) -and (Test-FocusWithin $add)) "focused='$(Get-FocusedName)'"

    # 5. A file another program holds open: Turn on says so on the card, keeps the editor, changes nothing; it works once let go.
    Invoke-Element (Get-Shown (Get-SetupWindow) $CT::Button 'Add an alert for Diamonds')
    Start-Sleep -Milliseconds 800
    Invoke-Element (Get-Shown (Get-SetupWindow) $CT::Button 'Crosses a number, for Diamonds')
    Start-Sleep -Milliseconds 800
    Set-ElementValue (Get-Shown (Get-SetupWindow) $CT::Edit 'Number for Diamonds') '5000000'
    $before = Get-Content $scratch -Raw
    $lock = [System.IO.File]::Open($scratch, 'Open', 'ReadWrite', 'None')
    Invoke-Element (Get-Shown (Get-SetupWindow) $CT::Button 'Turn on the alert for Diamonds')
    $problem = Wait-Line (Get-SetupWindow) 'AlertEditorProblemLine' 'locked' 5
    Check '5 A locked file is said on the card and the editor stays open' (($problem -like "RoRoRo's rules file is locked*") -and [bool](Get-Shown (Get-SetupWindow) $CT::Edit 'Number for Diamonds')) $problem
    $lock.Dispose()
    $lock = $null
    Check '5b ...and nothing changed' ((Get-Content $scratch -Raw) -eq $before) 'compared'
    Invoke-Element (Get-Shown (Get-SetupWindow) $CT::Button 'Turn on the alert for Diamonds')
    $said = Wait-Line (Get-SetupWindow) 'AlertResultLine' '^On\.' 5
    Check '5c Once let go, Turn on works' ($said -eq "On. RoRoRo will alert you when an account's Diamonds goes below 5,000,000.") $said

    # 6. Remove deletes exactly Ur Score's alert of that kind; your rule stays; focus goes to + Add an alert.
    Invoke-Element (Get-Shown (Get-SetupWindow) $CT::Button 'Remove the stops climbing alert for Diamonds')
    $said = Wait-Line (Get-SetupWindow) 'AlertResultLine' '^Removed\.' 5
    Check '6 The card says what was removed' ($said -eq "Removed. RoRoRo won't alert you when an account's Diamonds gains fewer than 250 a minute for 15 minutes any more.") $said
    $rules = @(Read-ScratchRules)
    $diamondRates = @($rules | Where-Object { $_.metricId -eq 'ps99.diamonds' -and $_.kind -eq 'Rate' })
    Check '6b Only that rule is gone' ($diamondRates.Count -eq 0 -and $rules.Count -eq 4) ($rules | ConvertTo-Json -Compress)
    Check '6c Focus goes to + Add an alert' (Test-FocusWithin (Get-Shown (Get-SetupWindow) $CT::Button 'Add an alert for Diamonds')) "focused='$(Get-FocusedName)'"
    & (Join-Path $PSScriptRoot 'shot.ps1') -Title 'Setup' -OutPath (Join-Path $UrShots 'setup-alerts.png') | Out-Null

    # 7. A stat you stop sending keeps its card while Ur Score has an alert for it, with Remove only.
    $setup = Open-SetupPage 'Stats'
    Set-Tick (Get-Check $setup 'Send Player rank') $false
    Invoke-Element (Find-ByAutomationId $setup 'SaveStatsButton')
    Wait-Line $setup 'StatsSavedLine' '^Saved\.' 5 | Out-Null
    Open-SetupPage 'Alerts' | Out-Null
    Start-Sleep -Milliseconds 800
    $texts = @(Get-AllTexts (Get-SetupWindow))
    Check '7 The card says you no longer send it' (@($texts -like "You don't send Player rank any more*").Count -eq 1) ($texts -join ' | ')
    Check '7b Remove only: no Change and no + Add an alert' ([bool](Get-Shown (Get-SetupWindow) $CT::Button 'Remove the crosses a number alert for Player rank') -and -not (Get-Shown (Get-SetupWindow) $CT::Button 'Change the crosses a number alert for Player rank') -and -not (Get-Shown (Get-SetupWindow) $CT::Button 'Add an alert for Player rank')) 'buttons'
    Check '7c The next step still shows for Diamonds' ((Line (Get-SetupWindow) 'AlertsNextLine') -like 'Next, in RoRoRo*') (Line (Get-SetupWindow) 'AlertsNextLine')
    Invoke-Element (Get-Shown (Get-SetupWindow) $CT::Button 'Remove the crosses a number alert for Player rank')
    $said = Wait-Line (Get-SetupWindow) 'AlertsResultLine' '^Removed\.' 5
    $gone = -not (@(Get-AllTexts (Get-SetupWindow)) -like "You don't send Player rank*")
    Check '7d Removing its last alert takes the card away and says so under the cards' (($said -like "Removed.*Player rank goes above 40 any more.") -and $gone) $said
}
finally {
    if ($null -ne $lock) { $lock.Dispose() }
    if ($null -ne $backup) { Restore-UrData $backup }
    Remove-Item Env:UR_SCORE_RULES_FILE -ErrorAction SilentlyContinue
    if (Test-Path $scratchDir) { Remove-Item $scratchDir -Recurse -Force }
    $realAfter = Get-RealRulesHash
    Check '8 RoRoRo''s own rules file is exactly as it was' ($realAfter -eq $realBefore) "same=$($realAfter -eq $realBefore)"
    Show-Results
}
exit $LASTEXITCODE
```

- [ ] **Step 2: The README**

In `tools/smoke/README.md`:
- Add this row to the scripts table, after `walk-stats-table.ps1`: `| walk-alerts.ps1 | Setup › Alerts against a scratch rules file: sentences with no JSON, Change, a crosses-a-number alert with a refused number and Enter, Escape, a locked file, Remove, a stat you stop sending, and RoRoRo's own rules file unchanged |`.
- Under "Your data is safe", add this paragraph: "`walk-alerts.ps1` never writes RoRoRo's `metric-rules.json`. It points Ur Score at a scratch file under `%TEMP%` with `UR_SCORE_RULES_FILE`, deletes that file afterwards, and fails its last step if RoRoRo's own file changed. If a run is killed mid-walk, close Ur Score before starting it again from a shell where that variable isn't set."

- [ ] **Step 3: Commit**

```bash
git add tools/smoke/walk-alerts.ps1 tools/smoke/README.md
git commit -m "smoke: walk Setup > Alerts against a scratch rules file, and prove RoRoRo's own file is untouched

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Step 4: The live walk (controller)**

Quit Ur Score. From the repo root:

```bash
dotnet build Ur-Score.csproj -c Release -warnaserror
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build
```

Start RoRoRo 1.28 (`Get-Process ROROROblox.App | Select-Object Path` shows which build is running). Run each walk one at a time, from a shell where `UR_SCORE_RULES_FILE` is not set, and record every result:

```powershell
powershell -ExecutionPolicy Bypass -File tools/smoke/walk-alerts.ps1
powershell -ExecutionPolicy Bypass -File tools/smoke/walk-stats-table.ps1
powershell -ExecutionPolicy Bypass -File tools/smoke/window-smoke.ps1 -Main CCGP
powershell -ExecutionPolicy Bypass -File tools/smoke/walk-alts.ps1
```

Expected: every step passes, no `626labs.ur-score.smoke-backup-*` folder and no `ur-score-smoke-rules-*` folder is left, and `walk-alerts` step 8 passes.

A failure goes back to its task before the release. The known risk points each map to a task:

| Step fails | What to check | Task |
|---|---|---|
| 2b | the minutes box opens empty | 3: move the `ItemsSource` attribute before `SelectedItem`, or set the selection in `OnMinutesChanged`'s row on `Loaded` |
| 3c | Enter did nothing | 3: `OnEditorKeyDown` on both number boxes |
| 7 | the Stats save didn't keep the pinned metric id | 2 |

By hand, on the owner's own data (A20, look only): open Setup › Alerts on the owner's real `metric-rules.json`. The clan battle card lists "Alert me when an account's Points gains fewer than 100 a minute for 10 minutes." with Change and Remove, and the next-step line is shown. Screenshot it with `shot.ps1 -Title 'Setup'` in both themes, press nothing, and quit.

---

## After the tasks: release v0.3.2 (the controller; each gated step waits for the owner's OK)

1. **Version and notes.**
   - Set `0.3.2` in `manifest.json` (`"version"`) and `Ur-Score.csproj` (`<Version>`).
   - In `CHANGELOG.md`, insert above `## Unreleased` a section `## 0.3.2 — ` plus the date that `powershell -Command "Get-Date -Format yyyy-MM-dd"` prints. Give it three lists:
     - **Added:** every stat you send gets a card on Setup › Alerts. Its alerts read as sentences you fill in: stops climbing (fewer than a number a minute for 10, 15 or 30 minutes) and crosses a number (above or below). + Add an alert, Change and Remove, with what happened said on the card. Rules you or another plugin wrote are listed and never changed. A standing line says what to turn on in RoRoRo. Rules Ur Score writes carry a `label`; RoRoRo 1.28 ignores it, and a later RoRoRo uses it to word alerts.
     - **Changed:** no stock message box and no JSON on the Alerts page. A failed write is said on the card and changes nothing. The rules file is backed up and written through a temporary file on every change. The Stats table's rule line counts a stat's alerts.
     - **Removed:** `written-rules.json` is no longer written.
   - In `docs/backlog.md`:
     - V3-S.10: remove `AlertsPage.xaml.cs:74 and :90` from its list, change "nine stock `MessageBox.Show` calls" to "seven", and keep it OPEN.
     - S1-12.5: point its Alerts entry at `src/UI/Setup/AlertsPage.xaml.cs` `Refresh` (the rules file is still read on the UI thread, now skipped while an editor is open).
     - Leave S1-12.11 OPEN (its other gaps remain); the new Alerts tests clean up their folders.
   - In `docs/plans/2026-09-15-backlog-remediation.md`, insert below its "Do not start yet" banner: `> **Versions, 2026-09-15:** 0.3.1 shipped the default views and 0.3.2 the Alerts page (docs/plans/2026-09-15-alerts-card.md). Wave 1 of this plan ships as **0.3.3**. Task 0's re-check covers both branches: RulesFile, AlertsModel, AlertsPage, StatsPage and ImportFlow changed, RuleInventory was deleted, and V3-S.10's two Alerts message boxes are gone.`
   - Append `## Execution record` with the date to this plan: commits per task, rulings made during execution, the walk results (Task 4 Step 4), and every review's Minors with their status.
   - Commit `release: 0.3.2`.
2. **Pull request (ask the owner first).** Run `git push -u origin feat/alerts-card`, then `gh pr create --base master --head feat/alerts-card --title "Ur Score 0.3.2: an Alerts page that tells you what to do"`. The body summarises the cards and sentences, the rules-file promises (other rules kept, backup, refusals, owner and label), the walk results, and the RoRoRo companion (the label is read by the metric alert wording change; nothing here depends on it). End the body with the Claude Code attribution line. Wait for the `test` workflow to pass.
3. **Merge (ask the owner first).** `gh pr merge --merge`, then `git checkout master` and `git pull --ff-only`.
4. **Tag and release (ask the owner first).** Run `git tag -a v0.3.2 -m "Ur Score 0.3.2: an Alerts page that tells you what to do"` and `git push origin v0.3.2`. The `release` workflow checks that the tag, `manifest.json` and the csproj agree, runs the tests, and attaches `manifest.json`, `manifest.sha256` and `plugin.zip`. Confirm all three: `gh release view v0.3.2 --json assets --jq ".assets[].name"`.
5. **Install from the release and walk the installed copy.**
   - Back up `%LOCALAPPDATA%\ROROROblox\plugins\626labs.ur-score` to the session scratchpad and remove it. Leave `%LOCALAPPDATA%\626labs.ur-score` in place.
   - In RoRoRo's Plugins page, use Install from URL with `https://github.com/estevanhernandez-stack-ed/Ur-Score/releases/latest/download/` and accept the consent; RoRoRo names version 0.3.2.
   - Quit the installed Ur Score, set `$env:UR_SCORE_EXE` to `%LOCALAPPDATA%\ROROROblox\plugins\626labs.ur-score\626labs.ur-score.exe`, and run `walk-alerts.ps1` and `walk-stats-table.ps1`.
   - Start the installed Ur Score from RoRoRo on your own data and look at Setup › Alerts without pressing anything (A20).
   - Tell the owner two things: pressing Change, then Save, on the Points alert adds its label; and Metric alerts must be on in RoRoRo's Settings › Alerts, with a destination, before Saturday.
6. **Clan post (the owner posts).** Draft a short "what's new in 0.3.2" note in the product's voice (builder to builder, second person, no jargon, no emoji). It says: Setup › Alerts now says in plain words what will alert you, lets you pick the number and the minutes, and tells you the one switch to flip in RoRoRo; update Ur Score, then turn on the alerts you want. Hand it to the owner.

---

## Self-review record

- **Design coverage:**
  - Every sent stat gets a card, and its alerts are sentences → Task 2 (`Build`, `Sentence`), Task 3 (the card template). Stops climbing with an editable number and 10, 15 or 30 minutes → Task 2 (`NewDraft`, `MinuteChoices`, `Check`), Task 3 (the Rate editor). Crosses a number, above or below → Task 2, Task 3 (the Level editor).
  - + Add an alert asks which kind, then shows the sentence and Turn on → Task 2 (`OpenAdd`, `ChooseKind`, `Rows`), Task 3. Change and Remove on Ur Score's own alert; one of each kind → Task 1 (`OursFor`, `TurnOn` AlreadyThere), Task 2 (`Managed`, `CanAdd`, A5). Yours and another plugin's rules listed and marked, never edited → Task 1 (`RuleOwner`, `NotThere`), Task 2 (marks, no buttons).
  - The card says what happened, in the theme → Task 2 (`AfterWrite`, `AfterRemove`), Task 3 (result lines, `Refusal` style). The standing "Next, in RoRoRo" line → Task 2 (`NextInRoRoRo`, `ShowNext`), Task 3. No JSON, no stock message box, and a failed write said on the card, changing nothing → Task 3 (`AlertsPageFenceTests`), Task 1 (refusals), Task 4 (steps 1d, 5).
  - Rules file: keeps everything else, backs up, refuses invalid → Task 1. Rate and Level only; Event not offered → Task 1 (`Guard`), Task 2 (`Offered`). Owner and label → Task 1 (`Build`). Remove deletes exactly one; Change rewrites in place → Task 1. The 0.3.1 rule recognised, and the next Change adds its label → Task 1 test, A4.
  - Rules that still bind: themed popups, RowList, DynamicResource, no hostnames, copy and no game names, other players, the one path to RoRoRo → Global Constraints and fence tests. Merging and releasing wait for the owner → the release section.
- **Planning calls the design doesn't settle:** A1 (path injection and the walk), A4 (the 0.3.1 rule), A5 (duplicates), A11 (a stat you stop sending), A12 (validation and minutes), A13 to A15 (editor lifecycle, refresh and focus), plus the rest of A1 to A20.
- **Placeholders:** none. Every code step carries its code, and every command is exact. The one open-ended step is Task 3 Step 9's look at the page, which has fixed checks.
- **Types across tasks:** each Task 1 name is used by Tasks 2 and 3 with the signature in the interface contract: `AlertRule`, `AlertSpec`, `RulesRead.For/SkippedFor/OursFor/Unusable/NoFile`, `RuleWrite`, `RulesProblem`, `RulesFile.Read/TurnOn/Change/Remove/ResolvePath/PathVariable/BackupSuffix`. The `AlertCardRow` and `AlertLineRow` property names bound in Task 3's XAML are exactly Task 2's: `Title`, `Note`, `HasNote`, `Lines`, `LinesName`, `NoAlerts`, `HasNoAlerts`, `ShowKinds`, `ShowRateKind`, `ShowLevelKind`, `RateTarget`, `LevelTarget`, `RateKindName`, `LevelKindName`, `CancelName`, `ShowRateEditor`, `ShowLevelEditor`, `Draft.Number`, `Draft.Minutes`, `Draft.Direction`, `MinuteChoices`, `DirectionChoices`, `NumberName`, `MinutesName`, `DirectionName`, `Problem`, `HasProblem`, `ShowEditor`, `ConfirmText`, `ConfirmName`, `ShowAdd`, `MetricId`, `AddName`, `Result`, `ShowResult`, `ShowResultProblem`, `Sentence`, `Mark`, `HasMark`, `Target`, `ShowChange`, `ShowRemove`, `ChangeName`, `RemoveName`. The accessible names Task 4 walks come from `AddName`, `KindName`, `ChangeName`, `RemoveName`, `NumberName`, `MinutesName`, `DirectionName` and `ConfirmName`, with the same words.
- **Against the tree at `bab5335`:** these are used as they are: `RulesFile.Owner/DefaultPath/ReadOptions`, `RecipeState.SentStats/StatChoices`, `StatChoice.MetricId`, `RecipeStats.Find`, `InstalledRecipe`, `Recipe.IsGroupList`, `RecipeParserTests.Fixture`, `TempDir.Create`, `ISetupServices.HostText/Installed/KnownAccounts/Sources/Settings/PolicyCounts`, `AppServices.AddTrail`, `StatsTable.Load(..., Func<string, string> ruleSentence, ...)`, the `ImportWindow` constructor, the App.xaml styles `Heading`, `Muted`, `Refusal`, `SectionLabel`, `Card`, `PrimaryButton`, `MonoFont` and `BoolToVisible`, and the smoke helpers listed in Task 4. No walk used the retired ids (`RuleStatBox`, `RuleLine`, `AddRuleButton`, `RulePreview`).
