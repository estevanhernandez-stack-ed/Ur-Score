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

> **Added 2026-09-15, after the plan was written.** The owner approved one more piece for 0.3.2 the same day: a Roblox
> avatar headshot beside each of your own accounts. It is a late ask, so it sits here, below the release run, rather than
> renumbering the tasks above — but it is built **before** the release, not after. **Execution order: Task 4 → Task 6 →
> Task 5 (the release run), release last.** The file table and the interface contract near the top of this plan cover
> Tasks 1 to 4; Task 6 lists its own files and names below. It changes nothing in `tools/smoke`.

### Task 6: Avatars beside your own accounts

**Files:**
- Modify: `src/Source/IconClient.cs`, `src/Board/PanelModels.cs`, `src/Board/AccountsTableModel.cs`, `src/Composition/AppServices.cs`, `src/Composition/ISetupServices.cs`, `src/UI/Setup/AccountsModel.cs`, `src/UI/Setup/AccountsPage.xaml`, `src/UI/Setup/AccountsPage.xaml.cs`, `src/App.xaml`, `src/UI/Panels/AccountsTablePanel.xaml`, `src/UI/Panels/MyAccountsPanel.xaml`, `src/UI/Panels/PromotionCheckPanel.xaml`, `src/UI/Panels/AccountCardPanel.xaml`, `README.md`
- Create: `src/Source/AvatarBook.cs`, `src/UI/Controls/AvatarFill.cs`
- Test: `tests/AvatarBookTests.cs` (new), `tests/AvatarFenceTests.cs` (new), `tests/IconClientTests.cs` (added to), `tests/PanelModelsTests.cs` (added to), `tests/AccountsModelTests.cs` (added to)
- Never touched: anything under `tools/smoke` (another agent owns those this cycle; A29 says why nothing there needs to change)

**Interfaces:**
- Consumes: `IconClient.ThumbnailsHost/IsPictureHost/CacheFor/MaxBytes/DefaultCacheDirectory`, its private `CachePath`, `IsFresh`, `DownloadAsync` and `Get`; `JsonNav.TryGet/TryUserId`; `UrScoreIdentity.UserAgent`; `HttpRecipeTransport.CreateHandler()`; `LiveBoard.UserIdsOf`, `HostAccount`; `BoardFixtures` (tests).
- Produces:

```csharp
// ---- src/Source/IconClient.cs ----
public sealed class IconClient : IAvatarSource                                     // the class gains the interface
{
    public const string HeadshotSize = "48x48";
    public const int HeadshotBatchLimit = 100;
    public const string AvatarPrefix = "avatar-";
    public Task<IReadOnlyDictionary<long, string>> HeadshotsAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken);
}

// ---- src/Source/AvatarBook.cs (new) ----
public interface IAvatarSource
{
    Task<IReadOnlyDictionary<long, string>> HeadshotsAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken);
}
public sealed class AvatarBook(IAvatarSource source)
{
    public IReadOnlyDictionary<long, string> Files { get; }                        // a copy, safe to hand to a model
    public string? FileFor(long userId);
    public Task<bool> AskAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken);   // true when the map changed
    public void Keep(IReadOnlySet<long> yours);
}

// ---- src/Board/PanelModels.cs, src/Board/AccountsTableModel.cs ----
// LiveBoard gains a trailing  IReadOnlyDictionary<long, string>? Avatars = null  and:
public string? AvatarFor(long userId);                                             // null for any id that isn't yours
// Each row gains a trailing  string? Avatar = null :
//   AccountRow (board table), AccountLineModel, PromotionRow, AccountCardModel, AccountRow (Setup, src/UI/Setup/AccountsModel.cs)

// ---- src/Composition ----
// ISetupServices gains:  string? AvatarFileFor(long userId);
// AccountsModel.Rows gains a trailing  Func<long, string?>? avatar = null

// ---- src/UI/Controls/AvatarFill.cs (new) ----
public sealed class AvatarFill : IValueConverter                                    // a cached file path -> a frozen round ImageBrush, or null
```

**Rulings made while planning**

Recorded so a reviewer doesn't read them as drift. Each names what was decided, why, and the cost if it is wrong. They
continue the plan's A-numbering.

- **A21. The picture path lives in `IconClient`; the session's map lives in `AvatarBook`; tests get a fake.** The batch
  headshot request goes in `IconClient.HeadshotsAsync`, beside the recipe-icon path it already owns, and it implements a
  new `IAvatarSource`. `AvatarBook` holds what has been asked for and what came back, takes an `IAvatarSource`, and is
  what the tests exercise. `AppServices` builds one book from its one `IconClient`. *Why:* `NoHostnameFenceTests` names
  exactly two files that may write a hostname; a third file naming `thumbnails.roblox.com` would mean editing the fence,
  and the avatar fetch is the same host, the same handler, the same User-Agent, the same size cap and the same picture
  domain check that file already enforces. *Cost if wrong:* `IconClient` does two jobs; they are 40 lines apart and share
  every guard.
- **A22. Only your own accounts, checked twice.** `AppServices.AvatarFileFor` answers only for an id in
  `LiveBoard.UserIdsOf(KnownAccounts)`, and `LiveBoard.AvatarFor` returns null for any id that isn't in `MyUserIds`,
  whatever the map holds. `TopRow` and `LeaderRow` have no `Avatar` member at all, so the Live leaderboard and Top of the
  battle cannot draw one even for your own row. `AvatarFenceTests` pins all of it, plus "only `IconClient.cs` names the
  headshot endpoint" and "only `AppServices.cs` asks the book". *Why:* other players' ids are read off a public
  leaderboard and dropped; asking Roblox for their pictures would send those ids back out and leave a file per player on
  this PC. *Cost if wrong:* none known; the two checks are each two lines.
- **A23. One ask per id per session, in one request, and a new account is asked at the next accounts refresh.**
  `AvatarBook.AskAsync` asks only for ids it hasn't asked for this session, `IconClient` sends them 100 to a request, and
  `AppServices` calls it after the book loads and after every `RefreshAccountsAsync` — after the numbers, never before.
  A user whose picture Roblox says is `Pending`, `Blocked` or anything but `Completed` is no icon this time and is not
  asked again this session; nor is a failed fetch. An id that stops being yours is forgotten by `Keep`, and is asked for
  again if RoRoRo lists it later. *Why:* a redraw happens on every read; re-asking on each one would be twenty requests an
  hour against Roblox for a picture that doesn't move, and that is exactly the reasoning `NameClient` already carries.
  *Cost if wrong:* an account whose headshot wasn't rendered yet when you started shows no picture until the next start.
- **A24. The files sit in the icon cache, named `avatar-<userId>.png`, and live seven days.** The same
  `%LOCALAPPDATA%\626labs.ur-score\icon-cache` folder, the same `IconClient.CacheFor`, the same freshness check on the
  file's last write time: a file younger than seven days is used with no request; an older one is fetched again at the
  first ask of the session that wants it. *Why:* one folder to clear, one rule to explain, and no new path to disclose.
  The prefix keeps them apart from a recipe icon's asset-id name and its `url-…` hash, and a Roblox user id is a public
  number that is only ever one of the owner's own here. *Cost if wrong:* someone who changes their avatar sees the old
  headshot for up to a week; deleting the folder fixes it.
- **A25. 48x48, round, one property name, one style.** The request asks for `size=48x48&format=Png&isCircular=false`;
  Ur Score does the round crop itself with an `Ellipse` filled by an `ImageBrush`, so the picture is round on every
  surface even if Roblox's circular option ever changes. Every row model calls the member `Avatar`, so one shared
  `AccountAvatar` style in `App.xaml` draws all five surfaces and a surface can only change size. *Why:* the design says
  one look everywhere, and a shared style is the only way a fence-free change stays consistent. *Cost if wrong:* at 200 %
  display scaling the 40 px account-card header upscales a 48 px picture and looks soft; it is one constant.
- **A26. The slot is laid out whether or not a picture is there — `Hidden`, never `Collapsed`.** The ellipse takes its
  space from the first draw, so a picture that arrives late, fails, or never comes does not move a single pixel of the
  row, and there is never an error line. *Why:* "icons never block" has to mean the numbers you are reading don't jump
  under your eyes; `Collapsed` would reflow the name column the moment a fetch landed. *Cost if wrong:* someone whose
  accounts have no pictures at all sees names indented by a constant 28 px (32 px on the card, 32 px on Setup) with
  nothing in it.
- **A27. One fetch serves every window.** There is one `IconClient`, one `AvatarBook` and one cache folder in the
  process. Pop-outs are drawn by `BoardWindow.RenderPanel` from the same `LiveBoard`, and Setup pages read
  `ISetupServices.AvatarFileFor`, so a popped-out accounts table and Setup › Your accounts show the same files with no
  second request. *Cost if wrong:* none known; a second process (there is none) would share the files through the cache
  anyway.
- **A28. The import screen says nothing new; the README row and a line on Setup › Your accounts are the disclosure.**
  "YOUR PC WILL CONTACT" lists what *that recipe* makes your PC contact, and `ImportReview` stays per-recipe. Avatars are
  Ur Score's own feature, like the username lookup, which has never been on that screen either. Instead the README's
  "What leaves your machine" table gains a row for Roblox's picture service (and its stale "three destinations" and "only
  outbound calls are the two named above" sentences are corrected), and Setup › Your accounts carries a standing line
  saying what is sent and that no other player's id ever is. *Why:* putting an Ur Score-wide call in a per-recipe consent
  list would make every import ask about something the recipe doesn't do. *Cost if wrong:* someone reads the import
  screen as the complete list of everything Ur Score contacts; a later version can give that screen its own "Ur Score
  itself" section.
- **A29. The picture is decoration: no automation id, no accessible name, no smoke-script change.** The row already
  carries the account's name for UI Automation, and a second "picture of X" would double every row for a screen reader.
  Nothing in the automation-id table changes, so the walks in `tools/smoke` run unchanged — which matters this cycle,
  because another agent owns that folder. The picture is proved by eye in the live look (A30's step), not by a walk.
  *Cost if wrong:* a Narrator user is told nothing about the picture, which is what a decorative image should do.
- **A30. It ships inside 0.3.2.** Task 5's `CHANGELOG.md` **Added** list gains exactly this line: "Each of your own
  accounts shows its Roblox avatar beside its name — in the accounts table, My accounts, Promotion check, the account
  card and Setup › Your accounts. Pictures load after the numbers and never hold a row up. Other players are never
  looked up: the leaderboard and Top of the battle show names only." Backlog V3-S.6 (the window icon at start) is
  untouched and stays OPEN. *Cost if wrong:* one line in one release note.

- [ ] **Step 1: Write the failing tests**

Create `tests/AvatarBookTests.cs`:

```csharp
using Labs626.UrScore.Source;

namespace UrScore.Tests;

/// <summary>
/// The pictures beside your own accounts. These pin the three promises: an id is asked for once a session, only the ids
/// an ask asked for are kept, and nothing here ever throws — a failure costs the pictures and nothing else.
/// </summary>
public class AvatarBookTests
{
    private sealed class FakeAvatars : IAvatarSource
    {
        public List<long[]> Asks { get; } = [];

        public Func<IReadOnlyCollection<long>, IReadOnlyDictionary<long, string>> Answer { get; set; } =
            ids => ids.ToDictionary(id => id, id => $@"C:\cache\avatar-{id}.png");

        public Exception? Throws { get; set; }

        public Task<IReadOnlyDictionary<long, string>> HeadshotsAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken)
        {
            Asks.Add([.. userIds]);
            cancellationToken.ThrowIfCancellationRequested();
            return Throws is { } ex
                ? Task.FromException<IReadOnlyDictionary<long, string>>(ex)
                : Task.FromResult(Answer(userIds));
        }
    }

    private static string[] Asked(FakeAvatars source) => [.. source.Asks.Select(a => string.Join(",", a))];

    [Fact]
    public async Task EachOfYourAccountsIsAskedForOnceASession()
    {
        var source = new FakeAvatars();
        var book = new AvatarBook(source);

        Assert.True(await book.AskAsync(new long[] { 101, 201, 101 }, CancellationToken.None));
        Assert.False(await book.AskAsync(new long[] { 101, 201 }, CancellationToken.None));
        Assert.True(await book.AskAsync(new long[] { 101, 202 }, CancellationToken.None));

        Assert.Equal(new[] { "101,201", "202" }, Asked(source));
        Assert.Equal(@"C:\cache\avatar-202.png", book.FileFor(202));
        Assert.Null(book.FileFor(999));
    }

    [Fact]
    public async Task AnIdWithNoRobloxUserIsNeverAskedAbout()
    {
        var source = new FakeAvatars();

        Assert.False(await new AvatarBook(source).AskAsync(new long[] { 0 }, CancellationToken.None));
        Assert.Empty(source.Asks);
    }

    [Fact]
    public async Task OnlyTheIdsAnAskAskedForAreKept()
    {
        // Roblox only ever answers for the ids sent, so this is inert against the real service. Keeping "asked for" and
        // "known" the same set is the point: a picture for anyone else must have nowhere to land.
        var source = new FakeAvatars
        {
            Answer = _ => new Dictionary<long, string> { [101] = "mine.png", [999] = "someone-else.png" },
        };
        var book = new AvatarBook(source);

        Assert.True(await book.AskAsync(new long[] { 101 }, CancellationToken.None));

        Assert.Equal(new long[] { 101 }, book.Files.Keys.Order().ToArray());
    }

    [Fact]
    public async Task APictureThatCouldNotBeFetchedCostsNothingElseAndIsNotRetriedInALoop()
    {
        var source = new FakeAvatars { Throws = new InvalidOperationException("no network") };
        var book = new AvatarBook(source);

        Assert.False(await book.AskAsync(new long[] { 101 }, CancellationToken.None));
        Assert.Empty(book.Files);

        source.Throws = null;
        Assert.False(await book.AskAsync(new long[] { 101 }, CancellationToken.None));
        Assert.Single(source.Asks);
    }

    [Fact]
    public async Task AStopYouAskedForLeavesThoseIdsToBeAskedAgain()
    {
        var source = new FakeAvatars();
        var book = new AvatarBook(source);
        using var stopped = new CancellationTokenSource();
        await stopped.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => book.AskAsync(new long[] { 101 }, stopped.Token));

        Assert.True(await book.AskAsync(new long[] { 101 }, CancellationToken.None));
        Assert.Equal(new[] { "101", "101" }, Asked(source));
    }

    [Fact]
    public async Task AnAccountThatIsNoLongerYoursIsForgotten()
    {
        var source = new FakeAvatars();
        var book = new AvatarBook(source);
        await book.AskAsync(new long[] { 101, 201 }, CancellationToken.None);

        book.Keep(new HashSet<long> { 101 });

        Assert.Equal(new long[] { 101 }, book.Files.Keys.Order().ToArray());
        Assert.True(await book.AskAsync(new long[] { 101, 201 }, CancellationToken.None));
        Assert.Equal(new[] { "101,201", "201" }, Asked(source));
    }
}
```

Create `tests/AvatarFenceTests.cs`:

```csharp
using Labs626.UrScore.Board;
using Labs626.UrScore.Recipes;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

/// <summary>
/// A picture beside a row is only ever one of YOUR OWN accounts'. Other members' ids are read off a public leaderboard,
/// compared against your accounts and dropped (README, "What leaves your machine"); asking Roblox for their pictures
/// would send those ids back out and leave a file of each one on this PC. These stand where a refactor would cross that
/// line quietly.
/// </summary>
public class AvatarFenceTests
{
    [Fact]
    public void OnlyYourOwnAccountsEverHaveAPicture()
    {
        var live = Live([], [], new Dictionary<string, RecipeSnapshot>()) with
        {
            Avatars = new Dictionary<long, string> { [Main.RobloxUserId] = "mine.png", [999] = "someone-else.png" },
        };

        Assert.Equal("mine.png", live.AvatarFor(Main.RobloxUserId));
        Assert.Null(live.AvatarFor(999));
        Assert.Null(live.AvatarFor(0));
        Assert.Null(Live([], [], new Dictionary<string, RecipeSnapshot>()).AvatarFor(Main.RobloxUserId));
    }

    [Fact]
    public void TheLeaderboardAndTheTopHaveNowhereToPutAPicture()
    {
        // Structural, not a convention: these rows have no such member, so no template can bind one.
        Assert.Null(typeof(LeaderRow).GetProperty("Avatar"));
        Assert.Null(typeof(TopRow).GetProperty("Avatar"));

        foreach (var file in new[] { "LiveLeaderboardPanel.xaml", "TopPanel.xaml" })
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "UI", "Panels", file));
            Assert.DoesNotContain("Avatar", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ThePictureLookupLivesInOneFileAndIsOnlyEverAskedWithYourOwnAccountIds()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var files = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Select(f => (Relative: Path.GetRelativePath(src, f), Text: File.ReadAllText(f)))
            .ToList();

        Assert.Equal(
            new[] { Path.Combine("Source", "AvatarBook.cs"), Path.Combine("Source", "IconClient.cs") },
            files.Where(f => f.Text.Contains("HeadshotsAsync", StringComparison.Ordinal))
                .Select(f => f.Relative).Order(StringComparer.Ordinal).ToArray());

        Assert.Equal(
            new[] { Path.Combine("Source", "IconClient.cs") },
            files.Where(f => f.Text.Contains("avatar-headshot", StringComparison.Ordinal)).Select(f => f.Relative).ToArray());

        Assert.Equal(
            new[] { Path.Combine("Composition", "AppServices.cs") },
            files.Where(f => f.Text.Contains(".AskAsync(", StringComparison.Ordinal)).Select(f => f.Relative).ToArray());

        var app = files.Single(f => string.Equals(f.Relative, Path.Combine("Composition", "AppServices.cs"), StringComparison.Ordinal)).Text;
        Assert.Contains("LiveBoard.UserIdsOf(KnownAccounts)", app, StringComparison.Ordinal);
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

In `tests/IconClientTests.cs`, add these four members after the `ARecipesOwnHostsAreItsStepsAndItsSearchLists` test (the
last one in the class), inside the class:

```csharp
    private const string HeadshotsUrl =
        "https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds=101,201&size=48x48&format=Png&isCircular=false";

    private const string HeadshotUrl = "https://tr.rbxcdn.com/AVATAR-101/48/48/AvatarHeadshot/Png/noFilter";

    private static string Headshots(params (long UserId, string State, string Url)[] rows) =>
        "{ \"data\": [ " + string.Join(", ", rows.Select(r =>
            $"{{ \"targetId\": {r.UserId}, \"state\": \"{r.State}\", \"imageUrl\": \"{r.Url}\" }}")) + " ] }";

    [Fact]
    public async Task YourAccountsPicturesComeBackInOneRequestAndAreCachedByUserId()
    {
        var handler = new RouteHandler()
            .On(HeadshotsUrl, () => Json(Headshots((101, "Completed", HeadshotUrl), (201, "Pending", ""))))
            .On(HeadshotUrl, () => Bytes(Png));

        var files = await Client(handler).HeadshotsAsync(new long[] { 101, 201 }, CancellationToken.None);

        // A picture Roblox hasn't rendered yet is no icon this time, and costs nobody else theirs.
        Assert.Equal(new long[] { 101 }, files.Keys.ToArray());
        Assert.Equal(Path.Combine(_dir, "avatar-101.png"), files[101]);
        Assert.Equal(Png, File.ReadAllBytes(files[101]));
        Assert.Equal(new[] { HeadshotsUrl, HeadshotUrl }, handler.Requests.Select(r => r.RequestUri!.AbsoluteUri).ToArray());
        Assert.All(handler.Requests, r => Assert.Contains("UrScore", r.Headers.UserAgent.ToString()));
    }

    [Fact]
    public async Task ACachedPictureYoungerThanSevenDaysIsUsedWithoutAsking()
    {
        Directory.CreateDirectory(_dir);
        var cached = Path.Combine(_dir, "avatar-101.png");
        File.WriteAllBytes(cached, Png);
        File.SetLastWriteTimeUtc(cached, Now.AddDays(-6).UtcDateTime);
        var handler = new RouteHandler();

        var files = await Client(handler).HeadshotsAsync(new long[] { 101 }, CancellationToken.None);

        Assert.Equal(cached, files[101]);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task APictureOffRobloxsOwnDomainIsRefusedAndOnlyTheIdsAskedForAreFetched()
    {
        var handler = new RouteHandler()
            .On(HeadshotsUrl, () => Json(Headshots(
                (101, "Completed", "https://evil.example/headshot.png"),
                (201, "Completed", HeadshotUrl),
                (999, "Completed", HeadshotUrl))))
            .On("https://evil.example/headshot.png", () => Bytes(Png))
            .On(HeadshotUrl, () => Bytes(Png));

        var files = await Client(handler).HeadshotsAsync(new long[] { 101, 201 }, CancellationToken.None);

        Assert.Equal(new long[] { 201 }, files.Keys.ToArray());
        Assert.Equal(new[] { HeadshotsUrl, HeadshotUrl }, handler.Requests.Select(r => r.RequestUri!.AbsoluteUri).ToArray());
        Assert.False(File.Exists(Path.Combine(_dir, "avatar-999.png")));
        Assert.False(File.Exists(Path.Combine(_dir, "avatar-101.png")));
    }

    [Fact]
    public async Task APictureServiceThatAnswersWithNothingUsefulCostsOnlyThePictures()
    {
        var handler = new RouteHandler().On(HeadshotsUrl, () => Json("{ \"data\": \"not a list\" }"));

        Assert.Empty(await Client(handler).HeadshotsAsync(new long[] { 101, 201 }, CancellationToken.None));
        Assert.Single(handler.Requests);
    }
```

In `tests/PanelModelsTests.cs`, add these two tests at the end of the class:

```csharp
    [Fact]
    public void YourOwnAccountsRowsCarryTheirPictureAndNobodyElsesDoes()
    {
        var pictures = new Dictionary<long, string>
        {
            [Main.RobloxUserId] = @"C:\cache\avatar-101.png",
            [999] = @"C:\cache\avatar-999.png",
        };
        var live = ProfileLive("diamonds", "rank") with { Avatars = pictures };

        var table = PanelModels.AccountsTable(live, DiamondsBook(), TableSettings, new AccountSort(AccountSort.NameKey, Descending: false));
        var mine = PanelModels.MyAccounts(live, DiamondsBook(), new PanelSettings(Profile.Slug, Stat: "diamonds"));
        var card = PanelModels.AccountCard(live, DiamondsBook(), new PanelSettings(Profile.Slug, Stat: "diamonds", UserId: Main.RobloxUserId));

        Assert.Equal(@"C:\cache\avatar-101.png", table.Rows.First(r => r.Name == Main.DisplayName).Avatar);
        Assert.Null(table.Rows.First(r => r.Name == AltOne.DisplayName).Avatar);
        Assert.Null(table.Rows[^1].Avatar);
        Assert.Equal(@"C:\cache\avatar-101.png", mine.Groups.SelectMany(g => g.Rows).First(r => r.UserId == Main.RobloxUserId).Avatar);
        Assert.Equal(@"C:\cache\avatar-101.png", card.Avatar);

        // A picture for anyone but you is never drawn, whatever the map holds.
        Assert.Null(live.AvatarFor(999));
        Assert.Null(live.AvatarFor(0));
    }

    [Fact]
    public void ThePromotionCheckShowsYourPictureBesideEachAccountItWouldPlace()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var alts = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
        var live = Live([main, alts], [Installed(Clan, "value")],
            Snaps(Snapshot(main.Id, [Row(5, 900), Row(6, 700)]), Snapshot(alts.Id, [Row(Main.RobloxUserId, 800)]))) with
        {
            Avatars = new Dictionary<long, string> { [Main.RobloxUserId] = @"C:\cache\avatar-101.png" },
        };

        var model = PanelModels.PromotionCheck(live, new PanelSettings(Clan.Slug, SourceId: alts.Id, ToSourceId: main.Id, Stat: "value"));

        Assert.Equal(@"C:\cache\avatar-101.png", Assert.Single(model.Rows).Avatar);
    }
```

In `tests/AccountsModelTests.cs`, add this test at the end of the class:

```csharp
    [Fact]
    public void EachSetupRowCarriesItsOwnAccountsPicture()
    {
        var rows = AccountsModel.Rows([Main, Alt], [Sending(Profile)], [], new Dictionary<string, RecipeSnapshot>(),
            id => id == Main.RobloxUserId ? @"C:\cache\avatar-101.png" : null);

        Assert.Equal(@"C:\cache\avatar-101.png", rows[0].Avatar);
        Assert.Null(rows[1].Avatar);

        // With no lookup (a page that hasn't one yet), every row is simply pictureless.
        Assert.Null(AccountsModel.Rows([Main], [Sending(Profile)], [], new Dictionary<string, RecipeSnapshot>())[0].Avatar);
    }
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`
Expected: FAIL to compile (`IAvatarSource`, `AvatarBook`, `IconClient.HeadshotsAsync`, `LiveBoard.Avatars`, `AvatarFor`
and each row's `Avatar` don't exist).

- [ ] **Step 3: The headshot path**

In `src/Source/IconClient.cs`, change the class line to:

```csharp
public sealed class IconClient : IAvatarSource
```

and add to its summary, after the paragraph beginning "The second named exemption":

```csharp
/// <para>
/// It also fetches the headshot of each of YOUR OWN accounts (<see cref="HeadshotsAsync"/>), for the rows that name
/// them. Same host, same handler, same User-Agent, same size cap, same picture-domain check. No other player's id is
/// ever passed in: <c>AppServices</c> asks with RoRoRo's list of your accounts and nothing else (plan A22).
/// </para>
```

Add these constants after `public const int MaxBytes = 1024 * 1024;`:

```csharp
    /// <summary>Roblox's own supported headshot size, the nearest above the 20-40 px a row draws (plan A25).</summary>
    public const string HeadshotSize = "48x48";

    /// <summary>The endpoint's ceiling, and well above the 256 accounts RoRoRo's history limit allows.</summary>
    public const int HeadshotBatchLimit = 100;

    /// <summary>What a cached headshot is called in the icon cache, so it can never collide with a recipe's icon (plan A24).</summary>
    public const string AvatarPrefix = "avatar-";
```

Add these two members after `ResolveAsync`, before `IsPictureHost`:

```csharp
    /// <summary>
    /// The cached headshot of each of <paramref name="userIds"/>, fetching the ones the cache has none younger than
    /// <see cref="CacheFor"/> for. Only your own accounts' ids are ever passed in (plan A22). A user Roblox has no
    /// finished picture for, and one whose picture is off Roblox's picture domain, is simply left out. Never throws for
    /// anything but a stop the caller asked for: a failure costs the pictures, and the rows keep their names.
    /// </summary>
    public async Task<IReadOnlyDictionary<long, string>> HeadshotsAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken)
    {
        var files = new Dictionary<long, string>();
        var wanted = userIds.Where(id => id > 0).Distinct().ToList();

        for (var offset = 0; offset < wanted.Count; offset += HeadshotBatchLimit)
        {
            var batch = wanted.Skip(offset).Take(HeadshotBatchLimit).ToList();
            var asked = batch.ToHashSet();

            var missing = new List<long>();
            foreach (var id in batch)
            {
                var cached = CachePath(AvatarKey(id));
                if (IsFresh(cached)) files[id] = cached;
                else missing.Add(id);
            }

            if (missing.Count == 0) continue;

            foreach (var (userId, url) in await HeadshotUrlsAsync(missing, cancellationToken).ConfigureAwait(false))
            {
                // Only what this batch asked for, so an answer carrying anyone else cannot reach the cache.
                if (!asked.Contains(userId) || !IsPictureHost(url)) continue;

                if (await DownloadAsync(url, CachePath(AvatarKey(userId)), cancellationToken).ConfigureAwait(false) is { } file)
                {
                    files[userId] = file;
                }
            }
        }

        return files;
    }
