# Build Checklist

Ur Score games-and-modes shell. Spec: docs/spec.md, including the binding "Review round 1 amendments" (A1-A11).

## Build Preferences

- **Build mode:** Autonomous (owner away; fully-autonomous profile). The orchestrator dispatches each item to a
  subagent (bulk tier, Sonnet) with TDD, then verifies on the session model (judgment tier).
- **Comprehension checks:** N/A (autonomous mode)
- **Git:** One commit (or a few) per item on `feat/games-and-modes` in the worktree `../Ur-Score-modes`, conventional
  messages (`feat(games): ...`, `test: ...`), ending with the Co-Authored-By line. Never commit in the shared
  `Ur-Score` checkout. Never merge, tag, or touch master.
- **Verification:** Yes, by the orchestrator instead of the owner. After each item: build `Ur-Score.csproj`, then
  `dotnet test tests/Ur-Score.Tests.csproj --logger trx --results-directory <scratch>`, read the exit code and the
  trx counts (never the "Passed!" line), grep the output for `Aborted|crashed`, and compare the test count against the
  expected (previous + added - planned deletions, each deletion named). Every 3-4 items: a review pass over the diff
  (requesting-code-review) before continuing.
- **Check-in cadence:** N/A (autonomous). Smoke walks that move the real data folder are written but NOT run while
  the owner is away (they need RoRoRo quit and no battle recording); they're listed for his return.

## Checklist

- [ ] **1. Tests can't touch the real app or data folder**
  Spec ref: `spec.md > Test infrastructure first (port of K0ii fe8d103)` + `Review round 1 amendments > A10`
  What to build: Record the baseline test count (run the suite on the untouched branch first, trx + exit code). Port
  K0ii's fence: read `C:\Users\estev\Projects\K0ii-Score\tests\TestProcess.cs` (FencedTestFramework is a class inside
  it), `src/App.xaml.cs` (HostedByTests), `src/Core/AppPaths.cs` (RefuseDefault latch). Adapt to Ur Score's
  namespaces; add InternalsVisibleTo if needed. Add a deliberate-breach test that proves resolving the default data
  folder throws inside tests.
  Acceptance: `prd.md > Safe to build`: tests can't start the real app or resolve the real data folder; a breach fails
  loudly. No test lost.
  Verify: count after = baseline + new fence tests; exit code 0; the breach test fails when the latch is removed
  (check once, then restore).

