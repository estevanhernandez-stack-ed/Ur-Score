# Backlog remediation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Do not start yet.** This plan was written on 2026-09-15 while v0.3.0 was still PR #6 on `feat/boards` (20ab152). Execution starts only after v0.3.0 is merged, released, installed into RoRoRo from the GitHub release, and walked by the owner. Task 0 then re-checks every item against `master` (names and line numbers may have moved) and folds in anything that smoke found. Until the owner says the release smoke is done, nothing below runs.

**Goal:** Ur Score v0.3.1: every backlog item you'd notice on screen is fixed, the backlog's remaining 104 code, test, performance and docs items are triaged into two later waves, and six are closed as won't fix with a reason.

**Architecture:**
- **Pure first, as in stages 1 and 2.** Wording, numbers and decisions go into `src/Board` (`PanelModels`, `PanelText`, `PanelForms`, `PanelGallery`, `BoardLayout`, `ChartGeometry`, `BoardEdits`), `src/Core` (`SnapshotCarry`, `AccountClaims`, `RecipeWatch`), `src/Book` (`Records`) or the Setup models (`AccountsModel`, `DiagnosticsModel`, `ScoreBookModel`, `ImportText`), each with unit tests. Windows and pages call them and draw.
- **Three waves.** Wave 1 (this plan, fully detailed) is the 36 "you'd notice" items less one that isn't reachable, plus two tidiness items that change on the same lines. Wave 2 (tests and safety nets) and wave 3 (code tidiness and performance) are outlined here and each gets its own detailed plan when the wave before it ships.
- **No format changes.** `boards.json`, `sources.json`, `accounts.json` and the score book keep their shapes, so v0.3.1 installs over v0.3.0 with nothing to migrate.

**Tech Stack:** .NET 10 WPF (`net10.0-windows`), xUnit 2.9, System.Text.Json, gRPC client to RoRoRo (`ROROROblox.PluginContract` 0.10.0). No new packages.

**Spec:** `docs/2026-09-14-score-book-design.md` (section numbers §n below refer to it). The items come from `docs/backlog.md`; each carries its ID (S1-6.8, S2-FR.2, …). The stage plans `docs/plans/2026-09-14-score-book-stage-1.md` and `docs/plans/2026-09-14-score-book-stage-2.md` hold the rulings R1–R21 and the execution-record rulings this plan keeps.

## Rulings made while planning

Each names what was decided, why, and the cost if it is wrong. They settle what the backlog and the spec leave open; a reviewer shouldn't read them as drift.

**Scope and order**

