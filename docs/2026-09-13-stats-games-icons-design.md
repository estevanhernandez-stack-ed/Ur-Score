# Ur Score stats, games and icons: design

> An addendum to `2026-09-13-recipes-design.md`, the recipes design. Everything there still holds
> except where a section below says it replaces something: §3.1's single `metricId`, §3.2's single
> `value`, §6.2's import screen, §8's window and §13's build order.
>
> Designed with Este on 2026-09-13. Section 4 (the window) was revised after an adversarial pass by
> four reviewers written as players: an alt farmer, a clan officer, a stats obsessive, and a
> returning casual player.

## §0 Why

Recipes part 1 made Ur Score read one number per recipe: the Pet Sim 99 clan battle's points. Three
things came up the moment it ran live.

1. **Clan battles run about every other week.** Between them the clan recipe has nothing to show, and
   a clan that hasn't joined the current battle read as "the response was not a shape Ur Score
   understands".
2. **Personal stats are richer and always there.** Pet Sim 99's public player profile carries
   diamonds, eggs hatched, rank, rebirths and hundreds of named counters, battle or no battle. People
   want to see several of these at once, and only some are for alerts.
3. **People play more than one game,** and a clan has an icon people recognise.

### Facts verified live on 2026-09-13

| Check | Result |
|---|---|
| `GET ps99.biggamesapi.io/v1/players/{name or id}?include=profile` | 200 for a player who made their profile public. Diamonds at `data.views.profile.data.Currency.Diamonds._am`, also `EggsHatched`, `Rank`, `RankStars`, `Rebirths` and a `Statistics` object of named counters |
| Same, profile view not public | Documented as 200 with `data.views.profile` = `{ "available": false, "reason": "not_public" }`. With no public view at all, the answer is 404 `player_not_found` |
| Profile freshness | Refreshed at most every 30 minutes (15 for VIP) |
| `GET ps99.biggamesapi.io/v1/leagues/{name}` | 200. `data.PointContributions[]` rows carry `UserID` and `Points`; also `data.Points`, `data.Level`, `data.Icon` |
| `GET ps99.biggamesapi.io/api/clan/CCGP` | 200 with `data.Icon` = `rbxassetid://14976358748`, and no `data.Battles.ArcadeBattle2026` because CCGP hasn't joined |
| `GET thumbnails.roblox.com/v1/assets?assetIds=14976358748&size=150x150&format=Png` | 200, `data[0].state` = `Completed`, `data[0].imageUrl` on `tr.rbxcdn.com` |
| Ur Score's recipe engine running a league recipe and a profile recipe, unchanged | Both parsed and read correctly (4 league rows; 9,169,613,101 diamonds for the docs' example player) |
| RoRoRo `MetricHistory` | `capacity` 64 samples per series, `maxSeries` 256. A new series past 256 is silently refused |
| `friends.roblox.com/v1/users/{id}/friends/count`, `.../followings/count` | 200, `count`, no sign-in |
| Bubble Gum Simulator INFINITY | No public stats API. Player and Guild data sits in the developer's private storage. bgsi.io needs Roblox sign-in |
| Other large games checked (Blox Fruits, Bee Swarm, Adopt Me, Jailbreak, Toilet Tower Defense, Anime Defenders, Doors, Rivals, Dress to Impress, Grow a Garden) | No official player-stats API. Trackers scrape |

## §1 Decisions, as made

1. **A recipe offers a list of stats. One read serves all of them.** Rejected: a hidden copy of the
   recipe per stat, and one file per stat. Both multiply requests by the number of stats.
2. **Each stat has two ticks. Show puts it on Ur Score's board; Send reports it to RoRoRo.** Alerts
   still need a rule in RoRoRo, so a sent stat without a rule just builds history.
3. **Counters beyond the recipe's list come from a search** over the names the source actually
   returned. A misspelled name can't be added.
4. **A clan not in the current battle is idle, not an error,** when the recipe opts in.
5. **The clan icon becomes the window and taskbar icon and sits beside the heading,** falling back to
   Ur Score's own icon.
6. **The board is a table of your accounts, and clicking one opens its card.** A panel per stat
   (break-out panels) comes later.
7. **Several games: a game list with a starred default.** Accounts a profile source doesn't know fold
   away on their own.

## §2 The principle, unchanged

A recipe describes where numbers are and what the data means. It never decides what happens with a
number. Consequences here:

- **Nothing is ticked by default.** A recipe that pre-ticked stats would be deciding what gets sent.
- **The data-meaning fields describe the data and nothing more.** `unavailable`, `absentMessage` and
  `sum` say what an answer means ("this profile is private", "this clan isn't in the battle",
  "adding these up is meaningless"). None of them triggers anything.
