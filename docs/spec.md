# Ur Score: games and modes — Technical Spec

Implements `docs/prd.md`. Base: master b383b1a (v0.6.3). Branch: feat/games-and-modes.

## Stack

Unchanged: .NET 10, WPF, C# (net10.0-windows), xUnit, ROROROblox.PluginContract 0.10.0, PowerShell UIA smoke walks.
No new dependencies. References: docs/2026-09-13-recipes-design.md (engine, fence), docs/2026-09-14-score-book-design.md
(book versions by recipe hash), docs/2026-09-15-default-views-design.md (starters, `follows`),
docs/2026-09-22-setup-transfer-design.md (SetupPack, SetupMerge), docs/testing.md (WPF test rules: one Application per
process, WpfCollection fencing, exit-code discipline).

## Runtime & Deployment

A RoRoRo plugin, unchanged runtime. Data folder `%LOCALAPPDATA%\626labs.ur-score\` (AppPaths). Version bumps to
**0.7.0** in `Ur-Score.csproj` `<Version>` and `manifest.json` `version`; `minHostVersion` unchanged (1.28.0.0).

### Deployment — Identity & Signing

GitHub release on `estevanhernandez-stack-ed/Ur-Score`, tag `v0.7.0`, built by the existing `release.yml` (plugin.zip,
manifest, .sha256, recipes). Then the RoRoRo catalog bump (`ROROROblox/docs/store/plugins-catalog.json`
`latestVersion`, uploaded to the release marked Latest). **All of this is owner-gated and out of this cycle's build**:
the build ends with the branch pushed and a PR open, nothing merged or tagged.

## Architecture Overview

```text
 embedded resources                     data folder (%LOCALAPPDATA%\626labs.ur-score)
 ┌──────────────────────────┐           ┌───────────────────────────────────────────┐
 │ games/pet-sim-99.game.json│           │ recipes/{slug}.state.json  (user choices) │
 │ recipes/*.recipe.json     │           │ recipes/{slug}.recipe.json (IGNORED now,  │
 └────────────┬─────────────┘           │   except orphans listed in Diagnostics)   │
              │                          │ sources.json  settings.json (+ modes)     │
              ▼                          └───────────────┬───────────────────────────┘
       GameCatalog (parse + validate)                    │
              │                                          │
              ▼                                          ▼
       Readers.Compose(catalog, store) ──► Installed: embedded text + saved state (or Suggested)
              │                            Orphans:   data-folder recipes no mode names
              ▼
 AppServices ── ModeSwitches(settings.Modes, catalog) ── ActiveSources = Sources where mode on
     │                                                        │
     ├─► SourceHost.Apply(ActiveSources)  (reads only for on modes)
     ├─► StarterBoards / BoardWindow tabs (hide starters of off modes)
     ├─► Panels: source → recipe slug → mode → "Battle is off" when off
     └─► Setup: GamePage (toggles, ClansSection per asked input, disclosure line, StartOnOpen)
```

## Games

### Game manifest file

`games/pet-sim-99.game.json`, embedded (`<EmbeddedResource Include="games\*.game.json" />` in Ur-Score.csproj next to
the recipes line; excluded from tests the same way). Format version field `"game": 1`.

```json
{
  "game": 1,
  "id": "pet-sim-99",
  "name": "Pet Sim 99",
  "modes": [
    { "id": "battle", "name": "Battle",
      "blurb": "Your clan's battle: place, race, pace and the top of the battle.",
      "reads": ["pet-sim-99-clan-battle-points", "pet-sim-99-top-clans"],
      "asks": "clan", "board": "battle", "onByDefault": true },
    { "id": "profile", "name": "Profile",
      "blurb": "Rank, diamonds, hatches and playtime for each of your accounts.",
      "reads": ["pet-sim-99-profile"],
      "note": "Needs each account linked on db.biggames.io with its Profile view public.",
      "board": "alts", "onByDefault": true }
  ]
}
```

Slugs are real recipe slugs (`Recipe.Slug` = Slugify(Name [+ Author])); the clan-battle file's slug is
`pet-sim-99-clan-battle-points`. `asks` names an input id of one of the mode's recipes. `board` is a starter key
(`StarterBoards.KeyOf`).

### GameCatalog (src/Games/GameCatalog.cs)

- `record GameDef(string Id, string Name, IReadOnlyList<ModeDef> Modes)`;
  `record ModeDef(string GameId, string Id, string Name, string Blurb, IReadOnlyList<string> Reads, string? Asks,
  string? Note, string? Board, bool OnByDefault)` with `Key => $"{GameId}/{Id}"`.
- `GameCatalog.BuiltIn` (lazy): parses every embedded resource ending `.game.json` (same assembly-resource pattern as
  `BuiltInRecipes.Read`), ordered by resource name.
- `Parse(string json)` returns `GameDef` or throws `GameManifestException` with a precise message (unknown `game`
  version, missing id/name, empty modes, duplicate mode id).
- `Validate(GameCatalog, IReadOnlyDictionary<string, Recipe> builtIns)` returns problems: unknown slug in `reads`, a
  slug in two modes (across all games), `board` not a starter key, `asks` not an input of the mode's recipes, mode with
  no reads. Called by tests (ManifestTests) and, defensively, at startup: a mode with problems is dropped and a trail
  line written (never a crash).
- Lookups: `ModeOf(string slug) → ModeDef?`, `Find(string modeKey)`, `Games`.
PRD ref: `prd.md > The game page`, `prd.md > Safe to build`.

### Disclosure line (src/Games/ModeLines.cs)

`ModeLines.Reads(ModeDef, IReadOnlyList<InstalledRecipe>)`: distinct hosts across every step URL of the mode's recipes
(use `RecipeHosts`), ordered by first appearance, and the shortest `EffectiveEverySeconds` (with the 60 s floor the
runner applies) as minutes: "Reads ps99.biggamesapi.io every 3 min". Hosts that come only from input `search` URLs are
included (the clans list is fetched from it). Pure; tested against the shipped recipes.
PRD ref: `prd.md > The game page`.

## Mode switches

### Settings (src/Core/Settings.cs)

Add `IReadOnlyDictionary<string, bool>? Modes = null` to the `Settings` record. Keys: a game id (`"pet-sim-99"`) or a
mode key (`"pet-sim-99/battle"`). Missing key = default (game: on; mode: `OnByDefault`). `ActiveRecipe` stays on the
record for reading old files but is no longer written by SetupPack (it was never read). **Superseded by fix round 2:** `SettingsVersion` 3 marks the modes decision as made; the upgrade map runs only for a
readable file below v3, and an unreadable settings.json is never written (defaults in memory).

### ModeSwitches (src/Games/ModeSwitches.cs)

Pure: `ModeSwitches(GameCatalog, IReadOnlyDictionary<string,bool>? modes)`.
- `IsGameOn(gameId)`, `IsModeSet(modeKey)` (the mode's own switch), `IsOn(modeKey)` = game on AND mode set.
- `IsReaderOn(slug)`: true when its mode `IsOn`; **false for a slug no mode names** (orphans never read).
- `With(key, bool)` returns the new dictionary (only the changed key is written; defaults stay implicit).
PRD ref: `prd.md > Turning modes and games on and off`.

## Readers (replacing the data-folder install list)

### Readers.Compose (src/Games/Readers.cs)

`Compose(GameCatalog, IReadOnlyDictionary<string, BuiltInRecipe>, RecipeStore) → (Installed, Orphans, Problems)`:
- For every slug any mode reads, in manifest order: parse the embedded text (BuiltInRecipes), load
  `{slug}.state.json` via a new `RecipeStore.LoadState(slug)` (the existing per-file loader, incl. legacy
  `metricIdOverride` migration); if no state file exists, seed `new RecipeState { Stats = StatsTableModel.Suggested }`
  (move `Suggested` into `src/Recipes/RecipeStats.cs` as `RecipeStats.SuggestedChoices(recipe)` so the composition
  layer doesn't call UI code; StatsTableModel delegates to it). Sends arrive OFF, exactly as an import did.
- `InstalledRecipe(Recipe, Text: embedded text, State)`: the TEXT is always the embedded copy.
- Orphans: `RecipeStore.LoadAll()` results whose slug no mode names. Kept as a list for Diagnostics only; never read,
  never written, never deleted.
- `RecipeStore` keeps `LoadAll` (orphans) and `SaveState`; `Save`/`Remove` of recipe text become unused by the app
  (keep `Save` for tests/CLI only if referenced; delete `Remove` with RecipesModel).
- A seeded state is NOT written to disk until the user changes a choice (so an upgrade writes nothing).
PRD ref: `prd.md > Readers that stay current`, `prd.md > Upgrading from 0.6.3`.

### Score book continuity

`RecipeWatch` hashes the text it reads with (`BookFiles.Hash`) and the book stores each version under
`scorebook/{slug}/recipes/{hash}.json`; lines carry `BookRecipeRef(slug, hash)`. An upgrade whose data-folder snapshot
differs from the embedded text simply starts a new version, which is what every recipe Update did since V3-S.42.
Required test: a book written under the old snapshot's hash plus new lines under the embedded hash reads as one
continuous series (Ranking, Records, PastPeriods, Pace) for the same source.

## Composition (src/Composition/AppServices.cs, ISetupServices.cs)

- `LoadInstalled` calls `Readers.Compose`; exposes `Installed`, `Orphans`, `RecipeProblems` (now: manifest drops +
  unparseable embedded text, which a test makes impossible).
- New `Catalog` (GameCatalog) and `Switches` (ModeSwitches built from Settings).
- `ActiveSources => Sources.Where(s => Switches.IsReaderOn(s.Recipe))`. `ApplySources` passes `ActiveSources` to
  `Runner.Apply` (enabled sources of off modes are dropped exactly like disabled ones: SourceHost cancels the watch).
- New `SetSwitch(string key, bool on)`: `Settings with { Modes = Switches.With(key, on) }`, save, rebuild Switches,
  `ApplySources()`, raise the existing changed event so Setup pages and BoardWindow re-render.
- Recording after off: SourceHost cancels the removed watch's token; add a test that a read completing after cancel
  writes no book line and reports nothing (if the current loop can record after cancel, guard the record call with
  `stop.IsCancellationRequested`).
- `LoadAtStart` unchanged in logic: `SourceRules.Migrate(Installed, [])` on a fresh install now sees the embedded
  readers, so profile gets its Mine source and top clans its Watch source automatically, as an import did.
- Remove `ReloadRecipes`/`RemoveRecipe` and the import entry points from ISetupServices; keep `SaveRecipeState`.
PRD ref: all epics.

## Setup UI

### Page list (src/UI/Setup/SetupPages.cs)

`For(catalog)`: one page per game (`game:{id}`, title = game name), then Your accounts, Stats, Alerts, Score book,
Diagnostics. `Recipes`, `ClansId`, `HasClansPage` removed. `StartPage`: `FirstRunPage ?? first page`.
`FirstRunPage(catalog, switches, installed, sources)`: the game page when an ON mode `asks` an input and no source for
that mode's asking recipe exists; else null. SetupWindow maps `game:{id}` to `GamePage`.

### GamePage (src/UI/Setup/GamePage.xaml + GameModel.cs)

- Header: game name, game toggle (ToggleButton styled like existing switches, AutomationId `GameSwitch`).
- Per mode (ItemsControl over `GameModel.Modes`): name, toggle (`ModeSwitch_{modeId}`), blurb, `ModeLines.Reads`
  line, note (if any), and for a mode with `asks`: a `ClansSection` bound to the asking recipe's slug.
  Off mode: section `IsEnabled=false`, line "Turn Battle on to read these clans."
- Footer: the **Start reading when Ur Score opens** checkbox (AutomationId `StartOnOpenBox`, same as today's), moved
  verbatim from RecipesPage with its handler.
- GameModel is pure (rows from catalog + switches + installed); tests in GameModelTests.

### ClansSection (src/UI/Setup/ClansSection.xaml)

ClansPage refactored into a UserControl with identical behaviour (Main/Mine/Watch lists, ClanSearchBox, probe, Watch
it instead, >5 confirm, requests line, Top switch) and the same AutomationIds, so walk-setup-clans' assertions port
by changing only how the page is reached. ClansModel unchanged except wording. Strings that named the recipe
("From {recipe.Name}…", "This recipe is no longer installed") are reworded per mode or deleted.

### Other pages, wording only

Stats ("Mode" combo of on modes' non-group-list readers, labelled by mode name), Alerts (per mode), Accounts (columns
headed by mode name), Score book ("PER MODE"; Battle's clans list labelled "Battle · clans list"), Diagnostics
(human lines by mode; an "Not part of any mode (kept, not read)" section for Orphans; copy lines keep slugs). A single
helper `ReaderNames.For(slug, catalog)` gives the label: the mode name, plus " · clans list" for a group-list reader
when its mode has another reader.

### Removed

`src/UI/ImportWindow.xaml(.cs)`, `src/UI/ImportText.cs`, `src/UI/Setup/ImportFlow.cs`, `RecipesPage.xaml(.cs)`,
`RecipesModel.cs`, `ClansPage.xaml(.cs)` (replaced by ClansSection), `src/Recipes/ImportReview.cs` (only used by the
import path), `BuiltInRecipes.CanUpdate`/`HasGroupNamesUpdate` and `PanelText.RecipeUpdate`. **Kept:**
ImportPreviewWindow/ImportPreviewModel (stats-file import), `src/Cli/TryCommand.cs` (developer tool), RecipeParser,
RecipeEngine, KeyStore, NoHostnameFenceTests.

## Board

### Starter boards (src/Board/StarterBoards.cs)

`All(installed, sources, switches, catalog)`: a starter whose mode is off is omitted (the mode that names it in
`board`). Saved boards with `Follows` = an omitted starter are hidden from the tab strip but kept in boards.json.
`BoardEmpty.NoRecipes` → `BoardEmpty.NoModes` ("Turn on a mode", button opens the game page). `NoSources` for a mode
that asks → "Pick your clan" (button opens the game page with the clan search focused).

### Panel off state

A panel whose `Settings.Recipe` (or its source's recipe) belongs to an off mode renders the existing PanelFrame empty
note: "{Mode} is off." with a **Turn on** button (AutomationId `TurnOnModeButton`) calling `SetSwitch(mode.Key, true)`.
A panel whose recipe is an orphan says "Not part of any mode." with no button. The gallery (+ Add panel) omits panel
types whose only possible sources belong to off modes.

### Race note

Delete the `NamesNote` update branch (PanelModels.Race.cs ~271-285) and `PanelText.RecipeUpdate`; the remaining
"rival lines stopped" note stays (it can still happen if a clan source was off).

## Setup transfer (src/Core/SetupPack.cs, SetupMerge.cs)

- `SetupRecipe.Text` becomes optional (`string? Text`). `FromHere` sets it null; `ToFolder` writes only
  `recipes/{slug}.state.json` (no `.recipe.json`); `FromFolder` reads a `.recipe.json` if present (old exports) and
  ignores it beyond the slug.
- New `setup/modes.json` (the `Modes` dictionary). `settings.json` in the pack drops `activeRecipe` on write, tolerates
  it on read.
- SetupMerge recipe items: for a slug a mode reads → merge state only (Same/Update by state); for any other slug →
  skipped, summary "N item(s) skipped: not part of any mode". No recipe text is ever saved by a merge. Clans whose
  recipe is skipped are skipped with it. Modes from the file apply as an item ("Mode switches").
- ImportPreviewModel labels items by `ReaderNames.For`.

## Test infrastructure first (port of K0ii fe8d103)

Port from `C:\Users\estev\Projects\K0ii-Score` (read its tests/TestProcess.cs, FencedTestFramework, App.HostedByTests,
AppPaths.RefuseDefault in that merge): a ModuleInitializer in the test assembly sets `App.HostedByTests = true` and
latches `AppPaths.RefuseDefault`, so `App.OnStartup` returns early and any resolution of the real default data folder
throws; `FencedTestFramework` fails the run if a real startup or composition happened. Adapt names to Ur Score's
namespaces (`Labs626.UrScore`). Verify by counting tests before/after (no drop) and by a deliberate breach test.

## Data Model

- settings.json: `{ "resolveNames": true, "startOnOpen": true, "settingsVersion": 2, "modes": { "pet-sim-99/battle": false } }`
  (`activeRecipe` may linger in old files; never written by new exports, left alone in settings.json itself).
- recipes/{slug}.state.json: unchanged shape (RecipeState).
- sources.json: unchanged shape; `Source.Recipe` is a slug.
- boards.json: unchanged.
- Setup pack folder: `recipes/{slug}.state.json`, `exclusions.json`, `sources.json`, `boards.json`, `settings.json`,
  `keys.json`, new `modes.json`.

## File Structure (changes only)

```text
games/
└── pet-sim-99.game.json            # NEW embedded manifest
src/Games/                          # NEW
├── GameCatalog.cs                  # GameDef/ModeDef, parse, validate, lookups
├── ModeSwitches.cs                 # on/off resolution from Settings.Modes
├── ModeLines.cs                    # "Reads host every N min"
├── ReaderNames.cs                  # user-facing label for a reader slug
└── Readers.cs                      # embedded text + saved state → Installed; orphans
src/Core/Settings.cs                # + Modes
src/Core/SetupPack.cs, SetupMerge.cs# text-free transfer, modes.json
src/Recipes/RecipeStore.cs          # + LoadState(slug); Remove deleted
src/Recipes/RecipeStats.cs          # + SuggestedChoices (moved from StatsTableModel)
src/Recipes/BuiltInRecipes.cs       # update-detection removed
src/Composition/AppServices.cs      # Catalog, Switches, ActiveSources, SetSwitch, Orphans
src/Board/StarterBoards.cs          # mode-aware, NoModes
src/Board/PanelModels.Race.cs, PanelText.cs, PanelGallery.cs, PanelForms.cs  # wording, off state, race note
src/UI/Setup/GamePage.xaml(.cs), GameModel.cs, ClansSection.xaml(.cs)        # NEW
src/UI/Setup/SetupPages.cs, SetupWindow.xaml.cs                               # page list
src/UI/Setup/{Stats,Alerts,Accounts,ScoreBook,Diagnostics}*                   # wording, labels
src/UI/BoardWindow.xaml.cs, BoardText.cs, Panels/PanelFrame.xaml             # empty states, off panels
DELETED: src/UI/ImportWindow.*, src/UI/ImportText.cs, src/UI/Setup/ImportFlow.cs, RecipesPage.*, RecipesModel.cs,
         ClansPage.*, src/Recipes/ImportReview.cs
tests/
├── TestProcess.cs, FencedTestFramework.cs (port)       # NEW
├── ManifestTests.cs, ModeSwitchesTests.cs, ModeLinesTests.cs, ReadersTests.cs, GameModelTests.cs   # NEW
├── ModeOffTests.cs (composition: no reads, no record after off, starters hidden, panel off state)  # NEW
├── UpgradeFrom063Tests.cs (old-install fixture + book continuity)                                 # NEW
├── RecipeWordFenceTests.cs (no "recipe" in UI strings outside the allow-list)                    # NEW
└── deleted with their code: ImportFlowTests, ImportTextTests, RecipesModelTests, LegacyRecipeUpdateTests,
    ImportReviewTests; rewritten: SetupPagesTests
tools/smoke/
├── walk-game-page.ps1, walk-first-run.ps1              # NEW
├── uia-import.ps1, walk-setup-clans.ps1                # RETIRED (clans assertions move into walk-game-page)
└── window-smoke, walk-starter-board, walk-stats-table, walk-alts, walk-alerts, walk-score-book,
    walk-visible-fixes, walk-top-bar, walk-board-editing, uia.ps1, uia-board.ps1   # updated: no import step;
                                                                                    # seed via the data folder
```

ClansModelTests stay (ClansModel survives); only their wording asserts change. ImportPreviewModelTests and
ImportPreviewRenderTests stay.

## Key Technical Decisions

1. **Manifest per game, recipes stay as plumbing** (owner, 2026-10-05). Tradeoff: one more file type and an
   indirection, in exchange for game knowledge staying in data and the host fence holding.
2. **Embedded text is the only reader text.** Tradeoff: a user can no longer pin an older reader; accepted, since
   updates-with-the-app is the point and the book is multi-version by design.
3. **Mode-off is a gate above Source.Enabled**, applied in AppServices, not by flipping sources. Tradeoff: two notions
   of "off" exist in code; accepted so a user's per-clan choices survive any number of mode toggles.
4. **Seeded state is not written until changed.** Tradeoff: a fresh install's ticks live in memory until first edit;
   accepted so an upgrade writes nothing to the data folder (PRD).

## Dependencies & External Services

None new. Hosts read are unchanged (ps99.biggamesapi.io via the shipped recipes; users.roblox.com and
thumbnails.roblox.com via Fetch/NameClient and IconClient).

## Review round 1 amendments (binding: these supersede any earlier section they contradict)

From an adversarial review against b383b1a (2026-10-05). Each was verified in code by the reviewer with file:line.

- **A1 Battle seed tick (critical).** The clan-battle recipe marks no value `show`, so a seeded Battle state ticks
  nothing and StarterBoards returns NoStats before NoSources: the Battle board could never fill on a fresh install.
  Fix: manifest modes gain `"shows": ["<slug>:<statKey>", ...]`, applied by `Readers.Compose` on top of
  `SuggestedChoices` when it seeds a state (never over a saved one). Battle shows the clan-battle row value (resolve
  the exact stat key from `RecipeStats.Offered` at build). The recipe text is NOT edited (no hash change). Test: fresh
  compose plus one Main source gives `BoardEmpty.None` on the Battle starter.
- **A2 Starters are never omitted (critical).** `Following.All` drops saved entries whose starter is missing
  (Following.cs:67), so omitting the Battle starter and then saving any board would delete the tab for good. Fix: an
  off mode's starter is still built, with no panels and new `BoardEmpty.ModeOff` ("Battle is off", Turn on button);
  `Shown` already hides following boards with 0 panels and `ToSave` keeps them. A board the user edited
  (`Follows = null`) stays visible and its panels show the off state; documented, not a bug.
- **A3 One runner gate.** `LoadBookOnceAsync` calls `Runner.Apply(Sources)` directly (AppServices.cs:407). Both sites go
  through one `ApplyRunner()` using `ActiveSources`; `_latest`/`_lastRead` prune against `ActiveSources`.
  `_sourceIcons.Keep` keeps using ALL `Sources` (or a toggle would forget clan icons). `ClansModel.RequestsPerHour` and
  `HistoryBudget.AfterSeed` use `ActiveSources`.
- **A4 Disclosure keeps its content; ImportReview is renamed, not deleted.** `ImportReview.Review`/`SendsText`
  become `src/Recipes/ReaderDisclosure.cs` (TryCommand compiles against it). `ModeLines` is built from it: ordered
  hosts (a list, not a set), icon hosts (thumbnails + picture host) when a reader has an icon, and the "sends your
  accounts' Roblox ids" clause for per-account readers. The game page shows the hosts line and, under it, the sends
  clause when present.
- **A5 Upgrade reads exactly what 0.6.3 read.** First 0.7.0 start with an existing data folder and no `modes` key:
  write an explicit `modes` map, each mode on iff any of its readers had a `{slug}.recipe.json` in the data folder
  (game on). Fresh install: no map written, manifest defaults. `SourceRules.ForNewRecipes` then only sees readers of on
  modes, so no surprise sources. Accepted writes after that are 0.6.3's normal ones (counter names, icon index); spec
  decision 4 is narrowed to "seeding itself writes nothing". FirstRunPage only fires for an on mode, so a Battle-less
  upgrader is never nagged.
- **A6 The old-install fixture is synthesized** (the real one was never supplied, backlog row). First checklist item
  builds `tests/Fixtures/old-install-0.6.3/`: old top-clans text without `groupsAreClans`, a legacy
  `metricIdOverride` state, pinned `ps99.*` metric ids, `sentFieldMetrics`, an orphan `roblox-followers` recipe with a
  source and a book, sources with Main/Mine/Watch, a boards.json with a following tab and an edited tab. Account ids
  synthetic.
- **A7 SetupPack reads both shapes.** `FromFolder` enumerates `*.state.json` OR `*.recipe.json`; merge compares states
  by serialized JSON (records hold dictionaries by reference); `ISetupWriter.SaveRecipe(recipe,text,state)` becomes
  `SaveState(slug,state)`; `ReloadRecipes` leaves the writer interface; imported mode switches apply live (Switches is
  derived from Settings in `SaveSettings`, which then calls `ApplySources`); the pack manifest version bumps so 0.6.3
  says "made by a newer Ur Score"; `SetupMerge` stops writing `ActiveRecipe`. `BookImport` skips books of orphan slugs
  with "not part of any mode" instead of "No recipe here for".
- **A8 Legacy states.** `RecipeStore.LoadState(Recipe)` is made internal-visible to Readers (it needs the recipe for the
  `metricIdOverride` branch). A state file that exists with `Stats == null` seeds from `ChoicesForUpdate` (legacy) and
  is tested; otherwise the legacy choices would send nothing forever now that ImportWindow is gone.
- **A9 Record after off.** RecipeWatch already throws on cancellation before recording (RecipeWatch.cs:393, S1-8.3);
  add the same check before `Backfill` (:440) and immediately before `book.Append`. Backfill on re-enable writes REAL
  past periods from the source; that is allowed. "Never filled" means nothing is invented.
- **A10 Couplings to update in the same task as their cause:** BoardWindow.xaml.cs:158/925/935/952 and its :173 trail
  text; PanelModels.Race.cs:282; AppCompositionTests (9 sites write fixture top-clans text that differs from shipped,
  so expected hashes and Same/Update change); K0ii's fence lives in tests/TestProcess.cs (FencedTestFramework is a
  class inside it) and needs InternalsVisibleTo.
- **A11 PRD homes that were missing:** first-run focus on Battle's clan search (GamePage sets focus when FirstRunPage
  opened it); README rewrite + settings reference (`modes`, drop `activeRecipe`); CHANGELOG 0.7.0; the word fence scans
  `src/**/*.xaml` text-bearing attributes (Text, Content, Header, Title, ToolTip, AutomationProperties.Name) and C#
  string literals under `src/UI/**` and `src/Board/**` for `\brecipes?\b` (case-insensitive), allow-listing
  `src/Cli/**`, `src/Recipes/**`, Diagnostics copy lines (`DiagnosticsModel` lines that start with `recipe=`/`source=`);
  the pack carries one setting (ResolveNames) plus `modes`.

## Open Issues

- Top switch on ClansSection is effectively unreachable today (the top-clans group list has no inputs). Ported as is;
  candidate for removal later. (default — confirm on next interactive run)
- `StatsTableModel.Suggested` move must keep StatsTable behaviour identical; the stats walk covers it.
- Smoke walks that seed by importing must seed by writing state/sources files instead (`Copy-UrControlData` pattern)
  since there is no import path; that is mechanical but touches ~10 scripts.
- RoRoRo's catalog text or the store description may say "recipes"; out of this repo, flagged for the owner.