- **P1. Execution waits for the v0.3.0 release smoke, and starts by re-checking.** Task 0 re-reads every item on `master`, updates the triage table where code moved, and adds smoke findings to the backlog before any fix. *Why:* the owner's instruction; v0.3.0's review fixes may still land. *Cost if wrong:* a day's delay.
- **P2. The target is v0.3.1, a patch release.** Only fixes and wording; no new data file and no change to any file's shape. *Why:* everything in wave 1 corrects how shipped features behave. *Cost if wrong:* none; a minor bump is a one-line change in Task 13.
- **P3. Wave 1 is the 36 "you'd notice" items less S1-9.3, plus S2-P.3 and S1-14.17.** S2-P.3 (the drag handle's automation id sits on a Border) changes on the element S2-P.18 replaces; S1-14.17 (an unreachable catch) is the catch S1-12.4's first message lives in. *Cost if wrong:* two small items reviewed a wave early.
- **P4. S1-9.3 is not reachable today, so it moves to wave 3.** `Records.For` merges the series of every source id it is given, but its only callers (`PanelModels.AccountCard`, `PanelModels.RecordsPanel`) pass exactly one id, so no board shows a merged "biggest day" or "fastest 7 days". Wave 3 narrows `Records.For` to one source id so it can't become reachable. *Cost if wrong:* if a caller that passes two ids appears before wave 3, records can double-count again.
- **P5. Waves 2 and 3 get their own detailed plans,** `docs/plans/<date>-backlog-wave-2.md` when v0.3.1 ships and `docs/plans/<date>-backlog-wave-3.md` when wave 2 ships. The outlines below fix their scope, not their code. *Cost if wrong:* none.
- **P6. The backlog rows flip to FIXED in one docs commit just before the v0.3.1 PR** (Task 13), each naming its commit. *Why:* a commit can't name its own hash. *Cost if wrong:* none.

**Panel numbers and pictures**

- **P7. "#rank of N" counts the rows that have a value for that stat** (S1-6.9), on My accounts and Account card, the same count Promotion check ranks against. The score book's `of` stays the row count spec §5.2 defines: it is one number per account, not per stat, so it can't be a per-stat count without a format change (P2). *Cost if wrong:* the live panel and a line in the book disagree on N for a stat some rows lack.
- **P8. With tied ranks, the gap is to the nearest group ranked higher** (S1-13.5), shown when that group's rank plus how many share it equals this group's rank, which is exactly when the list holds every group ranked between them. 12, 12, 14: 14th measures to 12th, each 12th to 11th; 12, 14 with no 13th still shows no gap. *Cost if wrong:* a gap to one of two tied groups picks the lower-valued one, which is the same value in practice.
- **P9. The sent dot means sent in the source's last read** (S1-F.5). The snapshot carries `SentThisRead`, filled by `RecipeWatch` from the sends that succeeded, so a read that stopped shows no dots. The panel's note becomes "● sent to RoRoRo in the last read". *Cost if wrong:* between battles, when reads stop, no row shows a dot.
- **P10. A chart whose values are all equal draws its line through the middle, with one grid line labelled with that value** (S1-13.11). A chart drawn "from zero" now keeps zero on the axis at both ends, so all-negative values plot below a zero line instead of on an axis that ends below zero. *Cost if wrong:* a from-zero chart of negative values loses some vertical detail.
- **P11. A one-row panel is arranged as tall as its row** (S1-14.11), through `BoardLayout.CellHeight`, the same sum the tall panel already used. *Cost if wrong:* a short panel beside a tall one shows empty card space at its bottom (the card is the panel's own background, so it reads as one row).
- **P12. A Standing change that fell is magenta** (S1-13.14). The theme has no red; stage 1 made "warning" `MagentaBrush` and "good" `CyanBrush` (stage 1 plan, "Things every UI task must know"). *Cost if wrong:* none known.
- **P13. The drag handle is six dots drawn with `Ellipse`s in `MutedTextBrush`, inside a `Label`** (S2-P.18, S2-P.3). No font has to carry a grip glyph, and a Label has an automation peer, so `DragHandle` is found by UI Automation. The Label isn't focusable; keyboard users move panels with Move earlier and Move later (R7). *Cost if wrong:* a screen reader lists one more text element per panel in edit mode.

**Panels that go blank or say the wrong thing**

- **P14. A read that stops keeps the last good read on the board** (S1-F.6). `SnapshotCarry` copies the previous snapshot's rows, headline, groups and period into a stopped read's snapshot, with `CarriedFrom` set to when they were read; the state, detail, misses, what was sent and what was kept are the stopped read's own. The overdue mark follows the age of what is shown: `LiveBoard.IsOverdue` measures from `CarriedFrom` when there is one. Spec §5.4's "a gap is a real gap" is about the book, which still keeps nothing for a stopped read. Nothing of a different recipe is carried, and the "recipe changed" snapshot carries nothing. *Cost if wrong:* between battles the board keeps the last battle's live numbers, marked overdue once they are more than 1.5 reads old.
- **P15. Accounts that no read placed get honest headings** (S1-13.4, S1-12.7), replacing spec §4.3's "Not in a watched clan", whose "watched" meant "read":
  - "Waiting for the first read" while no source of the recipe has been read;
  - "Not found in the {groups} read so far" while some have;
  - "Not in any {group} you've added" once every source of the recipe has been read;
  - "Only in {groups} you're watching" for accounts that appear only in a watched source's rows (shown with dashes: a watched source is never matched to your accounts for keeping or sending, §4.1).
  Setup › Your accounts uses the same words, and names the group with the recipes' own word only when every recipe with inputs uses the same one, else "source". A recipe without inputs keeps "Not in the last read". *Cost if wrong:* one more group heading in My accounts.
- **P16. A Race says which problem it has** (S1-13.6): a recipe with no summed headline is "This panel's recipe has no total to race." (stale, with Choose another); a race with some of its sources removed draws the rest with the note "One of this race's clans was removed." (or "2 of … were removed."); more than 5 draws the first 5 with "Only the first 5 clans are drawn."; all removed is the stale source message as before. *Cost if wrong:* none known.
- **P17. A Profile stat never quietly reads another source** (S1-13.7, S2-FR.2). A pinned source that is gone is stale ("This panel's source was removed."). An unpinned panel (a starter's) reads the recipe's first source that is on, else its first source, exactly what `PanelForms.Build` pins, and when that source is off the head's note says "{name} is switched off, so it isn't being read." `PanelForms.Problem` accepts an unpinned Profile stat whenever its recipe has any source. A row whose value missed shows the miss's own text instead of "can't read". This changes two assertions in `PanelFormsTests.AProfileStatWhoseOnlySourceIsOffIsNotStale` (the unpinned-and-off case), which Task 5 updates. *Cost if wrong:* a board that shows an off source's last numbers instead of "was removed" (only a hand-edited `sources.json` switches a profile source off).
- **P18. An Account card pinned to one account names it** (S1-13.8), refining R16: "No reading of {name} yet." when RoRoRo lists that account, "RoRoRo isn't listing this panel's account right now." when it doesn't; "No reading of your accounts yet." stays for "Your top account". *Cost if wrong:* none known.

**Forms and the gallery**

- **P19. The Race card counts only sources that are on** (S2-P.5), as `PanelForms.Defaults` does, so an enabled card always opens on a form with no problem. Its reason text is unchanged: only the Top switch turns a source off in Setup; a clan source is off only in a hand-edited `sources.json`. *Cost if wrong:* a user with a hand-edited file sees "Needs at least 2 clans" while having two.
- **P20. A panel whose recipe is gone uses "source" in its form, as its board does** (S2-4.5). The words of a removed recipe aren't known; guessing from another recipe could call a profile panel's source a clan. A blank form with no recipe picked keeps the first recipe with inputs. *Cost if wrong:* a less friendly word in a rare case.
- **P21. The Promotion check card speaks for the first recipe a Promotion check can actually be added on, the main's recipe first** (S2-4.6), and its Can add uses the same list. *Cost if wrong:* none known.
- **P22. Past periods' Stat list ends with "Don't show your best account"** (S2-FR.1), key `PanelForms.NoStatKey`, so a panel whose stat was removed can be saved when its recipe has no ticked stat left. It comes last, so a new panel still defaults to the first ticked stat (R14). *Cost if wrong:* one more entry in one list.
- **P23. Duplicate is off while the board on screen is the first-run starter still following your sources with no panels** (S2-F.8). There is nothing to copy, and the first save would drop the original under the F4 ruling (`BoardEdits.ForFirstSave`), which stays. The state is decided by `BoardButtons.For` (execution-record ruling), through a new `nothingToCopy` input. *Cost if wrong:* none known.

**The board window**

- **P24. The state line says what Ur Score is waiting for** (S1-14.3, S1-14.5): "Starting: asking RoRoRo for your accounts…" while Start waits (up to `AppServices.AccountsWait`), "Reading every source once…" while Test now reads, and, stopped, the last read's news after "Stopped." or "Not started.": "Last read, K0i2: Could not reach the data." for a source in trouble, else "The last read covered 3 sources." *Cost if wrong:* a longer line after Stop.
- **P25. A score book that can't be read shows as the board's empty state, with Try again** (S1-14.2, S2-8.3): "Your score book couldn't be read" / "Nothing is read or kept until Ur Score can read it. The reason is on the line above." / **Try again**, which calls `AppServices.LoadBookAsync` again (it already starts a new load after a failed one). While the book isn't read, no pop-out opens, and the empty state covers the "is popped out" slots. *Cost if wrong:* none known.
- **P26. The period line never follows a group list while the board has a source of its own recipe** (S2-5.9). `BoardEdits.AnchorSourceId` takes the installed recipes and picks, in order: a panel's own source that is on and isn't a group list; a source that is on of a panel's recipe (My accounts and Records name only a recipe); the main; a panel's group-list source. *Cost if wrong:* none known.
- **P27. A recipe update that drops its icon puts Ur Score's own icon back** (S1-14.1): recipes that are gone or have no `icon` lose their fetched icon when recipes load. *Cost if wrong:* none known.
- **P28. Only a close you asked for returns a pop-out at once** (S2-8.2): its own ✕, Alt+F4, or the taskbar's Close window (the last two arrive as the system Close command). Any other close (an updater or `taskkill` without `/f` sends WM_CLOSE to every window) waits 2 seconds for Ur Score itself to close, which keeps every `popout` for the next start; if Ur Score is still open after the wait, the panel returns. *Cost if wrong:* a pop-out closed by another program alone takes 2 seconds to return to its board.

**Setup**

- **P29. Claim conflicts are named in Setup, not redrawn on the panels** (S1-6.8, S1-F.1). `AccountClaims.TryClaim` reports the source that holds a claim, `RecipeSnapshot.KeptElsewhere` carries your accounts another source kept (your ids only), Setup › Diagnostics lists them by display name under the source that kept them (spec §4.2 "Diagnostics notes it"), and Setup › Score book's reason names that source. Panels keep showing live rows as read: an account mid-way through a group switch really is in both lists. *Cost if wrong:* Standing's "Your accounts N of M" counts that account on both panels for up to two reads.
- **P30. After Stop, Diagnostics says "Not running. Last read: …"** (S1-12.8), so a state from the last read never reads as current. *Cost if wrong:* none known.
- **P31. A Setup message stays until the next action on its page** (S1-12.4, S1-14.10). Score book keeps "Could not open the folder" on its own line; Recipes restores its last message when an import is cancelled; an import that opens a Clans page carries its result to that page's line under the main search (`SetupWindow.ShowPage(pageId, note)`). The accounts page's "Could not ask RoRoRo" was unreachable (`RefreshAccountsAsync` never throws for RoRoRo's sake) and goes with its catch (S1-14.17). *Cost if wrong:* none known.
- **P32. A recipe that was saved but didn't load says so** (S1-12.12): "{name} was saved, but Ur Score couldn't load it. Restart Ur Score to load it." The trail gets the exception's type. *Cost if wrong:* none known.
- **P33. The import screen's "Kept in your score book" list names everything a read keeps besides ticked stats** (S1-F.8): headline items, "Which {period} each read belongs to", "When the {period} starts and ends" (or starts, or ends), and "When the source last updated the numbers" (", and whether it calls them stale" when the recipe reads that). *Cost if wrong:* a longer list.
- **P34. Every stock message box becomes Ur Score's own themed `MessageWindow`** (S1-11.4), not only the sixth-source confirmation: theming one of nine would make the app less consistent. Questions default to Cancel, as the stock boxes did; a fence test keeps `MessageBox.Show` out of `src`. The smoke helpers `Close-MessageBox` and `Get-MessageBoxText` learn the new window in the same commit. *Cost if wrong:* nine call sites touched for a cosmetic gain.

**Docs**

- **P35. The README is rewritten for 0.3.x, and the CHANGELOG catches up** (S2-F.7). The README still describes the pre-recipe build (a clan name in `settings.json`, "Start Score Watch"); a boards section on top of it would sit beside instructions that no longer exist. The CHANGELOG stops at 0.1.0 plus an "Unreleased" section that shipped in 0.2.0; it gains 0.2.0, 0.3.0 and 0.3.1. Neither names a game: recipes do. *Cost if wrong:* a longer review of Task 12.
- **P36. The stage 2 plan's text errors get errata appended, not rewrites** (S2-P.4, S2-P.7, S2-P.19, S2-8.5, wave 3). The plan is an executed record; its steps won't run again. *Cost if wrong:* none.

**Won't fix**

- **P37. S1-2.3 (wrong co-author lines on stage 1 commits).** Merged history on a public repository; fixing it means rewriting published commits and breaking every clone and tag. *Cost if wrong:* none; the lines stay as they are.
- **P38. S1-4.2 (after a failed or cancelled request, the next one to that site still waits 2 s).** Global Constraint: at least 2 s apart per host. A failed request may still have reached the host, so waiting is the safe reading, and it can only be slower, never too fast. *Cost if wrong:* up to 2 s slower recovery after a failure.
- **P39. S1-6.5 (switching a clan to Watch during a read lets the rest of that read send).** By design: `RecipeWatch.UpdateSource` applies to the next cycle, and what the rest of that read sends is your own account's value, which the source was set to send when the read began. *Cost if wrong:* one read's values for your own accounts reach RoRoRo after you chose Watch.
- **P40. S1-9.2 (two final lines for one account in one battle: the first wins).** Finals are write-once (§6.1: a final is written only when the book has none for that period and account), so a second one can only come from a duplicate write, and the first is the stable choice. *Cost if wrong:* none known; no source corrects a settled period today.
- **P41. S1-9.4 (a past battle's time is its earliest final line).** Deliberate: a supplementary final for an alt added later (§6.1) must not move an old battle to the top of Past periods, which sorts by that time. *Cost if wrong:* none known.
- **P42. S2-P.9 (an unreadable `boards.json` is copied verbatim).** By design under R3: the copy exists so your hand-edited file isn't lost, it doesn't parse so it can't be sanitized, and anything in it is text you typed. *Cost if wrong:* a stranger's id you typed into your own file stays in your own copy.

---

## Global Constraints

Carried from stages 1 and 2 (`docs/plans/2026-09-14-score-book-stage-1.md`, `…-stage-2.md`):

- **No hostname literal in `src/`** except `NameClient.cs` (users.roblox.com) and `IconClient.cs` (thumbnails.roblox.com). `NoHostnameFenceTests` enforces it. Recipes and fixtures carry hosts; code never does.
- **Other players never reach disk.** Another player's Roblox id, name or value is never written to state, `sources.json`, `accounts.json`, `boards.json`, the score book, the trail, diagnostics or the clipboard. Live panels may show them in memory.
- **Keys never reach disk.** A saved key value never appears in files, errors, the trail or the clipboard. `Redactor` masks keys in any text that could carry one.
- **One path to RoRoRo.** `ReportPolicy.SendAsync` is the only caller of `IHostClient.ReportMetricAsync` (`ReportPolicyTests` enforces it).
- **Theme.** Themed brushes are referenced with `DynamicResource` only. No hex colours or literal brushes in `src/UI` XAML, and no colour code in UI `.cs` (`ThemeFenceTests`). Every brush used is one `ThemeService` paints: `BgBrush`, `CyanBrush`, `MagentaBrush`, `WhiteBrush`, `MutedTextBrush`, `DividerBrush`, `RowBgBrush`, `RowHoverBrush`, `EdgeBrush`.
- **Ur Score's own text never names a game.** Words like "clan" come from the recipe (input `plural`, labels, `RecipeWords`).
- **Copy style:** sentence case, second person, specific, no emoji.
- **Group-list recipes** are never matched to accounts, never sent, never recorded. **`watch` sources** send nothing and record no accounts.
- **Requests:** at most one request at a time per host, at least 2 s apart.
- **Build gate:** `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror` passes, and `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build` passes. Run both at the end of every task.
- **Commits:** one or more per task, message style `area: what changed`, ending with the line `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.
- **`boards.json` holds no other player** (R17). **Pure first:** logic in `src/Board` and the other pure classes, with unit tests; windows decide nothing a test can't see.
- **Automation ids** that `tools/smoke` relies on change only with their scripts, in the same commit.
- **`System.IO` is not an implicit using in the app project** (WPF drops it); a `src/` file using `File`, `Directory` or `Path` needs `using System.IO;`. **The `Source` name clash:** in `Labs626.UrScore.UI`, `.Board`, `.Book` or `.Composition` files, `using Source = Labs626.UrScore.Core.Source;` goes after the file-scoped `namespace` line.
- **A running Ur Score locks `bin\Release`.** Close it (tray or window) before building.

Carried from the stage 2 execution record (rulings made during execution):

- Every list in `src/UI` is a `ui:RowList`, so row controls reach UI Automation by name.
- Board-window button state is decided only by `BoardButtons.For` and applied by `ApplyButtons`.
- One save path for boards: a failed board save shows a message owned by the board; a failed pop-out position save goes to the trail only. The boards-file problem (R3) takes precedence on the detail line.
- Edits that change nothing return the same instance and write nothing. Handlers re-read the boards after a dialog closes.
- Titles and a source's role label live in `PanelText`, shared by `PanelModels`, the gallery and the forms.
- Pop-outs clamp to monitor work areas, never maximize, and keep current automation ids after a renumber.

Added for this plan:

- **Execution gate (P1):** Task 0 runs only after the owner says the v0.3.0 release smoke is done. Work happens on `fix/backlog-wave-1`, branched from `master` after v0.3.0 merged.
- **No format or data-file change (P2):** `boards.json`, `sources.json`, `accounts.json`, `settings.json` and book lines keep their shapes. No new package.
- **Smoke walks are the controller's.** Each task that changes the UI names the walk steps to re-run or add; the controller runs them against RoRoRo 1.28 before Task 13. New and changed steps are ASCII only (Windows PowerShell 5.1 reads BOM-less files as ANSI).
- **Tests name fixture accounts by reference** (`Main.DisplayName`, `AltOne.RobloxUserId`), never by typing a real-looking name or id (S1-F.11 cleans the existing ones in wave 3).
- **Merging, tagging and releasing are never pre-authorized.** Each needs the owner's OK in the session (Task 13).
- **No literal control characters** in code: `(char)0x1F`, `'\u001F'`.

---

## File structure (wave 1)

New files:

| File | Responsibility |
|---|---|
| `src/Core/SnapshotCarry.cs` | Keeps the last good read's rows, headline, groups and period on a stopped read's snapshot (P14) |
| `src/UI/MessageWindow.xaml(.cs)` | Ur Score's themed message box: `Ask` (OK/Cancel) and `Tell` (OK) (P34) |
| `tests/SnapshotCarryTests.cs` | `SnapshotCarry` |
| `tests/AppServicesIconTests.cs` | `AppServices.IconsToForget` |
| `tools/smoke/walk-book-unreadable.ps1` | A score book that can't be read, Try again, and a pop-out that waits for the book (P25) |

Modified files, by task:

| Task | Files |
|---|---|
| 1 | `src/Board/ChartGeometry.cs`, `src/Board/BoardLayout.cs`, `src/UI/Controls/PanelGrid.cs`, `src/UI/Panels/PanelFrame.xaml`, `src/Book/Records.cs`, `src/Board/PanelModels.cs`, `src/UI/Panels/StandingPanel.xaml`; tests `ChartGeometryTests`, `BoardLayoutTests`, `PanelModelsTests`; walks `walk-starter-board.ps1`, `walk-board-editing.ps1` |
| 2 | `src/Board/PanelModels.cs`, `src/Core/RecipeWatch.cs`; tests `PanelModelsTests`, `RecipeWatchBookTests` |
| 3 | `src/Core/RecipeWatch.cs` (snapshot property), `src/Board/PanelModels.cs` (`LiveBoard.IsOverdue`), `src/Composition/AppServices.cs`; tests `SnapshotCarryTests`, `PanelModelsTests` |
| 4 | `src/Board/PanelText.cs`, `src/Board/PanelModels.cs`, `src/UI/Setup/AccountsModel.cs`; tests `PanelTextTests`, `PanelModelsTests`, `AccountsModelTests`; walk `walk-starter-board.ps1` |
| 5 | `src/Board/PanelText.cs`, `src/Board/PanelModels.cs`, `src/Board/PanelForms.cs`; tests `PanelModelsTests`, `PanelFormsTests` |
| 6 | `src/Board/PanelGallery.cs`, `src/Board/PanelForms.cs`, `src/UI/BoardButtons.cs`, `src/UI/BoardWindow.xaml.cs`; tests `PanelGalleryTests`, `PanelFormsTests`, `BoardButtonsTests`; walk `window-smoke.ps1` |
| 7 | `src/UI/BoardText.cs`, `src/Board/StarterBoards.cs` (`BoardEmpty.BookNotRead`), `src/Board/BoardEdits.cs`, `src/UI/BoardButtons.cs` (doc), `src/UI/BoardWindow.xaml.cs`, `src/Composition/AppServices.cs`; tests `BoardTextTests`, `BoardEditsTests`, `AppServicesIconTests`; walks `walk-starter-board.ps1`, new `walk-book-unreadable.ps1`, `tools/smoke/README.md` |
| 8 | `src/UI/Boards/PanelPopOutWindow.xaml.cs`, `src/UI/BoardWindow.PopOuts.cs`; walk `walk-pop-outs.ps1` |
| 9 | `src/Core/AccountClaims.cs`, `src/Core/RecipeWatch.cs`, `src/UI/Setup/DiagnosticsModel.cs`, `src/UI/Setup/DiagnosticsPage.xaml`, `src/UI/Setup/ScoreBookModel.cs`; tests `RecipeWatchBookTests`, `DiagnosticsModelTests`, `ScoreBookModelTests` |
| 10 | `src/UI/Setup/DiagnosticsModel.cs`, `src/UI/Setup/AccountsPage.xaml.cs`, `src/UI/Setup/ScoreBookPage.xaml(.cs)`, `src/UI/Setup/RecipesPage.xaml.cs`, `src/UI/SetupWindow.xaml.cs`, `src/UI/Setup/ClansPage.xaml.cs`, `src/UI/Setup/ImportFlow.cs`, `src/UI/ImportText.cs`; tests `DiagnosticsModelTests`, `ImportTextTests`; walk `window-smoke.ps1` |
| 11 | `src/UI/BoardWindow.xaml.cs`, `src/UI/Setup/AlertsPage.xaml.cs`, `src/UI/Setup/ClansPage.xaml.cs`, `src/UI/Setup/ImportFlow.cs`, `src/UI/Setup/RecipesPage.xaml.cs`; tests `ThemeFenceTests`; helpers `tools/smoke/uia-import.ps1`, walk `window-smoke.ps1` |
| 12 | `README.md`, `CHANGELOG.md` |
| 13 | `manifest.json`, `Ur-Score.csproj`, `CHANGELOG.md`, `docs/backlog.md`, this plan (execution record) |

## Interface contract (wave 1)

Every task implements, or relies on, exactly these names. Everything else keeps the names it has on `master` after v0.3.0.

```csharp
// ---- Task 1 ----
// Labs626.UrScore.Board
public static class BoardLayout { public static double CellHeight(PanelPlacement placement, IReadOnlyList<double> rows, double gap); }
public sealed record StandingModel(/* unchanged positional parameters */) { public bool ChangeFell { get; init; } }
// Labs626.UrScore.Book
public static class Records { public static bool Fell(IReadOnlyList<SeriesPoint> series); }   // Change's baseline, and the last value below it
// ChartGeometry.Layout keeps its signature (P10).

// ---- Task 2 ----
// Labs626.UrScore.Core
public sealed record RecipeSnapshot { public IReadOnlySet<(Guid AccountId, string Stat)> SentThisRead { get; init; } }

// ---- Task 3 ----
public sealed record RecipeSnapshot { public DateTimeOffset? CarriedFrom { get; init; } }
public static class SnapshotCarry { public static RecipeSnapshot Carry(RecipeSnapshot? previous, DateTimeOffset? previousAt, RecipeSnapshot next); }

// ---- Task 4 ----
// Labs626.UrScore.Board
public static class PanelText
{
    public static string NotFound(string group, string groups, int read, int sources);   // P15
    public static string OnlyWatched(string groups);
}

// ---- Task 5 ----
public static class PanelText
{
    public const string NoTotalToRace = "This panel's recipe has no total to race.";
    public static string RaceRemoved(int removed, string groups);
    public static string RaceOverLimit(string groups);
    public static string SwitchedOff(string name);
}

// ---- Task 6 ----
public static class PanelForms { public const string NoStatKey = ""; }
// Labs626.UrScore.UI
public static class BoardButtons
{
    public static BoardButtonStates For(bool loaded, bool running, bool starting, bool testing, bool importing, int boards = 1, bool editing = false, bool nothingToCopy = false);
}

// ---- Task 7 ----
public enum BoardEmpty { None, NoRecipes, NoStats, NoSources, NoPanels, BookNotRead }            // Labs626.UrScore.Board
public static class BoardText                                                                      // Labs626.UrScore.UI
{
    public const string Starting = "Starting: asking RoRoRo for your accounts…";
    public const string Testing = "Reading every source once…";
    public static string StateLine(LiveBoard live, bool everStarted, bool starting = false, bool testing = false);
    public static BoardEmpty EmptyFor(StarterBoard starter, bool followsStarter, BoardDef board, bool bookUnread = false);
}
public static class BoardEdits { public static string? AnchorSourceId(BoardDef board, IReadOnlyList<Source> sources, IReadOnlyList<InstalledRecipe> installed); }   // replaces the 2-argument form
public sealed class AppServices { internal static IReadOnlyList<string> IconsToForget(IEnumerable<string> slugsWithIcons, IReadOnlyList<InstalledRecipe> installed); }

// ---- Task 8 ----
public partial class PanelPopOutWindow { public bool ClosedByYou { get; } }

// ---- Task 9 ----
public sealed class AccountClaims { public bool TryClaim(string recipe, long userId, string sourceId, TimeSpan window, out string owner); }   // the 4-argument form stays
public sealed record RecipeSnapshot { public IReadOnlyDictionary<long, string> KeptElsewhere { get; init; } }
public sealed class RecipeWatch { internal const string NotRecordingKeptElsewhere = "Your accounts in this read are kept by another source of this recipe, which read them first."; }
public sealed record SourceDiagnostic(string SourceId, string Name, string State, string Detail, string LastRead, string NextRead, string Misses, string KeptElsewhere = "")
{ public bool HasKeptElsewhere { get; } }
public static class DiagnosticsModel { public static string KeptElsewhere(Recipe? recipe, RecipeSnapshot? snapshot, IReadOnlyList<Source> sources, IReadOnlyList<HostAccount> accounts); }

// ---- Task 10 ----
public partial class SetupWindow { public void ShowPage(string pageId, string? note = null); }
public partial class ClansPage { public void ShowNote(string note); }
public static class ImportFlow { public static string SavedNotLoaded(Recipe recipe); }

// ---- Task 11 ----
public partial class MessageWindow : Window
{
    public static bool Ask(Window? owner, string text);   // true only for OK; Cancel is the default
    public static void Tell(Window? owner, string text);
}
```

## Automation ids (wave 1)

Stage 1's and stage 2's tables still hold. Wave 1 adds or changes:

| Where | Automation ids |
|---|---|
| Inside a panel, edit mode | `DragHandle` is now a `Label` (same id, now visible to UI Automation) |
| Board | `EmptyStateButton` named `Try again` while the score book can't be read |
| Message (`Ur Score`, window automation id `MessageWindow`) | `MessageText`, `MessageOkButton`, `MessageCancelButton` (questions only) |
| Setup › Score book | `BookFolderProblemLine` |

---

## Triage

Every OPEN item in `docs/backlog.md`, once. "W1 T4" is wave 1 Task 4 below; "W2-3" and "W3-6" are the outlined wave 2 and wave 3 tasks. Items fixed by the same change share a task.

| ID | What (short) | Triage |
|---|---|---|
| S1-1.1 | test: a period `past` path can't point at one player | W2-1 |
| S1-1.2 | test: an empty input label falls back to "Items" | W2-1 |
| S1-1.3 | tidy: the group-list rule repeated with no comment | W3-5 |
| S1-2.1 | tidy/test: a read that stops on its last step carries a period | W3-3 |
| S1-2.2 | edge: a fractional rank is cut to a whole number | W3-3 |
| S1-2.3 | process: wrong co-author lines in merged history | Won't fix: rewriting published history (P37) |
| S1-3.1 | test: promoting a watched clan straight to main | W2-2 |
| S1-3.2 | edge: re-adding a switched-off clan switches it on | W3-5 |
| S1-4.1 | test: a second site isn't held up by the first | W2-2 |
| S1-4.2 | edge: after a failed request the next still waits 2 s | Won't fix: the 2 s spacing is a Global Constraint and can only err slow (P38) |
| S1-4.3 | tidy: claims and per-site lanes never forget | W3-5 |
| S1-5.1 | edge: the dropped-lines count can be off by one | W3-1 |
| S1-5.2 | tidy: `Written`'s comment names the wrong thread | W3-1 |
| S1-5.3 | test: `RemovingARecipeLeavesItsBook` exercises nothing | W2-3 |
| S1-5.4 | test: the writer loop's last-resort catch | W2-3 |
| S1-5.6 | test: the drop-limit test takes about 24 s | W2-3 |
| S1-6.1 | test: a recipe change mid-send with a book | W2-2 |
| S1-6.2 | test: a group list on your own source | W2-2 |
| S1-6.3 | test: the claim expiring after twice the interval | W2-2 |
| S1-6.4 | tidy: the line records the source's inputs, not the read's | W3-3 |
| S1-6.5 | edge: switching to Watch mid-read lets that read send | Won't fix: by design, applies next read (P39) |
| S1-6.6 | tidy: the lock comment vs an unlocked `Recipe` read | W3-3 |
| S1-6.7 | tidy: report timestamps use the system clock | W3-3 |
| S1-6.8 | notice: wrong "not recording" reason when another source claimed your account | W1 T9 |
| S1-6.9 | notice: "#rank of N" counts rows with no value | W1 T2 |
| S1-6.10 | edge: the headline id guard sees only this read's rows | W3-3 |
| S1-6.11 | tidy: the recipe hash is recomputed every read | W3-3 |
| S1-7.1 | tidy: an unnecessary fully qualified name | W3-1 |
| S1-7.2 | tidy: "the period has ended" written twice | W3-1 |
| S1-8.1 | tidy: cancellation sources never disposed | W3-4 |
| S1-8.2 | edge: exit doesn't wait for a read in flight | W3-4 |
| S1-8.3 | edge: Test now keeps reading a clan removed mid-read | W3-4 |
| S1-8.4 | edge: off and straight back on can run two watches | W3-4 |
| S1-8.5 | tidy: a watch is created inside the lock | W3-4 |
| S1-8.6 | tidy: a source reference read without the lock | W3-4 |
| S1-8.7 | test: `SourceHost` Stop/Start, timers, clamp, Dispose, lookup guard, the removal race | W2-3 |
| S1-8.8 | test: `Assert.All` over no book lines passes | W2-3 |
| S1-9.1 | test: the reader's headline series | W2-1 |
| S1-9.2 | edge: two finals for one account in one battle, the first wins | Won't fix: finals are write-once (P40) |
| S1-9.3 | notice (not reachable): Records merges sources' series | W3-1 (P4) |
| S1-9.4 | edge: a past battle's time is its earliest final | Won't fix: keeps old battles from jumping to the top (P41) |
| S1-9.5 | tidy: fully qualified `Source.KeyOf` | W3-1 |
| S1-10.1 | tidy: the same `Show` helper in 7 files | W3-7 |
| S1-10.2 | tidy: a refused Send tick redraws up to three times | W3-7 |
| S1-10.3 | test: a saved counter the recipe no longer recognises | W2-1 |
| S1-11.1 | tidy: clan search picks and name loading with no outer catch | W3-7 |
| S1-11.2 | edge: the Top switch uses the first group-list recipe | W3-7 |
| S1-11.4 | notice: the sixth-source confirmation is a stock message box | W1 T11 |
| S1-12.4 | notice: Setup messages vanish on the next refresh | W1 T10 |
| S1-12.5 | perf: disk reads on the UI thread on every refresh | W3-6 |
| S1-12.6 | edge: Stats asks for counter names again on each switch | W3-7 |
| S1-12.7 | notice: an account only in a watched clan reads "Not in a watched clan" | W1 T4 |
| S1-12.8 | notice: Diagnostics can say "Reporting to RoRoRo." after Stop | W1 T10 |
| S1-12.9 | tidy: an unknown page id shows Diagnostics; an unused parameter | W3-7 |
| S1-12.11 | test: Alerts tests leave temp folders; three untested cases | W2-4 |
| S1-12.12 | notice: a failed reload says "Could not save that recipe" | W1 T10 |
| S1-13.4 | notice: before the first read everyone is "Not in a watched clan" | W1 T4 |
| S1-13.5 | notice: no gap to the group above when ranks are tied | W1 T2 |
| S1-13.6 | notice: Race says "clan was removed" when it has no total | W1 T5 |
| S1-13.7 | notice: Profile stat falls back to another source; "can't read" | W1 T5 |
| S1-13.8 | notice: an Account card's chosen account says "your accounts" | W1 T5 |
| S1-13.9 | tidy: Past periods regroups finals | W3-2 |
| S1-13.10 | tidy: your account-id set rebuilt on every access | W3-9 |
| S1-13.11 | notice: a chart of equal values on an invented axis | W1 T1 |
| S1-13.12 | test: expected values computed with the code's own calls; `withGap` asserts no gap | W2-1 |
| S1-13.13 | tidy: an unknown panel type becomes a Live leaderboard | W3-9 |
| S1-13.14 | notice: Standing's change is cyan when it fell | W1 T1 |
| S1-13.15 | tidy: `PanelModels.cs` is 761 lines | W3-9 |
| S1-13.16 | test: "no earlier read" never asserted; no non-UTC test | W2-1 |
| S1-14.1 | notice: a dropped recipe icon stays on the window | W1 T7 |
| S1-14.2 | notice: a score book that fails to load keeps Start off | W1 T7 |
| S1-14.3 | notice: Start waits up to 20 s with nothing on screen | W1 T7 |
| S1-14.5 | notice: stopped, the top line never shows a Test now result | W1 T7 |
| S1-14.6 | tidy: async paths with no catch | W3-6 |
| S1-14.7 | edge: an icon fetch that throws is never retried | W3-6 |
| S1-14.8 | edge: a tracked-stats change releases a held stop | W3-6 |
| S1-14.9 | test: `AppServices` and App startup | W2-4 |
| S1-14.10 | notice: a cancelled import blanks the Recipes line | W1 T10 |
| S1-14.11 | notice: ragged panel bottoms in a row | W1 T1 |
| S1-14.12 | tidy: the icon client and closing token never disposed | W3-6 |
| S1-14.13 | edge: a late policy refresh briefly allows an excluded stat | W3-6 |
| S1-14.14 | privacy tidy: trail lines carry exception text | W3-6 |
| S1-14.15 | edge: a denied `sources.json` looks missing | W3-5 |
| S1-14.16 | tidy: a dead `_services is null` check | W3-6 |
| S1-14.17 | tidy: an unreachable catch around the accounts refresh | W1 T10 (with S1-12.4, P3) |
| S1-15.1 | test: `--try` JSON checks one other id | W2-6 |
| S1-15.2 | test: `--try` runs before the single-instance lock | W2-4 |
| S1-15.4 | test: `--try` never run against a real battle | W2-6 |
| S1-16.1 | smoke: a killed walk leaves your data in a backup unnoticed | W2-7 |
| S1-F.1 | notice: claim conflicts not in Diagnostics | W1 T9 |
| S1-F.2 | perf: startup reads the book twice | W3-2 |
| S1-F.3 | perf: each chart scans every kept reading | W3-2 |
| S1-F.4 | edge: the recipe text isn't written atomically | W3-1 |
| S1-F.5 | notice: the SENT dot means "sent this session" | W1 T2 |
| S1-F.6 | notice: a read that stops blanks the panels | W1 T3 |
| S1-F.8 | notice: "Kept in your score book" leaves out period values | W1 T10 |
| S1-F.9 | tidy: the account-list fallback catches every exception | W3-5 |
| S1-F.10 | edge: a book line that fails to load leaves no trail note | W3-1 |
| S1-F.11 | docs: real-looking names and ids in tests; game names in comments | W3-10 |
| S1-L.1 | test: the starter walk ignores whether Test now went disabled | W2-7 |
| S1-L.2 | test: the RowList automation test vs the original bug's shape | W2-8 |
| S1-L.3 | test: walk-score-book sleeps a fixed 20 s | W2-7 |
| S1-L.4 | test: ListBox lists aren't covered by the RowList fence | W2-8 |
| S1-L.5 | accessibility: lists expose their controls directly | W2-8 |
| S2-P.3 | test/accessibility: `DragHandle` on a Border UIA can't see | W1 T1 (with S2-P.18, P3) |
| S2-P.4 | plan text: CS0117 where CS1061 is right | W3-10 (P36) |
| S2-P.5 | notice: the Race card counts switched-off clans | W1 T6 |
| S2-P.7 | plan text: Task 9's Consumes list | W3-10 (P36) |
| S2-P.9 | edge: an unreadable `boards.json` is copied verbatim | Won't fix: R3 keeps your own text (P42) |
| S2-P.11 | test: only `AppServices` writes `boards.json`, through `Sanitize` | W2-4 |
| S2-P.13 | tidy: MainClan/AltClan/Rival fixtures in 5 test classes | W3-9 |
| S2-P.14 | test: the new-board naming rule lives in the window | W2-5 |
| S2-P.16 | test: pop-outs from two boards can share an automation id | W2-5 |
| S2-P.18 | notice: the drag handle is a Braille glyph | W1 T1 |
| S2-P.19 | plan text: the contract omits two names | W3-10 (P36) |
| S2-1.1 | edge: a locked `boards.json` throws while reading the old file | W3-8 |
| S2-1.2 | tidy: Save's comment | W3-8 |
| S2-2.1 | tidy: a redundant alias in `BoardEdits` | W3-8 |
| S2-2.2 | tidy: two identical search loops | W3-8 |
| S2-2.3 | tidy: doc comments on some `BoardEdits` methods only | W3-8 |
| S2-4.1 | tidy: "★ name" hand-built twice | W3-9 |
| S2-4.5 | notice: a panel with its recipe gone says "clan" in its form, "source" on the board | W1 T6 |
| S2-4.6 | notice: the Promotion check card can use another recipe's words | W1 T6 |
| S2-4.7 | tidy: the gallery recomputes defaults on every title call | W3-9 |
| S2-5.1 | perf: the following starter is rebuilt on every `Boards` read | W3-6 |
| S2-5.9 | notice: a Battle starter's period line may follow another source | W1 T7 |
| S2-5.10 | edge: a retried save can make a second unreadable copy | W3-8 |
| S2-6.4 | test: "no race list" equal to "empty race list" | W2-5 |
| S2-6.7 | tidy: settings applied by panel id without a type check | W3-8 |
| S2-8.2 | notice: an outside close returns pop-outs | W1 T8 |
| S2-8.3 | notice: "is popped out" slots with no windows when the book fails | W1 T7 |
| S2-8.5 | plan text: the automation id table misses the slot's Remove | W3-10 (P36) |
| S2-8.6 | test: pop-outs on mixed-DPI monitors | W2-8 |
| S2-9.1 | docs: the smoke README rows don't mention the privacy check | W3-10 |
| S2-F.7 | notice (docs): README and CHANGELOG say nothing about boards | W1 T12 |
| S2-F.8 | notice: duplicating the empty first-run starter drops the original | W1 T6 |
| S2-FR.1 | notice: a Past periods panel whose stat was removed can't be saved | W1 T6 |
| S2-FR.2 | notice: a Profile stat's form picks an off source while the board says "removed" | W1 T5 |
| S2-FR.3 | test: walk-pop-outs step 3 skips when reads are broken | W2-7 |
| S2-FR.4 | tidy: the menu item style handles flat items only | W3-7 |
| V3-S.1 | notice: a clan outside the current battle gets no past battles backfilled | W1, planned in T0 (new task) |
| V3-S.2 | notice: updating an old recipe starts with every stat unticked | W1, planned in T0 (with T10) |
| V3-S.3 | notice: "What changed" omits battle tracking | W1, planned in T0 (with T10) |
| V3-S.4 | notice: the footer repeats one credit per recipe | W1, planned in T0 (with T7) |
| V3-S.5 | test: smoke scripts can't target the installed exe | W2 |

Totals: wave 1, 41 items (39 you'd notice, S2-P.3, S1-14.17); wave 2, 34; wave 3, 64 (S1-9.3 included); won't fix, 6. 145 in all. The five V3-S items came from the v0.3.0 smoke on 2026-09-15; T0 writes their detailed steps before wave 1 starts, and V3-S.1 is its own task.

---

# Wave 1

### Task 0: Re-check against master and fold in the release smoke

P1. No code changes; this task makes sure every later task edits the code that is really there.

**Files:**
- Modify: `docs/backlog.md` (a new section for the release smoke's findings)
- Modify: `docs/plans/2026-09-15-backlog-remediation.md` (this plan: the triage table and any task whose code moved)

**Interfaces:**
- Consumes: nothing.
- Produces: a branch `fix/backlog-wave-1` at `master` after v0.3.0, and a plan whose file and line references match it.

- [ ] **Step 1: Confirm the gate**

Ask the owner whether the v0.3.0 release smoke (install from the GitHub release into RoRoRo, then the walk) is done, and what it found. Don't go on without a yes.

- [ ] **Step 2: Branch from master**

```bash
git fetch origin
git checkout master
git pull --ff-only
git tag --list v0.3.0
git log -1 --oneline
git checkout -b fix/backlog-wave-1
```

Expected: `v0.3.0` is listed, and the last commit is the merge of PR #6 (or later).

- [ ] **Step 3: The gates pass before anything changes**

Close Ur Score if it runs. Run:

```bash
dotnet build Ur-Score.csproj -c Release -warnaserror
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build
```

Expected: both builds succeed and every test passes. Write the test count down; each later task only adds to it.

- [ ] **Step 4: Every wave 1 task still finds the code it replaces**

Run each line; each must print at least one match. A line that prints nothing means that code moved or changed after this plan was written: open the backlog item, find the code's new shape, and correct that task's steps here before running it.

```bash
git grep -n "if (maxV <= minV) maxV = minV + 1;" -- src/Board/ChartGeometry.cs
git grep -n "child.DesiredSize.Height" -- src/UI/Controls/PanelGrid.cs
git grep -n "<Border x:Name=\"DragHandle\"" -- src/UI/Panels/PanelFrame.xaml
git grep -n "Text=\"{Binding Change}\" Foreground=\"{DynamicResource CyanBrush}\"" -- src/UI/Panels/StandingPanel.xaml
git grep -n "of {rows.Count}\|of {listRows.Count}" -- src/Board/PanelModels.cs
git grep -n "above.Rank != here.Rank - 1" -- src/Board/PanelModels.cs
git grep -n "snapshot.Accounts.Any(l => l.AccountId == account.AccountId" -- src/Board/PanelModels.cs
git grep -n "if (sent) Remember(" -- src/Core/RecipeWatch.cs
git grep -n "_latest\[sourceId\] = snapshot;" -- src/Composition/AppServices.cs
git grep -n "Not in a watched" -- src/Board/PanelModels.cs src/UI/Setup/AccountsModel.cs
git grep -n "sources.Count == 0)" -- src/Board/PanelModels.cs
git grep -n "?? live.Sources.FirstOrDefault(s => s.Enabled && string.Equals(s.Recipe, recipe.Slug" -- src/Board/PanelModels.cs
git grep -n "No reading of your accounts yet." -- src/Board/PanelModels.cs
git grep -n ".Any(g => g.Count() >= 2)" -- src/Board/PanelGallery.cs
git grep -n "type == PanelType.Top ? GroupWords((Recipe?)null) : GroupWords(live)" -- src/Board/PanelForms.cs
git grep -n "DuplicateBoard: !editing" -- src/UI/BoardButtons.cs
git grep -n "StateLine.Text = \"Reading every source once" -- src/UI/BoardWindow.xaml.cs
git grep -n "BOOK NOT LOADED" -- src/UI/BoardWindow.xaml.cs
git grep -n "BoardEdits.AnchorSourceId(board, _services.Sources)" -- src/UI/BoardWindow.xaml.cs
git grep -n "private void LoadInstalled()" -- src/Composition/AppServices.cs
git grep -n "if (!_closingApp) ReturnPanel(window.PanelId);" -- src/UI/BoardWindow.PopOuts.cs
git grep -n "claims.TryClaim(readRecipe.Slug, kv.Key, readSource.Id, window)" -- src/Core/RecipeWatch.cs
git grep -n "StateText(snapshot.State);" -- src/UI/Setup/DiagnosticsModel.cs
git grep -n "Could not ask RoRoRo for your accounts" -- src/UI/Setup/AccountsPage.xaml.cs
git grep -n "Could not open the folder" -- src/UI/Setup/ScoreBookPage.xaml.cs
git grep -n "services.ReloadRecipes();" -- src/UI/Setup/ImportFlow.cs
git grep -n "recipe.IsGroupList ? \[\] : \[.. recipe.Headline.Select(h => h.Label)\]" -- src/UI/ImportText.cs
git grep -n "MessageBox.Show" -- src
git grep -n "Being rebuilt around recipes" -- README.md
```

- [ ] **Step 5: Waves 2 and 3 and the won't-fix items still describe the code**

Run: `git diff --stat 20ab152..master -- src tests tools`

For each file it lists, find the triage rows whose "where" in `docs/backlog.md` names that file, and read the item against the file on `master`. An item the v0.3.0 review fixed becomes **FIXED** in `docs/backlog.md` (naming the commit) and its triage row here reads "Fixed in v0.3.0 (commit)". An item whose line moved keeps its triage and gets its new line in the backlog.

- [ ] **Step 6: Fold in what the release smoke found**

In `docs/backlog.md`, before `# Stage 1 (v0.2.0)`, add a section headed `# v0.3.0 release smoke` with one line per finding, numbered `R3-1`, `R3-2`, … and written in the backlog's own line format: the ID, **OPEN**, "you'd notice:" (or "code:") and what the owner saw in the owner's words, the file and line once you have found them in the code, and "release smoke" with the date the owner walked it. With no findings, the section says "Nothing found." and nothing else changes.

Triage each one into this plan's table. A finding you'd notice that is small enough for a patch gets its own wave 1 task, written in this plan's format (files, interfaces, failing test, code, commands, commit) and inserted before Task 13, and it is reviewed like any other task before it runs. Anything else goes to wave 2 or 3 with a one-line approach under that wave's outline. Update the backlog's Counts section.

- [ ] **Step 7: Commit**

```bash
git add docs/backlog.md docs/plans/2026-09-15-backlog-remediation.md
git commit -m "plan: backlog remediation re-checked against v0.3.0, release smoke folded in

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 1: Charts, rows, the drag handle, and a change that fell

S1-13.11, S1-14.11, S2-P.18 with S2-P.3, S1-13.14. Rulings P10, P11, P12, P13.

**Files:**
- Modify: `src/Board/ChartGeometry.cs` (`Layout`)
- Modify: `src/Board/BoardLayout.cs` (add `CellHeight`)
- Modify: `src/UI/Controls/PanelGrid.cs` (`ArrangeOverride`)
- Modify: `src/UI/Panels/PanelFrame.xaml` (the drag handle)
- Modify: `src/Book/Records.cs` (`Change`, add `Fell`)
- Modify: `src/Board/PanelModels.cs` (`StandingModel`, `Standing`)
- Modify: `src/UI/Panels/StandingPanel.xaml` (the change's colour)
- Test: `tests/ChartGeometryTests.cs`, `tests/BoardLayoutTests.cs`, `tests/PanelModelsTests.cs`
- Walks: `tools/smoke/walk-starter-board.ps1`, `tools/smoke/walk-board-editing.ps1`

**Interfaces:**
- Consumes: `ChartGeometry.Layout`, `BoardLayout.Flow`, `BoardLayout.RowHeights`, `PanelPlacement`, `Records.Change`, `ScoreBookReader.HeadlineSeries`, `PanelText.Short` as they are on `master`.
- Produces: `BoardLayout.CellHeight(PanelPlacement, IReadOnlyList<double>, double)`, `Records.Fell(IReadOnlyList<SeriesPoint>)`, `StandingModel.ChangeFell` (init property).

- [ ] **Step 1: Write the failing chart tests**

Add to `tests/ChartGeometryTests.cs`, inside the class:

```csharp
    [Fact]
    public void EqualValuesRunThroughTheMiddleWithTheirOneValueLabelled()
    {
        ChartSeries[] series = [new("A", [new(T0, 7_500), new(T0.AddHours(1), 7_500)], 0)];

        var layout = ChartGeometry.Layout(series, 240, 112, fromZero: false, labels: true);

        // The plot is 100 tall from y = 6, so its middle is 56. No axis is made up around the one value.
        Assert.Equal(new[] { (40.0, 56.0), (236.0, 56.0) }, layout.Lines[0].Points.ToArray());
        var grid = Assert.Single(layout.Grid);
        Assert.Equal((56.0, PanelText.Short(7_500)), (grid.Y, grid.Label));
    }

    [Fact]
    public void AChartFromZeroOfOnlyZerosIsFlatToo()
    {
        var layout = ChartGeometry.Layout([new ChartSeries("A", [new(T0, 0), new(T0.AddHours(1), 0)], 0)], 240, 112, fromZero: true, labels: true);

        Assert.Equal(56.0, layout.Lines[0].Points[0].Y);
        Assert.Equal(PanelText.Short(0), Assert.Single(layout.Grid).Label);
    }

    [Fact]
    public void NegativeValuesPlotBelowZeroAndFromZeroKeepsZeroOnTheAxis()
    {
        ChartSeries[] series = [new("A", [new(T0, -20), new(T0.AddHours(1), -10)], 0)];

        var free = ChartGeometry.Layout(series, 240, 112, fromZero: false, labels: true);
        var fromZero = ChartGeometry.Layout(series, 240, 112, fromZero: true, labels: true);

        Assert.Equal(new[] { 106.0, 6.0 }, free.Lines[0].Points.Select(p => p.Y).ToArray());
        Assert.Equal(PanelText.Short(-20), free.Grid[0].Label);
        // From zero, the top of the axis is 0: -10 sits half way down and -20 at the bottom.
        Assert.Equal(new[] { 106.0, 56.0 }, fromZero.Lines[0].Points.Select(p => p.Y).ToArray());
        Assert.Equal(PanelText.Short(0), fromZero.Grid[^1].Label);
    }
```

- [ ] **Step 2: Write the failing layout test**

Add to `tests/BoardLayoutTests.cs`, inside the class:

```csharp
    [Fact]
    public void EveryPanelIsAsTallAsTheRowsItSpans()
    {
        var side = BoardLayout.Flow([new PanelSize(6), new PanelSize(6)], 1280);
        var sideRows = BoardLayout.RowHeights(side, [300, 120], 12);

        var tall = BoardLayout.Flow([new PanelSize(6, Tall: true), new PanelSize(6), new PanelSize(6)], 1280);
        var tallRows = BoardLayout.RowHeights(tall, [500, 200, 150], 12);

        // Two half panels share one 300 row, so the 120 one reaches its bottom too.
        Assert.Equal(new[] { 300.0, 300.0 }, side.Select(p => BoardLayout.CellHeight(p, sideRows, 12)).ToArray());
        // Rows are 200 and 288: the tall panel takes both and the gap, the 150 panel stretches to its 288 row.
        Assert.Equal(new[] { 500.0, 200.0, 288.0 }, tall.Select(p => BoardLayout.CellHeight(p, tallRows, 12)).ToArray());
    }
```

- [ ] **Step 3: Write the failing Standing test**

Add to `tests/PanelModelsTests.cs`, under `// ---- Standing ----`:

```csharp
    [Fact]
    public void AStandingThatFellSaysSoSoItsChangeIsColouredAsAFall()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var live = Live([main], [Installed(Clan, "value")], Snaps(Snapshot(main.Id, [], [Place(14), Points(29_000_000)], LivePeriod)));
        var falling = Reader(
            Read(main, Now.AddHours(-1), Period, Headline(30_000_000), "value"),
            Read(main, Now.AddMinutes(-3), Period, Headline(29_000_000), "value"));
        var rising = Reader(
            Read(main, Now.AddHours(-1), Period, Headline(28_000_000), "value"),
            Read(main, Now.AddMinutes(-3), Period, Headline(29_000_000), "value"));

        var down = PanelModels.Standing(live, falling, new PanelSettings(Clan.Slug, SourceId: main.Id));
        var up = PanelModels.Standing(live, rising, new PanelSettings(Clan.Slug, SourceId: main.Id));

        Assert.StartsWith("-", down.Change);
        Assert.True(down.ChangeFell);
        Assert.StartsWith("+", up.Change);
        Assert.False(up.ChangeFell);
        Assert.False(Records.Fell([]));
    }
```

- [ ] **Step 4: Run them to see them fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: the build fails with `CS0117: 'BoardLayout' does not contain a definition for 'CellHeight'`, `CS1061: 'StandingModel' does not contain a definition for 'ChangeFell'` and `CS0117: 'Records' does not contain a definition for 'Fell'`.

Temporarily comment out those two tests (the layout and Standing ones), then run:

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ChartGeometryTests"
```

Expected: `EqualValuesRunThroughTheMiddleWithTheirOneValueLabelled`, `AChartFromZeroOfOnlyZerosIsFlatToo` and `NegativeValuesPlotBelowZeroAndFromZeroKeepsZeroOnTheAxis` FAIL (the flat line sits at y = 106 on a four-line axis). Uncomment the two tests.

- [ ] **Step 5: Implement the chart rule**

In `src/Board/ChartGeometry.cs`, replace the body of `Layout` from `var minT = points.Min(p => p.T);` through the `double Y(double value) => …;` line with:

```csharp
        var minT = points.Min(p => p.T);
        var maxT = points.Max(p => p.T);
        var minV = points.Min(p => p.Value);
        var maxV = points.Max(p => p.Value);
        if (fromZero)
        {
            // Zero stays on the axis at whichever end it belongs, so negative values plot below a zero line.
            minV = Math.Min(0, minV);
            maxV = Math.Max(0, maxV);
        }

        var spanSeconds = (maxT - minT).TotalSeconds;

        double X(DateTimeOffset t) => left + (spanSeconds <= 0 ? plotWidth : (t - minT).TotalSeconds / spanSeconds * plotWidth);

        // Every value the same: there is no range to draw, so the line runs through the middle and the one grid line
        // names that value, instead of an axis made up around it (P10).
        if (maxV <= minV)
        {
            var middle = Top + plotHeight / 2;
            var flat = series
                .Where(s => s.Points.Count > 0)
                .Select(s => new ChartLine(s.Colour, [.. s.Points.OrderBy(p => p.T).Select(p => (X(p.T), middle))]))
                .ToList();
            return new ChartLayout(flat, [new ChartGridLine(middle, labels ? PanelText.Short(minV) : "")]);
        }

        double Y(double value) => Top + (1 - (value - minV) / (maxV - minV)) * plotHeight;
```

The rest of the method (`var lines = …`, `var grid = …`, `return new ChartLayout(lines, grid);`) stays as it is.

- [ ] **Step 6: Implement the row height**

In `src/Board/BoardLayout.cs`, add after `RowHeights`:

```csharp
    /// <summary>How tall a panel's cell is: the rows it spans and the gaps between them, so every panel reaches its row's bottom (P11).</summary>
    public static double CellHeight(PanelPlacement placement, IReadOnlyList<double> rows, double gap) =>
        Enumerable.Range(placement.Row, placement.Rows).Sum(r => rows[r]) + gap * (placement.Rows - 1);
```

In `src/UI/Controls/PanelGrid.cs`, in `ArrangeOverride`, replace:

```csharp
            var height = placement.Rows == 1
                ? child.DesiredSize.Height
                : Enumerable.Range(placement.Row, placement.Rows).Sum(r => rows[r]) + Gap * (placement.Rows - 1);
```

with:

```csharp
            var height = BoardLayout.CellHeight(placement, rows, Gap);
```

- [ ] **Step 7: Implement the change that fell**

In `src/Book/Records.cs`, replace the whole `Change` method with:

```csharp
    /// <summary>
    /// "+220K in 1h": the last value against the latest reading at least an hour before it, or the first
    /// reading when all are within the hour. <paramref name="now"/> is kept for callers that show "ago" beside it.
    /// </summary>
    public static string Change(IReadOnlyList<SeriesPoint> series, DateTimeOffset now)
    {
        if (Baseline(series) is not { } earlier) return "no earlier read";

        var last = series[^1];
        var delta = last.Value - earlier.Value;
        return $"{(delta < 0 ? "-" : "+")}{StatText.Abbrev(Math.Abs(delta))} in {StatText.Span(last.T - earlier.T)}";
    }

    /// <summary>Whether <see cref="Change"/> shows a fall, so a panel can colour it as one (P12).</summary>
    public static bool Fell(IReadOnlyList<SeriesPoint> series) => Baseline(series) is { } earlier && series[^1].Value < earlier.Value;

    /// <summary>What a change is measured from: the latest reading at least an hour before the last, else the first; none with fewer than two.</summary>
    private static SeriesPoint? Baseline(IReadOnlyList<SeriesPoint> series)
    {
        if (series.Count < 2) return null;

        var target = series[^1].T - TimeSpan.FromHours(1);
        for (var i = series.Count - 2; i >= 0; i--)
        {
            if (series[i].T <= target) return series[i];
        }

        return series[0];
    }
```

In `src/Board/PanelModels.cs`, replace the `StandingModel` declaration with:

```csharp
public sealed record StandingModel(
    PanelHead Head, string Place, string PlaceSuffix, string TotalLabel, string Total, string Change,
    bool HasGap, string GapLabel, string Gap, double GapFill, bool HasAccounts, string Accounts, string PeriodLine)
{
    /// <summary>The change is a fall, which the panel shows in the theme's warning colour (P12).</summary>
    public bool ChangeFell { get; init; }
}
```

In `PanelModels.Standing`, replace:

```csharp
        var change = totalId is null || !periodKnown
            ? Dash
            : Records.Change(reader.HeadlineSeries(source.Id, totalId, snapshot?.Period?.Value), live.Now);
```

with:

```csharp
        IReadOnlyList<SeriesPoint> totals = totalId is null || !periodKnown ? [] : reader.HeadlineSeries(source.Id, totalId, snapshot?.Period?.Value);
        var change = totalId is null || !periodKnown ? Dash : Records.Change(totals, live.Now);
```

and replace the final `return new StandingModel(` … `PanelText.PeriodLine(snapshot?.Period, live.Now, null));` statement with the same constructor call followed by an initializer:

```csharp
        return new StandingModel(
            new PanelHead(title, name, PanelText.Chip(source.Role), live.IsOverdue(source)),
            place is { } p ? PanelText.Ordinal((int)p) : Dash,
            recipe.Period is null || place is null ? "" : $"in the {RecipeWords.Period(recipe)}",
            recipe.Headline.FirstOrDefault(h => h.Id == totalId)?.Label ?? "Total",
            PanelText.Full(total),
            change,
            gap.Has, gap.Label, gap.Text, gap.Fill,
            hasAccounts,
            hasAccounts ? $"{mine} of {rows!.Count}" : "",
            PanelText.PeriodLine(snapshot?.Period, live.Now, null))
        {
            ChangeFell = Records.Fell(totals),
        };
```

- [ ] **Step 8: Run the tests to see them pass**

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ChartGeometryTests|FullyQualifiedName~BoardLayoutTests|FullyQualifiedName~PanelModelsTests|FullyQualifiedName~ScoreBookReaderTests"
```

Expected: all PASS, the four existing chart tests included.

- [ ] **Step 9: Colour the Standing change**

In `src/UI/Panels/StandingPanel.xaml`, replace:

```xml
                    <TextBlock DockPanel.Dock="Right" Text="{Binding Change}" Foreground="{DynamicResource CyanBrush}" />
```

with:

```xml
                    <TextBlock DockPanel.Dock="Right" Text="{Binding Change}">
                        <TextBlock.Style>
                            <Style TargetType="TextBlock">
                                <Setter Property="Foreground" Value="{DynamicResource CyanBrush}" />
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding ChangeFell}" Value="True">
                                        <Setter Property="Foreground" Value="{DynamicResource MagentaBrush}" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </TextBlock.Style>
                    </TextBlock>
```

- [ ] **Step 10: Draw the drag handle**

In `src/UI/Panels/PanelFrame.xaml`, replace the whole `<Border x:Name="DragHandle" …>` element (its three lines, through `</Border>`) with:

```xml
            <!-- Six dots in the theme's muted brush, so no font has to carry a grip glyph (P13). A Label, unlike a Border, has an
                 automation peer, so UI Automation finds DragHandle by its id. It isn't focusable: Move earlier and Move later are
                 the keyboard's way (R7). -->
            <Label x:Name="DragHandle" Background="Transparent" Cursor="SizeAll" Padding="6,3" VerticalAlignment="Center"
                   MouseLeftButtonDown="OnDragHandleDown" ToolTip="Drag to move" AutomationProperties.Name="Drag to move">
                <Grid Width="8" Height="12">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition />
                        <ColumnDefinition />
                    </Grid.ColumnDefinitions>
                    <Grid.RowDefinitions>
                        <RowDefinition />
                        <RowDefinition />
                        <RowDefinition />
                    </Grid.RowDefinitions>
                    <Ellipse Grid.Row="0" Grid.Column="0" Width="3" Height="3" Fill="{DynamicResource MutedTextBrush}" />
                    <Ellipse Grid.Row="0" Grid.Column="1" Width="3" Height="3" Fill="{DynamicResource MutedTextBrush}" />
                    <Ellipse Grid.Row="1" Grid.Column="0" Width="3" Height="3" Fill="{DynamicResource MutedTextBrush}" />
                    <Ellipse Grid.Row="1" Grid.Column="1" Width="3" Height="3" Fill="{DynamicResource MutedTextBrush}" />
                    <Ellipse Grid.Row="2" Grid.Column="0" Width="3" Height="3" Fill="{DynamicResource MutedTextBrush}" />
                    <Ellipse Grid.Row="2" Grid.Column="1" Width="3" Height="3" Fill="{DynamicResource MutedTextBrush}" />
                </Grid>
            </Label>
```

`OnDragHandleDown` in `PanelFrame.xaml.cs` takes `MouseButtonEventArgs` and needs no change.

- [ ] **Step 11: Add the walk steps**

In `tools/smoke/walk-starter-board.ps1`, after the line that records `'1c The promotion check names both clans'`, add:

```powershell
    # Panels in one row reach the row's bottom: the first row holds both Clan standings and the taller Battle race.
    $heights = @('StandingPanel1', 'StandingPanel2', 'RacePanel1') | ForEach-Object { (Find-ByAutomationId $board $_).Current.BoundingRectangle.Height }
    $spread = ($heights | Measure-Object -Maximum).Maximum - ($heights | Measure-Object -Minimum).Minimum
    Check '1d Panels in a row share its height' ($spread -lt 2) "heights: $($heights -join ', ')"
```

In `tools/smoke/walk-board-editing.ps1`, in step 2, after the line that records `'2 Edit mode shows the panel tools'`, add:

```powershell
    Check '2a The drag handle is visible to UI Automation' ([bool](Find-ByAutomationId (Find-ByAutomationId $board 'RacePanel1') 'DragHandle')) 'DragHandle'
```

Run the parse check from the smoke README:

```powershell
Get-ChildItem tools/smoke -Filter *.ps1 | ForEach-Object {
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$null, [ref]$errors) | Out-Null
    "{0}: {1} parse error(s)" -f $_.Name, @($errors).Count
}
```

Expected: every file reports `0 parse error(s)`.

- [ ] **Step 12: Run the full gate**

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build
```

Expected: both pass (`ThemeFenceTests` included: the new brushes are `DynamicResource`).

Walks for the controller: `walk-starter-board.ps1` (new step 1d, and its screenshot shows the dots' row and a Standing change), `walk-board-editing.ps1` (new step 2a, steps 3 to 5 still drag-free).

- [ ] **Step 13: Commit**

```bash
git add src/Board/ChartGeometry.cs src/Board/BoardLayout.cs src/UI/Controls/PanelGrid.cs src/UI/Panels/PanelFrame.xaml src/Book/Records.cs src/Board/PanelModels.cs src/UI/Panels/StandingPanel.xaml tests/ChartGeometryTests.cs tests/BoardLayoutTests.cs tests/PanelModelsTests.cs tools/smoke/walk-starter-board.ps1 tools/smoke/walk-board-editing.ps1
git commit -m "board: flat charts on a real axis, rows that reach their bottom, a drawn drag handle, a fall in magenta

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: Ranks, tied gaps and the sent dot

S1-6.9, S1-13.5, S1-F.5. Rulings P7, P8, P9.

**Files:**
- Modify: `src/Board/PanelModels.cs` (`MyAccounts`, `AccountCard`, `Gap`)
- Modify: `src/Core/RecipeWatch.cs` (`RecipeSnapshot.SentThisRead`, the send loop)
- Test: `tests/PanelModelsTests.cs`, `tests/RecipeWatchBookTests.cs`

**Interfaces:**
- Consumes: `Ranking.Competition`, `BoardFixtures.Snapshot(..., sent:)`, the `Watch` helper in `RecipeWatchBookTests`.
- Produces: `RecipeSnapshot.SentThisRead` (`IReadOnlySet<(Guid AccountId, string Stat)>`, never null). Task 3's `SnapshotCarry` relies on it not being carried.

- [ ] **Step 1: Write the failing panel tests**

Add to `tests/PanelModelsTests.cs`, under `// ---- Standing ----`:

```csharp
    [Fact]
    public void WithTiedRanksTheGapIsToTheNearestGroupRankedHigher()
    {
        var top = SourceOf("s-0000000a", TopList, null, SourceRole.Watch);
        GroupRow Group(string name, double points, int rank) => new(name, new Dictionary<string, double> { ["value"] = points }, rank);
        GroupRow[] groups = [Group("Eleventh", 50_000_000, 11), Group("TiedA", 40_000_000, 12), Group("TiedB", 40_000_000, 12), Group("CCGP", 30_200_000, 14)];

        StandingModel For(string clan)
        {
            var main = SourceOf("s-00000001", Clan, clan, SourceRole.Main);
            var live = Live([main, top], [Installed(Clan, "value"), Installed(TopList)],
                Snaps(Snapshot(main.Id, [], [Place(14), Points(30_200_000)]), Snapshot(top.Id, null, groups: groups)));
            return PanelModels.Standing(live, Reader(), new PanelSettings(Clan.Slug, SourceId: main.Id));
        }

        var behindTheTie = For("CCGP");
        var inTheTie = For("TiedB");

        Assert.True(behindTheTie.HasGap);
        Assert.Equal(("To 12th", $"{StatText.Abbrev(9_800_000)} behind"), (behindTheTie.GapLabel, behindTheTie.Gap));
        Assert.True(inTheTie.HasGap);
        Assert.Equal(("To 11th", $"{StatText.Abbrev(10_000_000)} behind"), (inTheTie.GapLabel, inTheTie.Gap));
    }
```

and under `// ---- My accounts ----`:

```csharp
    [Fact]
    public void RankInAGroupCountsOnlyTheRowsWithThatStatAsPromotionCheckDoes()
    {
        var alts = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
        // Four rows, one with no points: your account is 1st of the 3 that have a value.
        IReadOnlyList<RecipeRow> rows = [Row(AltOne.RobloxUserId, 12_418_220), Row(7, 50), Row(8, null), Row(9, 3_000)];
        var live = Live([alts], [Installed(Clan, "value")], Snaps(Snapshot(alts.Id, rows, period: LivePeriod)));

        var accounts = PanelModels.MyAccounts(live, Reader(), new PanelSettings(Clan.Slug, Stat: "value"));
        var card = PanelModels.AccountCard(live, Reader(), new PanelSettings(Clan.Slug, Stat: "value", UserId: AltOne.RobloxUserId));

        Assert.Equal("#1 of 3", accounts.Groups[0].Rows.Single(r => r.UserId == AltOne.RobloxUserId).InGroup);
        Assert.Equal(new FactModel("In clan", "#1 of 3"), card.Facts[0]);
    }

    [Fact]
    public void TheSentDotMeansSentInTheLastReadNotEarlierThisSession()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        // RoRoRo got this account's points on an earlier read (its line remembers them), but not on the last one.
        var earlier = new AccountLine(Main.DisplayName, Main.AccountId, new Dictionary<string, double> { ["value"] = 14_000_000 }, Now.AddMinutes(-3));
        var notThisRead = Snapshot(main.Id, [Row(Main.RobloxUserId, 14_020_550)], period: LivePeriod, sent: [earlier]);
        var thisRead = notThisRead with { SentThisRead = new HashSet<(Guid AccountId, string Stat)> { (Main.AccountId, "value") } };

        MyAccountsModel For(RecipeSnapshot snapshot) =>
            PanelModels.MyAccounts(Live([main], [Installed(Clan, "value")], Snaps(snapshot)), Reader(), new PanelSettings(Clan.Slug, Stat: "value"));

        Assert.False(For(notThisRead).Groups[0].Rows.Single(r => r.UserId == Main.RobloxUserId).Sent);
        Assert.True(For(thisRead).Groups[0].Rows.Single(r => r.UserId == Main.RobloxUserId).Sent);
        Assert.Equal("● sent to RoRoRo in the last read", For(thisRead).Head.Note);
    }
```

In the existing `MyAccountsGroupsMainFirstThenMineThenAccountsInNoWatchedClan`, replace `Snapshot(main.Id, ccgpRows, period: LivePeriod, sent: [sentLine])` with:

```csharp
Snapshot(main.Id, ccgpRows, period: LivePeriod, sent: [sentLine]) with { SentThisRead = new HashSet<(Guid AccountId, string Stat)> { (Main.AccountId, "value") } }
```

- [ ] **Step 2: Write the failing watch test**

Add to `tests/RecipeWatchBookTests.cs`, inside the class:

```csharp
    [Fact]
    public async Task AReadNamesWhatItSentThisTimeOnly()
    {
        var engine = new StubEngine(() => Reading(EngineRow(111, 4200)));
        var watch = Watch(engine, new StubHost(true, AltAccount), new MemoryBook(), SourceOf(SourceRole.Mine));

        var sent = await watch.RunOnceAsync(CancellationToken.None);
        engine.Read = () => RecipeReading.Stop(ReadingOutcome.Idle, "No clan battle running");
        var stopped = await watch.RunOnceAsync(CancellationToken.None);

        Assert.Equal(new[] { (Alt, "value") }, sent.SentThisRead.ToArray());
        Assert.Empty(stopped.SentThisRead);
        // The remembered lines still hold the earlier send, which is what the dot used to read.
        Assert.NotEmpty(stopped.Accounts);
    }
```

- [ ] **Step 3: Run them to see them fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: the build fails with `CS0117: 'RecipeSnapshot' does not contain a definition for 'SentThisRead'` and `CS1061` for `sent.SentThisRead`.

Temporarily comment out `TheSentDotMeansSentInTheLastReadNotEarlierThisSession`, `AReadNamesWhatItSentThisTimeOnly` and the `with { SentThisRead = … }` you added to the existing test, then run:

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~PanelModelsTests"
```

Expected: `WithTiedRanksTheGapIsToTheNearestGroupRankedHigher` FAILS (`HasGap` is false) and `RankInAGroupCountsOnlyTheRowsWithThatStatAsPromotionCheckDoes` FAILS (`#1 of 4`). Uncomment what you commented.

- [ ] **Step 4: Implement SentThisRead**

In `src/Core/RecipeWatch.cs`, inside `RecipeSnapshot`, add after `NotRecordingReason`:

```csharp
    /// <summary>What this read sent to RoRoRo: one of your accounts and a stat key per value that went. Empty for a read that sent nothing (P9).</summary>
    public IReadOnlySet<(Guid AccountId, string Stat)> SentThisRead { get; init; } = new HashSet<(Guid AccountId, string Stat)>();
```

In `RunOnceCoreAsync`, replace:

```csharp
        var (recorded, notRecording) = Record(readRecipe, readInputs, readText, readTracked, readSource, trigger, reading, owned);
        RecipeSnapshot Kept(RecipeSnapshot snapshot) => snapshot with { Recorded = recorded, NotRecordingReason = notRecording };
```

with:

```csharp
        var (recorded, notRecording) = Record(readRecipe, readInputs, readText, readTracked, readSource, trigger, reading, owned);

        // What this read sent, so the board's sent dot means this read and not an earlier one (P9).
        var sentNow = new HashSet<(Guid AccountId, string Stat)>();
        RecipeSnapshot Kept(RecipeSnapshot snapshot) => snapshot with { Recorded = recorded, NotRecordingReason = notRecording, SentThisRead = sentNow };
```

and in the send loop replace:

```csharp
                    if (sent) Remember(readRecipe, readInputs, subject, accounts, stat.Key, value, observedAt);
```

with:

```csharp
                    if (!sent) continue;

                    sentNow.Add((subject, stat.Key));
                    Remember(readRecipe, readInputs, subject, accounts, stat.Key, value, observedAt);
```

- [ ] **Step 5: Implement the panel rules**

In `src/Board/PanelModels.cs`, in `MyAccounts`, replace:

```csharp
                var sent = snapshot.Accounts.Any(l => l.AccountId == account.AccountId && l.LastValues.ContainsKey(stat.Key));
```

with:

```csharp
                var sent = snapshot.SentThisRead.Contains((account.AccountId, stat.Key));
```

and in the same method replace `$"#{rank} of {rows.Count}"` with `$"#{rank} of {ranks.Count}"`, and `Note: "● sent to RoRoRo"` with `Note: "● sent to RoRoRo in the last read"`.

In `AccountCard`, replace `$"#{rank} of {listRows.Count}"` with `$"#{rank} of {ranks.Count}"`.

Replace the whole `Gap` method with:

```csharp
    /// <summary>
    /// The gap to the group just above, only when a group list holds both (spec §9.4). With competition ranks (12, 12, 14) the
    /// group above is the nearest one ranked higher, and the list holds everything between when that group's rank plus how many
    /// share it is this group's rank: 14th measures to 12th, each 12th to 11th, and 12, 14 with no 13th shows no gap (P8).
    /// </summary>
    private static (bool Has, string Label, string Text, double Fill) Gap(LiveBoard live, string name)
    {
        foreach (var source in live.Sources.Where(s => s.Enabled))
        {
            if (live.FindRecipe(source.Recipe)?.Recipe is not { IsGroupList: true } recipe) continue;
            if (live.SnapshotOf(source.Id)?.Groups is not { Count: > 0 } groups) continue;

            var ordered = OrderGroups(groups, recipe.LastStep.Values[0].Id);
            var index = ordered.FindIndex(g => string.Equals(g.Row.Name, name, StringComparison.OrdinalIgnoreCase));
            if (index <= 0) continue;

            var here = ordered[index];
            var aboveIndex = ordered.FindLastIndex(index - 1, g => g.Rank < here.Rank);
            if (aboveIndex < 0) continue;

            var above = ordered[aboveIndex];
            if (above.Rank + ordered.Count(g => g.Rank == above.Rank) != here.Rank) continue;
            if (above.Value is not { } a || here.Value is not { } h) continue;

            return (true, $"To {PanelText.Ordinal(above.Rank)}", $"{StatText.Abbrev(Math.Max(0, a - h))} behind", a <= 0 ? 0 : Math.Clamp(h / a, 0, 1));
        }

        return (false, "", "", 0);
    }
```

- [ ] **Step 6: Run the tests to see them pass**

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~PanelModelsTests|FullyQualifiedName~RecipeWatchBookTests|FullyQualifiedName~RecipeWatchTests"
```

Expected: all PASS, `TheGapShowsOnlyWhenTheGroupListHasThisAndTheOneJustAbove` included (13 then 14 still has a gap; 12 then 14 still has none).

- [ ] **Step 7: Run the full gate**

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build
```

Expected: both pass. Walks for the controller: `walk-starter-board.ps1` step 3 (My accounts), and during a live read the dots show only after a read that sent.

- [ ] **Step 8: Commit**

```bash
git add src/Board/PanelModels.cs src/Core/RecipeWatch.cs tests/PanelModelsTests.cs tests/RecipeWatchBookTests.cs
git commit -m "panels: ranks count rows with a value, tied gaps, and a sent dot that means the last read

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: A read that stops keeps the board

S1-F.6. Ruling P14.

**Files:**
- Create: `src/Core/SnapshotCarry.cs`
- Modify: `src/Core/RecipeWatch.cs` (`RecipeSnapshot.CarriedFrom`)
- Modify: `src/Board/PanelModels.cs` (`LiveBoard.IsOverdue`)
- Modify: `src/Composition/AppServices.cs` (`Record`)
- Test: `tests/SnapshotCarryTests.cs` (new), `tests/PanelModelsTests.cs`

**Interfaces:**
- Consumes: `RecipeSnapshot.SentThisRead` (Task 2), `Records.Overdue`.
- Produces: `RecipeSnapshot.CarriedFrom` (`DateTimeOffset?`), `SnapshotCarry.Carry(RecipeSnapshot? previous, DateTimeOffset? previousAt, RecipeSnapshot next)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/SnapshotCarryTests.cs`:

```csharp
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class SnapshotCarryTests
{
    private static readonly DateTimeOffset Then = new(2026, 9, 19, 17, 57, 0, TimeSpan.Zero);

    private static RecipeSnapshot Good() =>
        new(WatchState.Reporting, "Reporting 1 of 2 row(s).", [], [], 2, "battle=B",
            [new RecipeRow(111, new Dictionary<string, double> { ["value"] = 4200 })],
            [new HeadlineValue("Clan place", "14") { Id = "clan-place", Number = 14 }])
        {
            RecipeSlug = "clan",
            SourceId = "s-00000001",
            Period = new ReadingPeriod("B", null, null),
        };

    private static RecipeSnapshot Stopped(WatchState state = WatchState.SourceUnreachable, string slug = "clan") =>
        new(state, "timed out", [], [], 0) { RecipeSlug = slug, SourceId = "s-00000001" };

    [Fact]
    public void AStoppedReadKeepsTheLastGoodReadsNumbersAndSaysWhenTheyWereRead()
    {
        var good = Good();

        var carried = SnapshotCarry.Carry(good, Then, Stopped());

        Assert.Equal(WatchState.SourceUnreachable, carried.State);
        Assert.Equal("timed out", carried.Detail);
        Assert.Same(good.Rows, carried.Rows);
        Assert.Same(good.Headline, carried.Headline);
        Assert.Equal(good.Period, carried.Period);
        Assert.Equal(Then, carried.CarriedFrom);
    }

    [Fact]
    public void ASecondStoppedReadKeepsTheTimeOfTheGoodRead()
    {
        var once = SnapshotCarry.Carry(Good(), Then, Stopped());

        var twice = SnapshotCarry.Carry(once, Then.AddMinutes(3), Stopped(WatchState.SourceIdle));

        Assert.Equal(Then, twice.CarriedFrom);
        Assert.NotNull(twice.Rows);
    }

    [Fact]
    public void AGoodReadCarriesNothingAndAReadThatThrewStillKeepsTheBoard()
    {
        var fresh = Good() with { Rows = [] };
        Assert.Same(fresh, SnapshotCarry.Carry(Good(), Then, fresh));

        // SourceHost's snapshot for a read that threw names its source but no recipe.
        var threw = new RecipeSnapshot(WatchState.SourceUnreachable, "The last read failed (HttpRequestException).", [], [], 0) { SourceId = "s-00000001" };
        Assert.NotNull(SnapshotCarry.Carry(Good(), Then, threw).Rows);
    }

    [Fact]
    public void NothingOfAnotherRecipeOrOfARecipeChangeIsCarried()
    {
        Assert.Null(SnapshotCarry.Carry(Good(), Then, Stopped(slug: "followers")).Rows);
        // The "recipe changed" snapshot is Showing with no rows, and must show nothing of the old recipe.
        Assert.Null(SnapshotCarry.Carry(Good(), Then, Stopped(WatchState.Showing)).Rows);
        Assert.Null(SnapshotCarry.Carry(null, null, Stopped()).Rows);
    }

    [Fact]
    public void WhatWasSentKeptOrMissedIsNeverCarried()
    {
        var good = Good() with
        {
            Recorded = true,
            SentThisRead = new HashSet<(Guid AccountId, string Stat)> { (Guid.NewGuid(), "value") },
            CellMisses = new Dictionary<(long UserId, string Stat), string> { [(111, "value")] = "no 'Points' here" },
        };

        var carried = SnapshotCarry.Carry(good, Then, Stopped());

        Assert.False(carried.Recorded);
        Assert.Empty(carried.SentThisRead);
        Assert.Empty(carried.CellMisses);
    }
}
```

Add to `tests/PanelModelsTests.cs`, after `OverdueFollowsRecordsOnlyWhileRunning`:

```csharp
    [Fact]
    public void NumbersKeptFromAnEarlierReadAreOverdueByThatReadsAge()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var carried = Snapshot(main.Id, [], [Place(14), Points(1)]) with { State = WatchState.SourceUnreachable, CarriedFrom = Now.AddMinutes(-10) };
        var lastRead = new Dictionary<string, DateTimeOffset> { [main.Id] = Now.AddSeconds(-5) };

        var model = PanelModels.Standing(Live([main], [Installed(Clan, "value")], Snaps(carried), running: true, lastRead), Reader(),
            new PanelSettings(Clan.Slug, SourceId: main.Id));

        // Read 5 s ago, but what the panel shows is 10 minutes old: past 1.5 times the recipe's 180 s.
        Assert.True(model.Head.Overdue);
        Assert.Equal("14th", model.Place);
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: the build fails with `CS0103: The name 'SnapshotCarry' does not exist in the current context` and `CS0117: 'RecipeSnapshot' does not contain a definition for 'CarriedFrom'`.

- [ ] **Step 3: Implement**

In `src/Core/RecipeWatch.cs`, inside `RecipeSnapshot`, add after `SentThisRead`:

```csharp
    /// <summary>
    /// When this snapshot's rows, headline, groups and period come from an earlier good read (<see cref="SnapshotCarry"/>): that
    /// read's time. Null for a read that has its own.
    /// </summary>
    public DateTimeOffset? CarriedFrom { get; init; }
```

Create `src/Core/SnapshotCarry.cs`:

```csharp
namespace Labs626.UrScore.Core;

/// <summary>
/// A read that stops (nothing running right now, a source that didn't answer) has no rows, headline or groups of its own. The
/// board keeps the last good read's instead of blanking every panel until the next good read, marked by
/// <see cref="RecipeSnapshot.CarriedFrom"/> so overdue follows their age (P14). Only what panels draw is carried: the state,
/// detail, misses, what was sent and what was kept are the stopped read's own, and the score book keeps its gap (spec §5.4).
/// </summary>
public static class SnapshotCarry
{
    public static RecipeSnapshot Carry(RecipeSnapshot? previous, DateTimeOffset? previousAt, RecipeSnapshot next)
    {
        if (previous is null || !Stopped(next) || !HasContent(previous)) return next;

        // A read of a different recipe: nothing of the old one's belongs on the new one's panels. A read that threw names no recipe.
        if (next.RecipeSlug.Length > 0 && !string.Equals(previous.RecipeSlug, next.RecipeSlug, StringComparison.Ordinal)) return next;

        return next with
        {
            Rows = previous.Rows,
            Headline = previous.Headline,
            Groups = previous.Groups,
            Period = previous.Period,
            CarriedFrom = previous.CarriedFrom ?? previousAt,
        };
    }

    /// <summary>Nothing to draw, and not the "recipe changed" snapshot (Showing with no rows), which must show nothing of the old recipe.</summary>
    private static bool Stopped(RecipeSnapshot snapshot) =>
        snapshot.Rows is null && snapshot.Headline is not { Count: > 0 } && snapshot.Groups.Count == 0 && snapshot.State != WatchState.Showing;

    private static bool HasContent(RecipeSnapshot snapshot) =>
        snapshot.Rows is not null || snapshot.Headline is { Count: > 0 } || snapshot.Groups.Count > 0;
}
```

In `src/Board/PanelModels.cs`, inside `LiveBoard`, replace `IsOverdue` with:

```csharp
    /// <summary>
    /// Spec §9.6: only while reading runs, and only once the source has been read. What counts is how old the numbers on screen
    /// are, so a stopped read that keeps an earlier read's (<see cref="RecipeSnapshot.CarriedFrom"/>) is as old as that read (P14).
    /// </summary>
    public bool IsOverdue(Source source) =>
        Running
        && (SnapshotOf(source.Id)?.CarriedFrom ?? LastReadOf(source.Id)) is { } shown
        && FindRecipe(source.Recipe) is { } installed
        && Records.Overdue(shown, installed.Recipe.EffectiveEverySeconds, Now);

    private DateTimeOffset? LastReadOf(string sourceId) => LastRead.TryGetValue(sourceId, out var at) ? at : null;
```

In `src/Composition/AppServices.cs`, in `Record`, replace:

```csharp
        _latest[sourceId] = snapshot;
        _lastRead[sourceId] = at;
```

with:

```csharp
        // A read that stopped keeps the last good read on the board (P14); the trail and misses below use the read itself.
        var before = _lastRead.TryGetValue(sourceId, out var lastAt) ? lastAt : (DateTimeOffset?)null;
        _latest[sourceId] = SnapshotCarry.Carry(_latest.GetValueOrDefault(sourceId), before, snapshot);
        _lastRead[sourceId] = at;
```

`ReadOnceAsync` still returns the read's own snapshot, so Setup › Clans' "Found … in …" line reads that read alone.

- [ ] **Step 4: Run the tests to see them pass**

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~SnapshotCarryTests|FullyQualifiedName~PanelModelsTests|FullyQualifiedName~BoardTextTests"
```

Expected: all PASS.

- [ ] **Step 5: Run the full gate**

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build
```

Expected: both pass. A stopped read can't be forced in a walk; the controller re-runs `walk-starter-board.ps1` and `walk-pop-outs.ps1` (unchanged behaviour on good reads), and during a live session watches a clan that sits a battle out: its panels keep their last numbers and show "overdue" once they are 4.5 minutes old.

- [ ] **Step 6: Commit**

```bash
git add src/Core/SnapshotCarry.cs src/Core/RecipeWatch.cs src/Board/PanelModels.cs src/Composition/AppServices.cs tests/SnapshotCarryTests.cs tests/PanelModelsTests.cs
git commit -m "board: a read that stops keeps the last good numbers on the board, overdue by their age

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: Where your accounts are

S1-13.4, S1-12.7. Ruling P15.

**Files:**
- Modify: `src/Board/PanelText.cs` (add `NotFound`, `OnlyWatched`)
- Modify: `src/Board/PanelModels.cs` (`MyAccounts`, add `Leftovers`)
- Modify: `src/UI/Setup/AccountsModel.cs` (`FoundIn`, add `Words`)
- Test: `tests/PanelTextTests.cs`, `tests/PanelModelsTests.cs`, `tests/AccountsModelTests.cs`
- Walk: `tools/smoke/walk-starter-board.ps1`

**Interfaces:**
- Consumes: `RecipeWords.Group`, `RecipeWords.GroupsLower`, `ClansModel.NameOf`, `SetupPages.HasClansPage`.
- Produces: `PanelText.NotFound(string group, string groups, int read, int sources)`, `PanelText.OnlyWatched(string groups)`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/PanelTextTests.cs`, inside the class:

```csharp
    [Fact]
    public void AccountsNoReadPlacedAreHeadedByHowMuchHasBeenRead()
    {
        Assert.Equal("Waiting for the first read", PanelText.NotFound("clan", "clans", read: 0, sources: 3));
        Assert.Equal("Not found in the clans read so far", PanelText.NotFound("clan", "clans", read: 1, sources: 3));
        Assert.Equal("Not in any clan you've added", PanelText.NotFound("clan", "clans", read: 3, sources: 3));
        Assert.Equal("Not in any clan you've added", PanelText.NotFound("clan", "clans", read: 0, sources: 0));
        Assert.Equal("Only in clans you're watching", PanelText.OnlyWatched("clans"));
    }
```

Add to `tests/PanelModelsTests.cs`, under `// ---- My accounts ----`:

```csharp
    [Fact]
    public void BeforeEveryClanIsReadMyAccountsDoesntSayYourAccountsAreInNone()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var alts = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
        var rival = SourceOf("s-00000003", Clan, "NovaForge", SourceRole.Watch);
        MyAccountsModel With(params RecipeSnapshot[] reads) =>
            PanelModels.MyAccounts(Live([main, alts, rival], [Installed(Clan, "value")], Snaps(reads)), Reader(), new PanelSettings(Clan.Slug, Stat: "value"));

        var nothingRead = With();
        var mainRead = With(Snapshot(main.Id, [Row(Main.RobloxUserId, 10)]));
        var allRead = With(
            Snapshot(main.Id, [Row(Main.RobloxUserId, 10)]),
            Snapshot(alts.Id, [Row(AltOne.RobloxUserId, 5)]),
            Snapshot(rival.Id, [Row(Loose.RobloxUserId, 7)]));

        Assert.Equal(new[] { "Waiting for the first read" }, nothingRead.Groups.Select(g => g.Heading).ToArray());
        Assert.Equal("Not found in the clans read so far", mainRead.Groups[^1].Heading);
        Assert.Equal(new[] { "★ CCGP", "K0i2", "Only in clans you're watching", "Not in any clan you've added" },
            allRead.Groups.Select(g => g.Heading).ToArray());
        var watched = Assert.Single(allRead.Groups[2].Rows);
        Assert.Equal((Loose.DisplayName, StatText.Dash), (watched.Name, watched.Value));
    }
```

In the existing `MyAccountsGroupsMainFirstThenMineThenAccountsInNoWatchedClan`, replace `"Not in a watched clan"` with `"Not in any clan you've added"`.

Add to `tests/AccountsModelTests.cs`, inside the class:

```csharp
    private const string GuildJson = """
        {
          "recipe": 1, "name": "Guild season", "credit": "Test data.", "metricId": "test.guild", "valueLabel": "Points",
          "everySeconds": 180,
          "inputs": [ { "id": "guild", "label": "Your guild", "plural": "Guilds" } ],
          "steps": [
            { "url": "https://example.test/season", "take": { "season": "data.season" } },
            { "url": "https://example.test/guild/{guild}", "rows": "data.members", "userId": "id", "value": "points" }
          ],
          "period": { "value": "season" },
          "headline": [ { "id": "guild-points", "label": "Guild points", "path": "data.points" } ]
        }
        """;

    [Fact]
    public void AnAccountNoReadPlacedSaysWhetherEverySourceHasBeenRead()
    {
        var clan = Sending(Clan);
        Source[] sources =
        [
            new("s-00000001", Clan.Slug, new Dictionary<string, string> { ["clan"] = "CCGP" }, SourceRole.Main),
            new("s-00000002", Clan.Slug, new Dictionary<string, string> { ["clan"] = "K0i2" }, SourceRole.Mine),
        ];

        Assert.Equal("Waiting for the first read", AccountsModel.FoundIn(Alt, [clan], sources, new Dictionary<string, RecipeSnapshot>()));
        Assert.Equal("Not found in the clans read so far",
            AccountsModel.FoundIn(Alt, [clan], sources, new Dictionary<string, RecipeSnapshot> { ["s-00000001"] = Read(101) }));
        Assert.Equal("Not in any clan you've added",
            AccountsModel.FoundIn(Alt, [clan], sources, new Dictionary<string, RecipeSnapshot> { ["s-00000001"] = Read(101), ["s-00000002"] = Read(101) }));
    }

    [Fact]
    public void RecipesThatNameTheirGroupsDifferentlyShareTheWordSource()
    {
        var guild = RecipeParser.Parse(GuildJson).Recipe!;
        Source[] sources = [new("s-00000001", Clan.Slug, new Dictionary<string, string> { ["clan"] = "CCGP" }, SourceRole.Main)];

        Assert.Equal("Not in any source you've added",
            AccountsModel.FoundIn(Alt, [Sending(Clan), Sending(guild)], sources, new Dictionary<string, RecipeSnapshot> { ["s-00000001"] = Read(101) }));
    }
```

In the existing `FoundInNamesTheClansAnAccountWasReadInMainFirst`, replace `Assert.Equal("Not in a watched clan", AccountsModel.FoundIn(Alt, [clan], sources, latest));` with:

```csharp
        // The account is in NovaForge's rows, which you only watch.
        Assert.Equal("Only in clans you're watching", AccountsModel.FoundIn(Alt, [clan], sources, latest));
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: the build fails with `CS0117: 'PanelText' does not contain a definition for 'NotFound'` (and `OnlyWatched`).

Temporarily comment out `AccountsNoReadPlacedAreHeadedByHowMuchHasBeenRead`, then:

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~PanelModelsTests|FullyQualifiedName~AccountsModelTests"
```

Expected: the new My accounts and Setup tests and the two edited ones FAIL, each on a "Not in a watched clan" heading. Uncomment the PanelText test.

- [ ] **Step 3: Implement the words**

In `src/Board/PanelText.cs`, add after `StaleSource`:

```csharp
    /// <summary>
    /// Your accounts no read of a recipe's sources placed (P15): nothing read yet; not in the ones read so far; or, once every source
    /// of the recipe has been read, in none of the groups you added.
    /// </summary>
    public static string NotFound(string group, string groups, int read, int sources) =>
        read >= sources ? $"Not in any {group} you've added"
        : read == 0 ? "Waiting for the first read"
        : $"Not found in the {groups} read so far";

    /// <summary>Your accounts seen only in groups you watch, which are never matched to your accounts for keeping or sending (spec §4.1).</summary>
    public static string OnlyWatched(string groups) => $"Only in {groups} you're watching";
```

- [ ] **Step 4: Implement My accounts**

In `src/Board/PanelModels.cs`, in `MyAccounts`, replace the block from `var rest = live.Accounts.Where(` through the closing brace of its `if (rest.Count > 0) { … }` with:

```csharp
        var rest = live.Accounts.Where(a => a.RobloxUserId == 0 || !assigned.Contains(a.RobloxUserId)).ToList();
        if (rest.Count > 0 && recipe.Inputs.Count == 0)
        {
            groups.Add(Leftovers("Not in the last read", rest));
        }
        else if (rest.Count > 0)
        {
            var ofRecipe = live.Sources.Where(s => s.Enabled && string.Equals(s.Recipe, recipe.Slug, StringComparison.Ordinal)).ToList();
            var inWatched = ofRecipe
                .Where(s => s.Role == SourceRole.Watch)
                .SelectMany(s => live.SnapshotOf(s.Id)?.Rows ?? [])
                .Select(r => r.UserId)
                .ToHashSet();
            var watched = rest.Where(a => a.RobloxUserId != 0 && inWatched.Contains(a.RobloxUserId)).ToList();
            var nowhere = rest.Where(a => !watched.Contains(a)).ToList();
            var read = ofRecipe.Count(s => live.SnapshotOf(s.Id)?.Rows is not null);
            var groupsWord = RecipeWords.GroupsLower(recipe);

            if (watched.Count > 0) groups.Add(Leftovers(PanelText.OnlyWatched(groupsWord), watched));
            if (nowhere.Count > 0) groups.Add(Leftovers(PanelText.NotFound(group, groupsWord, read, ofRecipe.Count), nowhere));
        }
```

and add this private helper after `EmptyCard`:

```csharp
    /// <summary>Your accounts under a heading with no value, rank or change: nothing placed them in a group read for them.</summary>
    private static AccountGroupModel Leftovers(string heading, IEnumerable<HostAccount> accounts) =>
        new(heading, [.. accounts.OrderBy(a => a.DisplayName, StringComparer.Ordinal)
            .Select(a => new AccountLineModel(a.RobloxUserId, a.DisplayName, Dash, Dash, Dash, false, false, true))]);
```

- [ ] **Step 5: Implement Setup › Your accounts**

In `src/UI/Setup/AccountsModel.cs`, replace `FoundIn` with:

```csharp
    /// <summary>
    /// The main and mine sources whose last read had this account, main first. Else where it is (P15): only in groups you watch, or
    /// not placed, in the words of how much has been read.
    /// </summary>
    public static string FoundIn(
        HostAccount account, IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources,
        IReadOnlyDictionary<string, RecipeSnapshot> latest)
    {
        var withInputs = installed.Where(SetupPages.HasClansPage).ToList();
        if (withInputs.Count == 0) return "";
        if (account.RobloxUserId == 0) return "Not matched by RoRoRo yet";

        var slugs = withInputs.Select(i => i.Recipe.Slug).ToHashSet(StringComparer.Ordinal);
        var ofRecipes = sources.Where(s => s.Enabled && slugs.Contains(s.Recipe)).ToList();
        bool Has(Source source) => latest.GetValueOrDefault(source.Id)?.Rows is { } rows && rows.Any(r => r.UserId == account.RobloxUserId);

        var names = new List<string>();
        foreach (var source in ofRecipes.Where(s => s.Role != SourceRole.Watch).OrderBy(s => s.Role == SourceRole.Main ? 0 : 1))
        {
            if (!Has(source)) continue;

            var name = ClansModel.NameOf(withInputs.First(i => string.Equals(i.Recipe.Slug, source.Recipe, StringComparison.Ordinal)).Recipe, source);
            names.Add(source.Role == SourceRole.Main ? $"★ {name}" : name);
        }

        if (names.Count > 0) return string.Join(", ", names.Distinct(StringComparer.Ordinal));

        var (group, groups) = Words(withInputs);
        return ofRecipes.Any(s => s.Role == SourceRole.Watch && Has(s))
            ? PanelText.OnlyWatched(groups)
            : PanelText.NotFound(group, groups, ofRecipes.Count(s => latest.GetValueOrDefault(s.Id)?.Rows is not null), ofRecipes.Count);
    }

    /// <summary>The recipes' own words when every recipe with inputs uses the same ones ("clan", "clans"), else "source", "sources".</summary>
    private static (string Group, string Groups) Words(IReadOnlyList<InstalledRecipe> withInputs)
    {
        var words = withInputs.Select(i => (Group: RecipeWords.Group(i.Recipe), Groups: RecipeWords.GroupsLower(i.Recipe))).Distinct().ToList();
        return words.Count == 1 ? words[0] : ("source", "sources");
    }
```

- [ ] **Step 6: Run the tests to see them pass**

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~PanelTextTests|FullyQualifiedName~PanelModelsTests|FullyQualifiedName~AccountsModelTests"
```

Expected: all PASS.

- [ ] **Step 7: Update the walk**

In `tools/smoke/walk-starter-board.ps1`, replace:

```powershell
    $grouped = @($accounts | Where-Object { $_ -like "*$Main" -or $_ -eq $Alt -or $_ -eq 'Not in a watched clan' }).Count -gt 0
```

with:

```powershell
    $leftover = '^(Waiting for the first read|Not found in the clans read so far|Only in clans you.re watching|Not in any clan you.ve added)$'
    $grouped = @($accounts | Where-Object { $_ -like "*$Main" -or $_ -eq $Alt -or $_ -match $leftover }).Count -gt 0
```

Run the parse check (Task 1 Step 11). Expected: `0 parse error(s)` for every file.

- [ ] **Step 8: Run the full gate**

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build
```

Expected: both pass. Walks for the controller: `walk-starter-board.ps1` step 3; Setup › Your accounts before and after Start.

- [ ] **Step 9: Commit**

```bash
git add src/Board/PanelText.cs src/Board/PanelModels.cs src/UI/Setup/AccountsModel.cs tests/PanelTextTests.cs tests/PanelModelsTests.cs tests/AccountsModelTests.cs tools/smoke/walk-starter-board.ps1
git commit -m "accounts: say where an account is, or how much has been read, instead of 'not in a watched clan'

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: Stale panels say what's really wrong

S1-13.6, S1-13.7 with S2-FR.2, S1-13.8. Rulings P16, P17, P18.

**Files:**
- Modify: `src/Board/PanelText.cs` (add `NoTotalToRace`, `RaceRemoved`, `RaceOverLimit`, `SwitchedOff`)
- Modify: `src/Board/PanelModels.cs` (`Race`, `ProfileStat`, `AccountCard`)
- Modify: `src/Board/PanelForms.cs` (`SourceProblem`)
- Test: `tests/PanelModelsTests.cs`, `tests/PanelFormsTests.cs`

**Interfaces:**
- Consumes: `PanelText.StaleSource`, `LiveBoard.SourceName`, `PanelForms.Build` (whose Profile stat source rule `ProfileStat` now mirrors).
- Produces: `PanelText.NoTotalToRace`, `PanelText.RaceRemoved(int removed, string groups)`, `PanelText.RaceOverLimit(string groups)`, `PanelText.SwitchedOff(string name)`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/PanelModelsTests.cs`, under `// ---- Race ----`:

```csharp
    [Fact]
    public void ARaceSaysWhichProblemItHasInsteadOfClaimingItsClanWasRemoved()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var rival = SourceOf("s-00000003", Clan, "NovaForge", SourceRole.Watch);
        var live = Live([main, rival], [Installed(Clan, "value"), Installed(Profile, "diamonds")],
            Snaps(Snapshot(main.Id, [], [Points(30_214_400)], LivePeriod), Snapshot(rival.Id, [], [Points(31_300_000)], LivePeriod)));

        // The profile recipe has no summed headline to race.
        var noTotal = PanelModels.Race(live, Reader(), new PanelSettings(Profile.Slug, SourceIds: [main.Id, rival.Id]));
        var oneGone = PanelModels.Race(live, Reader(), new PanelSettings(Clan.Slug, SourceIds: [main.Id, "s-gone0000", rival.Id]));

        Assert.Equal(PanelText.NoTotalToRace, noTotal.Head.Stale);
        Assert.Null(oneGone.Head.Stale);
        Assert.Equal(2, oneGone.Series.Count);
        Assert.Equal("One of this race's clans was removed.", oneGone.Head.Note);
    }

    [Fact]
    public void ARaceOfMoreThanFiveDrawsFiveAndSaysSo()
    {
        var clans = Enumerable.Range(1, 7).Select(i => SourceOf($"s-0000000{i}", Clan, $"Clan{i}", SourceRole.Watch)).ToList();
        var live = Live(clans, [Installed(Clan, "value")], Snaps([.. clans.Select(c => Snapshot(c.Id, [], [Points(1_000)], LivePeriod))]));

        var model = PanelModels.Race(live, Reader(), new PanelSettings(Clan.Slug, SourceIds: [.. clans.Select(c => c.Id)]));

        Assert.Equal(PanelModels.MaxRace, model.Series.Count);
        Assert.Equal("Only the first 5 clans are drawn.", model.Head.Note);
    }
```

Under `// ---- Account card ----`:

```csharp
    [Fact]
    public void AnAccountCardForOneAccountNamesThatAccountWhenItHasNoReading()
    {
        var alts = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
        var live = Live([alts], [Installed(Clan, "value")], Snaps(Snapshot(alts.Id, [Row(AltOne.RobloxUserId, 12_418_220)], period: LivePeriod)));

        var listed = PanelModels.AccountCard(live, Reader(), new PanelSettings(Clan.Slug, Stat: "value", UserId: AltTwo.RobloxUserId));
        var unlisted = PanelModels.AccountCard(live, Reader(), new PanelSettings(Clan.Slug, Stat: "value", UserId: 987_654_321));
        var top = PanelModels.AccountCard(Live([alts], [Installed(Clan, "value")], Snaps()), Reader(), new PanelSettings(Clan.Slug, Stat: "value"));

        Assert.Equal($"No reading of {AltTwo.DisplayName} yet.", listed.Head.Note);
        Assert.Equal("RoRoRo isn't listing this panel's account right now.", unlisted.Head.Note);
        Assert.Equal("No reading of your accounts yet.", top.Head.Note);
    }
```

Under `// ---- Profile stat ----`:

```csharp
    [Fact]
    public void AProfileStatWhosePinnedSourceWasRemovedSaysSoAndARowNamesItsMiss()
    {
        var removed = SourceOf("s-00000009", Profile, null, SourceRole.Mine);
        var other = SourceOf("s-00000010", Profile, null, SourceRole.Mine);
        var snapshot = Snapshot(other.Id, [Row(AltOne.RobloxUserId, 5, "diamonds")]) with
        {
            CellMisses = new Dictionary<(long UserId, string Stat), string> { [(Main.RobloxUserId, "diamonds")] = "no 'Diamonds' in this account's response" },
        };
        var live = Live([other], [Installed(Profile, "diamonds")], Snaps(snapshot));

        var gone = PanelModels.ProfileStat(live, Reader(), new PanelSettings(Profile.Slug, SourceId: removed.Id, Stat: "diamonds"));
        var unpinned = PanelModels.ProfileStat(live, Reader(), new PanelSettings(Profile.Slug, Stat: "diamonds"));

        Assert.Equal("This panel's source was removed.", gone.Head.Stale);
        Assert.Equal("no 'Diamonds' in this account's response", unpinned.Rows.Single(r => r.Name == Main.DisplayName).Note);
    }
```

In `tests/PanelFormsTests.cs`, in `AProfileStatWhoseOnlySourceIsOffIsNotStale`, replace the last three statements (from the comment `// With no source pinned, …` through `Assert.Equal("Choose a source.", …);`) with:

```csharp
        // With no source pinned, the panel reads the same off source the form pins, and says it is off (P17).
        var unpinned = PanelModels.ProfileStat(live, Reader(), settings with { SourceId = null });
        Assert.False(unpinned.Head.HasStale);
        Assert.Equal(PanelText.SwitchedOff(Profile.Name), unpinned.Head.Note);
        Assert.Null(PanelForms.Problem(PanelType.ProfileStat, settings with { SourceId = null }, live));
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: the build fails with `CS0117: 'PanelText' does not contain a definition for 'NoTotalToRace'` (and `SwitchedOff`).

Temporarily comment out `ARaceSaysWhichProblemItHasInsteadOfClaimingItsClanWasRemoved` and the edited statements in `AProfileStatWhoseOnlySourceIsOffIsNotStale`, then:

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~PanelModelsTests"
```

Expected: `ARaceOfMoreThanFiveDrawsFiveAndSaysSo` (empty note), `AnAccountCardForOneAccountNamesThatAccountWhenItHasNoReading` ("No reading of your accounts yet.") and `AProfileStatWhosePinnedSourceWasRemovedSaysSoAndARowNamesItsMiss` (not stale) FAIL. Uncomment.

- [ ] **Step 3: Implement the words**

In `src/Board/PanelText.cs`, add after `StaleStat`:

```csharp
    public const string NoTotalToRace = "This panel's recipe has no total to race.";

    /// <summary>A race drawn without some of its lines (P16): "One of this race's clans was removed.", "2 of this race's clans were removed."</summary>
    public static string RaceRemoved(int removed, string groups) =>
        removed == 1 ? $"One of this race's {groups} was removed." : $"{removed} of this race's {groups} were removed.";

    /// <summary>A race with more lines than it draws, from a hand-edited boards.json.</summary>
    public static string RaceOverLimit(string groups) => $"Only the first {PanelModels.MaxRace} {groups} are drawn.";

    /// <summary>A panel reading a source that is off (P17): "Profile is switched off, so it isn't being read."</summary>
    public static string SwitchedOff(string name) => $"{name} is switched off, so it isn't being read.";
```

- [ ] **Step 4: Implement Race**

In `src/Board/PanelModels.cs`, replace the whole `Race` method with:

```csharp
    public static RaceModel Race(LiveBoard live, ScoreBookReader reader, PanelSettings settings)
    {
        var recipe = live.FindRecipe(settings.Recipe)?.Recipe;
        var title = PanelText.Title(PanelType.Race, recipe, live.Installed);
        if (recipe is null) return new RaceModel(StaleSource(live, settings, title), [], [], "");

        // A recipe with no summed headline has nothing to race; its sources may all be there (P16).
        if (TotalId(recipe) is not { } totalId) return new RaceModel(new PanelHead(title, Stale: PanelText.NoTotalToRace), [], [], "");

        IReadOnlyList<string> ids = settings.SourceIds ?? [];
        var found = ids.Select(live.FindSource).OfType<Source>().ToList();
        if (found.Count == 0) return new RaceModel(StaleSource(live, settings, title), [], [], "");

        var sources = found.Take(MaxRace).ToList();
        var groupsWord = RecipeWords.GroupsLower(recipe);
        var notes = new List<string>();
        if (found.Count < ids.Count) notes.Add(PanelText.RaceRemoved(ids.Count - found.Count, groupsWord));
        if (found.Count > MaxRace) notes.Add(PanelText.RaceOverLimit(groupsWord));

        var series = new List<ChartSeries>();
        var legend = new List<LegendItem>();
        var overdue = false;
        var anyPeriodKnown = recipe.Period is null;

        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            var snapshot = live.SnapshotOf(source.Id);
            // Before this source's own period is known, "no period" reads the book as every period kept.
            var periodKnown = recipe.Period is null || snapshot?.Period is not null;
            anyPeriodKnown |= periodKnown;

            var points = periodKnown
                ? reader.HeadlineSeries(source.Id, totalId, snapshot?.Period?.Value).Select(p => new ChartPoint(p.T, p.Value)).ToList()
                : new List<ChartPoint>();

            // The live read, until the book has a line for it.
            if (HeadlineNumber(snapshot, totalId) is { } now && live.LastRead.TryGetValue(source.Id, out var at)
                && (points.Count == 0 || points[^1].T < at.AddSeconds(-30)))
            {
                points.Add(new ChartPoint(at, now));
            }

            var label = PanelText.SourceLabel(live.SourceName(source), source.Role);

            series.Add(new ChartSeries(label, points, i));
            legend.Add(new LegendItem($"{label} {(points.Count > 0 ? StatText.Abbrev(points[^1].Value) : Dash)}", i));
            overdue |= live.IsOverdue(source);
        }

        var totalLabel = recipe.Headline.First(h => h.Id == totalId).Label;
        var head = new PanelHead(title, $"{RecipeWords.Lower(totalLabel)} since the {RecipeWords.Period(recipe)} started", Overdue: overdue,
            Note: string.Join(" ", notes));

        // No source's period is known yet: every point in "series" would be mixing periods together.
        if (!anyPeriodKnown)
        {
            return new RaceModel(head with { Note = string.Join(" ", notes.Prepend("Waiting for the first read.")) }, [], [], "");
        }

        return new RaceModel(head, series, legend, $"{title}: {string.Join(", ", legend.Select(l => l.Text))}");
    }
```

- [ ] **Step 5: Implement Profile stat and Account card**

In `PanelModels.ProfileStat`, replace:

```csharp
        var source = live.FindSource(settings.SourceId)
                     ?? live.Sources.FirstOrDefault(s => s.Enabled && string.Equals(s.Recipe, recipe.Slug, StringComparison.Ordinal));
        if (source is null) return new ProfileStatModel(StaleSource(live, settings, title), "", []);
```

with:

```csharp
        // A pinned source that is gone is gone: never another source's numbers under this panel's settings. Unpinned (a starter's
        // panel), the recipe's first source that is on, else its first, as PanelForms.Build pins (P17).
        var source = settings.SourceId is not null
            ? live.FindSource(settings.SourceId)
            : live.Sources.FirstOrDefault(s => s.Enabled && string.Equals(s.Recipe, recipe.Slug, StringComparison.Ordinal))
              ?? live.Sources.FirstOrDefault(s => string.Equals(s.Recipe, recipe.Slug, StringComparison.Ordinal));
        if (source is null) return new ProfileStatModel(StaleSource(live, settings, title), "", []);
```

in the row, replace:

```csharp
                unavailable ?? (value is null && missed is not null ? "can't read" : ""),
```

with:

```csharp
                unavailable ?? (value is null ? missed ?? "" : ""),
```

and replace the method's last `return` with:

```csharp
        var note = source.Enabled ? "" : PanelText.SwitchedOff(live.SourceName(source));
        return new ProfileStatModel(new PanelHead(title, stat.Label, Overdue: live.IsOverdue(source), Note: note), stat.Label, MissingLast(rows));
```

In `PanelModels.AccountCard`, replace:

```csharp
        if (picked.Count == 0) return EmptyCard(new PanelHead(title, Note: "No reading of your accounts yet."));
```

with:

```csharp
        if (picked.Count == 0)
        {
            // R16, refined (P18): a card pinned to one account names it, or says RoRoRo isn't listing it.
            var note = settings.UserId is not { } pinned ? "No reading of your accounts yet."
                : live.Accounts.FirstOrDefault(a => a.RobloxUserId == pinned && pinned != 0) is { } account ? $"No reading of {account.DisplayName} yet."
                : "RoRoRo isn't listing this panel's account right now.";
            return EmptyCard(new PanelHead(title, Note: note));
        }
```

- [ ] **Step 6: Implement the form's rule**

In `src/Board/PanelForms.cs`, in `SourceProblem`, replace:

```csharp
        // A Profile stat with no source reads the recipe's first source that is on (PanelModels.ProfileStat); with none on, it must pin one.
        if (settings.SourceId is null)
        {
            return type == PanelType.ProfileStat && live.Sources.Any(s => s.Enabled && s.Recipe == settings.Recipe) ? null : $"Choose a {word}.";
        }
```

with:

```csharp
        // A Profile stat with no source reads the recipe's first source that is on, else its first (PanelModels.ProfileStat, P17).
        if (settings.SourceId is null)
        {
            return type == PanelType.ProfileStat && live.Sources.Any(s => s.Recipe == settings.Recipe) ? null : $"Choose a {word}.";
        }
```

- [ ] **Step 7: Run the tests to see them pass**

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~PanelModelsTests|FullyQualifiedName~PanelFormsTests|FullyQualifiedName~PanelGalleryTests|FullyQualifiedName~PanelTextTests"
```

Expected: all PASS, `TheRaceWaitsForTheFirstReadWhenNoSourceHasAPeriodYet` included (its note is still exactly "Waiting for the first read.").

- [ ] **Step 8: Run the full gate**

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build
```

Expected: both pass. Walks for the controller: `walk-board-editing.ps1` step 7 (a removed clan's Standing still says "This panel's clan was removed.").

- [ ] **Step 9: Commit**

```bash
git add src/Board/PanelText.cs src/Board/PanelModels.cs src/Board/PanelForms.cs tests/PanelModelsTests.cs tests/PanelFormsTests.cs
git commit -m "panels: a race, a profile stat and an account card say what is really wrong

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: Forms and gallery corners

S2-P.5, S2-4.5, S2-4.6, S2-FR.1, S2-F.8. Rulings P19, P20, P21, P22, P23.

**Files:**
- Modify: `src/Board/PanelGallery.cs` (`CardRecipe`, `CanAdd`, add `PromotionOrigins`)
- Modify: `src/Board/PanelForms.cs` (add `NoStatKey`; `StatChoices`, `Defaults`, `Problem`)
- Modify: `src/UI/BoardButtons.cs` (`For` gains `nothingToCopy`)
- Modify: `src/UI/BoardWindow.xaml.cs` (`ButtonStates`)
- Test: `tests/PanelGalleryTests.cs`, `tests/PanelFormsTests.cs`, `tests/BoardButtonsTests.cs`
- Walk: `tools/smoke/window-smoke.ps1`

**Interfaces:**
- Consumes: `PanelForms.SourceChoices`, `PanelForms.StatChoices`, `PanelForms.Defaults`, `PanelForms.Build`, `PanelModels.Standing` (its stale words), `AppServices.BoardsFollowStarter`, `BoardWindow.ShownBoard`.
- Produces: `PanelForms.NoStatKey` (`""`), `BoardButtons.For(..., bool nothingToCopy = false)`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/PanelGalleryTests.cs`, inside the class (after `GuildSeasonJson`, `Guild` and `Card` are declared):

```csharp
    [Fact]
    public void ARaceCardCountsOnlySourcesThatAreOnAsItsFormDoes()
    {
        var live = Live([MainClan, AltClan with { Enabled = false }], [Installed(Clan, "value")], NoReads);

        Assert.False(Card(PanelGallery.Cards(live), PanelType.Race).CanAdd);
        Assert.Single(PanelForms.Defaults(PanelType.Race, live).Sources!);
    }

    [Fact]
    public void ThePromotionCheckCardSpeaksForARecipeItCanBeAddedOn()
    {
        // Your main is a guild with no other guild to compare with; two clans of yours can be compared.
        var guildMain = new Source("s-00000004", Guild.Slug, new Dictionary<string, string> { ["guild"] = "Wolves" }, SourceRole.Main);
        var nova = SourceOf("s-00000003", Clan, "NovaForge", SourceRole.Mine);
        var cards = PanelGallery.Cards(Live([guildMain, AltClan, nova], [Installed(Guild, "value"), Installed(Clan, "value")], NoReads));

        var card = Card(cards, PanelType.PromotionCheck);
        Assert.True(card.CanAdd);
        Assert.Equal("Needs a clan your accounts are in, and one to compare with (your main unless you pick another).", card.Needs);
    }
```

Add to `tests/PanelFormsTests.cs`, inside the class:

```csharp
    [Fact]
    public void APanelWhoseRecipeIsGoneUsesTheBoardsWordInItsForm()
    {
        var live = Everything();
        var settings = new PanelSettings("removed-recipe", SourceId: "s-gone0000");

        var board = PanelModels.Standing(live, Reader(), settings).Head.Stale;

        Assert.Equal("This panel's source was removed.", board);
        Assert.Equal($"{board} Choose another.", PanelForms.Problem(PanelType.Standing, settings, live));
    }

    [Fact]
    public void APastPeriodsPanelWhoseStatIsGoneCanStopShowingABestAccount()
    {
        // The clan recipe has no ticked stat left, and the panel's stat was removed.
        var live = Live([MainClan], [Installed(Clan)], new Dictionary<string, RecipeSnapshot>());
        var saved = new PanelSettings(Clan.Slug, SourceId: MainClan.Id, Stat: "gone-stat");

        var none = Assert.Single(PanelForms.StatChoices(PanelType.PastPeriods, live, PanelForms.From(saved), saved));
        var cleared = PanelForms.Build(PanelType.PastPeriods, PanelForms.From(saved) with { Stat = none.Key }, live);

        Assert.Equal((PanelForms.NoStatKey, "Don't show your best account"), (none.Key, none.Label));
        Assert.Equal("This panel's stat was removed. Choose another.", PanelForms.Problem(PanelType.PastPeriods, saved, live));
        Assert.Equal(saved with { Stat = null }, cleared);
        Assert.Null(PanelForms.Problem(PanelType.PastPeriods, cleared, live));
        // With no ticked stat, a new panel starts on no stat, as before.
        Assert.Null(PanelForms.Defaults(PanelType.PastPeriods, live).Stat);
    }
```

In the existing `StatsAreTheTickedOnesNamedByRecipeWhenThereIsMoreThanOne`, replace:

```csharp
        Assert.Equal(new[] { ClanStat }, Keys(PanelForms.StatChoices(PanelType.PastPeriods, live, new FormValues(Source: MainClan.Id), null)));
```

with:

```csharp
        Assert.Equal(new[] { ClanStat, PanelForms.NoStatKey }, Keys(PanelForms.StatChoices(PanelType.PastPeriods, live, new FormValues(Source: MainClan.Id), null)));
```

Add to `tests/BoardButtonsTests.cs`, inside the class:

```csharp
    [Fact]
    public void TheFirstRunStarterWithNoPanelsCantBeDuplicated()
    {
        var empty = BoardButtons.For(loaded: true, running: false, starting: false, testing: false, importing: false, boards: 1, nothingToCopy: true);
        var filled = BoardButtons.For(loaded: true, running: false, starting: false, testing: false, importing: false, boards: 1);

        Assert.False(empty.DuplicateBoard);
        Assert.True(empty.RenameBoard);
        Assert.True(filled.DuplicateBoard);
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: the build fails with `CS0117: 'PanelForms' does not contain a definition for 'NoStatKey'` and `CS1739: The best overload for 'For' does not have a parameter named 'nothingToCopy'`.

Temporarily comment out `APastPeriodsPanelWhoseStatIsGoneCanStopShowingABestAccount`, `TheFirstRunStarterWithNoPanelsCantBeDuplicated` and the edited line in `StatsAreTheTickedOnes…`, then:

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~PanelGalleryTests|FullyQualifiedName~PanelFormsTests"
```

Expected: `ARaceCardCountsOnlySourcesThatAreOnAsItsFormDoes` (CanAdd true), `ThePromotionCheckCardSpeaksForARecipeItCanBeAddedOn` ("Needs a guild …") and `APanelWhoseRecipeIsGoneUsesTheBoardsWordInItsForm` ("This panel's clan was removed. …") FAIL. Uncomment.

- [ ] **Step 3: Implement the gallery**

In `src/Board/PanelGallery.cs`, add after the file-scoped `namespace` line:

```csharp
using Source = Labs626.UrScore.Core.Source;
```

Replace `CardRecipe` and `CanAdd` with:

```csharp
    /// <summary>
    /// The recipe a card speaks for: the one a panel added from it starts on (<see cref="PanelForms.Defaults"/>), and for Promotion
    /// check the first recipe one can actually be added on (P21); else the first installed recipe the type fits, else none.
    /// </summary>
    private static Recipe? CardRecipe(PanelType type, LiveBoard live)
    {
        var slug = type == PanelType.PromotionCheck
            ? PromotionOrigins(live).FirstOrDefault()?.Recipe
            : PanelForms.Build(type, PanelForms.Defaults(type, live), live).Recipe;

        return live.FindRecipe(slug)?.Recipe ?? live.Installed.FirstOrDefault(i => PanelForms.Fits(type, i.Recipe))?.Recipe;
    }

    private static bool CanAdd(PanelType type, LiveBoard live)
    {
        var none = new FormValues();
        return type switch
        {
            // Only sources that are on, as the form's defaults take (P19).
            PanelType.Race => live.Sources
                .Where(s => s.Enabled && live.FindRecipe(s.Recipe) is { } installed && PanelForms.Fits(type, installed.Recipe))
                .GroupBy(s => s.Recipe, StringComparer.Ordinal)
                .Any(g => g.Count() >= 2),
            PanelType.PromotionCheck => PromotionOrigins(live).Count > 0,
            PanelType.MyAccounts or PanelType.Records or PanelType.ProfileStat or PanelType.AccountCard =>
                PanelForms.StatChoices(type, live, none, null).Count > 0,
            _ => PanelForms.SourceChoices(type, PanelField.Source, live, none).Count > 0,
        };
    }

    /// <summary>
    /// Sources a Promotion check can be added from: yours, with another source of the same recipe to compare with and a ticked
    /// stat. Those of the main's recipe come first, so the card speaks for the recipe its form starts on whenever it can.
    /// </summary>
    private static IReadOnlyList<Source> PromotionOrigins(LiveBoard live)
    {
        var mainRecipe = live.FindSource(PanelForms.Defaults(PanelType.PromotionCheck, live).ToSource)?.Recipe;
        return [.. PanelForms.SourceChoices(PanelType.PromotionCheck, PanelField.Source, live, new FormValues())
            .Where(origin => PanelForms.SourceChoices(PanelType.PromotionCheck, PanelField.ToSource, live, new FormValues(Source: origin.Key)).Count > 0
                             && PanelForms.StatChoices(PanelType.PromotionCheck, live, new FormValues(Source: origin.Key), null).Count > 0)
            .Select(origin => live.FindSource(origin.Key))
            .OfType<Source>()
            .OrderBy(source => string.Equals(source.Recipe, mainRecipe, StringComparison.Ordinal) ? 0 : 1)];
    }
```

- [ ] **Step 4: Implement the forms**

In `src/Board/PanelForms.cs`, add after `TopAccountKey`:

```csharp
    /// <summary>Past periods' "Don't show your best account" (P22): its stat is optional, so a form with no ticked stat left can still save.</summary>
    public const string NoStatKey = "";

    private const string NoStatLabel = "Don't show your best account";
```

In `StatChoices`, replace the final `return [.. found.SelectMany(…)];` statement with:

```csharp
        IReadOnlyList<FormChoice> choices = [.. found.SelectMany(f => f.Stats.Select(s => new FormChoice(
            StatKey(f.Installed.Recipe.Slug, s.Key),
            found.Count > 1 ? $"{s.Label} · {f.Installed.Recipe.Name}" : s.Label)))];

        // Last, so a new panel still starts on the first ticked stat (R14).
        return type == PanelType.PastPeriods ? [.. choices, new FormChoice(NoStatKey, NoStatLabel)] : choices;
```

In `Defaults`, in the `default:` branch, replace:

```csharp
                return type == PanelType.PastPeriods ? values with { Stat = StatChoices(type, live, values, null).FirstOrDefault()?.Key } : values;
```

with:

```csharp
                return type == PanelType.PastPeriods
                    ? values with { Stat = StatChoices(type, live, values, null).FirstOrDefault(c => c.Key != NoStatKey)?.Key }
                    : values;
```

In `Problem`, replace:

```csharp
        var (word, words) = installed is { Recipe.IsGroupList: false }
            ? GroupWords(installed.Recipe)
            : type == PanelType.Top ? GroupWords((Recipe?)null) : GroupWords(live);
```

with:

```csharp
        // The words the board uses for the same panel: a recipe that is gone has none of its own, so "source", as
        // PanelModels.StaleSource says (P20). A blank form with no recipe picked yet takes the first recipe with inputs.
        var (word, words) = installed is { Recipe.IsGroupList: false } ? GroupWords(installed.Recipe)
            : type == PanelType.Top || (installed is null && settings.Recipe.Length > 0) ? GroupWords((Recipe?)null)
            : GroupWords(live);
```

- [ ] **Step 5: Implement Duplicate**

In `src/UI/BoardButtons.cs`, add to the `For` doc comment, after the `editing` param:

```csharp
    /// <param name="nothingToCopy">
    /// The board on screen is the first-run starter still following your sources, with no panels (P23): a copy would have nothing
    /// in it, and the first save would drop the original (<see cref="Labs626.UrScore.Board.BoardEdits.ForFirstSave"/>).
    /// </param>
```

and replace the `For` method's signature line and its `DuplicateBoard:` line:

```csharp
    public static BoardButtonStates For(
        bool loaded, bool running, bool starting, bool testing, bool importing, int boards = 1, bool editing = false, bool nothingToCopy = false) => new(
```

```csharp
        DuplicateBoard: !editing && !nothingToCopy,
```

In `src/UI/BoardWindow.xaml.cs`, replace `ButtonStates` (the `=>` method and its summary) with:

```csharp
    /// <summary>What every button, tab and tab menu item takes right now, for <see cref="ApplyButtons"/> and the press guards.</summary>
    private BoardButtonStates ButtonStates()
    {
        var boards = _services.Boards;
        var nothingToCopy = _services.BoardsFollowStarter && ShownBoard(boards).Panels.Count == 0;
        return BoardButtons.For(_services.ReaderLoaded, _services.Running, _starting, _testing, _importing, boards.Count, Editing, nothingToCopy);
    }
```

- [ ] **Step 6: Run the tests to see them pass**

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~PanelGalleryTests|FullyQualifiedName~PanelFormsTests|FullyQualifiedName~BoardButtonsTests"
```

Expected: all PASS, `EveryTypesDefaultsBuildSettingsWithNoProblem` and `DefaultsFollowTheMain` included.

- [ ] **Step 7: Add the walk step**

In `tools/smoke/window-smoke.ps1`, change the dot-source line from `uia-import.ps1` to `uia-board.ps1` (which dot-sources `uia-import.ps1` itself):

```powershell
. (Join-Path $PSScriptRoot 'uia-board.ps1')
```

and after the line that records `'1 First run asks for a recipe'`, add:

```powershell
    Check '1b The first-run board has nothing to duplicate' (-not (Invoke-TabMenu $board 'DuplicateBoardItem')) 'DuplicateBoardItem disabled'
```

Run the parse check (Task 1 Step 11). Expected: `0 parse error(s)` for every file.

- [ ] **Step 8: Run the full gate**

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build
```

Expected: both pass. Walks for the controller: `window-smoke.ps1` (new step 1b), `walk-board-editing.ps1` steps 6 (gallery) and 9 (duplicate on a board with panels still works).

- [ ] **Step 9: Commit**

```bash
git add src/Board/PanelGallery.cs src/Board/PanelForms.cs src/UI/BoardButtons.cs src/UI/BoardWindow.xaml.cs tests/PanelGalleryTests.cs tests/PanelFormsTests.cs tests/BoardButtonsTests.cs tools/smoke/window-smoke.ps1
git commit -m "boards: gallery cards that open on a form they can fill, the board's words in a form, a best account you can drop

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 7: The board's lines, a book that won't load, the period line and the icon

S1-14.3, S1-14.5, S1-14.2 with S2-8.3, S2-5.9, S1-14.1. Rulings P24, P25, P26, P27.

**Files:**
- Modify: `src/Board/StarterBoards.cs` (`BoardEmpty.BookNotRead`)
- Modify: `src/UI/BoardText.cs` (`Starting`, `Testing`, `StateLine`, `EmptyFor`, `EmptyState`)
- Modify: `src/Board/BoardEdits.cs` (`AnchorSourceId`)
- Modify: `src/UI/BoardButtons.cs` (the `importing` doc)
- Modify: `src/UI/BoardWindow.xaml.cs`
- Modify: `src/Composition/AppServices.cs` (`LoadInstalled`, add `IconsToForget`)
- Test: `tests/BoardTextTests.cs`, `tests/BoardEditsTests.cs`, `tests/AppServicesIconTests.cs` (new)
- Walks: `tools/smoke/walk-starter-board.ps1`, `tools/smoke/walk-book-unreadable.ps1` (new), `tools/smoke/README.md`

**Interfaces:**
- Consumes: `AppServices.LoadBookAsync` (a call after a failed load starts a new load), `AppServices.ReaderLoaded`, `DiagnosticsModel.StateText`, `BoardButtons.For(..., nothingToCopy)` (Task 6).
- Produces: `BoardEmpty.BookNotRead`; `BoardText.Starting`, `BoardText.Testing`, `BoardText.StateLine(LiveBoard, bool everStarted, bool starting = false, bool testing = false)`, `BoardText.EmptyFor(..., bool bookUnread = false)`; `BoardEdits.AnchorSourceId(BoardDef, IReadOnlyList<Source>, IReadOnlyList<InstalledRecipe>)`; `AppServices.IconsToForget(IEnumerable<string>, IReadOnlyList<InstalledRecipe>)` (internal).

- [ ] **Step 1: Write the failing tests**

In `tests/BoardTextTests.cs`, replace `TheStateLineSaysStartedStoppedOrNamesASourceInTrouble` with:

```csharp
    [Fact]
    public void TheStateLineSaysStartedStoppedOrNamesASourceInTrouble()
    {
        var snaps = new Dictionary<string, RecipeSnapshot>
        {
            [MainClan.Id] = Snapshot(MainClan.Id, []),
            [AltClan.Id] = new RecipeSnapshot(WatchState.SourceUnreachable, "timed out", [], [], 0) { SourceId = AltClan.Id },
        };
        LiveBoard Board(bool running, params Source[] sources) => Live(sources, [Installed(Clan, "value")], snaps, running);
        var neverRead = Live([MainClan, AltClan], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot>());

        Assert.Equal("Not started.", BoardText.StateLine(neverRead, everStarted: false));
        Assert.Equal("Stopped. Last read, K0i2: Could not reach the data.", BoardText.StateLine(Board(false, MainClan, AltClan), everStarted: true));
        Assert.Equal("K0i2: Could not reach the data.", BoardText.StateLine(Board(true, MainClan, AltClan), everStarted: true));
        Assert.Equal("Reading 1 source.", BoardText.StateLine(Board(true, MainClan), everStarted: true));
    }

    [Fact]
    public void StoppedTheLineStillGivesTheLastReadAndStartAndTestNowSayWhatTheyWaitFor()
    {
        var healthy = Live([MainClan, AltClan], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot>
        {
            [MainClan.Id] = Snapshot(MainClan.Id, []),
            [AltClan.Id] = Snapshot(AltClan.Id, []),
        });

        Assert.Equal("Not started. The last read covered 2 sources.", BoardText.StateLine(healthy, everStarted: false));
        Assert.Equal(BoardText.Starting, BoardText.StateLine(healthy, everStarted: false, starting: true));
        Assert.Equal(BoardText.Testing, BoardText.StateLine(healthy, everStarted: true, testing: true));
    }

    [Fact]
    public void AScoreBookThatCouldntBeReadComesBeforeEveryOtherEmptyState()
    {
        var saved = new BoardDef("b-00000001", "Rivals", []);
        var state = BoardText.EmptyState(BoardEmpty.BookNotRead, null);

        Assert.Equal(BoardEmpty.BookNotRead, BoardText.EmptyFor(StarterBoards.Build([], []), followsStarter: true, saved, bookUnread: true));
        Assert.Equal(BoardEmpty.NoRecipes, BoardText.EmptyFor(StarterBoards.Build([], []), followsStarter: true, saved));
        Assert.Equal(("Your score book couldn't be read", "Try again"), (state.Line, state.Button));
    }
```

In `tests/BoardEditsTests.cs`, replace the four assertions of `TheAnchorIsTheFirstPanelsSourceThatIsStillOn` with:

```csharp
        IReadOnlyList<Labs626.UrScore.Recipes.InstalledRecipe> installed = [Installed(Clan, "value")];
        Assert.Equal(alt.Id, BoardEdits.AnchorSourceId(board, [main, alt], installed));
        Assert.Equal(main.Id, BoardEdits.AnchorSourceId(board, [main, alt with { Enabled = false }], installed));
        Assert.Equal(main.Id, BoardEdits.AnchorSourceId(board with { Panels = [] }, [alt, main], installed));
        Assert.Null(BoardEdits.AnchorSourceId(board with { Panels = [] }, [alt], installed));
```

and add after it:

```csharp
    [Fact]
    public void TheAnchorIsNeverAGroupListWhileTheBoardHasASourceOfItsOwnRecipe()
    {
        // A recipe with no inputs has one source; the board's first panel that names a source is a Top panel on the group list.
        var own = SourceOf("s-00000009", Profile, null, SourceRole.Mine);
        var top = SourceOf("s-0000000a", TopList, null, SourceRole.Watch);
        var board = new BoardDef("b", "b",
        [
            new PanelDef("p-1", PanelType.MyAccounts, new PanelSize(6), new PanelSettings(Profile.Slug, Stat: "diamonds")),
            new PanelDef("p-2", PanelType.Top, new PanelSize(4), new PanelSettings(TopList.Slug, SourceId: top.Id)),
        ]);
        IReadOnlyList<Labs626.UrScore.Recipes.InstalledRecipe> installed = [Installed(Profile, "diamonds"), Installed(TopList)];

        Assert.Equal(own.Id, BoardEdits.AnchorSourceId(board, [top, own], installed));
        Assert.Equal(top.Id, BoardEdits.AnchorSourceId(board, [top, own with { Enabled = false }], installed));
    }
```

Create `tests/AppServicesIconTests.cs`:

```csharp
using Labs626.UrScore.Composition;
using Labs626.UrScore.Recipes;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

public class AppServicesIconTests
{
    [Fact]
    public void AnIconIsForgottenWhenItsRecipeIsGoneOrNoLongerNamesOne()
    {
        // The clan fixture names an icon; the profile fixture doesn't.
        IReadOnlyList<InstalledRecipe> installed = [Installed(Clan, "value"), Installed(Profile, "diamonds")];

        Assert.Equal(new[] { Profile.Slug, "removed-recipe" },
            AppServices.IconsToForget([Clan.Slug, Profile.Slug, "removed-recipe"], installed).ToArray());
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: the build fails with `CS0117: 'BoardEmpty' does not contain a definition for 'BookNotRead'`, `CS0117: 'BoardText' does not contain a definition for 'Starting'`, `CS1739` for `bookUnread`, `CS1501: No overload for method 'AnchorSourceId' takes 3 arguments` and `CS0117: 'AppServices' does not contain a definition for 'IconsToForget'`.

- [ ] **Step 3: Implement the words and the empty state**

In `src/Board/StarterBoards.cs`, replace the enum with:

```csharp
public enum BoardEmpty { None, NoRecipes, NoStats, NoSources, NoPanels, BookNotRead }
```

In `src/UI/BoardText.cs`, add after `HostDown`:

```csharp
    public const string Starting = "Starting: asking RoRoRo for your accounts…";

    public const string Testing = "Reading every source once…";
```

Replace `StateLine` with:

```csharp
    /// <summary>
    /// What Ur Score is doing (P24). Start's wait for RoRoRo and a Test now read say so for as long as they last. Stopped, the line
    /// still gives the last read's news, a Test now's included: the first source in trouble, else how many sources were read.
    /// </summary>
    public static string StateLine(LiveBoard live, bool everStarted, bool starting = false, bool testing = false)
    {
        if (testing) return Testing;
        if (starting && !live.Running) return Starting;

        var enabled = live.Sources.Where(s => s.Enabled).ToList();
        var trouble = enabled
            .Select(s => (Source: s, Snapshot: live.SnapshotOf(s.Id)))
            .FirstOrDefault(x => x.Snapshot is { } snapshot && !Healthy(snapshot.State));
        var troubleText = trouble.Snapshot is { } bad ? $"{live.SourceName(trouble.Source)}: {DiagnosticsModel.StateText(bad.State)}" : null;

        if (!live.Running)
        {
            var lead = everStarted ? "Stopped." : "Not started.";
            var read = enabled.Count(s => live.SnapshotOf(s.Id) is not null);
            return troubleText is not null ? $"{lead} Last read, {troubleText}"
                : read == 0 ? lead
                : read == 1 ? $"{lead} The last read covered 1 source."
                : $"{lead} The last read covered {read} sources.";
        }

        if (enabled.Count == 0) return "Running, with nothing to read yet.";
        return troubleText ?? (enabled.Count == 1 ? "Reading 1 source." : $"Reading {enabled.Count} sources.");
    }
```

Replace `EmptyFor` (with its summary) with:

```csharp
    /// <summary>
    /// Which empty state a board shows (R1): a score book that couldn't be read over everything (P25); no recipes over every board;
    /// the starter's own states while the board still follows your sources; a saved board with no panels; else none.
    /// </summary>
    public static BoardEmpty EmptyFor(StarterBoard starter, bool followsStarter, BoardDef board, bool bookUnread = false) =>
        bookUnread ? BoardEmpty.BookNotRead
        : starter.Empty == BoardEmpty.NoRecipes ? BoardEmpty.NoRecipes
        : followsStarter ? starter.Empty
        : board.Panels.Count == 0 ? BoardEmpty.NoPanels
        : BoardEmpty.None;
```

In `EmptyState`, add before the `_ =>` arm:

```csharp
            BoardEmpty.BookNotRead => ("Your score book couldn't be read",
                "Nothing is read or kept until Ur Score can read it. The reason is on the line above.",
                "Try again"),
```

In `src/UI/BoardButtons.cs`, replace the `importing` param doc with:

```csharp
    /// <param name="importing">The empty state's button is busy: Import recipe… or Try again (P25).</param>
```

- [ ] **Step 4: Implement the anchor**

In `src/Board/BoardEdits.cs`, add `using Labs626.UrScore.Recipes;` under `using Labs626.UrScore.Core;`, and replace `AnchorSourceId` (with its summary) with:

```csharp
    /// <summary>
    /// The source the top bar's period line follows (P26): a panel's own source that is on and isn't a group list; else a source
    /// that is on of a panel's recipe (My accounts and Records name only a recipe); else the main; else a panel's group-list source.
    /// </summary>
    public static string? AnchorSourceId(BoardDef board, IReadOnlyList<Source> sources, IReadOnlyList<InstalledRecipe> installed)
    {
        var enabled = sources.Where(s => s.Enabled).ToList();
        bool IsList(Source source) => installed.Any(i => string.Equals(i.Recipe.Slug, source.Recipe, StringComparison.Ordinal) && i.Recipe.IsGroupList);

        var named = board.Panels
            .SelectMany(panel => new[] { panel.Settings.SourceId, panel.Settings.ToSourceId }.Concat(panel.Settings.SourceIds ?? Array.Empty<string>()))
            .Select(id => enabled.FirstOrDefault(s => s.Id == id))
            .OfType<Source>()
            .ToList();
        if (named.FirstOrDefault(s => !IsList(s)) is { } own) return own.Id;

        foreach (var panel in board.Panels)
        {
            if (enabled.FirstOrDefault(s => s.Recipe == panel.Settings.Recipe && !IsList(s)) is { } ofRecipe) return ofRecipe.Id;
        }

        return (enabled.FirstOrDefault(s => s.Role == SourceRole.Main) ?? named.FirstOrDefault())?.Id;
    }
```

- [ ] **Step 5: Implement the icon rule**

In `src/Composition/AppServices.cs`, replace `LoadInstalled` with:

```csharp
    private void LoadInstalled()
    {
        var load = Store.LoadAll();
        Installed = load.Recipes;
        RecipeProblems = [.. load.Problems.Select(p => Redactor.Redact(p))];
        foreach (var problem in RecipeProblems) AddTrail($"RECIPE FILE SKIPPED: {problem}");

        // A recipe that is gone, or updated to one that names no icon, no longer sets the window's icon (P27). Every path that
        // reloads recipes then applies sources, whose RaiseIconIfChanged puts Ur Score's own icon back.
        foreach (var slug in IconsToForget([.. _iconFiles.Keys, .. _iconTexts.Keys], Installed).Distinct(StringComparer.Ordinal))
        {
            _iconFiles.Remove(slug);
            _iconTexts.Remove(slug);
        }
    }

    /// <summary>Recipes whose fetched icon no longer applies: not installed, or installed with no <c>icon</c> (P27).</summary>
    internal static IReadOnlyList<string> IconsToForget(IEnumerable<string> slugsWithIcons, IReadOnlyList<InstalledRecipe> installed) =>
        [.. slugsWithIcons.Where(slug => installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, slug, StringComparison.Ordinal))?.Recipe.Icon is null)];
```

- [ ] **Step 6: Run the tests to see them pass**

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
```

Expected: the test build now fails only in `src/UI/BoardWindow.xaml.cs` with `CS7036` for the two-argument `AnchorSourceId` call. Go on to Step 7 before running tests.

- [ ] **Step 7: Wire the board window**

In `src/UI/BoardWindow.xaml.cs`:

Add a field after `_importing`:

```csharp
    /// <summary>The book's empty state's Try again is reading the score book.</summary>
    private bool _retryingBook;
```

Replace `OnLoaded` with:

```csharp
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _services.StartFollowingTheme();
        _clock.Start();
        await ReadBookAsync();
    }

    /// <summary>
    /// Reads the score book. When it can't be read the board says why, on the detail line and as its empty state with Try again
    /// (P25), and nothing that needs the book runs: Start and Test now stay off and no pop-out opens. The trail gets the type only.
    /// </summary>
    private async Task ReadBookAsync()
    {
        try
        {
            await _services.LoadBookAsync();
        }
        catch (Exception ex)
        {
            _bookProblem = _services.Redactor.Redact(ex.Message);
            _services.AddTrail($"BOOK NOT LOADED: {ex.GetType().Name}");
            Render();
            StateLine.Text = "Your score book could not be read.";
            DetailLine.Text = _bookProblem;
            return;
        }

        _bookProblem = null;
        ApplyButtons();
        Render();

        // Spec §7.1: a recipe with inputs and no sources opens Setup on its Clans page.
        if (SetupPages.FirstRunPage(_services.Installed, _services.Sources) is { } page) OpenSetup(page);
    }
```

In `RenderBoard`, replace:

```csharp
        // Only once shown: the constructor's first draw must not open windows ahead of the board.
        if (IsLoaded) SyncPopOuts();

        _anchorSourceId = BoardEdits.AnchorSourceId(board, _services.Sources);
```

with:

```csharp
        // Only once shown, and only once the score book is read: the constructor's first draw must not open windows ahead of the
        // board, and a pop-out has nothing to show before the book (P25).
        if (IsLoaded && _services.ReaderLoaded) SyncPopOuts();

        _anchorSourceId = BoardEdits.AnchorSourceId(board, _services.Sources, _services.Installed);
```

In `RenderLinesCore`, replace `StateLine.Text = BoardText.StateLine(live, _services.EverStarted);` with:

```csharp
        StateLine.Text = BoardText.StateLine(live, _services.EverStarted, _starting, _testing);
```

In `RenderEmpty`, replace `_empty = BoardText.EmptyFor(starter, _services.BoardsFollowStarter && !Editing, board);` with:

```csharp
        _empty = BoardText.EmptyFor(starter, _services.BoardsFollowStarter && !Editing, board, bookUnread: _bookProblem is not null);
```

In `OnStartStopClick`, replace:

```csharp
        _starting = true;
        ApplyButtons();

        try
        {
            await _services.StartAsync();
```

with:

```csharp
        _starting = true;

        // The state line says Start is waiting on RoRoRo, for as long as it waits (P24).
        RenderLines();

        try
        {
            await _services.StartAsync();
```

In `OnTestNowClick`, replace:

```csharp
        _testing = true;
        ApplyButtons();
        StateLine.Text = "Reading every source once…";
```

with:

```csharp
        _testing = true;
        RenderLines();
```

In `ButtonStates`, replace `_importing, boards.Count` with `_importing || _retryingBook, boards.Count`.

In `OnEmptyStateClick`, replace:

```csharp
        if (_importing) return;

        if (_empty == BoardEmpty.NoPanels)
```

with:

```csharp
        if (_importing || _retryingBook) return;

        if (_empty == BoardEmpty.BookNotRead)
        {
            _retryingBook = true;
            ApplyButtons();
            StateLine.Text = "Reading your score book…";
            try
            {
                await ReadBookAsync();
            }
            finally
            {
                _retryingBook = false;
                ApplyButtons();
            }

            return;
        }

        if (_empty == BoardEmpty.NoPanels)
```

- [ ] **Step 8: Run the tests to see them pass**

```bash
dotnet build Ur-Score.csproj -c Release -warnaserror
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~BoardTextTests|FullyQualifiedName~BoardEditsTests|FullyQualifiedName~AppServicesIconTests|FullyQualifiedName~BoardButtonsTests"
```

Expected: both builds succeed and all PASS.

- [ ] **Step 9: The walks**

In `tools/smoke/walk-starter-board.ps1`, replace:

```powershell
    $stopped = Wait-Line $board 'StateLine' '^Stopped\.$' 20
    Check '4 Stop stops' ($stopped -eq 'Stopped.') $stopped
```

with:

```powershell
    # Stopped, the line still gives the last read's news after "Stopped." (P24).
    $stopped = Wait-Line $board 'StateLine' '^Stopped\.' 20
    Check '4 Stop stops' ($stopped -like 'Stopped.*') $stopped
```

Create `tools/smoke/walk-book-unreadable.ps1`:

```powershell
# A score book that can't be read at start (P25): the board says so with Try again, Start and Test now stay off, a saved
# pop-out waits for the book, and once the folder can be read again Try again loads it without a restart.
# Denies you list access to a fresh scorebook folder with icacls, and takes the deny off in a finally.
param()

. (Join-Path $PSScriptRoot 'uia-board.ps1')
$ErrorActionPreference = 'Stop'
$backup = $null
$book = Join-Path $UrData 'scorebook'
$deny = "$($env:USERNAME):(RD)"

try {
    $backup = Move-UrDataAside
    New-Item -ItemType Directory -Force $book | Out-Null
    # One board with one popped-out panel, so the walk can see a pop-out wait for the book.
    $boards = '[{"id":"b-0000walk","name":"Walk","panels":[{"id":"p-0000walk","type":"standing","size":{"span":3,"tall":false},"order":0,' +
        '"settings":{"recipe":"no-such-recipe","sourceId":"s-0000walk"},"popout":{"x":120,"y":120,"w":360,"h":300}}]}]'
    Set-Content -Path (Join-Path $UrData 'boards.json') -Value $boards -Encoding ASCII
    & icacls $book /deny $deny | Out-Null

    Start-Process -FilePath $UrExe -WorkingDirectory (Split-Path $UrExe) | Out-Null
    $board = Wait-UrWindow '^RoRoRo Ur Score$' 60
    if (-not $board) { throw 'the board window never appeared' }

    $line = Wait-Line $board 'EmptyStateLine' "^Your score book couldn't be read$" 30
    Check '1 The board says the score book could not be read' ($line -eq "Your score book couldn't be read") $line
    $start = Find-ByAutomationId $board 'StartStopButton'
    $test = Find-ByAutomationId $board 'TestNowButton'
    Check '1b Start and Test now stay off' ((-not $start.Current.IsEnabled) -and (-not $test.Current.IsEnabled)) "start=$($start.Current.IsEnabled) test=$($test.Current.IsEnabled)"
    $retry = Find-ByAutomationId $board 'EmptyStateButton'
    Check '1c It offers Try again' ($retry.Current.Name -eq 'Try again') $retry.Current.Name
    Start-Sleep -Seconds 2
    Check '1d No pop-out opens before the book is read' ((Get-PopOutWindows).Count -eq 0) "count=$((Get-PopOutWindows).Count)"

    & icacls $book /remove:d $env:USERNAME | Out-Null
    Invoke-Element $retry
    $ready = Wait-Until { (Find-ByAutomationId (Get-BoardWindow) 'StartStopButton').Current.IsEnabled } 30
    Check '2 Try again reads it once the folder can be read' $ready "StartStopButton enabled=$ready"
    Check '2b The board shows its own empty state again' ((Line (Get-BoardWindow) 'EmptyStateLine') -eq 'Import a recipe to start') (Line (Get-BoardWindow) 'EmptyStateLine')
    $opened = Wait-Until { (Get-PopOutWindows).Count -eq 1 } 15
    Check '2c The saved pop-out opens once the book is read' $opened "count=$((Get-PopOutWindows).Count)"
}
finally {
    if (Test-Path $book) { & icacls $book /remove:d $env:USERNAME | Out-Null }
    if ($null -ne $backup) { Restore-UrData $backup }
    Show-Results
}
exit $LASTEXITCODE
```

In `tools/smoke/README.md`, add this row to the table under "The scripts", after the `walk-score-book.ps1` row:

```markdown
| `walk-book-unreadable.ps1` | A score book that can't be read: the board says so with Try again, Start and Test now stay off, a saved pop-out waits, and Try again loads it once the folder can be read (it denies you list access to a fresh `scorebook` folder with `icacls`, and lifts the deny in a `finally`) |
```

Run the parse check (Task 1 Step 11), then the ASCII check:

```powershell
Get-ChildItem tools/smoke -Filter *.ps1 | Where-Object { [System.IO.File]::ReadAllBytes($_.FullName) | Where-Object { $_ -gt 127 } | Select-Object -First 1 }
```

Expected: `0 parse error(s)` for every file, and no output from the ASCII check.

- [ ] **Step 10: Run the full gate**

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build
```

Expected: both pass. Walks for the controller:
- `walk-book-unreadable.ps1` (all steps);
- `walk-starter-board.ps1` step 4 and step 2 (Start's line changes from "Starting: …" to "Reading …");
- `window-smoke.ps1` step 7;
- by hand: import a copy of the clan fixture with its `"icon"` line deleted as an update; the window's and taskbar's icon go back to Ur Score's.

- [ ] **Step 11: Commit**

```bash
git add src/Board/StarterBoards.cs src/UI/BoardText.cs src/Board/BoardEdits.cs src/UI/BoardButtons.cs src/UI/BoardWindow.xaml.cs src/Composition/AppServices.cs tests/BoardTextTests.cs tests/BoardEditsTests.cs tests/AppServicesIconTests.cs tools/smoke/walk-starter-board.ps1 tools/smoke/walk-book-unreadable.ps1 tools/smoke/README.md
git commit -m "board: say what Start and Test now wait for, try an unreadable book again, anchor on the board's own source, drop a gone icon

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 8: Pop-outs closed from outside

S2-8.2. Ruling P28.

This change lives in two windows and has no pure decision worth a class of its own (return now, or wait and return unless Ur Score is closing). Its test is a smoke step that reproduces the bug with `taskkill`, which is what an updater's close does; write it first and watch it fail.

**Files:**
- Modify: `src/UI/Boards/PanelPopOutWindow.xaml.cs` (`ClosedByYou`, the system Close hook)
- Modify: `src/UI/BoardWindow.PopOuts.cs` (`OnPopOutClosed`, `SyncPopOuts`)
- Walk: `tools/smoke/walk-pop-outs.ps1`

**Interfaces:**
- Consumes: `BoardWindow.ReturnPanel`, `_closingApp`, `_popOuts`, `Get-UrProcessId`, `Read-Boards`, `Get-PopOutWindows`, `Start-UrScore`.
- Produces: `PanelPopOutWindow.ClosedByYou` (`bool`, read-only).

- [ ] **Step 1: Write the failing walk step**

In `tools/smoke/walk-pop-outs.ps1`, after the line that records `'5b ...where they were'`, add:

```powershell
    # 5c. A close sent from outside to every window (taskkill without /f, as an updater does) keeps both for the next start (P28).
    & taskkill /pid (Get-UrProcessId) | Out-Null
    $gone = Wait-Until { -not (Get-UrProcessId) } 20
    $kept = @(@(Read-Boards) | ForEach-Object { @($_.panels) } | Where-Object { $_ -and $null -ne $_.popout }).Count
    Check '5c An outside close keeps both pop-outs' ($gone -and $kept -eq 2) "exited=$gone popouts saved=$kept"
    Start-UrScore | Out-Null
    Wait-Until { (Get-PopOutWindows).Count -eq 2 } 30 | Out-Null
    Check '5d ...and both reopen at start' ((Get-PopOutWindows).Count -eq 2) "count=$((Get-PopOutWindows).Count)"
```

Run the parse check (Task 1 Step 11). Expected: `0 parse error(s)`.

- [ ] **Step 2: Run it to see it fail**

Close Ur Score. Run:

```bash
dotnet build Ur-Score.csproj -c Release -warnaserror
```

```powershell
powershell -ExecutionPolicy Bypass -File tools/smoke/walk-pop-outs.ps1 -Main CCGP
```

Expected: `5c An outside close keeps both pop-outs` FAILS with `popouts saved=0` or `1` (taskkill reaches the always-on-top pop-outs first, and each returns its panel before the board closes), and `5d` fails with them.

- [ ] **Step 3: Tell a close you asked for from one sent from outside**

In `src/UI/Boards/PanelPopOutWindow.xaml.cs`, replace:

```csharp
        SourceInitialized += (_, _) => DropStateBoxes();
```

with:

```csharp
        SourceInitialized += (_, _) =>
        {
            DropStateBoxes();
            if (PresentationSource.FromVisual(this) is HwndSource source) source.AddHook(WatchSystemClose);
        };
```

add after the `Moved` event:

```csharp
    /// <summary>
    /// You closed this window: its ✕, Alt+F4, or the taskbar's Close window. A close sent from outside Ur Score (an updater,
    /// taskkill without /f) is none of these, and the board then waits to see whether Ur Score itself is closing (P28).
    /// </summary>
    public bool ClosedByYou { get; private set; }
```

replace `OnReturnClick` with:

```csharp
    private void OnReturnClick(object sender, RoutedEventArgs e)
    {
        ClosedByYou = true;
        Close();
    }
```

and add after `DropStateBoxes`:

```csharp
    private const int SystemCommand = 0x0112;
    private const int CloseCommand = 0xF060;

    /// <summary>Alt+F4 and the taskbar's Close window arrive as the system Close command before WM_CLOSE; a WM_CLOSE sent from outside doesn't.</summary>
    private IntPtr WatchSystemClose(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == SystemCommand && (wParam.ToInt64() & 0xFFF0) == CloseCommand) ClosedByYou = true;
        return IntPtr.Zero;
    }
```

- [ ] **Step 4: Wait on a close from outside**

In `src/UI/BoardWindow.PopOuts.cs`, add after `_closingApp`:

```csharp
    /// <summary>How long a pop-out closed from outside waits for Ur Score itself to close before its panel returns (P28).</summary>
    private static readonly TimeSpan OutsideCloseWait = TimeSpan.FromSeconds(2);

    /// <summary>Pop-outs closed from outside and still waiting: <see cref="SyncPopOuts"/> doesn't reopen them meanwhile.</summary>
    private readonly HashSet<string> _outsideCloses = new(StringComparer.Ordinal);
```

Replace `OnPopOutClosed` with:

```csharp
    private void OnPopOutClosed(object? sender, EventArgs e)
    {
        if (sender is not PanelPopOutWindow window) return;

        window.Moved -= OnPopOutMoved;
        window.Closed -= OnPopOutClosed;
        _lastPopOut[window.PanelId] = window.Rect;

        // A window the board closed itself (its panel came back, or went) is no longer tracked, and returns nothing.
        if (!_popOuts.TryGetValue(window.PanelId, out var current) || !ReferenceEquals(current, window)) return;

        _popOuts.Remove(window.PanelId);
        if (_closingApp) return;

        if (window.ClosedByYou)
        {
            ReturnPanel(window.PanelId);
            return;
        }

        // Closed from outside: an updater or taskkill closes every window, the board a moment later, and its Closing keeps every
        // popout for the next start. Still open once the wait is over, the close was this window's alone, and its panel returns.
        var panelId = window.PanelId;
        _outsideCloses.Add(panelId);
        var wait = new DispatcherTimer { Interval = OutsideCloseWait };
        wait.Tick += (_, _) =>
        {
            wait.Stop();
            _outsideCloses.Remove(panelId);
            if (!_closingApp) ReturnPanel(panelId);
        };
        wait.Start();
    }
```

In `SyncPopOuts`, replace:

```csharp
            if (_popOuts.ContainsKey(id) || def.PopOut is not { } rect) continue;
```

with:

```csharp
            if (_popOuts.ContainsKey(id) || _outsideCloses.Contains(id) || def.PopOut is not { } rect) continue;
```

- [ ] **Step 5: Run the walk to see it pass**

Close Ur Score, then:

```bash
dotnet build Ur-Score.csproj -c Release -warnaserror
```

```powershell
powershell -ExecutionPolicy Bypass -File tools/smoke/walk-pop-outs.ps1 -Main CCGP
```

Expected: every step PASSES (step 3 may show SKIP, needs a live battle), `5c` and `5d` included; step 6 (✕ returns the panel) and step 7 (Bring back) still pass.

- [ ] **Step 6: Run the full gate**

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build
```

Expected: both pass. By hand for the controller: Alt+F4 on a pop-out returns its panel at once.

- [ ] **Step 7: Commit**

```bash
git add src/UI/Boards/PanelPopOutWindow.xaml.cs src/UI/BoardWindow.PopOuts.cs tools/smoke/walk-pop-outs.ps1
git commit -m "pop-outs: a close sent from outside keeps them for the next start

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 9: Accounts two sources both saw

S1-6.8, S1-F.1. Ruling P29.

**Files:**
- Modify: `src/Core/AccountClaims.cs` (a `TryClaim` that reports the owner)
- Modify: `src/Core/RecipeWatch.cs` (`RecipeSnapshot.KeptElsewhere`, `NotRecordingKeptElsewhere`, `OwnedMap`, `Record`)
- Modify: `src/UI/Setup/DiagnosticsModel.cs` (`SourceDiagnostic.KeptElsewhere`, `Sources`, add `KeptElsewhere`, `CopyText`)
- Modify: `src/UI/Setup/DiagnosticsPage.xaml`
- Modify: `src/UI/Setup/ScoreBookModel.cs` (`NotRecording`)
- Test: `tests/RecipeWatchBookTests.cs`, `tests/DiagnosticsModelTests.cs`, `tests/ScoreBookModelTests.cs`

**Interfaces:**
- Consumes: `RecipeSnapshot.SentThisRead` and the `Kept` local function as Task 2 left them; `ClansModel.NameOf`, `PanelText.SourceLabel`.
- Produces: `AccountClaims.TryClaim(string, long, string, TimeSpan, out string owner)`, `RecipeSnapshot.KeptElsewhere` (your Roblox user id to the id of the source that kept it; never null), `RecipeWatch.NotRecordingKeptElsewhere`, `SourceDiagnostic.KeptElsewhere` and `HasKeptElsewhere`, `DiagnosticsModel.KeptElsewhere(Recipe?, RecipeSnapshot?, IReadOnlyList<Source>, IReadOnlyList<HostAccount>)`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/RecipeWatchBookTests.cs`, after `TwoSourcesThatSeeOneAccountKeepAndSendItOnce`:

```csharp
    [Fact]
    public async Task AnAccountAnotherSourceReadFirstNamesThatSourceAndTheReasonSaysSo()
    {
        var claims = new AccountClaims(TimeProvider.System);
        var host = new StubHost(true, AltAccount);
        var book = new MemoryBook();
        // No headline: with nothing of its own to keep, the second source's read keeps no line at all.
        var engine = new StubEngine(() => new RecipeReading(ReadingOutcome.Read, null, [EngineRow(111, 4200)], [], "battle=B", 1));

        await Watch(engine, host, book, SourceOf(SourceRole.Main, "s-00000001"), claims: claims).RunOnceAsync(CancellationToken.None);
        var second = await Watch(engine, host, book, SourceOf(SourceRole.Mine, "s-00000002"), claims: claims).RunOnceAsync(CancellationToken.None);

        var kept = Assert.Single(second.KeptElsewhere);
        Assert.Equal((111L, "s-00000001"), (kept.Key, kept.Value));
        Assert.False(second.Recorded);
        Assert.Equal(RecipeWatch.NotRecordingKeptElsewhere, second.NotRecordingReason);
    }
```

Add to `tests/DiagnosticsModelTests.cs`, inside the class:

```csharp
    [Fact]
    public void AnAccountAnotherSourceKeptIsNamedWithThatSource()
    {
        Source[] sources = [ForClan, new("s-00000002", Clan.Slug, new Dictionary<string, string> { ["clan"] = "K0i2" }, SourceRole.Mine)];
        var snapshot = new RecipeSnapshot(WatchState.NoMatches, null, [], [], 2)
        {
            // 999 isn't one of your accounts: a watch never puts one there, and it is never named if it were.
            KeptElsewhere = new Dictionary<long, string> { [Main.RobloxUserId] = ForClan.Id, [999] = ForClan.Id },
        };
        var latest = new Dictionary<string, RecipeSnapshot> { ["s-00000002"] = snapshot };

        var row = DiagnosticsModel.Sources([Installed], sources, latest, _ => Now, running: true, [Main], Now, new Redactor(() => []))
            .Single(r => r.SourceId == "s-00000002");

        Assert.Equal($"{Main.DisplayName}: kept under ★ CCGP this time, which read it first.", row.KeptElsewhere);
        Assert.True(row.HasKeptElsewhere);
        Assert.DoesNotContain("999", row.KeptElsewhere);
    }
```

Add to `tests/ScoreBookModelTests.cs`, inside the class:

```csharp
    [Fact]
    public void ASourceWhoseAccountsAnotherReadFirstNamesThatSource()
    {
        Source[] sources = [ClanSource("s-00000001", "CCGP"), ClanSource("s-00000002", "K0i2") with { Role = SourceRole.Mine }];
        var latest = new Dictionary<string, RecipeSnapshot>
        {
            ["s-00000001"] = Snapshot(true, null),
            ["s-00000002"] = Snapshot(false, "Your accounts in this read are kept by another source of this recipe, which read them first.") with
            {
                SourceId = "s-00000002",
                KeptElsewhere = new Dictionary<long, string> { [111] = "s-00000001" },
            },
        };

        var item = Assert.Single(ScoreBookModel.NotRecording([Installed], sources, latest, running: true, accountsEverListed: true));

        Assert.Equal("Your accounts in this read are kept under ★ CCGP this time, which read them first.", item.Reason);
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: the build fails with `CS1061`/`CS0117` naming `KeptElsewhere` on `RecipeSnapshot` and `SourceDiagnostic`, and `CS0117` for `NotRecordingKeptElsewhere`.

- [ ] **Step 3: Implement the claim's owner**

Replace the body of `AccountClaims` (in `src/Core/AccountClaims.cs`) after its fields with:

```csharp
    public bool TryClaim(string recipe, long userId, string sourceId, TimeSpan window) => TryClaim(recipe, userId, sourceId, window, out _);

    /// <param name="owner">Who holds the claim after this call: <paramref name="sourceId"/> when it returns true, else the source that read the account first.</param>
    public bool TryClaim(string recipe, long userId, string sourceId, TimeSpan window, out string owner)
    {
        var now = time.GetUtcNow();
        lock (_gate)
        {
            if (_claims.TryGetValue((recipe, userId), out var claim) && claim.SourceId != sourceId && now - claim.At < window)
            {
                owner = claim.SourceId;
                return false;
            }

            _claims[(recipe, userId)] = (sourceId, now);
            owner = sourceId;
            return true;
        }
    }
```

- [ ] **Step 4: Implement it in the watch**

In `src/Core/RecipeWatch.cs`, inside `RecipeSnapshot`, add after `CarriedFrom`:

```csharp
    /// <summary>
    /// Your accounts this read saw that another source of the same recipe keeps and sends this time (Ruling R6, P29), by Roblox
    /// user id, with that source's id. Your own ids only; empty when nothing was claimed elsewhere.
    /// </summary>
    public IReadOnlyDictionary<long, string> KeptElsewhere { get; init; } = new Dictionary<long, string>();
```

In `RecipeWatch`, add after `NotRecordingNoAccounts`:

```csharp
    internal const string NotRecordingKeptElsewhere = "Your accounts in this read are kept by another source of this recipe, which read them first.";
```

In `RunOnceCoreAsync`, replace:

```csharp
        var owned = OwnedMap(readRecipe, readSource, reading, map);

        var (recorded, notRecording) = Record(readRecipe, readInputs, readText, readTracked, readSource, trigger, reading, owned);

        // What this read sent, so the board's sent dot means this read and not an earlier one (P9).
        var sentNow = new HashSet<(Guid AccountId, string Stat)>();
        RecipeSnapshot Kept(RecipeSnapshot snapshot) => snapshot with { Recorded = recorded, NotRecordingReason = notRecording, SentThisRead = sentNow };
```

with:

```csharp
        var (owned, keptElsewhere) = OwnedMap(readRecipe, readSource, reading, map);

        var (recorded, notRecording) = Record(readRecipe, readInputs, readText, readTracked, readSource, trigger, reading, owned, keptElsewhere.Count > 0);

        // What this read sent, so the board's sent dot means this read and not an earlier one (P9).
        var sentNow = new HashSet<(Guid AccountId, string Stat)>();
        RecipeSnapshot Kept(RecipeSnapshot snapshot) =>
            snapshot with { Recorded = recorded, NotRecordingReason = notRecording, SentThisRead = sentNow, KeptElsewhere = keptElsewhere };
```

Replace `OwnedMap` with:

```csharp
    /// <summary>
    /// Ruling R6. Accounts not in this read's rows (a private profile) need no claim. Also returns, by user id, the source that keeps
    /// each of your accounts another source of this recipe claimed first (P29).
    /// </summary>
    private (IReadOnlyDictionary<long, Guid> Owned, IReadOnlyDictionary<long, string> Elsewhere) OwnedMap(
        Recipe readRecipe, Source? readSource, RecipeReading reading, IReadOnlyDictionary<long, Guid> map)
    {
        var elsewhere = new Dictionary<long, string>();
        if (claims is null || readSource is null || map.Count == 0) return (map, elsewhere);

        var window = TimeSpan.FromSeconds(readRecipe.EffectiveEverySeconds * 2);
        var inRows = reading.Rows.Select(r => r.UserId).ToHashSet();
        var owned = new Dictionary<long, Guid>();
        foreach (var (userId, subject) in map)
        {
            if (!inRows.Contains(userId))
            {
                owned[userId] = subject;
            }
            else if (claims.TryClaim(readRecipe.Slug, userId, readSource.Id, window, out var owner))
            {
                owned[userId] = subject;
            }
            else
            {
                elsewhere[userId] = owner;
            }
        }

        return (owned, elsewhere);
    }
```

In `Record`, add a last parameter and use it; replace the signature's last line and the "no line" return:

```csharp
        RecipeReading reading, IReadOnlyDictionary<long, Guid> owned, bool keptElsewhere)
```

```csharp
        if (line is null) return (false, keptElsewhere ? NotRecordingKeptElsewhere : NotRecordingNoAccounts);
```

- [ ] **Step 5: Implement Setup**

In `src/UI/Setup/DiagnosticsModel.cs`, add `using Labs626.UrScore.Board;` above `using Labs626.UrScore.Core;`, and replace the `SourceDiagnostic` record with:

```csharp
public sealed record SourceDiagnostic(string SourceId, string Name, string State, string Detail, string LastRead, string NextRead, string Misses, string KeptElsewhere = "")
{
    public string Timing => $"Last read {LastRead} · next read {NextRead}";

    public bool HasDetail => Detail.Length > 0;

    public bool HasMisses => Misses.Length > 0;

    public bool HasKeptElsewhere => KeptElsewhere.Length > 0;
}
```

In `Sources`, replace the `rows.Add(new SourceDiagnostic(` … `));` statement with:

```csharp
            rows.Add(new SourceDiagnostic(
                source.Id,
                recipe is null ? $"{source.Recipe} (not installed)" : ScoreBookModel.SourceLabel(recipe, source),
                state,
                redactor.Redact(snapshot?.Detail),
                last is null ? "never" : $"{StatText.Span(now - last.Value)} ago",
                next,
                redactor.Redact(Misses(recipe, snapshot, accounts)),
                redactor.Redact(KeptElsewhere(recipe, snapshot, sources, accounts))));
```

Add after `Misses`:

```csharp
    /// <summary>
    /// Which of your accounts another source of the recipe kept this time, by display name, and under which source (spec §4.2:
    /// "Diagnostics notes it"; P29). Only your own accounts are named.
    /// </summary>
    public static string KeptElsewhere(Recipe? recipe, RecipeSnapshot? snapshot, IReadOnlyList<Source> sources, IReadOnlyList<HostAccount> accounts)
    {
        if (snapshot is null || snapshot.KeptElsewhere.Count == 0) return "";

        var lines = new List<string>();
        foreach (var owner in snapshot.KeptElsewhere.GroupBy(kv => kv.Value, StringComparer.Ordinal))
        {
            var names = owner
                .Select(kv => accounts.FirstOrDefault(a => a.RobloxUserId == kv.Key && kv.Key != 0)?.DisplayName)
                .OfType<string>()
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (names.Count == 0) continue;

            var under = sources.FirstOrDefault(s => s.Id == owner.Key) is { } source && recipe is not null
                ? PanelText.SourceLabel(ClansModel.NameOf(recipe, source), source.Role)
                : "another source";
            lines.Add($"{string.Join(", ", names)}: kept under {under} this time, which read {(names.Count == 1 ? "it" : "them")} first.");
        }

        return string.Join(Environment.NewLine, lines);
    }
```

In `CopyText`, after the line that appends `misses=`, add:

```csharp
            if (row.HasKeptElsewhere) text.AppendLine($"  kept elsewhere={row.KeptElsewhere.Replace(Environment.NewLine, " | ", StringComparison.Ordinal)}");
```

In `src/UI/Setup/DiagnosticsPage.xaml`, after the `Misses` `TextBlock`, add:

```xml
                            <TextBlock Text="{Binding KeptElsewhere}" Style="{StaticResource Muted}" Margin="0,4,0,0" TextWrapping="Wrap"
                                       Visibility="{Binding HasKeptElsewhere, Converter={StaticResource BoolToVisible}}" />
```

In `src/UI/Setup/ScoreBookModel.cs`, in `NotRecording`, replace:

```csharp
                : snapshot.Recorded ? null
                : snapshot.NotRecordingReason ?? "The last read kept nothing.";
```

with:

```csharp
                : snapshot.Recorded ? null
                : snapshot.KeptElsewhere.Count > 0 ? KeptUnder(recipe, sources, snapshot)
                : snapshot.NotRecordingReason ?? "The last read kept nothing.";
```

and add after `NotRecording`:

```csharp
    /// <summary>"Your accounts in this read are kept under ★ CCGP this time, which read them first." (P29). Names sources, never an account.</summary>
    private static string KeptUnder(Recipe recipe, IReadOnlyList<Source> sources, RecipeSnapshot snapshot)
    {
        var owners = snapshot.KeptElsewhere.Values
            .Distinct(StringComparer.Ordinal)
            .Select(id => sources.FirstOrDefault(s => s.Id == id))
            .OfType<Source>()
            .Select(s => PanelText.SourceLabel(ClansModel.NameOf(recipe, s), s.Role))
            .ToList();

        return owners.Count == 0
            ? RecipeWatch.NotRecordingKeptElsewhere
            : $"Your accounts in this read are kept under {string.Join(" and ", owners)} this time, which read them first.";
    }
```

- [ ] **Step 6: Run the tests to see them pass**

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~RecipeWatchBookTests|FullyQualifiedName~RecipeWatchTests|FullyQualifiedName~DiagnosticsModelTests|FullyQualifiedName~ScoreBookModelTests|FullyQualifiedName~SharedAccountsTests"
```

Expected: all PASS, `TwoSourcesThatSeeOneAccountKeepAndSendItOnce` included.

- [ ] **Step 7: Run the full gate**

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build
```

Expected: both pass. A claim conflict needs one of your accounts in two groups' rows at once and can't be forced in a walk; the controller re-runs `walk-score-book.ps1` (Setup › Score book and Diagnostics unchanged when nothing conflicts) and checks Diagnostics during the next real group switch.

- [ ] **Step 8: Commit**

```bash
git add src/Core/AccountClaims.cs src/Core/RecipeWatch.cs src/UI/Setup/DiagnosticsModel.cs src/UI/Setup/DiagnosticsPage.xaml src/UI/Setup/ScoreBookModel.cs tests/RecipeWatchBookTests.cs tests/DiagnosticsModelTests.cs tests/ScoreBookModelTests.cs
git commit -m "setup: name the accounts another source kept, and where, in Diagnostics and Score book

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 10: Setup lines that vanish or mislead

S1-12.4 with S1-14.17, S1-14.10, S1-12.12, S1-12.8, S1-F.8. Rulings P30, P31, P32, P33.

**Files:**
- Modify: `src/UI/Setup/DiagnosticsModel.cs` (`Sources`'s state)
- Modify: `src/UI/Setup/AccountsPage.xaml.cs` (`AskForAccountsAsync`)
- Modify: `src/UI/Setup/ScoreBookPage.xaml`, `src/UI/Setup/ScoreBookPage.xaml.cs`
- Modify: `src/UI/Setup/RecipesPage.xaml.cs`
- Modify: `src/UI/SetupWindow.xaml.cs` (`ShowPage`)
- Modify: `src/UI/Setup/ClansPage.xaml.cs` (add `ShowNote`)
- Modify: `src/UI/Setup/ImportFlow.cs` (add `Reload`, `SavedNotLoaded`)
- Modify: `src/UI/ImportText.cs` (`Kept`, `KeptNote`)
- Test: `tests/DiagnosticsModelTests.cs`, `tests/ImportTextTests.cs`
- Walk: `tools/smoke/window-smoke.ps1`

**Interfaces:**
- Consumes: `ISetupServices.ReloadRecipes`, `ISetupServices.AddTrail`, `RecipeWords.Period`, `Recipe.LastStep.AsOf`, `RecipePeriod.Starts`/`Ends`.
- Produces: `SetupWindow.ShowPage(string pageId, string? note = null)`, `ClansPage.ShowNote(string note)`, `ImportFlow.SavedNotLoaded(Recipe)`, the automation id `BookFolderProblemLine`.

- [ ] **Step 1: Write the failing tests**

In `tests/DiagnosticsModelTests.cs`, in `EachSourceSaysWhenItWasReadAndWhenItReadsNext`, add after `Assert.Equal("never", stopped.LastRead);`:

```csharp
        // After Stop, the last read's state isn't what Ur Score is doing now (P30).
        Assert.Equal("Not running. Last read: Reporting to RoRoRo.", stopped.State);
```

In `tests/ImportTextTests.cs`, replace `EveryHeadlineItemIsListedAsKept` and `ARecipeWithNoHeadlineKeepsOnlyTheTickedStats` with:

```csharp
    [Fact]
    public void EverythingAReadKeepsIsListedTheHeadlineAndThePeriodIncluded()
    {
        var clan = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

        Assert.Equal(new[] { "Clan place", "Clan points", "Which battle each read belongs to", "When the battle starts and ends" }, ImportText.Kept(clan).ToArray());
        Assert.Equal("Every read keeps these, and the stats you tick for your own accounts only.", ImportText.KeptNote(clan));
    }

    [Fact]
    public void ARecipeThatReadsTheSourcesOwnTimeListsIt()
    {
        var profile = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

        Assert.Equal(new[] { "When the source last updated the numbers, and whether it calls them stale" }, ImportText.Kept(profile).ToArray());
    }

    [Fact]
    public void ARecipeWithNothingButStatsKeepsOnlyTheTickedStats()
    {
        var followers = RecipeParser.Parse(RecipeParserTests.Fixture("roblox-followers.recipe.json")).Recipe!;

        Assert.Empty(ImportText.Kept(followers));
        Assert.Equal("Every read keeps the stats you tick, for your own accounts only.", ImportText.KeptNote(followers));
    }

    [Fact]
    public void ARecipeSavedButNotLoadedSaysItWasSaved()
    {
        var clan = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

        Assert.Equal($"{clan.Name} was saved, but Ur Score couldn't load it. Restart Ur Score to load it.", ImportFlow.SavedNotLoaded(clan));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: the build fails with `CS0117: 'ImportFlow' does not contain a definition for 'SavedNotLoaded'`.

Temporarily comment out `ARecipeSavedButNotLoadedSaysItWasSaved`, then:

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~DiagnosticsModelTests|FullyQualifiedName~ImportTextTests"
```

Expected: `EachSourceSaysWhenItWasReadAndWhenItReadsNext` ("Reporting to RoRoRo."), `EverythingAReadKeepsIsListedTheHeadlineAndThePeriodIncluded` and `ARecipeThatReadsTheSourcesOwnTimeListsIt` FAIL. Uncomment.

- [ ] **Step 3: Implement Diagnostics after Stop**

In `src/UI/Setup/DiagnosticsModel.cs`, in `Sources`, replace:

```csharp
            var state = !source.Enabled ? "Switched off."
                : snapshot is null ? (running ? "Waiting for its first read." : "Not started.")
                : StateText(snapshot.State);
```

with:

```csharp
            var state = !source.Enabled ? "Switched off."
                : snapshot is null ? (running ? "Waiting for its first read." : "Not started.")
                : running ? StateText(snapshot.State)
                : $"Not running. Last read: {StateText(snapshot.State)}";
```

- [ ] **Step 4: Implement the import screen's list**

In `src/UI/ImportText.cs`, replace `Kept` and `KeptNote` with:

```csharp
    /// <summary>
    /// What every successful read writes to the book besides the stats you tick (P33): the headline items, which period the read
    /// belongs to and when it runs, and the source's own time for the numbers. A group list writes nothing.
    /// </summary>
    public static IReadOnlyList<string> Kept(Recipe recipe)
    {
        if (recipe.IsGroupList) return [];

        var kept = recipe.Headline.Select(h => h.Label).ToList();
        if (recipe.Period is { } period)
        {
            var word = RecipeWords.Period(recipe);
            kept.Add($"Which {word} each read belongs to");
            var when = (period.Starts, period.Ends) switch
            {
                (not null, not null) => $"When the {word} starts and ends",
                (not null, null) => $"When the {word} starts",
                (null, not null) => $"When the {word} ends",
                _ => null,
            };
            if (when is not null) kept.Add(when);
        }

        if (recipe.LastStep.AsOf is { } asOf)
        {
            kept.Add(asOf.Stale is null
                ? "When the source last updated the numbers"
                : "When the source last updated the numbers, and whether it calls them stale");
        }

        return kept;
    }

    public static string KeptNote(Recipe recipe) =>
        recipe.IsGroupList ? "Nothing from this recipe is kept. Its rows are groups, shown live only."
        : Kept(recipe).Count == 0 ? "Every read keeps the stats you tick, for your own accounts only."
        : "Every read keeps these, and the stats you tick for your own accounts only.";
```

- [ ] **Step 5: Implement the import's reload**

In `src/UI/Setup/ImportFlow.cs`, replace:

```csharp
                services.Store.Save(recipe, text, installed.State);
                services.ReloadRecipes();
                return Outcome(services, recipe, $"Updated {recipe.Name}. {string.Join(" ", comparison.Changes)}".Trim());
```

with:

```csharp
                services.Store.Save(recipe, text, installed.State);
                return Reload(services, recipe, $"Updated {recipe.Name}. {string.Join(" ", comparison.Changes)}".Trim());
```

and:

```csharp
            services.Store.Save(recipe, text, state);
            services.ReloadRecipes();
            return Outcome(services, recipe, $"Imported {recipe.Name}.");
```

with:

```csharp
            services.Store.Save(recipe, text, state);
            return Reload(services, recipe, $"Imported {recipe.Name}.");
```

and add after `Outcome`:

```csharp
    /// <summary>
    /// The file is saved by now, and a reload that fails doesn't unsave it: the message says it was saved and what to do, never
    /// "Could not save" (P32). The trail gets the exception's type.
    /// </summary>
    private static ImportOutcome Reload(ISetupServices services, Recipe recipe, string done)
    {
        try
        {
            services.ReloadRecipes();
        }
        catch (Exception ex)
        {
            services.AddTrail($"RECIPE SAVED, NOT LOADED: {recipe.Slug} {ex.GetType().Name}");
            return new ImportOutcome(recipe.Slug, SavedNotLoaded(recipe), ChooseSources: false);
        }

        return Outcome(services, recipe, done);
    }

    public static string SavedNotLoaded(Recipe recipe) => $"{recipe.Name} was saved, but Ur Score couldn't load it. Restart Ur Score to load it.";
```

- [ ] **Step 6: Run the tests to see them pass**

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~DiagnosticsModelTests|FullyQualifiedName~ImportTextTests"
```

Expected: all PASS.

- [ ] **Step 7: Keep the pages' messages**

In `src/UI/Setup/AccountsPage.xaml.cs`, replace `AskForAccountsAsync` with:

```csharp
    private async Task AskForAccountsAsync()
    {
        _asking = true;
        ListedLine.Text = ImportFlow.AskingForAccounts;
        try
        {
            // Never throws for RoRoRo's sake: a slow or broken answer gives the saved list (AppServices.RefreshAccountsAsync), so
            // there is no "could not ask" to show, and the refresh that follows can't wipe one (P31).
            await _services.RefreshAccountsAsync(CancellationToken.None);
        }
        finally
        {
            _asking = false;
            ListedLine.Text = AccountsModel.ListedLine(_services.Accounts.Last, _services.AccountsCache.SavedAt(), DateTimeOffset.UtcNow);
        }
    }
```

In `src/UI/Setup/ScoreBookPage.xaml`, after the `Grid` that holds `BookFolderLine` and `OpenBookFolderButton`, add:

```xml
        <TextBlock x:Name="BookFolderProblemLine" Style="{StaticResource Refusal}" Margin="0,6,0,0" TextWrapping="Wrap" Visibility="Collapsed" />
```

In `src/UI/Setup/ScoreBookPage.xaml.cs`, add a field after `_services`:

```csharp
    /// <summary>Why Open folder failed, kept on its own line until the next press; the page's refresh used to write over it (P31).</summary>
    private string? _folderProblem;
```

In `Refresh`, after the `Show(BookPendingLine, …);` statement, add:

```csharp
        Show(BookFolderProblemLine, _folderProblem ?? "");
```

and replace `OnOpenFolderClick` with:

```csharp
    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_services.Book.Root);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_services.Book.Root}\"") { UseShellExecute = true });
            _folderProblem = null;
        }
        catch (Exception ex)
        {
            _folderProblem = $"Could not open the folder: {ex.Message}";
        }

        Show(BookFolderProblemLine, _folderProblem ?? "");
    }
```

In `src/UI/SetupWindow.xaml.cs`, replace `ShowPage` with:

```csharp
    /// <summary>Shows a page by id, building it fresh. A note (an import's result) goes on that page's own line, so it isn't lost with the page it came from (P31).</summary>
    public void ShowPage(string pageId, string? note = null)
    {
        _currentId = null;
        RebuildNav(pageId);
        if (note is not null && PageHost.Content is ClansPage clans) clans.ShowNote(note);
    }
```

In `src/UI/Setup/ClansPage.xaml.cs`, add after `Refresh`:

```csharp
    /// <summary>A message from the page that opened this one, under the main search, until a pick replaces it.</summary>
    public void ShowNote(string note) => Show(MainFoundLine, note);
```

In `src/UI/Setup/RecipesPage.xaml.cs`, add a field after `_importing`:

```csharp
    /// <summary>The page's last message: an import's result or a removal. A cancelled import puts it back (P31).</summary>
    private string _line = "";
```

and replace `OnImportClick` and `OnRemoveClick` with:

```csharp
    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        // One import screen at a time, however long the accounts wait lasts.
        if (_importing) return;
        _importing = true;
        ImportRecipeButton.IsEnabled = false;

        try
        {
            // While RoRoRo is asked for accounts the line says so; once it answers, the page's last message comes back.
            var outcome = await ImportFlow.RunAsync(_window, _services, text => Show(RecipesLine, text.Length == 0 ? _line : text));
            if (outcome is null)
            {
                Show(RecipesLine, _line);
                return;
            }

            _line = outcome.Message;
            Show(RecipesLine, _line);
            if (outcome.ChooseSources) _window.ShowPage(SetupPages.ClansId(outcome.Slug), outcome.Message);
        }
        finally
        {
            _importing = false;
            ImportRecipeButton.IsEnabled = true;
        }
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string slug }) return;
        if (_services.Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, slug, StringComparison.Ordinal))?.Recipe is not { } recipe) return;

        var answer = MessageBox.Show(_window, RecipesModel.ConfirmRemove(recipe), "Ur Score",
            MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;

        try
        {
            _services.RemoveRecipe(slug);
            _line = $"Removed {recipe.Name}. {RecipesModel.KeepsBook}";
        }
        catch (Exception ex)
        {
            _line = _services.Redactor.Redact($"Could not remove it: {ex.Message}");
        }

        Show(RecipesLine, _line);
    }
