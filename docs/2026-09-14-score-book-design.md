# Ur Score: the score book, past battles and the account-first scoreboard

Status: approved in brainstorm on 2026-09-14, after a four-reviewer adversarial pass on the recorder. Ships
as **v0.2.0**, in three stages that are each releasable on their own (§11).

This spec **replaces** the part 2b board in `docs/2026-09-13-stats-games-icons-design.md`:
- §6.2, the table-first board;
- §6.5, the account card;
- §6.7, trend lines.

That spec's §6.1 game list, §6.3 honest numbers, §6.4 empty/idle states and §7 import rules still hold unless
this document says otherwise.

## §0 Why

- **Ur Score has two jobs:** alert you, and **keep all your scores**. Part 2a did the first. Nothing kept a
  record: the window forgot every number on restart, and RoRoRo keeps only 64 samples per series for sent
  stats.
- **The owner's real use this weekend:** the main account battles in one clan and the alts in another. They
  want to see which alts are doing well enough to move up.
- **Clanmates are about to try Ur Score** for the first time.

## §1 Decisions, as made

1. **Keep all four:**
   - each battle's final result;
   - every reading over time;
   - records and bests;
   - personal stats over time.
2. **The scoreboard is for the user,** about their own accounts. Sharing is not built.
3. **Approach A: Ur Score keeps its own score book.** A local file of the user's own readings. RoRoRo is not a
   history store, and no plugin contract change is needed.
4. **Board shape: account-first.**
   - The left list is the ranking: your accounts sorted by a picked stat, grouped by clan.
   - The right side is one account's scoreboard.
5. **Two clans at once** for one recipe (§8).
6. **A recipe picker** in the window. Today the window silently runs the last-imported recipe, and nothing
   switches it back.
7. **The Stats table gets much simpler** (§6). There is no separate Look up → search → pick → Add flow.
8. **A recipe try-out command** for agents and recipe authors (§9).
9. **The UI Automation smoke scripts are committed** (§10).
10. **Ur MCP tools for Ur Score** come after this release, in the `rororo-ur-mcp` repo. They are not part of
    this spec.
11. **A Pet Sim trading tool** (inventory and trades views) is a separate later project built on the same book.
12. **Version 0.2.0.** All three stages are wanted in it. Each stage is releasable, so the tag carries
    whatever is finished and tested.

## §2 Facts verified on 2026-09-14

| Fact | Where it came from |
|---|---|
| `GET /api/clan/{name}` returns `data.Battles`: one entry per battle the clan joined (23 for CCGP), each with the final `Place` and `Points`, and `PointContributions: [{UserID, Points}]` | The saved step-2 response for CCGP |
| So a clan's own past battles, and its members' final points, can be read at any time. The top-100 limit applies only to `/v1/clans/battles`, which Ur Score doesn't use | Same, plus `v1/clans.md` |
| `GET /api/activeClanBattle` keeps returning the last battle after it ends, until the next one starts. `configData.StartTime` and `FinishTime` are unix seconds | Live, 2026-09-14: ArcadeBattle2026, finish Fri 11 Sep 12:30 |
| `v1/players/{slug}?include=profile`: `views.profile.fetchedAt` is ISO-8601 UTC and `views.profile.isStale` is a boolean | Live, the owner's own account |
| Big Games API terms: personal, non-commercial use; no excessive requests or scraping | `TERMS.md` |
| Ur Score is single-instance per user (`Local\626labs.ur-score.single-instance`) | `src/App.xaml.cs:13` |

## §3 Recipe format additions

All are optional, so every existing recipe stays valid.

### 3.1 `period`, on the recipe

```json
"period": { "value": "battle", "starts": "battleStarts", "ends": "battleEnds", "past": "data.Battles" }
```

- **`value`** names a `take` from an earlier step. It is the thing a reading belongs to (a battle, a season).
  - Required when `period` is present.
  - It must be a `take` name.
- **`starts` / `ends`** name `take`s holding the period's start and end.
  - Unix seconds, or ISO-8601 text.
  - A value that parses as neither is ignored for that read, with a Diagnostics note. It never stops the read.
