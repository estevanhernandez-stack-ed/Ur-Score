# Default views: a Battle tab and an Alts tab — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ur Score v0.3.1 opens with two tabs that are already right for the clan battle on Saturday 2026-09-19: **Battle** (standings, top of the battle, the race, the promotion check, your accounts, past battles and records, in rows that fill 12 columns) and **Alts** (a new Accounts table of your own accounts side by side, sortable, with totals and change, beside Records and an Account card that shows the row you pick). Each tab keeps following your sources until you change that tab. The profile recipe gains named stats (rebirths, playtime, distinct pets hatched and more) with counts, durations and dates, and suggests which to show on a first import.

**Architecture:**
- **Recipe format (`src/Recipes`):** a value gains `count` (entries of an object or list), `format` (`number`, `duration`, `date`), `show` (a suggested Show tick on a first import) and `section` (the account card's heading). Parser, validation, engine and update screen learn them; the profile fixture names the new stats.
- **Pure board model (`src/Board`):** `PanelText.Value/Change` write durations and dates; `PanelModels.AccountsTable` builds the table (sort, totals, change, private rows); `PanelModels.AccountCard` shows a picked account in sections; `StarterBoards` builds Battle and Alts in rows that close up; `Following` decides which tabs follow and what `boards.json` holds after a change.
- **WPF, kept thin:** `AccountsTablePanel` is a themed `DataGrid` that raises a sort and a pick; `BoardWindow.Accounts.cs` keeps both for the session only and hands them to the panels as a `PanelSession`. `AppServices.Boards` and `SaveBoards` go through `Following`.

**Tech Stack:** .NET 10 WPF (`net10.0-windows`), xUnit 2.9, System.Text.Json. No new packages.

**Spec:** `docs/2026-09-15-default-views-design.md` (approved 2026-09-15, binding). It builds on `docs/2026-09-14-score-book-design.md` (§9.2 boards, §9.4 panels) and `docs/2026-09-13-stats-games-icons-design.md` (§2 principle, §3.2 value fields, §7 import), and keeps the stage 1 and stage 2 plans' rulings (R1–R21) and stage 2's execution-record rulings except where a ruling below extends them.

**Branch:** `feat/default-views` (at `9218049` = master v0.3.0 plus the design). **Deadline:** built, reviewed, walked and ready for the owner's release OK before Saturday 2026-09-19.

## Rulings made while planning

Recorded so a reviewer doesn't read them as drift. Each names what was decided, why, and the cost if it is wrong.

**Tabs and following**

- **D1. Fixed ids per starter tab** (extends R2). A following tab's board id is `b-starter-battle` / `b-starter-alts`; its panels are `p-battle-1`, `p-battle-2`… and `p-alts-1`… in order. Materializing keeps them, so a pop-out made on a following tab survives the write. A starter added with + Board gets random ids. *Cost if wrong:* none known; ids are opaque and `BoardsFile` repairs duplicates.
- **D2. Each tab follows on its own** (extends R1, the design's "until you change that tab"). `BoardDef` gains `Follows` ("battle", "alts" or null) and `boards.json` a board field `follows`. A following board is written as `{ "id", "name", "follows", "panels": [] }`; its panels are rebuilt from your sources on every read. The first change to a tab (a panel added, removed, moved, resized or re-set, a rename, a pop-out) is detected by comparing it with the tab as your sources build it now (`BoardEdits.Changed`); that tab is written as it is and follows no more, and every other following tab stays a following entry. A following tab that was on screen and is gone was deleted: it doesn't come back (+ Board offers it). One that was hidden (nothing to show) keeps following. *Why:* writing both tabs on the first change (the stage 2 behaviour) would freeze Battle the moment you tidy Alts. *Cost if wrong:* `boards.json` has one more optional field; a downgrade to 0.3.0 shows a following entry as an empty board with its starter's name.
- **D3. Migration.** No `boards.json` (every 0.3.0 user still following the single starter, the owner included): both tabs follow, nothing to migrate, because 0.3.0 never wrote the following starter. A `boards.json` saved by 0.3.0: its boards stay exactly as saved (their `b-starter`/`p-starter-N` ids too) and no tab is added; + Board offers Battle and Alts. *Cost if wrong:* a 0.3.0 user who had already saved boards adds Alts with one click.
- **D4. A tab appears only when its starter has panels.** When no starter has any (no recipe, no ticked stat, no source), one board shows today's first-run empty state, on the first following starter that is asking for something: Import a recipe, then Choose your main clan, else No stats turned on yet (`StarterBoards.EmptyState`). *Cost if wrong:* someone with a ticked profile recipe and a clan recipe with no main sees only Alts until they pick a main (first run opens Setup › Clans anyway).
- **D5. Grind goes.** `StarterBoards.Grind`, its builder and + Board's Grind button are replaced by Alts. Saved boards named Grind are untouched. *Cost if wrong:* rebuild it from the gallery.
- *(Changed during execution: Battle now uses the mock's three rows — standings · race / My accounts · Promotion · Top / Past battles · Records — see the design doc and the execution record.)*
- **D6. Rows that close up.** Each starter is written as rows with their full spans; when some panels of a row can't be built, the rest share the 12 columns equally (1 → 12, 2 → 6 + 6, 3 → 4 + 4 + 4). Battle: [Standing (main) 4 · Standing (a clan your accounts are in) 4 · Top 4], [Race 8 · Promotion check 4], [My accounts 12], [Past battles 7 · Records 5]. Alts: [Accounts table 12], [Records 5 · Account card 7]. The rows fill at the default window width (1280) and anything at or above 1100; narrower windows keep stage 1's reflow and may leave a half-width panel alone on a line. *Cost if wrong:* an unequal pair becomes equal halves when its partner is missing.
- *(Changed during execution: panels keep their natural height, top-aligned in their row, as the mock does; tall panels still span two rows. See the execution record.)*
- **D7. A one-row panel is arranged as tall as its row** (backlog S1-14.11, which the backlog remediation plan put in its Task 1 as P11): `BoardLayout.CellHeight` and one line in `PanelGrid`. *Why:* the design's "no gaps"; Records beside a taller Account card would otherwise end short. *Cost if wrong:* none; the remediation plan's Task 0 re-check finds it done.
- **D8. Battle has no Account card** (the design's Battle list). Walks that popped out `AccountCardPanel1` on the Battle tab use `RecordsPanel1`.

**Recipe format**

- **D9. Counting entries is a value field, `"count": true`**, not a path function: the number is how many entries the object or list at `path` holds (`{}` is 0; anything else is a miss that names the path). *Why:* a path stays a path, and a count describes the data (§2). Keys are never read, so pet or zone names can't leak. *Cost if wrong:* a count of something nested deeper needs another field later.
- **D10. Durations and dates are a value field, `"format": "number" | "duration" | "date"`.** Duration is seconds, date is unix seconds; the book keeps plain numbers. A count's format is number, and a date needs `"sum": false` (both are validation problems). `PanelText.Value` writes a duration as its two largest units ("586d 5h", "5h 12m", "12m") and a date as "13 Sep 2020" in local time; `PanelText.Change` writes a duration change "+2h 0m" and a date has no change (a dash). Formats are honoured on the Accounts table, the Account card and Profile stat; Records, My accounts, Promotion check and Past periods keep numbers. *Cost if wrong:* a Records panel set to Playtime shows seconds.
- **D11. Suggested Show ticks are a value field, `"show": true`,** applied only on a first import (no installed recipe of that slug), only as Show, never Send. The import screen shows the boxes ticked and a line saying the recipe suggests them and that nothing is sent unless you tick Send; you can untick before Import. Updates keep stats design §7.2 (a new stat is listed unticked), and a user's saved ticks are never touched. Backlog V3-S.2 (updating a pre-0.3.0 state starts unticked) is not fixed here. This amends stats design §2's "Nothing is ticked by default" to "nothing is sent by default": a suggestion is visible before anything runs and reaches no one; Task 2 adds a banner to that design. *Cost if wrong:* the owner, who has the profile recipe installed, ticks the four new default stats once on the update screen or in Setup › Stats.
- **D12. Card sections are a value field, `"section"` (text).** A picked counter's section is the counters' own label ("Game statistics"). The card shows your Show-ticked stats other than its big one (only tracked stats are read), stats with no section first with no heading, then sections in the order the recipe first names them, counters last. *Cost if wrong:* to see slots on the card you tick them, which also adds table columns.
- **D13. The profile recipe's named stats** keep `diamonds`, `eggs`, `rank` first with their ids, paths and metric ids unchanged (so saved ticks, pinned names, `FirstStat` and the book stay valid), then add rebirths, rank stars, distinct pets hatched (count), goals completed, achievements (count), zones unlocked (count), playtime (duration), sessions, first joined (date), booth diamonds earned, booth slots, egg slots bought and pet slots bought. `show` on rank, rebirths, diamonds, eggs hatched, distinct pets hatched, goals completed and playtime (the design's list). `sum: false` on rank, rank stars, rebirths, distinct pets, achievements, zones, first joined and the three slots. Table columns follow recipe order. Robux spent and login count stay out (the design). *Cost if wrong:* Player rank is the third column, not the first; reordering is a recipe edit.

**The Accounts table and the card**

- **D14. The Accounts table is a themed `DataGrid`, not a `ui:RowList`** (extends the execution-record rule "every list in `src/UI` is a `ui:RowList`": a table of cells is not a list of row controls; Live leaderboard is the precedent). It gives horizontal scrolling with a frozen account column, heading clicks, arrow-key row selection and UI Automation's grid, header and data-item elements. It sits transparent on the panel card, grows to all its rows, and passes the mouse wheel to the board. Your own accounts may be copied (`ClipboardCopyMode` IncludeHeader). *Cost if wrong:* rebuild as a RowList in a ScrollViewer and lose the frozen column.
- **D15. Sort is this session's only**, per panel id, held by the board window; never written, so a heading click never stops a tab following. Default: the first shown stat, highest first. A missing value sorts last either way; ties by name. Clicking the sorted heading flips it; another stat sorts highest first; Account sorts A to Z. A sort on a stat no longer shown falls back to the default. The sorted heading ends in ↓ or ↑. *Cost if wrong:* the sort resets at each start.
- **D16. The sorted column's change** is two columns right after it, Today and 7 days, from the score book with Profile stat's window rule (a shorter history states its span, "+1.2M in 3h"); a dash with fewer than two readings. None while sorted by Account or by a date.
- **D17. Rows and totals.** One row per account RoRoRo lists with a Roblox id (id 0 skipped, as Profile stat). An account the read called unavailable shows the recipe's own message under its name, with dashes. Before the first read, dashes and the note "Waiting for the first read."; with no Show tick, "Tick Show on a stat to fill this panel." A last row "Total" sums each stat with `sum` true that isn't a date, over accounts with a value (a dash when none has one); every other cell of that row is blank, including Today and 7 days. A pinned source that is gone is stale; an unpinned panel reads the recipe's first source that is on, else its first (backlog P17's rule). *Cost if wrong:* no today total across accounts.
- **D18. The picked account is this session's only, per board.** Selecting a row (click or arrow keys, also in a popped-out table) picks it; every Account card on that board set to "Your top account" shows it; a card pinned to an account stays pinned. A picked account RoRoRo no longer lists falls back to the top account. A picked account with no reading says why: the recipe's unavailable message, else "No reading of {name} yet." *Cost if wrong:* the pick resets at each start.
- **D19. The Accounts table is in the gallery**, after Profile stat: it fits a recipe without a period, adding asks for its source, ⋯ asks for nothing more, it keeps no stat, its default size is Wide, and its title is "Accounts table". *Cost if wrong:* one more gallery card.

**Release and polish**

- **D20. Version 0.3.1 is this work** (the design). The backlog remediation plan also says 0.3.1; its wave 1 becomes 0.3.2, and its Task 0 re-check covers this branch (S1-14.11 closed here by design, as panels keep their natural height per the Task 8 ruling, not stretched; `StarterBoards`, `AccountCard` and `ProfileStat` changed). *Cost if wrong:* a version number in that plan.
- **D21. Visual fidelity stays inside the theme.** The theme paints nine brushes and has no separate page and panel tones, no green and no amber (stage 1: cyan is good, magenta is warning). Task 8 matches the mock through type, size, weight, spacing, padding, alignment, density and dividers only, in the files it names; panel titles are shown upper case while their accessible names stay sentence case. *Cost if wrong:* the mock's two-tone ground and green deltas aren't matched.
- **D22. Smoke walks can start an installed copy** (backlog V3-S.5): `uia.ps1` takes `UR_SCORE_EXE` when set, so the release step walks the copy RoRoRo installed. *Cost if wrong:* none.

---

## Global Constraints

Carried from stages 1 and 2 (`docs/plans/2026-09-14-score-book-stage-1.md`, `…-stage-2.md`):

- **No hostname literal in `src/`** except `NameClient.cs` (users.roblox.com) and `IconClient.cs` (thumbnails.roblox.com). `NoHostnameFenceTests` enforces it. Recipes and fixtures carry hosts; code never does.
- **Other players never reach disk.** Another player's Roblox id, name or value is never written to state, `sources.json`, `accounts.json`, `boards.json`, the score book, the trail, diagnostics or the clipboard. The Alts tab only ever shows your own accounts.
- **Keys never reach disk.** `Redactor` masks keys in any text that could carry one.
- **One path to RoRoRo.** `ReportPolicy.SendAsync` is the only caller of `IHostClient.ReportMetricAsync`.
- **Theme.** Themed brushes are referenced with `DynamicResource` only. No hex colours or literal brushes in `src/UI` XAML (`Transparent` is allowed), and no colour code in UI `.cs` (`ThemeFenceTests`). Every brush used is one `ThemeService` paints: `BgBrush`, `CyanBrush`, `MagentaBrush`, `WhiteBrush`, `MutedTextBrush`, `DividerBrush`, `RowBgBrush`, `RowHoverBrush`, `EdgeBrush`.
- **Ur Score's own text never names a game.** Words like "clan", section names and stat labels come from the recipe (`RecipeWords`, labels).
- **Copy style:** sentence case, second person, specific, no emoji.
- **Group-list recipes** are never matched to accounts, never sent, never recorded. **`watch` sources** send nothing and record no accounts.
- **Requests:** at most one request at a time per host, at least 2 s apart.
- **Build gate:** `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror` passes, and `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build` passes. Run both at the end of every task. Nullable warnings are errors.
- **Commits:** one or more per task, message style `area: what changed`, ending with the line `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.
- **`boards.json` holds no other player** (R17). **Pure first:** logic in `src/Board`, `src/Recipes` and the table models, with unit tests; windows decide nothing a test can't see.
- **Automation ids** that `tools/smoke` relies on change only with their scripts, in the same commit.
- **`System.IO` is not an implicit using in the app project**; a `src/` file using `File`, `Directory` or `Path` needs `using System.IO;`. In `Labs626.UrScore.UI`, `.Board`, `.Book` or `.Composition` files, `using Source = Labs626.UrScore.Core.Source;` goes after the file-scoped `namespace` line.
- **A running Ur Score locks `bin\Release`.** Close it before building.

Carried from the stage 2 execution record:

- Board-window button state is decided only by `BoardButtons.For` and applied by `ApplyButtons`.
- One save path for boards (`BoardWindow.SaveBoards` → `AppServices.SaveBoards`): a failed board save shows a message owned by the board; a failed pop-out position save goes to the trail only.
- Edits that change nothing return the same instance and write nothing. Handlers re-read the boards after a dialog closes.
- Titles and a source's role label live in `PanelText`, shared by `PanelModels`, the gallery and the forms.
- Every popup and toast is themed.

Added for this plan:

- **`boards.json` changes by one optional board field, `follows`** (D2). `sources.json`, `accounts.json`, `settings.json`, recipe state files and book lines keep their shapes. A recipe format 1 file gains four optional value fields (D9–D12); a recipe without them reads exactly as before.
- **Session state is never written:** a table's sort and a board's picked account live in `BoardWindow` memory only (D15, D18).
- **Smoke walks are the controller's.** Each UI task names the walk steps; the controller runs them against RoRoRo 1.28 before release. Walk scripts are ASCII only (Windows PowerShell 5.1 reads BOM-less files as ANSI): a non-ASCII character is written `[char]0x2193`.
- **Tests name fixture accounts by reference** where the fixture offers it (`Main`, `AltOne`, `AltTwo`, `Loose` in `BoardFixtures`); display-name strings already in `BoardFixtures` may be asserted.
- **No literal control characters** in code: write `(char)0x1F`. Printable characters such as `↓`, `·` and `—` may be written as themselves in C#, XAML and JSON (never in a walk script).
- **Merging, tagging and releasing are never pre-authorized.** Each waits for the owner's OK in the session.

---

## File structure

New files:

| File | Responsibility |
|---|---|
| `src/Board/AccountsTableModel.cs` | `AccountColumnKind`, `AccountColumn`, `AccountSort`, `AccountRow`, `AccountsTableModel`, `PanelSession` |
| `src/Board/Following.cs` | Which starter tabs follow and show (`Shown`), and what `boards.json` holds after a change (`ToSave`) |
| `src/UI/Panels/AccountsTablePanel.xaml(.cs)` | The Accounts table control; raises `SortEvent` and `PickEvent` |
| `src/UI/BoardWindow.Accounts.cs` | The session's sorts and picks, and each panel's `PanelSession` |
| `src/UI/Controls/UpperCase.cs` | A converter that shows a title upper case (Task 8) |
| `tests/FollowingTests.cs` | `Following` |
| `tools/smoke/walk-alts.ps1` | The Alts tab walk |

Modified files, by task:

| Task | Files |
|---|---|
| 1 | `src/Recipes/RecipeFormat.cs`, `Recipe.cs`, `RecipeStats.cs`, `RecipeParser.cs`, `RecipeEngine.cs`, `ImportReview.cs`; `tests/Fixtures/petsim99-profile.recipe.json`; tests `RecipeParserTests`, `RecipeEngineTests`, `ImportReviewTests`, `RecipeStatsTests`, `StatsTableModelTests` |
| 2 | `src/UI/Controls/StatsTableModel.cs`, `src/UI/Controls/StatsTable.xaml.cs`, `src/UI/ImportWindow.xaml(.cs)`, `src/UI/ImportText.cs`, `docs/2026-09-13-stats-games-icons-design.md`; tests `StatsTableModelTests`, `ImportTextTests` |
| 3 | `src/Board/PanelText.cs`, `src/Board/PanelModels.cs`, `src/UI/Panels/AccountCardPanel.xaml`; tests `PanelTextTests`, `PanelModelsTests` |
| 4 | `src/Board/PanelModels.cs`, `PanelText.cs`, `PanelForms.cs`, `PanelGallery.cs`, `BoardDefs.cs`, `src/UI/Boards/PanelSettingsWindow.xaml.cs`; tests `PanelModelsTests`, `PanelFormsTests`, `PanelGalleryTests` |
| 5 | `src/UI/Panels/PanelViews.cs`, `src/UI/BoardWindow.xaml.cs`, `src/UI/BoardWindow.PopOuts.cs`, `src/App.xaml` |
| 6 | `src/Board/StarterBoards.cs`, `src/Board/BoardLayout.cs`, `src/UI/Controls/PanelGrid.cs`, `src/UI/Boards/AddBoardWindow.xaml(.cs)`; tests `StarterBoardsTests`, `BoardLayoutTests` |
| 7 | `src/Board/BoardDefs.cs`, `BoardsFile.cs`, `BoardEdits.cs`, `src/UI/BoardText.cs`, `src/UI/BoardWindow.xaml.cs`, `src/Composition/AppServices.cs`; tests `BoardsFileTests`, `BoardEditsTests`, `BoardTextTests` |
| 8 | `src/App.xaml` (panel styles only), `src/UI/BoardWindow.xaml`, `src/UI/Panels/*.xaml` |
| 9 | `tools/smoke/uia.ps1`, `uia-board.ps1`, `walk-starter-board.ps1`, `walk-stats-table.ps1`, `walk-pop-outs.ps1`, `check-boards-privacy.ps1`, `README.md` |
| Release | `manifest.json`, `Ur-Score.csproj`, `CHANGELOG.md`, `docs/backlog.md`, this plan (execution record) |

## Interface contract

Every task implements, or relies on, exactly these names. Everything else keeps the name it has at `9218049`. Namespaces: `Labs626.UrScore.Recipes`, `Labs626.UrScore.Board`, `Labs626.UrScore.UI`, `Labs626.UrScore.Composition`.

```csharp
// ---- Task 1: format (Labs626.UrScore.Recipes) ----
public enum StatFormat { Number, Duration, Date }                                                   // RecipeFormat.cs
public sealed record RecipeValue(string Id, string Label, string Path, string MetricId, bool Sum = true,
    bool Count = false, StatFormat Format = StatFormat.Number, bool Show = false, string? Section = null);
public sealed record RecipeStat(string Key, string Label, string Path, string SuggestedMetricId, bool Sum,
    bool Count = false, StatFormat Format = StatFormat.Number, string? Section = null);             // a counter's Section is its counters' Label
// RecipeEngine: a value with Count reads the number of entries of the object or list at its path.
// ImportReview.CompareToInstalled lists "{Label} now counts entries." / "{Label} no longer counts entries." /
//   "{Label} is shown as a duration instead of a number." without asking.

// ---- Task 2: suggested ticks (Labs626.UrScore.UI) ----
public static class StatsTableModel { public static IReadOnlyDictionary<string, StatChoice> Suggested(Recipe recipe); }
// StatsTable.Load gains a last parameter:  IReadOnlyDictionary<string, StatChoice>? startTicks = null
public static class ImportText { public static string SuggestedNote(Recipe recipe); }                // "" when the recipe suggests none

// ---- Task 3: formats and the card (Labs626.UrScore.Board) ----
public static class PanelText
{
    public static string Value(double? value, StatFormat format, TimeZoneInfo zone);  // number "14,020,550"; duration "586d 5h"; date "13 Sep 2020"; null a dash
    public static string Change(double? value, StatFormat format);                    // number = Signed; duration "+2h 0m"; date a dash
    public static string Duration(double seconds);
}
public sealed record CardSection(string Heading, IReadOnlyList<FactModel> Facts) { public bool HasHeading { get; } }
public sealed record AccountCardModel(PanelHead Head, string BigLabel, string Big, IReadOnlyList<CardSection> Sections,
    IReadOnlyList<ChartSeries> Line, IReadOnlyList<FactModel> Facts, string ChartName);             // Sections replaces Numbers
// PanelModels.AccountCard(LiveBoard live, ScoreBookReader reader, PanelSettings settings, long? pickedUserId = null)

// ---- Task 4: the table model (Labs626.UrScore.Board) ----
// PanelType gains AccountsTable (last member).
public enum AccountColumnKind { Name, Stat, Today, Week }
public sealed record AccountColumn(string Key, string Header, AccountColumnKind Kind, bool Sorted = false, bool Descending = false)
{ public bool CanSort { get; } public string Heading { get; } }                                     // Heading: "Diamonds ↓" when sorted
public sealed record AccountSort(string Key, bool Descending)
{ public const string NameKey = ""; public static AccountSort Clicked(AccountColumn column); }
public sealed record AccountRow(long UserId, string Name, IReadOnlyList<string> Cells, string Note, bool Missing, bool Picked, bool IsTotal = false)
{ public bool HasNote { get; } public override string ToString(); }                                // ToString is Name
public sealed record AccountsTableModel(PanelHead Head, IReadOnlyList<AccountColumn> Columns, IReadOnlyList<AccountRow> Rows);
public sealed record PanelSession(AccountSort? Sort = null, long? PickedUserId = null);
// PanelModels.AccountsTable(LiveBoard live, ScoreBookReader reader, PanelSettings settings, AccountSort? sort = null, long? pickedUserId = null)

// ---- Task 5: the panel (Labs626.UrScore.UI) ----
public sealed class AccountSortEventArgs(RoutedEvent routedEvent, AccountColumn column) : RoutedEventArgs(routedEvent) { public AccountColumn Column { get; } }
public sealed class AccountPickEventArgs(RoutedEvent routedEvent, long userId) : RoutedEventArgs(routedEvent) { public long UserId { get; } }
public partial class AccountsTablePanel : UserControl
{ public static readonly RoutedEvent SortEvent, PickEvent; public void Render(AccountsTableModel model); }
// PanelViews.Render gains a last parameter:  PanelSession session

// ---- Task 6: starters (Labs626.UrScore.Board) ----
public static class StarterBoards
{
    public const string Battle = "Battle", Alts = "Alts";                                          // Grind is gone
    public static IReadOnlyList<string> Names { get; }                                              // [Battle, Alts]
    public static string KeyOf(string name);                                                        // "battle"
    public static IReadOnlyList<StarterBoard> All(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources);
    public static StarterBoard? Named(IReadOnlyList<StarterBoard> starters, string? key);
    public static StarterBoard EmptyState(IReadOnlyList<StarterBoard> starters);
    public static StarterBoard Build(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources, string name = Battle);
    public static string? FirstStat(InstalledRecipe installed);                                     // unchanged
}
public static class BoardLayout { public static double CellHeight(PanelPlacement placement, IReadOnlyList<double> rows, double gap); }

// ---- Task 7: following (Labs626.UrScore.Board, .UI, .Composition) ----
public sealed record BoardDef(string Id, string Name, IReadOnlyList<PanelDef> Panels, string? Follows = null);
public static class BoardDefs
{
    public static string StarterBoardId(string starterName);                                        // replaces the const: "b-starter-battle"
    public static BoardDef FromStarter(StarterBoard starter, bool freshIds);                        // freshIds false: "b-starter-alts", "p-alts-1"...; Follows null
    public static BoardDef Following(StarterBoard starter);                                         // FromStarter(fixed ids) with Follows = KeyOf(name)
}
public static class Following
{
    public static IReadOnlyList<BoardDef> Shown(IReadOnlyList<BoardDef>? saved, IReadOnlyList<StarterBoard> starters);
    public static IReadOnlyList<BoardDef> ToSave(IReadOnlyList<BoardDef>? saved, IReadOnlyList<StarterBoard> starters, IReadOnlyList<BoardDef> edited);
}
// BoardText.EmptyFor(IReadOnlyList<StarterBoard> starters, BoardDef board, bool editing = false)   replaces the 3-argument form
// BoardEdits.ForFirstSave and AppServices.BoardsFollowStarter are removed (Following covers both).
```

## Automation ids

Stage 1's and stage 2's tables still hold. This plan adds or changes:

| Where | Automation ids |
|---|---|
| Board | Tabs named `Battle` and `Alts`; panels `AccountsTablePanel<n>` |
| Inside an Accounts table panel | `AccountsGrid` (a DataGrid named "Your accounts side by side"); its header items are named by heading ("Account", "Diamonds ↓", "Today", "7 days", …); its data items are named by account display name, the last `Total` |
| Add a board (`Add a board`) | `AltsBoardButton` replaces `GrindBoardButton` |
| Import recipe / Update recipe | `SuggestedLine` |
| Board, a popped-out Records panel's slot | a button named `Bring back Records` |

---

### Task 1: Counts, durations, dates, sections and suggestions in the recipe format, and the profile recipe's named stats

**Files:**
- Modify: `src/Recipes/RecipeFormat.cs`, `src/Recipes/Recipe.cs` (`RecipeValue`), `src/Recipes/RecipeStats.cs` (`RecipeStat`, `Find`), `src/Recipes/RecipeParser.cs` (`ParseValues`), `src/Recipes/RecipeEngine.cs` (three `NumberAt` call sites), `src/Recipes/ImportReview.cs` (`CompareToInstalled`)
- Modify: `tests/Fixtures/petsim99-profile.recipe.json`
- Test: `tests/RecipeParserTests.cs`, `tests/RecipeEngineTests.cs`, `tests/ImportReviewTests.cs`, `tests/RecipeStatsTests.cs`, `tests/StatsTableModelTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `StatFormat`; `RecipeValue.Count/Format/Show/Section`; `RecipeStat.Count/Format/Section`; `RecipeParserTests.ProfileIds` (the fixture's 16 value ids in order, for other tests).

- [ ] **Step 1: Write the failing parser tests**

In `tests/RecipeParserTests.cs`, add after `OnePerAccountStep`:

```csharp
    /// <summary>The profile fixture's value ids, in recipe order (D13). Other tests use it so a new stat changes one list.</summary>
    internal static readonly string[] ProfileIds =
    [
        "diamonds", "eggs", "rank", "rebirths", "rank-stars", "pets", "goals", "achievements", "zones",
        "playtime", "sessions", "first-join", "booth-diamonds", "booth-slots", "egg-slots", "pet-slots",
    ];

    /// <summary>A valid per-account recipe whose last step lists these values.</summary>
    private static string Values(string values) =>
        With($$"""[{ "url": "https://example.com/u/{userId}", "perAccount": true, "values": [ {{values}} ] }]""");
```

and these tests at the end of the class:

```csharp
    [Fact]
    public void AValueCanCountEntriesReadAsTimeSuggestShowAndNameItsSection()
    {
        var result = RecipeParser.Parse(Values("""
            { "id": "pets", "label": "Pets", "path": "p", "metricId": "t.pets", "count": true, "sum": false, "show": true, "section": " Progression " },
            { "id": "time", "label": "Time", "path": "t", "metricId": "t.time", "format": "duration" },
            { "id": "joined", "label": "Joined", "path": "j", "metricId": "t.joined", "format": "date", "sum": false },
            { "id": "plain", "label": "Plain", "path": "n", "metricId": "t.plain", "format": null, "section": null }
            """));

        Assert.True(result.Ok, string.Join(" | ", result.Problems));
        Assert.Equal(
            new[]
            {
                new RecipeValue("pets", "Pets", "p", "t.pets", Sum: false, Count: true, Show: true, Section: "Progression"),
                new RecipeValue("time", "Time", "t", "t.time", Format: StatFormat.Duration),
                new RecipeValue("joined", "Joined", "j", "t.joined", Sum: false, Format: StatFormat.Date),
                new RecipeValue("plain", "Plain", "n", "t.plain"),
            },
            result.Recipe!.LastStep.Values);
    }

    [Theory]
    [InlineData("\"format\": \"minutes\"", "'format' in step 1's value 1 must be \"number\", \"duration\" or \"date\".")]
    [InlineData("\"format\": 3", "'format' in step 1's value 1 must be \"number\", \"duration\" or \"date\".")]
    [InlineData("\"count\": \"yes\"", "'count' in step 1's value 1 must be true or false.")]
    [InlineData("\"show\": 1", "'show' in step 1's value 1 must be true or false.")]
    [InlineData("\"section\": 4", "'section' in step 1's value 1 must be text.")]
    [InlineData("\"section\": \"  \"", "'section' in step 1's value 1 must be text.")]
    [InlineData("\"count\": true, \"format\": \"duration\"", "Step 1's value 1 counts entries, so its format can only be \"number\".")]
    [InlineData("\"format\": \"date\"", "Step 1's value 1 is a date, and dates can't be added up. Give it \"sum\": false.")]
    public void AValuesNewFieldsNameTheirProblem(string field, string problem) =>
        Assert.Contains(problem, Problems(Values($$"""{ "id": "a", "label": "A", "path": "a", "metricId": "t.a", {{field}} }""")));
```

- [ ] **Step 2: Write the failing engine test**

In `tests/RecipeEngineTests.cs`, add:

```csharp
    [Fact]
    public async Task ACountingStatReadsHowManyEntriesItsObjectOrListHolds()
    {
        var recipe = Parse("""
            { "recipe": 1, "name": "Counts", "credit": "Test.", "everySeconds": 60,
              "steps": [ { "url": "https://example.test/u/{userId}", "perAccount": true,
                "values": [
                  { "id": "pets", "label": "Pets", "path": "data.Pets", "metricId": "t.pets", "count": true, "sum": false },
                  { "id": "zones", "label": "Zones", "path": "data.Zones", "metricId": "t.zones", "count": true, "sum": false } ] } ] }
            """);
        var transport = new FakeTransport()
            .On("https://example.test/u/1", 200, """{ "data": { "Pets": { "Cat": 3, "Dog": 1 }, "Zones": ["a", "b", "c"] } }""")
            .On("https://example.test/u/2", 200, """{ "data": { "Pets": {}, "Zones": 7 } }""");

        var reading = await Read(transport, recipe, inputs: NoInputs, ids: [1, 2], tracked: new HashSet<string> { "pets", "zones" });

        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
        Assert.Equal(new RecipeRow(1, new Dictionary<string, double> { ["pets"] = 2, ["zones"] = 3 }), reading.Rows[0]);
        Assert.Equal(new RecipeRow(2, new Dictionary<string, double> { ["pets"] = 0 }), reading.Rows[1]);
        Assert.Equal("'data.Zones' is not a list or an object for user id 2, so its entries can't be counted.", reading.CellMisses[(2, "zones")]);
    }
```

- [ ] **Step 3: Write the failing update-screen test, and pin the update tests to three values**

In `tests/ImportReviewTests.cs`, replace the `Profile` property with the fixture's first three values, so the stats design §7.2 tests keep describing a three-stat recipe after the fixture grows in Step 10:

```csharp
    /// <summary>The profile fixture's first three values (diamonds, eggs, rank): the update tests below describe that recipe.</summary>
    private static Recipe Profile => FirstThree(Load("petsim99-profile.recipe.json"));

    private static Recipe FirstThree(Recipe recipe) => WithValues(recipe, [.. recipe.LastStep.Values.Take(3)]);
```

and add:

```csharp
    [Fact]
    public void AStatThatNowCountsOrReadsAsTimeIsListedWithoutAsking()
    {
        var values = Profile.LastStep.Values;
        var incoming = WithValues(Profile, values[0], values[1] with { Count = true }, values[2] with { Format = StatFormat.Duration });

        var comparison = ImportReview.CompareToInstalled(Profile, incoming, new FakeKeys());

        Assert.False(comparison.AsksAgain);
        Assert.Equal(new[] { "Eggs hatched now counts entries.", "Player rank is shown as a duration instead of a number." }, comparison.Changes);
    }
```

- [ ] **Step 4: Run the tests to see them fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`
Expected: FAIL to compile (`StatFormat` and the named arguments `Count`, `Show`, `Section`, `Format` don't exist).

- [ ] **Step 5: The format enum and the records**

In `src/Recipes/RecipeFormat.cs`, add after `RecipeAsOf`:

```csharp
/// <summary>How a value's number reads (D10): a plain number, seconds as a duration, or unix seconds as a date.</summary>
public enum StatFormat { Number, Duration, Date }
```

In `src/Recipes/Recipe.cs`, replace `RecipeValue` with:

```csharp
/// <summary>
/// One stat the user can tick. <see cref="MetricId"/> is only a suggestion: the name RoRoRo gets is
/// whatever the user accepted, pinned in the recipe's state. <see cref="Sum"/> says whether adding
/// this stat up across accounts means anything, <see cref="Count"/> that the number is how many entries
/// the object or list at <see cref="Path"/> holds, <see cref="Format"/> how the number reads, and
/// <see cref="Section"/> where an account card lists it. <see cref="Show"/> suggests ticking Show on a
/// first import; it never ticks Send (D11). All of them describe the data and trigger nothing.
/// </summary>
public sealed record RecipeValue(
    string Id, string Label, string Path, string MetricId, bool Sum = true,
    bool Count = false, StatFormat Format = StatFormat.Number, bool Show = false, string? Section = null)
{
    /// <summary>The id a single <c>value</c> is given when the parser turns it into a list.</summary>
    public const string ShorthandId = "value";
}
```

In `src/Recipes/RecipeStats.cs`, replace the `RecipeStat` record and the two `return new RecipeStat(...)` lines in `Find`:

```csharp
/// <summary>
/// One stat as Ur Score handles it after parsing: a recipe value, or a counter the user picked.
/// <see cref="Key"/> is the value's id, or <c>counter:</c> plus the counter's name (stats design §5.1).
/// A counter reads a number, sums, and sits in its counters' own section.
/// </summary>
public sealed record RecipeStat(
    string Key, string Label, string Path, string SuggestedMetricId, bool Sum,
    bool Count = false, StatFormat Format = StatFormat.Number, string? Section = null);
```

```csharp
        if (value is not null) return new RecipeStat(value.Id, value.Label, value.Path, value.MetricId, value.Sum, value.Count, value.Format, value.Section);

        if (step.Counters is { } counters && IsCounterKey(key, out var name) && CanPick(name))
        {
            return new RecipeStat(key, name, $"{counters.Path}.{name}", counters.MetricIdPrefix + Slug(name), Sum: true, Section: counters.Label);
        }
```

- [ ] **Step 6: The parser**

In `src/Recipes/RecipeParser.cs`, in `ParseValues`, replace the lines from `var sum = OptionalBool(...)` through `values.Add(...)`'s closing brace with:

```csharp
            var sum = OptionalBool(item, "sum", true, valueWhere, problems);
            var count = OptionalBool(item, "count", false, valueWhere, problems);
            var show = OptionalBool(item, "show", false, valueWhere, problems);
            var format = ParseFormat(item, valueWhere, problems);
            var section = OptionalText(item, "section", valueWhere, problems);

            if (count && format != StatFormat.Number)
            {
                problems.Add($"{Capitalize(valueWhere)} counts entries, so its format can only be \"number\".");
            }

            if (format == StatFormat.Date && sum)
            {
                problems.Add($"{Capitalize(valueWhere)} is a date, and dates can't be added up. Give it \"sum\": false.");
            }

            if (id is not null && label is not null && path is not null && metricId is not null)
            {
                values.Add(new RecipeValue(id, label, path, metricId, sum, count, format, show, section));
            }
```

and add these helpers after `OptionalBool`:

```csharp
    /// <summary>Absent or null reads as a number; otherwise exactly "number", "duration" (seconds) or "date" (unix seconds), else a named problem.</summary>
    private static StatFormat ParseFormat(JsonElement item, string where, List<string> problems)
    {
        if (!Present(item, "format", out var element)) return StatFormat.Number;

        switch (element.ValueKind == JsonValueKind.String ? element.GetString() : null)
        {
            case "number": return StatFormat.Number;
            case "duration": return StatFormat.Duration;
            case "date": return StatFormat.Date;
            default:
                problems.Add($"'format' in {where} must be \"number\", \"duration\" or \"date\".");
                return StatFormat.Number;
        }
    }

    /// <summary>Absent or null is none; anything but text with something in it is a named problem. Trimmed.</summary>
    private static string? OptionalText(JsonElement obj, string name, string where, List<string> problems)
    {
        if (!Present(obj, name, out var element)) return null;
        if (element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString())) return element.GetString()!.Trim();

        problems.Add($"'{name}' in {where} must be text.");
        return null;
    }
```

- [ ] **Step 7: The engine counts entries**

In `src/Recipes/RecipeEngine.cs`, add after `NumberAt`:

```csharp
    /// <summary>
    /// Why a stat has no number here, or null with its number: the value itself, or for a counting stat (D9) how
    /// many entries its object or list holds. Only the count is read, never the keys.
    /// </summary>
    private static string? StatNumberAt(PathResult result, RecipeStat stat, string path, string where, out double number)
    {
        if (!stat.Count) return NumberAt(result, path, where, out number);

        number = 0;
        if (result.Outcome != PathOutcome.Found) return result.Miss ?? $"'{path}' was empty {where}.";

        switch (result.Value.ValueKind)
        {
            case JsonValueKind.Object:
                number = result.Value.EnumerateObject().Count();
                return null;
            case JsonValueKind.Array:
                number = result.Value.GetArrayLength();
                return null;
            default:
                return $"'{path}' is not a list or an object {where}, so its entries can't be counted.";
        }
    }
```

Then change the three stat reads to go through it (the `rank` read in `ReadGroups` stays on `NumberAt`):
- in `ReadList`: `var miss = StatNumberAt(result, stat, Placeholders.Fill(stat.Path, values, encode: false), "in this row", out var value);`
- in `ReadPerAccountAsync`: `var miss = StatNumberAt(result, stat, Placeholders.Fill(stat.Path, perRequest, encode: false), $"for user id {userId}", out var value);`
- in `ValuesAt`: `if (StatNumberAt(RecipePath.Resolve(row, stat.Path, values, "this row"), stat, stat.Path, "in this row", out var value) is null)`

- [ ] **Step 8: The update screen lists a count or format change**

In `src/Recipes/ImportReview.cs`, inside the `foreach (var (key, now) in newStats...)` loop, after the `SuggestedMetricId` check, add:

```csharp
            if (was.Count != now.Count)
            {
                changes.Add(now.Count ? $"{now.Label} now counts entries." : $"{now.Label} no longer counts entries.");
            }

            if (was.Format != now.Format)
            {
                changes.Add($"{now.Label} is shown as {FormatWords(now.Format)} instead of {FormatWords(was.Format)}.");
            }
```

and after `MeaningChanged`:

```csharp
    private static string FormatWords(StatFormat format) => format switch
    {
        StatFormat.Duration => "a duration",
        StatFormat.Date => "a date",
        _ => "a number",
    };
```

- [ ] **Step 9: Run the new tests**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror` then `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~RecipeParserTests|FullyQualifiedName~RecipeEngineTests|FullyQualifiedName~ImportReviewTests"`
Expected: PASS.

- [ ] **Step 10: The profile recipe names its stats**

Replace `tests/Fixtures/petsim99-profile.recipe.json` with:

```json
{
  "recipe": 1,
  "name": "Pet Sim 99 profile",
  "credit": "Data from Big Games' public Pet Simulator 99 API. Each account must be linked on db.biggames.io with its Profile view public.",
  "everySeconds": 1800,
  "steps": [
    { "url": "https://ps99.biggamesapi.io/v1/players/{userId}?include=profile",
      "perAccount": true,
      "asOf": { "time": "data.views.profile.fetchedAt", "stale": "data.views.profile.isStale" },
      "unavailable": { "path": "data.views.profile.available", "is": false,
                       "message": "Profile is private. Link this account on db.biggames.io and turn on its Profile view." },
      "values": [
        { "id": "diamonds", "label": "Diamonds", "path": "data.views.profile.data.Currency.Diamonds._am", "metricId": "ps99.diamonds", "show": true, "section": "Account" },
        { "id": "eggs", "label": "Eggs hatched", "path": "data.views.profile.data.EggsHatched", "metricId": "ps99.eggs-hatched", "show": true, "section": "Progression" },
        { "id": "rank", "label": "Player rank", "path": "data.views.profile.data.Rank", "metricId": "ps99.rank", "sum": false, "show": true, "section": "Progression" },
        { "id": "rebirths", "label": "Rebirths", "path": "data.views.profile.data.Rebirths", "metricId": "ps99.rebirths", "sum": false, "show": true, "section": "Progression" },
        { "id": "rank-stars", "label": "Rank stars", "path": "data.views.profile.data.RankStars", "metricId": "ps99.rank-stars", "sum": false, "section": "Progression" },
        { "id": "pets", "label": "Different pets hatched", "path": "data.views.profile.data.PetHatchCount", "metricId": "ps99.pets-hatched", "count": true, "sum": false, "show": true, "section": "Progression" },
        { "id": "goals", "label": "Goals completed", "path": "data.views.profile.data.GoalsCompleted", "metricId": "ps99.goals-completed", "show": true, "section": "Progression" },
        { "id": "achievements", "label": "Achievements", "path": "data.views.profile.data.Achievements", "metricId": "ps99.achievements", "count": true, "sum": false, "section": "Progression" },
        { "id": "zones", "label": "Zones unlocked", "path": "data.views.profile.data.UnlockedZones", "metricId": "ps99.zones-unlocked", "count": true, "sum": false, "section": "Progression" },
        { "id": "playtime", "label": "Playtime", "path": "data.views.profile.data.Age", "metricId": "ps99.playtime", "format": "duration", "show": true, "section": "Account" },
        { "id": "sessions", "label": "Sessions", "path": "data.views.profile.data.TotalSessions", "metricId": "ps99.sessions", "section": "Account" },
        { "id": "first-join", "label": "First joined", "path": "data.views.profile.data.FirstJoinTimestamp", "metricId": "ps99.first-join", "format": "date", "sum": false, "section": "Account" },
        { "id": "booth-diamonds", "label": "Booth diamonds earned", "path": "data.views.profile.data.BoothDiamondsEarned", "metricId": "ps99.booth-diamonds-earned", "section": "Account" },
        { "id": "booth-slots", "label": "Booth slots", "path": "data.views.profile.data.BoothSlots", "metricId": "ps99.booth-slots", "sum": false, "section": "Slots" },
        { "id": "egg-slots", "label": "Egg slots bought", "path": "data.views.profile.data.EggSlotsPurchased", "metricId": "ps99.egg-slots", "sum": false, "section": "Slots" },
        { "id": "pet-slots", "label": "Pet slots bought", "path": "data.views.profile.data.PetSlotsPurchased", "metricId": "ps99.pet-slots", "sum": false, "section": "Slots" }
      ],
      "counters": { "label": "Game statistics", "path": "data.views.profile.data.Statistics", "metricIdPrefix": "ps99.stat." } }
  ]
}
```

No label but "Eggs hatched" contains "eggs" (the Stats table search tests rely on it), and "rebirths" is now offered, so the tests that used it as a stat the recipe doesn't offer move to "prestige".

- [ ] **Step 11: Update the tests that pinned the old three-stat fixture**

`tests/RecipeParserTests.cs`, `TheProfileRecipeParsesItsValuesCountersAndUnavailable`: replace its three assertions on ids, metric ids and sums with:

```csharp
        Assert.Equal(ProfileIds, step.Values.Select(v => v.Id).ToArray());
        Assert.Equal(
            new[]
            {
                "ps99.diamonds", "ps99.eggs-hatched", "ps99.rank", "ps99.rebirths", "ps99.rank-stars", "ps99.pets-hatched", "ps99.goals-completed",
                "ps99.achievements", "ps99.zones-unlocked", "ps99.playtime", "ps99.sessions", "ps99.first-join", "ps99.booth-diamonds-earned",
                "ps99.booth-slots", "ps99.egg-slots", "ps99.pet-slots",
            },
            step.Values.Select(v => v.MetricId).ToArray());
        Assert.Equal(new[] { "diamonds", "eggs", "rank", "rebirths", "pets", "goals", "playtime" }, step.Values.Where(v => v.Show).Select(v => v.Id).ToArray());
        Assert.Equal(new[] { "diamonds", "eggs", "goals", "playtime", "sessions", "booth-diamonds" }, step.Values.Where(v => v.Sum).Select(v => v.Id).ToArray());
        Assert.Equal(new[] { "pets", "achievements", "zones" }, step.Values.Where(v => v.Count).Select(v => v.Id).ToArray());
        Assert.Equal(StatFormat.Duration, step.Values.Single(v => v.Id == "playtime").Format);
        Assert.Equal(StatFormat.Date, step.Values.Single(v => v.Id == "first-join").Format);
        Assert.Equal(new[] { "Account", "Progression", "Slots" }, step.Values.Select(v => v.Section).Distinct().ToArray());
```

`tests/RecipeStatsTests.cs`:
- `AValueIsFoundByItsId`: expected `new RecipeStat("rank", "Player rank", "data.views.profile.data.Rank", "ps99.rank", Sum: false, Section: "Progression")`.
- `ACounterKeyReadsUnderTheCountersPath`: expected `new RecipeStat("counter:Huge Pets Opened", "Huge Pets Opened", "data.views.profile.data.Statistics.Huge Pets Opened", "ps99.stat.huge-pets-opened", Sum: true, Section: "Game statistics")`.
- `AKeyTheRecipeDoesNotOfferIsNoStat`: `Assert.Null(RecipeStats.Find(Profile, "prestige"));` in place of `"rebirths"`.
- `OfferedListsValuesThenPickedCountersInKeyOrder`: expected `[.. RecipeParserTests.ProfileIds, "counter:Eggs Opened", "counter:Zones"]` (as `string[] expected = [...]; Assert.Equal(expected, ...)`).

`tests/StatsTableModelTests.cs`:
- `RowsAreValuesThenCounterNamesInSourceOrderThenSavedCountersThenChoicesNoLongerOffered`: the saved non-offered entry becomes `("prestige", new StatChoice(false, false, "ps99.prestige"))`; then

```csharp
        string[] expected = [.. RecipeParserTests.ProfileIds, "counter:Pets Hatched", "counter:Coins Spent", "counter:Zones Unlocked", "prestige"];
        Assert.Equal(expected, rows.Select(r => r.Key).ToArray());
        Assert.All(rows.Take(rows.Count - 1), row => Assert.True(row.Offered));
        Assert.False(rows[^1].Offered);
        Assert.Equal("prestige (no longer offered)", rows[^1].DisplayLabel);
        Assert.Equal("Coins Spent", rows.Single(r => r.Key == "counter:Coins Spent").DisplayLabel);
```

- `AChoiceTheRecipeNoLongerOffersIsNeverTicked`: `Saved(("prestige", new StatChoice(true, true, "ps99.prestige")))` and `rows.Single(r => r.Key == "prestige")`.

- [ ] **Step 12: The build gate**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror` then `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: both pass. A failure that names the profile fixture's shape is a test pinned to three values that Step 11 missed: pin it the same way (`ProfileIds`, "prestige", or `FirstThree`), never by shrinking the fixture.

- [ ] **Step 13: Commit**

```bash
git add src/Recipes tests/Fixtures/petsim99-profile.recipe.json tests/RecipeParserTests.cs tests/RecipeEngineTests.cs tests/ImportReviewTests.cs tests/RecipeStatsTests.cs tests/StatsTableModelTests.cs
git commit -m "recipes: values can count entries, read as durations or dates, suggest Show and name a section; the profile recipe names its stats

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: A first import starts with the suggested stats ticked to show

**Files:**
- Modify: `src/UI/Controls/StatsTableModel.cs`, `src/UI/Controls/StatsTable.xaml.cs` (`Load`), `src/UI/ImportWindow.xaml`, `src/UI/ImportWindow.xaml.cs`, `src/UI/ImportText.cs`
- Modify: `docs/2026-09-13-stats-games-icons-design.md` (a banner)
- Test: `tests/StatsTableModelTests.cs`, `tests/ImportTextTests.cs`

**Interfaces:**
- Consumes: `RecipeValue.Show` (Task 1).
- Produces: `StatsTableModel.Suggested(Recipe)`, `StatsTable.Load(..., startTicks)`, `ImportText.SuggestedNote(Recipe)`, automation id `SuggestedLine`.

- [ ] **Step 1: Write the failing tests**

`tests/StatsTableModelTests.cs`:

```csharp
    [Fact]
    public void AFirstImportStartsWithShowTickedOnWhatTheRecipeSuggestsAndNothingSent()
    {
        var suggested = StatsTableModel.Suggested(Profile);

        Assert.Equal(new[] { "diamonds", "eggs", "goals", "pets", "playtime", "rank", "rebirths" }, suggested.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.All(suggested.Values, choice => Assert.True(choice.Show && !choice.Send));
        Assert.Equal("ps99.playtime", suggested["playtime"].MetricId);

        // Rows built from them are ticked, and what Import saves is exactly them: a fresh import has no saved choices.
        var rows = StatsTableModel.Build(Profile, suggested, []);
        Assert.True(StatsTableModel.AnyTicked(rows));
        Assert.Equal(suggested.Keys.Order(StringComparer.Ordinal), StatsTableModel.Choices(new Dictionary<string, StatChoice>(), rows).Keys.Order(StringComparer.Ordinal));

        var clan = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;
        Assert.Empty(StatsTableModel.Suggested(clan));
    }
```

`tests/ImportTextTests.cs`:

```csharp
    [Fact]
    public void TheImportScreenSaysWhichStatsStartTickedAndThatNothingIsSent()
    {
        var profile = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;
        var clan = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

        Assert.Equal(
            "The recipe suggests showing Diamonds, Eggs hatched, Player rank, Rebirths, Different pets hatched, Goals completed and Playtime, "
            + "so they start ticked. Untick any you don't want. Nothing is sent to RoRoRo unless you tick Send.",
            ImportText.SuggestedNote(profile));
        Assert.Equal("", ImportText.SuggestedNote(clan));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`
Expected: FAIL to compile (`Suggested`, `SuggestedNote` don't exist).

- [ ] **Step 3: The model and the text**

`src/UI/Controls/StatsTableModel.cs`, after `Build`:

```csharp
    /// <summary>
    /// A first import's starting ticks (D11): Show on each value the recipe suggests, under its suggested name, and never
    /// Send. An update and Setup › Stats start from your saved choices instead, so a tick you made is never changed.
    /// </summary>
    public static IReadOnlyDictionary<string, StatChoice> Suggested(Recipe recipe) =>
        recipe.LastStep.Values
            .Where(v => v.Show)
            .ToDictionary(v => v.Id, v => new StatChoice(Show: true, MetricId: v.MetricId), StringComparer.Ordinal);
```

`src/UI/ImportText.cs`:

```csharp
    /// <summary>Why some Show boxes start ticked on a first import (D11), or "" when the recipe suggests none.</summary>
    public static string SuggestedNote(Recipe recipe)
    {
        List<string> labels = recipe.IsGroupList ? [] : [.. recipe.LastStep.Values.Where(v => v.Show).Select(v => v.Label)];
        if (labels.Count == 0) return "";

        var named = labels.Count == 1 ? labels[0] : $"{string.Join(", ", labels.Take(labels.Count - 1))} and {labels[^1]}";
        var start = labels.Count == 1 ? "so it starts ticked" : "so they start ticked";
        return $"The recipe suggests showing {named}, {start}. Untick any you don't want. Nothing is sent to RoRoRo unless you tick Send.";
    }
```

- [ ] **Step 4: The table starts from them, and the screen says so**

`src/UI/Controls/StatsTable.xaml.cs`, `Load`: add a last parameter and use it for the first rows only; the saved choices (`_existing`) stay what `Choices` merges into and what the budget counts from:

```csharp
    /// <param name="startTicks">The ticks the rows start with when they aren't your saved ones: a first import's suggestions (D11).</param>
    public void Load(
        Recipe recipe, RecipeState existing, IReadOnlyList<InstalledRecipe> installed,
        Func<IReadOnlyCollection<Guid>> accountIds, Func<string, string> ruleSentence,
        Func<CancellationToken, Task<CounterLookup>>? readNames, string readNamesLabel,
        IReadOnlyList<string>? extraRefusals = null, IReadOnlyDictionary<string, StatChoice>? startTicks = null)
```

and its last line becomes `Rebuild(startTicks ?? existing.StatChoices);`.

`src/UI/ImportWindow.xaml`: replace the comment above `StatsSection` and add the line inside it:

```xml
            <!-- Nothing is sent for the user. A first import starts with Show ticked on what the recipe suggests (D11),
                 and says so; a group list has no stats to tick at all. -->
            <StackPanel x:Name="StatsSection">
                <TextBlock Text="STATS" Style="{StaticResource SectionLabel}" Margin="0,16,0,4" />
                <TextBlock x:Name="SuggestedLine" Style="{StaticResource Muted}" Margin="0,0,0,6" Visibility="Collapsed" />
                <ui:StatsTable x:Name="StatsTable" />
            </StackPanel>
```

`src/UI/ImportWindow.xaml.cs`, the `else` branch that loads the table:

```csharp
        else
        {
            // A first import starts from the recipe's suggestions; an update from your saved ticks, untouched.
            var fresh = !comparison.IsUpdate;
            StatsTable.Load(recipe, _existing, installed, accountIds, ruleSentence,
                readCounterNames is null ? null : _ => readCounterNames(),
                ImportText.ShowEveryStat, review.Refusals,
                fresh ? StatsTableModel.Suggested(recipe) : null);
            Show(SuggestedLine, fresh ? ImportText.SuggestedNote(recipe) : "");
            StatsTable.Changed += (_, _) => Refresh();
        }
```

- [ ] **Step 5: Banner the stats design**

In `docs/2026-09-13-stats-games-icons-design.md`, insert after the title line:

```markdown
> **Amended 2026-09-15 (default views, v0.3.1).** A recipe value may carry `"show": true`. On a first import only, it
> starts that stat's Show box ticked, never Send, and the import screen says so; an update keeps §7.2. §2's "Nothing is
> ticked by default" now reads "nothing is sent by default". The same release adds `count`, `format` and `section` to a
> value. See `docs/plans/2026-09-15-default-views.md`, rulings D9 to D12.
```

- [ ] **Step 6: The build gate**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror` then `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: both pass.

- [ ] **Step 7: Commit**

```bash
git add src/UI/Controls/StatsTableModel.cs src/UI/Controls/StatsTable.xaml.cs src/UI/ImportWindow.xaml src/UI/ImportWindow.xaml.cs src/UI/ImportText.cs docs/2026-09-13-stats-games-icons-design.md tests/StatsTableModelTests.cs tests/ImportTextTests.cs
git commit -m "import: a first import starts with the recipe's suggested stats ticked to show, and says nothing is sent

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

Walk (controller, Task 9): `walk-stats-table.ps1` step 1b, `walk-alts.ps1` step 1.

---

### Task 3: Durations and dates on panels, and an account card in sections that shows the picked account

**Files:**
- Modify: `src/Board/PanelText.cs` (`Value`, `Change`, `Duration`, `Date`)
- Modify: `src/Board/PanelModels.cs` (`CardSection`, `AccountCardModel.Sections`, `AccountCard`, `ProfileStat`, `WindowGain`, a `Sections` helper)
- Modify: `src/UI/Panels/AccountCardPanel.xaml`
- Test: `tests/PanelTextTests.cs`, `tests/PanelModelsTests.cs`

**Interfaces:**
- Consumes: `StatFormat`, `RecipeStat.Format/Section` (Task 1).
- Produces: `PanelText.Value(double?, StatFormat, TimeZoneInfo)`, `PanelText.Change(double?, StatFormat)`, `PanelText.Duration(double)`; `CardSection`; `AccountCardModel.Sections`; `PanelModels.AccountCard(..., long? pickedUserId = null)`; the private `WindowGain(series, since, StatFormat format = Number, string none = "no earlier read")` Task 4 uses.

- [ ] **Step 1: Write the failing tests**

`tests/PanelTextTests.cs`:

```csharp
    [Fact]
    public void DurationsAndDatesReadAsTimeNotRawNumbers()
    {
        var utc = TimeZoneInfo.Utc;

        Assert.Equal("586d 5h", PanelText.Value(50_651_629, StatFormat.Duration, utc));
        Assert.Equal("5h 12m", PanelText.Value(18_720, StatFormat.Duration, utc));
        Assert.Equal("12m", PanelText.Value(720, StatFormat.Duration, utc));
        Assert.Equal("13 Sep 2020", PanelText.Value(1_600_000_000, StatFormat.Date, utc));
        Assert.Equal("14,020,550", PanelText.Value(14_020_550, StatFormat.Number, utc));
        Assert.Equal(StatText.Dash, PanelText.Value(null, StatFormat.Duration, utc));
        Assert.Equal(StatText.Dash, PanelText.Value(-5, StatFormat.Date, utc));

        Assert.Equal("+2h 0m", PanelText.Change(7_200, StatFormat.Duration));
        Assert.Equal("-12m", PanelText.Change(-720, StatFormat.Duration));
        Assert.Equal(StatText.Dash, PanelText.Change(86_400, StatFormat.Date));
        Assert.Equal(PanelText.Signed(220_000), PanelText.Change(220_000, StatFormat.Number));
        Assert.Equal(StatText.Dash, PanelText.Change(null, StatFormat.Duration));
    }
```

`tests/PanelModelsTests.cs`, in the Account card section:

```csharp
    [Fact]
    public void TheAccountCardShowsThePickedAccountInTheRecipesSections()
    {
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);
        var snapshot = Snapshot(profile.Id,
        [
            new RecipeRow(Main.RobloxUserId, new Dictionary<string, double> { ["diamonds"] = 215_850_364, ["rebirths"] = 9, ["playtime"] = 50_651_629, ["first-join"] = 1_600_000_000 }),
            new RecipeRow(AltOne.RobloxUserId, new Dictionary<string, double> { ["diamonds"] = 3_957_873_882, ["rebirths"] = 11, ["playtime"] = 3_600 }),
        ]) with
        {
            Unavailable = new Dictionary<long, string> { [AltTwo.RobloxUserId] = "Profile is private." },
        };
        var live = Live([profile], [Installed(Profile, "diamonds", "rebirths", "playtime", "first-join")], Snaps(snapshot));
        var settings = new PanelSettings(Profile.Slug, Stat: "diamonds");

        var top = PanelModels.AccountCard(live, Reader(), settings);
        var picked = PanelModels.AccountCard(live, Reader(), settings, pickedUserId: Main.RobloxUserId);
        var pinned = PanelModels.AccountCard(live, Reader(), settings with { UserId = AltOne.RobloxUserId }, pickedUserId: Main.RobloxUserId);
        var unread = PanelModels.AccountCard(live, Reader(), settings, pickedUserId: AltTwo.RobloxUserId);
        var gone = PanelModels.AccountCard(live, Reader(), settings, pickedUserId: 999);

        // With nothing picked, the top account; a pick shows that account; a card pinned to an account stays on it.
        Assert.StartsWith(AltOne.DisplayName, top.Head.Subtitle);
        Assert.StartsWith(Main.DisplayName, picked.Head.Subtitle);
        Assert.StartsWith(AltOne.DisplayName, pinned.Head.Subtitle);
        Assert.StartsWith(AltOne.DisplayName, gone.Head.Subtitle);

        // The big number is the card's stat; the other shown stats sit in the recipe's sections, in its order, read as time.
        Assert.Equal("215,850,364", picked.Big);
        Assert.Equal(new[] { "Account", "Progression" }, picked.Sections.Select(s => s.Heading).ToArray());
        Assert.Equal(new[] { new FactModel("Playtime", "586d 5h"), new FactModel("First joined", "13 Sep 2020") }, picked.Sections[0].Facts);
        Assert.Equal(new[] { new FactModel("Rebirths", "9") }, picked.Sections[1].Facts);

        // A picked account the read couldn't reach says why, in the recipe's words.
        Assert.Equal(AltTwo.DisplayName, unread.Head.Subtitle);
        Assert.Equal("Profile is private.", unread.Head.Note);
        Assert.Empty(unread.Sections);
    }
```

In `ProfileStatShowsTodayAndSevenDayGainsAndKeepsMissingValuesLast` nothing changes (a number's `Value` is `Full`, its `Change` is `Signed`). Add to the same region:

```csharp
    [Fact]
    public void AProfileStatOfPlaytimeReadsAsADuration()
    {
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);
        var snapshot = Snapshot(profile.Id, [Row(Main.RobloxUserId, 50_651_629, "playtime")]);
        var reader = Reader(
            Read(profile, Now.AddDays(-8), null, null, "playtime", (Main.RobloxUserId, 50_644_429)),
            Read(profile, Now.AddMinutes(-5), null, null, "playtime", (Main.RobloxUserId, 50_651_629)));
        var live = Live([profile], [Installed(Profile, "playtime")], Snaps(snapshot));

        var row = PanelModels.ProfileStat(live, reader, new PanelSettings(Profile.Slug, SourceId: profile.Id, Stat: "playtime")).Rows[0];

        Assert.Equal("586d 5h", row.Value);
        Assert.Equal("+2h 0m", row.Today);
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`
Expected: FAIL to compile (`PanelText.Value`, `Sections`, `pickedUserId` don't exist).

- [ ] **Step 3: PanelText writes durations and dates**

In `src/Board/PanelText.cs`, add after `Signed`:

```csharp
    /// <summary>A value as its recipe says it reads (D10): a number with every digit, seconds as a duration, unix seconds as a date in your time zone.</summary>
    public static string Value(double? value, StatFormat format, TimeZoneInfo zone) => value is not { } v ? StatText.Dash : format switch
    {
        StatFormat.Duration => Duration(v),
        StatFormat.Date => Date(v, zone),
        _ => StatText.Number(v),
    };

    /// <summary>A change as its recipe says it reads: "+220K", "+2h 0m". A date has no change.</summary>
    public static string Change(double? value, StatFormat format) => value is not { } v ? StatText.Dash : format switch
    {
        StatFormat.Duration => (v < 0 ? "" : "+") + Duration(v),
        StatFormat.Date => StatText.Dash,
        _ => Signed(v),
    };

    /// <summary>Seconds as the two largest whole units: "586d 5h", "5h 12m", "12m".</summary>
    public static string Duration(double seconds)
    {
        if (!double.IsFinite(seconds)) return StatText.Dash;

        var minutes = (long)Math.Floor(Math.Abs(seconds) / 60);
        var (days, hours, rest) = (minutes / 1440, minutes / 60 % 24, minutes % 60);
        var text = days > 0 ? $"{days.ToString("N0", CultureInfo.InvariantCulture)}d {hours}h"
            : hours > 0 ? $"{hours}h {rest}m"
            : $"{rest}m";
        return seconds < 0 ? "-" + text : text;
    }

    /// <summary>Unix seconds as "13 Sep 2020" in your time zone; a time outside years 1970 to 9999 is a dash.</summary>
    private static string Date(double unixSeconds, TimeZoneInfo zone) =>
        unixSeconds is > 0 and <= 253402300799
            ? TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds((long)Math.Floor(unixSeconds)), zone).ToString("d MMM yyyy", CultureInfo.InvariantCulture)
            : StatText.Dash;
```

- [ ] **Step 4: The card's sections and its picked account**

In `src/Board/PanelModels.cs`, replace the `AccountCardModel` record with:

```csharp
/// <summary>One heading of an account card and its facts; the heading is the recipe's section name, empty for stats with none.</summary>
public sealed record CardSection(string Heading, IReadOnlyList<FactModel> Facts)
{
    public bool HasHeading => Heading.Length > 0;
}

public sealed record AccountCardModel(
    PanelHead Head, string BigLabel, string Big, IReadOnlyList<CardSection> Sections, IReadOnlyList<ChartSeries> Line,
    IReadOnlyList<FactModel> Facts, string ChartName);
```

In `AccountCard`, change the signature to `public static AccountCardModel AccountCard(LiveBoard live, ScoreBookReader reader, PanelSettings settings, long? pickedUserId = null)` and replace from `var picked = settings.UserId is { } userId` through the `var numbers = ...ToList();` statement with:

```csharp
        // A card pinned to an account shows it; else the account picked in a table on its board while RoRoRo lists it (D18); else the top one.
        var wanted = settings.UserId ?? (pickedUserId is { } asked && live.MyUserIds.Contains(asked) ? asked : (long?)null);
        var picked = wanted is { } userId
            ? found.Where(f => f.Account.RobloxUserId == userId).ToList()
            : [.. found.OrderBy(f => ValueOf(f.Row, stat.Key) is null).ThenByDescending(f => ValueOf(f.Row, stat.Key) ?? 0)];

        if (picked.Count == 0)
        {
            if (settings.UserId is null && wanted is { } id)
            {
                var name = live.AccountName(id);
                var why = SourcesYoursIn(live, recipe)
                    .Select(s => live.SnapshotOf(s.Id)?.Unavailable.GetValueOrDefault(id))
                    .FirstOrDefault(message => message is not null);
                return EmptyCard(new PanelHead(title, name, Note: why ?? $"No reading of {name} yet."));
            }

            return EmptyCard(new PanelHead(title, Note: "No reading of your accounts yet."));
        }

        var (pickedAccount, pickedSource, pickedRow) = picked[0];
        var snapshot = live.SnapshotOf(pickedSource.Id)!;
        var series = reader.Series(pickedSource.Id, pickedAccount.RobloxUserId, stat.Key, snapshot.Period?.Value, Since(recipe, live.Now));
        var zone = live.Time.LocalTimeZone;

        var sections = Sections(recipe, installed.State.ShownStats(recipe).Where(s => s.Key != stat.Key), pickedRow, zone);
```

At the end of `AccountCard`, the return passes `sections` and the big number in its format:

```csharp
        return new AccountCardModel(
            new PanelHead(title, $"{pickedAccount.DisplayName} · {live.SourceName(pickedSource)}", Overdue: live.IsOverdue(pickedSource)),
            stat.Label, PanelText.Value(ValueOf(pickedRow, stat.Key), stat.Format, zone), sections, line, facts,
            $"{pickedAccount.DisplayName}'s {stat.Label} over time");
```

Change `EmptyCard` to `new(head, "", Dash, [], [], [], "")` (unchanged text; `Sections` takes the old `Numbers` slot). Add after `EmptyCard`:

```csharp
    /// <summary>
    /// A card's other shown stats under the recipe's own section names (D12): stats with no section first, with no
    /// heading; then sections in the order the recipe first names them; a picked counter's section (its counters'
    /// label) last. Each value reads as its format says.
    /// </summary>
    private static IReadOnlyList<CardSection> Sections(Recipe recipe, IEnumerable<RecipeStat> stats, RecipeRow row, TimeZoneInfo zone)
    {
        var order = recipe.LastStep.Values.Select(v => v.Section ?? "").Prepend("").Distinct(StringComparer.Ordinal).ToList();
        return [.. stats
            .GroupBy(s => s.Section ?? "", StringComparer.Ordinal)
            .OrderBy(g => order.IndexOf(g.Key) is var at && at >= 0 ? at : int.MaxValue)
            .Select(g => new CardSection(g.Key, [.. g.Select(s => new FactModel(s.Label, PanelText.Value(ValueOf(row, s.Key), s.Format, zone)))]))];
    }
```

- [ ] **Step 5: Profile stat and the change window read the format**

In `ProfileStat`, the row becomes:

```csharp
            rows.Add((value, new ProfileRow(
                account.DisplayName,
                PanelText.Value(value, stat.Format, live.Time.LocalTimeZone),
                WindowGain(series, midnight, stat.Format),
                WindowGain(series, now.AddDays(-7), stat.Format),
                unavailable ?? (value is null && missed is not null ? "can't read" : ""),
                value is null)));
```

Replace `WindowGain` with:

```csharp
    /// <summary>
    /// A window's gain: from the latest reading at or before <paramref name="since"/> to the last reading.
    /// When nothing was read that early, falls back to the series' own first reading and states the real
    /// span covered, rather than silently understating a shorter history as the full window (spec §9.4).
    /// Written in the stat's format; a date has no gain, and fewer than two readings is <paramref name="none"/>.
    /// </summary>
    private static string WindowGain(IReadOnlyList<SeriesPoint> series, DateTimeOffset since, StatFormat format = StatFormat.Number, string none = "no earlier read")
    {
        if (format == StatFormat.Date) return Dash;
        if (series.Count < 2) return none;

        var last = series[^1];
        SeriesPoint? baseline = null;
        for (var i = series.Count - 2; i >= 0; i--)
        {
            if (series[i].T <= since)
            {
                baseline = series[i];
                break;
            }
        }

        var from = baseline ?? series[0];
        var text = PanelText.Change(last.Value - from.Value, format);
        return baseline is null ? $"{text} in {StatText.Span(last.T - from.T)}" : text;
    }
```

- [ ] **Step 6: The card draws its sections**

In `src/UI/Panels/AccountCardPanel.xaml`, replace the `RowList` bound to `Numbers` with:

```xml
                <ui:RowList ItemsSource="{Binding Sections}" Margin="0,6,0,0" AutomationProperties.Name="This account's stats">
                    <ui:RowList.ItemTemplate>
                        <DataTemplate>
                            <StackPanel>
                                <TextBlock Text="{Binding Heading}" Style="{StaticResource ColumnHeader}" Margin="0,8,0,2"
                                           Visibility="{Binding HasHeading, Converter={StaticResource BoolToVisible}}" />
                                <ui:RowList ItemsSource="{Binding Facts}">
                                    <ui:RowList.ItemTemplate>
                                        <DataTemplate>
                                            <DockPanel Margin="0,3,0,0">
                                                <TextBlock DockPanel.Dock="Right" Text="{Binding Value}" FontWeight="SemiBold" />
                                                <TextBlock Text="{Binding Label}" Style="{StaticResource KeyLabel}" />
                                            </DockPanel>
                                        </DataTemplate>
                                    </ui:RowList.ItemTemplate>
                                </ui:RowList>
                            </StackPanel>
                        </DataTemplate>
                    </ui:RowList.ItemTemplate>
                </ui:RowList>
```

- [ ] **Step 7: The build gate**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror` then `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: both pass (`TheAccountCardPicksTheTopAccountAndShowsItsRecords` still passes: a clan card with one shown stat has no sections).

- [ ] **Step 8: Commit**

```bash
git add src/Board/PanelText.cs src/Board/PanelModels.cs src/UI/Panels/AccountCardPanel.xaml tests/PanelTextTests.cs tests/PanelModelsTests.cs
git commit -m "panels: durations and dates read as time, and the account card shows a picked account in the recipe's sections

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: The Accounts table's model, form and gallery card

**Files:**
- Create: `src/Board/AccountsTableModel.cs`
- Modify: `src/Board/PanelModels.cs` (`PanelType.AccountsTable`, `AccountsTable`, `StatOf`)
- Modify: `src/Board/PanelText.cs` (`Title`), `src/Board/PanelForms.cs` (`Fits`, `Build`, `SourceProblem`), `src/Board/PanelGallery.cs` (`Order`, the card), `src/Board/BoardDefs.cs` (`DefaultSize`), `src/UI/Boards/PanelSettingsWindow.xaml.cs` (the source field's word)
- Test: `tests/PanelModelsTests.cs`, `tests/PanelFormsTests.cs`, `tests/PanelGalleryTests.cs`

**Interfaces:**
- Consumes: `PanelText.Value/Change`, `WindowGain(..., format, none)` (Task 3).
- Produces: `PanelType.AccountsTable`; `AccountColumnKind`, `AccountColumn`, `AccountSort`, `AccountRow`, `AccountsTableModel`, `PanelSession`; `PanelModels.AccountsTable(LiveBoard, ScoreBookReader, PanelSettings, AccountSort? sort = null, long? pickedUserId = null)`.

Until Task 5 teaches `PanelViews` the type, a table added from the gallery on this branch draws nothing; no starter builds one before Task 6.

- [ ] **Step 1: Write the failing model tests**

`tests/PanelModelsTests.cs`, a new region before Live leaderboard:

```csharp
    // ---- Accounts table ----

    private static readonly Source ProfileSource = SourceOf("s-00000009", Profile, null, SourceRole.Mine);

    /// <summary>Your four accounts on the profile recipe: Main and AltOne read, Loose read without diamonds, AltTwo private.</summary>
    private static LiveBoard ProfileLive(params string[] shown) => Live([ProfileSource], [Installed(Profile, shown)], Snaps(Snapshot(ProfileSource.Id,
    [
        new RecipeRow(Main.RobloxUserId, new Dictionary<string, double> { ["diamonds"] = 215_850_364, ["rank"] = 37, ["playtime"] = 50_651_629 }),
        new RecipeRow(AltOne.RobloxUserId, new Dictionary<string, double> { ["diamonds"] = 3_957_873_882, ["rank"] = 12, ["playtime"] = 3_600 }),
        new RecipeRow(Loose.RobloxUserId, new Dictionary<string, double> { ["rank"] = 5 }),
    ]) with
    {
        Unavailable = new Dictionary<long, string> { [AltTwo.RobloxUserId] = "Profile is private." },
    }));

    private static ScoreBookReader DiamondsBook() => Reader(
        Read(ProfileSource, Now.AddDays(-8), null, null, "diamonds", (Main.RobloxUserId, 200_000_000)),
        Read(ProfileSource, Now.AddMinutes(-5), null, null, "diamonds", (Main.RobloxUserId, 215_850_364)));

    private static readonly PanelSettings TableSettings = new(Profile.Slug, SourceId: "s-00000009");

    [Fact]
    public void TheAccountsTableHasAColumnPerShownStatSortedByTheFirstWithItsChangeAndATotal()
    {
        var model = PanelModels.AccountsTable(ProfileLive("diamonds", "rank", "playtime"), DiamondsBook(), TableSettings);

        Assert.Equal(new[] { "Account", "Diamonds ↓", "Today", "7 days", "Player rank", "Playtime" }, model.Columns.Select(c => c.Heading).ToArray());
        Assert.Equal(new[] { AltOne.DisplayName, Main.DisplayName, Loose.DisplayName, AltTwo.DisplayName, "Total" }, model.Rows.Select(r => r.Name).ToArray());

        // Highest first; a missing value sorts last; the change comes from the book, a dash with fewer than two readings.
        var main = model.Rows[1];
        Assert.Equal(new[] { Main.DisplayName, "215,850,364", PanelText.Signed(15_850_364), PanelText.Signed(15_850_364), "37", "586d 5h" }, main.Cells);
        Assert.Equal(new[] { AltOne.DisplayName, "3,957,873,882", StatText.Dash, StatText.Dash, "12", "1h 0m" }, model.Rows[0].Cells);
        Assert.False(model.Rows[2].Missing);

        // An account the read couldn't reach says why, plainly, with dashes.
        var hidden = model.Rows[3];
        Assert.Equal(("Profile is private.", true), (hidden.Note, hidden.Missing));
        Assert.All(hidden.Cells.Skip(1), cell => Assert.Equal(StatText.Dash, cell));

        // The total adds up what adds up (diamonds, playtime); a rank and the change columns stay blank.
        var total = model.Rows[^1];
        Assert.True(total.IsTotal);
        Assert.Equal(new[] { "Total", "4,173,724,246", "", "", "", "586d 6h" }, total.Cells);
        Assert.Equal("", model.Head.Note);
    }

    [Fact]
    public void AClickedHeadingSortsItsColumnAndTheChangeFollowsIt()
    {
        var live = ProfileLive("diamonds", "rank", "playtime");

        var byRank = PanelModels.AccountsTable(live, DiamondsBook(), TableSettings, new AccountSort("rank", Descending: false));
        Assert.Equal(new[] { "Account", "Diamonds", "Player rank ↑", "Today", "7 days", "Playtime" }, byRank.Columns.Select(c => c.Heading).ToArray());
        Assert.Equal(new[] { Loose.DisplayName, AltOne.DisplayName, Main.DisplayName, AltTwo.DisplayName, "Total" }, byRank.Rows.Select(r => r.Name).ToArray());

        // The sorted heading flips; another stat sorts highest first; Account sorts A to Z; the change columns don't sort.
        Assert.Equal(new AccountSort("rank", Descending: true), AccountSort.Clicked(byRank.Columns[2]));
        Assert.Equal(new AccountSort("diamonds", Descending: true), AccountSort.Clicked(byRank.Columns[1]));
        Assert.Equal(new AccountSort(AccountSort.NameKey, Descending: false), AccountSort.Clicked(byRank.Columns[0]));
        Assert.False(byRank.Columns[3].CanSort);

        var byName = PanelModels.AccountsTable(live, DiamondsBook(), TableSettings, new AccountSort(AccountSort.NameKey, Descending: false));
        Assert.Equal(new[] { "Account ↑", "Diamonds", "Player rank", "Playtime" }, byName.Columns.Select(c => c.Heading).ToArray());
        Assert.Equal(new[] { AltOne.DisplayName, Main.DisplayName, Loose.DisplayName, AltTwo.DisplayName, "Total" }, byName.Rows.Select(r => r.Name).ToArray());

        // A sort on a stat you no longer show falls back to the first.
        var stale = PanelModels.AccountsTable(live, DiamondsBook(), TableSettings, new AccountSort("eggs", Descending: false));
        Assert.Equal("Diamonds ↓", stale.Columns[1].Heading);
    }

    [Fact]
    public void TheAccountsTableMarksThePickAndSaysWhatItIsWaitingFor()
    {
        var picked = PanelModels.AccountsTable(ProfileLive("diamonds"), DiamondsBook(), TableSettings, pickedUserId: AltOne.RobloxUserId);
        Assert.Equal(new[] { AltOne.RobloxUserId }, picked.Rows.Where(r => r.Picked).Select(r => r.UserId).ToArray());

        var unread = PanelModels.AccountsTable(Live([ProfileSource], [Installed(Profile, "diamonds")], Snaps()), DiamondsBook(), TableSettings);
        Assert.Equal("Waiting for the first read.", unread.Head.Note);
        Assert.All(unread.Rows.Where(r => !r.IsTotal), row => Assert.True(row.Missing));

        var nothingShown = PanelModels.AccountsTable(ProfileLive(), DiamondsBook(), TableSettings);
        Assert.Equal("Tick Show on a stat to fill this panel.", nothingShown.Head.Note);
        Assert.Equal(new[] { "Account ↑" }, nothingShown.Columns.Select(c => c.Heading).ToArray());
        Assert.DoesNotContain(nothingShown.Rows, r => r.IsTotal);

        // A pinned source that's gone is stale; an unpinned table reads the recipe's first source that is on.
        Assert.True(PanelModels.AccountsTable(ProfileLive("diamonds"), DiamondsBook(), new PanelSettings(Profile.Slug, SourceId: "s-gone")).Head.HasStale);
        Assert.False(PanelModels.AccountsTable(ProfileLive("diamonds"), DiamondsBook(), new PanelSettings(Profile.Slug)).Head.HasStale);
    }
```

- [ ] **Step 2: Write the failing form and gallery tests**

`tests/PanelFormsTests.cs`, two more rows on `AddingAsksOnlyForWhatThePanelNeeds`:

```csharp
    [InlineData(PanelType.AccountsTable, true, new[] { PanelField.Source })]
    [InlineData(PanelType.AccountsTable, false, new[] { PanelField.Source })]
```

and:

```csharp
    [Fact]
    public void AnAccountsTableReadsASourceWithoutAPeriodAndKeepsNoStat()
    {
        var live = Everything();

        Assert.Equal(new[] { ProfileSource.Id }, Keys(PanelForms.SourceChoices(PanelType.AccountsTable, PanelField.Source, live, new FormValues())));

        var settings = PanelForms.Build(PanelType.AccountsTable, new FormValues(Source: ProfileSource.Id, Stat: PanelForms.StatKey(Profile.Slug, "diamonds")), live);
        Assert.Equal(new PanelSettings(Profile.Slug, SourceId: ProfileSource.Id), settings);
        Assert.Null(PanelForms.Problem(PanelType.AccountsTable, settings, live));
        Assert.Null(PanelForms.Problem(PanelType.AccountsTable, new PanelSettings(Profile.Slug), live));
        Assert.Equal("This panel can't show that clan.", PanelForms.Problem(PanelType.AccountsTable, new PanelSettings(Clan.Slug, SourceId: MainClan.Id), live));
        Assert.Equal(new PanelSize(PanelSize.Wide), BoardDefs.DefaultSize(PanelType.AccountsTable));
    }
```

`tests/PanelGalleryTests.cs`: rename `TenCardsInTheSpecsOrderTitledInTheRecipesWords` to `EveryCardInOrderTitledInTheRecipesWords` and add `"Accounts table"` between `"Profile stat"` and `"Live leaderboard"` in its title list, and in `WithNothingInstalledEveryCardIsOffAndSaysWhy`'s title list.

- [ ] **Step 3: Run them to see them fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`
Expected: FAIL to compile.

- [ ] **Step 4: The records**

Create `src/Board/AccountsTableModel.cs`:

```csharp
namespace Labs626.UrScore.Board;

/// <summary>What a column of the accounts table holds: the account's name, a stat, or the sorted stat's change today or over 7 days.</summary>
public enum AccountColumnKind { Name, Stat, Today, Week }

/// <summary>One column: its key (a stat key, or <see cref="AccountSort.NameKey"/>), its heading, and whether rows are sorted by it.</summary>
public sealed record AccountColumn(string Key, string Header, AccountColumnKind Kind, bool Sorted = false, bool Descending = false)
{
    /// <summary>The change columns follow the sort; only the name and the stats take a click.</summary>
    public bool CanSort => Kind is AccountColumnKind.Name or AccountColumnKind.Stat;

    /// <summary>The heading as drawn: a sorted column ends in an arrow.</summary>
    public string Heading => Sorted ? $"{Header} {(Descending ? '↓' : '↑')}" : Header;
}

/// <summary>How one table is sorted this session (D15): a stat key or <see cref="NameKey"/>. Never saved.</summary>
public sealed record AccountSort(string Key, bool Descending)
{
    public const string NameKey = "";

    /// <summary>What a click on a heading does: the sorted column flips; another stat sorts highest first; the name A to Z.</summary>
    public static AccountSort Clicked(AccountColumn column) =>
        new(column.Key, column.Sorted ? !column.Descending : column.Kind == AccountColumnKind.Stat);
}

/// <summary>One of your accounts, or the totals row (user id 0). Cells line up with the columns, the name first.</summary>
public sealed record AccountRow(long UserId, string Name, IReadOnlyList<string> Cells, string Note, bool Missing, bool Picked, bool IsTotal = false)
{
    public bool HasNote => Note.Length > 0;

    /// <summary>UI Automation names a table row by this: your own account's name, or "Total".</summary>
    public override string ToString() => Name;
}

public sealed record AccountsTableModel(PanelHead Head, IReadOnlyList<AccountColumn> Columns, IReadOnlyList<AccountRow> Rows);

/// <summary>What a panel shows this session that is never saved: a table's sort, and the account picked on its board (D15, D18).</summary>
public sealed record PanelSession(AccountSort? Sort = null, long? PickedUserId = null);
```

- [ ] **Step 5: The builder**

In `src/Board/PanelModels.cs`, add `AccountsTable` as the last `PanelType` member, and add after `ProfileStat`:

```csharp
    /// <summary>
    /// Your accounts side by side (the Alts tab): a column per shown stat sorted as this session asks (D15), the sorted
    /// column's change today and over 7 days from the book (D16), a totals row and an account that couldn't be read
    /// saying why (D17). Only your own accounts: other rows a list recipe read are never looked at.
    /// </summary>
    public static AccountsTableModel AccountsTable(
        LiveBoard live, ScoreBookReader reader, PanelSettings settings, AccountSort? sort = null, long? pickedUserId = null)
    {
        var title = PanelText.Title(PanelType.AccountsTable, null, live.Installed);
        if (live.FindRecipe(settings.Recipe) is not { } installed) return new AccountsTableModel(StaleSource(live, settings, title), [], []);

        var recipe = installed.Recipe;
        bool OfRecipe(Source s) => string.Equals(s.Recipe, recipe.Slug, StringComparison.Ordinal);

        // A pinned source that's gone is stale; an unpinned table reads the recipe's first source that is on, else its first.
        var source = settings.SourceId is { } pinned
            ? live.FindSource(pinned)
            : live.Sources.FirstOrDefault(s => s.Enabled && OfRecipe(s)) ?? live.Sources.FirstOrDefault(OfRecipe);
        if (source is null) return new AccountsTableModel(StaleSource(live, settings, title), [], []);

        var stats = installed.State.ShownStats(recipe);
        var snapshot = live.SnapshotOf(source.Id);
        var zone = live.Time.LocalTimeZone;

        var effective = sort is { } asked && (asked.Key == AccountSort.NameKey || stats.Any(s => s.Key == asked.Key))
            ? asked
            : stats.Count > 0 ? new AccountSort(stats[0].Key, Descending: true) : new AccountSort(AccountSort.NameKey, Descending: false);
        var sorted = stats.FirstOrDefault(s => s.Key == effective.Key);
        var withChange = sorted is { Format: not StatFormat.Date };
        var byName = effective.Key == AccountSort.NameKey;

        var columns = new List<AccountColumn> { new(AccountSort.NameKey, "Account", AccountColumnKind.Name, byName, byName && effective.Descending) };
        foreach (var stat in stats)
        {
            var isSorted = stat.Key == effective.Key;
            columns.Add(new AccountColumn(stat.Key, stat.Label, AccountColumnKind.Stat, isSorted, isSorted && effective.Descending));
            if (!isSorted || !withChange) continue;

            columns.Add(new AccountColumn(stat.Key, "Today", AccountColumnKind.Today));
            columns.Add(new AccountColumn(stat.Key, "7 days", AccountColumnKind.Week));
        }

        var local = TimeZoneInfo.ConvertTime(live.Now, zone);
        var midnight = new DateTimeOffset(local.Date, local.Offset);
        var read = live.Accounts
            .Where(a => a.RobloxUserId != 0)
            .DistinctBy(a => a.RobloxUserId)
            .Select(a => (Account: a, Row: snapshot?.Rows?.FirstOrDefault(r => r.UserId == a.RobloxUserId)))
            .ToList();

        var rows = new List<(double? Sort, AccountRow Row)>();
        foreach (var (account, row) in read)
        {
            IReadOnlyList<SeriesPoint> series = sorted is not null && withChange
                ? reader.Series(source.Id, account.RobloxUserId, sorted.Key, null, DateTimeOffset.MinValue)
                : [];
            var changeFormat = sorted?.Format ?? StatFormat.Number;

            var cells = columns.Select(column => column.Kind switch
            {
                AccountColumnKind.Name => account.DisplayName,
                AccountColumnKind.Today => WindowGain(series, midnight, changeFormat, Dash),
                AccountColumnKind.Week => WindowGain(series, live.Now.AddDays(-7), changeFormat, Dash),
                _ => PanelText.Value(row is null ? null : ValueOf(row, column.Key), StatOf(stats, column.Key).Format, zone),
            }).ToList();

            rows.Add((sorted is null || row is null ? null : ValueOf(row, sorted.Key), new AccountRow(
                account.RobloxUserId,
                account.DisplayName,
                cells,
                snapshot?.Unavailable.GetValueOrDefault(account.RobloxUserId) ?? "",
                Missing: row is null,
                Picked: account.RobloxUserId == pickedUserId)));
        }

        var names = StringComparer.OrdinalIgnoreCase;
        IEnumerable<(double? Sort, AccountRow Row)> ordered = byName
            ? effective.Descending ? rows.OrderByDescending(r => r.Row.Name, names) : rows.OrderBy(r => r.Row.Name, names)
            : effective.Descending
                ? rows.OrderBy(r => r.Sort is null).ThenByDescending(r => r.Sort ?? 0).ThenBy(r => r.Row.Name, names)
                : rows.OrderBy(r => r.Sort is null).ThenBy(r => r.Sort ?? 0).ThenBy(r => r.Row.Name, names);
        var list = ordered.Select(r => r.Row).ToList();

        if (stats.Count > 0 && list.Count > 0)
        {
            var totals = columns.Select(column =>
            {
                if (column.Kind == AccountColumnKind.Name) return "Total";
                if (column.Kind != AccountColumnKind.Stat || StatOf(stats, column.Key) is not { Sum: true, Format: not StatFormat.Date } stat) return "";

                var values = read.Where(x => x.Row is not null).Select(x => ValueOf(x.Row!, stat.Key)).OfType<double>().ToList();
                return values.Count == 0 ? Dash : PanelText.Value(values.Sum(), stat.Format, zone);
            }).ToList();
            list.Add(new AccountRow(0, "Total", totals, "", Missing: false, Picked: false, IsTotal: true));
        }

        var note = stats.Count == 0 ? "Tick Show on a stat to fill this panel." : snapshot is null ? "Waiting for the first read." : "";
        return new AccountsTableModel(new PanelHead(title, live.SourceName(source), Overdue: live.IsOverdue(source), Note: note), columns, list);
    }

    private static RecipeStat StatOf(IReadOnlyList<RecipeStat> stats, string key) => stats.First(s => s.Key == key);
```

- [ ] **Step 6: Title, size, form, gallery**

`src/Board/PanelText.cs`, `Title`: add `PanelType.AccountsTable => "Accounts table",` before the `_` arm.

`src/Board/BoardDefs.cs`, `DefaultSize`: `PanelType.LiveLeaderboard or PanelType.AccountsTable => new PanelSize(PanelSize.Wide),`.

`src/Board/PanelForms.cs`:
- `Fits`: `PanelType.ProfileStat or PanelType.AccountsTable => recipe.Period is null,`
- `Build`: `var keepsStat = type is not (PanelType.Standing or PanelType.Race or PanelType.Top or PanelType.LiveLeaderboard or PanelType.AccountsTable);`
- `SourceProblem`, the unpinned case: `return type is PanelType.ProfileStat or PanelType.AccountsTable && live.Sources.Any(s => s.Enabled && s.Recipe == settings.Recipe) ? null : $"Choose a {word}.";` and its comment: `// A Profile stat or an Accounts table with no source reads the recipe's first source that is on; with none on, it must pin one.`

(`Fields` needs no change: its default arm asks for a source and adds nothing in ⋯.)

`src/Board/PanelGallery.cs`: `Order` gains `PanelType.AccountsTable` between `ProfileStat` and `LiveLeaderboard`; the card switch gains, before the `_` arm:

```csharp
                PanelType.AccountsTable => ($"Needs a {group} whose recipe reads without a period.",
                    "Your accounts side by side: a column per stat you show, sorted by any column, with totals and change.",
                    "Needs a ticked stat from a recipe that reads without a period."),
```

`src/UI/Boards/PanelSettingsWindow.xaml.cs`: `PanelType.Top or PanelType.ProfileStat or PanelType.AccountsTable => "source",`.

- [ ] **Step 7: The build gate**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror` then `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: both pass.

- [ ] **Step 8: Commit**

```bash
git add src/Board/AccountsTableModel.cs src/Board/PanelModels.cs src/Board/PanelText.cs src/Board/PanelForms.cs src/Board/PanelGallery.cs src/Board/BoardDefs.cs src/UI/Boards/PanelSettingsWindow.xaml.cs tests/PanelModelsTests.cs tests/PanelFormsTests.cs tests/PanelGalleryTests.cs
git commit -m "board: the accounts table's model - a column per shown stat, a session sort, change, totals and private rows

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: The Accounts table on the board: sort and pick for the session

**Files:**
- Create: `src/UI/Panels/AccountsTablePanel.xaml`, `src/UI/Panels/AccountsTablePanel.xaml.cs`, `src/UI/BoardWindow.Accounts.cs`
- Modify: `src/UI/Panels/PanelViews.cs`, `src/UI/BoardWindow.xaml.cs` (constructor, `RenderBoard`, `RenderPanel`), `src/UI/BoardWindow.PopOuts.cs` (`SyncPopOuts`, `RenderPopOuts`), `src/App.xaml` (two table styles)

**Interfaces:**
- Consumes: `PanelModels.AccountsTable`, `AccountSort.Clicked`, `AccountColumn`, `AccountRow`, `PanelSession` (Task 4); `PanelModels.AccountCard(..., pickedUserId)` (Task 3).
- Produces: `AccountsTablePanel` with `SortEvent`/`PickEvent`; `PanelViews.Render(..., PanelSession session)`; automation id `AccountsGrid`.

This task is WPF only; its proof is the build gate plus `walk-alts.ps1` (Task 9). The decisions it draws are unit-tested in Task 4.

- [ ] **Step 1: Table styles**

In `src/App.xaml`, after the `GridCheckBoxDisplay` style:

```xml
            <!-- A table that sits on a panel card (D14): no fill of its own, dividers between rows, headings like the panels' column headers. -->
            <Style x:Key="PanelTableHeader" TargetType="DataGridColumnHeader" BasedOn="{StaticResource {x:Type DataGridColumnHeader}}">
                <Setter Property="Background" Value="Transparent" />
                <Setter Property="FontSize" Value="10.5" />
                <Setter Property="Padding" Value="6,6" />
            </Style>
            <Style x:Key="PanelTable" TargetType="DataGrid" BasedOn="{StaticResource {x:Type DataGrid}}">
                <Setter Property="Background" Value="Transparent" />
                <Setter Property="RowBackground" Value="Transparent" />
                <Setter Property="AlternatingRowBackground" Value="Transparent" />
                <Setter Property="BorderThickness" Value="0" />
                <Setter Property="GridLinesVisibility" Value="Horizontal" />
                <Setter Property="IsReadOnly" Value="True" />
                <Setter Property="AutoGenerateColumns" Value="False" />
                <Setter Property="CanUserReorderColumns" Value="False" />
                <Setter Property="CanUserResizeRows" Value="False" />
                <Setter Property="SelectionUnit" Value="FullRow" />
                <Setter Property="ClipboardCopyMode" Value="IncludeHeader" />
                <Setter Property="HorizontalScrollBarVisibility" Value="Auto" />
                <Setter Property="VerticalScrollBarVisibility" Value="Disabled" />
                <Setter Property="ColumnHeaderStyle" Value="{StaticResource PanelTableHeader}" />
            </Style>
```

- [ ] **Step 2: The panel**

Create `src/UI/Panels/AccountsTablePanel.xaml`:

```xml
<UserControl x:Class="Labs626.UrScore.UI.AccountsTablePanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="clr-namespace:Labs626.UrScore.UI">
    <UserControl.Resources>
        <!-- The account column: the name, and under it why the account couldn't be read (D17). -->
        <DataTemplate x:Key="AccountNameCell">
            <StackPanel>
                <TextBlock Text="{Binding Name}" Style="{StaticResource RowCell}" />
                <TextBlock Text="{Binding Note}" Style="{StaticResource Muted}" FontSize="11" MaxWidth="240"
                           Visibility="{Binding HasNote, Converter={StaticResource BoolToVisible}}" />
            </StackPanel>
        </DataTemplate>
    </UserControl.Resources>
    <Border Style="{StaticResource PanelCard}">
        <StackPanel>
            <ui:PanelFrame DataContext="{Binding Head}" />
            <!-- Columns are built from the model in code: a stat key isn't a binding path. -->
            <DataGrid x:Name="AccountsGrid" Margin="0,10,0,0" Style="{StaticResource PanelTable}" ItemsSource="{Binding Rows}"
                      FrozenColumnCount="1"
                      Visibility="{Binding Head.HasBody, Converter={StaticResource BoolToVisible}}"
                      Sorting="OnSorting" SelectionChanged="OnSelectionChanged" PreviewMouseWheel="OnPreviewMouseWheel"
                      AutomationProperties.Name="Your accounts side by side">
                <DataGrid.RowStyle>
                    <Style TargetType="DataGridRow" BasedOn="{StaticResource {x:Type DataGridRow}}">
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding IsTotal}" Value="True">
                                <Setter Property="FontWeight" Value="SemiBold" />
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </DataGrid.RowStyle>
            </DataGrid>
            <TextBlock x:Name="PanelNote" Text="{Binding Head.Note}" Style="{StaticResource PanelNoteText}"
                       Visibility="{Binding Head.HasNote, Converter={StaticResource BoolToVisible}}" />
        </StackPanel>
    </Border>
</UserControl>
```

Create `src/UI/Panels/AccountsTablePanel.xaml.cs`:

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

/// <summary>A heading of the accounts table was clicked: the board sorts by it for this session (D15).</summary>
public sealed class AccountSortEventArgs(RoutedEvent routedEvent, AccountColumn column) : RoutedEventArgs(routedEvent)
{
    public AccountColumn Column { get; } = column;
}

/// <summary>An account's row was picked, by a click or the arrow keys: the account cards on its board show it (D18).</summary>
public sealed class AccountPickEventArgs(RoutedEvent routedEvent, long userId) : RoutedEventArgs(routedEvent)
{
    public long UserId { get; } = userId;
}

/// <summary>
/// Your accounts side by side (D14). It decides nothing: a heading click and a row pick are raised for the board, which
/// keeps them for the session and draws the table again from <see cref="PanelModels.AccountsTable"/>.
/// </summary>
public partial class AccountsTablePanel : UserControl
{
    public static readonly RoutedEvent SortEvent = EventManager.RegisterRoutedEvent(
        "Sort", RoutingStrategy.Bubble, typeof(EventHandler<AccountSortEventArgs>), typeof(AccountsTablePanel));

    public static readonly RoutedEvent PickEvent = EventManager.RegisterRoutedEvent(
        "Pick", RoutingStrategy.Bubble, typeof(EventHandler<AccountPickEventArgs>), typeof(AccountsTablePanel));

    private IReadOnlyList<AccountColumn> _columns = [];

    /// <summary>Set while a model is drawn, so selecting its picked row isn't taken for a new pick.</summary>
    private bool _rendering;

    public AccountsTablePanel() => InitializeComponent();

    public void Render(AccountsTableModel model)
    {
        _rendering = true;
        try
        {
            if (!model.Columns.SequenceEqual(_columns)) BuildColumns(model.Columns);
            _columns = model.Columns;
            DataContext = model;
            AccountsGrid.SelectedItem = model.Rows.FirstOrDefault(r => r.Picked);
        }
        finally
        {
            _rendering = false;
        }
    }

    /// <summary>One grid column per model column; a column's sort member is its index, so a click names the model column.</summary>
    private void BuildColumns(IReadOnlyList<AccountColumn> columns)
    {
        AccountsGrid.Columns.Clear();
        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            DataGridColumn built = column.Kind == AccountColumnKind.Name
                ? new DataGridTemplateColumn
                {
                    CellTemplate = (DataTemplate)FindResource("AccountNameCell"),
                    ClipboardContentBinding = new Binding(nameof(AccountRow.Name)),
                    MinWidth = 160,
                }
                : new DataGridTextColumn
                {
                    Binding = new Binding($"Cells[{index}]"),
                    ElementStyle = (Style)FindResource("NumberCell"),
                    MinWidth = column.Kind == AccountColumnKind.Stat ? 96 : 76,
                };

            built.Header = column.Heading;
            built.Width = DataGridLength.Auto;
            built.SortMemberPath = index.ToString(CultureInfo.InvariantCulture);
            built.CanUserSort = column.CanSort;
            AccountsGrid.Columns.Add(built);
        }
    }

    private void OnSorting(object sender, DataGridSortingEventArgs e)
    {
        // The grid never sorts its own text; the board sorts the numbers.
        e.Handled = true;
        if (int.TryParse(e.Column.SortMemberPath, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < _columns.Count)
        {
            RaiseEvent(new AccountSortEventArgs(SortEvent, _columns[index]));
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_rendering || AccountsGrid.SelectedItem is not AccountRow { IsTotal: false } row) return;

        RaiseEvent(new AccountPickEventArgs(PickEvent, row.UserId));
    }

    /// <summary>The table grows to all its rows and never scrolls up or down itself, so the wheel scrolls the board.</summary>
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;

        e.Handled = true;
        RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = this });
    }
}
```

- [ ] **Step 3: PanelViews knows the table and passes the session**

`src/UI/Panels/PanelViews.cs`: `Create` gains `PanelType.AccountsTable => new AccountsTablePanel(),` before the `_` arm. `Render` becomes:

```csharp
    public static void Render(
        FrameworkElement view, PanelSettings settings, LiveBoard live, ScoreBookReader reader, IReadOnlyDictionary<long, string> names,
        PanelSession session)
    {
        switch (view)
        {
            case StandingPanel panel: panel.Render(PanelModels.Standing(live, reader, settings)); break;
            case RacePanel panel: panel.Render(PanelModels.Race(live, reader, settings)); break;
            case MyAccountsPanel panel: panel.Render(PanelModels.MyAccounts(live, reader, settings)); break;
            case PromotionCheckPanel panel: panel.Render(PanelModels.PromotionCheck(live, settings)); break;
            case AccountCardPanel panel: panel.Render(PanelModels.AccountCard(live, reader, settings, session.PickedUserId)); break;
            case PastPeriodsPanel panel: panel.Render(PanelModels.PastPeriods(live, reader, settings)); break;
            case RecordsPanel panel: panel.Render(PanelModels.RecordsPanel(live, reader, settings)); break;
            case TopPanel panel: panel.Render(PanelModels.Top(live, settings)); break;
            case ProfileStatPanel panel: panel.Render(PanelModels.ProfileStat(live, reader, settings)); break;
            case AccountsTablePanel panel: panel.Render(PanelModels.AccountsTable(live, reader, settings, session.Sort, session.PickedUserId)); break;
            case LiveLeaderboardPanel panel: panel.Render(PanelModels.LiveLeaderboard(live, settings, names)); break;
        }
    }
```

- [ ] **Step 4: The board keeps the session's sorts and picks**

Create `src/UI/BoardWindow.Accounts.cs`:

```csharp
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

/// <summary>
/// What an accounts table asks for and the board keeps for this session only, never in <c>boards.json</c>: each table's
/// sort by panel id (D15), and the account picked on each board by board id (D18). A table popped out sorts and picks
/// as it does on its board.
/// </summary>
public partial class BoardWindow
{
    private readonly Dictionary<string, AccountSort> _sorts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _picked = new(StringComparer.Ordinal);

    private void HookAccounts()
    {
        BoardPanels.AddHandler(AccountsTablePanel.SortEvent, new EventHandler<AccountSortEventArgs>(OnAccountSort));
        BoardPanels.AddHandler(AccountsTablePanel.PickEvent, new EventHandler<AccountPickEventArgs>(OnAccountPick));
    }

    private void HookAccounts(PanelPopOutWindow window)
    {
        window.View.AddHandler(AccountsTablePanel.SortEvent, new EventHandler<AccountSortEventArgs>(OnAccountSort));
        window.View.AddHandler(AccountsTablePanel.PickEvent, new EventHandler<AccountPickEventArgs>(OnAccountPick));
    }

    /// <summary>What a panel on <paramref name="boardId"/> shows this session.</summary>
    private PanelSession SessionFor(string boardId, PanelDef def) =>
        new(_sorts.GetValueOrDefault(def.Id), _picked.TryGetValue(boardId, out var userId) ? userId : (long?)null);

    private void OnAccountSort(object? sender, AccountSortEventArgs e)
    {
        if (TableAt(e.OriginalSource) is not { } at) return;

        e.Handled = true;
        _sorts[at.PanelId] = AccountSort.Clicked(e.Column);
        Render();
    }

    private void OnAccountPick(object? sender, AccountPickEventArgs e)
    {
        if (TableAt(e.OriginalSource) is not { } at) return;

        e.Handled = true;
        if (_picked.TryGetValue(at.BoardId, out var current) && current == e.UserId) return;

        _picked[at.BoardId] = e.UserId;
        Render();
    }

    /// <summary>The panel and board of a table that raised an event: on the board on screen, or in a pop-out.</summary>
    private (string PanelId, string BoardId)? TableAt(object? source)
    {
        if (PanelAt(source) is { } def && _boardId is { } boardId) return (def.Id, boardId);

        foreach (var window in _popOuts.Values)
        {
            if (ReferenceEquals(window.View, source) && BoardEdits.Find(_services.Boards, window.PanelId) is { } found)
            {
                return (window.PanelId, found.Board.Id);
            }
        }

        return null;
    }
}
```

`src/UI/BoardWindow.xaml.cs`:
- constructor: `HookAccounts();` after `HookPopOuts();`
- `RenderBoard`: `foreach (var (def, view, _) in _panels) RenderPanel(def, view, live, board.Id);`
- `RenderPanel`:

```csharp
    private void RenderPanel(PanelDef def, FrameworkElement view, LiveBoard live, string boardId)
    {
        try
        {
            PanelViews.Render(view, def.Settings, live, _services.Reader, _names, SessionFor(boardId, def));
        }
        catch (Exception ex)
        {
            // A panel never takes the window down. The type only: a message could name another player's id.
            _services.AddTrail($"PANEL NOT DRAWN: {def.Type}: {ex.GetType().Name}");
        }
    }
```

`src/UI/BoardWindow.PopOuts.cs`:
- `SyncPopOuts`, after `window.Closed += OnPopOutClosed;`: `HookAccounts(window);`
- `RenderPopOuts`: `RenderPanel(found.Panel, window.View, live, found.Board.Id);`

- [ ] **Step 5: The build gate**

Close Ur Score. Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror` then `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: both pass (`ThemeFenceTests`: the styles use `Transparent` and theme brushes only).

- [ ] **Step 6: A quick look (controller)**

`dotnet build Ur-Score.csproj -c Release`, start `bin\Release\net10.0-windows\626labs.ur-score.exe` on a data folder with the profile recipe, add an Accounts table from the gallery on a new board, click a heading and a row with an Account card on the same board. The full walk is Task 9's `walk-alts.ps1`.

- [ ] **Step 7: Commit**

```bash
git add src/UI/Panels/AccountsTablePanel.xaml src/UI/Panels/AccountsTablePanel.xaml.cs src/UI/BoardWindow.Accounts.cs src/UI/Panels/PanelViews.cs src/UI/BoardWindow.xaml.cs src/UI/BoardWindow.PopOuts.cs src/App.xaml
git commit -m "board: the accounts table panel, with a sort and a picked account kept for the session

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: Battle and Alts, in rows that fill the grid

**Files:**
- Modify: `src/Board/StarterBoards.cs` (replaced), `src/Board/BoardLayout.cs` (`CellHeight`), `src/UI/Controls/PanelGrid.cs` (`ArrangeOverride`), `src/UI/Boards/AddBoardWindow.xaml`, `src/UI/Boards/AddBoardWindow.xaml.cs`
- Test: `tests/StarterBoardsTests.cs`, `tests/BoardLayoutTests.cs`

**Interfaces:**
- Consumes: `PanelType.AccountsTable` (Task 4).
- Produces: `StarterBoards.Alts`, `Names`, `KeyOf`, `All`, `Named`, `EmptyState`, `Build(..., name = Battle)`; `BoardLayout.CellHeight`; automation id `AltsBoardButton`.

Until Task 7, the one tab that follows your sources is still Battle (`AppServices` calls `Build` with its default); Alts is reachable from + Board.

- [ ] **Step 1: Write the failing tests**

In `tests/StarterBoardsTests.cs`, keep `WithNoRecipesTheBoardAsksForAnImport`, `RecipesWithNothingTickedAskForStats`, `AClanRecipeWithNoSourcesAsksForTheMainClan` and `TheFirstStatFallsBackToASentOneWhenNoneIsShown`. Replace `TheBattleBoardFollowsTheSpecsGridOrder`, `WithoutAMainTheFirstClanLeadsAndThereIsNoPromotionCheck`, `OneSourceHasNoRaceAndASwitchedOffTopListHasNoTopPanel`, `WithNoPeriodRecipeTheBoardIsGrind` and `AStarterCanBeAskedForByName` with:

```csharp
    [Fact]
    public void TheBattleTabHasTheDesignsFourRowsEachFillingTwelveColumns()
    {
        var slug = Clan.Slug;

        var board = StarterBoards.Build([Installed(Clan, "value"), Installed(TopList)], [AltClan, Rival, MainClan, SecondAltClan, TopSource], StarterBoards.Battle);

        Assert.Equal((StarterBoards.Battle, BoardEmpty.None), (board.Name, board.Empty));
        Assert.Equal(
            new[]
            {
                (PanelType.Standing, 4), (PanelType.Standing, 4), (PanelType.Top, 4),
                (PanelType.Race, 8), (PanelType.PromotionCheck, 4),
                (PanelType.MyAccounts, 12),
                (PanelType.PastPeriods, 7), (PanelType.Records, 5),
            },
            board.Panels.Select(p => (p.Type, p.Span)).ToArray());
        Assert.Equal(new PanelSettings(slug, SourceId: MainClan.Id), board.Panels[0].Settings);
        Assert.Equal(new PanelSettings(slug, SourceId: AltClan.Id), board.Panels[1].Settings);
        Assert.Equal(new PanelSettings(TopList.Slug, SourceId: TopSource.Id), board.Panels[2].Settings);
        Assert.Equal(new[] { MainClan.Id, AltClan.Id, SecondAltClan.Id, Rival.Id }, board.Panels[3].Settings.SourceIds!.ToArray());
        Assert.Equal(new PanelSettings(slug, SourceId: AltClan.Id, ToSourceId: MainClan.Id, Stat: "value"), board.Panels[4].Settings);
        Assert.Equal(new PanelSettings(slug, Stat: "value"), board.Panels[5].Settings);
        Assert.Equal(new PanelSettings(slug, SourceId: MainClan.Id, Stat: "value"), board.Panels[6].Settings);
        Assert.Equal(new PanelSettings(slug, Stat: "value"), board.Panels[7].Settings);

        var rows = BoardLayout.Flow(board.Panels.Select(p => p.Span).ToList(), 1280).GroupBy(p => p.Row);
        Assert.All(rows, row => Assert.Equal(BoardLayout.Columns, row.Sum(p => p.Span)));
    }

    [Fact]
    public void ARowWithAPanelMissingClosesUpIntoEqualShares()
    {
        // One clan and a switched-off top list: its standing takes row 1 alone; there's no race or promotion check.
        var one = StarterBoards.Build([Installed(Clan, "value"), Installed(TopList)], [MainClan, TopSource with { Enabled = false }], StarterBoards.Battle);
        Assert.Equal(new[] { (PanelType.Standing, 12), (PanelType.MyAccounts, 12), (PanelType.PastPeriods, 7), (PanelType.Records, 5) },
            one.Panels.Select(p => (p.Type, p.Span)).ToArray());

        // Two clans your accounts are in and no main: the first leads, both share row 1, the race takes row 2 alone.
        var two = StarterBoards.Build([Installed(Clan, "value")], [AltClan, SecondAltClan], StarterBoards.Battle);
        Assert.Equal(
            new[] { (PanelType.Standing, 6), (PanelType.Standing, 6), (PanelType.Race, 12), (PanelType.MyAccounts, 12), (PanelType.PastPeriods, 7), (PanelType.Records, 5) },
            two.Panels.Select(p => (p.Type, p.Span)).ToArray());
        Assert.Equal(AltClan.Id, two.Panels[0].Settings.SourceId);
    }

    [Fact]
    public void TheAltsTabIsTheAccountsTableThenRecordsAndTheAccountCard()
    {
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);

        var board = StarterBoards.Build([Installed(Clan, "value"), Installed(Profile, "diamonds", "eggs", "rank")], [MainClan, profile], StarterBoards.Alts);

        Assert.Equal((StarterBoards.Alts, BoardEmpty.None), (board.Name, board.Empty));
        Assert.Equal(new[] { (PanelType.AccountsTable, 12), (PanelType.Records, 5), (PanelType.AccountCard, 7) }, board.Panels.Select(p => (p.Type, p.Span)).ToArray());
        Assert.Equal(new PanelSettings(Profile.Slug, SourceId: profile.Id), board.Panels[0].Settings);
        Assert.Equal(new PanelSettings(Profile.Slug, Stat: "diamonds"), board.Panels[1].Settings);
        Assert.Equal(new PanelSettings(Profile.Slug, Stat: "diamonds"), board.Panels[2].Settings);
    }

    [Fact]
    public void EachStarterNeedsItsKindOfRecipeAndAllListsBothInTabOrder()
    {
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);

        var clanOnly = StarterBoards.All([Installed(Clan, "value")], [MainClan]);
        Assert.Equal(new[] { StarterBoards.Battle, StarterBoards.Alts }, clanOnly.Select(s => s.Name).ToArray());
        Assert.NotEmpty(clanOnly[0].Panels);
        Assert.Equal((BoardEmpty.NoStats, 0), (clanOnly[1].Empty, clanOnly[1].Panels.Count));

        var profileOnly = StarterBoards.All([Installed(Profile, "diamonds")], [profile]);
        Assert.Empty(profileOnly[0].Panels);
        Assert.NotEmpty(profileOnly[1].Panels);

        Assert.Same(profileOnly[1], StarterBoards.Named(profileOnly, "ALTS"));
        Assert.Null(StarterBoards.Named(profileOnly, "grind"));
        Assert.Equal("battle", StarterBoards.KeyOf(StarterBoards.Battle));

        // With nothing to show anywhere, the empty state asks for what's missing first.
        Assert.Equal(BoardEmpty.NoRecipes, StarterBoards.EmptyState(StarterBoards.All([], [])).Empty);
        var noMain = StarterBoards.All([Installed(Clan, "value"), Installed(Profile)], []);
        Assert.Equal((StarterBoards.Battle, BoardEmpty.NoSources), (StarterBoards.EmptyState(noMain).Name, StarterBoards.EmptyState(noMain).Empty));
    }
```

`tests/BoardLayoutTests.cs`:

```csharp
    [Fact]
    public void APanelIsArrangedAsTallAsItsRowsAndTheGapsBetweenThem()
    {
        IReadOnlyList<double> rows = [120, 80];

        Assert.Equal(120, BoardLayout.CellHeight(new PanelPlacement(0, 0, 0, 6), rows, 12));
        Assert.Equal(80, BoardLayout.CellHeight(new PanelPlacement(1, 1, 0, 12), rows, 12));
        Assert.Equal(212, BoardLayout.CellHeight(new PanelPlacement(2, 0, 6, 6, Rows: 2), rows, 12));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`
Expected: FAIL to compile (`Alts`, `All`, `Named`, `EmptyState`, `KeyOf`, `CellHeight` don't exist).

- [ ] **Step 3: The starters**

Replace `src/Board/StarterBoards.cs` with:

```csharp
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

using Source = Labs626.UrScore.Core.Source;

/// <summary>One panel on a board: its type, how many of the 12 columns it spans, and what it shows.</summary>
public sealed record PanelSpec(PanelType Type, int Span, PanelSettings Settings);

public enum BoardEmpty { None, NoRecipes, NoStats, NoSources, NoPanels }

/// <summary>A starter board's panels, or the empty state it shows instead, and the recipe that empty state names.</summary>
public sealed record StarterBoard(string Name, BoardEmpty Empty, IReadOnlyList<PanelSpec> Panels, string? RecipeSlug);

/// <summary>
/// The default tabs (default views design): Battle, the tab you watch on battle day, and Alts, your accounts side by
/// side. Each is built from your recipes and sources as they are now, in rows that fill the 12 columns and close up
/// when a panel can't be built (D6). Pure, so which panels appear for which sources is testable.
/// </summary>
public static class StarterBoards
{
    public const string Battle = "Battle";
    public const string Alts = "Alts";

    /// <summary>Every starter, in tab order.</summary>
    public static IReadOnlyList<string> Names { get; } = [Battle, Alts];

    /// <summary>A starter's name in ids and in <c>boards.json</c>'s <c>follows</c>: "battle", "alts".</summary>
    public static string KeyOf(string name) => name.ToLowerInvariant();

    /// <summary>Every starter as your sources build it now, in tab order. Any of them may be an empty state.</summary>
    public static IReadOnlyList<StarterBoard> All(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources) =>
        [.. Names.Select(name => Build(installed, sources, name))];

    /// <summary>The starter a key names ("alts", in any letter case), or null.</summary>
    public static StarterBoard? Named(IReadOnlyList<StarterBoard> starters, string? key) =>
        key is null ? null : starters.FirstOrDefault(s => string.Equals(KeyOf(s.Name), key.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The empty state shown when no starter has a panel (D4): import a recipe first, then choose the source a starter
    /// needs, else the first starter's own.
    /// </summary>
    public static StarterBoard EmptyState(IReadOnlyList<StarterBoard> starters) =>
        starters.FirstOrDefault(s => s.Empty == BoardEmpty.NoRecipes)
        ?? starters.FirstOrDefault(s => s.Empty == BoardEmpty.NoSources)
        ?? starters[0];

    /// <summary>
    /// One starter: Battle from a recipe with a period, Alts from one without (your accounts one by one first). A
    /// starter whose kind of recipe has no ticked stat is empty. Any name but Alts builds Battle.
    /// </summary>
    public static StarterBoard Build(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources, string name = Battle)
    {
        var alts = string.Equals(name, Alts, StringComparison.Ordinal);
        var named = alts ? Alts : Battle;
        if (installed.Count == 0) return new StarterBoard(named, BoardEmpty.NoRecipes, [], null);

        var ticked = installed.Where(i => !i.Recipe.IsGroupList && i.State.TrackedStats(i.Recipe).Count > 0).ToList();
        if (ticked.Count == 0)
        {
            var first = installed.FirstOrDefault(i => !i.Recipe.IsGroupList) ?? installed[0];
            return new StarterBoard(named, BoardEmpty.NoStats, [], first.Recipe.Slug);
        }

        var enabled = sources.Where(s => s.Enabled).ToList();
        var kind = ticked.Where(i => (i.Recipe.Period is null) == alts).ToList();
        if (kind.Count == 0) return new StarterBoard(named, BoardEmpty.NoStats, [], ticked[0].Recipe.Slug);

        return alts ? AltsBoard(kind, enabled) : BattleBoard(installed, kind, enabled);
    }

    /// <summary>The first shown stat in recipe order, else the first sent one.</summary>
    public static string? FirstStat(InstalledRecipe installed)
    {
        var shown = installed.State.ShownStats(installed.Recipe);
        if (shown.Count > 0) return shown[0].Key;

        var tracked = installed.State.TrackedStats(installed.Recipe);
        return RecipeStats.Offered(installed.Recipe, installed.State.StatChoices.Keys).FirstOrDefault(s => tracked.Contains(s.Key))?.Key;
    }

    private static StarterBoard BattleBoard(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<InstalledRecipe> withPeriod, IReadOnlyList<Source> enabled)
    {
        bool Of(Source s, InstalledRecipe r) => string.Equals(s.Recipe, r.Recipe.Slug, StringComparison.Ordinal);

        var main = enabled.FirstOrDefault(s => s.Role == SourceRole.Main && withPeriod.Any(r => Of(s, r)));
        var recipe = main is not null
            ? withPeriod.First(r => Of(main, r))
            : withPeriod.FirstOrDefault(r => enabled.Any(s => Of(s, r))) ?? withPeriod[0];
        var slug = recipe.Recipe.Slug;

        var mine = enabled.Where(s => Of(s, recipe) && s.Role == SourceRole.Mine).ToList();
        var watch = enabled.Where(s => Of(s, recipe) && s.Role == SourceRole.Watch).ToList();
        var anchor = main ?? mine.FirstOrDefault() ?? watch.FirstOrDefault();

        if (anchor is null && recipe.Recipe.Inputs.Count > 0) return new StarterBoard(Battle, BoardEmpty.NoSources, [], slug);

        var stat = FirstStat(recipe);
        var otherMine = mine.FirstOrDefault(s => s.Id != anchor?.Id);
        var top = enabled.FirstOrDefault(s => installed.Any(i => Of(s, i) && i.Recipe.IsGroupList));
        var race = new[] { main }.OfType<Source>().Concat(mine).Concat(watch)
            .DistinctBy(s => s.Id).Take(PanelModels.MaxRace).Select(s => s.Id).ToList();

        var panels = new List<PanelSpec>();

        // 1. Your main clan's standing, a clan your accounts are in, and the top of the battle.
        panels.AddRange(Row(
            (PanelType.Standing, 4, anchor is null ? null : new PanelSettings(slug, SourceId: anchor.Id)),
            (PanelType.Standing, 4, otherMine is null ? null : new PanelSettings(slug, SourceId: otherMine.Id)),
            (PanelType.Top, 4, top is null ? null : new PanelSettings(top.Recipe, SourceId: top.Id))));

        // 2. The race, wide, and the promotion check from that clan to the main.
        panels.AddRange(Row(
            (PanelType.Race, 8, race.Count >= 2 ? new PanelSettings(slug, SourceIds: race) : null),
            (PanelType.PromotionCheck, 4, main is not null && otherMine is not null && stat is not null
                ? new PanelSettings(slug, SourceId: otherMine.Id, ToSourceId: main.Id, Stat: stat)
                : null)));

        // 3. My accounts, full width.
        panels.AddRange(Row((PanelType.MyAccounts, 12, stat is null ? null : new PanelSettings(slug, Stat: stat))));

        // 4. Past battles of the main clan, and records.
        panels.AddRange(Row(
            (PanelType.PastPeriods, 7, anchor is not null && recipe.Recipe.Period?.Past is not null ? new PanelSettings(slug, SourceId: anchor.Id, Stat: stat) : null),
            (PanelType.Records, 5, stat is null ? null : new PanelSettings(slug, Stat: stat))));

        return new StarterBoard(Battle, BoardEmpty.None, panels, slug);
    }

    private static StarterBoard AltsBoard(IReadOnlyList<InstalledRecipe> withoutPeriod, IReadOnlyList<Source> enabled)
    {
        bool Of(Source s, InstalledRecipe r) => string.Equals(s.Recipe, r.Recipe.Slug, StringComparison.Ordinal);

        // A recipe that reads your accounts one by one first, then any other without a period; one with a source on first.
        var ordered = withoutPeriod.OrderBy(r => r.Recipe.LastStep.PerAccount ? 0 : 1).ToList();
        var recipe = ordered.FirstOrDefault(r => enabled.Any(s => Of(s, r))) ?? ordered[0];
        var slug = recipe.Recipe.Slug;
        var source = enabled.FirstOrDefault(s => Of(s, recipe));

        if (source is null && recipe.Recipe.Inputs.Count > 0) return new StarterBoard(Alts, BoardEmpty.NoSources, [], slug);

        var stat = FirstStat(recipe);
        var panels = new List<PanelSpec>();

        // 1. The accounts table, full width.
        panels.AddRange(Row((PanelType.AccountsTable, 12, new PanelSettings(slug, SourceId: source?.Id))));

        // 2. Records and the account card, which shows the account picked in the table.
        panels.AddRange(Row(
            (PanelType.Records, 5, stat is null ? null : new PanelSettings(slug, Stat: stat)),
            (PanelType.AccountCard, 7, stat is null ? null : new PanelSettings(slug, Stat: stat))));

        return new StarterBoard(Alts, BoardEmpty.None, panels, slug);
    }

    /// <summary>
    /// One row (D6): the panels that could be built keep the row's spans when all of them could, else share the
    /// 12 columns equally, so a missing panel never leaves a hole.
    /// </summary>
    private static IEnumerable<PanelSpec> Row(params (PanelType Type, int Span, PanelSettings? Settings)[] slots)
    {
        var present = slots.Where(s => s.Settings is not null).ToList();
        var share = BoardLayout.Columns / Math.Max(1, present.Count);
        return present.Select(s => new PanelSpec(s.Type, present.Count == slots.Length ? s.Span : share, s.Settings!));
    }
}
```

- [ ] **Step 4: A panel fills its row**

`src/Board/BoardLayout.cs`, after `RowHeights`:

```csharp
    /// <summary>How tall a panel is arranged: its rows and the gaps between them, so a short panel fills its row (D7).</summary>
    public static double CellHeight(PanelPlacement placement, IReadOnlyList<double> rows, double gap) =>
        Enumerable.Range(placement.Row, placement.Rows).Sum(r => rows[r]) + gap * (placement.Rows - 1);
```

`src/UI/Controls/PanelGrid.cs`, `ArrangeOverride`: replace the `var height = placement.Rows == 1 ? ... ;` statement with `var height = BoardLayout.CellHeight(placement, rows, Gap);`.

- [ ] **Step 5: + Board offers Alts**

`src/UI/Boards/AddBoardWindow.xaml`: `<Button x:Name="AltsBoardButton" Margin="0,8,0,0" Click="OnAltsClick" />` replaces the Grind button.

`src/UI/Boards/AddBoardWindow.xaml.cs`: the field `_grind` becomes `_alts = StarterBoards.Build(installed, sources, StarterBoards.Alts);`, `Describe(AltsBoardButton, _alts);`, the starter line tests `_alts`, and `private void OnAltsClick(object sender, RoutedEventArgs e) => FinishStarter(_alts);` replaces `OnGrindClick`.

- [ ] **Step 6: The build gate**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror` then `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: both pass. `grep -rn "Grind" src tests` finds only `BoardsFileTests`' saved board named "Grind" (a user's own board name).

- [ ] **Step 7: Commit**

```bash
git add src/Board/StarterBoards.cs src/Board/BoardLayout.cs src/UI/Controls/PanelGrid.cs src/UI/Boards/AddBoardWindow.xaml src/UI/Boards/AddBoardWindow.xaml.cs tests/StarterBoardsTests.cs tests/BoardLayoutTests.cs
git commit -m "board: Battle and Alts starters in rows that fill twelve columns and close up, and panels as tall as their row

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 7: Each tab follows your sources until you change that tab

**Files:**
- Create: `src/Board/Following.cs`, `tests/FollowingTests.cs`
- Modify: `src/Board/BoardDefs.cs` (`BoardDef.Follows`, `StarterBoardId`, `FromStarter`, `Following`), `src/Board/BoardsFile.cs` (`follows`), `src/Board/BoardEdits.cs` (remove `ForFirstSave`), `src/UI/BoardText.cs` (`EmptyFor`), `src/UI/BoardWindow.xaml.cs` (`RenderEmpty`), `src/Composition/AppServices.cs` (`Boards`, `SaveBoards`, remove `BoardsFollowStarter`)
- Test: `tests/BoardsFileTests.cs`, `tests/BoardEditsTests.cs`, `tests/BoardTextTests.cs`

**Interfaces:**
- Consumes: `StarterBoards.All/Named/EmptyState/KeyOf` (Task 6); `BoardEdits.Changed` (stage 2).
- Produces: `BoardDef.Follows`; `BoardDefs.StarterBoardId(string)`, `BoardDefs.Following(StarterBoard)`; `Following.Shown`, `Following.ToSave`; `BoardText.EmptyFor(starters, board, editing)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/FollowingTests.cs`:

```csharp
using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

public class FollowingTests
{
    private static readonly Source MainClan = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
    private static readonly Source AltClan = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
    private static readonly Source ProfileSource = SourceOf("s-00000009", Profile, null, SourceRole.Mine);

    private static IReadOnlyList<StarterBoard> Both() =>
        StarterBoards.All([Installed(Clan, "value"), Installed(Profile, "diamonds")], [MainClan, AltClan, ProfileSource]);

    private static IReadOnlyList<StarterBoard> BattleOnly() => StarterBoards.All([Installed(Clan, "value")], [MainClan, AltClan]);

    [Fact]
    public void WithNothingSavedEveryStarterWithPanelsIsATabThatFollows()
    {
        var shown = Following.Shown(null, Both());

        (string Id, string Name, string? Follows)[] expected = [("b-starter-battle", "Battle", "battle"), ("b-starter-alts", "Alts", "alts")];
        Assert.Equal(expected, shown.Select(b => (b.Id, b.Name, b.Follows)).ToArray());
        Assert.Equal("p-alts-1", shown[1].Panels[0].Id);
        Assert.Equal(new[] { "b-starter-battle" }, Following.Shown(null, BattleOnly()).Select(b => b.Id).ToArray());
    }

    [Fact]
    public void WhenNoStarterHasPanelsOneFollowingBoardShowsTheEmptyState()
    {
        var none = Assert.Single(Following.Shown(null, StarterBoards.All([], [])));
        Assert.Equal(("b-starter-battle", 0), (none.Id, none.Panels.Count));
        Assert.Equal("battle", none.Follows);

        // A clan recipe with no main and a profile recipe with nothing ticked: Battle asks for the main.
        var noMain = StarterBoards.All([Installed(Clan, "value"), Installed(Profile)], []);
        Assert.Equal("b-starter-battle", Assert.Single(Following.Shown(null, noMain)).Id);
    }

    [Fact]
    public void AChangeToOneTabWritesThatTabAndTheOtherKeepsFollowing()
    {
        var starters = Both();
        var shown = Following.Shown(null, starters);
        var alts = BoardEdits.RemovePanel(shown[1], shown[1].Panels[1].Id);

        var saved = Following.ToSave(null, starters, BoardEdits.Replace(shown, alts));

        Assert.Equal(("b-starter-battle", 0), (saved[0].Id, saved[0].Panels.Count));
        Assert.Equal("battle", saved[0].Follows);
        Assert.Null(saved[1].Follows);
        Assert.Equal(shown[1].Panels.Count - 1, saved[1].Panels.Count);

        // Read back later: Battle is rebuilt from the sources as they are then; Alts stays as it was written.
        var later = StarterBoards.All([Installed(Clan, "value"), Installed(Profile, "diamonds")], [MainClan, ProfileSource]);
        var again = Following.Shown(saved, later);
        Assert.Equal(later[0].Panels.Count, again[0].Panels.Count);
        Assert.Equal(saved[1], again[1]);
    }

    [Fact]
    public void AnUntouchedTabStaysFollowingAndARenameOrAPopOutIsAChange()
    {
        var starters = Both();
        var shown = Following.Shown(null, starters);
        var added = new BoardDef("b-00000001", "Board 3", []);

        var saved = Following.ToSave(null, starters, [.. shown, added]);
        Assert.Equal(new[] { "battle", "alts", null }, saved.Select(b => b.Follows).ToArray());
        Assert.All(saved.Take(2), board => Assert.Empty(board.Panels));

        var renamed = Following.ToSave(null, starters, BoardEdits.Rename(shown, "b-starter-alts", "Grinding"));
        Assert.Null(renamed[1].Follows);
        Assert.Equal("Grinding", renamed[1].Name);

        var popped = Following.ToSave(null, starters,
            BoardEdits.Replace(shown, BoardEdits.PopOut(shown[0], shown[0].Panels[0].Id, new PopOutRect(10, 10, 360, 300))));
        Assert.Null(popped[0].Follows);
        Assert.Equal("p-battle-1", popped[0].Panels[0].Id);
        Assert.Equal("alts", popped[1].Follows);
    }

    [Fact]
    public void ADeletedTabIsGoneForGoodButAHiddenOneKeepsFollowing()
    {
        // Only the clan recipe: Alts has nothing to show and is hidden, so + Board doesn't delete it.
        var starters = BattleOnly();
        var shown = Following.Shown(null, starters);
        var rivals = new BoardDef("b-00000001", "Rivals", []);

        var saved = Following.ToSave(null, starters, [.. shown, rivals]);
        (string Id, string? Follows)[] expected = [("b-starter-battle", "battle"), ("b-00000001", null), ("b-starter-alts", "alts")];
        Assert.Equal(expected, saved.Select(b => (b.Id, b.Follows)).ToArray());

        // The profile recipe arrives: Alts appears, after the boards you have.
        Assert.Equal(new[] { "Battle", "Rivals", "Alts" }, Following.Shown(saved, Both()).Select(b => b.Name).ToArray());

        // Battle deleted while it showed: it doesn't come back.
        var deleted = Following.ToSave(saved, Both(), BoardEdits.Delete(Following.Shown(saved, Both()), "b-starter-battle"));
        Assert.DoesNotContain(deleted, b => b.Id == "b-starter-battle");
        Assert.Equal(new[] { "Rivals", "Alts" }, Following.Shown(deleted, Both()).Select(b => b.Name).ToArray());
    }

    [Fact]
    public void AFirstRunBoardAddedBesideTheEmptyStateDoesntFreezeIt()
    {
        var none = StarterBoards.All([], []);
        var added = new BoardDef("b-00000001", "Board 2", []);

        var saved = Following.ToSave(null, none, [.. Following.Shown(null, none), added]);

        Assert.Equal(new[] { "battle", null, "alts" }, saved.Select(b => b.Follows).ToArray());
        Assert.Equal(new[] { "Board 2" }, Following.Shown(saved, none).Select(b => b.Name).ToArray());
    }

    [Fact]
    public void AHandEditedFileCantShowOneStarterTwice()
    {
        IReadOnlyList<BoardDef> saved = [new BoardDef("b-starter-alts", "Alts", []), new BoardDef("b-x", "Alts", [], Follows: "alts")];

        Assert.Single(Following.Shown(saved, Both()), b => b.Id == "b-starter-alts");
    }
}
```

`tests/BoardsFileTests.cs`: replace `TheFollowingStarterHasFixedIdsAndAFreshCopyHasNewOnes` with:

```csharp
    [Fact]
    public void AFollowingStarterHasFixedIdsAndAFreshCopyHasNewOnes()
    {
        var starter = StarterBoards.Build([Installed(Clan, "value")], [MainClan, AltClan]);

        var following = BoardDefs.Following(starter);
        var fresh = BoardDefs.FromStarter(starter, freshIds: true);

        Assert.Equal(("b-starter-battle", StarterBoards.Battle), (following.Id, following.Name));
        Assert.Equal("battle", following.Follows);
        Assert.Equal(starter.Panels.Select((_, i) => $"p-battle-{i + 1}"), following.Panels.Select(p => p.Id));
        Assert.Equal(starter.Panels.Select(p => (p.Type, p.Span, p.Settings)), following.Panels.Select(p => (p.Type, p.Size.Span, p.Settings)));
        Assert.All(following.Panels, p => Assert.False(p.Size.Tall));
        Assert.Matches("^b-[0-9a-f]{8}$", fresh.Id);
        Assert.All(fresh.Panels, p => Assert.Matches("^p-[0-9a-f]{8}$", p.Id));
        Assert.Null(fresh.Follows);
    }

    [Fact]
    public void AFollowingTabIsWrittenByNameAloneAndOnlyAKnownStarterFollows()
    {
        IReadOnlyList<BoardDef> boards = [new BoardDef("b-starter-battle", "Battle", [], Follows: "battle"), new BoardDef("b-00000001", "Rivals", [])];

        var json = BoardsFile.Serialize(boards);
        Assert.Contains("\"follows\": \"battle\"", json);
        Assert.DoesNotContain("\"follows\": null", json);
        Assert.Equal(new[] { "battle", null }, BoardsFile.Parse(json).Select(b => b.Follows).ToArray());

        // An unknown starter follows nothing; a second board following the same starter follows nothing either.
        var odd = BoardsFile.Parse("""
            [ { "id": "a", "name": "A", "follows": "grind", "panels": [] },
              { "id": "b", "name": "B", "follows": "ALTS" },
              { "id": "c", "name": "C", "follows": "alts" } ]
            """);
        Assert.Equal(new[] { null, "alts", null }, odd.Select(b => b.Follows).ToArray());
    }
```

`tests/BoardEditsTests.cs`: delete `AnEmptyFollowingStarterIsNotWrittenByTheFirstChangeElsewhere` (its cases are `FollowingTests.AFirstRunBoardAddedBesideTheEmptyStateDoesntFreezeIt` and `AnUntouchedTabStaysFollowingAndARenameOrAPopOutIsAChange`).

`tests/BoardTextTests.cs`: replace `NoRecipesShowsOverEveryBoardAndTheStartersStatesOnlyWhileItFollows` with:

```csharp
    [Fact]
    public void NoRecipesShowsOverEveryBoardAndAStartersStatesOnlyOnATabThatFollowsIt()
    {
        var noStats = StarterBoards.All([Installed(Clan)], [MainClan]);
        var following = BoardDefs.Following(noStats[0]);
        var saved = new BoardDef("b-00000001", "Rivals", []);
        var withPanel = saved with
        {
            Panels = [new PanelDef("p-00000001", PanelType.Standing, new PanelSize(3), new PanelSettings(Clan.Slug, SourceId: MainClan.Id))],
        };

        Assert.Equal(BoardEmpty.NoRecipes, BoardText.EmptyFor(StarterBoards.All([], []), withPanel));
        Assert.Equal(BoardEmpty.NoStats, BoardText.EmptyFor(noStats, following));
        Assert.Equal(BoardEmpty.NoPanels, BoardText.EmptyFor(noStats, following, editing: true));
        Assert.Equal(BoardEmpty.NoPanels, BoardText.EmptyFor(noStats, saved));
        Assert.Equal(BoardEmpty.None, BoardText.EmptyFor(noStats, withPanel));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`
Expected: FAIL to compile (`Following`, `BoardDef.Follows`, `BoardDefs.Following` don't exist).

- [ ] **Step 3: Board definitions**

`src/Board/BoardDefs.cs`:

```csharp
/// <summary>
/// One tab (spec §9.2). Holds no other player: see <see cref="BoardDefs.Sanitize"/>. <see cref="Follows"/> names the
/// starter it follows ("battle", "alts"): while set, its panels are rebuilt from your sources and aren't saved (D2).
/// </summary>
public sealed record BoardDef(string Id, string Name, IReadOnlyList<PanelDef> Panels, string? Follows = null);
```

Replace the `StarterBoardId` constant and `FromStarter` with:

```csharp
    /// <summary>A following starter's board id (D1): "b-starter-battle", "b-starter-alts".</summary>
    public static string StarterBoardId(string starterName) => "b-starter-" + StarterBoards.KeyOf(starterName);

    /// <summary>
    /// A starter as a board. With fixed ids (freshIds false) it is "b-starter-alts" with panels "p-alts-1".., so a pop-out
    /// made on a following tab survives the write (D1); a starter added with + Board gets new ones. Follows nothing.
    /// </summary>
    public static BoardDef FromStarter(StarterBoard starter, bool freshIds)
    {
        var key = StarterBoards.KeyOf(starter.Name);
        return new BoardDef(
            freshIds ? NewBoardId() : StarterBoardId(starter.Name),
            starter.Name,
            [.. starter.Panels.Select((panel, index) => new PanelDef(
                freshIds ? NewPanelId() : $"p-{key}-{index + 1}",
                panel.Type,
                new PanelSize(Math.Clamp(panel.Span, 1, BoardLayout.Columns)),
                panel.Settings))]);
    }

    /// <summary>A starter as the tab that follows it (D2).</summary>
    public static BoardDef Following(StarterBoard starter) =>
        FromStarter(starter, freshIds: false) with { Follows = StarterBoards.KeyOf(starter.Name) };
```

- [ ] **Step 4: Following**

Create `src/Board/Following.cs`:

```csharp
namespace Labs626.UrScore.Board;

/// <summary>
/// The starter tabs that follow your sources (D2–D4). A board with <see cref="BoardDef.Follows"/> is rebuilt from its
/// starter on every read and shows only while the starter has panels. The first change to that board writes it as it
/// is, and it follows no more; every other following tab keeps following.
/// </summary>
public static class Following
{
    /// <summary>
    /// The boards the window shows, in order. With nothing saved every starter follows. When nothing would show, one
    /// following board carries the first-run empty state (D4).
    /// </summary>
    public static IReadOnlyList<BoardDef> Shown(IReadOnlyList<BoardDef>? saved, IReadOnlyList<StarterBoard> starters)
    {
        var all = All(saved, starters);
        var visible = all.Where(b => b.Follows is null || b.Panels.Count > 0).ToList();
        if (visible.Count > 0) return visible;

        var followed = starters.Where(s => all.Any(b => b.Follows == StarterBoards.KeyOf(s.Name))).ToList();
        return [BoardDefs.Following(StarterBoards.EmptyState(followed.Count > 0 ? followed : starters))];
    }

    /// <summary>
    /// What <c>boards.json</c> holds after <paramref name="edited"/>, a changed copy of <see cref="Shown"/>. A following board
    /// drawn as its starter draws it now stays a following entry, with no panels; a changed one (a panel, a size, a
    /// setting, a name, a pop-out) is written as it is and follows no more. A following board that was showing and is
    /// gone was deleted; one hidden because its starter had nothing to show was never on screen, and keeps following.
    /// </summary>
    public static IReadOnlyList<BoardDef> ToSave(IReadOnlyList<BoardDef>? saved, IReadOnlyList<StarterBoard> starters, IReadOnlyList<BoardDef> edited)
    {
        var all = All(saved, starters);
        var following = all.Where(b => b.Follows is not null).ToDictionary(b => b.Id, StringComparer.Ordinal);
        var shown = Shown(saved, starters).Select(b => b.Id).ToHashSet(StringComparer.Ordinal);

        var result = edited
            .Select(board => following.TryGetValue(board.Id, out var built) && !BoardEdits.Changed(built, board)
                ? Entry(built)
                : board with { Follows = null })
            .ToList();

        foreach (var hidden in all.Where(b => b.Follows is not null && !shown.Contains(b.Id)))
        {
            if (result.All(b => b.Id != hidden.Id)) result.Add(Entry(hidden));
        }

        return result;
    }

    /// <summary>Saved boards with each following entry rebuilt from its starter; one whose id a saved board already has is dropped.</summary>
    private static IReadOnlyList<BoardDef> All(IReadOnlyList<BoardDef>? saved, IReadOnlyList<StarterBoard> starters)
    {
        if (saved is null) return [.. starters.Select(BoardDefs.Following)];

        var taken = saved.Where(b => b.Follows is null).Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
        var boards = new List<BoardDef>();
        foreach (var board in saved)
        {
            if (board.Follows is null)
            {
                boards.Add(board);
                continue;
            }

            if (StarterBoards.Named(starters, board.Follows) is not { } starter) continue;

            var built = BoardDefs.Following(starter);
            if (taken.Add(built.Id)) boards.Add(built);
        }

        return boards;
    }

    /// <summary>A following board as <c>boards.json</c> keeps it: its id, its starter's name, and no panels.</summary>
    private static BoardDef Entry(BoardDef built) => new(built.Id, built.Name, [], built.Follows);
}
```

- [ ] **Step 5: boards.json reads and writes `follows`**

`src/Board/BoardsFile.cs`:
- `BoardDto` gains `public string? Follows { get; set; }`; `Serialize` sets `Follows = board.Follows` (null is left out by `WhenWritingNull`).
- In `Parse`, before the board loop: `var followed = new HashSet<string>(StringComparer.Ordinal);`. The board is added as:

```csharp
            // A board follows a starter Ur Score knows, and only the first board following it does (D2).
            var follows = FollowsOf(Text(board, "follows"));
            if (follows is not null && !followed.Add(follows)) follows = null;

            boards.Add(new BoardDef(boardId, BoardDefs.CleanName(Text(board, "name")) ?? $"Board {boards.Count + 1}", panels, follows));
```

- and after `TypeOf`:

```csharp
    /// <summary>The starter a board follows, as its key; anything else follows nothing.</summary>
    private static string? FollowsOf(string? text) =>
        StarterBoards.Names.Select(StarterBoards.KeyOf).FirstOrDefault(key => string.Equals(key, text?.Trim(), StringComparison.OrdinalIgnoreCase));
```

- [ ] **Step 6: The empty state, the window and the composition root**

`src/Board/BoardEdits.cs`: delete `ForFirstSave` (and its doc comment).

`src/UI/BoardText.cs`: replace `EmptyFor` with:

```csharp
    /// <summary>
    /// Which empty state a board shows: no recipes over every board; a starter's own state on a tab that follows it
    /// (D4); a board with no panels, including a following tab being edited; else none.
    /// </summary>
    public static BoardEmpty EmptyFor(IReadOnlyList<StarterBoard> starters, BoardDef board, bool editing = false) =>
        starters.Any(s => s.Empty == BoardEmpty.NoRecipes) ? BoardEmpty.NoRecipes
        : !editing && StarterBoards.Named(starters, board.Follows) is { Empty: not BoardEmpty.None } starter ? starter.Empty
        : board.Panels.Count == 0 ? BoardEmpty.NoPanels
        : BoardEmpty.None;
```

`src/UI/BoardWindow.xaml.cs`, `RenderEmpty`, the first four lines become:

```csharp
        var starters = StarterBoards.All(_services.Installed, _services.Sources);
        // A draft is a board being shaped, not a tab following your sources: with no panels it says so and offers Add panel.
        _empty = BoardText.EmptyFor(starters, board, Editing);
        _emptyRecipe = (StarterBoards.Named(starters, board.Follows) ?? StarterBoards.EmptyState(starters)).RecipeSlug;
```

`src/Composition/AppServices.cs`: replace `Boards`, delete `BoardsFollowStarter`, and replace `SaveBoards`' doc comment, signature and its first two statements (the `ForFirstSave` line and `var clean = ...`) with:

```csharp
    /// <summary>
    /// The boards on screen: the saved ones, with each tab that still follows a starter rebuilt from your sources and
    /// shown while it has panels; with nothing saved, every starter follows (D1–D4). Never empty.
    /// </summary>
    public IReadOnlyList<BoardDef> Boards => Following.Shown(_savedBoards, StarterBoards.All(Installed, Sources));
```

```csharp
    /// <summary>
    /// Writes <c>boards.json</c> with only your own account ids (R17) and redraws. A following tab you didn't change stays
    /// a following entry, and one you changed is written as it is (D2, <see cref="Following.ToSave"/>). The old file is
    /// kept beside it when it doesn't parse, and on the first save after it couldn't be read at start (R3). Throws when
    /// the file can't be written; nothing changes then.
    /// </summary>
    public void SaveBoards(IReadOnlyList<BoardDef> boards)
    {
        var clean = BoardDefs.Sanitize(Following.ToSave(_savedBoards, StarterBoards.All(Installed, Sources), boards), LiveBoard.UserIdsOf(KnownAccounts));
```

(the rest of `SaveBoards`, from `var kept = _boardsFile.Save(...)`, is unchanged.)

- [ ] **Step 7: The build gate**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror` then `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: both pass. `grep -rn "ForFirstSave\|BoardsFollowStarter\|StarterBoardId\b" src tests` finds only `BoardDefs.StarterBoardId(` and its callers.

- [ ] **Step 8: A quick look (controller)**

On a clean data folder with the clan fixture (main and one alt) and the profile fixture: two tabs, Battle then Alts, no `boards.json`. Edit Alts, remove Records, Done: `boards.json` holds `b-starter-battle` with `"follows": "battle"` and no panels, and Alts with its two panels. Restart: both tabs come back and Battle still shows every panel.

- [ ] **Step 9: Commit**

```bash
git add src/Board/Following.cs src/Board/BoardDefs.cs src/Board/BoardsFile.cs src/Board/BoardEdits.cs src/UI/BoardText.cs src/UI/BoardWindow.xaml.cs src/Composition/AppServices.cs tests/FollowingTests.cs tests/BoardsFileTests.cs tests/BoardEditsTests.cs tests/BoardTextTests.cs
git commit -m "boards: Battle and Alts each follow your sources until you change that tab

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 8: Both tabs next to the approved board mock

**Files:**
- Create: `src/UI/Controls/UpperCase.cs`
- Modify (only these, and only for type, size, weight, spacing, padding, alignment, density and dividers): `src/App.xaml` (the `xmlns:ui` declaration, the `Upper` converter resource, and the panel styles `PanelCard`, `BigNumber`, `KeyLabel`, `ColumnHeader`, `PanelNoteText`, `RowCell`, `NumberCell`, `PanelTable`, `PanelTableHeader`, and a new `PanelTitleText` and `KeyValue`), `src/UI/BoardWindow.xaml`, `src/UI/Panels/*.xaml`
- Not allowed: a new brush or colour, a changed automation id or accessible name, a changed model, text or layout rule, any `.cs` beyond `UpperCase.cs`

**Interfaces:**
- Consumes: Tasks 3 to 7 on screen.
- Produces: nothing new for code; the fidelity record in this plan's execution record.

The controller takes every screenshot; the implementer changes XAML between rounds. The reference is the approved mock (the "Ur Score Modular Board" artifact) at (its HTML, saved from the artifact into the session scratchpad): its first window (the Battle board) for the Battle tab, and its last window (the Grind board: Profile stat table, Records, Account card) plus its table styles for the Alts tab.

- [ ] **Step 1: Baseline shots (controller)**

1. Build Release; run `walk-starter-board.ps1 -Main CCGP -Alt K0i2` and `walk-alts.ps1` (Task 9 writes them; before Task 9 lands, set the same data up by hand). Keep `artifacts\smoke\starter-board.png` and `alts-board.png`; the board window opens at its default 1280 × 900.
2. Open the mock in a browser (Playwright: navigate to the file URL, resize to 1320 × 1000, screenshot the first `.window` and the last `.window` elements) to `artifacts\smoke\mock-battle.png` and `mock-grind.png`.
3. Put each pair side by side and list every difference against the checklist in Step 3, with where it shows.

- [ ] **Step 2: Upper-case titles that still read as sentence case**

Create `src/UI/Controls/UpperCase.cs`:

```csharp
using System.Globalization;
using System.Windows.Data;

namespace Labs626.UrScore.UI;

/// <summary>Shows a panel title upper case, as the mock does (D21). The title's accessible name is bound to the original text.</summary>
public sealed class UpperCase : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as string)?.ToUpperInvariant() ?? "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
```

`src/App.xaml`: add `xmlns:ui="clr-namespace:Labs626.UrScore.UI"` to the `Application` element, `<ui:UpperCase x:Key="Upper" />` beside `BoolToVisible`, and after `SectionLabel`:

```xml
            <!-- A panel's title (the mock's .p-title): mono, small, cyan, drawn upper case; its accessible name stays the title. -->
            <Style x:Key="PanelTitleText" TargetType="TextBlock" BasedOn="{StaticResource SectionLabel}">
                <Setter Property="Margin" Value="0" />
                <Setter Property="VerticalAlignment" Value="Center" />
            </Style>
            <!-- A value on a key-value line (the mock's .kv b): white, medium, tabular figures. -->
            <Style x:Key="KeyValue" TargetType="TextBlock">
                <Setter Property="FontSize" Value="12" />
                <Setter Property="FontWeight" Value="Medium" />
                <Setter Property="Typography.NumeralAlignment" Value="Tabular" />
                <Setter Property="VerticalAlignment" Value="Center" />
            </Style>
```

`src/UI/Panels/PanelFrame.xaml`, the title:

```xml
            <TextBlock x:Name="PanelTitle" DockPanel.Dock="Left" Text="{Binding Title, Converter={StaticResource Upper}}"
                       AutomationProperties.Name="{Binding Title}" Style="{StaticResource PanelTitleText}" />
```

`tools/smoke` reads a TextBlock's UI Automation name, so every walk that checks `PanelTitle` keeps passing; confirm with `walk-starter-board.ps1` step 1.

- [ ] **Step 3: Match the mock, round by round**

The checklist, with the mock's values (its CSS) as targets. The app's body font stays Segoe UI Variable and its mono Cascadia Code.

| Element | Target |
|---|---|
| Board window text | 13 px body (`BoardWindow.xaml` `FontSize="13"`) |
| Board padding and gap | 14 px around the grid, 12 px between panels (today's; keep) |
| Panel card | 1 px `DividerBrush` edge, radius 8, padding 12 top, 14 sides and bottom, 10 px between its parts |
| Panel title | mono 11 semibold cyan, upper case, vertically centred with its chip and tools |
| Chip | mono 10.5 semibold, pill (radius 999), padding 7 × 4, `DividerBrush` edge, muted text |
| Subtitle | 12 px muted |
| Big number | display 30 semibold, tabular figures; "in the battle" 12 px muted on its baseline |
| Key-value line | label 12 px muted on the left, value 12 px medium white tabular on the right (`KeyValue`) |
| Table heading | mono 10.5 medium muted, 6 px padding, a 1 px `DividerBrush` line under the headings |
| Table row | 6 px vertical padding, a 1 px `DividerBrush` line between rows, numbers right-aligned in tabular figures (`NumberCell` gains `Typography.NumeralAlignment` Tabular) |
| Group heading (My accounts) | mono 10.5 semibold muted, 10 px above, no line under |
| Note | mono 11 `EdgeBrush`, 10 px above |
| Accounts table | the same heading and row rules; the totals row semibold; the frozen account column separated by nothing but its padding |
| Account card sections | section heading as a table heading; facts as key-value lines; at 7 of 12 columns the sections may sit side by side (`WrapPanel` items panel on the sections list, each section `MinWidth` 200, 24 px apart) |
| Rows of panels | no gaps: a shorter panel's card reaches its row's bottom (Task 6) |

For each difference found in Step 1, change only the files this task allows. After each round, the controller re-runs the two walks and Step 1.2's shots, and records what is still different.

- [ ] **Step 4: Stop rule**

Stop when every checklist row matches at 1280 × 900 on both tabs, or when a remaining difference can only be closed with a brush the theme doesn't paint (record it as D21's cost). The controller keeps the final four shots in `artifacts\smoke\` and lists the rounds and what changed in the execution record.

- [ ] **Step 5: The build gate**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror` then `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: both pass (`ThemeFenceTests`, `RowListFenceTests`).

- [ ] **Step 6: Commit**

```bash
git add src/UI/Controls/UpperCase.cs src/App.xaml src/UI/BoardWindow.xaml src/UI/Panels
git commit -m "ui: panel type, spacing and tables brought to the approved board mock

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 9: Smoke walks for two tabs and the Alts tab, and the live walk

**Files:**
- Create: `tools/smoke/walk-alts.ps1`
- Modify: `tools/smoke/uia.ps1` (`UR_SCORE_EXE`), `tools/smoke/uia-board.ps1` (tab and table helpers), `tools/smoke/walk-starter-board.ps1`, `tools/smoke/walk-stats-table.ps1`, `tools/smoke/walk-pop-outs.ps1`, `tools/smoke/check-boards-privacy.ps1`, `tools/smoke/README.md`

**Interfaces:**
- Consumes: automation ids `AccountsGrid`, `AccountsTablePanel1`, `SuggestedLine`, tabs `Battle` and `Alts`; `boards.json`'s `follows`.
- Produces: the walks the release runs.

Every script stays ASCII: the sort arrows are `[char]0x2193` and `[char]0x2191`, the dash `[char]0x2014`.

- [ ] **Step 1: The helpers**

`tools/smoke/uia.ps1`, the exe line:

```powershell
# UR_SCORE_EXE walks another copy, such as the one RoRoRo installed (D22); else the Release build.
$script:UrExe = if ($env:UR_SCORE_EXE) { $env:UR_SCORE_EXE } else { Join-Path $UrRepo 'bin\Release\net10.0-windows\626labs.ur-score.exe' }
```

`tools/smoke/uia-board.ps1`, after `Get-SelectedTabName`:

```powershell
# Selects a tab by its board name, the way a click does.
function Select-Tab($board, [string]$name) {
    $tab = Get-TabItems $board | Where-Object { $_.Current.Name -eq $name } | Select-Object -First 1
    if (-not $tab) { throw "no tab '$name'" }
    $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 1000
}

# The accounts table on the board on screen.
function Get-AccountsGrid {
    $panel = Find-ByAutomationId (Get-BoardWindow) 'AccountsTablePanel1'
    if (-not $panel) { return $null }
    Find-ByAutomationId $panel 'AccountsGrid'
}

# A table's column headings in order; a sorted one ends in an arrow.
function Get-GridHeaders($grid) { @(Find-All $grid $CT::HeaderItem | ForEach-Object { $_.Current.Name }) }

# A table's rows in order: named by account, the totals row last.
function Get-GridRows($grid) { @(Find-All $grid $CT::DataItem) }

# Clicks the heading whose name starts with a label, the way a person sorts.
function Invoke-GridHeader($grid, [string]$label) {
    $header = Find-All $grid $CT::HeaderItem | Where-Object { $_.Current.Name.StartsWith($label) } | Select-Object -First 1
    if (-not $header) { throw "no heading '$label'" }
    Invoke-Element $header
    Start-Sleep -Milliseconds 800
}

# Picks a row, the way a click or the arrow keys do.
function Select-GridRow($row) {
    if (-not $row) { throw 'row not found' }
    $row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 800
}
```

`tools/smoke/check-boards-privacy.ps1`: the board key list becomes `@('id', 'name', 'panels', 'follows')`.

- [ ] **Step 2: The Alts walk**

Create `tools/smoke/walk-alts.ps1`:

```powershell
# The Alts tab on a clean data folder: a first import of the profile recipe starts with its suggested stats ticked to
# show and none to send; the board opens on Alts alone; the accounts table has a column per shown stat, sorted by the
# first with its change; a heading click sorts and a second flips; picking a row fills the account card; the total
# sums what adds up; and none of it writes boards.json. Steps 4 to 7 need RoRoRo running and listing your accounts.
. (Join-Path $PSScriptRoot 'uia-board.ps1')
$ErrorActionPreference = 'Stop'
$profileFixture = Join-Path $UrFixtures 'petsim99-profile.recipe.json'
$rororo = [bool](Get-Process -Name 'ROROROblox.App' -ErrorAction SilentlyContinue)
$boardsFile = Join-Path $UrData 'boards.json'
$down = [string][char]0x2193
$up = [string][char]0x2191
$dash = [string][char]0x2014
$backup = $null

function Get-Toggle($root, [string]$name) {
    $box = Get-Check $root $name
    $box -and ($box.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On)
}

try {
    $backup = Move-UrDataAside
    Start-UrScore | Out-Null

    # 1. A first import ticks what the recipe suggests, to show only, and says so.
    Start-Import $profileFixture
    $screen = Wait-UrWindow '^Import recipe$' 30
    $suggested = @('Diamonds', 'Eggs hatched', 'Player rank', 'Rebirths', 'Different pets hatched', 'Goals completed', 'Playtime')
    $shown = @($suggested | Where-Object { Get-Toggle $screen "Show $_" })
    $sent = @($suggested | Where-Object { Get-Toggle $screen "Send $_" })
    Check '1 The suggested stats start ticked to show' ($shown.Count -eq $suggested.Count) ($shown -join ', ')
    Check '1b ...and none to send' ($sent.Count -eq 0) ($sent -join ', ')
    $line = Line $screen 'SuggestedLine'
    Check '1c The screen says why, and that nothing is sent' ($line -like 'The recipe suggests showing *Nothing is sent to RoRoRo unless you tick Send.') $line
    Invoke-Element (Find-ByAutomationId $screen 'ImportButton')
    Start-Sleep -Seconds 2
    Close-UrWindow (Get-SetupWindow)

    # 2. Only Alts: there is no battle recipe to build Battle from.
    Wait-Until { (Get-TabNames (Get-BoardWindow)) -contains 'Alts' } 20 | Out-Null
    $board = Get-BoardWindow
    $tabs = @(Get-TabNames $board)
    Check '2 The board opens on the Alts tab alone' ($tabs.Count -eq 1 -and $tabs[0] -eq 'Alts') ($tabs -join ', ')
    $ids = @(Get-PanelIds $board)
    Check '2b The accounts table, then records and the account card' (($ids -join ',') -eq 'AccountsTablePanel1,RecordsPanel1,AccountCardPanel1') ($ids -join ',')
    Check '2c The table is titled' ((Line (Find-ByAutomationId $board 'AccountsTablePanel1') 'PanelTitle') -eq 'Accounts table') (Line (Find-ByAutomationId $board 'AccountsTablePanel1') 'PanelTitle')

    # 3. A column per shown stat, sorted by the first, its change beside it.
    $headers = @(Get-GridHeaders (Get-AccountsGrid))
    $expected = @('Account', "Diamonds $down", 'Today', '7 days', 'Eggs hatched', 'Player rank', 'Rebirths', 'Different pets hatched', 'Goals completed', 'Playtime')
    Check '3 A column per shown stat, the first sorted with its change' (($headers -join '|') -eq ($expected -join '|')) ($headers -join ' | ')

    if (-not $rororo) {
        Skip '4 Test now fills a row per account and a total' 'needs RoRoRo' 'RoRoRo is not running'
    }
    else {
        # 4. A row per account, then the total; playtime reads as a duration.
        Invoke-Element (Find-ByAutomationId $board 'TestNowButton')
        Wait-Until { -not (Find-ByAutomationId (Get-BoardWindow) 'TestNowButton').Current.IsEnabled } 10 | Out-Null
        Wait-Until { (Find-ByAutomationId (Get-BoardWindow) 'TestNowButton').Current.IsEnabled } 240 | Out-Null
        $rows = @(Get-GridRows (Get-AccountsGrid))
        $names = @($rows | ForEach-Object { $_.Current.Name })
        Check '4 A row per account, then the total' ($rows.Count -ge 2 -and $names[-1] -eq 'Total') ($names -join ', ')
        $playtimes = @($rows | Select-Object -SkipLast 1 | ForEach-Object { @(Get-AllTexts $_)[-1] })
        $timeLike = @($playtimes | Where-Object { $_ -match '^(\d[\d,]*d \d+h|\d+h \d+m|\d+m)$' -or $_ -eq $dash })
        Check '4b Playtime reads as a duration' ($playtimes.Count -gt 0 -and $timeLike.Count -eq $playtimes.Count) ($playtimes -join ' | ')

        # 5. A heading click sorts by it and its change follows it; a second click flips it.
        Invoke-GridHeader (Get-AccountsGrid) 'Player rank'
        $headers = @(Get-GridHeaders (Get-AccountsGrid))
        Check '5 Player rank sorts highest first, its change beside it' ((($headers -join '|') -like "*|Player rank $down|Today|7 days|*") -and $headers[1] -eq 'Diamonds') ($headers -join ' | ')
        Invoke-GridHeader (Get-AccountsGrid) 'Player rank'
        $headers = @(Get-GridHeaders (Get-AccountsGrid))
        Check '5b A second click flips it' ($headers -contains "Player rank $up") ($headers -join ' | ')

        # 6. Picking an account fills the account card.
        $pick = @(Get-GridRows (Get-AccountsGrid)) | Where-Object { $_.Current.Name -ne 'Total' } | Select-Object -Last 1
        $pickName = $pick.Current.Name
        Select-GridRow $pick
        $card = Find-ByAutomationId (Get-BoardWindow) 'AccountCardPanel1'
        $subtitle = Line $card 'PanelSubtitle'
        Check '6 The card shows the picked account' ($subtitle.StartsWith($pickName)) "picked '$pickName'; card '$subtitle'; note '$(Line $card 'PanelNote')'"

        # 7. The total sums Diamonds (the first stat column); the rank column stays blank.
        $total = @(Get-GridRows (Get-AccountsGrid)) | Where-Object { $_.Current.Name -eq 'Total' } | Select-Object -First 1
        $totalTexts = @(Get-AllTexts $total)
        Check '7 The total row sums what adds up' ($totalTexts.Count -ge 2 -and $totalTexts[0] -eq 'Total' -and ($totalTexts[1] -match '^[\d,]+$' -or $totalTexts[1] -eq $dash)) ($totalTexts -join ' | ')
    }

    # 8. Sorting and picking are this session's only.
    Check '8 Sorting and picking write no boards.json' (-not (Test-Path $boardsFile)) "exists=$(Test-Path $boardsFile)"

    & (Join-Path $PSScriptRoot 'shot.ps1') -OutPath (Join-Path $UrShots 'alts-board.png') | Out-Null
}
finally {
    if ($null -ne $backup) { Restore-UrData $backup }
    Show-Results
    "RoRoRo running: $rororo"
}
exit $LASTEXITCODE
```

- [ ] **Step 3: The starter walk sees two tabs, following one at a time**

`tools/smoke/walk-starter-board.ps1`:
- Dot-source `uia-board.ps1` instead of `uia-import.ps1`; add `$profileFixture = Join-Path $UrFixtures 'petsim99-profile.recipe.json'` and `$boardsFile = Join-Path $UrData 'boards.json'`.
- After the top fixture's import, import the profile recipe with its suggestions:

```powershell
    Start-Import $profileFixture
    $screen = Wait-UrWindow '^Import recipe$' 30
    Invoke-Element (Find-ByAutomationId $screen 'ImportButton')
    Start-Sleep -Seconds 2
```

- The expected panels, and the wait, become the Battle tab's (Records is its last panel):

```powershell
    $expected = [ordered]@{
        'StandingPanel1'       = 'Clan standing'
        'StandingPanel2'       = 'Clan standing'
        'RacePanel1'           = 'Battle race'
        'PromotionCheckPanel1' = 'Promotion check'
        'MyAccountsPanel1'     = 'My accounts'
        'PastPeriodsPanel1'    = 'Past battles'
        'RecordsPanel1'        = 'Records'
    }
    if ($topFixture) { $expected['TopPanel1'] = 'Top of the battle' }

    Wait-Until {
        $records = Find-ByAutomationId (Get-BoardWindow) 'RecordsPanel1'
        $records -and (Line $records 'PanelTitle') -eq 'Records'
    } 20 | Out-Null
```

- Step 1b becomes:

```powershell
    $tabs = @(Get-TabNames $board)
    Check '1b Two tabs, Battle first' ($tabs.Count -eq 2 -and $tabs[0] -eq 'Battle' -and $tabs[1] -eq 'Alts') ($tabs -join ', ')
    Check '1d Battle has no account card or table' (-not (Find-ByAutomationId $board 'AccountCardPanel1') -and -not (Find-ByAutomationId $board 'AccountsTablePanel1')) (@(Get-PanelIds $board) -join ',')
```

- Before the final Stop, add the per-tab following steps:

```powershell
    # 5. Alts is a tab of its own.
    Select-Tab (Get-BoardWindow) 'Alts'
    Check '5 Alts shows the accounts table' ([bool](Find-ByAutomationId (Get-BoardWindow) 'AccountsTablePanel1')) (@(Get-PanelIds (Get-BoardWindow)) -join ',')
    & (Join-Path $PSScriptRoot 'shot.ps1') -OutPath (Join-Path $UrShots 'alts-tab.png') | Out-Null

    # 6. Changing Alts writes Alts; Battle keeps following.
    Enter-EditMode (Get-BoardWindow)
    Invoke-PanelTool (Get-BoardWindow) 'RecordsPanel1' 'RemovePanelButton'
    Complete-EditMode (Get-BoardWindow)
    $saved = @(Read-Boards)
    $battleEntry = $saved | Where-Object { $_.id -eq 'b-starter-battle' } | Select-Object -First 1
    $altsEntry = $saved | Where-Object { $_.id -eq 'b-starter-alts' } | Select-Object -First 1
    Check '6 boards.json keeps Battle following, with no panels' ($battleEntry -and $battleEntry.follows -eq 'battle' -and @($battleEntry.panels).Count -eq 0) ($saved | ConvertTo-Json -Depth 2 -Compress)
    Check '6b ...and Alts as you left it' ($altsEntry -and -not $altsEntry.follows -and @($altsEntry.panels).Count -eq 2) "alts panels=$(@($altsEntry.panels).Count)"
    Select-Tab (Get-BoardWindow) 'Battle'
    Check '6c Battle still shows its panels' ([bool](Find-ByAutomationId (Get-BoardWindow) 'RacePanel1')) (@(Get-PanelIds (Get-BoardWindow)) -join ',')
```

(The `ConvertTo-Json` of `boards.json` prints ids, names, types and settings of your own boards only: R17.)

- The header comment names the new steps: "…then every Battle panel with its title, Alts as its own tab, Start, Test now and Stop, and a change to Alts that leaves Battle following."
- The shot at step 3 stays `starter-board.png` (the Battle tab).

- [ ] **Step 4: The stats walk starts from the suggestions**

`tools/smoke/walk-stats-table.ps1`, right after the import screen opens (before step 2's search):

```powershell
    $suggested = @('Diamonds', 'Eggs hatched', 'Player rank', 'Rebirths', 'Different pets hatched', 'Goals completed', 'Playtime')
    $ticked = @($suggested | Where-Object { $c = Get-Check $screen "Show $_"; $c -and $c.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On })
    Check '1b A first import starts with the suggested stats shown' ($ticked.Count -eq $suggested.Count) ($ticked -join ', ')
    foreach ($label in $suggested) { Set-Tick (Get-Check $screen "Show $label") $false }
```

Then `Showing 1 of 3` becomes `Showing 1 of 16` in step 2 (pattern and `Wait-Line`), and `Showing 2 of 3` becomes `Showing 2 of 16` in step 3.

- [ ] **Step 5: The pop-out walk pops out Records**

`tools/smoke/walk-pop-outs.ps1`: `AccountCardPanel1` becomes `RecordsPanel1` (step 2, both lines) and `'Bring back Account card'` becomes `'Bring back Records'` (step 7). The Battle tab has no Account card (D8).

- [ ] **Step 6: The README**

`tools/smoke/README.md`: in the scripts table, `walk-starter-board.ps1` reads "Two tabs, every Battle panel by automation id and title, Alts as its own tab, Start, Test now, Stop, and a change to Alts that leaves Battle following"; add a row `walk-alts.ps1` "The Alts tab: a first import's suggested ticks, the accounts table's columns, sort and flip, a picked account in the card, the total, nothing written"; and under "Before you run": "To walk another copy (the one RoRoRo installed), set `UR_SCORE_EXE` to its `626labs.ur-score.exe` first."

- [ ] **Step 7: Commit**

```bash
git add tools/smoke
git commit -m "smoke: the Alts walk, two tabs that follow one at a time, suggested ticks, and walking an installed copy

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Step 8: The live walk (controller)**

Quit Ur Score. From the repo root:

```bash
dotnet build Ur-Score.csproj -c Release -warnaserror
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build
```

Start RoRoRo 1.28 and confirm it lists your accounts (`Get-Process ROROROblox.App | Select-Object Path` shows the build you mean). Run each walk, one at a time, and record every result:

```powershell
powershell -ExecutionPolicy Bypass -File tools/smoke/window-smoke.ps1 -Main CCGP
powershell -ExecutionPolicy Bypass -File tools/smoke/walk-setup-clans.ps1 -Main CCGP -Alt K0i2
powershell -ExecutionPolicy Bypass -File tools/smoke/walk-stats-table.ps1
powershell -ExecutionPolicy Bypass -File tools/smoke/walk-starter-board.ps1 -Main CCGP -Alt K0i2
powershell -ExecutionPolicy Bypass -File tools/smoke/walk-alts.ps1
powershell -ExecutionPolicy Bypass -File tools/smoke/walk-board-editing.ps1 -Main CCGP -Alt K0i2
powershell -ExecutionPolicy Bypass -File tools/smoke/walk-pop-outs.ps1 -Main K0i2
powershell -ExecutionPolicy Bypass -File tools/smoke/walk-score-book.ps1 -Main K0i2
powershell -ExecutionPolicy Bypass -File tools/smoke/check-boards-privacy.ps1
powershell -ExecutionPolicy Bypass -File tools/smoke/check-book-privacy.ps1
```

Expected: every step passes (a SKIP that needs a live battle is not a failure), and no `626labs.ur-score.smoke-backup-*` folder is left. By hand, on the owner's own data folder (no `boards.json`, profile recipe already installed): update the profile recipe from `tests/Fixtures/petsim99-profile.recipe.json` (the update screen lists the new stats unticked and keeps your ticks), tick Rebirths, Different pets hatched, Goals completed and Playtime in Setup › Stats, and check both tabs after Test now. Put `boards.json` back to absent afterwards. A failure goes back to its task before the release.

---

## After the tasks: release v0.3.1 (the controller, each gated step waits for the owner's OK)

1. **Version and notes.** Set `0.3.1` in `manifest.json` (`"version"`) and `Ur-Score.csproj` (`<Version>`). In `CHANGELOG.md`, insert above `## Unreleased` a section `## 0.3.1 — ` and the date `powershell -Command "Get-Date -Format yyyy-MM-dd"` prints, with **Added**: two tabs that are ready on first start (Battle and Alts), each following your sources until you change it; the Accounts table (your accounts side by side, sortable, with totals and today's and 7-day change); the account card in sections showing the account you pick; the profile recipe's new stats with playtime as a duration and first join as a date, and the stats it suggests ticked on a first import (never sent). **Changed**: panels keep their natural height, top-aligned in their row; panel type and tables follow the board design; + Board offers Alts instead of Grind. In `docs/backlog.md`, close `S1-14.11` as **GONE**, by design ("the approved mock keeps natural heights: the stretch built in 0.3.1 default views Task 6 was reversed by the Task 8 ruling"), not FIXED, and move the counts by one. Append `## Execution record` with the date to this plan: commits per task, rulings made during execution, the fidelity rounds (Task 8), and the walk results (Task 9 Step 8). Commit `release: 0.3.1`.
2. **Pull request (ask the owner first).** `git push -u origin feat/default-views`, then `gh pr create --base master --head feat/default-views --title "Ur Score 0.3.1: Battle and Alts tabs, ready for the clan battle"` with a body summarising the tabs, the Accounts table, the recipe's new stats and the walk results, ending with the Claude Code attribution line. Wait for the `test` workflow to pass.
3. **Merge (ask the owner first).** `gh pr merge --merge`, then `git checkout master` and `git pull --ff-only`.
4. **Tag and release (ask the owner first).** `git tag -a v0.3.1 -m "Ur Score 0.3.1: Battle and Alts tabs"` and `git push origin v0.3.1`. The `release` workflow checks that the tag, `manifest.json` and the csproj agree, runs the tests and attaches `manifest.json`, `manifest.sha256` and `plugin.zip`. Confirm all three with `gh release view v0.3.1`.
5. **Install from the release and walk the installed copy.** Back up `%LOCALAPPDATA%\ROROROblox\plugins\626labs.ur-score` to the session scratchpad and remove it; leave `%LOCALAPPDATA%\626labs.ur-score` in place. In RoRoRo's Plugins page, Install from URL with `https://github.com/estevanhernandez-stack-ed/Ur-Score/releases/latest/download/` and accept the consent; RoRoRo names version 0.3.1. Quit the installed Ur Score, set `$env:UR_SCORE_EXE` to `%LOCALAPPDATA%\ROROROblox\plugins\626labs.ur-score\626labs.ur-score.exe`, and run `walk-alts.ps1` and `walk-starter-board.ps1 -Main CCGP -Alt K0i2` (they move your data aside and put it back). Then start the installed Ur Score from RoRoRo on your own data: Battle and Alts are there and read.
6. **Clan post (the owner posts).** Draft a short "what's new in 0.3.1" note in the product's voice (builder to builder, second person, no jargon, no emoji): Ur Score opens with a Battle tab to watch on Saturday and an Alts tab with every account side by side; click a column to sort, click an account to see it in the card; update the profile recipe and tick the new stats you want; nothing about other players is saved. Hand it to the owner.

---

## Self-review record

- **Design coverage:**
  - Two tabs built from your clans and recipes → Task 6 (starters), Task 7 (shown as tabs). Each keeps following until you change that tab → Task 7 (D2). A tab appears only when it has something to show → Task 7 (`Following.Shown`, D4). + Board offers both → Task 6 (`AltsBoardButton`). Single-panel views are not tabs → nothing built for them.
  - Battle rows 1–4, filling 12 columns, closing up when a source is missing → Task 6 (`Row`, D6), no gaps in a row → Task 6 (`CellHeight`, D7).
  - Alts: the Accounts table (one row per account; columns are the stats you show; heading sorts; totals row; the sorted column's today and 7-day change; a private account says so) → Task 4 (model), Task 5 (panel); Records · Account card, a clicked row shown in the card, the card in sections → Task 3 (card), Task 5 (pick), Task 6 (layout). Grind goes → Task 6 (D5).
  - Data: the named stats, distinct counts, playtime and first join as durations and dates, default ticks on a fresh install → Task 1 (format, fixture), Task 2 (ticks), Task 3 (formats on panels). Robux spent and login count out of scope → not added.
  - Rules that bind: your accounts only (Task 4 reads `live.Accounts`; D14 clipboard is your own accounts); themed popups and RowList/BoardButtons rules (D14 records the table's exception; no button state changes); no hostname literals, copy style and no game names (recipe words and labels only); the mock comparison → Task 8; merge and release wait for the owner → the release section.
- **Placeholders:** none; every code step carries its code, every command is exact. Task 8's rounds are a checklist the controller runs, with fixed targets and a stop rule.
- **Types across tasks:** `StatFormat`, `RecipeStat.Format/Section` (Task 1) are used by `PanelText.Value` and `Sections` (Task 3) and `AccountsTable` (Task 4). `WindowGain(series, since, format, none)` (Task 3) is what Task 4 calls. `AccountColumn`, `AccountSort.Clicked`, `AccountRow`, `PanelSession` (Task 4) are what `AccountsTablePanel` and `BoardWindow.Accounts.cs` use (Task 5). `AccountCard(..., pickedUserId)` (Task 3) is what `PanelViews.Render` passes (Task 5). `StarterBoards.All/Named/EmptyState/KeyOf` (Task 6) are what `Following`, `BoardDefs.Following`, `BoardsFile.FollowsOf`, `BoardText.EmptyFor` and `AppServices` use (Task 7). Automation ids in the table above match the walks (Task 9).
- **Against the tree at `9218049`:** `PanelSettings`, `PanelHead`, `LiveBoard.FindSource/FindRecipe/SourceName/AccountName/MyUserIds/IsOverdue`, `RecipeSnapshot.Rows/Unavailable`, `ScoreBookReader.Series`, `BoardEdits.Changed/Replace/RemovePanel/Rename/PopOut/Delete/Find`, `BoardFixtures.Installed/SourceOf/Snapshot/Row/Read/Reader/Live`, `PanelFrame`, `PanelAt`, `_popOuts`, `PanelPopOutWindow.View/PanelId`, `StatsTable.Load`, `ImportWindow.Show` and the smoke helpers `Find-All`, `Get-Check`, `Set-Tick`, `Line`, `Get-AllTexts`, `Read-Boards`, `Enter-EditMode`, `Invoke-PanelTool`, `Complete-EditMode`, `Start-Import` are used as they are.

---

## Execution record (2026-09-15)

Built by nine subagent-driven tasks off `9218049`, each with its own dispatch, a per-task review, and a fix round where the review found real issues (Task 5 took two fix rounds to close a focus/scroll bug properly; Task 7 and Task 8 each took one). A visual pass (Task 8) held the built Battle and Alts tabs up against the owner's approved mock through two rounds of screenshot comparison plus a review round of its own. Once all nine tasks were done, a whole-branch review read the full diff from `9218049`, found two Important cross-task issues and eleven Minors, and one fix wave plus a scoped re-review closed all of it with no Critical or Important findings left. The branch ends at 855 tests, and both the app and test-project builds are `-warnaserror` clean.

### Tasks

- **Task 1** (recipe format additions, the profile recipe's named stats): `b633ac8`.
- **Task 2** (first import starts with suggested stats ticked): `9466d88`.
- **Task 3** (durations/dates on panels, the sectioned account card): `c634488`.
- **Task 4** (Accounts table model, form, gallery card): `7ec5170`.
- **Task 5** (Accounts table on the board — sort, pick, focus): `edee6f2`, fix round 1 `2b49123`, fix round 2 `61dbd06`.
- **Task 6** (Battle and Alts in rows that fill the grid): `c3f53c3`.
- **Task 7** (each tab follows your sources until you change it): `6224164`, fix round `8525906`.
- **Task 8** (both tabs next to the approved mock): round 1 `1fdc025`, round 2 `7cd5086` + `c0a49bd`, review-fix round `fa4320b`.
- **Task 9** (smoke walks, the live walk): Steps 1-7 `3a327c7`, follow-up `c5c81a1`, follow-up round 2 `910e326`, controller's walk-regression fix `b997bb9`.
- **Docs tie-up before the whole-branch review:** `f69abcc`.
- **Final fix wave** (whole-branch review's Importants and Minors): `cc3aa86`, `13ea6e7`, `ecb6029`, `88d7fdb`, `41703e2`, `04ffc38`, `e088a0c`.

### Rulings made during execution

- Pre-flight FIX findings were folded straight into the tasks that touched their files, not fixed separately: T1 renamed the not-offered test stat `"rebirths"` → `"prestige"` (`RecipeStoreTests.cs`); T2 updated `walk-stats-table`'s counts and default ticks in its own commit; T4 extracted shared "first enabled source, else its first" and midnight helpers instead of copying them a third time; T6 moved the two-tab `walk-starter-board`/`walk-pop-outs` edits into its own commit; T9 Step 8 checks the "Updated … New stat" outcome text instead of an update screen that turns out not to exist for a same-hosts update.
- Task 3's ruling bound durations/dates everywhere they show — the Account card's value facts and the Records panel, not only the table and card sections — overruling D10's original carve-out.
- Task 5's ruling (sort/pick redraws never run inside the DataGrid's own `Sorting`/`SelectionChanged`; keyboard focus returns to the picked row, D14) took three passes to land safely: the first fix missed a Total-row round-trip that could still arm a restore; the second added a generation-stamped token (`FocusRestoreGate`) so a data refresh can never restore focus or scroll the board, only the redraw that actually answered the table's own input can.
- Task 9's walk scripts (Steps 1-7) were run before Task 8, because Task 8's baseline shots needed `walk-alts`; Task 9 Step 8 (the live walk) ran after Task 8.
- Natural heights (reversing Task 6's row-stretch, superseding D7): panels keep their own height, top-aligned in their row, matching the mock instead of stretching to their row's tallest neighbour; carried by `PanelGrid.cs` and a new `BoardLayout.ArrangedHeight`.
- The Battle rebalance (fidelity round 2): rows reordered to standings + race / My accounts + Promotion + Top / Past battles + Records, matching the mock's arrangement and moving panels the owner had already approved — the controller flagged this to the owner rather than treating it as self-evidently fine.
- Chips are coloured by role (`PanelHead.ChipRole`), not by their text, so a copy change can never silently mute a chip's colour; after a contrast finding on the watching chip, chip text became `WhiteBrush` for every role, with the role's colour kept on the border only.
- The clipboard default flipped: the shared `PanelTable` style now defaults to `ClipboardCopyMode=None`, and only `AccountsGrid` opts in locally to `IncludeHeader` (your own accounts only), guarded by a fence test.
- The one-source rule: "first enabled source, else its first" (`PanelForms.FirstSourceOfRecipe`) is now shared by the Accounts table and Profile stat instead of each panel picking its own rule; the table's note now distinguishes a switched-off source from "waiting for the first read."
- Card section columns are chosen from width by a pure rule (`CardLayout.SectionColumns`, floor of 200px per column, equal widths), replacing the fixed two-column grid that trimmed labels on a Small card or a pop-out.
- Starter name lookup: `StarterBoards.Build` now matches a starter's name or its key, ignoring case, and throws on anything else — closing the trap where passing `"alts"` (a `boards.json` key) silently built Battle instead.
- The walk-script regression on `910e326` was root-caused, not just re-run: `Invoke-WhenReady`'s `Wait-Until` assigned `$el` in its own scope (the outer `$el` stayed null, so every import walk threw "element not found"), and `Select-GridRowAsUser` called `SetFocus` on a DataGrid row, which isn't focusable. Both were fixed in the controller's `b997bb9`.

### Live walk

Final results, on `e088a0c`, all green: `walk-alts` 16/16, `walk-stats-table` 11/11, `walk-starter-board` 21/21, `walk-board-editing` 24/24, `walk-pop-outs -Main K0i2` 14/14, `walk-score-book -Main K0i2` 6/6, `window-smoke` 12/12, `walk-setup-clans` 11/11 — 115 checks, no `smoke-backup` folder left behind. The battle-only steps (`walk-pop-outs`, `walk-score-book`) ran against a real account, K0i2, whose clan battle is public and readable, rather than a fixture.

### Release note to carry

After a downgrade to 0.3.0 and one save there, following tabs come back as empty boards. Delete them and add Battle or Alts again with **+ Board**.

### Nice-to-haves (every review Minor)

Every Minor finding from every task review, rereview, the final review and re-review, the pre-flight scan, and the two visual-fidelity rounds, checked against the code at `e088a0c` on 2026-09-15. **39 open, 35 fixed, 0 gone** (74 total).

**Pre-flight**
- DV-PF.1 **OPEN** — a code comment still claims a recipe can never name "a stat to tick," but the new `show` field does exactly that — code tidiness — `src/Recipes/Recipe.cs:8-11` — preflight.md §4.
- DV-PF.2 **OPEN** — the plan's own note on downgrading to 0.3.0 doesn't mention that it also drops any Accounts table panel on a plain (non-following) board — doc gap — plan D2 cost line — preflight.md §4.

**Task 1 — recipe format, profile recipe's named stats**
- DV-T1.1 **OPEN** — two nearly identical count-reading helpers repeat the same two-line "value wasn't found" check instead of one owning it — code tidiness — `src/Recipes/RecipeEngine.cs:656-657,672-673` — task-1-review.md Minor.

**Task 2 — first import starts with suggested ticks**
- DV-T2.1 **OPEN** — the import screen's "so it starts ticked" wording (exactly one suggested stat) has no test; only the many-stats and zero-stats cases are tested — test gap — `src/UI/ImportText.cs:29-33`, `tests/ImportTextTests.cs` — task-2-review.md Minor 1.
- DV-T2.2 **OPEN** — a helper method sits after a different one than the brief asked for; no effect on behaviour — code tidiness — `src/UI/Controls/StatsTableModel.cs:160` — task-2-review.md Minor 2.

**Task 3 — durations/dates on panels, sectioned account card**
- DV-T3.1 **OPEN** — the "Best {period}" fact (e.g. "Best battle") is wired to format as a duration or date but no test exercises that combination — test gap — `src/Board/PanelModels.cs:396,548` — task-3-review.md Minor 1.
- DV-T3.2 **OPEN** — a date reading of exactly midnight 1 Jan 1970 shows a dash instead of the date, which the code's own doc comment says should be valid — code tidiness — `src/Board/PanelText.cs:60-62` — task-3-review.md Minor 2.

**Task 4 — Accounts table model, form, gallery card**
- DV-T4.1 **OPEN** — the gallery's doc comment still says "the ten panels"; the Accounts table makes eleven — code tidiness — `src/Board/PanelGallery.cs:9` — task-4-review.md Minor.

**Task 5 — Accounts table on the board (sort, pick, focus)**
- DV-T5.1 **OPEN** — you can't sort the Accounts table from the keyboard, only by clicking a column heading; backlogged on purpose (a default sort always exists) — `src/App.xaml:482-487` — task-5-review.md Minor 1.
- DV-T5.2 **OPEN** — if you alt-tab away with focus in the table, a data refresh can leave keyboard navigation stuck until you click the table again — `src/UI/Panels/AccountsTablePanel.xaml.cs:51` — task-5-review.md Minor 2.
- DV-T5.3 **FIXED** — a focus restore after sorting could land in the wrong column; it now follows the column by name, not position — `src/Board/AccountsTableFocus.cs:31-48` — task-5-rereview.md Ruling 3.
- DV-T5.4 **FIXED** — "which row stays picked after a refresh" and "where focus falls back to" are now pure, tested rules instead of undocumented control logic — `src/Board/AccountsTableFocus.cs:17-24` — task-5-rereview.md Ruling 4.
- DV-T5.5 **FIXED** — a table cell style no longer duplicates WPF's whole default cell template just to change its padding — code tidiness — `src/App.xaml:450-461` — task-5-rereview.md Ruling 5.
- DV-T5.6 **FIXED** — a redundant header style setter (duplicated an existing one) was removed and commented — code tidiness — `src/App.xaml:531-533` — task-5-rereview.md Ruling 6.
- DV-T5.7 **FIXED** — the keyboard focus outline on a table cell was thinner (1px) than the rest of the app's (1.5px); now matches — `src/App.xaml:497` — task-5-rereview.md Ruling 7.
- DV-T5.8 **OPEN** — the line between table rows is full-strength, where the mock uses a dimmer ~55%; never given a theme slot — `src/App.xaml:202-203` — task-5-review.md Minor 8.
- DV-T5.9 **OPEN** — clicking a row or column heading redraws the entire board (every panel, every pop-out), not just the table and card; fine at clan sizes today — `src/UI/BoardWindow.Accounts.cs:66` — task-5-review.md Minor 9; also flagged in final-review.md's after-Saturday recommendations.
- DV-T5.10 **FIXED** — extra table styles landed ahead of Task 8's visual pass; Task 8 confirmed it built on them instead of restyling twice — `src/App.xaml:489-536` — task-5-review.md Minor 10.

**Task 6 — Battle and Alts in rows that fill the grid**
- DV-T6.1 **FIXED** — building a starter by its saved key (e.g. `"alts"`) used to silently build Battle instead; now matches name or key, ignoring case, and rejects anything else — `src/Board/StarterBoards.cs:56-57` — task-6-review.md Minor 1, fixed by task-7-review.md Ruling 1.
- DV-T6.2 **OPEN** — the empty-tab fallback would throw if the starter list were ever empty (not reachable today, since there are always two starters) — code tidiness — `src/Board/StarterBoards.cs:44-47` — task-6-review.md Minor 2.
- DV-T6.3 **OPEN** — the same small helper for matching a source to a recipe is written out twice, once for Battle and once for Alts — code tidiness — `src/Board/StarterBoards.cs:87,133` — task-6-review.md Minor 3.
- DV-T6.4 **OPEN** — a row-height calculation is duplicated instead of one calling the other — code tidiness — `src/Board/BoardLayout.cs:68-75,82` — task-6-review.md Minor 4.
- DV-T6.5 **FIXED** — a profile-only user (no clan recipe) used to see Battle's "No stats turned on yet" until Task 7 shipped; now they see Alts alone with no empty Battle tab — task-6-review.md Minor 5, fixed by task-7-review.md Ruling 2.
- DV-T6.6 **OPEN** — a test checks that a profile-only user's Battle tab has no panels, but not which "nothing to show yet" message it would display — test gap — `tests/StarterBoardsTests.cs:142-143` — task-6-review.md Minor 6.
- DV-T6.7 **FIXED** — two Alts-tab paths (no available source; two candidate recipes) had no test; Task 7 added both — task-6-review.md Minor 7, fixed by task-7-review.md Ruling 1's tests.
- DV-T6.8 **FIXED** — a test's saved reference layout was named after the old four-row starter design and was misleading; renamed — `tests/BoardLayoutTests.cs` — task-6-review.md Minor 8, fixed in Task 8 round 2.
- DV-T6.9 **OPEN** — a code comment describing starter panel widths as "4- or 5-wide" is stale; starters now use 3, 4, 5, 6, 7 and 8 — code tidiness — `src/UI/Panels/PanelFrame.xaml.cs:122` — task-6-review.md Minor 9.
- DV-T6.10 **FIXED** — a popped-out panel next to a taller one used to sit half-empty; addressed by Task 8's natural-heights change — task-6-review.md Minor 10.

**Task 7 — each tab follows your sources until you change it**
- DV-T7.1 **OPEN** — with nothing ticked anywhere, the one empty tab shown is always named "Battle," even for a profile-only user who has no clan recipe installed — `src/Board/StarterBoards.cs:44-47,62-66` — task-7-review.md Minor 1.
- DV-T7.2 **FIXED** — editing a following tab (e.g. Alts) while its underlying source disappears (e.g. you untick every stat in Setup mid-edit) used to drop your edit silently on Done; now it's kept — `src/Board/BoardEdits.cs:42-49` — task-7-rereview.md Ruling 3.
- DV-T7.3 **FIXED** — the message shown when `boards.json` can't be read still said "the starter board is showing" from when there was only one tab; now says "starter tabs" — `src/Composition/AppServices.cs:772-773` — task-7-rereview.md Ruling 2.
- DV-T7.4 **FIXED** — the walk script never actually checked that no `boards.json` file gets written while both tabs are still following; a check was added — `tools/smoke/walk-starter-board.ps1` — task-7-rereview.md Ruling 1.
- DV-T7.5 **OPEN** — downgrading to 0.3.0 and saving there turns following tabs into permanent empty boards, including a hidden Alts you never saw; needs a release-notes line — see "Release note to carry" above — task-7-review.md Minor 5.
- DV-T7.6 **FIXED** — the privacy check script accepted any value in a board's `follows` field; now only accepts the two real values — `tools/smoke/check-boards-privacy.ps1` — task-7-rereview.md Ruling 4.
- DV-T7.7 **OPEN** — the same "match a starter's name or key" logic is written three separate times with slightly different rules — code tidiness — `src/Board/StarterBoards.cs`, `src/Board/BoardsFile.cs:166` — task-7-review.md Minor 7.
- DV-T7.8 **OPEN** — both starter tabs get rebuilt from scratch on every read of the board list, several times per click; fine at clan sizes today — `src/Composition/AppServices.cs:214` — task-7-review.md Minor 8; also flagged in final-review.md's after-Saturday recommendations.
- DV-T7.9 **OPEN** — no test specifically proves that cleaning up a board's data keeps its "follows" flag intact — test gap — `tests/BoardsFileTests.cs` — task-7-review.md Minor 9.
- DV-T7.10 **OPEN** — a test's wording and ids still describe the old single-starter-tab design — code tidiness — `tests/BoardEditsTests.cs:233-235` — task-7-review.md Minor 10.
- DV-T7.11 **OPEN** — a `follows` value from a future version Ur Score doesn't recognize loads as a visible empty board, and the next save silently drops that "follows" for good — accepted, no fix planned — `src/Board/BoardsFile.cs:166` — task-7-review.md Minor 11.

**Task 8 — both tabs next to the approved mock**
- DV-T8.1 **FIXED** — the new "panel keeps its own height" layout rule had no test guarding it from regressing back to full-stretch; now a tested rule — `src/Board/BoardLayout.cs` (`ArrangedHeight`) — task-8-review.md Minor 1.
- DV-T8.2 **FIXED** — two visual on/off switches (hide the empty chart; hide empty sections) were driven by a fragile technical trick instead of a named, tested flag — `src/Board/PanelModels.cs` (`HasLine`/`HasSections`) — task-8-review.md Minor 2.
- DV-T8.3 **FIXED** — a chip's colour and its wording were two separate settings that could disagree; the colour now always comes from the role — `src/Board/PanelModels.cs` (`PanelHead.Chip`) — task-8-review.md Minor 3.
- DV-T8.4 **FIXED** — a leftover style setter had no effect and could mislead a future edit; removed — `src/App.xaml` — task-8-review.md Minor 4.
- DV-T8.5 **FIXED** — the same "picked row" highlight colour was hardcoded in two places; now one shared style — `src/App.xaml` (`PanelRowTint`), `src/UI/Panels/TopPanel.xaml` — task-8-review.md Minor 5.
- DV-T8.6 **FIXED** — clicking a row in the Live leaderboard picked up the Accounts table's "picked row" highlight, which means nothing there — `src/UI/Panels/LiveLeaderboardPanel.xaml` — task-8-review.md Minor 6.
- DV-T8.7 **FIXED** — the **+ Board** button stayed bright cyan even when disabled, reading as clickable when it wasn't — `src/UI/BoardWindow.xaml` — task-8-review.md Minor 7.
- DV-T8.8 **FIXED** — subsumed by the Important-1 fix (card section columns from width): uneven row heights across account-card section columns are gone — task-8-review.md Minor 8.
- DV-T8.9 **OPEN** — a chip's padding is 7×3 rather than the checklist's 7×4; a deliberate, explained trade-off for WPF's taller line height — accepted as a D21 cost — `src/UI/Panels/PanelFrame.xaml:40` — task-8-review.md Minor 9.
- DV-T8.10 **FIXED** — the plan (D6, D7) and the design doc described the pre-rebalance, stretched-panel Battle layout; both now carry "changed during execution" banners pointing at this record — `docs/2026-09-15-default-views-design.md:23`, this plan's D6/D7 — task-8-review.md Minor 10.
- DV-T8.11 **OPEN** — some panels put their subtitle on the same line as the title in the mock ("BATTLE RACE points since…"); the build always puts it on its own line — needs a per-panel model change — fidelity-round-1.md.
- DV-T8.12 **OPEN** — a clan that isn't in the battle shows a plain "—" rather than a designed empty state — backlog V3-S.1 — fidelity-round-1.md.
- DV-T8.13 **OPEN** — the race chart has no x-axis labels or line-end dots before any data arrives — fidelity-round-1.md.
- DV-T8.14 **OPEN** — the footer repeats one credit line per recipe instead of combining them — backlog V3-S.4 — fidelity-round-1.md.
- DV-T8.15 **OPEN** — a chip reads "yours" where the mock says "alts"; wording only, no functional difference — fidelity-round-1.md.
- DV-T8.16 **OPEN** — the account card has an empty band between its big number and its sections, reserved for a sparkline that has no data yet — fidelity-round-1.md.
- DV-T8.17 **OPEN** — panel titles have no letter-spacing; WPF's `TextBlock` doesn't support it — fidelity-round-2.md.
- DV-T8.18 **OPEN** — chip borders are drawn at full colour strength rather than the mock's softer tint — fidelity-round-2.md.
- DV-T8.19 **OPEN** — the app's theme has no green or amber brush, so the mock's colours for those states can't be matched — fidelity-round-2.md.
- DV-T8.20 **OPEN** — line spacing throughout is about 1px shorter than the mock's — fidelity-round-2.md.
- DV-T8.21 **OPEN** — pop-out windows keep 12px text rather than matching the board's sizing — fidelity-round-2.md.
- DV-T8.22 **OPEN** — the top bar's padding doesn't exactly match the mock's — fidelity-round-2.md.

**Task 9 — smoke walks, the live walk**
- DV-T9.1 **FIXED** — `walk-alts.ps1` had no check for the exact focus/scroll bug Task 5 fixed; it now has one (picking a row keeps focus; a refresh doesn't scroll the board) — `tools/smoke/walk-alts.ps1` (checks 6b, 7b) — task-9-review.md Minor 1.
- DV-T9.2 **FIXED** — the "no build found" error when starting Ur Score for a walk didn't say whether the problem was a missing build or a bad `UR_SCORE_EXE` override; now it does — `tools/smoke/uia.ps1:182-183` — task-9-review.md Minor 2.

**Final whole-branch review**
- DV-F.1 **FIXED** — the Live leaderboard panel wrote every stat as a plain number, ignoring duration/date formatting (latent — no live recipe uses it yet) — `src/Board/PanelModels.cs` — final-review.md Minor 1.
- DV-F.2 **FIXED** — a focus-restore helper took a parameter that was always passed the same value, so its own test proved nothing — `src/Board/AccountsTableFocus.cs` — final-review.md Minor 2.
- DV-F.3 **FIXED** — the Accounts table would read from a source you'd switched off while Records and the Account card refused to, so the three panels could disagree with no explanation why (also raised independently in preflight.md 1.24) — `src/Board/PanelModels.cs` — final-review.md Minor 3.
- DV-F.4 **FIXED** — Profile stat and the Accounts table used two different rules for "which source to fall back to" when nothing was pinned — `src/Board/PanelModels.cs:270` — final-review.md Minor 4.
- DV-F.5 **FIXED** — "which stats a recipe suggests ticking" was implemented twice, and the two copies could drift; now one shared helper — `src/Recipes/RecipeStats.cs` (`Suggested`) — final-review.md Minor 5.
- DV-F.6 **OPEN** — when a following tab's draft is kept because its starter went empty mid-edit, it's appended as the last tab rather than reinserted where it was — `src/Board/BoardEdits.cs:48` — final-review.md Minor 6 (parked by ruling).
- DV-F.7 **FIXED** — no test proved an Accounts table panel survives being saved to and loaded back from `boards.json` — `tests/BoardsFileTests.cs` — final-review.md Minor 7.
- DV-F.8 **FIXED** — no test proved the Accounts table shows only your own accounts when the underlying source is a clan/group list with other players in it — `tests/PanelModelsTests.cs` — final-review.md Minor 8.
- DV-F.9 **OPEN** — a clan member who updates the shared recipe file before updating Ur Score itself would see the three new count stats error out, and playtime/first-join show as raw seconds; needs a release-notes/clan-post line telling people to update the app first (also raised independently in preflight.md §4) — `tests/Fixtures/petsim99-profile.recipe.json` — final-review.md Minor 9 (parked by ruling).
- DV-F.10 **FIXED** — the design doc's "changed during execution" banner was dated a day late (16th instead of 15th) — `docs/2026-09-15-default-views-design.md:23` — final-review.md Minor 10.
- DV-F.11 **FIXED** — the release step's draft CHANGELOG text and a backlog item's disposition still described the old stretched-panel layout instead of natural heights — this plan's release step 1, D20 — final-review.md Minor 11.