```

(Task 11 replaces the `MessageBox.Show` here.)

- [ ] **Step 8: Add the walk step**

In `tools/smoke/window-smoke.ps1`, after the line that records `'4 A recipe with inputs opens Setup on its Clans page'`, add:

```powershell
    Check '4b The Clans page says the import happened' ((Line $setup 'MainFoundLine') -like 'Imported *') (Line $setup 'MainFoundLine')
```

Run the parse check (Task 1 Step 11). Expected: `0 parse error(s)`.

- [ ] **Step 9: Run the full gate**

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build
```

Expected: both pass. Walks for the controller:
- `window-smoke.ps1` steps 3 (the import screen lists "Which battle each read belongs to" under KEPT IN YOUR SCORE BOOK), 4b and 8;
- `walk-score-book.ps1` step 3;
- by hand: Setup › Recipes, remove a recipe (the line says "Removed …"), press Import recipe…, pick a recipe, press Cancel on the import screen: the "Removed …" line is still there;
- Setup › Diagnostics after Stop reads "Not running. Last read: …".

- [ ] **Step 10: Commit**

```bash
git add src/UI/Setup/DiagnosticsModel.cs src/UI/Setup/AccountsPage.xaml.cs src/UI/Setup/ScoreBookPage.xaml src/UI/Setup/ScoreBookPage.xaml.cs src/UI/Setup/RecipesPage.xaml.cs src/UI/SetupWindow.xaml.cs src/UI/Setup/ClansPage.xaml.cs src/UI/Setup/ImportFlow.cs src/UI/ImportText.cs tests/DiagnosticsModelTests.cs tests/ImportTextTests.cs tools/smoke/window-smoke.ps1
git commit -m "setup: messages that stay, an honest Diagnostics after Stop, and everything a read keeps on the import screen

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 11: Themed message boxes

S1-11.4. Ruling P34.

**Files:**
- Create: `src/UI/MessageWindow.xaml`, `src/UI/MessageWindow.xaml.cs`
- Modify: `src/UI/BoardWindow.xaml.cs` (`OnDeleteBoardClick`, `SaveBoards`)
- Modify: `src/UI/Setup/AlertsPage.xaml.cs` (`OnAddRuleClick`)
- Modify: `src/UI/Setup/ClansPage.xaml.cs` (`Confirm`)
- Modify: `src/UI/Setup/ImportFlow.cs` (the save failure, `Warn`)
- Modify: `src/UI/Setup/RecipesPage.xaml.cs` (`OnRemoveClick`)
- Test: `tests/ThemeFenceTests.cs`
- Helpers and walk: `tools/smoke/uia-import.ps1`, `tools/smoke/window-smoke.ps1`, `tools/smoke/README.md`

**Interfaces:**
- Consumes: `ThemeService.Attach`, the app's `PrimaryButton` style and implicit `Button` style.
- Produces: `MessageWindow.Ask(Window? owner, string text)` returning `bool` (true only for OK), `MessageWindow.Tell(Window? owner, string text)`; automation ids `MessageWindow` (the window), `MessageText`, `MessageOkButton`, `MessageCancelButton`.

- [ ] **Step 1: Write the failing fence**

In `tests/ThemeFenceTests.cs`, add inside the class:

```csharp
    [GeneratedRegex(@"\bMessageBox\.Show\s*\(")]
    private static partial Regex StockMessageBox();

    [Fact]
    public void NoWindowOpensAStockMessageBox()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => StockMessageBox().IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"These files open a stock Windows message box, which ignores RoRoRo's theme: {string.Join(", ", offenders)}. "
            + "Use MessageWindow.Ask or MessageWindow.Tell.");
    }
