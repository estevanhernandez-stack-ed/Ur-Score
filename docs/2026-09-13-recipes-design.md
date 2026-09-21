# Ur Score recipes — design

> Supersedes §2 of `2026-09-12-ur-score-design.md` ("The endpoint is compiled in; the clan is
> configured"). That decision made Ur Score a Pet Simulator 99 plugin. This one makes it a
> reporter for any web stat, with Pet Sim 99 as the first recipe rather than the product.
>
> Host-side spec this still answers to:
> `ROROROblox/docs/superpowers/specs/2026-09-09-external-metric-alerts-design.md`.
>
> **Extended 2026-09-13 by `2026-09-13-stats-games-icons-design.md`,** which replaces this
> document's single `metricId` and `value` (§3.1, §3.2), the import screen (§6.2), the window (§8.1)
> and the build order (§13). Read both.

## §0 Why this exists

Metric alerts were meant to work with stats from any Roblox game. RoRoRo's side already does: it
accepts `ReportMetric(subjectId, metricId, value, observedAt)` from any consented plugin, keys its
rules by metric id, and names no game or vendor anywhere in its binary.

Ur Score does not. It knows one address (`ps99.biggamesapi.io`), one two-request flow, and one data
shape. Every other game would need its own plugin. Seeing the first build running on 2026-09-13
made that gap concrete, and this design closes it.

### Facts verified live on 2026-09-13, which the design depends on

| Check | Result |
|---|---|
| `GET ps99.biggamesapi.io/api/activeClanBattle` | 200, `data.configName` = `ArcadeBattle2026` |
| `GET ps99.biggamesapi.io/api/clansList` | 200, `data` is a list of 169,238 clan names, 1.1 MB |
| `GET ps99.biggamesapi.io/api/clans?page=1&pageSize=5&sort=Points` | 200, `data` rows carry `Name`, `Members`, `Points` |
| `GET friends.roblox.com/v1/users/1/followers/count` | 200, `{"count": …}`, no sign-in |
| `GET badges.roblox.com/v1/users/{id}/badges` | **401**, "Authentication token is missing" |
| Pet Sim contribution rows | fields `UserID` and `Points` (from `ClanParser.cs`) |

Most Roblox games publish no stats anywhere. Pet Sim 99 is unusual in having a public API.

## §1 Decisions, as made

1. **Sources: web stats APIs and Roblox's own public data.** On-screen numbers read by OCR are out
   of scope, even though Ur OCR already reads screen regions. Both chosen sources are the same
   shape: fetch JSON over https, find a number, tie it to an account.
2. **Setup: a point-and-pick builder and shareable recipe files. No presets.** 626 Labs ships no
   knowledge of any game, which is the same line RoRoRo already holds. Pet Sim 99 ships as a file
   someone shares, not inside the app.
3. **Keys: bring your own.** A recipe names the key a source needs; each user supplies their own.
4. **Shape: evolve Ur Score into the generic reporter.** Rejected: a second generic plugin beside a
   Pet-Sim-only Ur Score (two plugins doing one job, and the Pet Sim one would still hardcode what
   this design removes); running recipes inside RoRoRo (its reviewer letter tells Microsoft it
   contacts no new endpoint and names no game, and that has passed certification every time).

## §2 The principle

**A recipe describes where a number is and how to read it. It never decides what happens with the
number.**

No thresholds, no rules, no alerts, no choice of which accounts send, and no triggering anything in
Ur Score or RoRoRo. What happens after the read belongs to the user, in Ur Score's window (the
Send checkboxes) and in RoRoRo (the rules and destinations).

A direct consequence: **Ur Score's own binary names no vendor either.** Today `ClanClient.cs` is the
one file that names Big Games. After this change, hosts exist only in recipe files, apart from
Roblox's own username lookup. §10 fences it.

## §3 The recipe format

One JSON file, readable and diffable. Extension `.recipe.json`.

### 3.1 Fields