- **`past`** is a path in the **last step's** response to an object whose keys are period values. Each key
  is one past or current period, read with the same `rows`, value paths and headline paths as the live
  period, by filling `{<value>}` with that key (§5.2).
  - List recipes only.
  - A per-account recipe with `past` is refused at import.

### 3.2 `asOf`, on the last step

```json
"asOf": { "time": "data.views.profile.fetchedAt", "stale": "data.views.profile.isStale" }
```

- **The source's own snapshot time,** and optionally whether it says the snapshot is stale.
- **Where it's read from:**
  - per-account steps: each account's response;
  - list steps: the response root.
- **`time`** is unix seconds or ISO-8601. **`stale`** must resolve to a boolean.
- **A miss** costs only the `asOf` field on that line, never the reading.

### 3.3 Headline ids

- Each headline item may carry `"id"`. When absent, the id is `RecipeStats.Slug(label)`.
- Ids are unique within a recipe; a duplicate is refused at import.
- The book stores headline values by id, so renaming a label never splits a series.

### 3.4 No literal numbers in paths (privacy)

- **What's refused:** any path in a recipe with a segment made only of digits, outside a `{placeholder}`.
  - The paths checked are: `take`, `rows`, `userId`, value paths, `counters.path`, `unavailable.path`,
    headline paths, `icon`, `asOf.time`, `asOf.stale` and `period.past`.
  - Refused at import, and again when installed recipes load.
- **Why:** `RecipePath` walks objects by key and never indexes arrays (`src/Recipes/RecipePath.cs`), so a
  digit segment can only name an object key, and in these APIs that is a player's user id. The check stops a
  shared recipe from reaching into a named clanmate's data.
- **The message:** "Step 2: 'data.members.1234567.name' names a number. Recipes can't point at a particular
  player; use a placeholder instead."

### 3.5 The worked recipes

These are fixtures now, and they ship as starter recipes once the owner and Claude make them together:

- **Clan battle:**
  - step 1 takes `battle`, `battleStarts` (`data.configData.StartTime`) and `battleEnds`
    (`data.configData.FinishTime`);
  - the recipe declares `period` with `past: "data.Battles"`;
  - headline items get ids `clan-place` and `clan-points`.
- **Profile:** the last step declares `asOf` on `views.profile`.

## §4 The score book

### 4.1 Where

`%LOCALAPPDATA%\626labs.ur-score\scorebook\<slug>\`

- `YYYY-MM.jsonl` holds lines, one file per **UTC** month of the line's `t`.
- `recipes\<hash>.json` holds the exact recipe text each line's hash names. It is written once, when a hash
  is first used.
- **Removing a recipe never touches its book.** `RecipeStore.Remove` stays as it is, and a test proves the
  book survives it.
- **Copy diagnostics never includes the book.**

### 4.2 Lines

- One JSON object per line, UTF-8, `\n`-terminated.
- Every line has `v: 1`. A reader skips any line whose `v` it doesn't know.

**A reading line**, written after a successful read:

```json
{"v":1,"kind":"read","t":"2026-09-19T18:03:00.412Z","off":-300,"trigger":"timer",
 "recipe":{"slug":"pet-sim-99-clan-battle-points","hash":"3f9a1c0b7e2d4a55"},
 "inputs":{"clan":"K0i2"},
 "period":{"value":"ArcadeBattle2026","starts":"2026-08-29T18:00:00Z","ends":"2026-09-11T17:30:00Z"},
 "headline":{"clan-place":14,"clan-points":30214400},
 "stats":["value"],
 "accounts":{"1647274201":{"v":{"value":12418220},"rank":{"value":3},"of":50}},
 "unavail":[]}
