# The whole setup travels: one file, merged behind a preview

V3-S.46. Export stats / Import stats (0.5.5, the same day) move the score book alone, and the owner's second
machine had nothing to import because nothing exported. Now the setup goes with it — recipes with their
ticks, clans, boards, two settings — in the same file, and the receiving PC merges it behind a preview with
ticks. Keys never travel. Alerts stay RoRoRo's. The owner's rulings, 2026-09-22, in the order they were
given: merge with a preview first; one file for everything; tick what comes in.

The owner's second machine is a pristine older install, untouched since the three original recipe files
went in. It is the old-versus-new test bed Ur Score has never had, and §6 makes it a fixture before the
setup import ships.

## 1. The file

The 0.5.5 stats file, one folder richer. Same `.zip`, same dated name (`ur-score-stats-2026-09-22.zip`),
written by the same `BookPack.Write` through a temp file and one move.

```
manifest.json          v: 2, takenAt, app, readings, finals, setup: true|false
scorebook/…            the month files and the recipe texts the book keeps, exactly as in 0.5.5
setup/
  recipes/<slug>.recipe.json    the recipe text, as installed
  recipes/<slug>.state.json     RecipeState: ticks, metric ids, counter names, field numbers;
                                exclusions carried by ROBLOX USER ID, never RoRoRo's account GUIDs
  sources.json                  your clans, with the ids this machine minted
  boards.json                   as saved, sanitized: your own account ids only (BoardDefs.Sanitize)
  settings.json                 ResolveNames and ActiveRecipe. NOT StartOnOpen: a per-machine choice
  keys.json                     the NAMES (key ids and the recipe that wants each) of every key a recipe
                                declares. Never a value.
```

**Never in the file:** `keys.dat` (DPAPI, bound to the user on the machine that saved it), `accounts.json`
(RoRoRo's cache; the other RoRoRo lists its own), `icon-cache` (regenerable), `written-rules.json` or any
other file an older version left behind, and anything RoRoRo owns — alerts live in RoRoRo's rules file.

**Versioning, honestly.** The manifest goes to `v: 2`. A 0.5.5 importer refuses it as "exported by a newer
Ur Score — update this one" rather than silently importing only the stats. A 0.5.5 file (`v: 1`) imports
into the new version as stats only, and the preview says "This file holds stats and no setup." `setup:
false` is the same file without the folder, for a stats-only export; the button stays one button and the
manifest says which the file is.

**The export line** grows: "Exported 1,204 readings and 12 finished battles, with 3 recipes, 5 clans and
2 boards, to ur-score-stats-2026-09-22.zip. Import it on the other PC from Setup › Score book."

## 2. The plan and its rules

`SetupMerge.Plan(SetupPack file, SetupHere here)` is pure: it reads the file's setup and this machine's and
returns `SetupMergePlan` — a list of `SetupItem`s, each with a **kind**, a **name**, an **outcome**, a
**note** in words, and what it **depends on**. Nothing is written by planning.

### Identity: how "the same thing" is recognised

| Kind | Matched by | Outcome when both have it |
|---|---|---|
| Recipe | slug | **Update** if the file's text differs (the file wins, as the Recipes page's Update button already lets a newer built-in win), else **Same**. The file's state comes with an Update — sends off, see below; a Same keeps this machine's state. |
| Clan | recipe slug + `Source.KeyOf(inputs)` — the clan name case-insensitive and trimmed, the rule `BookMerge` uses | **Replace** if role or enabled differ, else **Same**. The receiving machine's ID IS KEPT, so its book lines and boards still point at it. |
| Board | name, case-insensitive and trimmed | **Replace** — the file's panels take the board's place. `Follows` and the starter boards are left alone: a following tab is not a board of yours to replace. |
| Key | the recipe's key id | always **Enter again**. A reminder, never a conflict. |
| Stats | — | one item, "N readings and M finished battles", merged after the setup exactly as 0.5.5 merges them. |

Anything only in the file is **Add**. Anything only here is **Kept**, listed so the preview is a complete
account of the machine afterward and not merely the diff.

### Dependencies, the one rule

A clan depends on its recipe: ticked in the plan, or already installed here. Unticking a recipe greys its
clans with "needs its recipe". A board depends on nothing: a panel whose clan did not come shows *stale*
with "Choose another", exactly as it does today after a clan is removed (`PanelHead.Stale`,
`BoardText`), so no cascade is needed and none is written.

### Send ticks arrive OFF