```

and these two private members just before `IsPng`:

```csharp
    private static string AvatarKey(long userId) => AvatarPrefix + userId.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// One batch ask for headshots. Only a row Roblox calls <c>Completed</c> has a picture to fetch: <c>Pending</c>,
    /// <c>Blocked</c> and the rest mean no icon this time, and no reason to spoil the batch for everyone else.
    /// </summary>
    private async Task<IReadOnlyList<(long UserId, Uri Url)>> HeadshotUrlsAsync(IReadOnlyList<long> userIds, CancellationToken cancellationToken)
    {
        var ids = string.Join(',', userIds.Select(id => id.ToString(CultureInfo.InvariantCulture)));
        var address = new Uri(
            $"https://{ThumbnailsHost}/v1/users/avatar-headshot?userIds={ids}&size={HeadshotSize}&format=Png&isCircular=false");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_requestTimeout);

            using var request = Get(address);
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return [];

            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);

            if (!JsonNav.TryGet(document.RootElement, "data", out var data) || data.ValueKind != JsonValueKind.Array) return [];

            var found = new List<(long, Uri)>();
            foreach (var row in data.EnumerateArray())
            {
                if (!JsonNav.TryGet(row, "state", out var state) || state.ValueKind != JsonValueKind.String
                    || !string.Equals(state.GetString(), "Completed", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!JsonNav.TryGet(row, "targetId", out var target) || !JsonNav.TryUserId(target, out var userId)) continue;
                if (!JsonNav.TryGet(row, "imageUrl", out var imageUrl) || imageUrl.ValueKind != JsonValueKind.String) continue;
                if (!Uri.TryCreate(imageUrl.GetString(), UriKind.Absolute, out var url)) continue;

                found.Add((userId, url));
            }

            return found;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return [];
        }
    }
```

- [ ] **Step 4: The session's pictures**

Create `src/Source/AvatarBook.cs`:

```csharp
namespace Labs626.UrScore.Source;

/// <summary>The seam the app and the tests use for headshots, so neither needs the network. <see cref="IconClient"/> implements it.</summary>
public interface IAvatarSource
{
    Task<IReadOnlyDictionary<long, string>> HeadshotsAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken);
}

/// <summary>
/// The picture beside each of your own accounts, for as long as the app runs (plan A21-A23).
/// <para>
/// Every id it is given is one of YOUR accounts: <c>AppServices</c> asks with RoRoRo's list and nothing else, and only
/// what an ask asked for is ever kept. Nobody else's id reaches this class, so nobody else's picture can reach the disk.
/// </para>
/// <para>
/// An id is asked about ONCE a session, whatever comes back. A board redraws on every read; re-asking each time would be
/// twenty requests an hour against Roblox for a picture that doesn't move. A picture Roblox hasn't rendered yet, and one
/// that couldn't be fetched, cost the picture until the next start — never a retry loop under a redraw.
/// </para>
/// <para>
/// Never throws except for a stop the caller asked for, and those ids are left to be asked again. Locked, because
/// <c>AppServices</c> may ask from a fetching thread while the UI thread reads <see cref="Files"/>.
/// </para>
/// </summary>
public sealed class AvatarBook(IAvatarSource source)
{
    private readonly object _gate = new();
    private readonly Dictionary<long, string> _files = [];
    private readonly HashSet<long> _asked = [];

    /// <summary>A copy, safe to hand to a view model that outlives the next ask.</summary>
    public IReadOnlyDictionary<long, string> Files
    {
        get
        {
            lock (_gate) return new Dictionary<long, string>(_files);
        }
    }

    public string? FileFor(long userId)
    {
        lock (_gate) return _files.GetValueOrDefault(userId);
    }

    /// <summary>
    /// Asks for the pictures of the ids not asked about yet this session, and keeps what came back for those ids.
    /// True when the map changed, so the caller can redraw.
    /// </summary>
    public async Task<bool> AskAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken)
    {
        List<long> missing;
        lock (_gate)
        {
            missing = [.. userIds.Where(id => id > 0 && _asked.Add(id))];
        }

        if (missing.Count == 0) return false;

        IReadOnlyDictionary<long, string> found;
        try
        {
            found = await source.HeadshotsAsync(missing, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A stop the user asked for is not an answer: these ids are still unknown.
            lock (_gate)
            {
                foreach (var id in missing) _asked.Remove(id);
            }

            throw;
        }
        catch (Exception)
        {
            return false;
        }

        var wanted = missing.ToHashSet();
        lock (_gate)
        {
            var changed = false;
            foreach (var (id, file) in found)
            {
                if (!wanted.Contains(id)) continue;
                if (string.Equals(_files.GetValueOrDefault(id), file, StringComparison.Ordinal)) continue;

                _files[id] = file;
                changed = true;
            }

            return changed;
        }
    }

    /// <summary>Your accounts changed: an id that is no longer yours is forgotten, and asked about again if it comes back.</summary>
    public void Keep(IReadOnlySet<long> yours)
    {
        lock (_gate)
        {
            foreach (var gone in _files.Keys.Where(id => !yours.Contains(id)).ToList()) _files.Remove(gone);
            foreach (var gone in _asked.Where(id => !yours.Contains(id)).ToList()) _asked.Remove(gone);
        }
    }
}
```

- [ ] **Step 5: The rows carry a picture**

In `src/Board/PanelModels.cs`:

1. Give `LiveBoard` a trailing parameter — replace `    bool Running)` in its declaration with:

```csharp
    bool Running,
    IReadOnlyDictionary<long, string>? Avatars = null)
```

2. Add this member to `LiveBoard`, after `AccountName`:

```csharp
    /// <summary>
    /// The cached picture for one of YOUR accounts, or null. An id that isn't yours has none, whatever the map holds:
    /// the leaderboard and Top show other members by name only, and this is the second of the two checks (plan A22).
    /// </summary>
    public string? AvatarFor(long userId) =>
        userId != 0 && MyUserIds.Contains(userId) ? Avatars?.GetValueOrDefault(userId) : null;
```

3. Give three row records a trailing `string? Avatar = null`:

```csharp
public sealed record AccountLineModel(long UserId, string Name, string Value, string InGroup, string Change, bool Sent, bool Stalled, bool Missing, string? Avatar = null);

public sealed record PromotionRow(string Name, string Value, string WouldPlace, bool Fits, bool Missing, string? Avatar = null);
```

and in `AccountCardModel`, replace `    IReadOnlyList<FactModel> Facts, string ChartName)` with:

```csharp
    IReadOnlyList<FactModel> Facts, string ChartName, string? Avatar = null)
```

4. Fill them in at the five build sites. In `MyAccounts`, the line inside `foreach (var account in mine)` becomes:

```csharp
                lines.Add((value, new AccountLineModel(
                    account.RobloxUserId,
                    account.DisplayName,
                    PanelText.Value(value, stat.Format, zone),
                    value is not null && ranks.TryGetValue(account.RobloxUserId, out var rank) ? $"#{rank} of {rows.Count}" : Dash,
                    RecentChange(series[account.RobloxUserId], stat.Format),
                    sent,
                    Records.Stalled(series[account.RobloxUserId], others),
                    value is null,
                    live.AvatarFor(account.RobloxUserId))));
```

and the "not in a watched" group's line becomes:

```csharp
                [.. rest.OrderBy(a => a.DisplayName, StringComparer.Ordinal)
                    .Select(a => new AccountLineModel(a.RobloxUserId, a.DisplayName, Dash, Dash, Dash, false, false, true, live.AvatarFor(a.RobloxUserId)))]));
```

In `PromotionCheck`, the two `rows.Add` lines become:

```csharp
                rows.Add((null, new PromotionRow(account.DisplayName, Dash, Dash, false, true, live.AvatarFor(account.RobloxUserId))));
```

```csharp
            rows.Add((value, new PromotionRow(account.DisplayName, StatText.Abbrev(value), text, fits, false, live.AvatarFor(account.RobloxUserId))));
```

In `AccountCard`, the returned model becomes:

```csharp
        return new AccountCardModel(
            new PanelHead(title, $"{pickedAccount.DisplayName} · {live.SourceName(pickedSource)}", Overdue: live.IsOverdue(pickedSource)),
            stat.Label, PanelText.Value(ValueOf(pickedRow, stat.Key), stat.Format, zone), sections, line, facts,
            $"{pickedAccount.DisplayName}'s {stat.Label} over time",
            live.AvatarFor(pickedAccount.RobloxUserId));
```

In `AccountsTable`, the `rows.Add` becomes:

```csharp
            rows.Add((sorted is null || row is null ? null : ValueOf(row, sorted.Key), new AccountRow(
                account.RobloxUserId,
                account.DisplayName,
                cells,
                snapshot?.Unavailable.GetValueOrDefault(account.RobloxUserId) ?? "",
                Missing: row is null,
                Picked: account.RobloxUserId == pickedUserId,
                Avatar: live.AvatarFor(account.RobloxUserId))));
```

5. In `src/Board/AccountsTableModel.cs`, the table row gains the same trailing member (the totals row keeps null, so
   "Total" lines up under the names):

```csharp
/// <summary>One of your accounts, or the totals row (user id 0). Cells line up with the columns, the name first.</summary>
public sealed record AccountRow(
    long UserId, string Name, IReadOnlyList<string> Cells, string Note, bool Missing, bool Picked, bool IsTotal = false, string? Avatar = null)
```

6. In `src/UI/Setup/AccountsModel.cs`, the Setup row gains the same member and `Rows` takes the lookup:

```csharp
public sealed record AccountRow(Guid AccountId, string DisplayName, string FoundIn, IReadOnlyList<SendTick> Sends, string? Avatar = null);
```

```csharp
    public static IReadOnlyList<AccountRow> Rows(
        IReadOnlyList<HostAccount> accounts, IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources,
        IReadOnlyDictionary<string, RecipeSnapshot> latest, Func<long, string?>? avatar = null)
    {
        var sending = SendingRecipes(installed);
        return [.. accounts.Select(account => new AccountRow(
            account.AccountId,
            account.DisplayName,
            FoundIn(account, installed, sources, latest),
            [.. sending.Select(r => new SendTick
            {
                RecipeSlug = r.Recipe.Slug,
                AccountId = account.AccountId,
                Name = $"Send {account.DisplayName} for {r.Recipe.Name}",
                On = !r.State.Excluded.Contains(account.AccountId),
            })],
            avatar?.Invoke(account.RobloxUserId)))];
    }
```

- [ ] **Step 6: The wiring**

In `src/Composition/ISetupServices.cs`, add after `IconFileFor`:

```csharp
    /// <summary>The cached picture for one of your own accounts, once it has been fetched, else null. Never another player's.</summary>
    string? AvatarFileFor(long userId);
```

In `src/Composition/AppServices.cs`:

1. Add to the aliases under the namespace (the file's `using Source = Labs626.UrScore.Core.Source;` hides the
   `Labs626.UrScore.Source` namespace, which is why `NameClient` and `IconClient` are aliased there already):

```csharp
using AvatarBook = Labs626.UrScore.Source.AvatarBook;
```

2. Add the field after `private readonly IconClient _icons;`:

```csharp
    private readonly AvatarBook _avatars;
```

3. In the constructor, straight after the `_icons = new IconClient(...)` line:

```csharp
        _avatars = new AvatarBook(_icons);
```

4. Add after `IconFileForBoard()`:

```csharp
    /// <summary>The picture for one of your own accounts (plan A22): an id RoRoRo isn't listing as yours has none.</summary>
    public string? AvatarFileFor(long userId) =>
        LiveBoard.UserIdsOf(KnownAccounts).Contains(userId) ? _avatars.FileFor(userId) : null;
```

5. `CurrentBoard()` hands the map to the panels:

```csharp
    public LiveBoard CurrentBoard() => new(
        Sources, Installed,
        new Dictionary<string, RecipeSnapshot>(_latest, StringComparer.Ordinal),
        new Dictionary<string, DateTimeOffset>(_lastRead, StringComparer.Ordinal),
        KnownAccounts, _time, Runner.Running, _avatars.Files);
```

6. Add these two members just after `ApplyIconAsync` / `RaiseIconIfChanged`:

```csharp
    /// <summary>
    /// The pictures beside your own accounts, after the numbers (plan A23): the ids RoRoRo lists as yours, once each per
    /// session, off the UI thread. Anything that fails costs the pictures and nothing else.
    /// </summary>
    private void AskForAvatars()
    {
        var yours = LiveBoard.UserIdsOf(KnownAccounts);
        if (yours.Count == 0) return;

        _avatars.Keep(yours);
        _ = AskForAvatarsAsync(yours);
    }

    private async Task AskForAvatarsAsync(IReadOnlySet<long> yours)
    {
        try
        {
            if (await _avatars.AskAsync(yours, _closing.Token)) RaiseChanged();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // The type only: a message can carry anything. The rows keep their names either way.
            AddTrail($"AVATARS: your accounts' pictures could not be fetched ({ex.GetType().Name}).");
        }
    }
```

7. Call it from the two places that know your accounts. In `LoadBookOnceAsync`, after `AddTrail($"BOOK: loaded from {root}.");`:

```csharp
        AskForAvatars();
```

and in `RefreshAccountsAsync`, between `WarnPastBudget();` and `RaiseChanged();`:

```csharp
        AskForAvatars();
```

8. In `src/UI/Setup/AccountsPage.xaml.cs`, `Refresh` passes the lookup:

```csharp
        _rows = AccountsModel.Rows(accounts, _services.Installed, _services.Sources, _services.Latest, _services.AvatarFileFor);
```

- [ ] **Step 7: The look**

Create `src/UI/Controls/AvatarFill.cs`:

```csharp
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Labs626.UrScore.UI;

/// <summary>
/// A cached picture file beside one of your accounts, as a round fill (plan A25). Never throws: a file that is gone,
/// half-written or not a picture gives no brush, and the row shows what it showed before the picture arrived.
/// <para>
/// Brushes are kept by path and write time, so redrawing a board every three minutes doesn't decode the same twenty
/// pictures again. The map holds one entry per account, so it cannot grow past your account list.
/// </para>
/// </summary>
public sealed class AvatarFill : IValueConverter
{
    private readonly Dictionary<string, ImageBrush?> _brushes = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string file || file.Length == 0) return null;

        string key;
        try
        {
            key = $"{file}|{File.GetLastWriteTimeUtc(file).Ticks}";
        }
        catch (Exception)
        {
            return null;
        }

        if (_brushes.TryGetValue(key, out var known)) return known;

        ImageBrush? brush = null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(file);
            image.EndInit();
            image.Freeze();

            brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            brush.Freeze();
        }
        catch (Exception)
        {
            // A picture that doesn't decode costs the picture. The row keeps its space and its name.
        }

        _brushes[key] = brush;
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
```

In `src/App.xaml`, directly after `<ui:UpperCase x:Key="Upper" />`:

```xml
            <ui:AvatarFill x:Key="Avatar" />

            <!-- One of your own accounts' pictures: round, sized to its row, the same on every surface. The slot is laid
                 out whether or not a picture is there (Hidden, never Collapsed), so a picture that arrives late, fails or
                 never comes never moves a row. Only your own accounts ever have one. -->
            <Style x:Key="AccountAvatar" TargetType="Ellipse">
                <Setter Property="Width" Value="20" />
                <Setter Property="Height" Value="20" />
                <Setter Property="Margin" Value="0,0,8,0" />
                <Setter Property="VerticalAlignment" Value="Center" />
                <Setter Property="Stroke" Value="{DynamicResource DividerBrush}" />
                <Setter Property="StrokeThickness" Value="1" />
                <Setter Property="Fill" Value="{Binding Avatar, Converter={StaticResource Avatar}}" />
                <Style.Triggers>
                    <DataTrigger Binding="{Binding Avatar}" Value="{x:Null}">
                        <Setter Property="Visibility" Value="Hidden" />
                    </DataTrigger>
                </Style.Triggers>
            </Style>
