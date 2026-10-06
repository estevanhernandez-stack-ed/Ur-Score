# Ur Score smoke scripts

UI Automation walks of the real window: they start the Release build, click through it the way a person would, and print PASS or FAIL per step. They are not part of `dotnet test`; run them by hand before a release and after UI changes.

## Before you run

- Windows PowerShell 5.1 or PowerShell 7, from any folder. Paths come from the scripts' own location.
- A Release build: `dotnet build Ur-Score.csproj -c Release` from the repo root. Close Ur Score first; a running copy locks `bin\Release`.
- **Quit RoRoRo first, and check it before and after.** Every walk records whether RoRoRo's host is up when it starts and when it ends (steps `0` and `9`), and fails the last step if RoRoRo came up during the walk. RoRoRo running is allowed (the steps that need your accounts then run; without it they are skipped or accept the "RoRoRo hasn't listed your accounts" wording), but a walk reports as the real plugin either way, so quitting is the safe default. It closes your notification host, which is why no script does it for you.
- **A walk CAN reach RoRoRo, and it reports as the real plugin.** The host resolves a plugin by the id the caller claims, never by the path it runs from, so a build out of `bin\Release` is indistinguishable from the installed one. Assume every walk connects (confirmed against the host's `Handshake`, 2026-09-21).
- **Not while a battle is recording.** A walk moves your real data folder aside and runs against an empty one, so a battle that is being recorded when it starts is not recorded into your own score book. Wait for the battle to end.
- Leave the mouse alone while a walk runs; `shot.ps1` brings windows to the front.
- To walk another copy (the one RoRoRo installed), set `UR_SCORE_EXE` to its `626labs.ur-score.exe` first.

## Your data is safe

Every walk that needs a clean start moves `%LOCALAPPDATA%\626labs.ur-score` to `626labs.ur-score.smoke-backup-<time>` and puts it back in a `finally`, even when a step throws. If a run is killed mid-walk, rename the newest `.smoke-backup-*` folder back to `626labs.ur-score` yourself.

**A walk that seeds a data folder from a copy must seed it through `Copy-UrControlData`, never a bare `Copy-Item`.** It forces `startOnOpen` false and every stat's `send` false, then re-reads the folder off disk and throws if anything could still report. Both matter: on 2026-09-21 a control walk copied a folder whose settings said `startOnOpen` true and whose clan recipe had `send` true on `clan.battle.points`, and launched it against a live host. Nobody pressed Start — that is what `startOnOpen` means — and the belief that a walk only reads when told to was simply wrong. A folder with nothing set to send cannot send, whichever version of the host is up, which is why the cure is here and not at the host.

Quitting RoRoRo for the duration is a second layer and a good one, but it closes the owner's notification host, so it is a decision to take each time rather than something a script should do on its own.

`walk-alerts.ps1` never writes RoRoRo's `metric-rules.json`. It points Ur Score at a scratch file under `%TEMP%` with `UR_SCORE_RULES_FILE`, deletes that file afterwards, and fails its last step if RoRoRo's own file changed. If a run is killed mid-walk, close Ur Score before starting it again from a shell where that variable isn't set.

## The rules

1. **RoRoRo quit, checked before and after.** See "Before you run". Steps `0` and `9` of every walk are that check.
2. **No walk while a battle is recording.** Walks move your real data folder aside.
3. **A walk removes only what it created.** It puts your folder back in a `finally`, and its cleanup never deletes a `before-import-*` or `smoke-backup-*` folder it did not make: `Get-UrBeforeImportFolders` is snapshotted before anything runs and `Remove-UrBeforeImportFoldersExcept` removes only what is not in the snapshot (a cleanup that removed every one destroyed a real backup on 2026-09-23). `walk-first-run` makes none and proves the set unchanged.
4. **A seeded folder sends nothing.** A fresh folder's readers arrive with every send off; a walk that needs a send ticks it on Setup > Stats itself (`walk-alerts`, `walk-visible-fixes`), with start on open off and nothing pressed to start. A folder copied from elsewhere goes through `Copy-UrControlData`.

## How the walks start

There is no import any more: the three Pet Sim 99 readers are built in, and a fresh data folder composes them (Profile and the top clans get their one source each; Battle waits for a clan). Every walk starts from `Move-UrDataAside`, which leaves a folder holding only a settings file with reading on open off and `settingsVersion` 3 (a lower version runs the 0.7.0 modes upgrade, which turns every mode off for a folder with no recipe files). A walk then picks its clans on the game page (`Open-GamePage`) or sets what it needs through Setup, the way a person would.

`walk-board-editing`, `walk-pop-outs`, `walk-top-bar` and `walk-visible-fixes` start with `Move-UrDataAside -ModesOff 'pet-sim-99/profile'`. Those walks were written against a Battle-only board (one tab, an empty first-run starter), and a fresh folder has Profile on, which adds an Alts tab. Writing the switch into settings keeps their intent without rewriting every step; `walk-visible-fixes` turns Profile on again from its switch when it needs it.

## The scripts

Run order for a full pass: `walk-first-run`, `window-smoke`, `walk-game-page`, `walk-stats-table`, `walk-alts`, `walk-starter-board`, `walk-top-bar`, `walk-board-editing`, `walk-pop-outs`, `walk-alerts`, `walk-score-book`, `walk-visible-fixes`. The cheap ones come first and the long ones last. Each walk stands alone, but run one at a time, never two together: they share the one data folder.

| Script | What it walks |
|---|---|
| `walk-first-run.ps1 [-Main CCGP]` | From an empty data folder: the built-in readers compose their own sources, Setup opens on the Pet Sim 99 page with no file picker and no Recipes or Clans page, the clan search has the keyboard, picking a clan fills the Battle board, and no before-import folder came or went |
| `window-smoke.ps1 [-Main CCGP]` | First run, Setup opening on the game page, both modes on, the main clan, the board, Test now, copied diagnostics |
| `walk-game-page.ps1 [-Main CCGP] [-Alt K0i2]` | The Pet Sim 99 page: both modes with their reads lines, start on open, Battle's clans (main, mine and watched, Make main, Remove, a repeat pick, the request line, the Top switch), Battle off (clans dimmed, the starter tab gone, an edited board's panels saying "Battle is off." with Turn on) and back on |
| `walk-stats-table.ps1` | Setup > Stats on the Profile mode: the suggested ticks of a fresh install, search, ticked rows kept, the name column, saving, "tick at least one stat" |
| `walk-alts.ps1` | The Alts tab: the suggested ticks of a fresh install, the accounts table's columns, sort and flip, a picked account in the card, the total, nothing written |
| `walk-starter-board.ps1 [-Main CCGP] [-Alt K0i2]` | Two tabs, every Battle panel (the top list included) by automation id and title, Alts as its own tab, Start, Test now, Pause through the status card, and a change to Alts that leaves Battle following |
| `walk-top-bar.ps1 [-Main CCGP]` | The status chip and its card: a never-started board offers "Start reading" and starts on a click, the chip opens the card without changing anything, Esc closes it, Pause inside the card pauses and Resume resumes, the title and state line say Paused, Test now and F5 still read while paused, a paused board closes without asking, and start on open (ticked on the game page) reads by itself; also grabs the 1024 DIP, three-tab screenshots for each chip state |
| `walk-board-editing.ps1 [-Main CCGP] [-Alt K0i2]` | The starter with no `boards.json`, edit mode (move, wide, tall, remove, Done), the gallery, a removed clan with Choose another, + Board with Add panel, rename, duplicate, delete, a restart; ends with `check-boards-privacy.ps1` |
| `walk-pop-outs.ps1 [-Main CCGP]` | Two pop-outs on top with their slots, a live update, a moved window saved, a restart reopening both, closing and Bring back; ends with `check-boards-privacy.ps1` |
| `walk-alerts.ps1` | Setup > Alerts against a scratch rules file: sentences with no JSON, Change, a crosses-a-number alert with a refused number and Enter, Escape, a locked file, Remove, a stat you stop sending, and RoRoRo's own rules file unchanged |
| `walk-score-book.ps1 [-Main CCGP]` | Setup > Score book counts, the not-recording reason, book files, Export stats to a file and Import stats of the same file back ("nothing new"), the setup import through its preview on a second, fresh folder (the clan unticked, sends off, the aside folder), diagnostics without book content, the privacy check |
| `walk-visible-fixes.ps1 [-Main CCGP]` | The 2026-09-17 fixes: Duplicate off for the empty starter, the drawn grip (named, no keyboard stop, dragging by it), both tooltips, a pop-out's own close against an outside close and taskkill, an unreadable score book with Try again, Past battles without a best-account line, the Your accounts notes' gap, a 1e300 alert and a magenta result for an alert that has gone, Profile stat on a switched-off source. Needs the pointer left alone; screenshots only while Ur Score is in front |
| `check-book-privacy.ps1 [-DataFolder path]` | Every account key and unavailable id in every book line is one of yours (exit 0), else counts them by kind and exits 1 -- it never prints an id |
| `check-boards-privacy.ps1 [-DataFolder path]` | Every account id in `boards.json` is one of yours and no panel carries more than its settings (exit 0), else counts the problems and exits 1 -- it never prints an id |
| `shot.ps1 -OutPath file.png [-Title 'Setup']` | A screenshot of one window |