```

- [ ] **Step 2: Run it to see it fail**

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ThemeFenceTests.NoWindowOpensAStockMessageBox"
```

Expected: FAIL, naming `BoardWindow.xaml.cs`, `AlertsPage.xaml.cs`, `ClansPage.xaml.cs`, `ImportFlow.cs` and `RecipesPage.xaml.cs` (in any order).

- [ ] **Step 3: Create the window**

Create `src/UI/MessageWindow.xaml`:

```xml
<Window x:Class="Labs626.UrScore.UI.MessageWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Ur Score" Width="440" SizeToContent="Height" ResizeMode="NoResize"
        WindowStartupLocation="CenterOwner" ShowInTaskbar="False"
        AutomationProperties.AutomationId="MessageWindow"
        Background="{DynamicResource BgBrush}" Foreground="{DynamicResource WhiteBrush}"
        FontFamily="{StaticResource BodyFont}">
    <StackPanel Margin="20,18,20,18">
        <TextBlock x:Name="MessageText" TextWrapping="Wrap" />
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,18,0,0">
            <Button x:Name="MessageCancelButton" Content="Cancel" Margin="0,0,8,0" Click="OnCancelClick" />
            <Button x:Name="MessageOkButton" Content="OK" Style="{StaticResource PrimaryButton}" Click="OnOkClick" />
        </StackPanel>
    </StackPanel>
</Window>
```