```

In `src/UI/Panels/AccountsTablePanel.xaml`, the account column's template becomes (a `DockPanel`, so the name still
takes the width the picture leaves):

```xml
        <DataTemplate x:Key="AccountNameCell">
            <StackPanel>
                <DockPanel>
                    <Ellipse DockPanel.Dock="Left" Style="{StaticResource AccountAvatar}" />
                    <TextBlock Text="{Binding Name}" Style="{StaticResource RowCell}" />
                </DockPanel>
                <TextBlock Text="{Binding Note}" Style="{StaticResource Muted}" FontSize="11" MaxWidth="240" HorizontalAlignment="Left"
                           Visibility="{Binding HasNote, Converter={StaticResource BoolToVisible}}" />
            </StackPanel>
        </DataTemplate>
```

In `src/UI/Panels/MyAccountsPanel.xaml`, the row's first column becomes:

```xml
                                                    <DockPanel Grid.Column="0">
                                                        <Ellipse DockPanel.Dock="Left" Style="{StaticResource AccountAvatar}" />
                                                        <Ellipse DockPanel.Dock="Left" Width="6" Height="6" Margin="0,0,6,0" VerticalAlignment="Center"
                                                                 Fill="{DynamicResource CyanBrush}"
                                                                 Visibility="{Binding Sent, Converter={StaticResource BoolToVisible}}" />
                                                        <TextBlock Text="{Binding Name}" Style="{StaticResource RowCell}" />
                                                    </DockPanel>
```

In `src/UI/Panels/PromotionCheckPanel.xaml`, the row's first column becomes:

```xml
                                    <DockPanel Grid.Column="0">
                                        <Ellipse DockPanel.Dock="Left" Style="{StaticResource AccountAvatar}" />
                                        <TextBlock Text="{Binding Name}" Style="{StaticResource RowCell}" />
                                    </DockPanel>
```

In `src/UI/Panels/AccountCardPanel.xaml`, the two header lines are wrapped so the picture sits beside them:

```xml
                <DockPanel>
                    <Ellipse DockPanel.Dock="Left" Style="{StaticResource AccountAvatar}" Width="40" Height="40" Margin="0,0,12,0"
                             VerticalAlignment="Top" />
                    <StackPanel>
                        <TextBlock Text="{Binding BigLabel}" Style="{StaticResource KeyLabel}" />
                        <TextBlock Text="{Binding Big}" Style="{StaticResource BigNumber}" Margin="0,5,0,-5" />
                    </StackPanel>
                </DockPanel>
```

In `src/UI/Setup/AccountsPage.xaml`, the row's name cell becomes:

```xml
                            <DockPanel Grid.Column="0">
                                <Ellipse DockPanel.Dock="Left" Style="{StaticResource AccountAvatar}" Width="24" Height="24" />
                                <TextBlock Text="{Binding DisplayName}" FontWeight="SemiBold" VerticalAlignment="Center"
                                           TextTrimming="CharacterEllipsis" />
                            </DockPanel>
```

and the page gains the standing line (plan A28), directly under the "Send reports an account's sent stats…" line:

```xml
        <TextBlock Style="{StaticResource Muted}" Margin="0,0,0,12"
                   Text="Each picture comes from Roblox: Ur Score asks Roblox's picture service for your own accounts' pictures and keeps them on this PC. No other player's id is ever sent for a picture, and the leaderboard shows other members by name only." />
```

- [ ] **Step 8: What leaves your machine**

In `README.md`, under "What leaves your machine":

- Change `Three separate destinations, and they're not the same boundary:` to `Four separate destinations, and they're not the same boundary:`.
- Add this row to the end of the table:

```
| **Roblox's own public picture service** (`thumbnails.roblox.com`, and the picture host it names on `rbxcdn.com`) | Your own accounts' Roblox user ids, so each of your rows can show that account's avatar, and a recipe's icon id when the recipe has one. The pictures are kept in `%LOCALAPPDATA%\626labs.ur-score\icon-cache` and asked for again after seven days. | Any other player's id. The leaderboard and Top of the battle show other members by name only, and no picture of anyone else is ever asked for or kept. |
```

- Replace the last sentence of the paragraph beginning "Ur Score has no webhook of its own" with: `Its only outbound
  calls are the four named above — the game's API, the username lookup, and Roblox's picture service for your own
  accounts' avatars and a recipe's icon — and its only inbound connection is the local pipe to RoRoRo.`

- [ ] **Step 9: Run the tests, then the build gate**

Run, from the repo root, with Ur Score closed (a running copy locks `bin\Release`):

```
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~AvatarBookTests|FullyQualifiedName~AvatarFenceTests|FullyQualifiedName~IconClientTests"
dotnet build Ur-Score.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build
```

Expected: all four pass. The app build is run on its own because a XAML mistake (a style key, a `DockPanel` that
swallowed a column) only shows there. If `YourAccountsPicturesComeBackInOneRequestAndAreCachedByUserId` fails on the
request list, compare `handler.Requests[0].RequestUri.AbsoluteUri` with `HeadshotsUrl` character by character: the comma
between ids must stay a comma, and the query order is `userIds`, `size`, `format`, `isCircular`.

- [ ] **Step 10: Commit**

```bash
git add src/Source/IconClient.cs src/Source/AvatarBook.cs src/Board/PanelModels.cs src/Board/AccountsTableModel.cs src/Composition/AppServices.cs src/Composition/ISetupServices.cs src/UI/Setup/AccountsModel.cs src/UI/Setup/AccountsPage.xaml src/UI/Setup/AccountsPage.xaml.cs src/UI/Controls/AvatarFill.cs src/App.xaml src/UI/Panels/AccountsTablePanel.xaml src/UI/Panels/MyAccountsPanel.xaml src/UI/Panels/PromotionCheckPanel.xaml src/UI/Panels/AccountCardPanel.xaml README.md tests/AvatarBookTests.cs tests/AvatarFenceTests.cs tests/IconClientTests.cs tests/PanelModelsTests.cs tests/AccountsModelTests.cs
git commit -m "accounts: your own accounts show their Roblox picture, and no one else's is ever asked for

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Step 11: The live look (controller)**

Quit Ur Score, then from the repo root:

```bash
dotnet build Ur-Score.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build
```

Start RoRoRo 1.28 with your accounts listed. Run these walks one at a time, unchanged (no script in `tools/smoke`
changed, and no automation id moved), from a shell where `UR_SCORE_RULES_FILE` is not set, and record every result:

```powershell
powershell -ExecutionPolicy Bypass -File tools/smoke/window-smoke.ps1 -Main CCGP
powershell -ExecutionPolicy Bypass -File tools/smoke/walk-alts.ps1
powershell -ExecutionPolicy Bypass -File tools/smoke/walk-starter-board.ps1 -Main CCGP -Alt K0i2
powershell -ExecutionPolicy Bypass -File tools/smoke/walk-pop-outs.ps1 -Main CCGP
powershell -ExecutionPolicy Bypass -File tools/smoke/walk-alerts.ps1
powershell -ExecutionPolicy Bypass -File tools/smoke/check-book-privacy.ps1
powershell -ExecutionPolicy Bypass -File tools/smoke/check-boards-privacy.ps1
```

Expected: every step passes and both privacy checks exit 0. A failure here goes back to its step above, before the
release run.

Then, by hand on your own data, with Ur Score started and read at least once:

1. **The Alts tab.** Every account in the accounts table has a round picture beside its name; the totals row has none and
   "Total" sits exactly where a name does. My accounts and Promotion check show the same picture beside the same names.
   Pick a row: the account card's header shows that account's picture at the larger size.
2. **Nobody else.** The Live leaderboard and Top of the battle show names only — no picture on any row, including your
   own.
3. **No jump, no error.** Watch a row while the next read lands: nothing moves. Then quit, delete
   `%LOCALAPPDATA%\626labs.ur-score\icon-cache`, turn the network off, and start again: rows draw at once with names, no
   picture and no error line anywhere, and Diagnostics' trail has at most one `AVATARS:` line.
4. **One fetch, every window.** With the network back, pop the accounts table out: the same pictures, and
   `Get-ChildItem $env:LOCALAPPDATA\626labs.ur-score\icon-cache avatar-*.png | Select-Object Name, LastWriteTime` is
   unchanged by opening the pop-out or Setup › Your accounts.
5. **Only yours on disk.** Every `avatar-<id>.png` in that folder is one of your own accounts: list them and compare the
   ids against RoRoRo's Accounts page. Nothing prunes this folder (review Minor 4), so an account you have since removed
   from RoRoRo leaves its picture behind until the seven-day refresh ages it out. So the check is that no id in the
   folder is a stranger's — not that the count matches. An id you don't recognise at all is the failure.
6. **Setup › Your accounts.** A picture beside each row, and the standing line about Roblox's picture service is on
   screen.
7. **Both themes.** Take four screenshots, switching RoRoRo's theme (Settings › Appearance) between the pairs and
   waiting for Ur Score to follow:

```powershell
powershell -ExecutionPolicy Bypass -File tools/smoke/shot.ps1 -Title 'Ur Score' -OutPath artifacts\smoke\avatars-board-dark.png
powershell -ExecutionPolicy Bypass -File tools/smoke/shot.ps1 -Title 'Setup' -OutPath artifacts\smoke\avatars-setup-dark.png
powershell -ExecutionPolicy Bypass -File tools/smoke/shot.ps1 -Title 'Ur Score' -OutPath artifacts\smoke\avatars-board-light.png
powershell -ExecutionPolicy Bypass -File tools/smoke/shot.ps1 -Title 'Setup' -OutPath artifacts\smoke\avatars-setup-light.png
```

   Each board shot must show: round pictures beside your accounts in the table, My accounts and the account card; the
   leaderboard with names and no pictures; names aligned in one column whether or not a row has a picture. Each pair must
   show the ring around a picture following the theme (it is `DividerBrush`), and no colour that stayed put across the
   switch.

---

> **Added 2026-09-15, later the same day.** The owner approved three more pieces for 0.3.2 after Task 6 landed at
> `ddfafc8`, in his words: *"option in settings, so once a user sets up they can enable it to start from open"*,
> *"last numbers it saw"*, and *"the plugin should tell them"*. Like Task 6 they sit below the release run rather
> than renumbering the tasks above, and like Task 6 they are built **before** it. **Execution order: Task 7 → Task 8
> → Task 9 → Task 5 (the release run), release last.** Task 9 uses `LiveBoard.LiveOf` and `PanelText.CannotRead`
> shaped in Task 8, so the order is load-bearing between 8 and 9. Each task lists its own files and names below; the
> file table and the interface contract near the top of this plan still cover Tasks 1 to 4 only.

### Task 7: Start reading when Ur Score opens

**Files:**
- Modify: `src/Core/Settings.cs`, `src/Composition/ISetupServices.cs`, `src/Composition/AppServices.cs`,
  `src/UI/BoardButtons.cs`, `src/UI/BoardWindow.xaml.cs`, `src/UI/Setup/RecipesPage.xaml`,
  `src/UI/Setup/RecipesPage.xaml.cs`, `README.md`
