# Ur Score: games and modes — Product Requirements

## Problem Statement

Every Ur Score player meets "recipes" before they see a single number: the first screen is "Import a recipe" with a
file picker, Setup has a Recipes page plus a Clans page per recipe, Stats, Alerts, Accounts and Score book are labelled
by recipe name, and a fix to a shipped recipe never reached a player until they found Setup > Recipes > Update. The
owner still doesn't get the concept after a month of daily use. The cost is that a new player bounces at first run
and an existing one runs stale readers without knowing it.

## Vocabulary (what the user sees)

- **Game**: a game Ur Score knows (Pet Sim 99). It has an on/off toggle.
- **Mode**: something a game can show you, built from what its API measures: **Battle** (your clan's battle: place,
  race, pace, top of the battle) and **Profile** (rank, diamonds, hatches per account). Each mode has an on/off toggle.
- The words "recipe", "import a recipe", "installed" never appear in the UI. "Import" survives only for the
  stats-file import from another PC (Setup > Score book), which is unrelated.

## User Stories

### Epic: The game page

- As a player, I want one page that shows the game and what it can do, so I can turn things on without learning a
  file format.
  - [ ] Setup's page list is, in order: **Pet Sim 99**, Your accounts, Stats, Alerts, Score book, Diagnostics. No
        Recipes page, no Clans page.
  - [ ] The Pet Sim 99 page shows the game name with an on/off toggle, and under it one row per mode in manifest order:
        Battle, then Profile.
  - [ ] Each mode row shows: its name, its toggle, a one-line blurb, and a line "Reads ps99.biggamesapi.io every
        N min" (several hosts listed comma-separated; the shortest cadence shown when its readers differ, e.g.
        "every 3 min").
  - [ ] Profile's row shows its note: "Needs each account linked on db.biggames.io with its Profile view public."
  - [ ] The page carries the **Start reading when Ur Score opens** checkbox (moved from the Recipes page), same
        behaviour and same setting as today.
  - [ ] The hosts line is never typed by hand: it is worked out from the recipes the mode reads, so it can't drift.

- As a player in a clan, I want to pick my clan inside Battle, so the clan lives with the mode that uses it.
  - [ ] Battle's row holds everything today's Clans page does: the ★ main clan with a search box, "Add a clan your
        accounts are in" (Mine), "Watch a clan" (Watch), each row's chip ("★ main" / "yours" / "watching"), Make main,
        Remove, the more-than-5 confirm, and the per-host requests-per-hour line.
  - [ ] After a pick, the one-time probe still runs, and "Watch it instead" is still offered when none of my accounts
        were found in that clan.
  - [ ] Clan search works as today (the clans list searched as I type; "Type the exact clan name" when the list can't
        be read).
  - [ ] When Battle is off, its clan section is shown but dimmed, with "Turn Battle on to read these clans."

### Epic: Turning modes and games on and off

- As a player, I want to switch a mode off without losing anything, so I can stop reading what I don't use.
  - [ ] Turning a mode off stops every read for that mode's readers within one cycle; Diagnostics shows its sources
        as "off (Battle is off)", not as errors.
  - [ ] Its starter tab (Battle for Battle, Alts for Profile) disappears from the tab strip; turning the mode back on
        brings the tab back with the same arrangement.
  - [ ] A panel of that mode on any other board shows "Battle is off" with a **Turn on** button, which turns the mode on.
  - [ ] Nothing is deleted: the score book, the clans, the stat ticks and the alert ids all stay.
  - [ ] Turning it back on resumes reading; the time it was off is a gap in charts, never filled in.
  - [ ] My clans' own on/off state (a source's Enabled) is untouched by the mode toggle; mode-off is a separate gate.
- As a player, I want a game toggle, so one switch silences everything for that game.
  - [ ] Game off turns every mode under it off in effect and dims the mode rows; each mode's own toggle position is
        remembered and restored when the game comes back on.
- Edge cases
  - [ ] All modes off: the board shows "Turn on a mode" with a button that opens the game page.
  - [ ] Turning a mode off while a read is in flight: the read finishes or is cancelled, and is not recorded after
        the switch (no line written after off).
  - [ ] Alerts for metrics of an off mode stop receiving values (RoRoRo sees silence, as it does when reading is
        paused today); the alert rules themselves are untouched.

### Epic: First run

- As a new player, I want Ur Score to open on something I can act on, so I'm reading numbers in under a minute.
  - [ ] A fresh install (empty data folder) opens Setup on the Pet Sim 99 page with Battle and Profile on.
  - [ ] No file picker opens at any point, and no step mentions recipes.
  - [ ] Battle's clan search has focus; picking a clan starts the Battle board filling on the next read.
  - [ ] With no clan picked, the Battle board's empty state says "Pick your clan" and opens the game page.
  - [ ] Profile reads right away for my saved RoRoRo accounts; an account that isn't linked shows the existing
        "Profile is private" message.
  - [ ] The game-choice step appears only when the manifest has two or more games (not in this release).

### Epic: No recipe words anywhere

