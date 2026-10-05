# Ur Score: games and modes

## Idea

Ur Score stops asking players to understand "recipes". It shows the games it knows, each with a toggle, and each game
offers modes built from what that game's API can measure. Pet Sim 99 ships with Battle and Profile; Loot follows in the
next cycle as the first mode built natively on this shell. The recipe engine stays, as plumbing nobody sees.

## Who It's For

The Pet Sim 99 player running Ur Score through RoRoRo, most often one main plus a few alts, who wants their clan's
battle and their accounts' numbers on a board without learning a data-file format. Este is the first such user and the
owner; his own verdict on recipes after a month of daily use was "I still don't get that." If the owner doesn't get the
concept, a new player won't either.

The unmet need today: a new player's first screen is "Import a recipe" with a file picker, Setup has a Recipes page and a
Clans page per recipe, and a fix to a shipped recipe needed a manual Setup > Recipes > Update step (backlog V3-S.55).

## Inspiration & References

- Este, 2026-10-05: "We have the api, we know what we can measure. We scaffold modes and offer them for known games."
- The approved brainstorm, logged decision by decision in the 626 dashboard (RoRoRo Plugins umbrella,
  Rp2A2gIjIFpdnsuw7dVa, entries of 2026-10-05: games-and-modes direction, manifest choice, Recipes page removal,
  design sections 1 to 3).
- docs/2026-09-13-recipes-design.md: why recipes exist (no vendor host in the binary; NoHostnameFenceTests).
- docs/2026-09-15-default-views-design.md: the Battle and Alts starter boards.
- docs/2026-09-22-setup-transfer-design.md: SetupPack, which carries recipe text today.
- K0ii Score (Projects/K0ii-Score, fe8d103): the test-harness fence to port first.
- No external web research: this is an internal reshaping of a shipped app, and the references are its own docs.
  (default — confirm on next interactive run)

## Goals

- No recipe concept anywhere a user can see it: no Recipes page, no import window, no "recipe" in UI text.
- A first run that opens on the game page, asks for a clan only if Battle is on, and fills the Battle board.
- Built-in readers update with the app.
- Every existing install keeps its score book, its stat ticks, its metric ids (so RoRoRo alert rules don't move), its
  sources' clans and roles, and its boards.
- The privacy promise ("you see every host your PC will contact before it does") survives, moved from the import
  screen to a line under each mode's toggle.
- Leave a clean seam for spec 2: adding Loot is adding a mode to the manifest plus its reader and panels.

## What "Done" Looks Like

Setup lists: Pet Sim 99 (game page), Your accounts, Stats, Alerts, Score book, Diagnostics. The game page shows Pet Sim
99 with an on/off toggle, and under it Battle and Profile, each with a toggle, a one-line blurb, the line "Reads
ps99.biggamesapi.io every N min" worked out from its recipes, Battle's clan box, and Profile's db.biggames.io linking
note. Turning a mode off stops its reads, hides its starter tab, and shows "Battle is off" with a Turn on button on its
panels on custom boards; the book is kept and the off period stays an honest gap. An upgrade from 0.6.3 lands with
modes on, everything else as it was. A fresh install opens on the game page with no file picker. Release 0.7.0 is
ready on the branch; merge, tag and catalog bump wait for Este.

## What's Explicitly Cut

- **Custom recipe import, outright** (Este's call B): no Recipes page, no hidden Diagnostics link. Only manifest games
  exist. Revisit if someone wants a game Ur Score doesn't ship.
- **Ripping out the recipe engine** (option B of "moving away"): the engine and the no-hostname fence stay; game
  knowledge stays in shipped data files.
- **The Loot mode itself:** spec 2.
- **A second game:** the manifest supports N games; a game picker only appears when there are 2+.
- **Account discovery from Roblox logs** (K0ii's Labs626.Watch): RoRoRo already supplies saved accounts.
- **Deleting anything on disk during migration:** orphaned recipe files and books are switched off and kept.

## Loose Implementation Notes

- One embedded manifest per game, `games/pet-sim-99.game.json`: modes with id, name, blurb, reads (recipe slugs),
  asks, note, board, onByDefault. Host and cadence derived from the recipes, never duplicated.
- Recipe text always comes from the embedded copy; data-folder snapshots are ignored. `{slug}.state.json` choices stay.
- Settings gains a `modes` map; a missing key means the manifest default.
- SetupPack carries slugs, choices and mode toggles, not recipe text; old exports import with text ignored.
- `--try` stays as a developer tool (we author readers now, and Loot's reader will be checked with it).
- First task: port K0ii's harness fence so tests can't boot the real app against the real data folder.
- Release 0.7.0.