Create `src/UI/MessageWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Input;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

/// <summary>
/// Ur Score's own message box, in RoRoRo's theme (P34): a question with OK and Cancel, where Cancel is the default so a held Enter
/// never agrees to anything, or a notice with OK alone. Escape closes either, as no.
/// </summary>
public partial class MessageWindow : Window
{
    private MessageWindow(string text, bool asks)
    {
        InitializeComponent();
        ThemeService.Attach(this);

        MessageText.Text = text;
        MessageCancelButton.Visibility = asks ? Visibility.Visible : Visibility.Collapsed;
        MessageCancelButton.IsDefault = asks;
        MessageOkButton.IsDefault = !asks;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;

            e.Handled = true;
            Close();
        };
        Loaded += (_, _) => (asks ? MessageCancelButton : MessageOkButton).Focus();
    }

    /// <summary>OK or Cancel; true only for OK.</summary>
    public static bool Ask(Window? owner, string text) => Open(owner, text, asks: true);

    /// <summary>A notice with OK.</summary>
    public static void Tell(Window? owner, string text) => Open(owner, text, asks: false);

    private static bool Open(Window? owner, string text, bool asks)
    {
        var window = new MessageWindow(text, asks);
        if (owner is { IsLoaded: true }) window.Owner = owner;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        return window.ShowDialog() == true;
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
```

