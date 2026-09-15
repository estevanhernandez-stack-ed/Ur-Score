# Ur Score backlog: the nice-to-haves from the score book reviews

Every Minor ("nice to have") finding from the code reviews of stage 1 (v0.2.0) and stage 2 (v0.3.0), de-duplicated and checked against `feat/boards` at 7223b9b on 2026-09-15. Listed: items a review marked Minor, re-review "out of scope" notes, pre-flight notes, and work an implementer flagged as left undone. Important and blocking findings were fixed in their own rounds and aren't repeated here. The remediation plan is `docs/plans/2026-09-15-backlog-remediation.md`.

Line format: status — what it is — where — source. IDs (S1-6.8 and so on) are for cross-reference only.

## Counts

- **OPEN: 140** (36 you'd notice, 104 code, tests, performance or docs only)
- **FIXED: 46**
- **GONE: 6**
- Total: 192 distinct items (duplicates merged; every source is named on the line)
- Note: the parked lists in the stage 2 plan's execution record (plan:5343-5349) match the OPEN items marked "parked" here.

## Open, and you'd notice

- S1-6.8 Setup › Score book can give the wrong "not recording" reason when another clan source has claimed your account.
- S1-6.9 "#rank of N" counts players with no value for that stat, so it disagrees with Promotion check.
- S1-9.3 Records can double-count "biggest day" / "fastest week" when two sources read the same account.
- S1-11.4 The "add a 6th source?" confirmation is a plain Windows box, not themed.
- S1-12.4 Error and outcome lines vanish on the next refresh (Accounts, Score book folder, import result).
- S1-12.7 An account seen only in a watched clan reads "Not in a watched clan".
- S1-12.8 Diagnostics can still say "Reporting to RoRoRo." after Stop.
- S1-12.12 A failed reload after a good import says "Could not save that recipe" though it saved.
- S1-13.4 Before the first read, My accounts lists everyone as "Not in a watched clan".
- S1-13.5 The gap to the group above doesn't show when ranks are tied.
- S1-13.6 Race says "clan was removed" when the recipe just has no total, and silently drops removed clans.
- S1-13.7 Profile stat quietly switches to another source when its own is removed, and shows "can't read" instead of the reason.
- S1-13.8 Account card for an account with no reading says "No reading of your accounts yet."
- S1-13.11 A chart of all-equal values draws a flat line on an invented axis.
- S1-13.14 Standing's change is cyan even when it fell.
- S1-14.1 A recipe update that drops its icon leaves the old icon on the window.
- S1-14.2 If the score book fails to load, Start and Test now stay off for the session (rare).
- S1-14.3 Start can wait up to 20 s for RoRoRo's accounts with nothing on screen saying so.
- S1-14.5 While stopped, the top line never shows a Test now result.
- S1-14.10 A cancelled import blanks the Recipes status line.
- S1-14.11 Panels in a row don't stretch to the row's height (ragged bottoms).
- S1-F.1 Claim conflicts aren't shown in Diagnostics; panels show one account under two clans.
- S1-F.5 The SENT dot means "sent sometime this session", not this read.
- S1-F.6 A read that stops (no battle, an error) blanks the panels until the next good read.
- S1-F.8 The import screen's "Kept in your score book" list leaves out the period values it keeps.
- S2-P.5 The gallery's Race card can be enabled by switched-off clans, then the form opens on a problem.
- S2-P.18 The drag handle uses a Braille glyph that may not render.
- S2-4.5 With a panel's recipe uninstalled, its settings say "clan" while the board says "source".
- S2-4.6 The Promotion check card can use another recipe's words in one corner case.
- S2-5.9 A Battle starter with no clan source may anchor its period line differently than 0.2.0 (rare).
- S2-8.2 An outside "close all windows" (taskkill without /f, an updater) brings pop-outs back instead of keeping them.
- S2-8.3 If the score book fails to load, "is popped out" slots show with no windows.
- S2-F.7 README and CHANGELOG say nothing about boards, tabs, the gallery, pop-outs or `boards.json`.
- S2-F.8 Duplicating the empty first-run starter makes the original tab disappear (accepted with the F4 ruling).
- S2-FR.1 A Past periods panel whose stat was removed can't be saved when no ticked stat is left (parked).
- S2-FR.2 With every source of a Profile stat's recipe off, the form picks an off source while the board shows "removed" (parked).

## Open, code tidiness or tests only

- **Test gaps:** S1-1.1, S1-1.2, S1-4.1, S1-5.3, S1-5.4, S1-5.6, S1-6.1, S1-6.2, S1-6.3, S1-3.1, S1-8.7, S1-8.8, S1-9.1, S1-10.3, S1-12.11, S1-13.12, S1-13.16, S1-14.9, S1-15.1, S1-15.2, S1-15.4, S1-L.1, S1-L.2, S1-L.3, S1-L.4, S2-P.3, S2-P.11, S2-P.14, S2-P.16, S2-6.4, S2-8.6, S2-FR.3
- **Behaviour edges nobody is likely to see:** S1-2.2, S1-3.2, S1-4.2, S1-5.1, S1-6.5, S1-6.10, S1-8.2, S1-8.3, S1-8.4, S1-9.2, S1-9.4, S1-11.2, S1-12.6, S1-14.7, S1-14.8, S1-14.13, S1-14.15, S1-F.4, S1-F.10, S1-L.5, S2-P.9, S2-1.1, S2-5.10
- **Performance:** S1-12.5, S1-F.2, S1-F.3, S2-5.1
- **Code tidiness:** S1-1.3, S1-2.1, S1-4.3, S1-5.2, S1-6.4, S1-6.6, S1-6.7, S1-6.11, S1-7.1, S1-7.2, S1-8.1, S1-8.5, S1-8.6, S1-9.5, S1-10.1, S1-10.2, S1-11.1, S1-12.9, S1-13.9, S1-13.10, S1-13.13, S1-13.15, S1-14.6, S1-14.12, S1-14.14, S1-14.16, S1-14.17, S1-F.9, S2-P.13, S2-1.2, S2-2.1, S2-2.2, S2-2.3, S2-4.1, S2-4.7, S2-6.7, S2-FR.4
- **Docs, plan text, process:** S1-2.3 (commit co-author lines), S1-16.1, S1-F.11 (before the repo goes public), S2-P.4, S2-P.7, S2-P.19, S2-8.5, S2-9.1

---

# Stage 1 (v0.2.0)

## Task 1: recipe format additions
- S1-1.1 **OPEN** — test gap: nothing tests that a recipe's period "past" path can't point at one particular player — tests/RecipeParserTests.cs:451-456 (check at src/Recipes/RecipeParser.cs:716) — T1 review
- S1-1.2 **OPEN** — test gap: an input with an empty label falling back to "Items" is untested — src/Recipes/Recipe.cs:66 — T1 review
- S1-1.3 **OPEN** — code tidiness: the parser repeats the "is this a group list" rule with no comment saying why it can't reuse it — src/Recipes/RecipeParser.cs:77 — T1 review

## Task 2: engine periods and past
- S1-2.1 **OPEN** — code tidiness / test gap: a read that stops on its last step still carries a period, contrary to what the report said; no test — src/Recipes/RecipeEngine.cs:171, 182 — T2 review
- S1-2.2 **OPEN** — a fractional rank in a top list is cut to a whole number instead of treated as unreadable (real data is whole numbers) — src/Recipes/RecipeEngine.cs:621 — T2 review
- S1-2.3 **OPEN** — process only: several stage 1 commits carry a "Claude Sonnet 5" or "Claude Haiku 4.5" co-author line instead of the mandated Opus 5; merged history, nothing to do short of a rewrite — git history (e.g. 52faa24, dd9e406) — T2, T3, T5, T9, T12, T16 reviews

## Task 3: sources
- S1-3.1 **OPEN** — test gap: promoting a watched clan straight to main is untested, both in the source rules and in the Clans page model — tests/SourcesTests.cs:69-80; tests/ClansModelTests.cs:95-100 — T3 review, T11 review
- S1-3.2 **OPEN** — re-adding a clan you had switched off silently switches it back on; undocumented and untested — src/Core/Sources.cs:151 — T3 review
- S1-3.3 **FIXED** — a locked or permission-denied sources/accounts file could crash loading instead of reading as empty — Task 4 fix round (src/Core/Sources.cs:67, src/Core/SharedAccounts.cs:30) — T3 review, T4 review
- S1-3.4 **GONE** — implementer reports with wrong test counts, line numbers or claims (the reports were deleted; the code was right) — n/a — T3, T4, T8, T11 reviews

## Task 4: shared accounts, claims, spacing
- S1-4.1 **OPEN** — test gap: the spacing test never checks that a second site isn't held up by the first — tests/SharedAccountsTests.cs:231-233 — T4 review
- S1-4.2 **OPEN** — after a failed or cancelled request, the next one to that site still waits the full 2 s (slower recovery, never too fast) — src/Recipes/SpacedTransport.cs:28 — T4 review
- S1-4.3 **OPEN** — code tidiness: the claims table and per-site lanes never forget old entries (negligible at real scale) — src/Core/AccountClaims.cs:12; src/Recipes/SpacedTransport.cs:13 — T4 review

## Task 5: score book writer
- S1-5.1 **OPEN** — the "dropped lines" count can be off by one in a rare race (the count only; no data lost) — src/Book/ScoreBook.cs:84 — T5 review
- S1-5.2 **OPEN** — code tidiness: `Written`'s comment says "on the writer's thread", untrue in test mode — src/Book/ScoreBook.cs:10 — T5 review
- S1-5.3 **OPEN** — test gap: `RemovingARecipeLeavesItsBook` passes without exercising anything that could delete a book — tests/ScoreBookTests.cs:166 — T5 review
- S1-5.4 **OPEN** — test gap: the writer loop's last-resort catch has no test — src/Book/ScoreBook.cs:114-123 — T5 re-review
- S1-5.5 **FIXED** — a wrongly typed time (`{"t":123}`) might break reading the book: the final review tested it and it is skipped safely, and the related nested-null hole was fixed — fix wave a830a42..a8161dc (`BookJson` completeness check in src/Book/BookLine.cs) — T5 re-review, final review Minor 7
- S1-5.6 **OPEN** — test gap / slow suite: the drop-limit test takes about 24 s because every line is flushed to disk (a durability choice) — tests/ScoreBookTests.cs:138; src/Book/ScoreBook.cs:207 — T5 re-review, T5 report

## Task 6: lines and watch recording
- S1-6.1 **OPEN** (in part) — test gap: a recipe change in the middle of sending, with a book attached, is untested (the before-the-read case got a test) — tests/RecipeWatchBookTests.cs:166 — T6 review
- S1-6.2 **OPEN** — test gap: the group-list test uses only a watch source; a group list on your own source still passes your account ids to the reader — tests/RecipeWatchBookTests.cs:134 — T6 review
- S1-6.3 **OPEN** — test gap: the account claim expiring after twice the interval isn't tested at watch level — tests/RecipeWatchBookTests.cs:109 — T6 review
- S1-6.4 **OPEN** — code tidiness: the book line records the source's inputs while the read used the watch's own copy; if they ever differ a line is filed under the wrong inputs — src/Book/LineBuilder.cs:48 — T6 review
- S1-6.5 **OPEN** — switching a clan to Watch during a read lets the rest of that read send (documented as "applies next read") — src/Core/RecipeWatch.cs:206 — T6 review
- S1-6.6 **OPEN** — code tidiness: the lock comment says every access is locked, but `Recipe` is read without it — src/Core/RecipeWatch.cs:95-97, 122 — T6 review
- S1-6.7 **OPEN** — code tidiness: report timestamps use the system clock, not the injected one — src/Core/RecipeWatch.cs:359, 427 — T6 review
- S1-6.8 **OPEN** — you'd notice: Setup › Score book gives the wrong "not recording" reason when another clan source has claimed your account ("None of your accounts were in this read") — src/Core/RecipeWatch.cs:453 — T6 review
- S1-6.9 **OPEN** — you'd notice: "#rank of N" counts players with no value for that stat, so it disagrees with Promotion check's count — src/Book/LineBuilder.cs:88; src/Board/PanelModels.cs:281 — T6 review, T13 review
- S1-6.10 **OPEN** — privacy edge: a headline number is only compared with the ids in this read's rows, so an id-like number for someone outside them could be kept — src/Book/LineBuilder.cs:92-100 — T6 review
- S1-6.11 **OPEN** — code tidiness: the recipe hash is recomputed every read — src/Core/RecipeWatch.cs:428 — T6 review

## Task 7: finals and backfill
- S1-7.1 **OPEN** — code tidiness: an unnecessary fully qualified name, with a wrong reason given in the report — src/Book/Finals.cs:18 — T7 review
- S1-7.2 **OPEN** — code tidiness: the "period has ended" test is written twice — src/Book/Finals.cs:77, 113 — T7 review

## Task 8: SourceHost
- S1-8.1 **OPEN** — code tidiness: cancellation sources are never disposed and pile up slightly with each Start/Stop — src/Core/SourceHost.cs:86, 110 — T8 review
- S1-8.2 **OPEN** — on exit, a read in flight isn't waited for, so at most one read's lines can be lost — src/Core/SourceHost.cs:99-106 — T8 review, T14 review (exit race), stage 1 final review triage
- S1-8.3 **OPEN** — Test now keeps reading, recording and sending a clan you removed mid-read; only its snapshot is hidden — src/Core/SourceHost.cs:91-97 — T8 review
- S1-8.4 **OPEN** — switching a clan off and straight back on during a read can briefly run two watches for it — src/Core/SourceHost.cs:45-48 — T8 review
- S1-8.5 **OPEN** — code tidiness: creating a watch runs inside the lock, so a slow or failing factory blocks the UI or half-applies — src/Core/SourceHost.cs:61 — T8 review
- S1-8.6 **OPEN** — code tidiness: a source reference is read without the lock (possibly stale, never corrupt) — src/Core/SourceHost.cs:57 — T8 review
- S1-8.7 **OPEN** — test gap: no tests for Stop then Start, timer-triggered reads, the interval clamp, Dispose or the interval-lookup guard; the removal-during-read test never hits the real race window — tests/SourceHostTests.cs:84-99, 145 — T8 review, T8 re-review
- S1-8.8 **OPEN** — test gap: `Assert.All` over book lines passes when no line was written — tests/SourceHostTests.cs:80 — T8 review

## Task 9: reader and records
- S1-9.1 **OPEN** — test gap: the reader's headline series has no direct test — src/Book/ScoreBookReader.cs:74 — T9 review
- S1-9.2 **OPEN** — with two final lines for one account in one battle, the first wins, so a later correction is ignored — src/Book/ScoreBookReader.cs:96 — T9 review
- S1-9.3 **OPEN** — you'd notice: Records merges several sources without collapsing near-duplicate reads, so "biggest day" and "fastest week" can double-count — src/Book/Records.cs:29-32 — T9 review
- S1-9.4 **OPEN** — a past battle's time is its earliest final line, not its latest — src/Book/ScoreBookReader.cs:100 — T9 review
- S1-9.5 **OPEN** — code tidiness: fully qualified `Source.KeyOf` instead of the alias used elsewhere — src/Book/ScoreBookReader.cs:86 — T9 review
- S1-9.6 **FIXED** — loading the book was quadratic, which would keep Start disabled for seconds — final fix wave (Load no longer prunes per line, src/Book/ScoreBookReader.cs) — T9 re-review, stage 1 final review Important 1

## Task 10: searchable Stats table
- S1-10.1 **OPEN** — code tidiness: the same `Show` helper now exists in 7 files — src/UI/Controls/StatsTable.xaml.cs:181 (and ImportWindow, Accounts, Clans, Recipes, ScoreBook, Stats pages) — T10 review
- S1-10.2 **OPEN** — code tidiness: a refused Send tick redraws the table up to three times — src/UI/Controls/StatsTable.xaml.cs:162, 167 — T10 review
- S1-10.3 **OPEN** — test gap: a saved counter the recipe no longer recognises — src/UI/Controls/StatsTableModel.cs:120 — T10 review

## Task 11: Setup window and Clans
- S1-11.1 **OPEN** — code tidiness: clan search picks and name loading are fire-and-forget with no outer catch — src/UI/Setup/ClansPage.xaml.cs:33-39 — T11 review
- S1-11.2 **OPEN** — the "Top of the battle" switch uses the first group-list recipe installed, not the page's own (matters only with two clan-type recipes) — src/UI/Setup/ClansModel.cs:149-150 — T11 review
- S1-11.3 (merged into S1-3.1)
- S1-11.4 **OPEN** — you'd notice: the "add a 6th source?" confirmation is a stock Windows message box, not themed — src/UI/Setup/ClansPage.xaml.cs:288-289 — T11 review

## Task 12: remaining Setup pages
- S1-12.1 **GONE** — the moved import flow dropped typed inputs: Task 14 removed the import screen's inputs — n/a — T12 review, T12 report
- S1-12.2 **FIXED** — two quick Send ticks could lose one — stage 1 fix wave item 9 (src/UI/Setup/AccountsPage.xaml.cs, change computed inside the deferred save) — T12 review, final review triage
- S1-12.3 **FIXED** — the Alerts card could describe a watch-only recipe as sending — Task 14 fix round (src/Core/ReportPolicies.cs, shared by AppServices and AlertsModel) — T12 review, T14 review, T14 report
- S1-12.4 **OPEN** — you'd notice: messages vanish on the next refresh: "Could not ask RoRoRo" (AccountsPage.xaml.cs:45 overwrites :58), "Could not open the folder" (ScoreBookPage.xaml.cs:27 overwrites :51), and the import result when the page jumps to Clans (RecipesPage.xaml.cs:43-44) — src/UI/Setup/ — T12 review
- S1-12.5 **OPEN** — performance: disk reads on the UI thread on every refresh (RoRoRo's rules file, the accounts cache time) — src/UI/Setup/AlertsPage.xaml.cs:62; ScoreBookPage.xaml.cs:36; AccountsPage.xaml.cs:35 — T12 review
- S1-12.6 **OPEN** — the Stats page asks the source for counter names again on each recipe switch or save while none are saved (spec says once) — src/UI/Setup/StatsPage.xaml.cs:92 — T12 review
- S1-12.7 **OPEN** — you'd notice: an account found only in a watched clan reads "Not in a watched clan", and the noun comes from the first recipe only — src/UI/Setup/AccountsModel.cs:87 — T12 review
- S1-12.8 **OPEN** — you'd notice: Diagnostics can still say "Reporting to RoRoRo." after you press Stop — src/UI/Setup/DiagnosticsModel.cs:31 — T12 review
- S1-12.9 **OPEN** — code tidiness: an unknown Setup page id shows Diagnostics, and the Stats page's recipe parameter is never passed — src/UI/SetupWindow.xaml.cs:87, 91 — T12 review
- S1-12.10 **FIXED** — a source comment named a real account and a game — stage 1 fix wave item 6 (src/UI/Setup/AccountsModel.cs) — T12 review, final review triage
- S1-12.11 **OPEN** — test gap: Alerts tests leave temp folders behind; no tests for listed-time preference, id-0 misses, or watch-source inputs in the copy text — tests/AlertsModelTests.cs:18 — T12 review
- S1-12.12 **OPEN** — you'd notice (rare): if the reload after a successful import throws, you're told "Could not save that recipe" although it was saved — src/UI/Setup/ImportFlow.cs:114-120 — T12 review

## Task 13: the ten panels
- S1-13.1 **FIXED** — stale panels had no "Choose another" button — stage 2 Task 6, 028e24d (`ChooseAnotherButton`) — T13 review
- S1-13.2 **FIXED** — Records showed "+-1.5K" for a fall — stage 1 fix wave item 4 (`PanelText.Signed`) — T13 review, final review Minor 5
- S1-13.3 **FIXED** — Ctrl+C on the live leaderboard copied other players' names — stage 1 fix wave item 5 (src/UI/Panels/LiveLeaderboardPanel.xaml, `ClipboardCopyMode="None"`) — T13 review, final review Minor 6
- S1-13.4 **OPEN** — you'd notice: before the first read, My accounts lists everyone under "Not in a watched clan", and so are accounts seen only by a watched clan — src/Board/PanelModels.cs:296 — T13 review
- S1-13.5 **OPEN** — you'd notice: the gap to the group above doesn't show when ranks are tied (12, 12, 14) — src/Board/PanelModels.cs:754 — T13 review
- S1-13.6 **OPEN** — you'd notice: Race says "This panel's clan was removed." when the recipe simply has no summed total, silently drops removed clans, and doesn't enforce 2 to 5 — src/Board/PanelModels.cs:187-191 — T13 review
- S1-13.7 **OPEN** — you'd notice: Profile stat quietly falls back to another source when its own was removed, and its row note says "can't read" instead of the real reason — src/Board/PanelModels.cs:616, 639 — T13 review
- S1-13.8 **OPEN** — you'd notice: an Account card whose chosen account has no reading says "No reading of your accounts yet." — src/Board/PanelModels.cs:389 — T13 review
- S1-13.9 **OPEN** — code tidiness: Past periods regroups finals that the reader already grouped — src/Board/PanelModels.cs:452-457 — T13 review
- S1-13.10 **OPEN** — code tidiness: your account-id set is rebuilt on every access — src/Board/PanelModels.cs:47 — T13 review
- S1-13.11 **OPEN** — you'd notice: a chart whose values are all equal draws a flat line at the bottom on a made-up axis; no tests for equal or negative values — src/Board/ChartGeometry.cs:40 — T13 review
- S1-13.12 **OPEN** — test gap: panel tests compute expected values with the same calls the code makes, and the `withGap` case actually asserts no gap — tests/PanelModelsTests.cs:67-73 — T13 review
- S1-13.13 **OPEN** — code tidiness: an unknown panel type silently becomes a Live leaderboard — src/UI/Panels/PanelViews.cs:14 — T13 review
- S1-13.14 **OPEN** — you'd notice: Standing's change is always cyan, even when it went down — src/UI/Panels/StandingPanel.xaml:29 — T13 review
- S1-13.15 **OPEN** — code tidiness: PanelModels.cs is 761 lines of records and builders; worth splitting — src/Board/PanelModels.cs — T13 review
- S1-13.16 **OPEN** — test gap: the Today / 7 days "no earlier read" text is never asserted, and no test runs in a time zone other than UTC — src/Board/PanelModels.cs:682; tests/BoardFixtures.cs:14 — T13 re-review

## Task 14: board window and AppServices
- S1-14.1 **OPEN** — you'd notice: when a recipe update drops its icon, the old icon stays on the window — src/Composition/AppServices.cs:692 — T14 review
- S1-14.2 **OPEN** — you'd notice (rare): if the score book fails to load, Start and Test now stay disabled for the whole session with no retry — src/UI/BoardWindow.xaml.cs:107-113 — T14 review, final review triage
- S1-14.3 **OPEN** — you'd notice: Start can wait up to 20 s for RoRoRo's accounts with nothing on screen saying so — src/Composition/AppServices.cs:36, 411 — T14 review
- S1-14.4 **FIXED** — Start/Stop presses were silently ignored during Test now — live-walk fix (src/UI/BoardButtons.cs, `BoardButtons.For` + `ApplyButtons`) — T14 review
- S1-14.5 **OPEN** — you'd notice: while stopped, the top line only ever says "Not started." or "Stopped.", so a Test now result never shows there — src/UI/BoardText.cs:31 — T14 review
- S1-14.6 **OPEN** — code tidiness: async paths with no catch (empty-state import, name resolving, icon fetch) — src/UI/BoardWindow.xaml.cs:554-584, 165; src/Composition/AppServices.cs:631 — T14 review
- S1-14.7 **OPEN** — an icon fetch that throws is never retried until the icon text changes — src/Composition/AppServices.cs:695 — T14 review
- S1-14.8 **OPEN** — changing only which stats you track releases a held "key rejected / sign in" stop, costing one extra rejected request — src/Composition/AppServices.cs:539-542 — T14 review
- S1-14.9 **OPEN** — test gap: AppServices (now 916 lines) and App startup have no tests of their own — src/Composition/AppServices.cs; src/App.xaml.cs — T14 review, T14 report, T14 re-review 2
- S1-14.10 **OPEN** — you'd notice: a cancelled import leaves the Recipes status line blank — src/UI/Setup/RecipesPage.xaml.cs:40-41 — T14 review, T14 report
- S1-14.11 **OPEN** — you'd notice: panels in one row keep their own height instead of stretching to the row, so bottoms are ragged — src/UI/Controls/PanelGrid.cs:65-66 — T14 review, T14 report
- S1-14.12 **OPEN** — code tidiness: the icon client and the closing token are never disposed — src/Composition/AppServices.cs:451-467 — T14 review
- S1-14.13 **OPEN** — a newly listed account's policy refresh can land after a fresher one, briefly allowing a just-excluded stat to your local RoRoRo (self-corrects) — src/Composition/AppServices.cs:551 — T14 re-review 1, final review triage
- S1-14.14 **OPEN** — privacy tidiness: some trail lines still carry full exception text or messages instead of the type — src/UI/BoardWindow.xaml.cs:112, 614; src/Composition/AppServices.cs:664, 683 — T14 re-review 1, stage 1 final fix report
- S1-14.15 **OPEN** — a sources.json you're denied read access to looks "missing", so start could migrate and save over it — src/Core/Sources.cs:60 — T14 re-review 1, final review triage
- S1-14.16 **OPEN** — code tidiness: a dead `_services is null` check in the startup guard — src/App.xaml.cs:76 — T14 re-review 2
- S1-14.17 **OPEN** — code tidiness: an unreachable catch around the accounts refresh — src/UI/Setup/AccountsPage.xaml.cs:56 — T14 report

## Task 15: try-out command
- S1-15.1 **OPEN** — test gap: the `--try` JSON output test checks only one other player's id (the text test checks three) — tests/TryCommandTests.cs:75 — T15 review
- S1-15.2 **OPEN** — test gap: nothing guards that `--try` runs before the single-instance lock — src/App.xaml.cs — T15 review
- S1-15.3 **GONE** — the `--try` KeyStore isn't disposed: KeyStore holds nothing disposable — src/Recipes/KeyStore.cs:29 — T15 review
- S1-15.4 **OPEN** — test gap: `--try` was only run live against an idle clan, never a real battle read — src/Cli/TryCommand.cs — T15 report

## Task 16: smoke scripts
- (co-author line merged into S1-2.3)
- S1-16.1 **OPEN** — if a smoke walk is killed mid-run, the next run doesn't notice your data still sitting in a `.smoke-backup-*` folder (the README says to rename it back) — tools/smoke/uia.ps1:231 — T16 review

## Stage 1 final review, re-review and fix report
- S1-F.1 **OPEN** — you'd notice: account claim conflicts aren't shown in Diagnostics; panels show the account under both clans while the book keeps it under one — src/Core/RecipeWatch.cs:408; src/UI/Setup/DiagnosticsModel.cs — final review Minor 8, parked in stage 2 execution record
- S1-F.2 **OPEN** — performance: startup reads the whole book twice (finals index, then the reader) — src/Composition/AppServices.cs:263-264 — final review Minor 9, parked
- S1-F.3 **OPEN** — performance: each chart scans every kept reading of every clan, and five weeks of book may hold 100-170 MB — src/Book/ScoreBookReader.cs:104-125 — final review Minor 10, parked
- S1-F.4 **OPEN** — the recipe text file isn't written atomically, and a torn write is never repaired — src/Book/ScoreBook.cs:188 — final review Minor 11, parked
- S1-F.5 **OPEN** — you'd notice: the SENT dot means "sent at some point this session", not "sent this read" — src/Board/PanelModels.cs:275 — final review Minor 12
- S1-F.6 **OPEN** — you'd notice: a read that stops (no battle, an error) blanks the panels until the next good read — src/Core/SourceHost.cs:173 — final review Minor 13
- S1-F.7 **FIXED** — a stale "Task 15 adds the --try branch HERE" comment — stage 1 fix wave item 7 (src/App.xaml.cs) — final review Minor 14
- S1-F.8 **OPEN** — you'd notice: the import screen's "Kept in your score book" list shows only headline items, though period values go into every line — src/UI/ImportText.cs:13 — final review Minor 15
- S1-F.9 **OPEN** — code tidiness: the account-list fallback catches every exception, so a real bug would look like "RoRoRo not answering" — src/Core/SharedAccounts.cs:92 — final re-review
- S1-F.10 **OPEN** — a book line that still fails to load is skipped with no trail note — src/Book/ScoreBookReader.cs:40; src/Book/Finals.cs:54 — final re-review, parked
- S1-F.11 **OPEN** — before the repo goes public: tests use real-looking account names (estehernandez, ItsJustEste...) and a possibly real Roblox id, test comments say "Pet Sim", and src comments use "CCGP" and "ps99.diamonds" as examples — tests/BoardFixtures.cs; tests/ScoreBookTests.cs:21; tests/RecipeEngineTests.cs:563; src/Board/PanelModels.cs:61; src/Recipes/RecipeStore.cs:9 — stage 1 final fix report

## Stage 1 live walk (fix review and report)
- S1-L.1 **OPEN** — test gap: the starter-board walk throws away whether Test now ever went disabled, so a broken button could still pass step 2c — tools/smoke/walk-starter-board.ps1:78 — live-fix review
- S1-L.2 **OPEN** — test gap: the RowList automation test was never run against the original bug's shape, so it may not catch the same bug by another route — tests/RowListAutomationTests.cs:66-67 — live-fix review
- S1-L.3 **OPEN** — test gap: walk-score-book still sleeps a fixed 20 s after Test now and can race a slow read — tools/smoke/walk-score-book.ps1:21 — live-fix report
- S1-L.4 **OPEN** — test gap: ListBox lists (Setup nav, search matches, stats rows) aren't covered by the RowList fence — tests/RowListFenceTests.cs — live-fix report
- S1-L.5 **OPEN** — accessibility: lists expose their buttons and text directly, which strict UI Automation checkers may flag (Narrator reads them fine) — src/UI/Controls/RowList.cs — live-fix report

---

# Stage 2 (v0.3.0)

## Pre-flight scan (NOTE rows)
- S2-P.1 **FIXED** — pop-out titles took the first recipe's words, not the panel's — e942e1d (`PanelGallery.TitleOf`) — pre-flight P15, final review Minor 1
- S2-P.2 **FIXED** — moving a borderless pop-out through UI Automation was unverified — walk-pop-outs passed 14/14 live (plan execution record) — pre-flight P25
- S2-P.3 **OPEN** — test / accessibility: the drag handle's automation id sits on a Border, which UI Automation can't see — src/UI/Panels/PanelFrame.xaml:7 — pre-flight P27, T7 review
- S2-P.4 **OPEN** — plan text: Task 3 Step 2 expects CS0117 for a missing instance member; it is CS1061 — docs/plans/2026-09-14-score-book-stage-2.md:1268 — pre-flight T3 row
- S2-P.5 **OPEN** — you'd notice: the gallery's Race card counts switched-off clans, so it can be enabled and then open on a problem — src/Board/PanelGallery.cs:81-84 — pre-flight T4 row
- S2-P.6 **FIXED** — the race checkbox problem line could lag one click — 028e24d (`OnRaceChanged` copies the tick before checking) — pre-flight T6 row
- S2-P.7 **OPEN** — plan text: Task 9's Consumes list omits `Get-SetupWindow` and `Get-UrProcessId` — docs/plans/2026-09-14-score-book-stage-2.md:4914 — pre-flight T9 row
- S2-P.8 **FIXED** — `StarterBoard.Key` and `AnchorSourceId` were read only by tests — a3c5296 — pre-flight R11, final review Minor 5
- S2-P.9 **OPEN** — privacy edge: an unreadable boards.json is copied verbatim, so a hand-typed stranger's id would sit in the kept copy (it's your own text) — src/Board/BoardsFile.cs:133-134 — pre-flight D8
- S2-P.10 **FIXED** — saving boards re-derived your ids instead of sharing the panels' helper — 0e21742 (`PanelModels.UserIdsOf`) — pre-flight D9, final review Minor 8
- S2-P.11 **OPEN** — test gap: no fence test that only AppServices writes boards.json, always through Sanitize — src/Composition/AppServices.cs:236 — pre-flight D10, final review recommendation 2
- S2-P.12 **FIXED** — a literal "5" in the Race card — src/Board/PanelGallery.cs:46 uses `PanelModels.MaxRace` — pre-flight D11
- S2-P.13 **OPEN** — code tidiness: the MainClan/AltClan/Rival fixtures are redefined in 5 test classes — tests/BoardTextTests.cs:11 (and 4 more) — pre-flight D12
- S2-P.14 **OPEN** (in part) — test gap: the new-board naming rule still lives untested in the window (the pop-out and form fallbacks moved into tested code) — src/UI/Boards/AddBoardWindow.xaml.cs:68-72 — pre-flight D13
- S2-P.15 **GONE** — ⋯ changes to a popped-out panel in edit mode wouldn't show until Done: a popped-out slot now ignores every tool but Remove — src/UI/BoardWindow.PopOuts.cs:112-120 — pre-flight D14
- S2-P.16 **OPEN** — test gap: pop-outs from two different boards can share one automation id — src/UI/BoardWindow.PopOuts.cs:200-201 — pre-flight D15
- S2-P.17 **FIXED** — duplicating a long board name lost " copy" — 0013952 — pre-flight D16, final review Minor 6
- S2-P.18 **OPEN** — you'd notice (maybe): the drag handle glyph is a Braille character that may not render in the UI font — src/UI/Panels/PanelFrame.xaml:9 — pre-flight D17
- S2-P.19 **OPEN** — plan text: the interface contract omits `BoardText.EmptyFor` and `PanelForms.GroupWords` — docs/plans/2026-09-14-score-book-stage-2.md:95-240 — pre-flight D18

## Task 1: board definitions and boards.json
- S2-1.1 **OPEN** — if boards.json is locked when you change a board, the save throws while reading the old file (you get the "not saved" message; nothing is lost) — src/Board/BoardsFile.cs:129 — T1 review
- S2-1.2 **OPEN** — code tidiness: Save's comment doesn't say it can also throw while reading the old file — src/Board/BoardsFile.cs:51 — T1 review
- S2-1.3 **GONE** — two near-identical board key builders: `StarterBoard.Key` was removed — a3c5296 — T1 review

## Task 2: board edits
- S2-2.1 **OPEN** — code tidiness: a redundant `using Source = ...` alias — src/Board/BoardEdits.cs:5 — T2 review
- S2-2.2 **OPEN** — code tidiness: two identical search loops for boards and panels — src/Board/BoardEdits.cs:280, 290 — T2 review
- S2-2.3 **OPEN** — code tidiness: doc comments on some BoardEdits methods and not others — src/Board/BoardEdits.cs — T2 review

## Task 3: layout
- No minor findings.

## Task 4: forms and gallery
- S2-4.1 **OPEN** — code tidiness: the "★ name" label is still hand-built in My accounts and in Setup › Your accounts instead of the shared helper — src/Board/PanelModels.cs:288; src/UI/Setup/AccountsModel.cs:82 — T4 review, T4 report
- S2-4.2 **FIXED** — the form retyped the board's "was removed" wording — 2e18235 — T4 review
- S2-4.3 **FIXED** — the group-recipe selector was written twice — 2e18235 (`PanelText.GroupRecipe`) — T4 review
- S2-4.4 **FIXED** — a Profile stat whose only source is off looked stale — 2e18235 — T4 review
- S2-4.5 **OPEN** — you'd notice: with a panel's recipe uninstalled, its settings form names the group ("clan") while the board says "source" — src/Board/PanelForms.cs:268 vs src/Board/PanelModels.cs:703 — T4 review, T4 re-review
- S2-4.6 **OPEN** — you'd notice (corner case): the Promotion check gallery card can use another recipe's words when your main has no second clan of its recipe — src/Board/PanelGallery.cs:72 — T4 re-review
- S2-4.7 **OPEN** — code tidiness: the gallery recomputes a card's defaults on every title call — src/Board/PanelGallery.cs:23, 72 — T4 re-review

## Task 5: saved boards and tabs
- S2-5.1 **OPEN** — performance: while following the starter, every read of the board list (each render, the 20 s clock) rebuilds the starter — src/Composition/AppServices.cs:214-215 — T5 review Minor 1, T5 report, final review Minor 9, parked
- S2-5.2 **FIXED** — the tab menu's hover and disabled colours might not match the theme — 9755c07 (themed menu, src/App.xaml:346-393) — T5 review Minor 2, final review parked item E
- S2-5.3 **FIXED** — many tabs squeezed + Board and the period line — 10eb325/da74cb2 (tabs scroll, capped width) — T5 review
- S2-5.4 **FIXED** — keyboard focus lost after a rename — 10eb325/da74cb2 — T5 review
- S2-5.5 **FIXED** — a stale board list was saved after a prompt — 10eb325/da74cb2 — T5 review
- S2-5.6 **FIXED** — starter buttons' accessible names dropped the panel count — 10eb325/da74cb2 — T5 review
- S2-5.7 **FIXED** — the add-board name box had no length limit — 10eb325/da74cb2 — T5 review
- S2-5.8 **FIXED** — SaveBoards' comment missed the empty-list rule — 10eb325/da74cb2 — T5 review
- S2-5.9 **OPEN** — you'd notice (rare): a Battle starter with no main, mine or watch clan may anchor its period line on a different source than 0.2.0 did — src/UI/BoardWindow.xaml.cs:155 — T5 review, T5 report
- S2-5.10 **OPEN** — retrying a failed save after a failed load can make a second unreadable-file copy — src/Composition/AppServices.cs:236-237 — T5 re-review
- S2-5.11 **FIXED** — the "wasn't saved" note hid the RoRoRo-down and budget text indefinitely — da74cb2 — T5 report

## Task 6: panel tools, settings, gallery
- S2-6.1 **FIXED** — an Account card pinned to an unlisted account opened blank and silently saved as "Your top account" — d42c838 — T6 review, final review parked
- S2-6.2 **FIXED** — adding a Profile stat pinned a source the form never showed — d42c838 (a leftover corner is S2-FR.2) — T6 review, final review parked
- S2-6.3 **FIXED** — a circular comment in `RenderEmpty` — 5bd1fab — T6 review
- S2-6.4 **OPEN** — test gap: "no race list" counting the same as "empty race list" is promised but not asserted — tests/BoardEditsTests.cs:153-163 — T6 review
- S2-6.5 **FIXED** — a long race list could push Save off screen — 5bd1fab — T6 review, final review parked
- S2-6.6 **FIXED** — keyboard focus dropped after ⋯ Save or Add panel — fa169ac, 2dc6858 — T6 review, final review Minor 2
- S2-6.7 **OPEN** — code tidiness: settings are applied by panel id without checking the panel's type (not reachable today) — src/UI/BoardWindow.PanelSettings.cs:42 — T6 review

## Task 7: edit mode
- S2-7.1 **FIXED** — Done's "compare with the board as editing began" rule had no test — fa169ac (`BoardEdits.Finish`) — T7 review
- S2-7.2 **FIXED** — an empty board in edit mode told you to use Edit board — fa169ac — T7 review
- S2-7.3 **FIXED** — Done could undo a pop-out change, and tools could move a popped-out panel — 4383253 (`CarryPopOuts`, Remove-only slots) — T7 review
- S2-7.4 **FIXED** — a non-IO save failure on Done was silent — fa169ac — T7 review

## Task 8: pop-outs
- S2-8.1 **FIXED** — a pop-out's automation id went stale after panels were renumbered — cd40bd0 — T8 review
- S2-8.2 **OPEN** — you'd notice (rare): a close sent to every window from outside (taskkill without /f, an updater) returns your pop-outs instead of keeping them for next start — src/UI/BoardWindow.PopOuts.cs:129 — T8 review, T8 report, parked
- S2-8.3 **OPEN** — you'd notice (rare): if the score book fails to load, "is popped out" slots show with no windows until a redraw — src/UI/BoardWindow.xaml.cs:107-113 — T8 review, T8 report, parked
- S2-8.4 **FIXED** — a pop-out could maximize or snap to half the screen — cd40bd0 — T8 review
- S2-8.5 **OPEN** — plan text: the automation id table doesn't list the slot's Remove button — docs/plans/2026-09-14-score-book-stage-2.md:132 — T8 review
- S2-8.6 **OPEN** — test gap: pop-out placement on mixed-DPI monitors is unverified (the app isn't per-monitor DPI aware) — src/UI/BoardWindow.PopOuts.cs:212 — T8 review, T8 re-review, final review parked

## Task 9: stage 2 smoke walks
- S2-9.1 **OPEN** — docs: the two new walk rows in the smoke README don't say they end with the privacy check — tools/smoke/README.md:24-25 — T9 review
- S2-9.2 **GONE** — README row placement and wording weren't disclosed in the report (report deleted; the placement is fine) — n/a — T9 review
- S2-9.3 **FIXED** — a `$board` parameter shadowed the walk's `$board` window — tools/smoke/walk-board-editing.ps1:14 now `$index` (final fix wave) — T9 review
- S2-9.4 **FIXED** — walk-pop-outs step 3 showed a bare FAIL when no battle was running — 73e911a (`Skip` with a reason; the leftover is S2-FR.3) — T9 review

## Stage 2 final review
- (Minor 1 is S2-P.1; Minor 2 is S2-6.6; Minor 5 is S2-P.8; Minor 6 is S2-P.17; Minor 9 is S2-5.1)
- S2-F.3 **FIXED** — holding Enter on Remove could delete panel after panel — 1313b28 — final review Minor 3
- S2-F.4 **FIXED** — an empty first-run starter could be frozen into boards.json by a change elsewhere — c41345e — final review Minor 4
- S2-F.5 **FIXED** — one wrongly typed JSON field made the whole boards.json unreadable — 5d0dd79 — final review Minor 7
- S2-F.6 **FIXED** — small duplications (your ids, group words, panel automation id) — 0e21742 — final review Minor 8
- S2-F.7 **OPEN** — you'd notice (docs): README and CHANGELOG don't mention boards, tabs, the gallery, pop-outs or `boards.json` (the 0.3.0 release commit only bumped versions) — README.md; CHANGELOG.md — final review recommendation 4

## Stage 2 final re-review
- S2-F.8 **OPEN** — you'd notice: duplicating the empty first-run starter makes the original tab disappear (disclosed, accepted with the F4 ruling) — src/Board/BoardEdits.cs:123-124 — final re-review ("not counted")
- S2-FR.1 **OPEN** — you'd notice: a Past periods panel whose stat was removed can't be saved when its recipe has no ticked stat left (only Cancel, another clan, or Remove) — src/UI/Boards/PanelSettingsWindow.xaml.cs:125, 136 — final re-review Minor 1, parked
- S2-FR.2 **OPEN** — you'd notice: when every source of a Profile stat's recipe is off, the form preselects an off source with no warning while the board shows "was removed" — src/Board/PanelForms.cs:242 vs src/Board/PanelModels.cs:616 — final re-review Minor 2, T4 re-review, parked
- S2-FR.3 **OPEN** — test gap: walk-pop-outs step 3 skips instead of failing when reads are broken — tools/smoke/walk-pop-outs.ps1:48-57 — final re-review Minor 3, parked
- S2-FR.4 **OPEN** — code tidiness: the app-wide menu item style handles flat items only (no submenus or check marks); the light-theme menu wasn't seen live — src/App.xaml:364-393 — final re-review Minor 4, parked