- **Ur Score's own text names no game and no vendor.** Source-specific wording comes from the recipe.

## §3 The recipe format, additions

### 3.1 A worked example

```json
{
  "recipe": 1,
  "name": "Pet Sim 99 profile",
  "credit": "Data from Big Games' public Pet Simulator 99 API. Each account must make its profile public in-game.",
  "everySeconds": 1800,
  "steps": [
    { "url": "https://ps99.biggamesapi.io/v1/players/{userId}?include=profile",
      "perAccount": true,
      "unavailable": { "path": "data.views.profile.available", "is": false,
                       "message": "Profile is private. Make it public in Pet Sim 99's dashboard." },
      "values": [
        { "id": "diamonds", "label": "Diamonds", "path": "data.views.profile.data.Currency.Diamonds._am", "metricId": "ps99.diamonds" },
        { "id": "eggs", "label": "Eggs hatched", "path": "data.views.profile.data.EggsHatched", "metricId": "ps99.eggs-hatched" },
        { "id": "rank", "label": "Player rank", "path": "data.views.profile.data.Rank", "metricId": "ps99.rank", "sum": false }
      ],
      "counters": { "label": "Game statistics", "path": "data.views.profile.data.Statistics", "metricIdPrefix": "ps99.stat." } }
  ]
}
```

And the clan battle recipe, gaining its idle message and icon:

```json
{
  "recipe": 1,
  "name": "Pet Sim 99 clan battle points",
  "credit": "Data from Big Games' public Pet Simulator 99 API.",
  "metricId": "clan.battle.points",
  "valueLabel": "Points",
  "placeLabel": "Clan place",
  "everySeconds": 180,
  "inputs": [ { "id": "clan", "label": "Your clan", "search": { "url": "https://ps99.biggamesapi.io/api/clansList", "list": "data" } } ],
  "steps": [
    { "url": "https://ps99.biggamesapi.io/api/activeClanBattle",
      "take": { "battle": "data.configName" },
      "idleWithout": "battle", "idleMessage": "No clan battle running" },
    { "url": "https://ps99.biggamesapi.io/api/clan/{clan}",
      "absentMessage": "Your clan hasn't joined this battle.",
      "rows": "data.Battles.{battle}.PointContributions",
      "userId": "UserID", "value": "Points" }
  ],
  "headline": [
    { "label": "Clan place", "path": "data.Battles.{battle}.Place", "sum": false },
    { "label": "Clan points", "path": "data.Battles.{battle}.Points" }
  ],
  "icon": "data.Icon"
}
```

### 3.2 Fields

| Field | Where | Meaning |
|---|---|---|
| `values` | Last step | A list of `{ id, label, path, metricId, sum }`. Each entry is one stat the user can tick. `path` is read like `value` today: relative to a row (list form) or to the response (per-account form). `metricId` is the suggested name RoRoRo sees. `sum` defaults to `true`. |
| `value` | Last step | Still accepted. With the recipe's `metricId` and `valueLabel`, it is shorthand for a one-item `values` list with id `value`. |
| `counters` | Last step, optional | `{ label, path, metricIdPrefix }`. An object whose keys are named numbers. A picked name becomes a stat whose path is `path` plus that name, and whose suggested metric id is the prefix plus a slug of the name (lowercase, spaces to hyphens, anything outside `a-z 0-9 -` dropped). Names containing a dot can't be picked. |
| `unavailable` | Per-account step, optional | `{ path, is, message }`. When the value at `path` equals `is` (JSON true, false, a number or a string), or the request answers 404, that account shows `message` and only that account is skipped. A 404 also counts toward folding (§6.4). |
| `absentMessage` | Any step, optional | When a path in this step misses at a segment that came from a placeholder, and the object that should hold it exists, the recipe is idle with this message. Any other miss is still a changed shape. |
| `sum` | A value or a headline entry | `false` for stats where a total is meaningless, such as a rank or a level. |
| `icon` | Top level, list form only | A path read from the last step's response, like `headline`. See §3.3. |
| `placeLabel` | Top level, list form only | The label for your accounts' position among the rows. Defaults to `Place`. |

`metricId` at the top level is required only when the last step uses the single `value`.

### 3.3 The icon

- **A `rbxassetid://N` value:** Ur Score asks Roblox's thumbnails service for asset N at 150×150 PNG.
  It fetches the image only when the answer's `state` is `Completed` and `imageUrl` is https on a
  host ending in `.rbxcdn.com`.