```

| Field | Meaning |
|---|---|
| `t`, `off` | When Ur Score read, in UTC, and the PC's UTC offset in minutes at that moment (for "day by day" in local time) |
| `trigger` | `start` (the first read after Start), `timer`, or `manual` (Test now) |
| `recipe.hash` | The first 16 hex digits of the SHA-256 of the recipe text the read used |
| `inputs` | The input values the read used. Inputs are never keys: keys live only in `KeyStore` |
| `period` | Present when the recipe declares `period`. `starts`/`ends` are ISO-8601 UTC, or absent |
| `headline` | Headline values by id, **numbers only**. A headline value that isn't a finite number is left out. The book keeps no free text from a response |
| `stats` | The tracked stat keys this read asked for (Show or Send) |
| `accounts` | Keyed by Roblox user id, **your accounts only**: `v` holds each tracked stat found, by stat key. A stat that missed is absent, never 0 |
| `accounts[id].rank`, `of` | List recipes only: this account's rank among **all** rows read, per tracked stat, with standard competition ranking (1, 2, 2, 4), and how many rows there were. Worked out before other rows are dropped; only these two numbers leave the read |
| `accounts[id].asOf`, `stale` | Per-account `asOf` (§3.2), when the recipe declares it |
| `asOf`, `stale` (top level) | A list step's `asOf` |
| `unavail` | Your account ids the source said it can't show (a 404, or `unavailable` matched). No message text |

**A final line**, the source's settled result for one period (§5):

```json
{"v":1,"kind":"final","t":"2026-09-19T18:03:00.910Z","off":-300,"trigger":"backfill",
 "recipe":{"slug":"pet-sim-99-clan-battle-points","hash":"3f9a1c0b7e2d4a55"},
 "inputs":{"clan":"K0i2"},
 "period":{"value":"CannonBattle"},
 "headline":{"clan-place":435,"clan-points":67104},
 "stats":["value"],
 "accounts":{"1647274201":{"v":{"value":40210},"rank":{"value":1},"of":3}}}