- Test: `tests/SettingsTests.cs` (added to), `tests/BoardButtonsTests.cs` (added to)
- Never touched: `tools/smoke` (A47), `manifest.json` (its `autostartDefault` is RoRoRo's switch, not this one — A33)

**Interfaces:**
- Consumes: `Settings.Load/Save/Defaults`, `BoardButtons.For`, `SetupPages.FirstRunPage`, `AppServices.StartAsync`,
  `AppServices.AddTrail`, `AppServices.RaiseChanged`, `Redactor.Redact`, the App.xaml styles `SectionLabel`, `Muted`
  and `Refusal`.
- Produces:

```csharp
// ---- src/Core/Settings.cs ----
public sealed record Settings(bool ResolveNames = true, string? ActiveRecipe = null, bool StartOnOpen = false)

// ---- src/Composition/ISetupServices.cs (AppServices implements it) ----
void SaveSettings(Settings settings);          // writes settings.json, then raises Changed; throws when it can't be written

// ---- src/UI/BoardButtons.cs ----
public static bool StartsOnOpen(bool startOnOpen, bool loaded, bool running, int installed, bool anySourceOn, bool firstRunPage);
```

**Rulings made while planning**

Recorded so a reviewer doesn't read them as drift. Each names what was decided, why, and the cost if it is wrong.
They continue the plan's A-numbering.

- **A31. It is a `Settings` key, not a `RecipeState` one, and it is off until you turn it on.** Start reads every
  enabled source at once, so nothing about it belongs to one recipe: it goes on the `Settings` record in
  `src/Core/Settings.cs` as `StartOnOpen`, third positional parameter, defaulting to `false`. A file written by 0.3.1
  loads with it off, which is the same as today's behaviour. Neither Task 8 nor Task 9 adds a setting at all — one is
  what the window does with what it already kept, the other is a reading of `Latest` — so `StartOnOpen` is the only
  new key in 0.3.2. *Why:* the owner was explicit that this is something a user enables after setting up, not a
  behaviour change everyone gets; an opt-in that defaults on is a different feature. *Cost if wrong:* someone who
  wants it on has to tick one box once.

  Superseded by BC1, 2026-09-23: reading now starts on open by default, on new and existing installs alike
  (`docs/2026-09-23-board-chrome-design.md` §1, §3.6).
- **A32. The control lives on Setup › Recipes, and settings get a save path.** A checkbox, "Start reading as soon as
  Ur Score opens", in a `WHEN UR SCORE OPENS` section at the bottom of `RecipesPage`, under the Import recipe… row.
  `ISetupServices` gains `SaveSettings`, `AppServices.Settings` gains a private setter, and no page edits
  `settings.json` by hand. *Why:* the standing rule is that a thing the app can do for you must not need a file
  edited, and that is what `84d4c19` fixed in the README this morning — so this setting ships with its control in the
  same commit as the key. Among the pages that are always listed (Your accounts, Stats, Recipes, Alerts, Score book,
  Diagnostics), Recipes is the one about what Ur Score reads, and reading is running the installed recipes; a Clans
  page would have been the other candidate and is disqualified because those pages exist only for a recipe with
  inputs, so with only the profile recipe installed there would be nowhere to put it. Rejected: Score book (it is
  about what is kept, not about reading), Diagnostics (a read-out). *Cost if wrong:* the box is one page away from
  where someone looks for it; moving it later is a XAML block and a handler.
- **A33. What "from open" means, exactly.** It is not RoRoRo's `autostartDefault` in `manifest.json` — that decides
  whether RoRoRo launches the plugin, and it stays `off`. Ur Score still reads only while its own window is open.
  When the box is on, the board does once, as it opens, exactly what pressing Start does: the same `StartAsync`, the
  same `_starting` flag, the same failure line. It fires after the score book has loaded, and never when pressing
  Start yourself would have done nothing — nothing installed, no enabled source, already running, or Setup has just
  opened on a recipe that has no source yet (spec §7.1). The decision is pure, in `BoardButtons.StartsOnOpen`, so a
  test sees every branch; the window only obeys it. A trail line says it happened. *Cost if wrong:* a first run with
  the box already on reads nothing until you press Start, which is what happens today anyway.
- **A34. A failed save is said on the page, and the box goes back to what is saved.** No message box (Global
  Constraints). The refusal is a `Refusal`-styled line under the checkbox, redacted; the box is then re-read from
  `_services.Settings`, so it never shows something the file doesn't. The handler is `Checked`/`Unchecked`, not
  `Click`, because a UI Automation toggle changes `IsChecked` without a click, and a `_settingBox` guard keeps
  `Refresh` from writing the file back. *Cost if wrong:* a disk that refuses the write leaves one visible line and a
  box that tells the truth.
- **A35. The README is patched in this task, not in the release.** Two places say the wrong thing the moment this
  lands: the "Run itself in the background" bullet ("Autostart defaults to off … it won't watch a battle you forgot
  to start it for") and the settings reference, which says the file holds two keys. Both are edited here, in the same
  commit as the behaviour. *Why:* `84d4c19`'s whole lesson was a README describing a window that had moved on.
  *Cost if wrong:* one bullet and one table row.
- **A36. It ships inside 0.3.2.** Task 5's `CHANGELOG.md` **Added** list gains exactly this line: "Setup › Recipes
  has a checkbox: start reading as soon as Ur Score opens. It is off until you turn it on, it does exactly what
  pressing Start does, and Ur Score still reads only while its window is open." *Cost if wrong:* one line in one
  release note.

- [ ] **Step 1: Write the failing tests**

Add to `tests/SettingsTests.cs`, after `AnEmptyObjectYieldsDefaults`:

```csharp
    [Fact]
    public void StartingFromOpenIsOffUntilYouTurnItOn()
    {
        // The owner's words: something a user enables once they are set up, never a behaviour change everyone gets.
        Assert.False(Settings.Defaults.StartOnOpen);

        Write("""{ "resolveNames": true, "activeRecipe": "pet-sim-99-profile" }""");
        Assert.False(Settings.Load(File()).StartOnOpen);
    }

    [Fact]
    public void StartOnOpenRoundTripsUnderItsCamelCaseKeyWithoutDisturbingTheOthers()
    {
        Settings.Save(new Settings(ResolveNames: false, ActiveRecipe: "x") with { StartOnOpen = true }, File());
        var json = System.IO.File.ReadAllText(File());

        Assert.Contains("\"startOnOpen\"", json);
        Assert.DoesNotContain("\"StartOnOpen\"", json);
        Assert.Equal(new Settings(false, "x", true), Settings.Load(File()));
    }
```

Add to `tests/BoardButtonsTests.cs`, at the end of the class:

```csharp
    /// <summary>Plan A33: the board starts itself only when pressing Start yourself would have done something.</summary>
    [Theory]
    [InlineData(true, true, false, 1, true, false, true)]     // the one case that reads
    [InlineData(false, true, false, 1, true, false, false)]   // off, which is the default
    [InlineData(true, false, false, 1, true, false, false)]   // the score book hasn't been read yet
    [InlineData(true, true, true, 1, true, false, false)]     // already running
    [InlineData(true, true, false, 0, true, false, false)]    // nothing installed
    [InlineData(true, true, false, 1, false, false, false)]   // every source switched off
    [InlineData(true, true, false, 1, true, true, false)]     // Setup just opened on a recipe with no source
    public void StartingFromOpenNeedsSomethingToRead(
        bool startOnOpen, bool loaded, bool running, int installed, bool anySourceOn, bool firstRunPage, bool starts) =>
        Assert.Equal(starts, BoardButtons.StartsOnOpen(startOnOpen, loaded, running, installed, anySourceOn, firstRunPage));

    [Fact]
    public void StartingFromOpenAgreesWithTheStartButton()
    {
        // Two gates that can drift apart is how a board starts itself while the button that does the same thing is
        // disabled. This one is the button's, plus the reasons a press would have been a no-op.
        Assert.False(BoardButtons.For(loaded: false, running: false, starting: false, testing: false, importing: false).StartStop);
        Assert.False(BoardButtons.StartsOnOpen(startOnOpen: true, loaded: false, running: false, installed: 1, anySourceOn: true, firstRunPage: false));
    }
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`
Expected: FAIL to compile (`Settings.StartOnOpen` and `BoardButtons.StartsOnOpen` don't exist).

- [ ] **Step 3: The setting**

In `src/Core/Settings.cs`, replace the record's declaration line with:

```csharp
public sealed record Settings(bool ResolveNames = true, string? ActiveRecipe = null, bool StartOnOpen = false)
```

and add this paragraph to the class summary, directly above `/// </summary>`:

```csharp
/// <para>
/// <c>StartOnOpen</c> is off until you turn it on, in Setup › Recipes (plan A31). It is about this app's own window —
/// whether the board does what pressing Start does as it opens — and not about RoRoRo launching the plugin, which is
/// the manifest's <c>autostartDefault</c> and stays off.
/// </para>
```

- [ ] **Step 4: The save path**

In `src/Composition/ISetupServices.cs`, add directly after `void SaveSources(IReadOnlyList<Source> sources);`:

```csharp
    /// <summary>
    /// Writes <c>settings.json</c> and raises <see cref="Changed"/>. Throws when it can't be written, and nothing
    /// changes then; no page edits that file itself.
    /// </summary>
    void SaveSettings(Settings settings);
```

In `src/Composition/AppServices.cs`:

1. Replace `    public Settings Settings { get; }` with:

```csharp
    public Settings Settings { get; private set; }
```

2. Add directly after the `SaveSources` method:

```csharp
    /// <summary>
    /// Writes <c>settings.json</c> and redraws. Nothing running changes: the only key a page writes is read when the
    /// board next opens (plan A33). Qualified as <c>Core.Settings</c> because the property beside it has that name.
    /// </summary>
    public void SaveSettings(Settings settings)
    {
        Core.Settings.Save(settings);
        Settings = settings;
        RaiseChanged();
    }
```

- [ ] **Step 5: The decision**

In `src/UI/BoardButtons.cs`, add inside `BoardButtons`, after `For`:

```csharp
    /// <summary>
    /// Whether the board starts reading by itself as it opens (plan A33). Off unless you turned it on, and then only
    /// when pressing Start yourself would have done something: the book is read, nothing runs yet, a recipe is
    /// installed, a source is on, and Setup isn't about to open on a recipe that has no source.
    /// <para>
    /// It ends with the Start button's own gate rather than restating it, so the two can't drift apart and leave the
    /// board starting itself while the button for the same thing is disabled.
    /// </para>
    /// </summary>
    public static bool StartsOnOpen(bool startOnOpen, bool loaded, bool running, int installed, bool anySourceOn, bool firstRunPage) =>
        startOnOpen && !running && installed > 0 && anySourceOn && !firstRunPage
        && For(loaded, running, starting: false, testing: false, importing: false).StartStop;
```

- [ ] **Step 6: The board starts itself**

In `src/UI/BoardWindow.xaml.cs`:

1. In `OnStartStopClick`, replace everything from `if (_services.Installed.Count == 0)` to the end of the method with:

```csharp
        await StartReadingAsync();
    }

    /// <summary>
    /// Start, from the button or from opening (plan A33): the same gate above it, the same in-flight flag, the same
    /// failure line. One path, so "from open" can never become a second, subtly different way to start.
    /// </summary>
    private async Task StartReadingAsync()
    {
        if (_services.Installed.Count == 0)
        {
            StateLine.Text = "No recipe to run.";
            DetailLine.Text = "Import a recipe first.";
            return;
        }

        _starting = true;
        ApplyButtons();

        try
        {
            await _services.StartAsync();
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
        finally
        {
            _starting = false;
            RenderLines();
        }
    }
```

2. In `OnLoaded`, replace the last two lines (the `// Spec §7.1` comment and the `if (SetupPages.FirstRunPage…)` line) with:

```csharp
        // Spec §7.1: a recipe with inputs and no sources opens Setup on its Clans page.
        var firstRun = SetupPages.FirstRunPage(_services.Installed, _services.Sources);
        if (firstRun is not null) OpenSetup(firstRun);

        // Plan A33: with "Start reading as soon as Ur Score opens" ticked, the board does once what pressing Start
        // does — after the book is read, and never while Setup has just opened on a recipe that has no source yet.
        if (!BoardButtons.StartsOnOpen(
                _services.Settings.StartOnOpen, _services.ReaderLoaded, _services.Running,
                _services.Installed.Count, _services.Sources.Any(s => s.Enabled), firstRun is not null))
        {
            return;
        }

        _services.AddTrail("START ON OPEN: reading started because Setup > Recipes has it ticked.");
        await StartReadingAsync();
```

The trail line is ASCII (`>`, not `›`): it goes into Diagnostics' Copy text, which is read in a terminal.

- [ ] **Step 7: The control**

In `src/UI/Setup/RecipesPage.xaml`, insert directly before the final `</StackPanel>`:

```xml
        <TextBlock Text="WHEN UR SCORE OPENS" Style="{StaticResource SectionLabel}" Margin="0,22,0,6" />
        <!-- Checked and Unchecked, not Click: a UI Automation toggle changes IsChecked without a click. -->
        <CheckBox x:Name="StartOnOpenBox" Content="Start reading as soon as Ur Score opens"
                  Checked="OnStartOnOpenChanged" Unchecked="OnStartOnOpenChanged"
                  AutomationProperties.Name="Start reading as soon as Ur Score opens" />
        <TextBlock Style="{StaticResource Muted}" Margin="24,4,0,0"
                   Text="Off unless you turn it on. Ticking it doesn't start reading now; it takes effect the next time you open Ur Score. Ur Score reads only while its window is open, and Stop still stops it." />
        <TextBlock x:Name="StartOnOpenProblemLine" Style="{StaticResource Refusal}" Margin="24,4,0,0" Visibility="Collapsed" />
```

In `src/UI/Setup/RecipesPage.xaml.cs`:

1. Add the field after `private bool _importing;`:

```csharp
    /// <summary>The box is being set from the saved settings, not by a click, so the handler doesn't write them back.</summary>
    private bool _settingBox;
```

2. Add at the end of `Refresh`:

```csharp
        _settingBox = true;
        StartOnOpenBox.IsChecked = _services.Settings.StartOnOpen;
        _settingBox = false;
```

3. Add the handler, after `OnImportClick`:

```csharp
    /// <summary>
    /// The one app-wide setting with a control (plan A32). A write that fails is said here and the box goes back to
    /// what is saved, so it never shows something the file doesn't — and no message box, ever (Global Constraints).
    /// </summary>
    private void OnStartOnOpenChanged(object sender, RoutedEventArgs e)
    {
        if (_settingBox) return;

        try
        {
            _services.SaveSettings(_services.Settings with { StartOnOpen = StartOnOpenBox.IsChecked == true });
            Show(StartOnOpenProblemLine, "");
        }
        catch (Exception ex)
        {
            Show(StartOnOpenProblemLine, _services.Redactor.Redact($"Could not save that: {ex.Message}"));
            _settingBox = true;
            StartOnOpenBox.IsChecked = _services.Settings.StartOnOpen;
            _settingBox = false;
        }
    }
```

- [ ] **Step 8: The README (A35)**

In `README.md`:

- Replace the bullet that begins `- **Run itself in the background.** Autostart defaults to off.` (through "…it won't
  watch a battle you forgot to start it for.") with:

```
- **Run itself in the background.** RoRoRo's autostart for this plugin is off by default, and Ur Score reads only
  while its own window is open. Once you're set up you can tick **Start reading as soon as Ur Score opens** in
  **Setup → Recipes** so you don't have to press Start; it still won't watch a battle you never opened it for.
```

- In *Settings reference*, change `is two keys, and only the first is` to `is three keys, and only the first is`, and
  add this row to the end of the table:

```
| `startOnOpen` | `false` | Whether Ur Score starts reading as soon as its window opens. Ticked in **Setup → Recipes**; no reason to edit it by hand. |
```

- [ ] **Step 9: Run the tests, then the build gate**

Run, from the repo root, with Ur Score closed (a running copy locks `bin\Release`):

```
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~SettingsTests|FullyQualifiedName~BoardButtonsTests"
dotnet build Ur-Score.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build
```

Expected: all four pass. The app build is run on its own because the new `CheckBox` and its two handlers only fail
there. If `SavedJsonUsesCamelCaseKeys` or `RoundTripsThroughDisk` breaks, the third positional parameter went in the
wrong place: `StartOnOpen` is last, after `ActiveRecipe`.

- [ ] **Step 10: Commit**

```bash
git add src/Core/Settings.cs src/Composition/ISetupServices.cs src/Composition/AppServices.cs src/UI/BoardButtons.cs src/UI/BoardWindow.xaml.cs src/UI/Setup/RecipesPage.xaml src/UI/Setup/RecipesPage.xaml.cs README.md tests/SettingsTests.cs tests/BoardButtonsTests.cs
git commit -m "settings: Ur Score can start reading as soon as it opens, off until you turn it on

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

The live look for this task is **Task 8 Step 9**, which opens the window twice anyway; doing it here would mean a
third restart for one checkbox.

---

### Task 8: The window opens on the last numbers it saw

**Files:**
- Modify: `src/Book/ScoreBookReader.cs`, `src/Core/RecipeWatch.cs` (the `RecipeSnapshot` record only),
  `src/Board/PanelModels.cs`, `src/UI/Panels/PanelFrame.xaml`, `src/UI/BoardText.cs`,
  `src/Composition/AppServices.cs`
- Create: `src/Book/Remembered.cs`
- Test: `tests/RememberedTests.cs` (new), `tests/BoardFixtures.cs` (added to), `tests/ScoreBookReaderTests.cs`
  (added to), `tests/PanelModelsTests.cs` (added to), `tests/BoardTextTests.cs` (added to)
- Never touched: `tools/smoke` (A47), `README.md` (A42)

**Interfaces:**
- Consumes: `BookLine`, `BookAccount`, `BookPeriod`, `BookLine.KindRead`, `ScoreBookReader`'s `_gate`/`_slugs`,
  `Source.KeyOf`, `Source.InputsKey`, `RecipeStats.Find`, `RecipeRow`, `HeadlineValue`, `ReadingPeriod`,
  `LiveBoard.UserIdsOf`, `StatText.Span`, `BoardFixtures.Read/Live/Snapshot`.
- Produces:

```csharp
// ---- src/Core/RecipeWatch.cs (on RecipeSnapshot) ----
public DateTimeOffset? RememberedAt { get; init; }   // non-null: every number here came from the score book, at that time

// ---- src/Book/ScoreBookReader.cs ----
public BookLine? LastReading(string sourceId);        // the newest kept reading for one source, else null

// ---- src/Book/Remembered.cs (new, Labs626.UrScore.Book) ----
public static class Remembered
{
    public static IReadOnlyDictionary<string, RecipeSnapshot> ForSources(
        ScoreBookReader reader, IReadOnlyList<Source> sources, IReadOnlyList<InstalledRecipe> installed, IReadOnlySet<long> yourUserIds);
    public static RecipeSnapshot? From(BookLine line, Source source, Recipe recipe, IReadOnlySet<long> yourUserIds);
}

// ---- src/Board/PanelModels.cs ----
// LiveBoard gains a trailing  IReadOnlyDictionary<string, RecipeSnapshot>? Remembered = null  and:
public RecipeSnapshot? LiveOf(string sourceId);       // the live reading alone
public bool IsRemembered(string sourceId);
public DateTimeOffset? OldestRemembered { get; }
// SnapshotOf falls back to Remembered; PanelHead gains a trailing  bool Remembered = false

// ---- src/UI/BoardText.cs ----
public static string RememberedLine(DateTimeOffset oldest, DateTimeOffset now);
```

**Rulings made while planning**

- **A37. They come out of the score book, not a new store.** `StatHistory` is the window's in-memory memory of the
  previous read and is documented "never saved"; `WatchState` is a state, not numbers; `boards.json` holds layout.
  The one thing already on disk that holds numbers is the score book, which `ScoreBookReader` loads at start anyway
  (the last 35 days of readings, plus every final). So the last numbers are `reader.LastReading(sourceId)` turned
  back into a `RecipeSnapshot` by a new `Remembered`, the reverse of `LineBuilder`. Nothing new is written, no new
  file, no new format. *Why:* a second store would be a second thing to keep true, and the book is already loaded
  before the first draw. *Cost if wrong:* a source whose newest kept reading is older than `ScoreBookReader.KeepReadings`
  shows nothing, exactly as today.
- **A38. Remembered snapshots live in their own map and reach the panels only.** `AppServices` keeps them in
  `_remembered`, never in `_latest`, and hands them to `LiveBoard` as its own parameter. `LiveBoard.SnapshotOf` — the
  panels' door — returns the live reading if there is one and the remembered one otherwise; `LiveBoard.LiveOf`
  returns the live one alone. Everything that reports what Ur Score is *doing* keeps reading the live map:
  `ISetupServices.Latest` (so Setup › Your accounts, Setup › Stats, `AccountsModel.FoundIn` and Diagnostics are
  untouched), `BoardText.DetailLine`'s RoRoRo-is-down check, and `BoardText.StateLine`'s hunt for a source in
  trouble, which moves from `SnapshotOf` to `LiveOf` in this task. *Why:* the alternative — seeding `_latest` — makes
  every existing consumer a place where a stale number can be mistaken for a current one, and there is no fence that
  catches the next consumer someone adds. One map, one door. *Cost if wrong:* Diagnostics says "Waiting for its first
  read" and "Last read never" for a source whose numbers are on screen, which is true.
- **A39. One mark, `RememberedAt`, and what a remembered snapshot may carry.** A nullable time, not a bool plus a
  time that can disagree: non-null means every number in it came from the book, at that moment. It carries your own
  accounts' rows (the stats the recipe still offers), the source's headline, the line's period, and nothing else —
  `Accounts`, `Unresolved`, `Groups`, `Unavailable`, `StatMisses`, `CellMisses`, `CounterNames` and `Detail` all stay
  empty, because the book never kept them. Its `State` is `Showing` and is never read: nothing that looks at
  `WatchState` sees this map (A38). *Why:* the book's own privacy rules come along for free — it only ever held your
  accounts and the headline, so nothing here can put another member back on screen. *Cost if wrong:* none known.
- **A40. The three panels that show other members refuse them, and Standing stops counting.** `LiveLeaderboard`,
  `Top` and `PromotionCheck` are each documented "Live only" and each needs rows the book never kept, so all three
  switch from `SnapshotOf` to `LiveOf` and keep the empty state they already have ("Waiting for the first read.").
  `Standing`'s "5 of 50 accounts" count is computed from the snapshot's rows, which for a remembered snapshot is your
  own accounts alone — so `hasAccounts` also requires a live reading. `PastPeriods` and `RecordsPanel` change not at
  all: they only ever read the book, and already say so. *Why:* a leaderboard that looks current while missing
  everyone but you is the worst possible version of this feature. *Cost if wrong:* three panels stay blank until the
  first read of the session, which is what they do today.
- **A41. Two marks, and neither is magenta.** Board-wide: `BoardText.StateLine` gains one sentence — "The numbers on
  screen are the last ones Ur Score read, from 3 h ago." — built from the *oldest* remembered reading among enabled
  sources, so the line can never claim the numbers are fresher than the oldest one on screen. It is in the top bar,
  above the scroll, always visible, and it needs nothing per panel, so it cannot be forgotten. Per panel: `PanelHead`
  gains `Remembered`, and the shared `PanelFrame` draws the word `remembered` beside the title in `MutedTextBrush` —
  never magenta, which is `overdue`, a read that should have happened and hasn't. Six panels pass it (Standing, Race,
  My accounts, Account card, Profile stat, Accounts table) and a test asserts each one. *Cost if wrong:* a panel
  someone adds later defaults to no mark; the board-wide line still says it.
- **A42. What makes a kept reading refuse to come back.** `Remembered.From` returns null unless the line is a
  reading (not a final), its recipe slug is this source's recipe, and `Source.KeyOf(line.Inputs)` equals the source's
  `InputsKey` — the same clan, the same inputs. Within a line, an account id RoRoRo isn't listing as yours right now
  is dropped, and a stat the recipe no longer offers is dropped. The line's **recipe hash is deliberately not
  checked**: a recipe update doesn't make the number it read yesterday untrue, and the per-stat filter already
  removes anything the new recipe can't name. Nothing about this touches the README: no new file, no new
  destination, and the score book section already says every read is kept. *Cost if wrong:* a recipe that changed
  what a stat *means* under the same id shows yesterday's number under today's label for one read.
- **A43. It ships inside 0.3.2.** Task 5's `CHANGELOG.md` **Added** list gains exactly this line: "Ur Score opens on
  the last numbers it read instead of an empty board. Each panel drawing them says `remembered`, the state line says
  how old they are, and the first read replaces them. The panels that show other members wait for a real read,
  because the score book never keeps anyone else." *Cost if wrong:* one line in one release note.

- [ ] **Step 1: Write the failing tests**

Create `tests/RememberedTests.cs`:

```csharp
using Labs626.UrScore.Board;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

using Source = Labs626.UrScore.Core.Source;

/// <summary>
/// The numbers the window shows before its first read. These pin what a remembered snapshot may carry (your own
/// accounts and the source's own headline, nothing else), what makes one refuse to be built, and that every one is
/// stamped — the stamp is the only thing downstream has to tell it from a read.
/// </summary>
public class RememberedTests
{
    private static readonly Source MainClan = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);

    private static readonly IReadOnlySet<long> Yours = LiveBoard.UserIdsOf(Accounts);

    private static BookLine Kept(DateTimeOffset at, params (long UserId, double Value)[] rows) =>
        Read(MainClan, at, "AutumnBattle", new Dictionary<string, double> { ["clan-points"] = 14_020_550 }, "value", rows);

    [Fact]
    public void TheLastKeptReadingComesBackStampedWithItsOwnTime()
    {
        var snapshot = Remembered.From(Kept(Now.AddHours(-3), (Main.RobloxUserId, 4200)), MainClan, Clan, Yours);

        Assert.NotNull(snapshot);
        Assert.Equal(Now.AddHours(-3), snapshot.RememberedAt);
        Assert.Equal(MainClan.Id, snapshot.SourceId);
        Assert.Equal("AutumnBattle", snapshot.Period?.Value);
        Assert.Equal(new RecipeRow(Main.RobloxUserId, new Dictionary<string, double> { ["value"] = 4200 }), Assert.Single(snapshot.Rows!));
        Assert.Equal(14_020_550, Assert.Single(snapshot.Headline!).Number);
    }

    [Fact]
    public void ItCarriesNothingTheBookNeverKept()
    {
        // The book's own privacy rules come along for free, and only for free if nothing here invents a field.
        var snapshot = Remembered.From(Kept(Now.AddMinutes(-20), (Main.RobloxUserId, 4200)), MainClan, Clan, Yours)!;

        Assert.Empty(snapshot.Accounts);
        Assert.Empty(snapshot.Unresolved);
        Assert.Empty(snapshot.Groups);
        Assert.Empty(snapshot.Unavailable);
        Assert.Empty(snapshot.CellMisses);
        Assert.Empty(snapshot.CounterNames);
        Assert.Null(snapshot.Detail);
        Assert.False(snapshot.Recorded);
    }

    [Fact]
    public void AnAccountThatIsNoLongerYoursDoesNotComeBack()
    {
        var line = Kept(Now.AddHours(-1), (Main.RobloxUserId, 4200), (AltOne.RobloxUserId, 900));

        var snapshot = Remembered.From(line, MainClan, Clan, new HashSet<long> { Main.RobloxUserId })!;

        Assert.Equal([Main.RobloxUserId], snapshot.Rows!.Select(r => r.UserId).ToArray());
    }

    [Fact]
    public void AStatTheRecipeNoLongerOffersIsLeftOutAndAnEmptyRowWithIt()
    {
        var line = Read(MainClan, Now.AddHours(-1), "AutumnBattle", null, "gone-from-the-recipe", (Main.RobloxUserId, 7));

        Assert.Null(Remembered.From(line, MainClan, Clan, Yours));
    }

    [Fact]
    public void AReadingOfOtherInputsIsNotThisSourcesNumbers()
    {
        // The same recipe, another clan: its total and its rows are about something else entirely.
        var otherClan = SourceOf(MainClan.Id, Clan, "K0i2", SourceRole.Main);
        var line = Kept(Now.AddHours(-1), (Main.RobloxUserId, 4200));

        Assert.Null(Remembered.From(line, otherClan, Clan, Yours));
        Assert.Null(Remembered.From(line, SourceOf(MainClan.Id, Profile, null, SourceRole.Mine), Profile, Yours));
    }

    [Fact]
    public void AFinalIsNotAReading()
    {
        var final = Final(MainClan, Now.AddDays(-9), "SpringBattle", new Dictionary<string, double> { ["clan-points"] = 1 }, "value", (Main.RobloxUserId, 4200));

        Assert.Null(Remembered.From(final, MainClan, Clan, Yours));
    }

    [Fact]
    public void EverySourceWithSomethingKeptGetsOneAndASwitchedOffSourceGetsNone()
    {
        var off = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine) with { Enabled = false };
        var reader = Reader(
            Kept(Now.AddHours(-5), (Main.RobloxUserId, 4000)),
            Kept(Now.AddHours(-2), (Main.RobloxUserId, 4200)),
            Read(off, Now.AddHours(-1), "AutumnBattle", null, "value", (AltOne.RobloxUserId, 900)));

        var map = Remembered.ForSources(reader, [MainClan, off], [Installed(Clan, "value")], Yours);

        Assert.Equal([MainClan.Id], map.Keys.ToArray());
        Assert.Equal(Now.AddHours(-2), map[MainClan.Id].RememberedAt);
    }
}
```

Add to `tests/ScoreBookReaderTests.cs`, using that file's own `Read` and `Reader` helpers (its `Read` takes the
source id last, and `Final` is always `s-1`):

```csharp
    [Fact]
    public void TheLastReadingIsTheNewestOneForThatSourceAlone()
    {
        var reader = Reader(
            Read(Now.AddHours(-4), 100),
            Read(Now.AddHours(-1), 300, source: "s-2"),
            Read(Now.AddHours(-2), 200),
            Final("B", Now, 999, 1));

        // A final is not a reading, so the 2 h old line is still the last thing s-1 read.
        Assert.Equal(Now.AddHours(-2), reader.LastReading("s-1")?.T);
        Assert.Equal(Now.AddHours(-1), reader.LastReading("s-2")?.T);
        Assert.Null(reader.LastReading("s-nothing-here"));
    }
```

Add to `tests/PanelModelsTests.cs`. That class has no shared clan `Source` (only `ProfileSource`, line 593), so
each test makes its own, and `Period` is the class's own constant for `"AutumnBattle"`:

```csharp
    /// <summary>Plan A41: a panel drawing numbers from the score book says so, in every panel that can.</summary>
    [Fact]
    public void EveryPanelThatDrawsRememberedNumbersSaysSoInItsHead()
    {
        var mainClan = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var kept = Remembered.From(
            Read(mainClan, Now.AddHours(-3), Period, Headline(99), "value", (Main.RobloxUserId, 4200)),
            mainClan, Clan, LiveBoard.UserIdsOf(Accounts))!;
        var installed = Installed(Clan, "value");
        var live = Live([mainClan], [installed], new Dictionary<string, RecipeSnapshot>(),
            remembered: new Dictionary<string, RecipeSnapshot> { [mainClan.Id] = kept });
        var reader = Reader();
        var settings = new PanelSettings(Clan.Slug, mainClan.Id, Stat: "value");

        Assert.True(PanelModels.Standing(live, reader, settings).Head.Remembered);
        Assert.True(PanelModels.Race(live, reader, settings with { SourceIds = [mainClan.Id] }).Head.Remembered);
        Assert.True(PanelModels.MyAccounts(live, reader, settings).Head.Remembered);
        Assert.True(PanelModels.AccountCard(live, reader, settings).Head.Remembered);
        Assert.True(PanelModels.ProfileStat(live, reader, settings).Head.Remembered);
        Assert.True(PanelModels.AccountsTable(live, reader, settings).Head.Remembered);

        // And the same six say nothing when the numbers were read this session.
        var read = Live([mainClan], [installed],
            new Dictionary<string, RecipeSnapshot> { [mainClan.Id] = Snapshot(mainClan.Id, [Row(Main.RobloxUserId, 4200)], [Points(99)]) });
        Assert.False(PanelModels.Standing(read, reader, settings).Head.Remembered);
        Assert.False(PanelModels.AccountsTable(read, reader, settings).Head.Remembered);
    }

    /// <summary>Plan A40: the book never kept another member, so a panel that shows them waits for a real read.</summary>
    [Fact]
    public void ThePanelsThatShowOtherMembersNeverDrawRememberedNumbers()
    {
        var mainClan = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var kept = Remembered.From(
            Read(mainClan, Now.AddHours(-3), Period, Headline(99), "value", (Main.RobloxUserId, 4200)),
            mainClan, Clan, LiveBoard.UserIdsOf(Accounts))!;
        var live = Live([mainClan], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot>(),
            remembered: new Dictionary<string, RecipeSnapshot> { [mainClan.Id] = kept });
        var settings = new PanelSettings(Clan.Slug, mainClan.Id, Stat: "value");

        Assert.Empty(PanelModels.LiveLeaderboard(live, settings, new Dictionary<long, string>()).Rows);
        Assert.Empty(PanelModels.PromotionCheck(live, settings with { ToSourceId = mainClan.Id }).Rows);
        Assert.False(PanelModels.LiveLeaderboard(live, settings, new Dictionary<long, string>()).Head.Remembered);

        // And Standing never turns your own four accounts into "4 of 4" of a clan it did not read.
        Assert.False(PanelModels.Standing(live, Reader(), settings).HasAccounts);
    }
```

Add to `tests/BoardTextTests.cs`:

```csharp
    [Fact]
    public void TheStateLineSaysTheNumbersOnScreenAreTheLastOnesItRead()
    {
        var kept = Snapshot(MainClan.Id, [Row(Main.RobloxUserId, 4200)]) with { RememberedAt = Now.AddHours(-3) };
        var live = Live([MainClan], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot>(),
            remembered: new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = kept });

        Assert.Equal("Not started. The numbers on screen are the last ones Ur Score read, from 3h ago.",
            BoardText.StateLine(live, everStarted: false));
    }

    [Fact]
    public void TheStateLineTakesTheOldestRememberedReadingSoItNeverSoundsFresherThanItIs()
    {
        var snaps = new Dictionary<string, RecipeSnapshot>
        {
            [MainClan.Id] = Snapshot(MainClan.Id, []) with { RememberedAt = Now.AddMinutes(-20) },
            [AltClan.Id] = Snapshot(AltClan.Id, []) with { RememberedAt = Now.AddDays(-2) },
        };
        var live = Live([MainClan, AltClan], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot>(), remembered: snaps);

        Assert.EndsWith("from 2d ago.", BoardText.StateLine(live, everStarted: true));
    }

    [Fact]
    public void ARememberedSnapshotIsNeverAStateUrScoreIsIn()
    {
        // Nothing in this map describes what is happening now, so the state line must not read one as a fault.
        var kept = new RecipeSnapshot(WatchState.SourceUnreachable, "timed out", [], [], 0) { SourceId = MainClan.Id, RememberedAt = Now.AddHours(-1) };
        var live = Live([MainClan], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot>(),
            running: true, remembered: new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = kept });

        Assert.StartsWith("Reading 1 source.", BoardText.StateLine(live, everStarted: true));
    }
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`
Expected: FAIL to compile (`Remembered`, `RecipeSnapshot.RememberedAt`, `ScoreBookReader.LastReading`,
`PanelHead.Remembered` and `BoardFixtures.Live`'s `remembered` parameter don't exist).

- [ ] **Step 3: The stamp and the reader**

In `src/Core/RecipeWatch.cs`, add to `RecipeSnapshot`, directly after the `Recorded` property:

```csharp
    /// <summary>
    /// When every number here came from the score book rather than a read, and when that reading was taken (plan
    /// A39). Null on everything a watch produces. It is the single mark: nothing pairs it with a flag that could
    /// disagree with it, and only <c>Remembered</c> ever sets it.
    /// </summary>
    public DateTimeOffset? RememberedAt { get; init; }
