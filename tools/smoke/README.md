# Ur Score smoke scripts

UI Automation walks of the real window: they start the Release build, click through it the way a person would, and print PASS or FAIL per step. They are not part of `dotnet test`; run them by hand before a release and after UI changes.

## Before you run

- Windows PowerShell 5.1 or PowerShell 7, from any folder. Paths come from the scripts' own location.
- A Release build: `dotnet build Ur-Score.csproj -c Release` from the repo root. Close Ur Score first; a running copy locks `bin\Release`.
- RoRoRo running is optional. Without it, steps that need your accounts are skipped or accept the "RoRoRo hasn't listed your accounts" wording.
- Leave the mouse alone while a walk runs; `shot.ps1` brings windows to the front.

## Your data is safe

Every walk that needs a clean start moves `%LOCALAPPDATA%\626labs.ur-score` to `626labs.ur-score.smoke-backup-<time>` and puts it back in a `finally`, even when a step throws. If a run is killed mid-walk, rename the newest `.smoke-backup-*` folder back to `626labs.ur-score` yourself.

## The scripts

| Script | What it walks |
|---|---|
| `window-smoke.ps1 [-Main CCGP]` | First run, a refused file, the import screen, Setup opening on Clans, the main clan, the board, Test now, copied diagnostics |
| `walk-setup-clans.ps1 [-Main CCGP] [-Alt K0i2]` | Main, mine and watched clans, Make main, Remove, a repeat pick, the request line, the Top switch |
| `walk-stats-table.ps1` | The Stats table: search, ticked rows kept, the name column, Setup › Stats, saving, "tick at least one stat" |
| `walk-starter-board.ps1 [-Main CCGP] [-Alt K0i2]` | Every Battle panel by automation id and title, Start, Test now, Stop |
| `walk-board-editing.ps1 [-Main CCGP] [-Alt K0i2]` | The starter with no `boards.json`, edit mode (move, wide, tall, remove, Done), the gallery, a removed clan with Choose another, + Board with Add panel, rename, duplicate, delete, a restart |
| `walk-pop-outs.ps1 [-Main CCGP]` | Two pop-outs on top with their slots, a live update, a moved window saved, a restart reopening both, closing and Bring back |
| `walk-score-book.ps1 [-Main CCGP]` | Setup › Score book counts, the not-recording reason, book files, diagnostics without book content, the privacy check |
| `check-book-privacy.ps1 [-DataFolder path]` | Every account key and unavailable id in every book line is one of yours (exit 0), else counts them by kind and exits 1 -- it never prints an id |
| `check-boards-privacy.ps1 [-DataFolder path]` | Every account id in `boards.json` is one of yours and no panel carries more than its settings (exit 0), else counts the problems and exits 1 -- it never prints an id |
| `shot.ps1 -OutPath file.png [-Title 'Setup']` | A screenshot of one window |

Screenshots from the walks go to `artifacts\smoke\` (gitignored build output).

## Helpers

`uia.ps1` (UI Automation, starting and stopping Ur Score, the data folder, results), `uia-import.ps1` (the file picker, the import screen, `sources.json`) and `uia-board.ps1` (tabs and their menu, edit mode, panel tools, the gallery and panel form, pop-outs, `boards.json`) are dot-sourced by the walks. The automation ids they rely on are listed in `docs/plans/2026-09-14-score-book-stage-1.md` (before its UI tasks) and `docs/plans/2026-09-14-score-book-stage-2.md` (before its tasks); a change to an id changes its script in the same commit.

The tab menu opens with Shift+F10 on the selected tab, and the pop-out walk quits by closing the board window: leave the keyboard and mouse alone while a walk runs.

A popped-out panel's slot on the board carries `Bring back <title>` (find by name, no automation id of its own) and, in edit mode only, a `RemovePanelButton` -- the same automation id a panel's own ✕ uses, scoped to the slot it's found under.

To add a walk: dot-source `uia-import.ps1` (or `uia-board.ps1` for a stage 2 walk), wrap the body in `try { $backup = Move-UrDataAside; ... } finally { if ($null -ne $backup) { Restore-UrData $backup }; Show-Results }`, and record each step with `Check 'n What it proves' <bool> <what was seen>`.