- [ ] **Step 4: Replace every stock box**

In `src/UI/BoardWindow.xaml.cs`, `OnDeleteBoardClick`, replace:

```csharp
        var answer = MessageBox.Show(this,
            $"Delete the {board.Name} board? Its panels go with it. Your score book isn't touched.",
            "Ur Score", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;
```

with:

```csharp
        if (!MessageWindow.Ask(this, $"Delete the {board.Name} board? Its panels go with it. Your score book isn't touched.")) return;
```

and in `SaveBoards` replace `MessageBox.Show(this, _boardsNote, "Ur Score", MessageBoxButton.OK, MessageBoxImage.Warning);` with:

```csharp
            MessageWindow.Tell(this, _boardsNote);
```

In `src/UI/Setup/AlertsPage.xaml.cs`, `OnAddRuleClick`, replace:

```csharp
        var answer = MessageBox.Show(owner,
            $"Add this rule to RoRoRo's metric-rules.json?\n\n{preview}\n\n"
            + "Your existing rules are kept, and the file is backed up first.",
            "Ur Score", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);

        if (answer != MessageBoxResult.OK) return;
```

with:

```csharp
        if (!MessageWindow.Ask(owner,
                $"Add this rule to RoRoRo's metric-rules.json?\n\n{preview}\n\n"
                + "Your existing rules are kept, and the file is backed up first.")) return;
```