- As a player, I want every page labelled in words I already know.
  - [ ] Stats: the picker is "Mode" listing Battle and Profile (not recipe names); its empty state says
        "Turn on a mode with stats to choose."
  - [ ] Alerts: "What each mode sends to RoRoRo", rows named by mode; "No mode on, so nothing is sent to RoRoRo."
  - [ ] Your accounts: the Send columns are headed Battle and Profile.
  - [ ] Score book: "PER MODE" with rows named by mode (Battle's clans list rows under Battle); "removing a recipe
        keeps its book" text replaced with "turning a mode off keeps its book".
  - [ ] Board texts, the panel gallery, panel forms and panel notes say "mode" or name the mode where they said
        "recipe" (e.g. "Needs the Battle mode on" instead of "Import a recipe that lists groups").
  - [ ] Diagnostics' human lines name modes; its copy-to-clipboard lines may keep slugs (they are for bug reports).
  - [ ] README is rewritten around games and modes, the "What leaves your machine" table is per mode, and the
        settings reference documents `modes` and drops `activeRecipe`.
  - [ ] A repo test fails if a UI-facing string in src/ contains "recipe" outside an allow-list (CLI, parser messages,
        engine detail lines that Diagnostics shows for bug reports).

### Epic: Readers that stay current

- As a player, I want fixes to how Pet Sim 99 is read to arrive with the app update, with nothing to click.
  - [ ] Every mode reads with the copy of its readers built into this version of Ur Score.
  - [ ] The race panel no longer says "Setup › Recipes has an update"; that condition can't occur.
  - [ ] The first read after an update with changed reader text starts a new version in the score book; charts
        and records run continuously across the change.

### Epic: Upgrading from 0.6.3

- As an existing player, I want the update to keep everything I set up.
  - [ ] A mode comes up on when the 0.6.3 install had any of its readers installed, and off otherwise, so an upgrade
        reads exactly what 0.6.3 read: a Battle-only player does not suddenly start reading profiles. This is written
        once, as an explicit `modes` map, on the first 0.7.0 start. A fresh install (no data folder) gets the manifest
        defaults instead.
  - [ ] Every source keeps its clan, its role (Main/Mine/Watch) and its Enabled state.
  - [ ] Every stat tick, every Send tick and every metric id is unchanged, so RoRoRo alert rules keep firing.
  - [ ] Boards, pop-outs and the board I last had open are unchanged.
  - [ ] A data-folder recipe that no manifest names (a hand-imported custom recipe) stops reading; its files, its
        sources and its book stay on disk, and Diagnostics lists it as "not part of any mode (kept, not read)".
  - [ ] Nothing in the data folder is deleted. The upgrade writes only settings.json (the `modes` map). Later writes
        are the ones 0.6.3 already made in normal running (counter names after a read, icon index, a toggle or a tick).

### Epic: Moving a setup to another PC

- As a player with two PCs, I want Export stats / Import stats to keep working.
  - [ ] A new export carries clans, stat ticks, Send ticks, boards, the mode toggles and the two settings it carries
        today, but no reader text.
  - [ ] An export from 0.6.3 or earlier still imports: its reader text is ignored, its ticks and clans apply.
  - [ ] An item for a reader no mode uses is skipped, and the summary says "1 item skipped: not part of any mode."
  - [ ] The preview lists items by mode name.

### Epic: Safe to build

- As the owner, I want the test suite unable to touch my real data.
  - [ ] Before any feature code: tests can't start the real app or resolve the real %LOCALAPPDATA% data folder; a run
        that does fails loudly (port of K0ii fe8d103).
  - [ ] The manifest is checked by tests: every mode reads at least one shipped reader, no reader is in two modes,
        every board is a real starter, `asks` names a real input, ids are unique.
  - [ ] NoHostnameFenceTests stays green: no host appears in src/ code.

## What We're Building

Everything in the epics above: the manifest and its loader, the game page (absorbing the Clans page and the
StartOnOpen checkbox), the mode and game toggles and their effect on reads, starter tabs and panels, the new first
run and empty states, recipe-free wording across Setup, the board and README, embedded readers as the only source of
reader text, the 0.6.3 upgrade path, SetupPack changes, the harness fence, the new tests and smoke walks, and a 0.7.0
CHANGELOG entry ready on the branch.

## What We'd Add With More Time

- A game picker step and a second game.
- A per-mode "last read 2 min ago · next in 1 min" line on the game page.
- Showing a mode's panels in the gallery greyed out with "Turn on Profile to use this" rather than hiding them.
- Retiring the dormant KeyStore and recipe-key plumbing once no shipped reader needs a key.
- A Loot mode (spec 2, its own cycle).

## Non-Goals

- **Custom recipe import.** Removed outright by the owner; only manifest games exist.
- **Removing the recipe engine or the no-hostname fence.** Game knowledge stays in shipped data files.
- **Changing what any mode reads or how often.** Same endpoints, same cadences as 0.6.3.
- **Deleting data during the upgrade.** Orphaned readers and books stay on disk.
- **Merge, tag, release, catalog bump.** Owner-gated; this cycle stops at a ready branch.

## Open Questions

- Should the gallery hide or grey out panels of an off mode? Built as: hidden from "+ Add panel" while the mode is off,
  and existing panels show the off state. (default — confirm on next interactive run)
- Should Diagnostics offer a way to delete an orphaned custom recipe's files? Built as: no, list only. (default —
  confirm on next interactive run)
- Settings.ActiveRecipe is read by nothing: dropped from new exports, tolerated on import. (default — confirm)