A recipe's state travels with its ticks — which stats to SHOW, their metric ids, counter names, the field
numbers ticked — but every **send** arrives off, whatever the file says. Raised by `rororoblox-fc` on
2026-09-22 while this was being written: a send that arrived on would start reporting to the receiving
PC's RoRoRo the moment the merge landed, on a machine whose owner never ticked it, and because metric ids
are fixed and shared by design, a rule already on that RoRoRo for the same id would fire on a clan its
owner never chose to watch — correctly worded and genuinely misleading. So a file from elsewhere cannot
start reports on its own, the same default `Copy-UrControlData` enforces for the smoke walks and for the
same reason. The preview says it in words: "Nothing will be sent from this PC until you tick it in
Setup › Stats." Turning sends on afterward is one visit to that page.

### Exclusions

`RecipeState.ExcludedAccountIds` holds RoRoRo account GUIDs, which differ per RoRoRo install. The file
carries them as Roblox user ids (looked up through this machine's `accounts.json` at export); on import
they are mapped back through the receiving machine's accounts. One that matches no account here is dropped
and counted in the recipe item's note ("1 excluded account not on this PC").

### Ids

The file's clan ids are never used here. An Added clan gets a freshly minted id (`SourceRules` mints them
today); a Replace keeps the local one. The plan carries the map file id → local id, and boards are
rewritten through it as `BookMerge` rewrites lines. A panel pointing at a clan that was unticked keeps the
file's id, which points at nothing here — which is what stale means.

## 3. The preview screen

One window, Ur Score's own and themed — never a stock dialog (owner rule, V3-S.10) — opened from Setup ›
Score book once the file is picked and unpacked. Title **Import from another PC**. It is the plan, drawn.

```
Import from ur-score-stats-2026-09-22.zip
Exported 22 Sep by Ur Score 0.5.5 on the other PC.

RECIPES
 [x] Pet Sim 99 clan battle points       Update — the file's copy is newer; your ticks come with it
 [x] Pet Sim 99 top clans                Add
     Pet Sim 99 profile                  Same as here

CLANS
 [x] CCGP · clan battle points · main    Replace — here it is watched; the file says main
 [x] K0i2 · clan battle points · yours   Add
     NovaForge · clan battle points      Kept — only this PC has it

BOARDS
 [x] Battle                              Replace — 8 panels here, 9 in the file
 [x] Alts                                Add

KEYS TO ENTER AGAIN
     ps99 api key · Pet Sim 99 profile   Keys never leave the PC that saved them. Setup › Recipes asks.

STATS
 [x] 1,204 readings and 12 finished battles — what is already here is skipped

Nothing will be sent from this PC until you tick it in Setup › Stats.
Your clans, recipes and boards here are copied aside first, dated, in case.
                                                            [Import ticked]  [Cancel]
```

Rules of the screen:

- A tick is on by default for every Add, Update and Replace. Same, Kept and Enter-again rows have no tick.
- Unticking a recipe greys its clans with "needs its recipe"; ticking it back restores them.
- The right-hand text is the plan's outcome in words, and a Replace says WHAT differs — role, enabled,
  panel count — so the decision is informed.