Retired with the import window in 0.7.0: `uia-import.ps1` and `walk-setup-clans.ps1` (its clan steps moved into `walk-game-page.ps1`).

Screenshots from the walks go to `artifacts\smoke\` (gitignored build output).

## Helpers

`uia.ps1` (UI Automation, starting and stopping Ur Score, the data folder, the file picker, `sources.json`, the game page, results) and `uia-board.ps1` (tabs and their menu, edit mode, panel tools, the gallery and panel form, pop-outs, `boards.json`, `Initialize-ClanBoard`) are dot-sourced by the walks. The file picker and `Read-Sources` used to live in `uia-import.ps1` and moved into `uia.ps1` when the import window went. The automation ids they rely on are listed in `docs/plans/2026-09-14-score-book-stage-1.md` (before its UI tasks) and `docs/plans/2026-09-14-score-book-stage-2.md` (before its tasks); a change to an id changes its script in the same commit. The game page's own ids: `GameSwitch`, `ModeSwitch_battle`, `ModeSwitch_profile`, `StartOnOpenBox`, and inside Battle's card the clan section's `MainFoundLine`, `AddMineButton`, `WatchClanButton`, `RequestsLine`, `TopSwitch`, and the account-placement question's `PlaceAccountsLine`, `RemainingAccountsLine`, `OtherClanSearch`, `ThatsAllButton` (no walk uses these yet).

The tab menu opens with Shift+F10 on the selected tab, and the pop-out walk quits by closing the board window: leave the keyboard and mouse alone while a walk runs.

A popped-out panel's slot on the board carries `Bring back <title>` (find by name, no automation id of its own) and, in edit mode only, a `RemovePanelButton` -- the same automation id a panel's own ✕ uses, scoped to the slot it's found under.

To add a walk: dot-source `uia.ps1` (or `uia-board.ps1` for a board walk), wrap the body in `try { $backup = Move-UrDataAside; ... } finally { if ($null -ne $backup) { Restore-UrData $backup }; Show-Results }`, and record each step with `Check 'n What it proves' <bool> <what was seen>`. A step that can't be judged in this run (the pop-out walk's live update with no battle running) is recorded with `Skip 'n What it proves' 'needs a live battle' <what was seen>`: it shows as SKIP, the summary counts it by reason ("13 passed, 0 failed, 1 needs a live battle"), and it doesn't change the exit code.
