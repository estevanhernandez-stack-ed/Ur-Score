# Changelog

All notable changes to RoRoRo Ur Score are documented here. Format roughly follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning follows [SemVer](https://semver.org/).

## Unreleased

### Fixed

- Diagnostics and copied diagnostics explain last-read account claim conflicts by own-account and
  source name, without implying current membership or successful sending. Claim rules are unchanged.
- Panel tooltips use the live application theme rather than Windows chrome.
- My accounts exposes last-read SENT status with the account name to UI Automation.
- An orphaned alert failure uses the theme's failure color instead of ordinary body text.
- Setup > Your accounts uses a compact gap between its explanatory notes.
- The panel drag grip uses drawn dots rather than a font-dependent glyph and exposes a named
  automation peer. Existing keyboard move controls remain available.
- Profile stat settings explain when the selected source is switched off without blocking Save.
- Past periods can explicitly omit the best-account stat to recover from a removed stat, while
  retaining headline history and the existing saved format.
- Extreme hand-edited alert thresholds stay compact and show the editing limit when Change opens;
  saving requires an explicit valid replacement.
- Empty source-following starters cannot be duplicated; populated starters and user-created boards
  retain Duplicate.
- Panel slots and pop-out windows wait for successful score-book loading. Failed loads can retry
  without clearing saved placement, and a late load cannot resume startup after shutdown begins.
- External pop-out closes preserve placement for next start, regardless of main-window close order.
  Return to the board, Bring back, and Alt+F4 still return the panel explicitly.

### Documentation

- Added board editing, gallery, tab-menu, pop-out, and saved-layout guidance; restored the missing
  0.3.0 and 0.3.1 release summaries below.

## 0.3.3 - 2026-09-16

### Added

- **Your finished battles fill in on their own.** A clan between battles used to read as
  "nothing to see": the read stopped before it reached the finished ones, so the Past battles panel
  stayed empty however long a clan had been playing. It now hands over the finished periods either
  way, and a first read backfills every one the source still remembers - 23 of them on the clan this
  was found on. Only your own accounts' numbers are kept, exactly as before.
- **Rename, Duplicate and Delete are on screen.** They were only ever on a board tab's right-click
  menu, so they read as missing. A `...` button now sits on the tab row beside + Board and opens the
  same menu. Right-click and Shift+F10 still work.

### Changed

- **Confirmation dialogs are Ur Score's own.** The seven remaining stock Windows message boxes are gone.
  Three questions that need asking - delete a board, remove a recipe, add a clan past the limit -
  share one themed confirmation whose safe answer is the default. The two that were only reporting a
  failure say it where you are looking instead: a board that would not save keeps its reason on the
  board until a save works, and a recipe that would not import says why under Import recipe.
- **The import screen fits its window again.** The stats list was a fixed height whatever the recipe
  offered, so a recipe with one stat pushed Cancel, Import, and the line telling you to tick a stat
  off the bottom of the screen. It now grows to its rows.
- **A popped-out panel opens at its panel's size** rather than one size for all of them, so a table
  opens as a table. Each panel's Pop out and Settings buttons now say which panel they belong to.
- **Past battles reads as a table.** Total and Your best no longer run together, the battle name
  keeps its width, and the panel says what it is ordered by instead of inheriting an order by
  accident.
- The line for a clan with no finished battles yet said you had none. It now says Ur Score has not
  read them yet, and that the next read fills them in.

## 0.3.2 - 2026-09-15

### Added

- **An Alerts page that says what will alert you.** Every stat you send gets a card on
  Setup > Alerts, and its alerts read as sentences you fill in: stops climbing (fewer than a number
  a minute, over 10, 15 or 30 minutes) and crosses a number (above or below). + Add an alert,
  Change and Remove, with what happened said on the card itself. Rules you or another plugin wrote
  are listed and never changed. A standing line names the one switch to turn on in RoRoRo. Rules
  Ur Score writes carry a `label`; RoRoRo 1.28 ignores it, and RoRoRo 1.29 uses it to word the
  alert from the rule that fired.
- **Your accounts have faces.** Each of your own accounts shows its Roblox avatar beside its name -
  in the accounts table, My accounts, Promotion check, the account card and Setup > Your accounts.
  Pictures load after the numbers and never hold a row up. Other players are never looked up: the
  leaderboard and the top of the period show names only.
- **Start reading when the window opens.** Once you are set up, tick **Start reading as soon as
  Ur Score opens** on Setup > Recipes and you no longer have to press Start. Off until you turn it
  on, and it takes effect the next time you open Ur Score.
- **The window opens on the last numbers it saw.** Rather than empty panels, Ur Score draws the
  last good reading from your score book, marks those panels `remembered`, and says in the state
  line how old they are. A remembered number is never sent to RoRoRo, never written back to the
  book as a new reading, and never given a rank it did not earn. A read that fails leaves them
  alone - it replaced nothing.
- **The reason an account has no numbers, beside the numbers it has none of.** When a recipe cannot
  read an account, its own explanation now appears on My accounts and Setup > Your accounts, where
  the dashes are, instead of only in places you might not be looking.

### Changed

- No stock message box and no JSON on the Alerts page. A failed write is said on the card and
  changes nothing. RoRoRo's rules file is backed up and written through a temporary file on every
  change, and every rule Ur Score does not own is kept exactly as it was found.
- The Stats table's rule line counts a stat's alerts.
- The board credits each source once. Recipes for one service share an opening sentence, and the
  footer used to repeat it once per recipe.
- The README describes the window that ships. It had described the build before recipes - including
  telling you to set your clan by editing a file, when Setup > Clans does it - and two of its
  claims about what leaves your machine were wrong: Ur Score does hold a recipe's key, in a
  DPAPI-encrypted store, and a per-account recipe does send your own Roblox user ids to that
  recipe's host.

### Removed

- `written-rules.json` is no longer written.

## 0.3.1

### Added

- Battle and Alts starter tabs follow their sources independently until each tab is edited.
- An accounts-table panel shows your own accounts across the selected stats, with sorting,
  totals, and a session-only selected account.
- Recipe values can describe durations, dates, counted entries, suggested Show choices, and
  sections used by the account card.

## 0.3.0

### Added

- Saved boards as tabs, with new-board, rename, duplicate, and delete actions.
- A panel gallery and settings forms for choosing each panel's source, stat, and accounts.
- Draft board editing with drag reordering, keyboard move controls, sizes, Tall, and removal.
- Always-on-top panel pop-outs that update live and restore their saved placement on restart.
- `boards.json` stores board layouts, panel settings, and pop-out placement separately from the
  score book. Account selections in layouts are restricted to your own accounts.

## 0.2.0

### Added

- **Recipes.** A recipe file says where a number is and how to read it; Ur Score runs it and hands
  your own accounts' values to RoRoRo. The Pet Simulator 99 clan battle is now a recipe
  (`tests/Fixtures/petsim99-clan-battle.recipe.json`) rather than code, and Ur Score itself names no
  vendor. Importing shows every host a recipe contacts and what each receives before anything runs.

### Changed

- **Ur Score paints in RoRoRo's theme.** It asks RoRoRo for its palette on connect and follows every
  theme switch after that, including the title bar. RoRoRo's Brand colours are used whenever RoRoRo
  is not running. No new capability: both theme calls are ungated.
- **Readable tables.** Cell text, headers, selection, checkboxes and scrollbars all use the theme,
  where the stock controls had painted black text on dark rows.
- Screen readers now read the status, clan and headline lines instead of a fixed label.
- The clan line reads "Live: ArcadeBattle2026" instead of the raw "battle=ArcadeBattle2026".
- The update screen lists each change on its own line.

## 0.1.0 — 2026-09-12

Initial build. Not yet installable — see the README's status callout: it needs RoRoRo v1.28.0.0,
which has not shipped, and a packaged release, which does not exist yet (`icon.png` is still
owed).

### Added

- **The clan dashboard.** Reads the live Pet Simulator 99 clan battle (via Big Games' public API)
  every three minutes: the clan's place and total points, every contributor ranked with your own
  RoRoRo accounts marked, and a points-per-minute figure per account computed from consecutive
  polls.
- **Reporting to RoRoRo.** Maps clan contributors to your saved RoRoRo accounts by Roblox user id,
  and hands each match's raw cumulative points to RoRoRo's `ReportMetric` so RoRoRo's alert rules
  can judge it. Reporting is gated by one chokepoint (`ReportPolicy`) enforcing three checks —
  configured metric id, allow-listed account, finite value — and nothing else in the plugin can
  reach the host client.
- **Eight distinguishable states** in the window instead of one shared "error": idle, unreachable,
  no battle running, unrecognised response shape, no matching contributors, reporting, host not
  running, and capability revoked. "No battle running" reads as normal, not as a failure.
- **Never a quiet empty list.** Every parse failure names the keys it actually found instead of
  returning nothing; the last raw response from each call is kept on disk (never transmitted) for
  a **Copy diagnostics** button to draw from.
- **Reads RoRoRo's `metric-rules.json`** every cycle and says plainly whether a rule exists for
  the configured metric id — and a one-click **Add this rule to RoRoRo** that previews the exact
  JSON before writing it, backs the file up first, merges rather than replaces, and never touches
  a rule someone else (the user, or another plugin) already owns.
- **Per-account send toggle.** Every saved account defaults to reporting; unticking one in the
  window stops it from sending while it keeps showing on the dashboard, and the choice survives a
  restart.
- **Leaderboard names, resolved through Roblox's own public batch lookup**, cached for the life of
  the process. Optional — turn off `resolveNames` in settings and the leaderboard still shows
  positions and points, just not other members' names, and the plugin then makes exactly two
  outbound calls per poll (both to the game's own API) instead of three.
- **Accounts with no Roblox id yet are named, not silently skipped** — RoRoRo hasn't resolved one
  yet (a brand-new saved account that's never been launched), and the window says so per account.
- **Attribution line** crediting Big Games' public API in the window itself, on every launch.

### Known gaps

- No packaged release exists. `build/build-plugin.ps1` stops at a missing `icon.png` by design —
  see README, *Build from source*.
- `ROROROblox.PluginContract` 0.10.0 is not yet on nuget.org; building from a clean clone needs a
  local feed or a local pack — see README.
- `minHostVersion` is `1.28.0.0`, which has not shipped. This plugin cannot be installed against
  any released RoRoRo yet.