```

`trigger` is `backfill` (a past period found in the source) or `ended` (the current period passed `ends`, or
was replaced by a new one).

### 4.3 What is never written

- **Another player's id, name or value.** A line's `accounts` are built only from rows whose user id is in
  your account map (§4.5).
- **Response text.** Headline values are numbers only. The only `take`s recorded are the ones `period` names
  (its value, start and end); every other `take` is used and forgotten.
- **Keys, cookies or headers.**
- **Anything from a read that stopped:** idle, unreachable, rate limited, key missing or rejected, sign-in
  required, shape not understood, needs input, or recipe changed mid-read. A gap in the book is a real gap.

### 4.4 When a reading line is written

| This read | Written |
|---|---|
| `Read`, at least one of your accounts has a value or is in `unavail` | a `read` line |
| `Read`, none of your accounts in the rows | nothing (the window already says "none of them are your accounts") |
| The period has ended (`ends` is in the past, or a final for this period and inputs exists) | nothing more for that period; its final line is the record |
| Any stopped outcome (§4.3) | nothing |
| Sending to RoRoRo refused, RoRoRo down, or no stat sent | a `read` line anyway. Keeping and sending are separate |

### 4.5 Your accounts when RoRoRo is closed

- **The account map** (Roblox user id → RoRoRo account) comes from RoRoRo each cycle, as today.
- **Every successful fetch** is saved to `%LOCALAPPDATA%\626labs.ur-score\accounts.json` as
  `[{accountId, robloxUserId, displayName}]`: your own accounts only, never a key or cookie.
- **When RoRoRo is unreachable,** the cycle uses the saved map, so reading and recording continue while
  nothing is sent.
- **When there is no saved map,** there are no accounts to record. The book line in the window says so.

### 4.6 The writer

- **One `ScoreBook` per process.** Lines go through a single queue with one consumer, so two watches (§8)
  never interleave bytes.
- **Each append:**
  1. Open with `FileMode.Append`, `FileShare.ReadWrite`.
  2. If the file's last byte isn't `\n`, write `\n` first.
  3. Write the whole line and its `\n` in one call, then `Flush(true)`.
- **When the file is locked or an IO error happens:**
  - Keep the line in memory and retry on the next line or after 5 s, oldest first.
  - At most 5,000 pending lines. Past that, the oldest reading lines are dropped (never final lines), and
    Diagnostics says how many.
  - Pending lines are flushed on exit.
- **The reader:** skips a line that isn't valid JSON, contains NUL, or has an unknown `v`, and keeps going.

### 4.7 In the window

Under the accounts, one line:
- **Recording:** "Keeping your *clan battle points* for K0i2 · 2,431 readings since Sat 19 Sep ·
  **Open folder**".
- **Not recording:** "Not keeping scores right now: *reason*". The reason is one of:
  - the watch is stopped;
  - nothing to read;
  - no stat is ticked;
  - RoRoRo has never listed your accounts;
  - the battle has ended and its final result is saved;
  - the source isn't answering.
- **The readings count** comes from a count kept while the book is loaded (§7.1), not from re-scanning the
  file each cycle.

### 4.8 Size

- Clan recipe, 10 accounts, every 180 s: about 0.25 MB a day, or 3.5 MB for a two-week battle.
- 50 accounts: about 1 MB a day.
- Profile recipe, 3 stats × 10 accounts, every 1800 s: about 0.05 MB a day.
- Nothing is thinned or deleted in this release. Thinning (hourly after 90 days) is a later decision, made
  with real files.

## §5 Finals and backfill (stage 2)

### 5.1 Which periods get a final

- **On every successful read** of a recipe with `period.past`, Ur Score looks at the last step's response
  and lists the keys under `past`.
- **A key is settled when either:**
  - it isn't the current period, or
  - it is the current period and its `ends` time has passed.
- **For each settled key,** check the book for two things:
  - **The period's clan result:** a final for (slug, inputs, period). If there is none, write one final line
    with the headline and every one of your accounts that has a row in that period (possibly none).
  - **Each of your accounts with a row in it:** a final for (slug, inputs, period, user id). Accounts still
    missing, such as an alt added to RoRoRo later, go into one supplementary final line for that period, with
    the headline repeated.
- **The trigger** is `ended` when the period is the current one, or became settled since the last read;
  `backfill` otherwise.
- **How "already has a final" is known:** an in-memory index loaded from the book (§7.1), updated as lines
  are written.
- **An alt added to RoRoRo later** gets its past finals on its first read.
- **Finals need no extra request.** They come from the same response the live read already fetched.

### 5.2 Reading a past period

- For key `K`, the engine re-reads `rows`, each tracked stat's path and the headline paths against the same
  response, with `{<period.value>}` filled as `K`.
- It uses the same ownership filter, and the same competition ranking over all rows.
- **A key whose rows path is missing or isn't a list is skipped.** Some old battles have no
  `PointContributions`, and a clan with no contributors still gets its headline in a final line with empty
  `accounts`.

### 5.3 After a period ends

- **Reading lines stop** for that period and inputs (§4.4).
- **The window keeps showing** the live numbers, which are now the finals.
- **The next period** starts its own reading lines when the source moves on.

## §6 Recipe picker and a simpler Stats table (stage 1)

### 6.1 The recipe picker

- **The control:** a "Recipe" dropdown at the top of the window, listing installed recipes by name, with
  the icon when there is one.
- **Choosing one** sets `Settings.ActiveRecipe` and runs it: the watch's `UpdateRecipe` clears the old
  recipe's remembered values, as a switch does today. A running watch keeps running on the new recipe.
- **Recipe settings** always opens for the recipe shown in the dropdown.
- **The book line** (§4.7) names the recipe, so switching away from the battle recipe mid-battle is visible.

### 6.2 The Stats table

One table replaces the Stats section, "Look up stat names", "Add a game statistic" and "Add this statistic".

- **Rows:**
  - every recipe value, in recipe order;
  - then every counter name the source offers, in the source's order;
  - then any saved choice the recipe no longer offers, shown greyed with Show and Send off (as today's
    normalization).
- **Columns:**
  - the stat's label;
  - Show;
  - Send;
  - "Name RoRoRo uses", only on rows with Send ticked (editable, pinned once ticked, as today).
- **A search box above the table** filters rows by label, ignoring case. Ticked rows always stay visible,
  with a "Showing 12 of 75 · ticked stats always shown" line.
- **Where counter names come from:**
  - **Recipe settings:** saved `CounterNames`. If there are none and the recipe has `counters`, one read runs
    when the window opens ("Reading stat names once from ps99.biggamesapi.io…"). The recipe is already
    imported and its hosts accepted.
  - **Import:** there is no automatic read before the user accepts the hosts. The table shows recipe values,
    plus one button: **"Show every game statistic (reads once from the hosts above)"**. It fills the table and
    saves the names with the state.
- **The table virtualizes rows,** because a profile can offer hundreds of counters.
- **Unchanged:**
  - the slot line;
  - the budget refusal that undoes a Send tick;
  - the metric-id collision and blank-name rules (`StatRules`);
  - "tick at least one stat".

### 6.3 Your clan, and more than one (see §8)

The inputs area lists each input set. §8.3 covers the "Add another clan" control.

## §7 The account-first scoreboard (stage 3)

### 7.1 Reading the book

- **`ScoreBookReader`** loads every line for the running recipe's slug on window open, and then applies each
  new line as it's written (the writer notifies it).
- **It builds, per input set and account:**
  - **Series:** per stat, `(t, value, asOf?)` for the current period, or the last 30 days when the recipe
    has no `period`.
  - **Finals:** per period, the value, rank and `of`, and the clan headline.
  - **Records** (§7.4).
  - **Counts:** readings and first reading time, for §4.7.
- **Collapsing duplicates:** consecutive reading lines for the same account with identical values and the
  same `asOf` (or, with no `asOf`, within the recipe's `EffectiveEverySeconds`) collapse into one point. Nothing is deduplicated when
  writing.

### 7.2 Layout

- **Top bar:**
  - the recipe picker;
  - the icon and heading;
  - the period line, such as "ArcadeBattle2026 · ends in 3d 4h" or "ended Fri 11 Sep, final saved";
  - "Next read in 2 min";
  - Start/Stop, Test now, Import recipe…, Recipe settings….
- **Left: the ranking.**
  - "Sort by *stat*" (the shown stats), then one group per input set: the clan name with its headline
    ("14th · 30.2M").
  - Inside each group, your accounts sorted high to low by the picked stat, each showing:
    - the name;
    - the value, abbreviated;
    - its rank in the clan ("#3 of 50", list recipes);
    - its change with span ("+220K in 1h").
  - A dot on a row means the account's Send is on.
  - Accounts with no value for the picked stat sort to the bottom and never count as zero.
  - Each group header can be clicked to open the clan view (§7.5).
- **Right: one account's scoreboard.** The top account is selected on open.
  - **Big numbers,** one per shown stat, full precision, each with its change and span. For list recipes,
    also "3rd of 50 in K0i2".
  - **Would place in** (two or more input sets, list recipes): for each other group, where this account's
    current value would rank among that group's live rows, e.g. "In CCGP: would place 41st of 50 (lowest
    member 8.2M)". Worked out live from rows in memory; never saved.
  - **The line:** the account's series for the picked stat in the current period (or last 30 days), drawn as
    a WPF polyline with a faint grid, first and last values labelled, and a mark where `stale` was true.
  - **Past battles** (recipes with `period`): one row per final, newest first, with period, value, "rank of
    of" and clan place.
  - **Records** (§7.4).
  - **The last read** and any miss or unavailable reason. Also a **Send to RoRoRo** toggle, the same
    per-account Send as today's table.
- **Bottom: an "Alerts and diagnostics" expander,** collapsed by default. It holds today's report policy
  card, alert rule card, state and detail lines, and Copy diagnostics, all unchanged.

### 7.3 Picking a period

- Above Past battles on the right, a **Period** dropdown: "This battle" plus every period with a final.
- Choosing a past period changes the whole window to that period:
  - the left ranking uses final values;
  - the right side uses its final numbers;
  - the line shows that period's reading lines, if Ur Score was recording then.
- "Back to live" returns to the current period.

### 7.4 Records

Per account, per shown stat, derived from the book:
- **Best period:** the highest final value, and which period.
- **Best rank:** the lowest final rank, and which period.
- **Periods played:** the count of finals with a value.
- **For stats without `period`:**
  - highest value seen;
  - biggest one-day gain (the most `value` rose between the first and last reading of a local calendar day).

### 7.5 The clan view

- Clicking a group header shows the clan's live leaderboard on the right, as today's leaderboard: every
  member, your accounts marked, the shown stat columns, a header click to sort.
- It stays live only, as today. It is never saved, and it disappears when you select an account.

### 7.6 Empty and first-run states

- **No stats ticked:** "No stats turned on yet" with **Choose stats**.
- **No book yet:** the right side shows live numbers and "Keeping scores from now on. Past battles fill in
  after the first read."
- **A recipe without `period`:** no Period dropdown, no Past battles.

### 7.7 Later, not in this release

- Folding accounts a profile source doesn't know ("Not in this game").
- Stalled marks.
- Break-out panels.
- Thinning the book.
- A merge tool for books from two PCs.

## §8 Two clans at once (stage 3)

### 8.1 State

- `RecipeState` gains `InputSets: [{ "clan": "CCGP" }, { "clan": "K0i2" }]`.
- An existing `Inputs` loads as a one-item list. `Inputs` is still written, as the first set, so an older
  Ur Score can still read the file.

### 8.2 Reading

- Each cycle reads each input set in turn, never at the same time: one full recipe run per set.
- Your account map is fetched once per cycle and shared.
- **An account appears in the set whose rows contain it.** When two sets both contain it (a clan switch
  mid-cycle), the first set in the list wins, and Diagnostics notes it.
- **Sending:** a stat reaches RoRoRo once per account per cycle.
- **`HistoryBudget`** is unchanged: accounts × sent stats. An account can't count twice.
- **The book:** one reading line per set per read, each with its own `inputs`.
- **`RecipeChanged`** compares the input-set list by reference, as it compares `inputs` today.

### 8.3 Settings

- Under the recipe's inputs, **"Add another clan"** (`"Add another " + input label`) adds a set, and each
  set after the first has **Remove**.
- An empty set can't be saved.
- Recipes with no inputs show no control.

## §9 The try-out command (stage 1)

```text
626labs.ur-score.exe --try <recipe.json> [--input id=value]... [--stat key]... [--account robloxUserId]... [--json]
```

- **Runs the recipe once and exits.** No window, no RoRoRo, no state, no book, no single-instance mutex, so
  it runs while Ur Score is open.
- **What it reads:**
  - the recipe file as given;
  - `KeyStore` for keys, which are never printed;
  - the stats given with `--stat`, or every recipe value when none are given.
- **Output:**
  - the parse result and any refusals;
  - the hosts it contacted and what each received;
  - the outcome and detail;
  - rows seen;
  - each headline id and value;
  - per stat: found in N rows, missed in M, and the smallest, largest and median value;
  - counter names found;
  - for each `--account` id: that id's values, rank and `of`;
  - with `period.past`, the past period keys found.
- **Never printed:** another player's id, name or single value. Only counts and the smallest, largest and
  median value leave the read.
- **`--json`** prints the same thing as one JSON object, for agents.
- **Exit codes:**
  - 0: read;
  - 2: the recipe didn't parse or was refused;
  - 3: an input is missing;
  - 4: the read stopped (the outcome name is in the output);
  - 5: bad arguments.
- **Stdout:** it writes through the parent's console when there is one (`AttachConsole`), and to
  redirected stdout otherwise.

## §10 Smoke scripts in the repo (stage 1)

- **`tools/smoke/`:**
  - `uia.ps1` and `uia-import.ps1`, the helpers;
  - `window-smoke.ps1`, plus walk scripts for the Stats table, the recipe picker, the book line and (stage 3)
    the board;
  - `shot.ps1`, which saves to a path you pass.
- **`tools/smoke/README.md`** says how to run them and what each checks.
- **No machine paths:** the repo root is found from the script's location.
- **They never touch real account data:** a walk that needs a clean recipes folder moves the user's folder
  aside and puts it back in a `finally`, as the part 2a walks did.

## §11 Build order

Each stage ends green (build with warnings as errors, all tests) and is smoke-walked live before the next
one starts.

**Stage 1: the recorder** (releasable as v0.2.0 on its own)
1. Parser: `period`, `asOf`, headline ids, the digit-segment refusal. Fixture updates (§3.5).
2. `ScoreBook` writer, the line builder and `accounts.json`. `RecipeWatch` records every successful read,
   with `trigger`.
3. Window: the book line (§4.7) and the recipe picker (§6.1).
4. The simpler Stats table (§6.2), and the import screen's "Kept in your score book" list (§13).
5. The try-out command (§9).
6. Smoke scripts committed (§10). Walk stage 1 live.

**Stage 2: finals and backfill**

7. The engine reads a past period; finals are written with the index (§5).

**Stage 3: the scoreboard**

8. Input sets: state, watch and settings (§8).
9. `ScoreBookReader`: series, finals, records (§7.1, §7.4).
10. The account-first window (§7.2 to §7.6). Walk stage 3 live.

**Release:**

11. Version 0.2.0 in `manifest.json` and the csproj.
12. Make the repo public.
13. Tag, then install from the release URL on a clean plugin folder and walk it.
14. Draft the clan how-to post.

## §12 Testing

- **Line builder:**
  - Other members' rows never reach `accounts`; a test feeds 50 rows with 2 of yours.
  - Show-only stats are recorded.
  - `unavail` holds only your ids.
  - Competition ranking with ties is correct.
  - A text headline is left out.
  - No field contains a key value (a fake key is saved and the line is searched for it).
- **Writer:**
  - A missing final newline is repaired.
  - NUL and broken lines are skipped by the reader.
  - A locked file retries, and pending lines survive until the lock clears.
  - The month file follows UTC.
  - Recipe text is stored once per hash.
  - `RecipeStore.Remove` leaves the book.
- **Parser:**
  - Digit segments are refused in every path kind, and allowed inside a placeholder.
  - `period` requires a known `take`.
  - `past` on a per-account recipe is refused.
  - A duplicate headline id is refused.
- **Finals:**
  - Backfill from a fixture with several past battles.
  - No duplicate finals across restarts.
  - An alt added later gets its past finals.
  - The current period ends by `ends`, and ends by being replaced.
  - Reading lines stop after the end.
  - A battle with no contributions writes the headline only.
- **Accounts cache:**
  - RoRoRo down with a saved map records.
  - RoRoRo down without one records nothing, with the reason line.
- **Input sets:**
  - Two sets are read in turn.
  - An account is in exactly one set.
  - One send per account.
  - An old `Inputs` state loads.
- **Reader:**
  - Duplicate collapse.
  - Records: best period, best rank, biggest day gain across a DST change using `off`.
  - Would-place ranks.
- **Try-out command:**
  - Output never contains another row's user id (a fixture with known other ids is searched).
  - The exit codes.
  - `--json` parses.
- **Fences stay green:**
  - the hostname fence;
  - the theme fence (the new board uses theme brushes only);
  - the budget and report policy tests.
- **Live:**
  - UIA walks per stage.
  - A real clan read that writes lines and backfills CCGP's past battles.
  - Install from the release URL.

## §13 Risks

- **Big Games trims `Battles`.** Finals already written stay; only older backfill is lost. The book keeps
  whatever it wrote.
- **Time.** Three stages for Saturday is ambitious. The stages exist so the tag carries only what's done.
- **The main window is rebuilt** (stage 3). Every automation id the smoke scripts and the theme fence rely on
  either stays or moves in the same commit as its test.
- **A headline that is really another player's id.** For example, a recipe headlines `data.Owner`, which in
  the clan response is the owner's user id, and a number.
  - **Stat values are safe.** They are only read inside a row or a per-account response, and `accounts` is
    filtered by your map, so another player's row never enters.
  - **Headlines are the gap.** The digit rule doesn't catch `data.Owner`. Two guards:
    1. The recorder drops a headline number equal to any user id in the rows read this cycle, other than
       your own.
    2. The import screen lists each headline item under "Kept in your score book", so a strange one is
       visible before import.
  - **What's left:** an owner who didn't contribute that battle could still be recorded, if a recipe
    headlines it. This is accepted for now and revisited with the recipe builder (part 3), which can refuse
    headline paths that end in an id-like key.
