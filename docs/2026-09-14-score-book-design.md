# Ur Score: the score book, clan watch and modular boards

- **Status:** approved in brainstorm on 2026-09-14.
  - Recorder: after a four-reviewer adversarial pass.
  - Board: from the mock at <https://claude.ai/code/artifact/eeecfc19-e836-4761-af5e-89c10bb6d5ee>, which the
    owner called "amazing".
- **Ships as v0.2.0,** in two stages that can each be released (§12).
- **What this replaces:**
  - In `docs/2026-09-13-stats-games-icons-design.md`: the part 2b window (§6 entire: game list, board,
    account card, recipe settings, later) and §5.4, the rule helper's placement.
  - That spec's §3 format, §4 reading, §5.1 to §5.3 choices and budget, and §7 import still hold unless this
    document says otherwise.

## §0 Why

- **Ur Score has two jobs:** alert you, and **keep all your scores.** Nothing kept a record: the window forgot
  every number on restart, and RoRoRo keeps only 64 samples per sent series.
- **The window reads as a settings page.** It's a leaderboard, your accounts, then diagnostics, the report
  policy and the rule card. The owner wants a scoreboard that's useful, and that users can shape themselves.
- **The owner's real weekend:**
  - the main battles in one clan and the alts in another;
  - they want to see which alts could move up;
  - they want to race rival clans.
- **Clanmates are about to try Ur Score** for the first time.

## §1 Decisions, as made

1. **Keep all four:**
   - each battle's final result;
   - every reading over time;
   - records and bests;
   - personal stats over time.
2. **Approach A: Ur Score keeps its own score book,** a local file of what it reads. No plugin contract change.
3. **The window is the board.** Everything that is setup moves into a separate **Setup** window behind a gear:
   - recipes, clans, accounts and stats;
   - alert rules, score book and diagnostics.
4. **Clan watch covers all four:**
   - your main's clan (★);
   - clans your alts are in;
   - rivals and targets (clans none of your accounts are in; clan-level numbers only);
   - the top of the battle.
5. **Entering the main clan is easy:**
   - type a few letters of any of the 169,238 clan names, and pick one;
   - Ur Score reads it once and names which of your accounts it found.
6. **Several sources run at once.** Each clan watch and your profile stats run side by side. There is no
   "active recipe" and no recipe picker.
7. **Boards are modular:**
   - panels from a gallery;
   - arranged in a grid, each with its own settings;
   - several boards as tabs;
   - any panel can pop out into a small always-on-top window;
   - new users start from **starter boards** (Battle, Grind).
8. **The Stats table is one searchable list** with Show and Send on each row. There's no Look up → search →
   pick → Add flow.
9. **A recipe try-out command** for agents and recipe authors.
10. **The UI Automation smoke scripts are committed.**
11. **Ur MCP tools for Ur Score** come after this release, in `rororo-ur-mcp`.
12. **A Pet Sim trading tool** is a separate later project on the same book.
13. **Version 0.2.0.** Both stages are wanted in it, and each is releasable, so the tag carries what is
    finished and tested.

## §2 Facts verified on 2026-09-14

| Fact | Where it came from |
|---|---|
| `GET /api/clan/{name}` returns `data.Battles`, one entry per battle the clan joined (23 for CCGP). Each has the final `Place` and `Points`, and `PointContributions: [{UserID, Points}]` | The saved step-2 response for CCGP |
| So any clan's own past battles, with members' final points, can be read at any time | Same |
| `GET /api/activeClanBattle` keeps returning the last battle after it ends, until the next starts. `configData.StartTime`/`FinishTime` are unix seconds | Live: ArcadeBattle2026, finished Fri 11 Sep 12:30 |
| `GET /v1/clans/battles/{configName}` returns `meta` (title, startTime, finishTime, state) and `topClans: [{rank, name, icon, points, reportedPlace, medal, contributorCount, members, memberCapacity}]` | `v1/clans.md` |
| Its leaderboard is built from the **top 100 clans by all-time points**, so it isn't every clan in the battle, and no total clan count is given | `v1/clans.md` |
| `GET /api/clansList` lists every clan name (169,238 on 2026-09-12) | Earlier session |
| `v1/players/{slug}?include=profile`: `views.profile.fetchedAt` is ISO-8601 UTC, and `isStale` is a boolean | Live, the owner's account |
| Big Games API terms: personal, non-commercial use; no excessive requests or scraping | `TERMS.md` |
| Ur Score is single-instance per user | `src/App.xaml.cs:13` |