- [ ] **2. A synthesized 0.6.3 install to test the upgrade against**
  Spec ref: `spec.md > Review round 1 amendments > A6`
  What to build: `tests/Fixtures/old-install-0.6.3/` (recipes/*.recipe.json + *.state.json, sources.json,
  settings.json without `modes`, boards.json, a scorebook folder): old top-clans text without `groupsAreClans`, a
  legacy `metricIdOverride` state, a state with `Stats` null, pinned `ps99.*` metric ids, `sentFieldMetrics`, an orphan
  `roblox-followers` recipe with a source and a book, Main/Mine/Watch sources, a following Battle tab and an edited
  board. Synthetic account ids only. Plus a characterization test loading it through today's `AppServices` seams
  (fenced) that pins current behaviour, so later items show exactly what changed.
  Acceptance: fixture loads under current code; no real handles or ids.
  Verify: suite green; `grep` the fixture for Este's handles from docs (none).

- [ ] **3. Reader state seeding and legacy states**
  Spec ref: `spec.md > Readers > Readers.Compose` + `A1` + `A8`
  What to build: `RecipeStats.SuggestedChoices(recipe)` (StatsTableModel.Suggested delegates to it, behaviour
  identical); `RecipeStore.LoadState(Recipe)` reachable from Readers (it needs the recipe for the legacy branch); a
  state with `Stats == null` seeds from `ChoicesForUpdate`.
  Acceptance: existing StatsTable tests unchanged; a legacy state yields its legacy choices; a missing state yields
  Suggested.
  Verify: tests green, count = previous + new.

- [ ] **4. Starter tabs survive being hidden**
  Spec ref: `spec.md > Board > Starter boards` + `A2`
  What to build: `BoardEmpty.ModeOff` and `BoardEmpty.NoModes` with their texts (BoardText) and buttons; StarterBoards
  builds an off starter with no panels and `ModeOff` (driven by a predicate passed in, so this item has no dependency
  on the catalog yet). Prove with a test that a saved following Battle tab survives: build with Battle off, save boards,
  build with Battle on, the tab and its arrangement are back.
  Acceptance: `prd.md > Turning modes and games on and off`: the starter tab comes back with the same arrangement.
  Verify: new Following/StarterBoards tests green; existing StarterBoardsTests green.

- [ ] **5. Disclosure without the import window**
  Spec ref: `spec.md > Review round 1 amendments > A4`
  What to build: rename `src/Recipes/ImportReview.cs` to `ReaderDisclosure.cs` (Review, SendsText kept), update
  TryCommand and callers; make host ordering a list in first-appearance order.
  Acceptance: TryCommand compiles and its tests pass; ImportReviewTests become ReaderDisclosureTests, same count.
  Verify: build + suite green; count unchanged.

- [ ] **6. The game manifest**
  Spec ref: `spec.md > Games` (Game manifest file, GameCatalog, Disclosure line) + `A1` (`shows`) + `A4`
  What to build: `games/pet-sim-99.game.json` (real slugs; Battle `shows` the clan-battle row stat, key resolved from
  `RecipeStats.Offered`), the csproj EmbeddedResource line, `src/Games/GameCatalog.cs`, `ModeLines.cs` (hosts line +
  sends clause from ReaderDisclosure), `ReaderNames.cs`. ManifestTests: every read resolves, no slug in two modes,
  boards are starters, `asks` is an input, `shows` keys are offered stats, ids unique; ModeLinesTests against the
  shipped recipes ("Reads ps99.biggamesapi.io every 3 min" for Battle, "every 30 min" for Profile plus the account-id
  clause).
  Acceptance: `prd.md > The game page`: the disclosure line is derived, never typed. NoHostnameFenceTests green.
  Verify: tests green; count = previous + new.

- [ ] **7. Mode switches and the upgrade map**
  Spec ref: `spec.md > Mode switches` + `A5`
  What to build: `Settings.Modes`; `ModeSwitches` (IsGameOn, IsModeSet, IsOn, IsReaderOn false for orphans, With);
  the one-time upgrade map: existing data folder + no `modes` → each mode on iff any of its readers had a
  `{slug}.recipe.json`; fresh install writes nothing.
  Acceptance: `prd.md > Upgrading from 0.6.3`: an upgrade reads exactly what 0.6.3 read; game-off remembers modes.
  Verify: ModeSwitchesTests + upgrade-map tests against the item 2 fixture variants (Battle-only, all three).

- [ ] **8. Readers come from the app, and off modes don't read**
  Spec ref: `spec.md > Readers` + `spec.md > Composition` + `A3` + `A5` + `A9`
  What to build: `Readers.Compose` (embedded text, saved/seeded state, `shows`, orphans); AppServices: `Catalog`,
  `Switches` derived from Settings, `Orphans`, `ActiveSources`, one `ApplyRunner()` used by both Runner.Apply sites,
  `_latest`/`_lastRead` pruned against ActiveSources, icon Keep over all Sources, `SetSwitch`, RequestsPerHour and
  HistoryBudget over ActiveSources; RecipeWatch cancellation checks before Backfill and before book.Append. Rewrite
  AppCompositionTests' fixture expectations in the same commit. UpgradeFrom063Tests: sources/roles/enabled kept, ticks
  and metric ids unchanged, orphan not read and kept on disk, nothing but settings.json written, book continuous across
  the old snapshot hash and the embedded hash (Ranking, Records, PastPeriods, Pace). ModeOffTests: no reads, nothing
  recorded or reported after off, fresh compose + one Main source gives `BoardEmpty.None` on Battle.
  Acceptance: `prd.md > Readers that stay current`, `> Upgrading from 0.6.3`, `> Turning modes and games on and off`.
  Verify: tests green; count = previous + new; diff of AppCompositionTests explained in the commit body.

- [ ] **9. Moving a setup between PCs without reader text**
  Spec ref: `spec.md > Setup transfer` + `A7`
  What to build: `SetupRecipe.Text` optional; ToFolder writes state only + `modes.json`, drops `activeRecipe`;
  FromFolder reads `*.state.json` or `*.recipe.json`; merge compares states by serialized JSON; `ISetupWriter.SaveState`;
  imported switches apply live; pack version bump; skipped-items line; BookImport skips orphan books with the new line;
  ImportPreviewModel labels by ReaderNames.
  Acceptance: `prd.md > Moving a setup to another PC`.
  Verify: SetupPack/SetupMerge/ImportPreview/BookImport tests updated + new (old-format export imports; new export has
  no text; orphan skipped); count recorded.

- [ ] **10. The board knows about modes**
  Spec ref: `spec.md > Board` (Starter boards, Panel off state, Race note) + `A10`
  What to build: StarterBoards and BoardWindow use the catalog/switches (ModeOff, NoModes, "Pick your clan" opening the
  game page), PanelFrame "{Mode} is off." + Turn on (`TurnOnModeButton`), orphan panels "Not part of any mode.",
  gallery omits types only off modes can feed, Race update note and `BuiltInRecipes.CanUpdate/HasGroupNamesUpdate` and
  `PanelText.RecipeUpdate` deleted, BoardWindow's NoRecipes/import branches and trail text replaced.
  Acceptance: `prd.md > Turning modes and games on and off`, `> First run` (board side).
  Verify: BoardText/StarterBoards/RaceBoard/Gallery tests updated + new; count recorded (RaceBoardTests' three update-
  note asserts deleted, named).

- [ ] **11. The game page replaces Recipes and Clans**
  Spec ref: `spec.md > Setup UI` (Page list, GamePage, ClansSection, Removed) + `A11` (focus)
  What to build: GameModel (pure) + GamePage.xaml + ClansSection (ClansPage's behaviour and AutomationIds), StartOnOpen
  checkbox moved, SetupPages/SetupWindow page list and FirstRunPage, first-run focus on the clan search; then delete
  ImportWindow, ImportText, ImportFlow, RecipesPage, RecipesModel, ClansPage and their tests (ImportFlowTests,
  ImportTextTests, RecipesModelTests, LegacyRecipeUpdateTests; SetupPagesTests rewritten; ConfirmsTests and
  SourceIconFenceTests references adjusted).
  Acceptance: `prd.md > The game page`, `> First run`.
  Verify: GameModelTests + rewritten SetupPagesTests + a WPF render test of GamePage (in WpfCollection) green; the
  planned drop is listed test by test in the commit body; Release build green.

- [ ] **12. No recipe words left in the UI**
  Spec ref: `spec.md > Setup UI > Other pages, wording only` + `A11` (word fence)
  What to build: reword Stats ("Mode"), Alerts, Accounts columns, Score book ("PER MODE"), Diagnostics (orphans
  section), PanelGallery, PanelForms, PanelText, AddBoardWindow, AlertCards, HistoryBudget, BookImport lines, per the
  PRD; then `RecipeWordFenceTests` with the allow-list from A11.
  Acceptance: `prd.md > No recipe words anywhere`.
  Verify: the fence test fails on a planted "recipe" string (check once), passes on the tree; count recorded.

- [ ] **13. Smoke walks for the new shape (written, run on owner's return)**
  Spec ref: `spec.md > File Structure > tools/smoke`
  What to build: retire `uia-import.ps1` and `walk-setup-clans.ps1` (clan assertions move into `walk-game-page.ps1`);
  new `walk-first-run.ps1`; every walk that seeded by importing now seeds by writing state/sources files through the
  existing data-folder helpers; tools/smoke/README.md updated. Parse-check every script (PowerShell parser, no run).
  Acceptance: scripts parse; README lists the run order and the RoRoRo-quit rule.
  Verify: `[System.Management.Automation.Language.Parser]::ParseFile` reports no errors for every changed script. Running
  them is deferred to Este (they move the real data folder aside).

- [ ] **14. Documentation, version and security pass**
  Spec ref: `prd.md > What We're Building` + `spec.md > Runtime & Deployment`
  What to build: README rewritten around games and modes (What leaves your machine per mode; settings reference with
  `modes`, no `activeRecipe`); recipes/README.md reframed as the reader format for developers; CHANGELOG 0.7.0 ("Recipes
  are gone. Pet Sim 99's modes are built in and update with the app." plus the upgrade rule); version 0.7.0 in
  csproj and manifest.json; docs/backlog.md rows for anything deferred; secrets scan of the diff (no webhooks, tokens,
  real account ids or handles); no dependency changes to audit. Final full-suite run + Release build. Push
  `feat/games-and-modes` and open a DRAFT PR (not merged).
  Acceptance: docs match the code; no secrets; branch pushed; draft PR open with the test count, the deletions and the
  deferred smoke walks listed.
  Verify: `git diff master --stat`; `git log -p master.. | grep -iE "webhook|token|password"` clean; PR URL.
