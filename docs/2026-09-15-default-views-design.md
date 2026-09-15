# Default views: a Battle tab and an Alts tab

Approved by the owner on 2026-09-15. Target: v0.3.1, in use for the clan battle on Saturday 2026-09-19. Branch `feat/default-views` off master at v0.3.0.

## Why

Editing a board is clumsy until the snap grid lands (backlog V3-S.9, after Saturday). Until then, Ur Score should open with two tabs that are already right, so nobody has to arrange anything on battle day. The owner found the approved board mock (the "Ur Score Modular Board" artifact) more polished than the built board, so this work also closes that gap for the panels it touches.

## What the owner sees

Ur Score opens with two tabs, built from your clans and recipes. Each tab keeps following your sources until you change that tab, as the single starter board does today (stage 2 R1). A tab appears only when it has something to show. + Board offers both as starters, so a deleted tab can come back.

The single-panel views proposed earlier (Live, Rivals, History) are not tabs: those panels can be added to any tab from the gallery or popped out.

### Battle (the tab you watch on battle day)

Rows fill all 12 columns across. When a source is missing, its row closes up rather than leaving a hole. Each panel keeps its natural height, top-aligned in its row, as in the approved mock.

1. Your main clan's standing · the standing of a clan your accounts are in (the alts' clan) · Battle race
2. My accounts, grouped by clan with points and change · Promotion check (alts' clan to main) · Top of the battle
3. Past battles (main clan) · Records

> Changed during execution (2026-09-15): the first draft had four rows (standings and Top, then race and Promotion, then My accounts full width, then Past battles and Records) with every panel stretched to its row's height. Screenshots next to the mock showed the first row lopsided and the race below the fold, so the controller moved to the mock's arrangement and natural heights. Same panels.

### Alts (your accounts side by side, like the Big Games database)

1. **Accounts table** (a new panel type), full width:
   - one row per account in your RoRoRo list;
   - the columns are the stats you tick for the profile recipe in Setup › Stats;
   - click a column heading to sort by it;
   - a totals row;
   - the sorted column also shows today's and the 7-day change;
   - an account whose Profile view isn't public shows that plainly in its row.
2. Records · Account card. Clicking an account row in the table shows that account in the card. The card shows the account's profile in sections like the database page (Account, Progression, Slots, Game stats), using the stats the recipe offers.

Grind goes away as a default tab; Alts covers it. It can still be rebuilt by adding panels.

## Data

The profile recipe reads `ps99.biggamesapi.io/v1/players/{userId}?include=profile` for each of your own accounts (unchanged host, unchanged privacy: your accounts only). Today it names diamonds, eggs hatched and rank, plus 75 game statistics as counters. The same response also holds, under `data.views.profile.data`: `Rank`, `RankStars`, `Rebirths`, `GoalsCompleted`, `EggsHatched`, `TotalSessions`, `Age` (playtime in seconds), `BoothDiamondsEarned`, `BoothSlots`, `EggSlotsPurchased`, `PetSlotsPurchased`, `FirstJoinTimestamp`, and objects whose entry counts are meaningful (`Achievements`, `PetHatchCount` for distinct pets hatched, `UnlockedZones`). Robux spent and login count come from the database's extended profile, which this request doesn't return; they're out of scope.

The profile recipe gains named stats for those fields. Default ticks (Show) for a fresh install of the recipe: rank, rebirths, diamonds, eggs hatched, distinct pets hatched, goals completed, playtime. Playtime and first join must read as durations and dates, not raw numbers.

## Rules that still bind

- Other players never reach disk; the Alts tab only ever shows your own accounts.
- Every popup and toast is themed; every list in `src/UI` is a `ui:RowList`; board buttons' enable state only through `BoardButtons.For`.
- No hostname literals in `src`; copy is sentence case, second person, no emoji, and Ur Score's own text never names a game.
- Before release, screenshot both tabs next to the approved board mock and fix the differences in type, spacing, alignment and density.
- Merging and releasing v0.3.1 each wait for the owner's OK.