- **An https URL value:** used only when its host is one this recipe already contacts.
- **Anything else is ignored** and the Ur Score icon stays.
- **Both fetches** use the recipe client's rules: no redirects, no cookies, Ur Score's User-Agent.
- **The image** must be a PNG or JPEG no larger than 1 MB that decodes. It is cached for 7 days in
  `%LOCALAPPDATA%\626labs.ur-score\icon-cache\{N}.png`.

## §4 Reading

- **Tracked means Show or Send is ticked.** Only tracked stats are read. One response per row or per
  account serves all of them, so adding stats never adds requests.
- **Misses cost only what they touch:**

| What went wrong | What it costs | What shows |
|---|---|---|
| One stat's path misses for one account | That cell | A dash. The reason, naming the keys present, is in Diagnostics and the account card |
| One stat misses for every account in a read | That stat | Its column header says it couldn't be read, with the keys present. Other stats continue |
| `unavailable` matched, or a per-account 404 | That account | Its row shows the recipe's message |
| Unreachable, rate limited, key missing or rejected, sign-in required | The cycle | Today's states, unchanged |

- **Change since last read** generalizes `PointsRate` to every stat for every account, from the last
  two reads, in memory, and carries its span (§6.3). A context change (a new battle) clears it, as
  it clears rates today. List recipes keep Rate/min for their value.
- **Counter names** are captured after a successful read: the keys under `counters.path` whose values
  are numbers, from your first account's response (per-account form) or the first row (list form).
  They are saved in the recipe's state. **Look up stat names** does one read to fill them before the
  first run.

## §5 Your choices and reporting

### 5.1 State

Per recipe, beside `inputs` and `excludedAccountIds`:

```json
"stats": {
  "diamonds":                 { "show": true, "send": true,  "metricId": "ps99.diamonds" },
  "eggs":                     { "show": true, "send": false, "metricId": "ps99.eggs-hatched" },
  "counter:Huge Pets Opened": { "show": true, "send": false, "metricId": "ps99.stat.huge-pets-opened" }
}
```