## §3 Recipe format additions

All are optional, so every existing recipe stays valid.

### 3.1 `period`, on the recipe

```json
"period": { "value": "battle", "starts": "battleStarts", "ends": "battleEnds", "past": "data.Battles" }
```

- **`value`:** a `take` name from an earlier step. It is what a reading belongs to (a battle, a season).
  Required when `period` is present.
- **`starts`/`ends`:** `take`s holding unix seconds or ISO-8601. A value that is neither is ignored for that
  read, with a Diagnostics note.
- **`past`:** a path in the **last step's** response to an object whose keys are period values. Each key is
  read with the same `rows`, value paths and headline paths, with `{<value>}` filled as that key (§6.2).
  - List steps only.
  - A per-account recipe with `past` is refused at import.

### 3.2 `asOf`, on the last step

```json
"asOf": { "time": "data.views.profile.fetchedAt", "stale": "data.views.profile.isStale" }
```

- **The source's own snapshot time,** and optionally whether it says the data is stale.
- **Where it's read:** per-account steps read it from each account's response; list steps from the root.
- **A miss** costs only the `asOf` field, never the reading.

### 3.3 Headline ids

- **Each headline item may carry `"id"`.** The default is `RecipeStats.Slug(label)`.
- **Ids are unique within a recipe.**
- **The book stores headline values by id.**

### 3.4 Input words

- **An input may carry `"plural"`,** such as `"label": "Your clan", "plural": "Clans"`.
- **Setup and panel titles use these words,** so Ur Score's own text never names a game:
  - the Setup page;
  - "Add a clan your accounts are in";
  - "Clan standing".
- **The default:** the label without a leading "Your ", plus "s".

### 3.5 Group lists

A last step may read rows that are **groups** (clans), not players:

```json
{ "url": "https://ps99.biggamesapi.io/v1/clans/battles/{battle}",
  "rows": "data.topClans", "groupName": "name", "value": "points", "rank": "rank" }
```

- **`groupName` replaces `userId`.** A step with both is refused.
- **Group rows are never matched to accounts, never sent and never recorded.** They are live only, for panels
  (§9.4).
- **`rank`** is an optional path to the row's own rank. Without it, rows are ranked by the first value.

### 3.6 No literal numbers in paths (privacy)

- **Refused, at import and when installed recipes load:** any path segment made only of digits, outside a
  `{placeholder}`, in any of these:
  - `take`, `rows`, `userId`, `groupName`, `rank`;
  - value paths, `counters.path`, `unavailable.path`;
  - headline paths, `icon`, `asOf.time`/`stale`, `period.past`.
- **Why:** `RecipePath` walks objects by key and never indexes arrays, so a digit segment can only name an
  object key, and in these APIs that is a player's user id.
- **The message:** "Step 2: 'data.members.1234567.name' names a number. Recipes can't point at a particular
  player; use a placeholder instead."

### 3.7 The worked recipes

These are fixtures now, and ship as starter recipes once the owner and Claude make them together.

- **Clan battle:**
  - step 1 takes `battle`, `battleStarts` and `battleEnds`;
  - `period.past` is `data.Battles`;
  - headline ids are `clan-place` and `clan-points`;
  - the input is `"plural": "Clans"`.
- **Top of the battle:** a group list over `v1/clans/battles/{battle}` `topClans`.
- **Profile:** `asOf` on `views.profile`.

## §4 Sources

### 4.1 What a source is

- **A source is one installed recipe with one set of input values and a role.** Examples:
  - the clan recipe with `clan = CCGP` as ★ main;
  - the clan recipe with `clan = K0i2` as mine;
  - the clan recipe with `clan = NovaForge` as watch;
  - the top-clans recipe;
  - the profile recipe.
