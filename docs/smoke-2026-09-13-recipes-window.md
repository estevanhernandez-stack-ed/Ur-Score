# Smoke — recipes part 1 window checks, themed build (2026-09-13)

**Verdict: 13 of 13 passed.** This covers the two manual steps the part 1 plan left open (Task 9
step 7, the window running a hand-placed recipe; Task 10 step 6, the six import paths) plus the new
theme feed. Two things are still unseen live and are listed at the end.

## How it was run

- Driven by UI Automation against the Release build on a real desktop, the same way the RoRoRo
  packaged-activation smoke was run: buttons invoked, the Windows file picker filled by window
  handle, message boxes and dialog text read back, installed recipe and state files read off disk.
  Screenshots taken at the import, update and main screens.
- RoRoRo **Store 1.28.0.0** running, with 8 saved accounts. Ur Score connected as `626labs.ur-score`
  (`host=1.28.0 reject=(none)`).
- A clan battle was live (`ArcadeBattle2026`). Clans used are public: `UN0` (place 1) for the
  import paths, then `K0i2` (place 20, 67 contributors) for the window check.
- The same checks were first run once on the unthemed build, with RoRoRo not running for check 1;
  all passed there too. This record is the re-run after theming landed.

## Results

| Step | Result | Seen |
| --- | --- | --- |
| Start with no recipe installed | PASS | Heading "No recipe yet" |
| 2.1 Invalid file (step 2 has no url) | PASS | "That recipe could not be imported: • Step 2 has no 'url'."; nothing installed |
| 2.2a First import names every host | PASS | `ps99.biggamesapi.io`, "Receives the value you enter for Your clan.", "Asks every 180 seconds." |
| 2.2b Import with the clan empty | PASS | "Set Your clan first." and the screen stays open |
| 2.2c Import with the clan set | PASS | "Imported Pet Sim 99 clan battle points."; state file pins `metricIdOverride` (Ruling J) |
| 2.3 Identical file again | PASS | No screen; "…is already installed, and is the recipe this window runs." |
| 2.4 Update, same hosts, every 300s | PASS | No screen; "Updated Pet Sim 99 clan battle points. Polls every 300s instead of 180s." |
| 2.5 Update adding `mirror.example` | PASS | "Update recipe" screen lists "• New: mirror.example receives the value you enter for Your clan"; Cancel left the installed file without it |
| 2.6 Recipe settings, change metric id | PASS | Save button; policy reads "as clan.battle.points.smoke"; restored afterwards |
| 1a Heading, credit, live context | PASS | "Pet Sim 99 clan battle points", "Live: ArcadeBattle2026", the recipe's credit line |
| 1b Headline and leaderboard | PASS | "Clan place 20 · Clan points 8,481,955,809"; ranked rows with resolved names |
| 1c With RoRoRo: accounts and state | PASS | Accounts seeded from RoRoRo; "Read 67 row(s); none of them are your accounts." |
| Theme feed | PASS | Trail: "THEME: following RoRoRo's theme."; window and title bar in RoRoRo's palette |

Without RoRoRo (first run): check 1 read the battle and showed "RoRoRo is not running. Still
watching; nothing is being sent."

## Found and fixed in the same pass

- **Rows were unreadable.** Cell text was the stock black on navy rows, headers were stock white.
  The windows now take every colour from RoRoRo's palette, and `ThemeFenceTests` fails the build on
  a colour written into a window.
- **Screen readers heard labels instead of the status.** The state, clan and headline lines had a
  fixed accessible name ("What Ur Score is doing") that hid their text. That name is now help text.
- **"Reading battle=ArcadeBattle2026"** showed the raw context. It now reads "Live: ArcadeBattle2026".
- **"Adding the rule below creates one"** appeared on the import screen, which has no rule below.
  Reworded for both places it appears.
- **The update screen's change list ran together.** Each change is now its own bullet, and empty
  lines on that screen no longer leave gaps.

## Still unseen live

1. **"Reporting to RoRoRo."** None of the 8 saved accounts is in `K0i2` or `UN0` right now, so the
   reporting state and a real report were not observed. The composed engine-and-watch test covers
   the path; seeing it needs a clan one of the accounts is battling in.
2. **A theme switch while open.** The palette arrived on connect; a live switch in RoRoRo's theme
   picker repainting Ur Score was not exercised, because it changes the user's own setting.

## Still open from the review

The update screen lists a host that now receives nothing as both "New" and "No longer" (a deferred
part 1 minor, recorded in the plan's carryover section).