and replace `MessageBox.Show(owner, $"Could not add the rule: {ex.Message}", "Ur Score", MessageBoxButton.OK, MessageBoxImage.Error);` with:

```csharp
            MessageWindow.Tell(owner, $"Could not add the rule: {ex.Message}");
```

In `src/UI/Setup/ClansPage.xaml.cs`, replace `Confirm` with:

```csharp
    private bool Confirm(string text) => MessageWindow.Ask(Window.GetWindow(this), text);
```

In `src/UI/Setup/ImportFlow.cs`, replace:

```csharp
            MessageBox.Show(owner, services.Redactor.Redact($"Could not save that recipe: {ex.Message}"), "Ur Score",
                MessageBoxButton.OK, MessageBoxImage.Error);
```

with:

```csharp
            MessageWindow.Tell(owner, services.Redactor.Redact($"Could not save that recipe: {ex.Message}"));
```

and replace `Warn` with:

```csharp
    private static void Warn(Window owner, string text) => MessageWindow.Tell(owner, text);
```

In `src/UI/Setup/RecipesPage.xaml.cs`, `OnRemoveClick`, replace:

```csharp
        var answer = MessageBox.Show(_window, RecipesModel.ConfirmRemove(recipe), "Ur Score",
            MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;
```

with:

```csharp
        if (!MessageWindow.Ask(_window, RecipesModel.ConfirmRemove(recipe))) return;
```

- [ ] **Step 5: Run the tests to see them pass**

```bash
dotnet build Ur-Score.csproj -c Release -warnaserror
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ThemeFenceTests"
```

Expected: both builds succeed and every fence test PASSES (`EveryBrushAWindowUsesIsOneThemeServicePaints` sees `BgBrush` and `WhiteBrush` only).

- [ ] **Step 6: Teach the smoke helpers**

In `tools/smoke/uia-import.ps1`, replace `Close-MessageBox` and `Get-MessageBoxText` (with the comment above the second) with:

```powershell
# Presses OK: on Ur Score's own message window (MessageWindow) by automation id, else on a stock Windows box by its handle.
function Close-MessageBox($w) {
    $themed = Find-ByAutomationId $w 'MessageOkButton'
    if ($themed) {
        Invoke-Element $themed
        Start-Sleep -Milliseconds 800
        return
    }
    $ok = $w.FindAll($TS::Descendants, $Cond::TrueCondition) |
        Where-Object { $_.Current.ClassName -eq 'Button' -and $_.Current.Name -eq 'OK' } | Select-Object -First 1
    [UrWin32Msg]::PostMessage([IntPtr]$ok.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 800
}

# A message's text: MessageText in Ur Score's own window; in a stock box, a Static control this UIA client names but types as a pane.
function Get-MessageBoxText($w) {
    $themed = Find-ByAutomationId $w 'MessageText'
    if ($themed) { return @($themed.Current.Name) }
    $w.FindAll($TS::Descendants, $Cond::TrueCondition) |
        Where-Object { $_.Current.ClassName -eq 'Static' -and $_.Current.Name } | ForEach-Object { $_.Current.Name }
}
```

In `tools/smoke/window-smoke.ps1`, replace:

```powershell
    $text = if ($box -and $box.Current.Name -eq 'Ur Score') { (Get-MessageBoxText $box) -join ' ' } else { "(no message box: '$($box.Current.Name)')" }
    if ($box -and $box.Current.Name -eq 'Ur Score') { Close-MessageBox $box }
```

with:

```powershell
    $text = if ($box -and $box.Current.Name -eq 'Ur Score') { (Get-MessageBoxText $box) -join ' ' } else { "(no message box: '$($box.Current.Name)')" }
    $themed = [bool]($box -and $box.Current.AutomationId -eq 'MessageWindow')
    if ($box -and $box.Current.Name -eq 'Ur Score') { Close-MessageBox $box }
    Check '2a The refusal is in Ur Score''s own message window' $themed "automation id '$(if ($box) { $box.Current.AutomationId })'"
```

In `tools/smoke/README.md`, add at the end of the "Helpers" section:

```markdown
Ur Score's message boxes are its own window (`MessageWindow`, title `Ur Score`, automation ids `MessageText`, `MessageOkButton`, `MessageCancelButton`). `Close-MessageBox` presses OK and `Get-MessageBoxText` reads the text, on it or on a stock Windows box.
```

Run the parse check and the ASCII check (Task 7 Step 9). Expected: `0 parse error(s)` and no ASCII output.

- [ ] **Step 7: Run the full gate**

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build
```

Expected: both pass. Walks for the controller:
- `window-smoke.ps1` steps 2 and 2a;
- `walk-board-editing.ps1` step 9c (Delete confirms through the new window);
- `walk-setup-clans.ps1` (a sixth clan asks, if the walk reaches it);
- `shot.ps1 -Title 'Ur Score' -OutPath artifacts/smoke/message-window.png` with the Delete question open, in RoRoRo's dark and light themes.

- [ ] **Step 8: Commit**

```bash
git add src/UI/MessageWindow.xaml src/UI/MessageWindow.xaml.cs src/UI/BoardWindow.xaml.cs src/UI/Setup/AlertsPage.xaml.cs src/UI/Setup/ClansPage.xaml.cs src/UI/Setup/ImportFlow.cs src/UI/Setup/RecipesPage.xaml.cs tests/ThemeFenceTests.cs tools/smoke/uia-import.ps1 tools/smoke/window-smoke.ps1 tools/smoke/README.md
git commit -m "ui: Ur Score's own themed message window replaces every stock message box

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 12: README and CHANGELOG

S2-F.7. Ruling P35.

**Files:**
- Modify: `README.md` (rewritten)
- Modify: `CHANGELOG.md`

