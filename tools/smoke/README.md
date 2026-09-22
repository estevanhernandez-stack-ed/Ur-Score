# Ur Score smoke scripts

UI Automation walks of the real window: they start the Release build, click through it the way a person would, and print PASS or FAIL per step. They are not part of `dotnet test`; run them by hand before a release and after UI changes.

## Before you run

- Windows PowerShell 5.1 or PowerShell 7, from any folder. Paths come from the scripts' own location.
- A Release build: `dotnet build Ur-Score.csproj -c Release` from the repo root. Close Ur Score first; a running copy locks `bin\Release`.
- RoRoRo running is optional. Without it, steps that need your accounts are skipped or accept the "RoRoRo hasn't listed your accounts" wording.
- **A walk CAN reach RoRoRo, and it reports as the real plugin.** The host resolves a plugin by the id the caller claims, never by the path it runs from, so a build out of `bin\Release` is indistinguishable from the installed one. Assume every walk connects (confirmed against the host's `Handshake`, 2026-09-21).
- Leave the mouse alone while a walk runs; `shot.ps1` brings windows to the front.
- To walk another copy (the one RoRoRo installed), set `UR_SCORE_EXE` to its `626labs.ur-score.exe` first.

## Your data is safe

Every walk that needs a clean start moves `%LOCALAPPDATA%\626labs.ur-score` to `626labs.ur-score.smoke-backup-<time>` and puts it back in a `finally`, even when a step throws. If a run is killed mid-walk, rename the newest `.smoke-backup-*` folder back to `626labs.ur-score` yourself.

**A walk that seeds a data folder must seed it through `Copy-UrControlData`, never a bare `Copy-Item`.** It forces `startOnOpen` false and every stat's `send` false, then re-reads the folder off disk and throws if anything could still report. Both matter: on 2026-09-21 a control walk copied a folder whose settings said `startOnOpen` true and whose clan recipe had `send` true on `clan.battle.points`, and launched it against a live host. Nobody pressed Start — that is what `startOnOpen` means — and the belief that a walk only reads when told to was simply wrong. A folder with nothing set to send cannot send, whichever version of the host is up, which is why the cure is here and not at the host.

Quitting RoRoRo for the duration is a second layer and a good one, but it closes the owner's notification host, so it is a decision to take each time rather than something a script should do on its own.

`walk-alerts.ps1` never writes RoRoRo's `metric-rules.json`. It points Ur Score at a scratch file under `%TEMP%` with `UR_SCORE_RULES_FILE`, deletes that file afterwards, and fails its last step if RoRoRo's own file changed. If a run is killed mid-walk, close Ur Score before starting it again from a shell where that variable isn't set.

## The scripts

| Script | What it walks |
|---|---|
| `window-smoke.ps1 [-Main CCGP]` | First run, a refused file, the import screen, Setup opening on Clans, the main clan, the board, Test now, copied diagnostics |
| `walk-setup-clans.ps1 [-Main CCGP] [-Alt K0i2]` | Main, mine and watched clans, Make main, Remove, a repeat pick, the request line, the Top switch |
| `walk-stats-table.ps1` | The Stats table: search, ticked rows kept, the name column, Setup › Stats, saving, "tick at least one stat" |
| `walk-alerts.ps1` | Setup › Alerts against a scratch rules file: sentences with no JSON, Change, a crosses-a-number alert with a refused number and Enter, Escape, a locked file, Remove, a stat you stop sending, and RoRoRo's own rules file unchanged |
| `walk-starter-board.ps1 [-Main CCGP] [-Alt K0i2]` | Two tabs, every Battle panel by automation id and title, Alts as its own tab, Start, Test now, Stop, and a change to Alts that leaves Battle following |
| `walk-alts.ps1` | The Alts tab: a first import's suggested ticks, the accounts table's columns, sort and flip, a picked account in the card, the total, nothing written |
| `walk-board-editing.ps1 [-Main CCGP] [-Alt K0i2]` | The starter with no `boards.json`, edit mode (move, wide, tall, remove, Done), the gallery, a removed clan with Choose another, + Board with Add panel, rename, duplicate, delete, a restart; ends with `check-boards-privacy.ps1` |
| `walk-pop-outs.ps1 [-Main CCGP]` | Two pop-outs on top with their slots, a live update, a moved window saved, a restart reopening both, closing and Bring back; ends with `check-boards-privacy.ps1` |
| `walk-score-book.ps1 [-Main CCGP]` | Setup › Score book counts, the not-recording reason, book files, Export stats to a file and Import stats of the same file back ("nothing new"), diagnostics without book content, the privacy check |
| `walk-visible-fixes.ps1 [-Main CCGP]` | The 2026-09-17 fixes: Duplicate off for the empty starter, the drawn grip (named, no keyboard stop, dragging by it), both tooltips, a pop-out's own close against an outside close and taskkill, an unreadable score book with Try again, Past battles without a best-account line, the Your accounts notes' gap, a 1e300 alert and a magenta result for an alert that has gone, Profile stat on a switched-off source. Needs the pointer left alone; screenshots only while Ur Score is in front |
| `check-book-privacy.ps1 [-DataFolder path]` | Every account key and unavailable id in every book line is one of yours (exit 0), else counts them by kind and exits 1 -- it never prints an id |
| `check-boards-privacy.ps1 [-DataFolder path]` | Every account id in `boards.json` is one of yours and no panel carries more than its settings (exit 0), else counts the problems and exits 1 -- it never prints an id |
| `shot.ps1 -OutPath file.png [-Title 'Setup']` | A screenshot of one window |

Screenshots from the walks go to `artifacts\smoke\` (gitignored build output).

## Helpers

`uia.ps1` (UI Automation, starting and stopping Ur Score, the data folder, results), `uia-import.ps1` (the file picker, the import screen, `sources.json`) and `uia-board.ps1` (tabs and their menu, edit mode, panel tools, the gallery and panel form, pop-outs, `boards.json`) are dot-sourced by the walks. The automation ids they rely on are listed in `docs/plans/2026-09-14-score-book-stage-1.md` (before its UI tasks) and `docs/plans/2026-09-14-score-book-stage-2.md` (before its tasks); a change to an id changes its script in the same commit.

The tab menu opens with Shift+F10 on the selected tab, and the pop-out walk quits by closing the board window: leave the keyboard and mouse alone while a walk runs.

A popped-out panel's slot on the board carries `Bring back <title>` (find by name, no automation id of its own) and, in edit mode only, a `RemovePanelButton` -- the same automation id a panel's own ✕ uses, scoped to the slot it's found under.

To add a walk: dot-source `uia-import.ps1` (or `uia-board.ps1` for a stage 2 walk), wrap the body in `try { $backup = Move-UrDataAside; ... } finally { if ($null -ne $backup) { Restore-UrData $backup }; Show-Results }`, and record each step with `Check 'n What it proves' <bool> <what was seen>`. A step that can't be judged in this run (the pop-out walk's live update with no battle running) is recorded with `Skip 'n What it proves' 'needs a live battle' <what was seen>`: it shows as SKIP, the summary counts it by reason ("13 passed, 0 failed, 1 needs a live battle"), and it doesn't change the exit code.