| Field | Required | Meaning |
|---|---|---|
| `recipe` | yes | Format version. This design is `1`. A higher number is refused with "made by a newer Ur Score". |
| `name` | yes | Shown in the recipe list and on the import screen. |
| `credit` | yes | One line naming where the data comes from. Replaces `ClanClient.Attribution`. |
| `author` | no | Who it says it is from. Always shown as unverified. |
| `metricId` | yes | The *suggested* name RoRoRo sees. Editable on import and afterwards. |
| `valueLabel` | no | Column label for the value, such as `Points`. Defaults to `Value`. |
| `everySeconds` | yes | Poll interval. Raised to the floor (§4.4) if lower. |
| `inputs` | no | Values the user fills in. §3.3. |
| `keys` | no | Keys the source needs. §3.4. |
| `steps` | yes | One or more requests, in order. §3.2. |
| `headline` | no | Up to two `{ "label", "path" }` values shown at the top. Display only, never reported. |
| `groupsAreClans` | no | For a list-form last step only: states that the rows are CLANS, not people. Defaults to `false`, and until it is `true` a read keeps **no group names at all**. See §3.2. |

### 3.2 Steps

A list-form last step (`groupName`) reads groups rather than players, so no account is ever matched, sent or
recorded from it. What the shape does NOT say is what the rows actually are: `groupName` points at whatever the
recipe's author chose, so a list of PLAYERS parses identically to a list of clans.

Names are therefore kept only when the recipe says `"groupsAreClans": true` at the top level. Without it the
read still writes its field summary — the leader, the top ten, the average, the bottom ten, and where yours
stands — so every band stays true, but not one name goes to disk. The import screen says which of the two is
happening, and a race chart with no board says why.

The claim cannot be inferred and is not set by default, because the cost of guessing wrong is strangers'
usernames written to a file nobody asked to keep (V3-S.25).



Every step has a `url`. Earlier steps can `take` values; the last step does the reading.

| Field | Meaning |
|---|---|
| `url` | https only. May contain placeholders (§3.5). |
| `useKeys` | Optional list of key ids this step sends. Each is placed as its declaration says (§3.4). |
| `take` | Map of variable name to path. Makes the value available to later steps as `{name}`. |
| `idleWithout` | A variable name. If `take` reads a JSON null on its path or an empty string, the recipe is idle, not failing. A key that is not there at all is a changed shape, reported with the keys present, never read as idle. |
| `idleMessage` | What the window says when idle, such as "No clan battle running". |
| `rows` | Last step, list form: path to an array whose items are players. |
| `userId` | Last step, list form: the field inside each row holding a Roblox user id. |
| `perAccount` | Last step, per-account form: `true`. The step runs once per account with `{userId}` filled. |
| `value` | Last step: the field holding the number, relative to a row (list form) or the response (per-account form). |

The last step uses exactly one form: `rows` + `userId` + `value`, or `perAccount` + `value`. Anything
else is a validation error naming the step.

### 3.3 Inputs

`{ "id", "label" }`, optionally with `"search": { "url", "list" }`. With `search`, the input is a
type-to-search box over the array at `list` in that response (§8.2). Every declared input must be
filled before the recipe runs. The chosen value is stored per recipe, never in the recipe file.

### 3.4 Keys

`{ "id", "label", "getOneAt", "in", "name" }`, where `in` is `header` or `query` and `name` is the
header name or query parameter. `getOneAt` must be https. A step sends a key by listing its id in
`useKeys`, and the engine places it. There is no key placeholder, so a key can never be spliced into
an address by hand. A key's value never appears in a recipe file (§7).

### 3.5 Paths and placeholders

- **Paths** are dot-separated keys: `data.Battles.{battle}.PointContributions`. A segment may be a
  placeholder. Keys containing a literal dot are not addressable in version 1, and the builder says
  so when it meets one.
- **Placeholders:** `{inputId}` (a user input), `{name}` (a taken variable), and `{userId}` (only in
  a `perAccount` step).
- Substituted into a **url**, values are percent-encoded. Substituted into a **path**, they are used
  as-is.
- An unknown placeholder is a validation error naming it.

### 3.6 Examples

Pet Sim 99, the real two-request flow:

```json
{
  "recipe": 1,
  "name": "Pet Sim 99 clan battle points",
  "credit": "Data from Big Games' public Pet Simulator 99 API.",
  "metricId": "clan.battle.points",
  "valueLabel": "Points",
  "everySeconds": 180,
  "inputs": [
    { "id": "clan", "label": "Your clan",
      "search": { "url": "https://ps99.biggamesapi.io/api/clansList", "list": "data" } }
  ],
  "steps": [
    { "url": "https://ps99.biggamesapi.io/api/activeClanBattle",
      "take": { "battle": "data.configName" },
      "idleWithout": "battle", "idleMessage": "No clan battle running" },
    { "url": "https://ps99.biggamesapi.io/api/clan/{clan}",
      "rows": "data.Battles.{battle}.PointContributions",
      "userId": "UserID", "value": "Points" }
  ]
}
```

A Roblox stat, one value per account:

```json
{
  "recipe": 1,
  "name": "Roblox followers",
  "credit": "Data from Roblox's public friends API.",
  "metricId": "roblox.followers",
  "valueLabel": "Followers",
  "everySeconds": 600,
  "steps": [
    { "url": "https://friends.roblox.com/v1/users/{userId}/followers/count",
      "perAccount": true, "value": "count" }
  ]
}
```

## §4 The engine

### 4.1 Running a recipe once

1. Resolve inputs. A required input with no value leaves the recipe idle: "Set Your clan to start".
2. Run steps in order. Each request: GET, https only, 30-second timeout, User-Agent
   `UrScore/<version> (RoRoRo plugin)`. Substitute placeholders; apply `take`.