- **`sources.json`** in the data folder: `[{id, recipe (slug), inputs, role, enabled}]`.
- **Roles:**
  - **`main`:** at most one per recipe. Starred everywhere. Promotion checks measure other sources against it.
  - **`mine`:** a source where your accounts are expected. Rows are matched to your accounts, sent and
    recorded.
  - **`watch`:** clan-level only. Rows are never matched to your accounts, never sent, and never recorded per
    account. Its headline is recorded (§5.4).
- **Recipes without inputs** (profile, top clans) have exactly one source, created on import with role `mine`
  (`watch` for group lists).
- **Migration:** part 2a's single `state.json` inputs become one `mine` source per recipe. Stat choices stay per
  recipe (`state.json` `stats`), shared by all of that recipe's sources.

### 4.2 Running

- **`SourceHost` runs one `RecipeWatch` per enabled source,** each on the recipe's own interval, starting and
  stopping together (**Start**/**Stop** in the top bar).
- **Reads for the same host are spaced** at least 2 s apart, and never more than one at a time per host, so
  five clan sources are five calls in turn, not a burst.
- **The account map** is fetched from RoRoRo once per cycle window (30 s) and shared by every watch (§5.5).
- **An account in two `mine` sources' rows in the same read window** belongs to the one read first, and
  Diagnostics notes it. A clan switch shows up here.
- **Sending:** each account's stat reaches RoRoRo at most once per recipe per cycle. `HistoryBudget` is
  unchanged (accounts × sent stats per recipe), because an account can't count twice.
- **`ReportPolicy` stays the only path** to `ReportMetricAsync`. One policy per recipe, shared by its sources.

### 4.3 Clans are found for you

- **Every `mine` or `main` source's rows tell Ur Score which of your accounts are in that clan.**
- **An account found in no source** shows as "Not in a watched clan" on boards.
- **Nothing is typed per account.**

## §5 The score book

### 5.1 Where

`%LOCALAPPDATA%\626labs.ur-score\scorebook\<slug>\`

- `YYYY-MM.jsonl` (the **UTC** month of `t`).
- `recipes\<hash>.json`: each recipe text a line names, written once.
- **Removing a recipe or source never deletes its book.**
- **Copy diagnostics never includes the book.**

### 5.2 A reading line

```json
{"v":1,"kind":"read","t":"2026-09-19T18:03:00.412Z","off":-300,"trigger":"timer",
 "recipe":{"slug":"pet-sim-99-clan-battle-points","hash":"3f9a1c0b7e2d4a55"},
 "source":"s-7f3a","role":"mine","inputs":{"clan":"K0i2"},
 "period":{"value":"ArcadeBattle2026","starts":"2026-08-29T18:00:00Z","ends":"2026-09-11T17:30:00Z"},
 "headline":{"clan-place":212,"clan-points":18902110},
 "stats":["value"],
 "accounts":{"1647274201":{"v":{"value":12418220},"rank":{"value":1},"of":48,"ranked":{"value":47}}},
 "unavail":[]}
```

| Field | Meaning |
|---|---|
| `t`, `off` | Read time in UTC, and the PC's UTC offset in minutes then |
| `trigger` | `start`, `timer` or `manual` |
| `recipe.hash` | The first 16 hex digits of the SHA-256 of the recipe text used |
| `source`, `role`, `inputs` | Which source read. Inputs are never keys; keys live only in `KeyStore` |
| `period` | When the recipe declares one. Times are ISO-8601 UTC |
| `headline` | By id, **finite numbers only**. No response text is kept |
| `stats` | The tracked stat keys asked for (Show or Send) |
| `accounts` | **Your accounts only**, by Roblox user id. `v` is each stat found; a miss is absent, never 0 |
| `rank`, `of`, `ranked` | List steps: the account's competition rank (1, 2, 2, 4) among **all** rows that have the stat, per stat; the row count; and per stat, how many rows that rank was counted among, the N of "#7 of 47" (a row with no value is in neither). Worked out before other rows are dropped. Counts only. `ranked` was added 2026-09-16 (backlog S1-6.9); a line without it shows its place with no N |
| `asOf`, `stale` | From §3.2: per account, or top level for list steps |
| `unavail` | Your account ids the source couldn't show. No message text |

### 5.3 A final line

```json
{"v":1,"kind":"final","t":"2026-09-19T18:03:00.910Z","off":-300,"trigger":"backfill",
 "recipe":{"slug":"pet-sim-99-clan-battle-points","hash":"3f9a1c0b7e2d4a55"},
 "source":"s-7f3a","role":"mine","inputs":{"clan":"K0i2"},"period":{"value":"CannonBattle"},
 "headline":{"clan-place":435,"clan-points":67104},"stats":["value"],
 "accounts":{"1647274201":{"v":{"value":40210},"rank":{"value":1},"of":3,"ranked":{"value":3}}}}
```

`trigger` is `backfill` or `ended` (§6).

### 5.4 When a reading line is written

| This read | Written |
|---|---|
| `Read`, and the recipe has a headline or at least one of your accounts has data or is `unavail` | a `read` line (`watch` sources: headline only, `accounts` empty) |
| Group-list recipe (§3.5) | nothing, ever |
| The period has ended (`ends` passed, or a final exists for it and these inputs) | nothing more for that period |
| Any stopped outcome: idle, unreachable, rate limited, key missing/rejected, sign-in required, shape not understood, needs input, recipe changed mid-read | nothing. A gap is a real gap |
| RoRoRo down, a send refused, or no stat sent | a `read` line anyway. Keeping and sending are separate |

### 5.5 Your accounts when RoRoRo is closed

- **Each successful account fetch is saved** to `accounts.json` as `[{accountId, robloxUserId, displayName}]`:
  your own accounts only.
- **When RoRoRo is unreachable,** watches use the saved map. Reading and recording continue; nothing is sent.
- **With no saved map,** nothing per-account is recorded, and Setup › Score book says why.

### 5.6 What is never written

- **Another player's id, name or value.**
  - `accounts` comes only from rows whose id is in your map.
  - `watch` sources never write accounts.
  - Group lists never write.
- **Response text.** The only `take`s recorded are the ones `period` names.
- **Keys, cookies, headers.**
- **Headline numbers equal to a user id in this read's rows** (other than yours) are dropped. This guards
  against a headline pointing at an owner or MVP id.

### 5.7 The writer

- **One `ScoreBook` per process,** one queue, one consumer.
- **Each append:**
  1. Open with append and `FileShare.ReadWrite`.
  2. Add `\n` if the file doesn't end with one.
  3. Write the whole line in one call, then `Flush(true)`.
- **When the file can't be written:** keep the line in memory and retry after 5 s or on the next line.
  - At most 5,000 pending lines. Past that, the oldest reading lines are dropped, never finals, and
    Diagnostics counts them.
  - Pending lines are flushed on exit.
- **The reader** skips invalid JSON, lines containing NUL, and unknown `v`.

### 5.8 Size

- **Clan source, 10 accounts, every 180 s:** about 0.25 MB a day.
- **A rival source:** about 0.05 MB a day.
- **Profile, 3 stats × 10 accounts, every 1800 s:** about 0.05 MB a day.
- **Nothing is thinned or deleted in this release.**

## §6 Finals and backfill

### 6.1 Which periods get a final

- **When:** on every successful read of a recipe with `period.past`, from that same response. There is no
  extra request.
- **Which keys:** every key under `past` is settled when either:
  - it isn't the current period, or
  - `ends` has passed.
- **For each settled key:**
  - **Clan result:** if the book has no final for (slug, inputs, period), write one with the headline and every
    one of your accounts in that period's rows (none for `watch` sources).
  - **Your accounts:** accounts with a row there but no final for (slug, inputs, period, user id), such as an
    alt added later, go into one supplementary final with the headline repeated.
- **`trigger`:** `ended` for the period that just settled, `backfill` for older ones.
- **The index** of existing finals is built when the book loads (§9.1) and updated on every write.

### 6.2 Reading a past period

- **How:** re-read `rows`, the tracked value paths and the headline paths against the same document, with the
  period placeholder set to the key. Same ownership filter, same ranking.
- **A key whose rows are missing or not a list** still gets its headline final, with empty `accounts`.

### 6.3 After a period ends

- **No more reading lines** for it.
- **Boards show** the final numbers as "final".
- **The next period** starts fresh when the source moves on.

## §7 Setup

A separate window, opened from **⚙ Setup** in the top bar. There's a list on the left and a page on the right.

### 7.1 Clans

- The page title is the input's `plural` (§3.4). There is one page per recipe that has inputs.
- **Your main clan:**
  - a search box over the input's `search` list (every clan name), matching anywhere in the name, ignoring
    case, showing the top 8;
  - Enter or a click picks one;
  - Ur Score then reads that source once and shows "Found estehernandez in CCGP", or "None of your accounts are
    in CCGP yet. You can still watch it." with **Watch it instead**.
  - First run opens Setup on this page when a recipe with inputs is installed and has no sources.
- **Clans your accounts are in:** a list of `mine` sources, each showing the accounts found, with **Make main**
  and **Remove**, plus **Add a clan your accounts are in** (the same search).
- **Clans you're watching:** a list of `watch` sources with **Remove**, plus **Watch a clan**.
- **Top of the battle:** an on/off switch for the group-list source, when that recipe is installed.
- **Changes apply at once.** A new source starts reading on the next Start (or immediately if running).

### 7.2 Your accounts

- **The table:** each RoRoRo account with **Send** (per account, as today) and the clans it was found in.
- **The line:** "Accounts come from RoRoRo. Last listed 2 min ago."

### 7.3 Stats

One page per recipe.

- **One table, replacing** the Stats section, Look up, "Add a game statistic" and "Add this statistic".
- **Rows:**
  - recipe values in recipe order;
  - then every counter name the source offers, in source order;
  - then saved choices the recipe no longer offers, greyed.
- **Columns:** label, Show, Send, and "Name RoRoRo uses" (only on rows with Send ticked; pinned once ticked).
- **A search box** filters by label, ignoring case. Ticked rows always stay visible, with "Showing 12 of 75 ·
  ticked stats always shown".
- **Counter names:** saved names are used. If there are none and the recipe has `counters`, one read runs when
  the page opens ("Reading stat names once from ps99.biggamesapi.io…").
- **The table virtualizes rows.**
- **Unchanged:**
  - the slot line;
  - the budget refusal that undoes a Send tick;
  - `StatRules` collisions and blank names;
  - "tick at least one stat".
- **At import** there is no read before the user accepts the hosts. The import screen shows recipe values and
  **"Show every game statistic (reads once from the hosts above)"**. It also lists each headline item under
  "Kept in your score book".

### 7.4 Recipes

- **The list:** installed recipes, each with icon, name, hosts, "Asks every N s", and **Remove**, which never
  deletes the book.
- **Import recipe…** opens today's import review screen, unchanged apart from §7.3.

### 7.5 Alerts

- **Today's rule card, moved here:** the "Rule for" picker, the rule sentence, **Add this rule to RoRoRo**,
  and the metric-alerts-are-off note.
- **Today's report policy card**, beside it.

### 7.6 Score book

- **Folder:** the path, with **Open folder**.
- **Per recipe:** readings kept, first reading, finals, and size on disk.
- **When a source isn't recording, why:**
  - stopped;
  - nothing to read;
  - no stat ticked;
  - RoRoRo has never listed your accounts;
  - the period ended and its final is saved;
  - the source isn't answering.

### 7.7 Diagnostics

- **Per source:** state, detail, last read, and next read.
- **The cell and stat misses,** as today.
- **Copy diagnostics,** which includes no book content.

## §8 Starter boards (stage 1)

Superseded by the board-chrome spec, 2026-09-23 (`docs/2026-09-23-board-chrome-design.md` §3): the top bar below
still names **Start**/**Stop** and **Test now** as buttons; they are now one status chip and ⟳.

In stage 1 the window shows one fixed board: the **Battle** starter board below, or **Grind** when no recipe
with a `period` is installed. Its panels are the §9.4 panels with fixed settings. Stage 2 makes them editable
and adds tabs.

**Battle**, in grid order (12 columns):

1. **Clan standing** (3 wide) for the ★ main source.
2. **Clan standing** (3 wide) for the first other `mine` source.
3. **Battle race** (6 wide): main, mine and watch sources, up to 5.
4. **My accounts** (5 wide), by the first shown stat.
5. **Promotion check** (4 wide): the first other `mine` source → main. Shown when both exist.
6. **Account card** (3 wide) for the top account.
7. **Top of the battle** (4 wide), when enabled.
8. **Past battles** (4 wide) for the main source.

**Grind:** **Profile stat** (6 wide) for each of the first two shown profile stats, **Records** (3 wide), and
**Account card** (3 wide).

- **The top bar:**
  - tabs (stage 2), and the period line ("Autumn Battle · ends in 3d 4h · next read 2 min");
  - **Start**/**Stop**, **Test now**;
  - **Edit board** (stage 2), **⚙ Setup**.
- **First run with no recipes:** "Import a recipe to start" with **Import recipe…**.
- **With recipes but no stats ticked:** "No stats turned on yet" with **Choose stats**.

## §9 Boards and panels (stage 2)

### 9.1 Reading the book

- **`ScoreBookReader` loads each slug's lines once,** then applies new lines as the writer notifies.
- **It keeps, per source and account:**
  - **Series:** per stat, `(t, value, asOf?, stale?)` for the current period, or the last 30 days without a
    period.
  - **Finals:** per period.
  - **Headline series:** per source, for race lines.
  - **Records** (§9.5).
  - **Counts** for §7.6.
- **Duplicates collapse:** consecutive identical readings with the same `asOf` (or, without `asOf`, within the
  recipe's interval) become one point.

### 9.2 Boards

- **`boards.json`:** `[{id, name, panels: [{id, type, size, order, settings, popout?: {x, y, w, h}}]}]`.
- **Tabs** across the top: click to switch, **+ Board** adds one (empty or from a starter), and right-click
  offers Rename, Duplicate or Delete.
- **Edit board:**
  - panels show drag handles, a size menu (small = 3, half = 6, wide = 12 columns; tall panels take two rows),
    ⋯ settings and ✕ remove;
  - panels flow in order in a 12-column grid;
  - dragging changes order;
  - **Done** saves.
- **Outside edit mode,** each panel shows ⧉ (pop out) and ⋯ (settings).

### 9.3 Pop-out panels

- **⧉ opens the panel in its own small window,** always on top, borderless title strip with the panel's name,
  resizable, and position remembered.
- **It updates live.** Closing it returns the panel to its board.
- **Several can be out at once.** Pop-outs reopen where they were when Ur Score starts.

### 9.4 The panel gallery

**+ Add panel** opens the gallery. Each card says what the panel needs, and adding one asks only for that. Any
type can be added more than once.

| Panel | Needs | Shows |
|---|---|---|
| Standing | a source with a headline | Place, total, last hour's gain, the period line. Gap to the one above only when the group list has both it and the one above. Beside the place, that source's own icon when its recipe reads one (never the window's), named for the source; its space is kept while the picture isn't there |
| Race | 2–5 sources | Each source's headline total over the current period, one line each, from the book, plus live |
| My accounts | a stat | Your accounts by that stat, grouped by source (★ first, then mine, then "Not in a watched clan"), rank in clan, change with span, a SENT dot, and a stalled mark (§9.6) |
| Promotion check | from source, to source (default: → main) | For each account in *from*: where its current value would rank among *to*'s live rows, and *to*'s lowest value. Live only |
| Account card | an account | Big numbers for shown stats, line for a picked stat, rank in clan, best period, best rank, periods played, last read, miss reason |
| Past periods | a source | Finals newest first: period, place, total, your best account |
| Records | a stat | Across your accounts: best period, best rank, highest value, biggest day, fastest 7 days |
| Top | a group-list source | Its rows live, with your sources' groups placed where they'd rank |
| Profile stat | a stat (no-period recipe) | Your accounts with value, today's gain, 7-day gain, private and unavailable reasons |
| Live leaderboard | a source | Every row live, your accounts marked, a column per shown stat. Never saved |

- **Panel titles** use the recipe's words (§3.4): "Clan standing", "Past battles".
- **Settings can go stale:** a panel whose source or stat is gone shows "This panel's clan was removed" with
  **Choose another**.

### 9.5 Records

Per account and stat:
- **Best period:** the highest final.
- **Best rank:** the lowest final rank.
- **Periods played:** finals with a value.
- **Highest value seen.**
- **Biggest day:** the largest rise between the first and last reading of a local calendar day, using `off`.
- **Fastest 7 days:** the largest rise across any 7 local days.

### 9.6 Honest numbers

These come from `docs/2026-09-13-stats-games-icons-design.md` §6.3 and still hold.

- **Change always states its span** ("+220K in 1h"), and says "no earlier read" when there isn't one.
- **Overdue:** a source whose last read is older than 1.5× its interval shows an overdue mark on its panels.
- **Stalled:** an account's stat didn't change over the last two readings, while more than half of your other
  accounts in the same source did (at least two others). Display only.
- **Missing values** sort last and never count as zero.
- **A rank's N counts only who has a value:** "#7 of 47" is a place among the rows with that stat, live or
  remembered, the same field Promotion check ranks against. Ties share a place (1, 2, 2, 4), and the gap to the
  group above a tie measures to the nearest group ranked higher.
- **A colour follows the sign:** Standing's change is cyan only for a rise, magenta for a fall, and neither for no
  change; its words always carry the sign.
- **The sent dot is the last read's:** it marks an account whose number for that stat went to RoRoRo in its
  source's last read, not earlier in the session.
- **No invented range:** a chart whose values are all equal draws through the middle with one grid line naming the
  value; a chart drawn from zero keeps zero on its axis at either end.
- **A record is one source's:** records never merge two sources' readings (§9.5).
- **An icon is one source's:** a picture is kept per source, never per recipe. The window and taskbar show the switched-on
  main's, and wait for it rather than showing another source's; with no main they keep Ur Score's own. Each source's
  picture comes back from the cache at start, before any read.

## §10 The try-out command (stage 1)

```text
626labs.ur-score.exe --try <recipe.json> [--input id=value]... [--stat key]... [--account robloxUserId]... [--json]
```

- **Runs the recipe once and exits.** No window, RoRoRo, state, book or single-instance mutex.
- **Keys** come from `KeyStore` and are never printed.
- **Default stats:** every recipe value, when no `--stat` is given.
- **Output:**
  - the parse result and refusals;
  - hosts contacted and what each received;
  - outcome and detail;
  - rows seen;
  - headline ids and values;
  - per stat: found and missed counts, with the smallest, median and largest value;
  - counter names;
  - for each `--account`: its values, and each rank with how many rows had that stat ("rank 1 of 47");
  - `period.past` keys;
  - for group lists: the count, and the first 10 group names (clan names aren't players).
- **Never printed:** a player's id, name or single value.
- **`--json`:** the same output as one JSON object.
- **Exit codes:** 0 read, 2 recipe refused, 3 input missing, 4 read stopped, 5 bad arguments.
- **Console:** attaches to the parent console, or writes to redirected stdout.

## §11 Smoke scripts (stage 1)

- **`tools/smoke/`:**
  - `uia.ps1` and `uia-import.ps1` (helpers);
  - `window-smoke.ps1`;
  - walks for Setup › Clans, the Stats table, the starter board, the score book page, and (stage 2) board
    editing and pop-outs;
  - `shot.ps1` (you pass the output path).
- **`README.md`.**
- **Paths:** the repo root is found from the script's own location.
- **Your data is safe:** walks that need a clean data folder move yours aside and restore it in a `finally`.

## §12 Build order

Each step ends green (warnings as errors, all tests). Each stage is walked live before the next starts.

**Stage 1: data, Setup and the starter board** (releasable as v0.2.0)
1. Parser: `period`, `asOf`, headline ids, input `plural`, group lists, the digit refusal. Fixtures (§3.7).
2. Sources: `sources.json`, migration from part 2a state, `SourceHost` running watches, and the shared account
   map with `accounts.json`.
3. `ScoreBook` writer and the line builder. Watches record reading lines (§5).
4. Finals and backfill (§6).
5. Setup window: Clans (§7.1), Accounts, Stats (§7.3 plus the import screen change), Recipes, Alerts, Score
   book, Diagnostics.
6. `ScoreBookReader` (§9.1), records (§9.5), and the ten panels as controls with fixed settings.
7. The window becomes the starter board (§8). Today's grids and cards leave the main window.
8. The try-out command (§10). The smoke scripts (§11). A live walk of stage 1.

**Stage 2: the panel system**

9. `boards.json`, tabs, edit mode, panel settings, the gallery.
10. Pop-out panels.
11. A live walk of stage 2.

**Release**

12. Version 0.2.0 in `manifest.json` and the csproj.
13. Make the repo public, tag, install from the release URL into a clean plugin folder, and walk it.
14. Draft the clan how-to post.

## §13 Testing

- **Parser:**
  - the digit refusal in every path kind, and allowed inside a placeholder;
  - `period` needs a known `take`;
  - `past` refused on per-account recipes;
  - duplicate headline ids;
  - `groupName` with `userId` refused.
- **Sources:**
  - migration from 2a state;
  - one main per recipe;
  - hosts spaced and serialized;
  - an account in two sources goes to the first;
  - one send per account per recipe per cycle;
  - `watch` sources send nothing.
- **Line builder:**
  - of 50 rows with 2 yours, only yours are written;
  - Show-only stats are recorded;
  - `unavail` holds only your ids;
  - competition ranks with ties;
  - text headlines are dropped;
  - a headline equal to another row's id is dropped;
  - a saved fake key appears nowhere;
  - `watch` lines have empty accounts;
  - group lists write nothing.
- **Writer:**
  - a missing newline is repaired;
  - NUL and broken lines are skipped;
  - a locked file retries and keeps its pending lines;
  - the UTC month file;
  - recipe text is stored once per hash;
  - removing a recipe or source keeps the book.
- **Finals:**
  - backfill of several past battles from a fixture;
  - no duplicates across restarts;
  - a later alt gets its finals;
  - a period ends by `ends` and by being replaced;
  - no reading lines after the end;
  - a headline-only final.
- **Accounts cache:** RoRoRo down with the cache records; without it, per-account data isn't recorded and the
  reason shows.
- **Reader:**
  - duplicate collapse;
  - records, including biggest day across DST using `off`;
  - would-place;
  - stalled;
  - overdue.
- **Boards:**
  - `boards.json` round-trip;
  - starter boards built from the sources present;
  - a stale panel setting shows its message.
- **Try-out:**
  - output never contains another row's id or name (a fixture with known ones is searched);
  - exit codes;
  - `--json` parses.
- **Fences stay green:**
  - hostname;
  - theme (new panels use theme brushes only);
  - the budget and report policy tests.
- **Live:**
  - stage walks;
  - a real read of two clans and a rival that writes lines and backfills CCGP's past battles;
  - install from the release URL.

## §14 Risks

- **Scope for Saturday.** Stage 1 alone is a real scoreboard with records. Stage 2 ships only if it's solid.
- **Big Games trims `Battles` or changes shapes.** Written finals stay; only older backfill is lost.
- **Request volume.** Each clan source is two calls every 3 minutes (activeClanBattle plus the clan). Five
  sources is about 3,300 calls a day, spaced per host. Setup shows the total ("Your PC asks Big Games about N
  times an hour"), and a sixth clan source asks for confirmation.
- **A headline that is really another player's id.** §5.6 drops ids seen in the rows. An owner who didn't
  contribute could still be recorded if a recipe headlines them. The import screen lists headline items under
  "Kept in your score book". The recipe builder (part 3) can refuse id-like headline keys.
- **The main window is rebuilt.** Automation ids that smoke scripts and the theme fence rely on stay, or move
  with their tests in the same commit.
- **"Would place" is an estimate:** it ranks your current value against the other clan's current rows, and the
  panel says "if you were in CCGP now".

## §15 Later

- Folding accounts a profile source doesn't know.
- Thinning the book.
- Merging books from two PCs.
- A trend read-back from RoRoRo.
- A points gap to clans outside the top-100 sample.
- The trading tool.
- Ur MCP tools.