- The list is a `RowList` (named rows; ticks carry accessible names like the Stats table's), so a screen
  reader reads it and the smoke walk drives it. Automation ids: `ImportPreviewList`, `ImportTickedButton`,
  `ImportCancelButton`, each row's tick named "Import <name>".
- **Import ticked** applies (§4). **Cancel** writes nothing. The unpacked temp folder is removed either way.
- A `v: 1` file shows only the STATS group and the sentence "This file holds stats and no setup."

## 4. Applying, and what a failure leaves behind

`SetupMerge.Apply(plan, ticked, services)` runs off the UI thread, in dependency order, through the stores
that exist. Nothing writes a file by a new route.

1. **Aside first.** `sources.json`, `boards.json`, `settings.json` and the `recipes` folder are copied to
   `626labs.ur-score.before-import-<yyyyMMdd-HHmm>` beside the data folder. Kilobytes. The preview said
   so; the summary line names the folder.
2. **Recipes.** Each ticked Add or Update through `RecipeStore.Save(recipe, text, state)`, state with
   exclusions mapped to this machine's accounts. A recipe that fails to parse here is skipped and named
   (it parsed there, so this is a version gap, and the line says which slug).
3. **Clans.** One new `sources.json`: Kept as they are, Replace rewritten under the local id, Add minted
   fresh. Written once through `SourceStore.Save`, which already goes through a temp file and one move.
4. **Boards.** File boards rewritten through the id map, then `BoardDefs.Sanitize` with this machine's
   own account ids (the boards file's existing fence), pop-out rects clamped to this machine's work areas
   (`PopOutPlacement.Clamp`); Add appended, Replace swapped by name; written once through
   `BoardsFile.Save`, which keeps an unreadable old file beside itself as it does today.
5. **Settings.** The two carried values through `Settings.Save`.
6. **Reload, then stats.** `AppServices` reloads recipes, sources and boards and applies the watches —
   the path a Setup save takes — then the stats merge runs as 0.5.5's import does, then the book reloads.

**A failure mid-way is named, not hidden.** Each step is atomic on its own file, so a throw in step 4
leaves recipes and clans applied and boards untouched. The page's line says which step failed (the
exception's type to the trail, its redacted message on screen), that the earlier steps stand, and where
the aside copy is. There is no automatic roll-back: unwinding a partly-applied setup while watches may
already be reading is a second thing to get wrong, and the aside folder plus a plain sentence is the
honest recovery.

**The after-line:** "Imported 2 recipes, 3 clans and 2 boards; 1 clan kept as it was; 1 key to enter in
Setup › Recipes. Then 1,204 readings, 12 already here. Your previous setup is in
626labs.ur-score.before-import-2026-09-22-1431."

## 5. Where the code goes

- `src/Book/BookPack.cs` — the manifest gains `Setup`; `Write` takes an optional `SetupPack` and writes the
  `setup/` folder; `Open` reads `v` 1 or 2 and hands back the setup when there is one.
- `src/Core/SetupPack.cs` (new) — what travels: `SetupPack(Recipes, Sources, Boards, Settings, Keys)` with
  `FromHere(AppPaths, accounts)` (exclusions to Roblox ids, boards sanitized, keys named) and
  `ToFolder`/`FromFolder`. Pure.
- `src/Core/SetupMerge.cs` (new) — `Plan` and `Apply`, `SetupItem`, `SetupMergePlan`. Pure planning; apply
  through `ISetupServices`.
- `src/UI/Setup/ImportPreviewWindow.xaml(.cs)` (new) — the preview, themed, `RowList`-based.
- `src/UI/Setup/ScoreBookPage.xaml.cs` — Import stats… opens the file, then the preview; Export stats…
  writes setup and stats.
- `src/Composition/AppServices.cs` / `ISetupServices.cs` — `ExportStats` includes the setup; a
  `ReloadSetup()` for step 6; the accounts lookup the exclusions need.
- Button text stays **Export stats…** / **Import stats…** (the owner's names); the section sentence says the
  file now carries the setup too.

## 6. Testing, and the old-versus-new walk

Red first, every test (`docs/testing.md`).

- `SetupPackTests` — round trip: every recipe, state, clan, board and the two settings come back equal;
  `keys.json` holds names and no value (the decoy is a real-looking key string in a state; a pack that
  contains it fails); a `v: 1` file opens stats-only; the 0.5.5-shaped refusal of `v: 2` in the words a
  person sees.
- `SetupMergeTests` — the plan: each identity rule with a decoy differing only in case or spaces; each
  outcome (Add, Update, Same, Replace with each reason, Kept, Enter again); a state with every send on in
  the file arriving with every send off and every show as it was; dependency greying; exclusions
  mapped by Roblox id with one that matches nothing counted; the id map, and a board rewritten through it
  with a panel to an unticked clan left pointing at nothing. Apply, against real stores in a temp folder:
  the aside folder exists and holds the previous files; a throw injected in step 4 leaves steps 2–3
  applied and the line naming step 4.
- `AppCompositionTests` — two PCs, the whole setup: A composed with two recipes, three clans and an edited
  board; export; B composed pristine; import with everything ticked; B's watches read B's own ids, B's
  boards point at B's ids, the stats landed. Then into a B that already has one clan as WATCHED: the plan
  says Replace, and afterward B has one clan, not two.
- A fence: `keys.dat` never in a pack — `SetupPack` names its files explicitly and a test scans the zip.
- The smoke walk: export on a seeded folder, import into a fresh one, read every preview row by name,
  untick one clan, Import ticked, then the Clans page, the board, the aside folder,
  `check-boards-privacy.ps1`. One owner-watched launch, RoRoRo quit, seeded through `Copy-UrControlData`.

**Old versus new, the owner's second machine — before the setup import ships, as its own step.** Install
0.5.5 there, open it on the old data folder, and see what a folder written by the earliest version looks
like to the newest. Then copy that folder's non-secret files — no `keys.dat`, no `accounts.json` — into
`tests/Fixtures/old-install-2026-09/`, with account ids scrubbed as S1-F.11 did, and
`AppCompositionTests` composes the app on it from then on: the migration test Ur Score has never had. Only
the owner can supply that fixture; the plan puts it before the setup import ships.

## 7. Out of scope, said so nobody re-derives it

- Alerts: RoRoRo's rules file, RoRoRo's concern.
- Keys: never travel. The names travel so the reminder is exact.
- `StartOnOpen`: per machine.
- Ticking individual panels or individual stats: the ticks are per recipe, clan and board (the owner's
  ruling), and a finer grain can come later if wanted.
- Roll-back: the aside folder is the recovery.
- Sends: arrive off, always. A future "and send as I had it" tick on the preview would be a deliberate
  choice with the sentence above beside it, not the default.