**Interfaces:**
- Consumes: the tag `v0.3.0` (for its date), and what Tasks 1 to 11 changed (for 0.3.1's list).
- Produces: docs only.

- [ ] **Step 1: Get the v0.3.0 date**

Run: `git log -1 --format=%as v0.3.0`
Expected: one date, `yyyy-mm-dd`. It goes in the 0.3.0 heading in Step 3.

- [ ] **Step 2: Rewrite the README**

Replace the whole of `README.md` with:

````markdown
# RoRoRo Ur Score

> A [RoRoRo](https://github.com/estevanhernandez-stack-ed/ROROROblox) plugin. It reads your accounts' numbers from wherever a recipe says, shows them on boards you arrange, keeps every read in a score book on your PC, and hands the numbers you choose to RoRoRo so its alerts can judge them.

## What you need

- **RoRoRo 1.28 or later**, on the same PC.
- **Your accounts saved in RoRoRo.** Ur Score matches rows to your accounts by Roblox user id, which RoRoRo fills in once an account has been used.
- **A recipe for the numbers you want.** Ur Score knows no game and no data source of its own. A recipe names them, and the board credits the recipe's source.

## Install

1. In RoRoRo, open **Plugins** and choose **Install from URL**.
2. Paste `https://github.com/estevanhernandez-stack-ed/Ur-Score/releases/latest/download/`.
3. Accept the consent screen. Ur Score asks for `host.queries.accounts` (to find your accounts) and `host.metrics.report` (to hand RoRoRo your numbers). If you decline either, Ur Score says which, instead of sitting quietly broken.
4. Start Ur Score from RoRoRo.

## First run

1. **Import a recipe.** Press **Import recipe…** on the board, or use Setup › Recipes. Before anything runs, the import screen lists every host the recipe contacts and what each one receives. Nothing is ticked for you.
2. **Tick your stats.** **Show** puts a stat on your boards; **Send** also hands it to RoRoRo. Tick at least one.
3. **Pick where your accounts are.** For a recipe with inputs, Setup opens on its page. Type a few letters of your main group's name and pick it: Ur Score reads it once and names which of your accounts it found. Add the other groups your accounts are in, and any you only want to watch.
4. **Press Start.** Each source reads on its recipe's own interval. **Test now** reads every source once.

## Boards

- **The starter board.** Until you change a board, the board follows your sources: **Battle** when a recipe with a period (a battle, a season) has ticked stats, else **Grind**.
- **Tabs.** **+ Board** adds an empty board or a starter. Right-click a tab, or press Shift+F10 on it, to rename, duplicate or delete it.
- **Edit board.** Move panels (drag the handle, or use the arrow buttons), size them Small, Half or Wide, make one Tall, remove one, and add one with **+ Add panel**. **Done** saves.
- **The gallery.** Ten panels: Standing, Race, My accounts, Promotion check, Account card, Past periods, Records, Top, Profile stat and Live leaderboard. Each card says what the panel needs. Titles use the recipe's words.
- **Panel settings.** ⋯ on a panel changes what it shows. A panel whose source or stat is gone says so, with **Choose another**.
- **Pop-outs.** ⧉ opens a panel in a small window that stays on top while you play. It updates live, remembers where you put it, and comes back when Ur Score starts. Close it, or press **Bring back** on the board, to return it.

## The score book

Every successful read is kept in `%LOCALAPPDATA%\626labs.ur-score\scorebook\`, one folder per recipe and one file per month. It holds your own accounts' values and each source's headline numbers, and never another player's id, name or value. When a recipe can read a source's finished periods, Ur Score fills them in from that record.

Removing a recipe or a source never deletes its book. Setup › Score book counts what's kept, and says why a source isn't recording.

## Alerts

Ur Score sets no thresholds: RoRoRo judges. Setup › Alerts shows the rule RoRoRo needs for a stat you send, and adds it to RoRoRo's `metric-rules.json` when you click. It backs the file up first and never touches a rule that's already there.

RoRoRo's own **Metric alerts** switch has to be on too. Ur Score can't see that switch, so check it in RoRoRo's Settings.

## What leaves your machine

| Where | What goes | What doesn't |
| --- | --- | --- |
| **RoRoRo**, over a local pipe | The stats you tick Send on, for your accounts with Send on, under the name RoRoRo uses that you accepted. One gate in the code (`ReportPolicy`) checks all three. | Anything about other players. |
| **The hosts a recipe names** | What the recipe asks for: the values you picked in Setup (a group's name), your accounts' Roblox ids for a recipe that reads per account, and a key you saved for that host. The import screen lists every host and what each receives. | Anything the recipe doesn't name. A key only ever goes to the host it was saved for. |
| **users.roblox.com** | Other players' Roblox ids from a Live leaderboard panel's rows, to show their names. Only while `resolveNames` is `true`. | Your own accounts' ids: their names come from RoRoRo. |
| **thumbnails.roblox.com** | The icon id a recipe reads, to show that icon on the window. | Anything about any account. |

Requests to one host go one at a time, at least 2 seconds apart.

## What's kept on your PC

Everything is in `%LOCALAPPDATA%\626labs.ur-score\`:

| File or folder | What it holds |
| --- | --- |
| `recipes\` | The recipes you imported, and your ticks for each |
| `sources.json` | Your sources: which recipe, the values you picked, whether each is your main, yours or watched, and whether it's on |
| `accounts.json` | Your own accounts as RoRoRo last listed them, so reading goes on while RoRoRo is closed |
| `boards.json` | Your boards, their panels, and where pop-outs sit. Written at your first board change; never holds another player |
| `scorebook\` | The score book |
| `keys.dat` | Keys you saved for a recipe's host, encrypted for your Windows account |
| `settings.json` | `resolveNames` (below) |
| `icon-cache\` | Recipe icons |

A `boards.json` that can't be read is never lost: the starter board shows, and the next board change keeps a copy of the old file beside the new one.

## Settings

`settings.json` holds one setting you might change:

| Key | Default | What it does |
| --- | --- | --- |
| `resolveNames` | `true` | Whether other players' Roblox ids from a Live leaderboard's rows go to Roblox for their names. Set it to `false` and those rows show `Member <id>`, and no other player's id leaves your PC. |

## Trying a recipe

```text
626labs.ur-score.exe --try <recipe.json> [--input id=value]... [--stat key]... [--account robloxUserId]... [--json]
```

This runs the recipe once and prints what it found: the hosts contacted, the outcome, rows seen, headline values, each stat's found and missed counts with the smallest, median and largest value, and the accounts you name. It never prints another player's id or name. It opens no window and uses no RoRoRo, book or saved state. Exit codes: 0 read, 2 recipe refused, 3 input missing, 4 read stopped, 5 bad arguments.

## Troubleshooting

- **Setup › Diagnostics** shows each source's state, its last read and its next read. **Copy diagnostics** puts that on the clipboard, with keys hidden and none of your score book.
- **"RoRoRo is not running."** Ur Score keeps reading and keeping your scores. Nothing is sent until RoRoRo is back, and nothing is queued for later.
- **"RoRoRo refused …"** A consent was declined. RoRoRo has no per-capability re-grant: remove Ur Score on RoRoRo's Plugins page and install it again to be asked again.

## Build from source

You need the .NET 10 SDK.

```powershell
dotnet build Ur-Score.csproj -c Release
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build
pwsh ./build/build-plugin.ps1
```

`build-plugin.ps1` (PowerShell 7) writes `artifacts/manifest.json`, `artifacts/manifest.sha256` and `artifacts/plugin.zip`, the three files RoRoRo's installer reads, with .NET included. The UI Automation walks in `tools/smoke/` click through the real window; see [`tools/smoke/README.md`](tools/smoke/README.md).

## License

MIT © 626 Labs LLC. The contract bindings (`ROROROblox.PluginContract`) ship under the same license; see the RoRoRo repository.

---

**A 626 Labs product · *Imagine Something Else*.**
````

- [ ] **Step 3: Bring the CHANGELOG up to date**

In `CHANGELOG.md`, replace everything from `## Unreleased` up to (not including) `## 0.1.0 — 2026-09-12` with the text below. Put the date Step 1 printed after `## 0.3.0 — `.

```markdown
## Unreleased

### Fixed

- Charts whose values are all the same draw a flat line through the middle, labelled with that value, instead of an axis made up around it. Charts drawn from zero keep zero on the axis for negative values.
- Panels in one row reach the row's bottom.
- The drag handle in edit mode is drawn, so it shows whatever fonts you have, and UI Automation can find it.
- A Standing panel's change shows in magenta when it fell.
- "#rank of N" counts only the rows with a value for that stat, as Promotion check does.
- The gap to the group above shows when ranks are tied.
- The sent dot in My accounts means sent in the last read.
- A read that stops (nothing running right now, a source that didn't answer) keeps the last good numbers on the board, marked overdue once they're old, instead of blanking every panel.
- My accounts and Setup › Your accounts say "Waiting for the first read", "Not found in the … read so far", "Only in … you're watching" or "Not in any … you've added" instead of calling everyone "Not in a watched …".
- Race, Profile stat and Account card panels say what's really wrong: a recipe with no total, a removed line, a removed source, a source that's off, or an account with no reading.
- Panel settings and the gallery: the Race card no longer counts sources that are off; the Promotion check card speaks for a recipe it can be added on; a panel whose recipe is gone uses the board's word in its form; a Past periods panel whose stat is gone can drop its best account and be saved; the first-run board with nothing on it can't be duplicated into a copy that replaces it.
- Start says it's asking RoRoRo for your accounts. Stopped, the top line still gives the last read's news, a Test now's included.
- A score book that can't be read shows why, with Try again, instead of leaving Start off for the session. Pop-outs wait for it.
- The period line follows a board's own source, never a top list while the board has one.
- A recipe update that drops its icon puts Ur Score's icon back.
- Pop-outs closed from outside Ur Score (an updater, `taskkill`) are kept for the next start.
- Setup › Diagnostics names your accounts that another source kept because it read them first, and Setup › Score book names that source. After Stop, Diagnostics says "Not running".
- Setup messages stay put: an import's result follows you to the page it opens, a cancelled import keeps the last message, and a recipe that was saved but didn't load says so.
- The import screen lists everything each read keeps, the period and the source's own time included.
- Message boxes are Ur Score's own, in RoRoRo's theme.

## 0.3.0 — 

### Added

- **Boards you shape.** Several boards as tabs: **+ Board** adds an empty one or a starter (Battle or Grind), and a tab's menu renames, duplicates or deletes it.
- **Edit board.** Move panels by dragging or with the arrow buttons, size them Small, Half or Wide, make them Tall, remove them, and **Done** saves.
- **The panel gallery.** All ten panels, each card saying what it needs; add any of them, as often as you like.
- **Panel settings.** ⋯ on any panel. A panel whose source or stat is gone says so, with **Choose another**.
- **Pop-outs.** ⧉ puts a panel in a small always-on-top window that updates live, remembers where it sits, and reopens when Ur Score starts.
- **`boards.json`** keeps your boards. It never holds another player's id, name or value, and a file that can't be read is kept beside the new one instead of being lost.

## 0.2.0 — 2026-09-15

### Added

- **Recipes.** A recipe file says where a number is and how to read it; Ur Score runs it and hands your own accounts' values to RoRoRo. Ur Score itself names no game or vendor. Importing shows every host a recipe contacts and what each receives before anything runs.
- **Several sources at once.** Your main group, the groups your other accounts are in, groups you watch, and a top list, read side by side and spaced per host.
- **The score book.** Every successful read is kept on your PC, your own accounts only, and finished periods are filled in from the source's own record.
- **Setup.** Its own window: search every group name to pick your main, Your accounts, one searchable Stats table, Recipes, Alerts, Score book and Diagnostics.
- **The starter board.** The window became a scoreboard of ten panels, built from your sources.
- **`--try`**, which runs a recipe once and prints what it found without naming another player.
- **UI Automation smoke walks** in `tools/smoke/`.

### Changed

- **Ur Score paints in RoRoRo's theme.** It asks RoRoRo for its palette on connect and follows every theme switch after that, including the title bar. RoRoRo's Brand colours are used whenever RoRoRo is not running. No new capability: both theme calls are ungated.
- **Readable tables.** Cell text, headers, selection, checkboxes and scrollbars all use the theme, where the stock controls had painted black text on dark rows.
- Screen readers read the status and headline lines instead of a fixed label.

```

Then edit the `## 0.3.0 — ` heading so the date from Step 1 follows the dash, for example `## 0.3.0 — ` plus the printed date.

- [ ] **Step 4: Check the docs**

Run each:

```bash
git grep -n -i "pet sim\|biggames\|clan battle" -- README.md
git grep -n "Unreleased" -- CHANGELOG.md
git grep -n "^## 0.3.0 — [0-9]" -- CHANGELOG.md
```

Expected: the first prints nothing (the README names no game); the second prints one line; the third prints the dated 0.3.0 heading.

- [ ] **Step 5: Run the full gate**

```bash
dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build
```

Expected: both pass (no source changed).

- [ ] **Step 6: Commit**

```bash
git add README.md CHANGELOG.md
git commit -m "docs: a README for recipes, boards and pop-outs, and a CHANGELOG through 0.3.1

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 13: Release v0.3.1

Every step that merges, tags or releases waits for the owner's OK in the session. None is pre-authorized.

**Files:**
- Modify: `manifest.json` (`"version"`), `Ur-Score.csproj` (`<Version>`), `CHANGELOG.md` (the 0.3.1 heading)
- Modify: `docs/backlog.md` (wave 1 rows to FIXED, P6)
- Modify: `docs/plans/2026-09-15-backlog-remediation.md` (an execution record at the end)

**Interfaces:**
- Consumes: Tasks 0 to 12, and any wave 1 task Task 0 added.
- Produces: tag `v0.3.1`, a GitHub release with `manifest.json`, `manifest.sha256` and `plugin.zip`.

- [ ] **Step 1: Version**

Set `0.3.1` in `manifest.json` (`"version": "0.3.1"`) and `Ur-Score.csproj` (`<Version>0.3.1</Version>`). In `CHANGELOG.md`, replace `## Unreleased` with `## 0.3.1 — ` followed by the date `powershell -Command "Get-Date -Format yyyy-MM-dd"` prints.

```bash
git add manifest.json Ur-Score.csproj CHANGELOG.md
git commit -m "release: 0.3.1

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Step 2: The gates and every walk (controller)**

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
powershell -ExecutionPolicy Bypass -File tools/smoke/walk-board-editing.ps1 -Main CCGP -Alt K0i2
powershell -ExecutionPolicy Bypass -File tools/smoke/walk-pop-outs.ps1 -Main K0i2
powershell -ExecutionPolicy Bypass -File tools/smoke/walk-score-book.ps1 -Main K0i2
powershell -ExecutionPolicy Bypass -File tools/smoke/walk-book-unreadable.ps1
powershell -ExecutionPolicy Bypass -File tools/smoke/check-boards-privacy.ps1
powershell -ExecutionPolicy Bypass -File tools/smoke/check-book-privacy.ps1
```

Expected: every step passes (a SKIP that needs a live battle is not a failure), and no `626labs.ur-score.smoke-backup-*` folder is left. Then the by-hand checks each task named (Tasks 3, 7, 8, 9, 10, 11). A failure goes back to its task before anything below.

- [ ] **Step 3: Flip the backlog**

In `docs/backlog.md`, for each wave 1 item (the 37 triage rows marked W1, plus any Task 0 added), change **OPEN** to **FIXED** and put where it was fixed before the source note, the way the FIXED rows already read: "0.3.1 Task" and the task's number, then that task's commit hash, which `git log --oneline master..HEAD` lists beside each task's commit message. Update the Counts section: OPEN falls by the number flipped, FIXED rises by it. Under "Open, and you'd notice", remove the flipped lines and leave S1-9.3 with "(not reachable; wave 3, P4)".

Append to this plan a section headed `## Execution record` with the date in parentheses, as the stage 2 plan's: the commits per task, rulings made during execution, the walk results from Step 2, and anything parked for waves 2 and 3.

```bash
git add docs/backlog.md docs/plans/2026-09-15-backlog-remediation.md
git commit -m "docs: backlog wave 1 fixed in 0.3.1, and its execution record

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Step 4: The pull request (ask the owner first)**

With the owner's OK:

```bash
git push -u origin fix/backlog-wave-1
gh pr create --base master --head fix/backlog-wave-1 --title "Ur Score 0.3.1: the backlog's you'd-notice fixes" --body "$(cat <<'EOF'
Wave 1 of docs/plans/2026-09-15-backlog-remediation.md: every backlog item you'd notice, less S1-9.3 (not reachable, wave 3).

- Panels: flat charts, rows that reach their bottom, a drawn drag handle, a fall in magenta, ranks and tied gaps, a sent dot for the last read, a board that keeps its numbers through a stopped read, honest account headings and stale messages.
- Forms and the gallery: cards that open on a form they can fill, the board's words in a form, a Past periods panel that can drop its best account, no duplicate of the empty first-run board.
- The board: what Start and Test now wait for, Try again for an unreadable score book, the period line on the board's own source, a dropped icon, pop-outs kept through an outside close.
- Setup: accounts another source kept, named; Diagnostics after Stop; messages that stay; everything a read keeps on the import screen; themed message boxes.
- Docs: README and CHANGELOG.

Tests: dotnet test passes. Walks: see the plan's execution record.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Wait for the `test` workflow on the pull request to pass.

- [ ] **Step 5: Merge (ask the owner first)**

With the owner's OK, merge the pull request on GitHub (or `gh pr merge --merge`), then:

```bash
git checkout master
git pull --ff-only
```

- [ ] **Step 6: Tag and release (ask the owner first)**

With the owner's OK:

```bash
git tag -a v0.3.1 -m "Ur Score 0.3.1: the backlog's you'd-notice fixes"
git push origin v0.3.1
```

The `release` workflow checks that the tag, `manifest.json` and the csproj agree, runs the tests, builds with .NET included, and attaches `manifest.json`, `manifest.sha256` and `plugin.zip`. Confirm all three are on the release: `gh release view v0.3.1`.

- [ ] **Step 7: Install from the release and walk it**

- Back up `%LOCALAPPDATA%\ROROROblox\plugins\626labs.ur-score` to the session scratchpad and remove it. Leave `%LOCALAPPDATA%\626labs.ur-score` (your data) in place.
- In RoRoRo's Plugins page, Install from URL with `https://github.com/estevanhernandez-stack-ed/Ur-Score/releases/latest/download/` and accept the consent.
- Start the installed Ur Score from RoRoRo: your boards and pop-outs come back; RoRoRo's Plugins page names 0.3.1; a Standing change, My accounts' headings, Stop's state line, and Setup › Diagnostics after Stop read as Tasks 1, 4, 7 and 10 say.

- [ ] **Step 8: Clan post (the owner posts)**

Draft a short "what's new in 0.3.1" note for the clan, in the product's voice (builder to builder, second person, no jargon, no emoji): panels keep their numbers when a read stops, clearer account headings and stale messages, pop-outs survive an updater's close, and Try again for a score book that can't be read. Hand it to the owner.

---

# Wave 2: tests and safety nets (outline)

Its own detailed plan, `docs/plans/<date>-backlog-wave-2.md`, is written when v0.3.1 ships (P5), after re-checking these items against `master` as Task 0 does. Each task below will carry files, failing tests, code, commands and a commit there. A test added here must fail when the rule it guards is broken: each task's plan breaks the rule locally, watches the test fail, and restores it.

### W2-1: Parser, reader and panel tests

- **Items:** S1-1.1, S1-1.2, S1-9.1, S1-10.3, S1-13.12, S1-13.16.
- **Files:** `tests/RecipeParserTests.cs`, `tests/RecipeFormatTests.cs`, `tests/ScoreBookReaderTests.cs`, `tests/StatsTableModelTests.cs`, `tests/PanelModelsTests.cs`, `tests/BoardFixtures.cs`.
- **Approach:**
  - A `period.past` path with a digit segment is refused (`RecipeParser.cs` near line 716), and `RecipeInput.DefaultPlural("Your ")` is "Items".
  - `ScoreBookReader.HeadlineSeries` gets direct tests for its period filter and duplicate collapse.
  - `StatsTableModel` shows a saved counter the recipe no longer offers as a greyed row.
  - Panel tests assert literal expected values instead of recomputing them with the code's own calls; `withGap` is renamed to what it checks, `missingRank`.
  - The Today and 7-day "no earlier read" text gets an assertion, and a `FixedTime` with a non-UTC `LocalTimeZone` (`TimeZoneInfo.CreateCustomTimeZone`) proves midnight is local.
- **Tested by:** the tests themselves, each seen failing against a deliberately broken rule.

### W2-2: Sources, claims, spacing and watch recording tests

- **Items:** S1-3.1, S1-4.1, S1-6.1, S1-6.2, S1-6.3.
- **Files:** `tests/SourcesTests.cs`, `tests/ClansModelTests.cs`, `tests/SharedAccountsTests.cs`, `tests/RecipeWatchBookTests.cs`.
- **Approach:**
  - `SourceRules.MakeMain` and `ClansModel` promote a watched source straight to main.
  - Two hosts' lanes in `SpacedTransport` don't wait on each other (a `ManualTime` and a gated inner transport).
  - A recipe change during a send with a book attached keeps its line and sends nothing more (a `StubHost` whose `ReportMetricAsync` calls `UpdateRecipe`).
  - A group list on a `mine` source passes no account ids (`StubEngine.LastIds` is empty).
  - A claim expires after twice the interval (`ManualTime.Advance`).
- **Tested by:** the tests themselves.

### W2-3: Writer and source host tests

- **Items:** S1-5.3, S1-5.4, S1-5.6, S1-8.7, S1-8.8.
- **Files:** `tests/ScoreBookTests.cs`, `src/Book/ScoreBook.cs`, `tests/SourceHostTests.cs`, `src/Core/SourceHost.cs`.
- **Approach:**
  - `RemovingARecipeLeavesItsBook` goes through `RecipeStore.Remove` and `SourceRules.ForgetRecipe` before checking the book's files.
  - An internal `ScoreBook` constructor takes the line writer, so a test can throw a non-IO exception past `Drain` into `Run`'s last-resort catch, and a `flushToDisk: false` switch lets the drop-limit test run in well under a second without changing the product's durability.
  - `SourceHost` gets an internal delay function, so tests cover Stop then Start, timer-triggered reads, the interval clamp, Dispose, a throwing interval lookup, and a removal during a read gated on a `TaskCompletionSource`.
  - `Assert.All` over book lines is preceded by `Assert.NotEmpty`.
- **Tested by:** the tests themselves; `dotnet test` time for `ScoreBookTests` recorded before and after.

### W2-4: Composition, startup and fences

- **Items:** S1-14.9, S1-15.2, S2-P.11, S1-12.11.
- **Files:** `tests/AppServicesTests.cs` (new), `src/Composition/AppServices.cs`, `tests/StartupFenceTests.cs` (new), `tests/AlertsModelTests.cs`.
- **Approach:**
  - `AppServices` can't be built in a test (real paths, a gRPC client). Its decisions (`Boards` while following the starter, what `SaveBoards` writes, `WarnPastBudget`) move into internal static functions with tests, the way Task 7 did `IconsToForget`.
  - A source fence checks that `TryCommand.Wants` appears before `new Mutex(` in `App.xaml.cs`, and that `BoardsFile.Save(` appears only in `AppServices.cs`, always on a value from `BoardDefs.Sanitize`.
  - `AlertsModelTests` uses `TempDir` scopes and adds the listed-time preference, id-0 misses and watch-source inputs in the copy text.
- **Tested by:** the tests and fences, each fence seen failing on a planted violation.

### W2-5: Board tests

- **Items:** S2-P.14, S2-6.4, S2-P.16.
- **Files:** `src/Board/BoardEdits.cs`, `src/UI/Boards/AddBoardWindow.xaml.cs`, `tests/BoardEditsTests.cs`, `src/UI/BoardWindow.PopOuts.cs`, `tools/smoke/uia-board.ps1`.
- **Approach:**
  - The new-board naming rule (typed name, else the starter's, else `NextName`) moves from `AddBoardWindow.xaml.cs` into `BoardEdits.NameForNew` with tests.
  - `SetSettings` with no race list and with an empty one is asserted a no-op.
  - Pop-outs from two boards get distinct automation ids: a pure `BoardEdits.PopOutAutomationId(boards, panelId)` adds the board's position after the first board ("StandingPanel1", "StandingPanel1OnBoard2"), and `uia-board.ps1` changes in the same commit.
- **Tested by:** unit tests, and `walk-pop-outs.ps1` with a second board's pop-out added.

### W2-6: The try-out command

- **Items:** S1-15.1, S1-15.4.
- **Files:** `tests/TryCommandTests.cs`, `docs/SMOKE.md`.
- **Approach:** the `--json` test searches for all three known other ids, as the text test does. During a live battle, `--try` runs against the clan fixture with `--account` for one of your own ids; the output's shape (no other id or name, the account's rank and `of`) is recorded in `docs/SMOKE.md` with the date.
- **Tested by:** the unit test, and the recorded live run.

### W2-7: Smoke walks

- **Items:** S1-L.1, S1-L.3, S2-FR.3, S1-16.1.
- **Files:** `tools/smoke/walk-starter-board.ps1`, `tools/smoke/walk-score-book.ps1`, `tools/smoke/walk-pop-outs.ps1`, `tools/smoke/uia.ps1`, `tools/smoke/README.md`.
- **Approach:**
  - Step 2c records whether Test now went disabled, and fails if it never did.
  - `walk-score-book.ps1` waits for Test now to be enabled again instead of sleeping 20 s.
  - `walk-pop-outs.ps1` step 3 fails, not skips, when the state line names a source in trouble.
  - `Move-UrDataAside` stops before starting when a `.smoke-backup-*` folder already exists, and prints how to put it back.
- **Tested by:** the PowerShell parse check, then each walk run once normally and once with the failure it now catches planted.

### W2-8: UI Automation, accessibility and screens

- **Items:** S1-L.2, S1-L.4, S1-L.5, S2-8.6.
- **Files:** `tests/RowListAutomationTests.cs`, `tests/RowListFenceTests.cs`, `src/UI/Controls/RowList.cs`, `docs/SMOKE.md`.
- **Approach:**
  - An STA test builds the original bug's shape (an `ItemsControl` of buttons in a `DataTemplate`) and shows UI Automation can't find a row's button by name, where `RowList` can.
  - The fence learns ListBox lists (Setup's nav, search matches, the Stats rows): each is allowed only with an automation test naming its items.
  - Accessibility Insights FastPass runs on the board, a pop-out and each Setup page; only real failures are fixed, in `RowList`'s peer, and the results are recorded.
  - With a monitor at another scale, `walk-pop-outs.ps1` steps 4 and 5 run on it; without one, `docs/SMOKE.md` records mixed DPI as unverified, with `PopOutPlacement.Clamp` as the safety net.
- **Tested by:** the STA and fence tests, the FastPass report, and the recorded screen check.

---

# Wave 3: code tidiness and performance (outline)

Its own detailed plan, `docs/plans/<date>-backlog-wave-3.md`, is written when wave 2 ships (P5), re-checked against `master` first. Tidiness keeps behaviour: every task's plan runs the whole suite before and after, and a task that changes behaviour (the edges and performance) adds a test that shows the change.

### W3-1: The score book writer and reader

- **Items:** S1-5.1, S1-5.2, S1-F.4, S1-F.10, S1-7.1, S1-7.2, S1-9.5, S1-9.3.
- **Files:** `src/Book/ScoreBook.cs`, `src/Book/Finals.cs`, `src/Book/ScoreBookReader.cs`, `src/Book/Records.cs`, `src/Board/PanelModels.cs`, `src/Composition/AppServices.cs`, their tests.
- **Approach:**
  - The dropped count changes under the same lock that removes the line.
  - `Written`'s comment names both threads.
  - Recipe texts are written to a temporary file and moved into place, and a recipe file whose hash doesn't match its text is rewritten.
  - The reader and the finals index count lines they skip, and `AppServices` trails "BOOK: n lines skipped" once at load.
  - The fully qualified names use the file's alias, and "the period has ended" becomes one helper.
  - `Records.For` takes one `sourceId`, since every caller passes one, so two sources' series can never be merged (P4).
- **Tested by:** a torn recipe file is repaired; a skipped line is counted; the existing records tests pass unchanged.

### W3-2: The book at scale

- **Items:** S1-F.2, S1-F.3, S1-13.9.
- **Files:** `src/Composition/AppServices.cs`, `src/Book/Finals.cs`, `src/Book/ScoreBookReader.cs`, `src/Board/PanelModels.cs`.
- **Approach:**
  - `FinalsIndex` is built from the finals the reader loads, in the same pass, so startup reads the book once.
  - The reader keeps readings per source id, so a chart scans only its own source.
  - Past periods uses the reader's grouped `FinalEntry` list as it is.
- **Tested by:** the reader and finals tests unchanged, plus a generated book of five weeks for ten sources loaded in a test that records the load time in its output (measured, not asserted against a machine-dependent number).

### W3-3: The recipe watch and the engine

- **Items:** S1-2.1, S1-2.2, S1-6.4, S1-6.6, S1-6.7, S1-6.10, S1-6.11.
- **Files:** `src/Recipes/RecipeEngine.cs`, `src/Core/RecipeWatch.cs`, `src/Book/LineBuilder.cs`, their tests.
- **Approach:**
  - A read that stops on its last step carries no period, matching what the stage 1 report said, with a test. Task 3's `SnapshotCarry` keeps the board's period through such a read.
  - A fractional rank in a top list is unreadable, not truncated.
  - Book lines record the inputs the read used.
  - `Recipe` is read under the lock, or the lock comment says why it needn't be.
  - Report timestamps come from the injected `TimeProvider`.
  - The headline id guard also checks ids in this read's past-period rows, and its comment names the residual (spec §14).
  - The recipe hash is computed once per recipe text, in `UpdateRecipe`.
- **Tested by:** engine and watch tests per rule.

### W3-4: The source host

- **Items:** S1-8.1, S1-8.2, S1-8.3, S1-8.4, S1-8.5, S1-8.6.
- **Files:** `src/Core/SourceHost.cs`, `tests/SourceHostTests.cs`.
- **Approach:**
  - Linked cancellation sources are disposed when their loop ends.
  - `Dispose` waits up to 5 s for reads in flight, so the last read's lines reach the writer before `ScoreBook.Flush`.
  - An entry removed during Test now is skipped before its snapshot is recorded (a per-entry token cancelled on removal).
  - Off then on during a read reuses the entry instead of starting a second watch.
  - Watches are created outside the lock and inserted under it, and `Entry.Source` is read under the lock.
- **Tested by:** W2-3's host tests plus one per behaviour change, using W2-3's delay seam.

### W3-5: Sources, accounts and claims

- **Items:** S1-1.3, S1-3.2, S1-4.3, S1-14.15, S1-F.9.
- **Files:** `src/Recipes/RecipeParser.cs`, `src/Core/Sources.cs`, `src/Core/AccountClaims.cs`, `src/Recipes/SpacedTransport.cs`, `src/Core/SharedAccounts.cs`, their tests.
- **Approach:**
  - The parser's repeated group-list rule gets its comment.
  - Re-adding a switched-off source keeps it off unless the page turns it on, with a test.
  - Claims older than their window are pruned on each claim, and lanes idle for 10 minutes are dropped.
  - A `sources.json` whose existence can't be checked (access denied) loads as unreadable, never as missing, so start never migrates over it.
  - The account-list fallback catches only transport failures (`RpcException`, `IOException`, `HttpRequestException`, `TimeoutException`), and anything else trails as a bug.
- **Tested by:** unit tests per rule; the denied file uses an ACL set by the test on a temp file and lifted in `Dispose`.

### W3-6: AppServices

- **Items:** S1-12.5, S1-14.6, S1-14.7, S1-14.8, S1-14.12, S1-14.13, S1-14.14, S1-14.16, S2-5.1.
- **Files:** `src/Composition/AppServices.cs`, `src/UI/BoardWindow.xaml.cs`, `src/App.xaml.cs`, `src/UI/Setup/AlertsPage.xaml.cs`, `src/UI/Setup/ScoreBookPage.xaml.cs`, `src/UI/Setup/AccountsPage.xaml.cs`.
- **Approach:**
  - RoRoRo's rules file and the accounts cache's time are read on a worker and cached for 30 s.
  - The fire-and-forget paths (empty-state import, name resolving, icon fetch) catch and trail by type.
  - An icon fetch that threw clears its icon text, so the next read tries again.
  - `WatchInputs` separates tracked stats from recipe and inputs, so a tick change doesn't release a held key-rejected stop.
  - The icon client and closing token are disposed.
  - Policy refreshes carry a version, so a late one never replaces a newer one.
  - The remaining trail lines log exception types, not messages.
  - The dead null check goes.
  - The following starter is cached, keyed on the `Installed` and `Sources` instances.
- **Tested by:** W2-4's extracted statics grow a test per rule; the rest by the full suite and `walk-starter-board.ps1`.

### W3-7: Setup pages and app styles

- **Items:** S1-10.1, S1-10.2, S1-11.1, S1-11.2, S1-12.6, S1-12.9, S2-FR.4.
- **Files:** `src/UI/Controls/StatsTable.xaml.cs`, the Setup pages that carry `Show`, `src/UI/Setup/ClansModel.cs`, `src/UI/Setup/StatsPage.xaml.cs`, `src/UI/SetupWindow.xaml.cs`, `src/App.xaml`.
- **Approach:**
  - One `UiLines.Show(TextBlock, string)` replaces the seven copies.
  - A refused Send tick redraws once.
  - The clan search's fire-and-forget calls catch and trail.
  - The Top switch uses the group-list recipe whose period matches the page's recipe, else the first.
  - The Stats page asks for counter names once per page open.
  - An unknown Setup page id falls back to `SetupPages.StartPage`, and the unused parameter goes.
  - The menu item style gains a submenu arrow and a check mark, with a screenshot in RoRoRo's light theme.
- **Tested by:** `ClansModelTests` for the Top rule, `SetupPagesTests` for the fallback, and `walk-setup-clans.ps1` and `walk-stats-table.ps1`.

### W3-8: The boards file and edits

- **Items:** S2-1.1, S2-1.2, S2-2.1, S2-2.2, S2-2.3, S2-5.10, S2-6.7.
- **Files:** `src/Board/BoardsFile.cs`, `src/Board/BoardEdits.cs`, `src/Composition/AppServices.cs`, `src/UI/BoardWindow.PanelSettings.cs`, their tests.
- **Approach:**
  - `KeepUnreadable` reads the old file with `FileShare.ReadWrite`, so a file another program holds doesn't stop the save, and Save's comment says what it can throw.
  - The redundant alias goes, the two search loops become one helper, and every `BoardEdits` method gets its doc comment.
  - A save retried after a failed load keeps one unreadable copy, not two.
  - Settings are applied only to a panel of the type the form was opened for.
- **Tested by:** `BoardsFileTests` with a file held open, and a retried save; `BoardEditsTests` for the type check.

### W3-9: Panel models and the gallery

- **Items:** S1-13.10, S1-13.13, S1-13.15, S2-P.13, S2-4.1, S2-4.7.
- **Files:** `src/Board/PanelModels.cs` (split into `PanelModels.<Panel>.cs` partial files), `src/UI/Panels/PanelViews.cs`, `tests/BoardFixtures.cs` and the five test classes that redefine clan fixtures, `src/UI/Setup/AccountsModel.cs`, `src/Board/PanelGallery.cs`.
- **Approach:**
  - `LiveBoard.MyUserIds` is computed once per board.
  - An unknown panel type shows a small "This panel isn't known to this version of Ur Score." view and a trail line.
  - `PanelModels` splits by panel with no behaviour change.
  - The shared clan fixtures move into `BoardFixtures`.
  - `PanelText.MainLabel` builds "★ name" for My accounts and Setup.
  - `PanelGallery.Cards` computes each card's defaults once.
- **Tested by:** the full suite, unchanged (a split and fixture move change no assertion), plus a test for the unknown-type view's text.

### W3-10: Names, words and errata

- **Items:** S1-F.11, S2-P.4, S2-P.7, S2-P.19, S2-8.5, S2-9.1.
- **Files:** `tests/*.cs`, comments in `src/Board/PanelModels.cs` and `src/Recipes/RecipeStore.cs`, `docs/plans/2026-09-14-score-book-stage-2.md`, `tools/smoke/README.md`.
- **Approach:**
  - Test accounts get obviously made-up names and ids ("MainAccount", 1000001), "Pet Sim" in test comments becomes "the clan fixture", and `src` comment examples use neutral names.
  - The stage 2 plan gets an "Errata" list appended under its execution record for the CS1061 step, Task 9's Consumes list, the contract's two missing names and the slot's `RemovePanelButton` (P36); nothing executed is rewritten.
  - The smoke README's two stage 2 walk rows say they end with the privacy check.
- **Tested by:** the full suite, and `git grep -n -i "estehernandez\|ItsJustEste\|Pet Sim" -- tests src` printing nothing.

---

## Self-review

- **Every OPEN item, once.** The triage table has 140 rows: the 36 "you'd notice" items and the 104 code, test, performance and docs items the backlog lists, each ID exactly once. By wave: W1 37 (35 you'd notice, S2-P.3, S1-14.17), W2 33, W3 64 (S1-9.3 included), won't fix 6 (S1-2.3, S1-4.2, S1-6.5, S1-9.2, S1-9.4, S2-P.9). Checked by script against `docs/backlog.md` when this plan was written; Task 0 checks again against `master`.
- **Wave 1 coverage.** Each wave 1 row points at a task, and each task's header names the same IDs: T1 S1-13.11, S1-14.11, S2-P.18, S2-P.3, S1-13.14; T2 S1-6.9, S1-13.5, S1-F.5; T3 S1-F.6; T4 S1-13.4, S1-12.7; T5 S1-13.6, S1-13.7, S2-FR.2, S1-13.8; T6 S2-P.5, S2-4.5, S2-4.6, S2-FR.1, S2-F.8; T7 S1-14.3, S1-14.5, S1-14.2, S2-8.3, S2-5.9, S1-14.1; T8 S2-8.2; T9 S1-6.8, S1-F.1; T10 S1-12.4, S1-14.17, S1-14.10, S1-12.12, S1-12.8, S1-F.8; T11 S1-11.4; T12 S2-F.7.
- **Placeholders.** Wave 1 steps carry their test code, implementation code, commands and expected output. Three values can't be known now and are looked up by a named command at the step: the v0.3.0 tag date (Task 12 Step 1), the release date and commit hashes (Task 13 Steps 1 and 3), and the release smoke's findings (Task 0 Step 6). Waves 2 and 3 are outlines by design (P5), not placeholders inside wave 1.
- **Names against the tree** (`feat/boards` at 20ab152): `PanelModels.Standing/Race/MyAccounts/AccountCard/ProfileStat/Gap/StaleSource/TotalId/HeadlineNumber/OrderGroups/EmptyCard`, `LiveBoard.IsOverdue/SnapshotOf/SourceName/FindSource/FindRecipe`, `PanelText.Short/Signed/Ordinal/SourceLabel/StaleSource/StaleStat`, `PanelForms.Problem/SourceProblem/StatChoices/Defaults/Build/From/GroupWords/Pick`, `PanelGallery.Cards/CardRecipe/CanAdd`, `BoardEdits.AnchorSourceId/ForFirstSave`, `BoardButtons.For`, `BoardText.StateLine/EmptyFor/EmptyState/Healthy`, `BoardWindow.OnLoaded/RenderBoard/RenderLinesCore/RenderEmpty/OnStartStopClick/OnTestNowClick/OnEmptyStateClick/ButtonStates/SaveBoards/OnDeleteBoardClick/ShownBoard`, `BoardWindow.PopOuts.OnPopOutClosed/SyncPopOuts/ReturnPanel`, `PanelPopOutWindow.OnReturnClick/DropStateBoxes`, `RecipeWatch.RunOnceCoreAsync/OwnedMap/Record/NotRecordingNoAccounts`, `RecipeSnapshot`, `AccountClaims.TryClaim`, `AppServices.Record/LoadInstalled/LoadBookAsync/RaiseIconIfChanged`, `Records.Change/Overdue`, `ChartGeometry.Layout`, `BoardLayout.RowHeights`, `PanelGrid.ArrangeOverride`, `AccountsModel.FoundIn`, `DiagnosticsModel.Sources/Misses/CopyText/StateText`, `ScoreBookModel.NotRecording/SourceLabel`, `ClansModel.NameOf`, `ImportFlow.RunAsync/Outcome/Warn`, `ImportText.Kept/KeptNote`, `SetupWindow.ShowPage`, `ClansPage.Confirm/Show`, `RecipesPage.OnImportClick/OnRemoveClick`, `ScoreBookPage.Refresh/OnOpenFolderClick`, `AccountsPage.AskForAccountsAsync`, and the test helpers `BoardFixtures.Live/Snapshot/Reader/Read/Final/Row/Place/Points/Installed/SourceOf`, `Main/AltOne/AltTwo/Loose`, `RecipeWatchBookTests.Watch/Reading/EngineRow/SourceOf`, `StubEngine.Read`, `MemoryBook` are used as they are in the tree.
- **Names across tasks.** `SentThisRead` (T2) is used by T3's `SnapshotCarry` and T9's `Kept`; `CarriedFrom` (T3) sits before `KeptElsewhere` (T9) in `RecipeSnapshot`; `ButtonStates` is written by T6 and edited by T7 (`_importing || _retryingBook`); `RecipesPage.OnRemoveClick` is written by T10 and its `MessageBox` replaced by T11; `DiagnosticsModel.Sources` gains `KeptElsewhere` in T9 and the not-running state in T10.
- **Copy.** Every new user-facing string is sentence case, second person where it addresses you, specific, and emoji-free; "clan", "battle" and the like come from `RecipeWords` or appear only in tests over the clan fixture; the README names no game.
- **Control characters.** None written literally; the only one in play is `PanelForms.KeySeparator`, which stays `(char)0x1F`.