3. If an `idleWithout` variable came back empty, stop with its `idleMessage`. Idle is normal.
4. Last step, list form: read `rows`; for each row read `userId` and `value`. Rows that cannot be
   read cost only themselves, and the first reason is kept (today's `ClanParser` behaviour).
5. Last step, per-account form: run it once per mapped account, sequentially.
6. Match user ids to RoRoRo accounts through `AccountMap`. Unmatched rows feed the leaderboard only.
7. Hand each matched account's value to `ReportPolicy`, which stays the single call site of
   `ReportMetricAsync`.

### 4.2 Misses name what was there

Every failure to find a path names the keys actually present at the point it stopped, the way
`ClanParser` does today: "No 'PointContributions' in Battles.ArcadeBattle2026. Keys present: …".
A present-but-wrong value says what it was: "'Points' is text in this row, not a number".

### 4.3 Transport failures

| Response | Behaviour |
|---|---|
| Timeout, DNS, TLS, 5xx | "Could not reach <host>". Retry next poll. |
| 429 | "<host> asked us to slow down". Skip to next poll. |
| 401 or 403 on a step using a key | Stop this recipe until the key changes (§7.5). |
| 401 or 403 on a step with no key | "<host> requires signing in, which recipes cannot do". Stop. |
| 404 on a step with an input placeholder | Name the input value: "No clan called 'x'". |

### 4.4 Poll floor

A global floor of 60 seconds, whatever a recipe says. A recipe may ask for longer; the Pet Sim
recipe asks for 180 because the game's own servers cache clan data for three minutes.

### 4.5 Multiple recipes

Each installed recipe runs its own loop and is started and stopped on its own. They share the host
connection, `AccountMap`, `NameClient` and the key store, and nothing else.

## §5 The builder

Lives in Ur Score's window as a **Build one** flow. Its audience is the one person in a clan who
builds a source for everyone, so it must be able to produce a multi-step recipe.

1. **Paste an address.** Placeholders prompt for a test value; `{userId}` offers a picker of your
   own RoRoRo accounts.
2. **See the response as a tree** of keys and sample values.
3. **Click the number.** The path is recorded. Inside an array, it asks whether each item is a
   player, then asks you to click the user id field (list form). A `{userId}` address produces the
   per-account form.
4. **Mark a changing segment.** Clicking a path segment and choosing "this changes" opens an earlier
   step: fetch it, click the value that supplies the name, name the variable. This is how the Pet
   Sim recipe is built.
5. **Add a key** if the source needs one: label, where to get one, header or query. You paste your
   own for the test.
6. **Name and details:** metric id suggested from the clicked field; credit line; poll interval;
   value label; optional headline values.
7. **Test before saving.** One run against your accounts, showing what each would send, narrated
   step by step.
8. **Save**, then **Export** a shareable file.

If a pasted address contains one of your saved key values, the builder offers to remove it from the
address and add that key to the step's `useKeys` instead (§7.4).

## §6 Import and the safety screen

### 6.1 What a recipe can never do

- Run code. It is data; Ur Score interprets requests and paths.
- Use anything but https, or anything but GET.
- Use your Roblox session. Ur Score has none.
- Learn more about your accounts than their Roblox user ids.
- Poll faster than the floor.
- Gain capabilities. Ur Score's consent from RoRoRo is the same two whatever it runs.
- **Decide what happens with the number** (§2).

### 6.2 The import screen

Shown before anything runs. Import or Cancel.

- Name, credit line, author labelled as unverified.
- **Every host this recipe contacts, and what goes to each:** your accounts' Roblox user ids, your
  saved key for that host, or an input value you entered.
- Poll interval, the suggested metric id (editable), and whether RoRoRo already has a rule for it.
- A note when a saved key will be reused for a host.

### 6.3 Updates

Importing a recipe with the same `name` and `author` as an installed one shows what changed. It asks
again if any host or anything sent changed. An identical file imports without asking. Name and
author are a convenience for recognizing an update, not proof of who made it, which is why a changed
host or send always asks again.

### 6.4 No signing

Signing would need 626 Labs to vouch for recipes about games. The import screen is the trust
mechanism instead.

### 6.5 Validation

A malformed file names its exact problem: "step 2 has no url", "unknown placeholder {clna}", "the
last step needs rows, userId and value, or perAccount and value".

## §7 Keys

### 7.1 Storage

Encrypted with Windows DPAPI for the current user, in `%LOCALAPPDATA%\626labs.ur-score\keys.dat`,
the same protection RoRoRo uses for its Discord and phone settings. Never in `settings.json`, a
recipe file, a saved response, diagnostics or a log.

### 7.2 Bound to a host

A key is stored against the host of the step that first used it and is only ever sent there. A
recipe that would send it to another host is refused at import, naming both hosts.

### 7.3 Entry

Masked with a Show toggle, Enter saves, a Remove button: the same pattern as RoRoRo's Alerts page.
The "get one here" link must be https and shows its host before opening, since a shared file could
point it at a phishing page.

### 7.4 Redaction and guards

- Every saved key value is redacted wherever it would otherwise appear: request addresses, error
  text, saved raw responses under `last-response`, the diagnostics text, and logs. Query-parameter
  keys make this the likeliest leak, because the key sits in the address.
- The builder offers to move a pasted key out of the address and into `useKeys`.
- Export refuses a recipe whose text contains any saved key value.

### 7.5 Failures

- Missing key: the recipe stops, naming the key and where to get one.
- Rejected key (401 or 403): the recipe stops until the key is changed, rather than retrying a bad
  key every poll. The message names the host.

## §8 The window

### 8.1 Layout

- **Recipe list** at the top: name, state, Start and Stop per recipe. With none installed: **Import a
  recipe** and **Build one**, and one line saying what a recipe is.
- **Detail for the selected recipe:**
  - **Headline:** up to two labelled display-only values.
  - **Leaderboard** for list recipes: rows ranked by value, highest first, your accounts highlighted, other
    members' names resolved when `resolveNames` is on.
  - **Your accounts:** loaded when the window opens. Send checkbox, account, position (list recipes
    only), value under `valueLabel`, rate per minute, last sent value, last sent at.
  - **Report policy:** which metric is sent for how many of your accounts, and which hosts this
    recipe contacts.
  - **Rule helper:** the user types kind, threshold and window. It shows the exact rule before
    writing it and backs up RoRoRo's rules file first. It no longer supplies a threshold.
  - **Diagnostics** and **Test now**, narrating that recipe's steps.
  - **Credit** from the recipe.

### 8.2 Searchable inputs

The list at `search.url` is fetched once and cached for 24 hours in
`%LOCALAPPDATA%\626labs.ur-score\search-cache\`. Typing filters it to the top matches. Picking a
value stores it with exact casing, which ends "clan not found" typos. A dropdown is not used: the
Pet Sim list has 169,238 entries.

### 8.3 Theme

The window reads RoRoRo's palette on connect through `GetTheme` and follows `SubscribeThemeChanged`
live, styling grids, buttons and panels from it. Follow Ur Task's implementation rather than writing
a second one.

## §9 What carries over

| Today | After |
|---|---|
| `JsonNav` | Kept. Becomes the engine's reader for every path. |
| `ReportPolicy` | Kept, still the single `ReportMetricAsync` call site. Allowed subjects and metric id per recipe. |
| `AccountMap`, `PointsRate`, `NameClient` | Kept. |
| `RulesFile`, `RuleInventory` | Kept. The rule helper takes user-typed threshold, kind and window. |
| `ScoreWatch` | Becomes the per-recipe runner. |
| `WatchState` | Generalized: value label, optional position, headline values. |
| `Settings` | Keeps `resolveNames`. Inputs, metric id overrides and Send exclusions move to per-recipe state. |
| `ClanClient` | Removed. Its request handling moves into the step runner. |
| `ClanParser`, `ClanStanding` | Removed. Their behaviour becomes the Pet Sim recipe plus the generic reader. |
| — | New: recipe model and validator, recipe store, key store, redactor, import review, search cache, builder, theme consumer. |

There is no migration. Ur Score has never been released, so there is no installed base.

## §10 Testing

**Unit:**
- Validator: every error in §6.5 names its problem; version refusal; one-form rule for the last step.
- Engine, with canned JSON: Pet Sim two-step reading; idle when `configName` is null; per-account
  followers; a miss names the keys present; an unreadable row costs only itself.
- Placeholders: url encoding versus path use; unknown placeholder refused.
- Transport table in §4.3, one test per row.
- Keys: host binding refuses a second host; missing key; rejected key stops polling.
- Redaction: a key never appears in a request log, error text, saved response or diagnostics.
- Import review: hosts and what is sent; update diff asks only when hosts or sends change.
- Search filter over a large list; exact casing stored.
- Theme mapping from a palette to the window's resources.

**Fences:**
- **No hostname literal in `src/`.** Replaces the old rule that `ClanClient.cs` is the only file
  naming the vendor. Recipe files and tests are exempt. One named exemption: `NameClient`'s
  `users.roblox.com`, which resolves usernames for every recipe's leaderboard. It is Ur Score's own
  feature, not a stat source, and it is disclosed and switchable through `resolveNames`.
- The existing report-policy fence stays: one call site of `ReportMetricAsync`.

**Live acceptance:**
Build the Pet Sim recipe with the builder against the live API, export it, remove it, import the
exported file, and watch it report for your own accounts through a rule you typed yourself, ending
in a real alert.

## §11 Out of scope

- Numbers on screen (OCR).
- Roblox data that needs signing in, such as badge lists.
- Counting items across pages.
- Anything but GET.
- Presets, and any 626-hosted catalog of recipes.
- Alerting on accounts other than your own.
- Keys inside a url path segment.

## §12 Risks

- **Rate limits** when a per-account recipe runs for many accounts. Mitigated by sequential requests,
  the floor, and 429 handling; a recipe author can raise `everySeconds`.
- **Shape churn** at a source. Mitigated by misses that name what was present, so the fix is a
  one-line recipe edit someone can share.
- **A hostile shared recipe.** Mitigated by §6: data only, GET only, host-bound keys, and a screen
  that names every host and everything sent before it runs.

## §13 Build order

Three parts, each shippable and testable on its own:

1. **Engine, format and import.** Recipe model, validator, step runner, reader, key store with host
   binding and redaction, import screen, per-recipe runner replacing `ScoreWatch`, the no-hostname
   fence. Recipes are written by hand at this stage. Exit: the Pet Sim recipe from §3.6, imported,
   reports for your own accounts.
2. **The window.** Recipe list, generalized detail, accounts on open, searchable inputs, rule helper
   with a user-typed threshold, RoRoRo theme.
3. **The builder.** Tree view, click to path, changing segments and earlier steps, keys, test before
   save, export. Exit: the §10 live acceptance.