```

In `src/Book/ScoreBookReader.cs`, add after `FirstReading`:

```csharp
    /// <summary>
    /// The newest reading this source kept, or null. Finals are not readings, and a line older than
    /// <see cref="KeepReadings"/> was never loaded, so an untouched source eventually has nothing to give back.
    /// </summary>
    public BookLine? LastReading(string sourceId)
    {
        lock (_gate)
        {
            return _slugs.Values
                .SelectMany(d => d.Readings)
                .Where(l => string.Equals(l.Source, sourceId, StringComparison.Ordinal))
                .OrderBy(l => l.T)
                .LastOrDefault();
        }
    }
```

- [ ] **Step 4: The last numbers as a snapshot**

Create `src/Book/Remembered.cs`:

```csharp
using System.Globalization;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Book;

// The bare name Source would find the Labs626.UrScore.Source namespace from in here; LineBuilder writes it out in full
// for the same reason.
using Source = Labs626.UrScore.Core.Source;

/// <summary>
/// The last numbers the score book kept, as a snapshot the panels can draw before the first read of the session lands
/// (plan A37-A42). The reverse of <see cref="LineBuilder"/>.
/// <para>
/// It inherits that class's privacy rules for free: the book only ever held YOUR accounts and the source's own
/// headline, so nothing here can put another member back on screen. It also cannot invent one — every field a
/// snapshot has that the book has no answer for is left empty.
/// </para>
/// <para>
/// Never a read and never a report. These snapshots live in their own map, never in <c>_latest</c>, and reach the
/// window only through <see cref="LiveBoard.SnapshotOf"/> — so the state line, Diagnostics and every Setup page go on
/// seeing only what was actually read. Every one of them carries <see cref="RecipeSnapshot.RememberedAt"/>.
/// </para>
/// </summary>
public static class Remembered
{
    /// <summary>The last reading each enabled source kept, by source id. A source with nothing usable kept is absent.</summary>
    public static IReadOnlyDictionary<string, RecipeSnapshot> ForSources(
        ScoreBookReader reader, IReadOnlyList<Source> sources, IReadOnlyList<InstalledRecipe> installed, IReadOnlySet<long> yourUserIds)
    {
        var map = new Dictionary<string, RecipeSnapshot>(StringComparer.Ordinal);

        foreach (var source in sources.Where(s => s.Enabled))
        {
            if (installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, source.Recipe, StringComparison.Ordinal))?.Recipe is not { } recipe) continue;
            if (reader.LastReading(source.Id) is not { } line) continue;
            if (From(line, source, recipe, yourUserIds) is { } snapshot) map[source.Id] = snapshot;
        }

        return map;
    }

    /// <summary>
    /// One kept reading as a snapshot, or null when it no longer describes this source: a final rather than a
    /// reading, another recipe, other inputs, or nothing left in it that this recipe still offers (plan A42).
    /// </summary>
    public static RecipeSnapshot? From(BookLine line, Source source, Recipe recipe, IReadOnlySet<long> yourUserIds)
    {
        if (line.Kind != BookLine.KindRead) return null;
        if (!string.Equals(line.Recipe.Slug, recipe.Slug, StringComparison.Ordinal)) return null;

        // The same clan, the same inputs: a line written for another one is about something else entirely. The recipe
        // HASH is deliberately not compared — an update doesn't make yesterday's number untrue, and a stat the new
        // recipe can't name is dropped below anyway.
        if (!string.Equals(Source.KeyOf(line.Inputs), source.InputsKey, StringComparison.Ordinal)) return null;

        var rows = new List<RecipeRow>();
        foreach (var (id, account) in line.Accounts)
        {
            // An id RoRoRo isn't listing as yours right now never comes back, whatever the book holds.
            if (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var userId) || !yourUserIds.Contains(userId)) continue;

            var values = account.V
                .Where(kv => RecipeStats.Find(recipe, kv.Key) is not null && double.IsFinite(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            if (values.Count > 0) rows.Add(new RecipeRow(userId, values));
        }

        var headline = recipe.Headline
            .Where(h => h.Id.Length > 0 && line.Headline.ContainsKey(h.Id))
            .Select(h => new HeadlineValue(h.Label, line.Headline[h.Id].ToString(CultureInfo.InvariantCulture))
            {
                Id = h.Id,
                Number = line.Headline[h.Id],
            })
            .ToList();

        if (rows.Count == 0 && headline.Count == 0) return null;

        // State is never read for one of these (plan A38); Showing is the honest one of the fourteen if it ever were.
        return new RecipeSnapshot(WatchState.Showing, null, [], [], rows.Count, null, rows, headline)
        {
            SourceId = source.Id,
            Period = line.Period is { } period ? new ReadingPeriod(period.Value, period.Starts, period.Ends) : null,
            RememberedAt = line.T,
        };
    }
}
```

- [ ] **Step 5: The board reads them through one door**

In `src/Board/PanelModels.cs`:

1. Give `LiveBoard` a trailing parameter — replace `    IReadOnlyDictionary<long, string>? Avatars = null)` with:

```csharp
    IReadOnlyDictionary<long, string>? Avatars = null,
    IReadOnlyDictionary<string, RecipeSnapshot>? Remembered = null)
```

2. Replace the `SnapshotOf` member with:

```csharp
    /// <summary>
    /// What a panel draws for a source: the reading from this session, else the last one the score book kept (plan
    /// A38). A remembered one carries <see cref="RecipeSnapshot.RememberedAt"/>; a panel that needs another member's
    /// row takes <see cref="LiveOf"/> instead (plan A40).
    /// </summary>
    public RecipeSnapshot? SnapshotOf(string sourceId) =>
        Snapshots.GetValueOrDefault(sourceId) ?? Remembered?.GetValueOrDefault(sourceId);

    /// <summary>The reading from this session alone. What Ur Score is DOING is only ever answered from this one.</summary>
    public RecipeSnapshot? LiveOf(string sourceId) => Snapshots.GetValueOrDefault(sourceId);

    public bool IsRemembered(string sourceId) => SnapshotOf(sourceId)?.RememberedAt is not null;

    /// <summary>
    /// The oldest reading behind anything on screen, so a line about them never claims they are fresher than the
    /// oldest one a panel is showing. Null once every enabled source has been read this session.
    /// </summary>
    public DateTimeOffset? OldestRemembered =>
        Sources.Where(s => s.Enabled).Select(s => SnapshotOf(s.Id)?.RememberedAt).Min();
```

3. Give `PanelHead` a trailing parameter — replace `    string Title, string Subtitle = "", SourceRole? ChipRole = null, bool Overdue = false, string? Stale = null, string Note = "")`
   with:

```csharp
    string Title, string Subtitle = "", SourceRole? ChipRole = null, bool Overdue = false, string? Stale = null, string Note = "",
    bool Remembered = false)
```

4. The six panels that say it. In `Standing`, replace the `hasAccounts` line and the returned head:

```csharp
        // Your own rows are all a remembered snapshot has, so "4 of 4" would be a clan this never read (plan A40).
        var hasAccounts = source.Role != SourceRole.Watch && rows is not null && snapshot?.RememberedAt is null;
```

```csharp
            new PanelHead(title, name, source.Role, live.IsOverdue(source), Remembered: live.IsRemembered(source.Id)),
```

   In `Race`, add `var remembered = false;` beside `var overdue = false;`, add `remembered |= live.IsRemembered(source.Id);`
   directly under `overdue |= live.IsOverdue(source);`, and give the head `Remembered: remembered`.

   In `MyAccounts`, add `var remembered = false;` beside `var overdue = false;`, add
   `remembered |= live.IsRemembered(source.Id);` directly under `overdue |= live.IsOverdue(source);`, and give the
   returned head `Remembered: remembered` after its `Note:`.

   In `AccountCard`, the picked head becomes:

```csharp
            new PanelHead(title, $"{pickedAccount.DisplayName} · {live.SourceName(pickedSource)}",
                Overdue: live.IsOverdue(pickedSource), Remembered: live.IsRemembered(pickedSource.Id)),
```

   In `ProfileStat`, the returned head becomes:

```csharp
        return new ProfileStatModel(
            new PanelHead(title, stat.Label, Overdue: live.IsOverdue(source), Remembered: live.IsRemembered(source.Id)), stat.Label, MissingLast(rows));
```

   In `AccountsTable`, the returned head becomes:

```csharp
        return new AccountsTableModel(
            new PanelHead(title, live.SourceName(source), Overdue: live.IsOverdue(source), Note: note, Remembered: live.IsRemembered(source.Id)),
            columns, list);
```

5. The three that refuse them (plan A40): in `PromotionCheck` replace `live.SnapshotOf(from.Id)?.Rows` with
   `live.LiveOf(from.Id)?.Rows` and `live.SnapshotOf(to.Id)?.Rows` with `live.LiveOf(to.Id)?.Rows`; in `Top` replace
   both `live.SnapshotOf(source.Id)?.Groups` and `HeadlineNumber(live.SnapshotOf(mineSource.Id), …)` with `LiveOf`;
   in `LiveLeaderboard` replace `live.SnapshotOf(source.Id)?.Rows` with `live.LiveOf(source.Id)?.Rows`. `PastPeriods`
   and `RecordsPanel` touch no snapshot and change not at all.

- [ ] **Step 6: The two marks**

In `src/UI/BoardText.cs`, replace `StateLine` with:

```csharp
    public static string StateLine(LiveBoard live, bool everStarted)
    {
        // Plan A41: whatever else this line says, it says so while any panel is drawing numbers from the score book.
        var remembered = live.OldestRemembered is { } oldest ? " " + RememberedLine(oldest, live.Now) : "";

        if (!live.Running) return (everStarted ? "Stopped." : "Not started.") + remembered;

        var enabled = live.Sources.Where(s => s.Enabled).ToList();
        if (enabled.Count == 0) return "Running, with nothing to read yet.";

        foreach (var source in enabled)
        {
            // The reading from this session only: a remembered snapshot is not a state Ur Score is in (plan A38).
            if (live.LiveOf(source.Id) is { } snapshot && !Healthy(snapshot.State))
            {
                return $"{live.SourceName(source)}: {DiagnosticsModel.StateText(snapshot.State)}";
            }
        }

        return (enabled.Count == 1 ? "Reading 1 source." : $"Reading {enabled.Count} sources.") + remembered;
    }

    /// <summary>
    /// Plan A41: how old the numbers on screen are, from the OLDEST reading behind any of them, so the line can never
    /// sound fresher than the worst thing it covers.
    /// </summary>
    public static string RememberedLine(DateTimeOffset oldest, DateTimeOffset now) =>
        $"The numbers on screen are the last ones Ur Score read, from {StatText.Span(now - oldest)} ago.";
```

In `src/UI/Panels/PanelFrame.xaml`, add directly after the `PanelOverdue` TextBlock:

```xml
            <!-- Plan A41: this panel's numbers came from the score book, not from a read this session. Muted, never
                 magenta: magenta is "overdue", which is a read that should have happened and hasn't. -->
            <TextBlock x:Name="PanelRemembered" DockPanel.Dock="Left" Text="remembered" Margin="8,0,0,0" FontSize="11"
                       VerticalAlignment="Center" Foreground="{DynamicResource MutedTextBrush}"
                       Visibility="{Binding Remembered, Converter={StaticResource BoolToVisible}}" />
```

- [ ] **Step 7: The wiring**

In `src/Composition/AppServices.cs`:

1. Add the field after `private readonly Dictionary<string, DateTimeOffset> _lastRead = …;`:

```csharp
    /// <summary>The last numbers the score book kept, per source, until that source is read this session (plan A38).</summary>
    private IReadOnlyDictionary<string, RecipeSnapshot> _remembered = new Dictionary<string, RecipeSnapshot>(StringComparer.Ordinal);
```

2. Replace `CurrentBoard`'s last line `KnownAccounts, _time, Runner.Running, _avatars.Files);` with:

```csharp
        KnownAccounts, _time, Runner.Running, _avatars.Files, _remembered);
```

3. Add after `AskForAvatars`:

```csharp
    /// <summary>
    /// The last numbers each source kept, so the window has something real in it before the first read lands (plan
    /// A37). Read from the loaded book, never from disk again: after the book loads, when the sources change, and
    /// when RoRoRo's account list changes, since which ids are yours decides which rows come back. A reading from
    /// this session always wins (<see cref="LiveBoard.SnapshotOf"/>), so nothing here needs clearing.
    /// </summary>
    private void RememberLastNumbers()
    {
        if (!ReaderLoaded) return;

        _remembered = Remembered.ForSources(Reader, Sources, Installed, LiveBoard.UserIdsOf(KnownAccounts));
    }
```

4. Call it: in `LoadBookOnceAsync`, between `Runner.Apply(Sources);` and `AddTrail($"BOOK: loaded from {root}.");`,
   insert `RememberLastNumbers();` and change the trail line to:

```csharp
        AddTrail($"BOOK: loaded from {root}. {_remembered.Count} source(s) opened on their last kept numbers.");
```

   In `ApplySources`, insert `RememberLastNumbers();` directly above `WarnPastBudget();`. In `OnListed`, insert
   `RememberLastNumbers();` directly under `RefreshPolicies();`.

5. `Remembered` is in `Labs626.UrScore.Book`, which this file already imports; if the name collides with anything,
   alias it the way `AvatarBook` is aliased at the top of the file.

In `tests/BoardFixtures.cs`, give `Live` a trailing parameter and pass it:

```csharp
    public static LiveBoard Live(
        IReadOnlyList<Source> sources, IReadOnlyList<InstalledRecipe> installed, IReadOnlyDictionary<string, RecipeSnapshot> snapshots,
        bool running = false, IReadOnlyDictionary<string, DateTimeOffset>? lastRead = null, IReadOnlyList<HostAccount>? accounts = null,
        IReadOnlyDictionary<string, RecipeSnapshot>? remembered = null) =>
        new(sources, installed, snapshots, lastRead ?? new Dictionary<string, DateTimeOffset>(), accounts ?? Accounts, new FixedTime(Now), running,
            Remembered: remembered);
```

- [ ] **Step 8: Run the tests, then the build gate**

```
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~RememberedTests|FullyQualifiedName~PanelModelsTests|FullyQualifiedName~BoardTextTests|FullyQualifiedName~ScoreBookReaderTests"
dotnet build Ur-Score.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build
```

Expected: all four pass. If an existing `PanelModelsTests` case starts failing on a head, check it isn't asserting a
whole `PanelHead` by value — the new trailing `Remembered` changes those comparisons and the fix is to compare the
member, not the record.

- [ ] **Step 9: Commit**

```bash
git add src/Book/Remembered.cs src/Book/ScoreBookReader.cs src/Core/RecipeWatch.cs src/Board/PanelModels.cs src/UI/Panels/PanelFrame.xaml src/UI/BoardText.cs src/Composition/AppServices.cs tests/RememberedTests.cs tests/BoardFixtures.cs tests/ScoreBookReaderTests.cs tests/PanelModelsTests.cs tests/BoardTextTests.cs
git commit -m "board: the window opens on the last numbers it saw, marked as remembered

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Step 10: The live look, for Tasks 7 and 8 (controller)**

Quit Ur Score, build both projects `-warnaserror`, then start RoRoRo with your accounts listed and, on your own data:

1. **It opens on real numbers.** Start Ur Score and, before pressing anything: the accounts table, My accounts and
   the account card have numbers in them, each panel's title line reads `remembered`, and the state line reads
   "Not started." followed by "The numbers on screen are the last ones Ur Score read, from … ago." Check that age
   against Diagnostics' own last-read line for the same source.
2. **Nobody else.** The Live leaderboard and Top of the battle are empty and say "Waiting for the first read."
   Clan standing shows the place and total but no "N of M accounts" count.
3. **The first read replaces them.** Press Start (or Test now). As each source lands, its panel's `remembered` goes,
   and the sentence leaves the state line once every enabled source has been read.
4. **The setting (Task 7).** Setup › Recipes has a `WHEN UR SCORE OPENS` section with the box unticked. Tick it,
   close Setup, quit Ur Score, start it again: the board starts reading by itself, the Start button reads **Stop**,
   the live dot is on, and Diagnostics' trail carries one `START ON OPEN:` line. Untick it, restart, and the button
   reads **Start** with nothing running.
5. **Two screenshots, one per theme**, of the board in its remembered state (quit before the first read lands, or
   read the state line before pressing Start):

```powershell
powershell -ExecutionPolicy Bypass -File tools/smoke/shot.ps1 -Title 'Ur Score' -OutPath artifacts\smoke\remembered-dark.png
powershell -ExecutionPolicy Bypass -File tools/smoke/shot.ps1 -Title 'Ur Score' -OutPath artifacts\smoke\remembered-light.png
```

   Each must show the word `remembered` reading as muted text, clearly not the magenta `overdue` mark, and the state
   line's sentence in full.