- **Keys:** a recipe stat is keyed by its `id`; a searched counter is `counter:` plus its name.
- **`metricId` is pinned** when the stat is first ticked, so an update can't move where reports go
  (part 1's Ruling J, per stat).
- **Replaces `metricIdOverride`.** Nothing is released, so there is no migration.

### 5.2 What gets sent

- **A number is reported only when both the account's Send and the stat's Send are on.**
- **`ReportPolicy` stays the only call site of `ReportMetricAsync`.** Its checks become: the metric id
  is one of this recipe's stats with Send on; the subject is one of your accounts with Send on; the
  value is finite. The existing fence test is kept.
- **The policy sentence names every sent stat**: "Ur Score sends Diamonds and Player rank for 4 of
  your 5 accounts, as `ps99.diamonds` and `ps99.rank`."
- **Metric ids are unique within a recipe.** Saving settings that would send an id another installed
  recipe already sends is refused, naming that recipe: two sources feeding one series make any rule
  on it meaningless.

### 5.3 RoRoRo's history budget

- **The count:** accounts with Send on × stats with Send on, summed across every installed recipe.
  It shows as "40 of RoRoRo's 256 history slots", with that basis stated.
- **From 200** the line warns that other plugins share the same slots.
- **A Send tick that would pass 256 is refused,** with the line saying why. RoRoRo would otherwise
  drop the newest series without a word.

### 5.4 The rule helper

Picks a sent stat first. Then kind, threshold and window are typed as part 2 already planned.

## §6 The window

Replaces §8.1 of the recipes design. §8.2 (searchable inputs) and §8.3 (theme) still hold.

### 6.1 Game list

- **Each entry:** the icon (§3.3, else Ur Score's), the recipe name, a status dot, Start and Stop.
- **Star one as the default.** It opens when the window opens. The window and taskbar icon follow
  the selected entry.
- **Add a game** holds Import and, once part 3 lands, Build one. It says: "Most Roblox games don't
  publish player stats, so only games that do can be added."

### 6.2 The board

- **List recipes:** the headline with the icon, then the leaderboard. The leaderboard has a column
  per shown stat, sorts by the first shown stat, and re-sorts on a header click. It shows "read 40s
  ago".
- **Your accounts table.** Columns, in order:
  - a Send checkbox (the header box selects all);
  - the account;
  - `placeLabel` (list recipes only);
  - one column per shown stat.
- **Cells:**
  - The value abbreviated (9.17B), with the full number on hover.
  - The change with its span (§6.3).
  - A small SENT mark when that stat is sent, dimmed where the account's Send is off.
- **The legend** under the table reads "SENT: reported to RoRoRo. Alerts need a rule there."
- **"Your total" row:** captioned with coverage and read times, such as "4 of 5 accounts, read 3–12
  min ago". Stats with `sum: false` show no total.
- **Sorting** works on both tables. Missing, private and not-yet-read values pin to the bottom in
  either direction and never count as zero.
- **Above the table:** "Next read in 18 min · reads every 30 min".
- **Headline values** get a change too.

### 6.3 Honest numbers

- **Span:** every change says over what, e.g. "+2.3M in 31m". When the earlier read is from before a
  fold-back, a restart or a context change, the cell says "no earlier read" instead of a number.
- **Overdue:** a row whose last read is older than 1.5× the recipe's interval is marked overdue.
- **Stalled:** a stat shows a stalled mark for an account when it didn't change while more than half
  of your other accounts with a read in the same cycle did change on that stat. It needs at least two
  other accounts. The mark is display only; nothing is sent or triggered by it.

### 6.4 Empty, idle and folded

- **No stats shown:** the board says "No stats turned on yet" with a **Choose stats** button.
- **Idle list recipe** (between battles, or `absentMessage`):
  - The board keeps your accounts' last values and the headline, labelled with the idle message and
    "last read Sep 6, 18:02".
  - That snapshot (your account ids, stat values, headline texts, time) is saved in the recipe's
    state so it survives a restart.
  - The leaderboard, which holds other members, is never saved.
- **Profile recipes fold accounts the source doesn't know.**
  - An account whose request answers 404 shows the recipe's `unavailable` message (or "The source
    doesn't know this account") for its first two reads.
  - On the 3rd consecutive 404 it moves into a collapsed **Not in this game (n)** group, captioned
    "The source doesn't know these accounts, or their profiles are fully private." Pet Sim 99 can't
    tell those two apart, so the caption names both.
  - It is re-read once a day instead of every cycle.
  - **Check again** on the group or on one account re-reads it now and restores it if it answers.
  - An account whose answer matched `unavailable` by value (a readable reply saying its profile view
    is private) is **not** folded. Its message stays in the table, because the player can fix it.
- **List recipes never fold.** Your accounts missing from the rows stay in the table as "Not in the
  results yet".

### 6.5 The account card

Clicking an account opens a side panel in the same window: its shown stats as large numbers with full
precision, each change with its span, rank progress where the recipe provides it, the time of the
last read, and any miss or unavailable reason.

### 6.6 Recipe settings: Stats

- **The table:** Show, Send and **Name RoRoRo uses** for each stat.
- **Add a game statistic:** the counter search (§3.2).
- **Look up stat names** before the first read.
- **The history-slot line** (§5.3).

### 6.7 Later

- **Break-out panels:** a stat as its own small window beside the table or a Roblox client.
- **Trend lines:** these need a RoRoRo capability to read metric history back, which the plugin
  contract doesn't have.
- **Points gap to the next clan:** a recipe concern, since it needs rival clans' numbers.

## §7 Import

Replaces §6.2 of the recipes design and extends §6.3 and §6.5.

### 7.1 The screen

- **Name, credit and author** labelled unverified, as before.
- **Every host and what it receives.**
  - A per-account step's host line reads "receives the Roblox user id of every account in your
    RoRoRo list". Send only controls what reaches RoRoRo, so an account with Send off still has its
    id sent here.
  - An `rbxassetid` icon adds two lines: `thumbnails.roblox.com` "receives the picture's id, to find
    the icon", and `tr.rbxcdn.com` "sends the picture".
- **Stats:**
  - Show, Send and **Name RoRoRo uses**, with nothing ticked.
  - **Look up stat names (reads once from the hosts above)** appears when the recipe has `counters`.
    Nothing else runs before Import.
  - Import stays disabled with "Tick at least one stat to show or send."
- **The history-slot line** shows what this import adds.
- **Poll interval,** and whether RoRoRo already has a rule for each sent stat.

### 7.2 Updates

| Change in the new version | Result |
|---|---|
| A new stat is offered | Listed ("New stat: Rebirths"), unticked, no ask |
| A stat you don't track is removed or its path changes | Listed |
| A stat you track has its path change | Listed. Its metric id stays pinned |
| A stat you send is removed | **Asks again**: "Player rank will no longer be read, so RoRoRo stops getting `ps99.rank`." |
| An icon is added that needs Roblox's picture hosts | **Asks again** (new hosts) |
| `absentMessage`, `unavailable`, `sum` or `placeLabel` changes | Listed |
| Hosts, or what a host receives, change | **Asks again**, as before |

### 7.3 Validation additions

Each problem names its field and step:

- `values` and `value` in the same step.
- A duplicate value id, or a duplicate `metricId` within the recipe.
- `sum` that isn't true or false.
- `counters` without a valid `path`.
- `icon` or `placeLabel` on a per-account recipe.
- `unavailable` on a list step, or missing `path`, `is` or `message`.
- `absentMessage` that isn't text.

## §8 Fences

- **No hostname in `src/`.** A new `IconClient` holds `thumbnails.roblox.com` as a second named
  exemption beside `NameClient`'s `users.roblox.com`. Both are Roblox's own services behind Ur Score
  features (names and icons), not stat sources. A test pins each file to exactly its one host. The
  picture host is never written in code: it comes from Roblox's answer and must end in `.rbxcdn.com`.
- **One call site of `ReportMetricAsync`**, kept.
- **No colour written into a window** (`ThemeFenceTests`), kept.
- **The saved idle snapshot holds only your own account ids.** A test writes a snapshot from a read
  with other members' rows and asserts none of their ids or values are in the state file.

## §9 Testing

**Unit, part 2a:**
- **Parser:** every §7.3 rule.
- **Engine:**
  - Several stats from one response, in both forms.
  - A one-cell miss, and a stat-wide miss.
  - `unavailable` by value and by 404.
  - `absentMessage` idle only for a missing placeholder segment inside an existing object; any other
    miss stays a changed shape.
  - Untracked stats are not read.
  - Counter names are captured.
- **Policy:** both Sends required; several metric ids allowed; the one-call-site fence.
- **Budget:** the count basis, the warning from 200, the refusal past 256.
- **State and updates:** metric ids stay pinned; every row of §7.2.
- **Icon:**
  - `rbxassetid` resolution.
  - A non-`rbxcdn.com` image refused.
  - Redirect refused.
  - An https icon on a recipe host accepted; any other host refused.
  - Oversize or undecodable image refused.
- **Fences:** the `IconClient` exemption.

**Unit, part 2b:**
- Change with span, and "no earlier read" after a fold-back, restart or context change.
- Overdue at 1.5× the interval.
- The stalled rule, including its two-account minimum.
- The totals caption, and `sum: false`.
- Sorting with missing values pinned.
- Fold after 3, daily re-read, Check again; list recipes never fold.
- The idle snapshot survives a restart and holds no other members.

**Window smoke:** the UI Automation smoke from part 1
(`docs/smoke-2026-09-13-recipes-window.md`) grows to cover each part, run against RoRoRo on a real
desktop.

**Live acceptance:**
1. Este makes one account's profile public in Pet Sim 99.
2. Import the profile recipe; show Diamonds, Eggs hatched and Player rank; send Diamonds.
3. Play and watch the values change across 30-minute reads.
4. Write a rule on `ps99.diamonds` and see a real alert.
5. The clan recipe shows CCGP's icon on the taskbar and its idle message when CCGP isn't in the
   battle.

## §10 Build order

Replaces §13 of the recipes design.

1. **Part 1, engine, format and import:** done, in PR #2. Merge first.
2. **Part 2a, stats:**
   - §3 (format, including icon resolution), §4, §5 and §7.
   - The current window shows each shown stat as a column; no board redesign yet.
   - Exit: the profile recipe reports Diamonds for Este's account, and the clan recipe idles on
     `absentMessage`.
3. **Part 2b, the window:**
   - §6.
   - Part 2's existing items: key entry, searchable inputs, accounts loaded on open, the rule helper
     with a typed threshold.
   - The `KeyStore.Load` fix before key entry ships.
   - A decision on ports, localhost and private addresses.
4. **Part 3, builder:** as designed, and it also produces `values`, `counters` and `unavailable`.

## §11 Risks

- **Profiles are opt-in.** Every account must be made public in-game. The `unavailable` message says
  exactly that, and the recipe credit line says it at import.
- **The stalled mark can be wrong,** for example when one account is deliberately idle. It is
  display only and costs nothing but a glance.
- **The history budget is shared with other plugins** that Ur Score can't see. The warning from 200
  leaves room; the refusal past 256 only guards Ur Score's own share.
- **Big Games' profile shape can change.** Misses name the keys present, so the fix is a one-line
  recipe edit someone can share.
- **Most games have no stats API.** The window says so plainly (§6.1) rather than implying a game
  was forgotten.