6. **The walks still pass, unchanged** (A47): run `window-smoke.ps1 -Main CCGP`, `walk-alts.ps1`,
   `walk-starter-board.ps1 -Main CCGP -Alt K0i2` and `walk-alerts.ps1`. A walk that now fails at a line it used to
   read is the state line's new sentence, and that is a walk to fix in its own commit, not a reason to drop the
   sentence.

---

### Task 9: The reason an account's numbers are empty, beside the empty numbers

**Files:**
- Modify: `src/Board/PanelText.cs`, `src/Board/PanelModels.cs` (`AccountLineModel` and `MyAccounts`),
  `src/UI/Panels/MyAccountsPanel.xaml`, `src/UI/Setup/AccountsModel.cs`, `src/UI/Setup/AccountsPage.xaml`
- Test: `tests/PanelTextTests.cs` (added to), `tests/PanelModelsTests.cs` (added to),
  `tests/AccountsModelTests.cs` (added to)
- Never touched: `src/UI/BoardText.cs`'s `Attribution` and the board's `AttributionLine` (A45), `tools/smoke` (A47)

**Interfaces:**
- Consumes: `RecipeSnapshot.Unavailable` (filled by `RecipeEngine` from the recipe's own `unavailable.message`, and
  from the same message on a 404), `Recipe.Name`, `SourceRole`, `ISetupServices.Latest`.
- Produces:

```csharp
// ---- src/Board/PanelText.cs ----
public static string CannotRead(
    long userId, IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources,
    IReadOnlyDictionary<string, RecipeSnapshot> latest, bool nameTheRecipe);

// ---- src/Board/PanelModels.cs ----
// AccountLineModel gains a trailing  string Note = ""  with  public bool HasNote => Note.Length > 0;

// ---- src/UI/Setup/AccountsModel.cs ----
// AccountRow gains a trailing  string Note = ""  with  public bool HasNote => Note.Length > 0;
```

**Rulings made while planning**

- **A44. The message already exists; this task is about where it lands.** The board's footer already carries the
  profile recipe's credit, and its second sentence is "Each account must be linked on db.biggames.io with its
  Profile view public." — so the plugin is not silent, it is just saying it in an 11 px line at the bottom of the
  window, nowhere near the dashes it explains. The recipe also carries a second, sharper sentence for exactly this
  case, `unavailable.message`: "Profile is private. Link this account on db.biggames.io and turn on its Profile
  view." `RecipeEngine` already puts it in `RecipeSnapshot.Unavailable` per account, both when the source says the
  profile is unavailable and on a 404. **No new sentence is written in this task.** It routes that existing message
  to the two places that show the account's numbers as dashes and say nothing about why: the **My accounts** panel
  (where a row with no reading falls into "Not in the last read" and gets four dashes) and **Setup › Your accounts**
  (where you decide what each account sends). The accounts table, Profile stat and the account card already show it
  and are not touched. *Why:* the owner's ask was that the plugin tell them, and the fix for "said in the wrong
  place" is to say it in the right place, not to say it twice. *Cost if wrong:* the same sentence now appears on up
  to five surfaces at once, which is one per screen showing that account, not one screen repeating itself.
- **A45. The board's footer stays exactly as it is.** Task 9 changes neither `BoardText.Attribution` nor the
  `AttributionLine` control. That line is the recipes' credit — attribution is its job, and it must go on naming
  every source being read. Its known defect, printing the shared first sentence of two recipes' credits twice, is
  backlog **V3-S.4** and the owner is fixing it in the same footer, separately; this task must not collide with that
  edit. *Why:* one owner per line. *Cost if wrong:* none; if the two edits ever did collide, the conflict would be
  in one method neither task rewrites.
- **A46. One helper, and the only thing Ur Score adds is a name.** `PanelText.CannotRead` builds the note in one place, for
  both callers, because the same words on two screens that drift apart is worse than either. It returns the recipe's
  own message verbatim, one line per recipe that could not read that account at its last read, oldest source order
  (main first, as `AccountsModel.FoundIn` already orders them), joined with `Environment.NewLine`. Its only Ur Score
  words are the prefix `{recipe.Name}: `, added when `nameTheRecipe` is true — on Setup › Your accounts, where two
  recipes' rows sit side by side, and not on My accounts, which is already drawn per recipe. The recipe's name is
  how the rest of the tree refers to a recipe (`AccountsModel.Rows`'s "Send {account} for {recipe.Name}",
  `RecipesModel`, `SetupPages.For`), so Ur Score still names no game of its own: everything a reader sees about a
  game here was written by the recipe. An id RoRoRo isn't listing as yours never produces a note. *Cost if wrong:*
  with two recipes failing on one account the Setup row is two lines tall.
- **A47. Tasks 7, 8 and 9 change nothing in `tools/smoke`.** No automation id moves and none is removed; the new
  ones are listed below so a later walk can find them without anything shifting. The three tasks are proved by unit
  tests plus the controller's live looks (Task 8 Step 10, Task 9 Step 6), and Task 8 Step 10 item 6 re-runs the
  existing walks unchanged. *Why:* this is the same call A29 made for Task 6, and the cycle's own lesson was that a
  walk written and never run is a liability — the clan battle is Saturday. *Cost if wrong:* three features whose
  regression cover is unit tests and one pass of human eyes; the ids are in place for a walk in 0.3.3.
- **A48. It ships inside 0.3.2.** Task 5's `CHANGELOG.md` **Changed** list gains exactly this line: "An account a
  source can't read now says why beside its own empty numbers — on My accounts and on Setup › Your accounts, in the
  recipe's own words, which say what to do about it. The board's footer is unchanged." *Cost if wrong:* one line in
  one release note.

- [ ] **Step 1: Write the failing tests**

Add to `tests/PanelTextTests.cs` (its four usings already cover every name below; a test file is outside
`Labs626.UrScore`, so the bare name `Source` needs no alias there — only `src/Board/PanelText.cs` does):

```csharp
    private static RecipeSnapshot CouldNotRead(string sourceId, params (long UserId, string Why)[] said) =>
        BoardFixtures.Snapshot(sourceId, []) with { Unavailable = said.ToDictionary(x => x.UserId, x => x.Why) };

    /// <summary>Plan A44/A46: the recipe's own sentence, not a new one, and only the recipe's name added to it.</summary>
    [Fact]
    public void AnAccountASourceCouldNotReadCarriesTheRecipesOwnWords()
    {
        var profile = BoardFixtures.SourceOf("s-00000009", BoardFixtures.Profile, null, SourceRole.Mine);
        var latest = new Dictionary<string, RecipeSnapshot>
        {
            [profile.Id] = CouldNotRead(profile.Id, (BoardFixtures.AltOne.RobloxUserId, "Profile is private. Link this account on db.biggames.io and turn on its Profile view.")),
        };
        var installed = new[] { BoardFixtures.Installed(BoardFixtures.Profile, "diamonds") };

        Assert.Equal(
            "Profile is private. Link this account on db.biggames.io and turn on its Profile view.",
            PanelText.CannotRead(BoardFixtures.AltOne.RobloxUserId, installed, [profile], latest, nameTheRecipe: false));
        Assert.Equal(
            "Pet Sim 99 profile: Profile is private. Link this account on db.biggames.io and turn on its Profile view.",
            PanelText.CannotRead(BoardFixtures.AltOne.RobloxUserId, installed, [profile], latest, nameTheRecipe: true));
        Assert.Equal("", PanelText.CannotRead(BoardFixtures.Main.RobloxUserId, installed, [profile], latest, nameTheRecipe: true));
    }

    [Fact]
    public void EveryRecipeThatCouldNotReadItGetsItsOwnLineMainFirst()
    {
        var clan = BoardFixtures.SourceOf("s-00000001", BoardFixtures.Clan, "CCGP", SourceRole.Main);
        var profile = BoardFixtures.SourceOf("s-00000009", BoardFixtures.Profile, null, SourceRole.Mine);
        var id = BoardFixtures.AltOne.RobloxUserId;
        var latest = new Dictionary<string, RecipeSnapshot>
        {
            [profile.Id] = CouldNotRead(profile.Id, (id, "Profile is private.")),
            [clan.Id] = CouldNotRead(clan.Id, (id, "Not in this clan right now.")),
        };
        var installed = new[] { BoardFixtures.Installed(BoardFixtures.Clan, "value"), BoardFixtures.Installed(BoardFixtures.Profile, "diamonds") };

        Assert.Equal(
            "Pet Sim 99 clan battle points: Not in this clan right now." + Environment.NewLine + "Pet Sim 99 profile: Profile is private.",
            PanelText.CannotRead(id, installed, [profile, clan], latest, nameTheRecipe: true));
    }

    [Fact]
    public void ASwitchedOffSourceAndAnIdThatIsNotYoursSayNothing()
    {
        // Unavailable only ever holds your own ids; this pins that nothing here would print one if it didn't.
        var profile = BoardFixtures.SourceOf("s-00000009", BoardFixtures.Profile, null, SourceRole.Mine) with { Enabled = false };
        var latest = new Dictionary<string, RecipeSnapshot> { [profile.Id] = CouldNotRead(profile.Id, (999_999, "Profile is private.")) };
        var installed = new[] { BoardFixtures.Installed(BoardFixtures.Profile, "diamonds") };

        Assert.Equal("", PanelText.CannotRead(999_999, installed, [profile], latest, nameTheRecipe: true));
        Assert.Equal("", PanelText.CannotRead(0, installed, [profile], latest, nameTheRecipe: true));
    }
```

The recipe names above are the fixtures' own, checked against the tree at `ddfafc8`:
`tests/Fixtures/petsim99-profile.recipe.json` is `"name": "Pet Sim 99 profile"` and
`petsim99-clan-battle.recipe.json` is `"name": "Pet Sim 99 clan battle points"`. The profile fixture's
`unavailable.message` is quoted verbatim here and is the sentence this whole task is about.

Add to `tests/PanelModelsTests.cs`:

```csharp
    /// <summary>Plan A44: the panel with the dashes is the panel that says why.</summary>
    [Fact]
    public void MyAccountsSaysWhyARowHasNoNumbers()
    {
        var snapshot = Snapshot(ProfileSource.Id, [Row(Main.RobloxUserId, 4200, "diamonds")])
            with { Unavailable = new Dictionary<long, string> { [AltOne.RobloxUserId] = "Profile is private. Link this account on db.biggames.io and turn on its Profile view." } };
        var live = Live([ProfileSource], [Installed(Profile, "diamonds")], new Dictionary<string, RecipeSnapshot> { [ProfileSource.Id] = snapshot });

        var model = PanelModels.MyAccounts(live, Reader(), new PanelSettings(Profile.Slug, ProfileSource.Id, Stat: "diamonds"));
        var rows = model.Groups.SelectMany(g => g.Rows).ToList();

        var unread = rows.Single(r => r.UserId == AltOne.RobloxUserId);
        Assert.True(unread.Missing);
        Assert.Equal("Profile is private. Link this account on db.biggames.io and turn on its Profile view.", unread.Note);
        Assert.True(unread.HasNote);

        // An account that was read says nothing, and no row repeats the recipe's name on a panel drawn per recipe.
        var read = rows.Single(r => r.UserId == Main.RobloxUserId);
        Assert.Equal("", read.Note);
        Assert.False(read.HasNote);
    }
```

Add to `tests/AccountsModelTests.cs`:

```csharp
    [Fact]
    public void SetupSaysWhyAnAccountsNumbersAreEmpty()
    {
        var profile = new Source("s-00000009", Profile.Slug, new Dictionary<string, string>(), SourceRole.Mine);
        var latest = new Dictionary<string, RecipeSnapshot>
        {
            [profile.Id] = Read(Main.RobloxUserId) with { Unavailable = new Dictionary<long, string> { [Alt.RobloxUserId] = "Profile is private. Link this account on db.biggames.io and turn on its Profile view." } },
        };
        var installed = new[] { Sending(Profile) };

        var rows = AccountsModel.Rows([Main, Alt], installed, [profile], latest);

        Assert.Equal("", rows.Single(r => r.AccountId == Main.AccountId).Note);
        Assert.Equal(
            "Pet Sim 99 profile: Profile is private. Link this account on db.biggames.io and turn on its Profile view.",
            rows.Single(r => r.AccountId == Alt.AccountId).Note);
        Assert.True(rows.Single(r => r.AccountId == Alt.AccountId).HasNote);
    }
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`
Expected: FAIL to compile (`PanelText.CannotRead`, `AccountLineModel.Note` and `AccountRow.Note` don't exist).

- [ ] **Step 3: The one place the words are built**

In `src/Board/PanelText.cs`, add `using Labs626.UrScore.Recipes;` to the usings if it isn't there, add
`using Source = Labs626.UrScore.Core.Source;` directly after the `namespace Labs626.UrScore.Board;` line (the bare
name `Source` would otherwise find the `Labs626.UrScore.Source` namespace), and add to `PanelText`:

```csharp
    /// <summary>
    /// Why a source could not read one of your accounts at its last read, in the recipe's own words, or empty
    /// (plan A44, A46). One line per recipe that said so, main source first.
    /// <para>
    /// The sentence is the recipe's: <c>unavailable.message</c>, which is the only thing here that knows what it
    /// reads and what you must do about it. The only words Ur Score adds are the recipe's own name, and only when
    /// <paramref name="nameTheRecipe"/> — a screen that already shows one recipe at a time doesn't need telling.
    /// </para>
    /// </summary>
    public static string CannotRead(
        long userId, IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources,
        IReadOnlyDictionary<string, RecipeSnapshot> latest, bool nameTheRecipe)
    {
        if (userId == 0) return "";

        var lines = new List<string>();
        foreach (var source in sources.Where(s => s.Enabled).OrderBy(s => s.Role == SourceRole.Main ? 0 : 1))
        {
            if (installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, source.Recipe, StringComparison.Ordinal))?.Recipe is not { } recipe) continue;
            if (latest.GetValueOrDefault(source.Id)?.Unavailable.GetValueOrDefault(userId) is not { Length: > 0 } why) continue;

            var line = nameTheRecipe ? $"{recipe.Name}: {why}" : why;
            if (!lines.Contains(line, StringComparer.Ordinal)) lines.Add(line);
        }

        return string.Join(Environment.NewLine, lines);
    }
```

- [ ] **Step 4: My accounts says it**

In `src/Board/PanelModels.cs`:

1. Replace the `AccountLineModel` record with:

```csharp
public sealed record AccountLineModel(
    long UserId, string Name, string Value, string InGroup, string Change, bool Sent, bool Stalled, bool Missing,
    string? Avatar = null, string Note = "")
{
    /// <summary>Why this row has no numbers, in the recipe's own words (plan A44). Empty for a row that was read.</summary>
    public bool HasNote => Note.Length > 0;
}
```

2. In `MyAccounts`, the "not in a watched" group's rows become:

```csharp
                [.. rest.OrderBy(a => a.DisplayName, StringComparer.Ordinal)
                    .Select(a => new AccountLineModel(
                        a.RobloxUserId, a.DisplayName, Dash, Dash, Dash, false, false, true, live.AvatarFor(a.RobloxUserId),
                        PanelText.CannotRead(a.RobloxUserId, live.Installed, live.Sources, live.Snapshots, nameTheRecipe: false)))]));
```

   `live.Snapshots`, not `live.SnapshotOf`: a remembered snapshot never carries an `Unavailable` entry (A39), so
   this reads what was actually read, and says nothing at all before the first read.

In `src/UI/Panels/MyAccountsPanel.xaml`, wrap the row's first column: replace the opening tag
`<DockPanel Grid.Column="0">` with

```xml
                                                    <StackPanel Grid.Column="0">
                                                        <DockPanel>
```

and replace that DockPanel's matching `</DockPanel>` with

```xml
                                                        </DockPanel>
                                                        <!-- Indented past the avatar slot and the sent dot (28 + 12 px, A26), so the
                                                             reason lines up under the name it is about. -->
                                                        <TextBlock Text="{Binding Note}" Style="{StaticResource Muted}" FontSize="11"
                                                                   Margin="40,3,0,0"
                                                                   Visibility="{Binding HasNote, Converter={StaticResource BoolToVisible}}" />
                                                    </StackPanel>
```

(the three children between them — the avatar `Ellipse`, the sent-dot `Ellipse` with its `Hidden` trigger, and the
name `TextBlock` — are untouched; only their parent moves one level down.)

- [ ] **Step 5: Setup › Your accounts says it**

In `src/UI/Setup/AccountsModel.cs`:

1. Replace the `AccountRow` record with:

```csharp
public sealed record AccountRow(
    Guid AccountId, string DisplayName, string FoundIn, IReadOnlyList<SendTick> Sends, string? Avatar = null, string Note = "")
{
    /// <summary>Why a source can't read this account, in that recipe's own words (plan A44).</summary>
    public bool HasNote => Note.Length > 0;
}
```

2. In `Rows`, the row's last argument becomes two:

```csharp
            avatar?.Invoke(account.RobloxUserId),
            PanelText.CannotRead(account.RobloxUserId, installed, sources, latest, nameTheRecipe: true)))];
```

In `src/UI/Setup/AccountsPage.xaml`, replace the row's `Found in` cell —
`<TextBlock Grid.Column="1" Text="{Binding FoundIn}" Style="{StaticResource Muted}" VerticalAlignment="Center" />` —
with:

```xml
                            <StackPanel Grid.Column="1" VerticalAlignment="Center">
                                <TextBlock Text="{Binding FoundIn}" Style="{StaticResource Muted}" />
                                <!-- The recipe's own words for why this account's numbers are empty, beside the row where
                                     you decide what it sends (plan A44). Muted, not Refusal: it is a standing condition,
                                     not the result of something you just pressed. -->
                                <TextBlock Text="{Binding Note}" Style="{StaticResource Muted}" FontSize="11" Margin="0,3,0,0"
                                           Visibility="{Binding HasNote, Converter={StaticResource BoolToVisible}}" />
                            </StackPanel>
```

- [ ] **Step 6: Run the tests, then the build gate**

```
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~PanelTextTests|FullyQualifiedName~PanelModelsTests|FullyQualifiedName~AccountsModelTests"
dotnet build Ur-Score.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build
```

Expected: all four pass. The app build is run on its own because the two XAML re-parentings only fail there — check
by eye that the My accounts name column still starts at the same x as its `Account` heading (A26's 40 px).

- [ ] **Step 7: Commit**

```bash
git add src/Board/PanelText.cs src/Board/PanelModels.cs src/UI/Panels/MyAccountsPanel.xaml src/UI/Setup/AccountsModel.cs src/UI/Setup/AccountsPage.xaml tests/PanelTextTests.cs tests/PanelModelsTests.cs tests/AccountsModelTests.cs
git commit -m "accounts: an account a source can't read says why beside its own empty numbers

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Step 8: The live look (controller)**

On your own data, with at least one account whose profile is not linked or not public (if every account is linked,
un-link one on db.biggames.io, read once, and link it back afterwards):

1. **My accounts.** That account sits under "Not in the last read" with four dashes and, under its name, the
   recipe's own sentence naming db.biggames.io. Every other row is unchanged and no row moved sideways.
2. **Setup › Your accounts.** The same account's row carries the same sentence under "Found in", prefixed with the
   recipe's name; the accounts that were read carry nothing.
3. **The footer is untouched.** The credit line at the bottom of the board reads exactly what it read before this
   task (whatever V3-S.4 has left it as), and Task 9 added nothing to it and took nothing from it.
4. **It clears.** Link the account, press Test now: its numbers appear and both notes go.

**Automation ids added by Tasks 7, 8 and 9** (nothing is moved or removed, so every walk runs unchanged — A47):

| Where | Automation ids and names |
|---|---|
| Setup › Recipes | `StartOnOpenBox`, a checkbox named "Start reading as soon as Ur Score opens"; `StartOnOpenProblemLine` |
| The board's top bar | `StateLine` gains the remembered sentence; `DetailLine`, `PeriodLine`, `AttributionLine` unchanged |
| Inside a panel | `PanelRemembered`, the word "remembered" beside a panel's title |
| My accounts, Setup › Your accounts | each row's reason, inside its row template and named by its own words, like the Send ticks |

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

## Execution record (2026-09-15)

Built by five subagent-driven tasks off `cec5987` (v0.3.1 + the backlog docs), each with its own dispatch, a per-task review, and a fix round where the review found real issues. Tasks 1, 2 and 3 took one fix round each; Task 4 took two, and the second one undid the first; Task 6 — the owner's late ask, planned and executed mid-cycle — took three. A whole-branch review then read `cec5987..8177b8d` in one pass, weighted on what a per-task review structurally cannot see: the two halves' shared files, the privacy fence across both halves at once, the release surface, and the states that only appear on a real machine. It found one Critical (the controller's own, see below), two Importants and twelve Minors; a scoped re-review of the four fix commits confirmed the Critical and one Important closed, and sent one Important and one Minor back for another round.

The branch ends at **936 tests**, both the app and test-project builds `-warnaserror` clean, working tree clean at `ddfafc8`.

That was the branch at day's end for the first six tasks, not the end of the cycle. The owner then closed both apps, answered five open questions, and the cycle kept going: Tasks 7, 8 and 9, four more fix rounds (three of them Task 8's own), a second whole-branch review, and a dedicated README audit. The branch now ends at **974 tests**, both builds still `-warnaserror` clean, HEAD at `fa3897a`; the release run (Task 5) is in progress in the owner's own working tree as this record is written (`CHANGELOG.md`, `manifest.json` and the csproj all carry uncommitted edits) and is not part of it.

**The walk ran, and passed.** The owner closed both apps; `walk-alerts.ps1` then passed **30 of 30** against a scratch rules file, including check 8 — RoRoRo's own `metric-rules.json` was byte-identical afterwards. `walk-stats-table` passed 10/10 and `walk-alts` 8/8 (one check self-skipped as needing RoRoRo). `walk-starter-board` failed its check 3 ("My accounts groups your accounts by clan") for an environmental reason, not a defect: RoRoRo was closed too, so Ur Score had no accounts to group — the panel drew its headings over zero rows, and the Alerts page said "0 of your 0 accounts" in the same run. That check has no needs-RoRoRo skip, unlike the one in `walk-alts`; giving it one is `AC-BR.2`.

**The second half's walks ran too, this time with RoRoRo up.** All green: `walk-alerts` 30/30 (check 8 again byte-identical against RoRoRo's real `metric-rules.json`), `walk-starter-board` 21/21, `walk-pop-outs` 13/13 (one self-skipped, needing a live battle), `walk-stats-table` 10/10, `walk-alts` 8/8 (one self-skipped), `window-smoke` 12/12, and `walk-score-book` 6/6 against `-Main K0i2`. Both privacy checks exit 0. `walk-score-book`'s check 4 fails with the default `-Main CCGP` for an environmental reason, not a defect — CCGP has no readable battle (backlog `V3-S.1`) — the same shape as the `walk-starter-board` failure the first half recorded, and worth the same skip `AC-BR.2` already covers for that one.

**Task 6's live look ran, on the owner's real data.** Avatars present on all 8 accounts in My accounts and Setup › Your accounts; the standing privacy line on screen; headings lined up. Proved, not asserted: 8 account ids, 8 `avatar-<id>.png` files, 0 that are not the owner's, 0 accounts missing a picture, every file written in the same second — one batched fetch (A23). Found by eye in the same look, and not anticipated by any review: the board footer printed its credit **twice**. The backlog's own assumption about the cause was wrong — `Attribution` already deduplicated, but by whole string, and three of the owner's Pet Sim 99 recipes each open with the same shared sentence before adding their own. Fixed at `d7102ae` by deduplicating per sentence rather than per whole string (937 tests; closes the pre-existing backlog item `V3-S.4`). Fixing it then tripped `NoHostnameFenceTests` on the controller's own doc comment, for naming a host outside `IconClient`/`NameClient` — the fence working exactly as designed, on the controller; the comment was reworded rather than the fence touched. The same look confirmed the board footer already tells people to link their Big Games profile, so the owner's fifth answer became a question of *placement* for Task 9, not of writing a new sentence — the planner was steered accordingly mid-flight.

**The owner answered, that evening.** Both apps closed, then RoRoRo restarted, the owner answered all five: closing RoRoRo and Ur Score; merging the RoRoRo companion branch to `main` now; shipping start-on-open as an opt-in setting a user turns on after setup, never automatic; showing the last numbers Ur Score saw at launch; and having the plugin say where to link a Big Games profile. RoRoRo's `feat/metric-alert-wording` merged to `main` (`--no-ff`) after a green build+test on the branch and again on the merged result; the branch was deleted; `main` sat 13 commits ahead of `origin/main`, unpushed, pushing being the owner's call, and CI must be green on `main` before any tag. Task 5 (the release run) had still not started when Tasks 7-9 began; as of this record it is in progress in the owner's own working tree, outside this record's scope.

### What shipped

- **Setup › Alerts is a card per stat you send.** Each alert reads as a sentence you fill in — "stops climbing" (fewer than a number a minute for 10, 15 or 30 minutes) and "crosses a number" (above or below) — with + Add an alert, Change and Remove, and what happened said on the card in the theme. Rules you or another plugin wrote are listed and marked, never changed. A standing "Next, in RoRoRo" line says the one switch to flip. No stock message box, no JSON on screen, and a fence test pinning both.
- **The rules file is written safely.** Every other rule and every unknown field survives a write byte for byte; the write goes through a temp file and one `File.Replace` swap, so a failure leaves the rules file *and* the previous undo point untouched; a locked, invalid or non-list file is refused with the reason on the card. Rules Ur Score writes carry `owner` and `label` (RoRoRo 1.28 ignores the label; the companion `feat/metric-alert-wording` branch reads it).
- **Rows are read exactly as RoRoRo reads them**, by a hand-copied `RuleRow`/`RowOptions` pinned to RoRoRo commit `dc44992` by six parity tests.
- **`RuleInventory` and `written-rules.json` are gone.** The Stats table's rule line now counts a stat's alerts from one snapshot instead of reading RoRoRo's file on every render.
- **Your own accounts show their Roblox picture** on all five surfaces, behind a privacy fence that is gated on the id rather than the caller at three independent layers. No other player's id ever leaves the machine, and no other player's picture ever reaches disk.
- **Ur Score can start reading the moment it opens**, off until you turn it on in Setup › Recipes. It does exactly what pressing Start does — same gate, same in-flight flag, same failure line — and only when a press would have done something; pressing Start by hand, on or off, is untouched.
- **The window opens on the last numbers the score book kept**, instead of empty panels, for every enabled source. Each panel that draws them says `remembered`; the state line says how old the oldest of them is; a remembered clan rank comes from the book itself rather than being derived from whatever rows happen to be in hand; and the first live read of a session replaces them, source by source.
- **An account a source can't read says why**, beside its own empty numbers, on My accounts and Setup › Your accounts — the same sentence three other surfaces already showed, routed to the two that only showed dashes.

### Tasks

- **Task 1** (rules file operations): `65224f1`, fix round 1 `aed8848`.
- **Task 2** (the pure Alerts model): `a5e1093`, fix round 1 `f9fd6f7`.
- **Task 3** (the themed page, the rules path, the old helper retired): `35242f0`, fix round 1 `953ee44`.
- **Task 4** (the Setup › Alerts smoke walk): `7c5b93e`, fix round 1 `ba0037c` (**superseded**), fix round 2 `05332af`.
- **Task 6** (avatars beside your own accounts): `52591e2`, fix round 1 `8177b8d`, fix round 2 `34569ae`, fix round 3 `ddfafc8`.
- **Controller's own commits:** `0666172`, `8e1bdd9`, `84d4c19`.
- **Task 7** (start reading when Ur Score opens): `5124fa2`, fix round 1 `93ab004`, controller's copy tightening `6ac00e6`, review's two Minors fixed `daa2f59`.
- **Task 8** (the window opens on the last numbers it saw): `1ce759e`, fix round 1 `f922ab3`, fix round 2 `7ee4afd`, fix round 3 `4ce08bb`.
- **Task 9** (an account a source can't read says why, beside its own empty numbers): `ace82c8`.
- **Controller's own commits (second half):** `d7102ae`, `fa3897a`.
- **Task 5** (the release run): not started within this workspace; the owner is carrying it out directly in the working tree as this record is written.

### Every commit, and what it is for

| Commit | What it is for |
| --- | --- |
| `bab5335` | The design: an Alerts page that tells you what to do. Approved before the plan was written. |
| `d0a94a3` | The plan: A1-A20, Tasks 1-4, the release run. |
| `65224f1` | Task 1. Read every rule as RoRoRo does; turn on, change or remove Ur Score's own, backing the file up first. |
| `aed8848` | Task 1 fix round 1. One `File.Replace` swap, so a failed write keeps the last backup; six parity tests pinning RoRoRo `dc44992`; a shared blank-id guard and a path-that-names-no-file fallback. |
| `a5e1093` | Task 2. Cards whose alerts read as sentences, checked input, and each step of the inline editor — pure, no WPF. |
| `f9fd6f7` | Task 2 fix round 1. Numbers show exactly as typed; a rule's place in the file never redraws the cards; minutes are 10, 15 or 30. |
| `35242f0` | Task 3. The themed Alerts page of cards and sentences, edited in place, with no message box and no JSON; `ISetupServices.RulesPath`; `RuleInventory` retired. |
| `953ee44` | Task 3 fix round 1. Change and Remove read the file at the click, not the drawn card; `AlertCards.Gone` says so in magenta when the alert is gone or now another owner's. |
| `7c5b93e` | Task 4. `walk-alerts.ps1`: Setup › Alerts against a scratch rules file, with RoRoRo's own file hashed before and after. |
| `52591e2` | Task 6. Your own accounts show their Roblox picture, and no one else's is ever asked for. |
| `ba0037c` | Task 4 fix round 1. Per-card scoping of the six line reads, plus the Stats save wait asserted. **The scoping half was wrong and is reverted at `05332af`;** the asserted wait survives. |
| `8177b8d` | Task 6 fix round 1. Eight Minors: the heading indent, an A26 `Hidden` fence, the 1601 empty-ring case, unconditional forgetting, a wrong doc comment, two fences given teeth, a stale inventory row — plus the hostname sweep extended to `.xaml`. |
| `0666172` | Controller. The README says you can install it (the banner and the "you cannot install this yet" block were three releases stale), and the live-look step reads the cache the way it behaves. |
| `34569ae` | Task 6 fix round 2. The sent dot holds its space, so every name in the My accounts column starts at the same x. |
| `05332af` | Task 4 fix round 2. Revert the per-card scoping (A13 makes it unnecessary) and pin the invariant with `Assert-OneLine` instead. |
| `8e1bdd9` | Controller. Gitignore the build gate's five root transcripts, so one stray `git add -A` cannot commit build logs to a public repo. |
| `84d4c19` | Controller. The README describes the window that shipped: Setup › Clans instead of Notepad, sentence cards instead of a dead JSON-preview button, and the four settings keys that still exist. |
| `ddfafc8` | Task 6 fix round 3. Both A26 fences assert the `Visibility` setter's value, not just the trigger's shape. |
| `d7102ae` | The controller's own fix: the board footer printed its credit twice for the owner's three Pet Sim 99 recipes, each opening with a shared sentence before adding its own; `Attribution` now dedupes per sentence rather than per whole string. Closes the pre-existing backlog item `V3-S.4`. |
| `5124fa2` | Task 7. A `startOnOpen` setting, off by default: when it is on, the board does once, on open, exactly what pressing Start does. |
| `93ab004` | Task 7 fix round 1. The line under the checkbox says *when* the tick takes effect — the next time you open Ur Score — not just that it is off until you turn it on. |
| `6ac00e6` | The controller's own copy tightening of that line to three sentences, without running the gate (Task 8's implementer was mid-build); reverted two commits later once reviewed. |
| `1ce759e` | Task 8. The board opens on the last reading the score book kept for each source, marked `remembered`, with a state-line sentence dated from the oldest one on screen. |
| `daa2f59` | Task 7 review's two Minors: the "Ur Score … Ur Score" echo fixed by reverting the third sentence to the implementer's original wording (better than the controller's own rewrite at `6ac00e6`), and the README bullet rewrapped to the file's usual width. |
| `ace82c8` | Task 9. An account a source can't read says why, beside its own empty numbers, on My accounts and Setup › Your accounts. |
| `f922ab3` | Task 8 fix round 1. A remembered snapshot's clan rank is now carried from the book or shown as a dash, never derived from the four rows in hand; the state-line sentence survives every branch; the first draw waits on RoRoRo's account list before filling. |
| `7ee4afd` | Task 8 fix round 2. A read that failed keeps the remembered numbers and their mark instead of clearing them to an empty board. |
| `4ce08bb` | Task 8 fix round 3. Only a reading that actually came back is plotted on the Race chart at the minute it came back; the Account card's "Last read" fact given the same fix. |
| `fa3897a` | The controller's own commit: every claim in the README checked against the tree rather than just the flagged lines, and the Task 7/8/9 and second whole-branch review's nice-to-haves folded into the backlog before the workspace goes. |

### Rulings made during execution

**From the pre-flight scan** (90 rows: 0 BLOCKER, 6 FIX, 28 NOTE). All six cost-if-wrong lines read "none."

- **Read rules exactly as RoRoRo does** (T1): skip a row unless `label` is absent, null or a string; when a field appears twice with different case, the last one wins, as RoRoRo's case-insensitive deserializer does. Both rows went into the skip test. The implementer went further than the ruling and copied RoRoRo's own `RuleRow`/`Options` field for field, which caught two more divergences the hand-written readers would have shipped (`1e400` reads as `Infinity` rather than being skipped; case-duplicate precedence).
- **A Change that changes nothing writes nothing** (T1): compare the built row with the existing one by `JsonNode.DeepEquals` and return Done with no backup and no write. An edit that changes nothing should not consume the one undo point.
- **`PinnedLabel` uses `MetricId?.Trim()`** (T2), like every other reader in the tree (`RecipeStore.cs:54`).
- **Drop the repeated `git rm src/Core/RuleInventory.cs`** from Task 3 Step 10 — Step 7 already removed it.
- **`shot.ps1` calls pass `-OutPath`** (T3 Step 9, T4 Step 4).
- **The walk throws before any click** if the seeded scratch-file sentence has not appeared within 15 s, so it can never act while Ur Score is reading RoRoRo's real `metric-rules.json`.

**During the tasks**

- **Task 1's two Importants both taken.** The `File.Replace` swap (a failure leaves file and backup untouched) in place of the brief's copy-then-move, and a `MIRRORS ROROROBLOX` comment naming RoRoRo's `LocalFileMetricRuleSource` at `dc44992` plus a parity test class pinning each behaviour relied on, `1e400` included. The blank-label omission and the numeric-`"kind": "7"` divergence were left as recorded, not chased.
- **Task 2: a small fix round for three of the twelve Minors** — values near the cap never display rounded; `AlertCards.Same` compares rule identity rather than row index, so an unrelated edit higher in the file cannot steal your keyboard focus; minutes are only 10, 15 or 30 for Ur Score's own rules. The other nine parked. The third of these reversed A12's extra own-minutes choice: a stored 20-minute rule now opens Change with an empty minutes box. Save is blocked with a clear line rather than silently rewriting the window, so this was judged a Minor and recorded rather than reverted.
- **Task 2's scoped re-review ran in parallel with Task 3.** The re-review is read-only and any finding would be a small model fix, so the cost if wrong was Task 3 adapting to one tweak.
- **Task 3: one fix round, for the stale-view Minor** — re-read the file at the start of Change and Remove, as `OnAddClick` already did. The write was always correct (`RulesFile` re-resolves `OursFor` against a fresh load), but the result sentence and Change's starting values could describe the old rule. The trail-only exception path and the untested focus wiring were left; the latter became a requirement on Task 4's walk, which must press Enter in a *closed* minutes box and Escape in a *closed* direction box, since a closed `ComboBox` does not handle those keys itself.
- **The owner's late ask became Task 6, not a bolt-on.** Roblox avatar headshots beside your own accounts, planned mid-cycle with its own rulings A21-A30, executed after Task 4 and before the release, so the order became T4 → T6 → T5. Two of its rulings were flagged for a nod and both stood: **A26's** permanent ~28 px indent on pictureless rows (a row that never moves is worth a constant indent; the alternative reflows numbers under the reader's eye) and **A28's** disclosure placement (the import screen lists what *that recipe* contacts, so an Ur Score-wide call there would make every import ask about something the recipe does not do; the README row plus a standing line on Setup › Your accounts are the honest place). Cost if wrong: a cosmetic indent, and one privacy sentence that moves to a different screen in a later version.
- **Task 6 was dispatched before Task 4's walk had been run**, rather than idling on a gate the owner controlled. It touches `src/` only, never `tools/smoke` (A29) and never the Alerts page the walk exercises, so the walk's result could not invalidate it. Cost if wrong: the two diffs interleave, which they would not, being different files.
- **Task 6: one fix round for eight of the thirteen Minors** — the header alignment (it lands in every screenshot, and the owner's standing rule is match the mock), a fence pinning A26's `Hidden` across all five surfaces, the missing-file-reads-1601 empty ring, `Keep` before the empty-list early return so forgetting is unconditional, a wrong doc comment, a fence assertion weaker than its name, a totals-row assertion that passed without a totals row, and a stale inventory row; plus an *attempt* at extending the hostname sweep to `.xaml`, with instructions to stop rather than edit product copy if it went red. Parked: #4, #6, #8, #11.
- **The `.xaml` hostname carve-out stands.** The sweep flags 60 matches, every one a `schemas.microsoft.com` inside an `xmlns` declaration. The fence strips `xmlns`/`xmlns:prefix` attribute *values* from `.xaml` only, sweeps everything else including `Text=` prose, keeps `.cs` swept whole, and adds a vacuity floor asserting at least one `.xaml` file was swept — which closes the failure mode that actually worried the controller, a fence that silently sweeps nothing. An XML dialect URI is not a host anything contacts.
- **Nothing prunes the icon cache, and that takes no code this cycle.** The ids are the owner's own and a stale file ages out on the seven-day refresh — but Step 11 item 5's count check would read as a false failure for anyone who has ever removed an account, so a sentence went into that step instead. Cost if wrong: a lingering file the owner could delete by hand.
- **The controller reviewed Task 4's 43-line single-file fix himself** instead of spending a re-review seat, on the grounds that he is the one running the walk and the walk's own run is the real gate. That ruling is the one that cost the most; see the first lesson below.

**During Tasks 7, 8 and 9**

- **The owner's five late answers became Tasks 7-9, planned together rather than one at a time.** Merge RoRoRo's companion branch now; ship start-on-open as an opt-in setting a user turns on, never automatic; show the last numbers Ur Score saw at launch; and have the plugin say where to link a Big Games profile — already true of the footer, so this became a placement question for Task 9, not a missing sentence. Four planner calls were accepted rather than sent to the owner: the checkbox lives on Setup › Recipes, the only always-listed page about what Ur Score reads; Task 9 adds no new sentence and leaves the footer alone; no new walk for Tasks 7-9, covered by unit tests plus controller live looks (A29's precedent); Remembered matches on recipe slug + inputs key, deliberately not the recipe hash. Cost if wrong: each is a small, visible change the owner can redirect at the live look.
- **Task 7's copy was sent back once.** The explainer under the checkbox said the setting is off until turned on, but not that ticking it while the board is already open does nothing until the next launch (A33, by design) — the exact confusion the owner had just pushed back on this cycle ("we need it to be more clear about what to do"). Sent back to say *when* it takes effect, in the control's own words rather than a tooltip, since a tooltip is invisible to exactly the person who is confused. Cost if wrong: one sentence of copy.
- **Task 8 was dispatched in parallel with Task 7's fix round**, on disjoint files, with the five Task 7 files named untouchable and an instruction to re-read `AppServices.cs` immediately before editing it and to stop rather than revert on a conflict. Cost if wrong: one merge conflict in one file, caught by the build.
- **Task 8's review was weighted on the one risk that dominates: a remembered number read as live.** Told an unmarked surface is a Critical, not a Minor; to check the mixed live/remembered case; to verify the age is taken from the *oldest* reading; and to trace that a remembered number can never be reported or written back. It found exactly that Critical, one step removed from where A40 had already guarded it.
- **C1 (a remembered snapshot fabricated a clan rank) was fixed by carrying the honest number, not suppressing it.** `LineBuilder` was already writing the competition rank and the true row count; `RememberedRanks` picks them up, so a remembered "In clan" reads the real place rather than a dash. The one case this doesn't reach — a remembered per-account (profile) recipe, which the book keeps no rank for — was ruled to show a dash rather than a special case: the exception is exactly how this class of bug comes back, and the honest fix is teaching the book to keep a rank for per-account readings, not letting the panel derive one. Cost if wrong: one dash on one column of one panel.
- **I1 (the sentence dropping on the fault branch) was overridden against the plan.** The brief's Step 6 mandated the early return; the ruling was that the sentence must ride every branch that can return while remembered numbers are drawn, because A41 called it "cannot be forgotten" and the fault branch is exactly when the numbers beside it go stale. Cost if wrong: the state line carries one more clause in an error state.
- **Concern 3 (a failed read wiping the remembered numbers) was ruled a defect, not a Minor, and sent back as fix round 2.** On the owner's own machine this reads: open before a battle, see last night's numbers, watch the first read fail — and CCGP routinely returns nothing readable — and the board goes blank, strictly worse than before the feature existed. Ruling: a read that failed has replaced nothing, so it must leave the numbers and their mark alone; only a successful read clears them. Cost if wrong: stale numbers persist through a failing session, which is what the mark exists to say.
- **The one judgement left to the controller — `IsOverdue` reading the attempt stamp rather than the last success — stays for 0.3.3.** It is pre-existing, defensible, and the state line already names the failure on the same screen; redefining what "overdue" means reaches past this task and past this release. Cost if wrong: a failing source looks calmer than it is, contradicted on the same screen by the state line.
- **The second whole-branch review's Critical (`Race` plots a remembered total at "now") was fixed at the door, not by naming the bad condition.** The implementer chose `HeadlineNumber(live.LiveOf(source.Id), totalId)` over the review's suggested `snapshot?.RememberedAt is null && …` guard, on the grounds that it states the requirement — plot what this session actually read — rather than the one condition that currently violates it. The audit ordered alongside it (see below) found a second inheritance of the same defect and the implementer fixed it rather than only reporting it, judged by the controller as exactly the discipline the round-1 mistake had been trying to teach all night. See lessons 3 and 5 below.
- **The README was audited claim by claim, not line by flagged line.** Removing the pre-recipes banner that morning and fixing only the sentences the first whole-branch review had named left three contradictions (found and fixed by the branch re-review); the controller's own copy that evening (`6ac00e6`) then added at least one more — the Troubleshooting table's invented "Idle — nothing to watch" line. Rather than fix a third flagged sentence and call it settled, a dedicated auditor (opus) was dispatched to check every claim in the file — about 150 of them — against the tree rather than the lines a reviewer had named. It found nine more contradictions, two of them wrong statements about what leaves the owner's machine. See lesson 4 below.
- **The backlog's 0.3.2 section was re-scoped in the same commit as the README audit** (`fa3897a`), from `cec5987..ddfafc8` to `cec5987..4ce08bb` — the full cycle to that point — with 36 rows added for Tasks 7-9 and the second whole-branch review, and the counts restated. Two judgement calls were flagged rather than silently resolved: `AC-B2.8` duplicates a pre-existing off-by-one count and is recorded FIXED because the recount folds into the new totals, though it concerns the doc rather than code; `AC-9.4` (a report's own test-count miscount) is marked GONE by the existing precedent for report-only inaccuracies rather than a code question. Both accepted — the precedent is real and following it beats inventing a status.

### Findings worth keeping past this cycle

**1. A false review finding, ruled on without checking the invariant, broke the thing it was meant to protect.**

Task 4's review raised an Important: `AlertResultLine` and `AlertEditorProblemLine` are declared once per card in the `DataTemplate`, `AlertCardList` does not virtualize, and `Wait-Line`/`Line` `FindFirst` from the window root — so with two sent stats on screen, checks 3b and 3c would always read Diamonds' copy and fail every run regardless of the app. It reads as airtight. The controller confirmed the XAML lines it cited and ruled a fix into the walk: climb `ControlViewWalker` parents from a per-stat-unique element to the card root, and scope the reads there.

It was a false finding, and the fix broke the walk outright. **The whole-branch review caught it as a Critical:** there is no per-card ancestor to climb to. `RowListAutomationPeer` is a plain `FrameworkElementAutomationPeer`, and WPF gives `Border`, `StackPanel` and `ContentPresenter` no automation peer at all, so every card's controls hoist **flat** under `AlertCardList`. The climb hits `AlertCardList` on step one and throws at all six sites; the walk would have aborted at check 2c, on an app behaving correctly — the same false-regression signal Task 4's review was written to prevent, just relocated.

**The invariant that made the original code correct was stated in a one-line comment the whole time**, at `src/UI/Setup/AlertCards.cs:32`: *"The page's one open editor and its one result line (A13)."* `AlertsUi` carries a single `Problem` and a single `ResultMetricId`; `Row()` blanks `Result` on every card but the matching one; both lines bind `Visibility` through a plain `BooleanToVisibilityConverter`, which yields `Collapsed`, and a `Collapsed` element has **no automation peer** — it is absent from the tree entirely, not present-but-offscreen. At most one of each id is ever findable. The unscoped read at `7c5b93e` was right all along.

The ruling was to revert the scoping, keep round 1's real fix (the asserted Stats save wait), and **not** take the branch review's own suggested fix either — an `IsOffscreen` filter rests on the same wrong premise. Instead, `Assert-OneLine` now asserts A13 rather than assuming it: a `FindAll` for the id must return at most one, throwing with the id and the count otherwise, with a comment recording why an unscoped read is correct. That turns a bad ruling into a net that catches the failure the false finding imagined. Cost if wrong: none.

The lesson is not that reviews are sometimes wrong. It is that a finding about a *structural* invariant has to be checked against the invariant, not against the lines the finding cites — and that the invariant was already written down, one line above the type that enforces it.

**2. "Is the fix complete?" is the wrong question. "What does this column lay out before a name, and is *all* of it unconditional?" is the right one.**

The heading-alignment fix was made twice. Round 1 measured the avatar slot — a 20 px ellipse plus an 8 px margin — indented every heading by 28 px, and claimed in its own commit subject that "the Account heading sits over the names again." The whole-branch review found it still misaligned on My accounts, and inconsistent *within itself*: that row has a second leading ellipse, the 6 px cyan "sent" dot, which was `Visibility`-bound through `BoolToVisible` and therefore **`Collapsed`** — no reserved space. Names started at 40 px on a sent row and 28 px on every other one, so the heading could line up with one or the other but never both.

Round 2 gave the dot the same rule A26 gives the avatar slot (a `DataTrigger` on `Sent=False` setting `Hidden`), so it holds its 12 px whether or not it paints, and set the heading to 28 + 12 = 40. A row no longer reflows when a report first lands.

The implementer's own account of why round 1 missed it is the part worth keeping: **it measured the avatar slot and stopped, treating the pre-existing dot as background.** The right question was what the column lays out before a name and whether all of it is unconditional — which is exactly why the same offset held on the other three surfaces, where nothing conditional sits between the row's left edge and the name.

**3. A mutation test must mutate *the* defect, not *a* defect.**

Round 2 added `TheSentDotIsLaidOutWhetherOrNotAnAccountIsSent` and mutation-tested it — by restoring the old `BoolToVisible` binding, which the test obviously caught. The scoped re-review found the gap: the test asserted the trigger's *shape* and the old converter's *absence*, and never read the setter's value. A regression to `<Setter Property="Visibility" Value="Collapsed" />` **inside that very trigger** keeps the opening tag and introduces no converter, so it would have sailed straight through the one test written to catch it — the exact defect that had just shipped.

Fixed at `ddfafc8`: both A26 fences now read the setter itself through a shared `HidesRatherThanCollapses` check (the markup sets `Property="Visibility"`, the value is `Hidden`, `Collapsed` appears nowhere in it), each assertion carrying a message that explains A26 rather than printing a string diff. Mutation-tested this time on the value: flipping each setter to `Collapsed` failed exactly its own test and nothing else moved. The avatar fence turned out **not** to have had the same gap — it already asserted `Value="Hidden"` — but was tightened to name the `Visibility` setter rather than trust that no other setter in that style carries a `Hidden`.

A mutation only proves the assertion it actually exercises. Choosing a mutation the test was already obviously going to catch proves nothing about the assertion that matters.

The second half produced the sharper version of the same lesson, this time in the choice of *fix* rather than the choice of mutation. The second whole-branch review's Critical named one bad condition — a `_lastRead` stamp surviving a failed read — and suggested guarding `Race`'s synthetic point on `snapshot?.RememberedAt is null`. The fix that shipped, `HeadlineNumber(live.LiveOf(source.Id), totalId)`, does not name that condition at all; it asks what the live reading actually brought back, which is true by construction whether the violating state is `SourceUnreachable`, `RateLimited`, `ShapeNotUnderstood`, `SourceIdle`, or a state nobody has written yet. A guard shaped around today's known offender is exactly the kind of mutation a test was already going to catch; a fix shaped around the requirement — plot what this session read — cannot be reopened by tomorrow's new failure mode. It is the same choice as the mutation-test lesson above, made one layer up, where getting it right is worth more.

**4. Deleting a staleness banner without auditing what it covered publishes the debt.**

The README carried a pre-recipes banner and a "you cannot install this yet" block, three releases stale — RoRoRo v1.28.0.0 had shipped the `ReportMetric` call it was warning about, and `icon.png` had been in the repo root since v0.2.0. The controller deleted the banner at `0666172` and fixed the two sentences the first whole-branch review had named as now-exposed contradictions. The branch re-review found three more the same afternoon (Setup's Notepad instruction for a page that now has a button; a dead JSON-preview description of a page that ships as sentence cards; a settings reference naming four keys that had moved onto the recipe) — fixed at `84d4c19`, with the lesson recorded then: *a staleness banner is a debt marker, and deleting it without reading what it covered publishes the debt.*

The lesson did not fully take. That evening's own copy tightening (`6ac00e6`) introduced the Troubleshooting table's "Idle — nothing to watch" line — a sentence the app has never printed — into the very file the lesson was about, by the hand that had just written the rule down. Rather than fix a third flagged sentence and call it done, the controller dispatched a dedicated audit against every claim in the README — not the ones a reviewer happened to name — and it found nine more contradictions, including two about what leaves the owner's machine: the README said Ur Score "carries no credential of any kind," when recipe keys live in DPAPI-encrypted `keys.dat`; and said a per-account read "doesn't know your RoRoRo accounts exist," when the profile recipe puts the owner's own Roblox user id straight in the URL — a fact the import screen has always said out loud. The difference between a fix and a debt payment is the word "every": checking the sentences a review names pays off the finding; checking every claim pays off the banner.

**5. A fix can inherit the bug it fixes.**

Task 8 round 1 was right to keep a source's remembered numbers on the board rather than clear them on the first failed read of a session — the alternative, ruled a defect at round 2, was strictly worse than the empty board the feature replaced: open before a battle, see last night's numbers, watch the first read fail, and the board goes blank. But the fix that kept the numbers used the same door a stale-freshness bug was already living in: `Record` stamps `_lastRead[sourceId]` on every snapshot, a failed one included, and nothing about "don't clear the numbers" touched that stamp. So the numbers survived, correctly — and the *timestamp that says a read just happened* survived with them, incorrectly. `Race`'s chart took the surviving stamp at face value and plotted the hours-old remembered total at the current minute, a flat line drawn to "now" for a reading that never occurred; `AccountCard`'s "Last read" fact took the same stamp and told the owner a three-hour-old number was "1m ago." Neither panel was touched by the round that introduced the exposure — both were reading a field that round's own commit never mentioned.

The second whole-branch review caught the chart; the controller's follow-up audit — asked for by name, rather than a single-line patch — caught the account card the same way, because it asked the general question ("what else reads `_lastRead` as though an attempt were a success?") instead of patching the one panel that had been named. A fix that changes what a stamp is allowed to mean has to be checked everywhere that stamp is read, not just where the bug was reported.

### Verification performed

- **Builds and tests at `ddfafc8`:** `dotnet build Ur-Score.csproj -c Release -warnaserror` and `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror` both succeeded with 0 warnings; `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build` gave **936 passed, 0 failed, 0 skipped**. The count walked 862 (Task 1) to 870, 906, 923, then 916 (Task 3, after `RuleInventory`'s seven tests left with it), 917, 933 (Task 6), 935 and 936.
- **Reviews:** nine in all — one per task (1, 2, 3, 4, 6), scoped re-reviews of Tasks 1 and 2, a whole-branch review of `cec5987..8177b8d`, and a scoped re-review of the four fix commits `8177b8d..8e1bdd9`. Task 1's review ran a focused `RulesFileTests` run and pulled RoRoRo's `LocalFileMetricRuleSource.cs` at `dc44992` with `git show` to check the parity claim byte for byte instead of trusting the report. The branch review ran `NoHostnameFenceTests | AvatarFenceTests | AlertsPageFenceTests | RulesFileParityTests` (16/16) and one out-of-repo WPF probe establishing that `Border`/`StackPanel`/`ContentPresenter` get no automation peer. The branch re-review was explicitly asked to re-derive the A13 invariant from the tree rather than take the controller's word, since the controller had got it wrong once already, and to re-check the other three panel surfaces itself, since that same "the others are clean" claim had been made a round earlier and was wrong for My accounts.
- **Mutation tests:** three fences in Task 6 round 1 (`Hidden` to `Collapsed`; `AskForAvatars` computing "yours" differently; `contoso.com` in a `Text=` attribute), one in round 2 (restoring the old dot binding), two in round 3 (each A26 setter to `Collapsed`). Each failed exactly its own test, nothing else moved, all reverted, the gate green after.
- **Byte scans:** every changed file checked for control characters, tabs and BOMs by the task reviews and again by the branch review; `walk-alerts.ps1` confirmed pure ASCII/LF three separate times, most recently by the branch re-review.
- **`walk-alerts.ps1` was parse-checked, never run** (`Parser::ParseFile`: 0 errors, 1730 tokens). Ur Score and RoRoRo were never launched by any task.
- **Not yet performed, at the point the first half of this record was written:** the live walk (Task 4 Step 4), Task 6's live look, and every step of Task 5. All but Task 5 were performed later that evening; see the walk and live-look paragraphs earlier in this record.
- **Tests, second half:** the count continued 936 → 937 (`d7102ae`, +1) → 947 (Task 7, `5124fa2`, +10) → 960 (Task 8, `1ce759e`, +13) → 966 (Task 9, `ace82c8`, +6) → 970 (Task 8 fix round 1, `f922ab3`, +4) → 972 (Task 8 fix round 2, `7ee4afd`, +2) → **974** (Task 8 fix round 3, `4ce08bb`, +2, the branch's count as of this record). Task 7's fix round 1, the controller's copy tightening, Task 7 review's fix, and the README audit are all copy-only and change no test — the 947→960 jump is Task 8's own, landing chronologically before Task 7's Minor fixes (`daa2f59`), which re-confirmed 960 rather than changing it. `dotnet build Ur-Score.csproj -c Release -warnaserror` and the test-project build were re-run clean at every commit.
- **Reviews, second half:** three per-task reviews (7, 8, 9) and a second whole-branch review (`8177b8d..7ee4afd`, which ran `dotnet test` itself and got 972 passed). Task 8's review ran with the working tree already carrying Task 9's uncommitted edits and traced the overlap by hand rather than assuming it; the second whole-branch review read every file both new tasks touched plus `README.md` and `manifest.json` in full, and confirmed the privacy fence held across Tasks 8 and 9 *together* — the thing no per-task review could see.
- **Mutation tests, second half:** three in Task 9 (suppressing the note on a remembered panel; flipping `nameTheRecipe`; merging remembered snapshots into the read map); in Task 8, one in fix round 1's C1 fix (reintroducing `InGroup`'s derived-rank fallback), one in fix round 2 (reverting `SnapshotOf` to `live ?? remembered`, which failed exactly its two intended tests), and two in fix round 3 (reverting the Race-chart fix and the account-card fix in turn) — each failed exactly the test(s) built to catch it, nothing else moved, all reverted, the gate green after.
- **The second half's walks and Task 6's live look**, both run by the controller with RoRoRo up on the owner's own machine: see the walk paragraph and the live-look paragraph earlier in this record.
- **`branch-rereview-2.md` (`7ee4afd..fa3897a`) independently re-verified both fixes**, and ran the suite itself: **974 passed, 0 failed, 0 skipped**. It confirmed the Critical's fix (`4ce08bb`) is not just similar to but *provably equivalent* to the reviewer's suggested guard, given two invariants it checked directly in the tree (`RememberedAt` is set only by `Remembered.From`; `BroughtNumbers(snapshot) == false` implies `Headline is null`) — walking `SnapshotOf`'s three branches against both guards, every branch produces the identical outcome. It confirmed all three README Importants genuinely fixed, including both privacy claims specifically named as risky (the DPAPI `keys.dat` claim against `KeyStore.cs`; the per-account recipe's own-id claim against the profile fixture and `RecipeEngine.ReadPerAccountAsync`). It found two new Minors, both in `docs/backlog.md`'s own bookkeeping and neither in code: the section's restated counts were wrong again by mechanical recount, and `AC-B2.2`-`AC-B2.4` sat OPEN in the same commit that fixed the README sections they name. Both were corrected directly in `docs/backlog.md` while this record was being finished — see Nice-to-haves, below, for the corrected figures.
- **Still not performed, as of this record:** the live look fix round 2 asked for by name — letting a source fail a read, or leave the main clan between battles (`SourceIdle`), and watching the Race chart stop at the last real reading rather than run flat to the right edge — has not been done; it is the one check that would exercise `4ce08bb`'s fix directly rather than by unit test.

### Where the sources disagree

Recorded rather than silently reconciled.

- **Task 4's review, Important 1, is wrong** — see lesson 1. Its two named XAML facts are true; the conclusion drawn from them is not, and the branch review's Critical 1 supersedes it. The branch review's own suggested fix (filter on `IsOffscreen`) is also wrong, for the same reason: these elements are `Collapsed` and absent from the tree, not present-but-offscreen.
- **Task 2's Minor 7** (a 20-minute rule's extra choice is lost after a refused Save) is counted among "the other nine parked" in the ledger, but ruling 3 removed the extra choice from `MinuteChoices` entirely, so the scenario it describes can no longer occur. It is recorded **GONE** in the backlog, with the replacement cost — the empty minutes box — carried as its own open row (`AC-2.13`).
- **Task 2's Minor 5** is recorded Fixed by the Task 2 re-review and half-closed by the branch review's Minor 12: `Placeless` drops `Index` but keeps the rule's own `Label`, so a hand edit to a label alone still redraws and drops focus. Both are in the backlog — the fix as `AC-2.5` (fixed), the residue as `AC-B.12` (open).
- **The branch re-review reports "Minor count: 2"**, but its second item states outright that it is not a new finding — it confirms Task 6's Minor 4 (no icon-cache pruning) still stands. One distinct new Minor, not two.
- **The backlog's README rows were stale in the other direction, and it was caught twice.** While writing this record, `AC-B2.2`, `AC-B2.3` and `AC-B2.4` were still recorded **OPEN** in `docs/backlog.md` even though the README audit that added those very rows (`fa3897a`) had rewritten all three sections in the same commit — checked directly against the tree, which then showed the icon.png contradiction gone, "What it does" rewritten in recipe terms, and the Troubleshooting table's strings matching `DiagnosticsModel.StateText` verbatim. `branch-rereview-2.md` independently found the identical gap (its Minor 2) rather than taking either agent's word for it, alongside a second bookkeeping Minor — the section's own restated totals were wrong again by mechanical recount (90 rows, not 88, by a plain count of every status marker). The controller then recounted the file directly rather than trust either count, and corrected both in `docs/backlog.md`: `AC-B2.2`-`AC-B2.4` now read **FIXED**, and the section's header states **56 OPEN, 32 FIXED, 2 GONE — 90 distinct items**, which is the figure Nice-to-haves, below, now reports. (One stale clause remains in the header paragraph's own prose — "the three README Importants … are still open" — trailing behind the row statuses it once matched; not fixed here, since this record does not edit `docs/backlog.md`.)
- **Task 8's first report overstated its own privacy claim.** It said remembered rows are "re-filtered against RoRoRo's current account list"; the review's Important 2 found this true only *after* the first account listing, not at the window's first draw, when the filter still ran against the disk cache. Fixed at fix round 1 (`OpenOnLastNumbersAsync` now asks before the first fill) — kept here as the reminder the controller gave at the time: an implementer's own verification claim is a claim, not a proof.

### Nice-to-haves (every review Minor)

Every Minor finding from thirteen of the fourteen reviews is written out row by row in `docs/backlog.md`, under **"0.3.2: the Alerts page and avatars"**, with ids `AC-1.*`, `AC-2.*`, `AC-3.*`, `AC-4.*`, `AC-6.*`, `AC-7.*`, `AC-8.*`, `AC-9.*` (per task), `AC-B.*` (the first whole-branch review), `AC-BR.*` (the first branch re-review) and `AC-B2.*` (the second whole-branch review). Each row keeps what the finding is, the `file:line` where the review gave one, whether it is open or was closed during the cycle, and the commit that closed it. The fourteenth, `branch-rereview-2.md`, raised its two Minors against `docs/backlog.md` itself rather than against `src/`; both were resolved by directly correcting the file (see Where the sources disagree, above) rather than by adding `AC-BR2.*` rows, so no such prefix exists.

**90 distinct items: 56 open, 32 fixed during the cycle, 2 gone.** This is `docs/backlog.md`'s own current figure, per its 0.3.2 section header at the point this record was finished — and it changed twice while this record was being written. The section was first extended past `ddfafc8` to `cec5987..4ce08bb` (the full cycle) with `AC-7.*`/`AC-8.*`/`AC-9.*`/`AC-B2.*` rows added, stating **58 OPEN, 28 FIXED, 2 GONE — 88 distinct items**; `branch-rereview-2.md` then found that figure itself wrong by mechanical recount (90 rows, not 88) and found `AC-B2.2`-`AC-B2.4` marked OPEN despite being fixed in the same commit that added them (see Where the sources disagree, above); the controller then recounted the file directly and corrected both, landing on the **56/32/2/90** figure this paragraph reports. Fourteen reviews raised the raw findings between them; five are restatements across reviews (Task 3's Minor 6 and the branch review's Minor 11; the Task 2 re-review's minutes-box item and the branch review's Minor 5; Task 1's re-review restating two of Task 1's own; the branch re-review's second item restating Task 6's Minor 4), merged into one row each with both sources named.

Per review, first half (as originally recorded, before the recount): Task 1 six (3 open), Task 2 thirteen (9 open, 1 gone), Task 3 eight (6 open), Task 4 two (2 open), Task 6 thirteen (4 open), the first whole-branch review ten (8 open), the first branch re-review one (0 open). Per review, second half (verified directly against `docs/backlog.md`'s own rows as of this record): Task 7 two, both fixed (0 open); Task 8 thirteen (10 open, 3 fixed); Task 9 four (3 open, 1 gone); the second whole-branch review seventeen, now 6 fixed and 11 open (`AC-B2.1`, `AC-B2.2`, `AC-B2.3`, `AC-B2.4`, `AC-B2.5` and `AC-B2.8` fixed; the rest open) since `AC-B2.2`-`AC-B2.4` moved from open to fixed partway through this record being written. These per-review counts, taken from a file that was being corrected while this record was written, will not sum cleanly to either 88 or 90 without re-deriving every row's status at one fixed instant, which this record does not attempt a third time. The **56/32/2/90** headline, current as of the last read while finishing this record, is the one to trust; re-read `docs/backlog.md` directly for anything past this point.

The four with a decision still to make from the first half: **`AC-6.4`** (nothing prunes the icon cache — it will make Step 11 item 5 read as a failure for anyone who has ever removed an account), **`AC-B.1`** (three backlog rows this branch invalidated, which Task 5's release step already instructs), **`AC-B.2`** and **`AC-B.3`** (three earlier design docs and the approved design itself now describe a page that no longer exists — banner-correct them, do not rewrite). A fifth surfaced in the second half: **`AC-B2.16`** — `activeRecipe` is a setting serialised into `settings.json` and read or written by nothing in `src/`; the controller had documented it as "set by Setup › Recipes," which was never true. Wire it or drop it.
