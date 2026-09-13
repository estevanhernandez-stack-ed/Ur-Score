# Ur Score — design

> A 626 Labs plugin for RoRoRo. Polls clan battle scores, maps members to your own saved accounts,
> and hands each account's number to RoRoRo. RoRoRo decides whether that is worth an alert.
>
> Host-side spec this answers to:
> `ROROROblox/docs/superpowers/specs/2026-09-09-external-metric-alerts-design.md`.
> Author reference: `ROROROblox/docs/plugins/AUTHOR_GUIDE.md`, §"Report a metric".

## §0 What this is, in one paragraph

RoRoRo shipped metric alerts with a deliberate hole in the middle: the host keeps history, applies
rules, and routes a breach to a toast, a Discord channel or a phone — but nothing in the host
gathers a number, and nothing ever will, because gathering one means naming a game's API. Ur Score
is the piece that fills the hole for one game.

**And it is a clan battle dashboard in its own right** (§6.1), which is the half that earns its place
on someone's machine when they never configure an alert at all: the clan's rank and total, every
contributor ranked with your own accounts marked, and a rate per member computed from data the
reporter was already fetching and throwing away. It reads clan battle point totals, matches the
members it finds against the Roblox accounts you have saved in RoRoRo, and reports each match. It
sets no thresholds, sends no notifications, and cannot tell whether anything it reported made your
phone ring.

**The feature's user-facing name inside this plugin is "Score Watch."** RoRoRo's own toggle stays
*Metric alerts* and must: the host is generic by construction and names no game.

## §1 Decisions taken before any code

Each of these was a real fork, and each is recorded because the reasoning is not recoverable from
the result.

1. **Own accounts only.** Ur Score reports only clan members that map to accounts saved in this
   copy of RoRoRo. It does not watch the rest of the clan.

   Why: RoRoRo's alert cooldown is keyed `(account id, kind)` — the metric id is **not** part of
   the key, and an unrecognised subject id collapses to `Guid.Empty`, the shared global carrier.
   So reporting twenty unmapped clan members would put all twenty in one cooldown bucket: one
   alert per five minutes for the whole clan, with the other nineteen breaches silently swallowed.
   Watching your own alts gives each one its own bucket and its own resolved name, which is what
   the alert machinery was built for.

   If the whole-clan case is ever wanted, it is reachable without host changes: derive a stable
   synthetic Guid per member so each gets an independent bucket, and put the member's name in the
   metric id, which the alert body already displays. That is deliberately not in scope here.

2. **The endpoint is compiled in; the clan is configured.** *(Superseded 2026-09-13 by
   `2026-09-13-recipes-design.md`: sources now come from recipe files, and Ur Score's binary
   names no vendor. The reasoning below is kept as the record of what was believed.)* A plugin is the one place a vendor's
   hostname and field path are allowed to exist — the host binary ships neither, and
   `NoVendorNameFenceTests` fails the build if that ever changes. Making the endpoint
   user-configurable would buy generality nobody asked for and hand the user a way to get it
   wrong. What is genuinely per-user — the clan name — goes in settings.

3. **Autostart off.** A plugin that polls someone else's API should not run itself around the
   clock. The user starts Score Watch when a battle is on. The cost is real and is accepted: it
   will not alert you during a battle you forgot to start it for.

4. **The contract package is consumed from a local feed until Ur Score is finished.**
   `ROROROblox.PluginContract` 0.10.0 — the version that first carries `ReportMetric` — is built
   in the host repo but **not published to nuget.org**, which holds 0.9.0 as its newest. Rather
   than publish an immutable version before anything has been built against it, 0.10.0 is packed
   locally and consumed through a `nuget.config` source. The csproj references it *as a package*,
   at the same version it will have once published, so releasing means deleting a local source and
   nothing else.

## §2 What the source actually returns

Verified live 2026-09-09 against `ArcadeBattle2026`, recorded in the host-side spec §0 and repeated
here because this plugin is the only thing that depends on it.

Two calls, in order:

| Call | Gives |
| --- | --- |
| `GET /api/activeClanBattle` | `configName` — which battle is live right now |
| `GET /api/clan/{name}` | `Battles.<configName>.PointContributions[]`, entries of `{UserID, Points}` |

- **`UserID` is a Roblox user id**, which is exactly the join key `SavedAccount.roblox_user_id`
  carries. The mapping needs nothing else.
- **`Points` is cumulative** — points so far this battle, not a rate. That is precisely the shape
  `ReportMetric` asks for, and the reason rate derivation lives in the host.
- **No auth, no sampling.** The legacy `/api/clan/{name}` lookup returns every contributor;
  confirmed on a clan ranked ~1200 of 169,161. The `/v1/clans/*` aggregates sample the top 25/100
  and are the wrong family for this.

**The vendor's own documentation is stale, and this will cost someone an afternoon.** Their
`legacy/README.md` shows `Contribution.Battle[]`. The live response has no `Contribution` key at
all. Anyone coding from the published example gets an empty array and concludes the clan has no
data.

**Cache and cadence.** Clan endpoints send `max-age=60, s-maxage=180` — a three minute server
cache. Polling faster returns the same bytes and is simply rude, so the default interval is three
minutes and the configured value is floored there. These endpoints do **not** share the per-player
refresh quota that governs `/v1/players` and `/v1/account`.

## §3 Terms, and what they mean for us

The API's terms are non-commercial and personal use only; commercial use is prohibited without
prior written consent, and clause 3 requires attribution for **public display** of their data.

This is why Ur Score is a separate, user-installed plugin rather than anything 626 Labs ships
inside RoRoRo: it is a tool a player runs on their own machine against a public API for their own
alerts. 626 Labs distributes no data and calls no endpoint.

**One consequence worth stating plainly.** RoRoRo can route a metric breach to a Discord channel,
including a clan channel. A breach posted into a shared channel *is* public display of their data,
and clause 3 then applies. Routing Score Watch alerts to a toast or a phone raises no such
question. This is a note for whoever sets up the routing, not a thing this plugin can enforce.

An inquiry was sent to the vendor on 2026-09-11 asking whether a member-built plugin polling their
public API is acceptable. No reply at time of writing. The argument stands without one; a written
answer would simply be better than an argument.

## §4 Shape

```text
Ur-Score/
  Ur-Score.csproj          WinExe, net10.0-windows, WPF
  manifest.json            id 626labs.ur-score, capabilities, autostartDefault off
  nuget.config             local feed for the unpublished contract + nuget.org
  icon.png                 TODO — through the 626labs-design skill, not improvised
  build/build-plugin.ps1   publish → zip → sha256, the three fixed artifacts
  src/
    App.xaml(.cs)          single-instance guard, then the window
    Core/Settings.cs       %LOCALAPPDATA%\626labs.ur-score\settings.json
    Core/ScoreWatch.cs     the poll → map → report loop
    Core/WatchState.cs     the state enum every failure resolves to — no shared "error"
    Core/RulesFile.cs      read RoRoRo's metric-rules.json; merge one owned rule on click
    Core/ReportPolicy.cs   the only path to the host client — metric id, subject, finite value
    Source/ClanClient.cs   the two HTTP calls; the only file naming the game's vendor
    Source/ClanParser.cs   forgiving extraction; on a miss, the keys actually present
    Source/ClanStanding.cs clan Place and Points, and ranking the contributors (§6.1)
    Source/NameClient.cs   Roblox's batch id-to-username lookup, cached (§6.1)
    Host/HostClient.cs     pipe connect, handshake, GetAccounts, ReportMetric
    UI/MainWindow.xaml     the dashboard (§6.1), then the state sentence, policy and controls
  tests/                   parser shape-tolerance and miss-reporting, mapping,
                           cadence flooring, rules-file merge preserves other rules,
                           policy drops unlisted subjects and ids, and a fence that
                           the policy is the only route to the client
```

**Capabilities declared:** `host.metrics.report` and `host.queries.accounts`. Nothing else. Absence
from the manifest is treated as a decline, so this list is the whole surface.

## §5 The loop

1. Read settings. No clan name means idle with a message saying so, never a poll.
2. `GetAccounts()` — cache `roblox_user_id → account_id`. Refresh every cycle: an account added
   mid-session should start being watched without a restart.
3. `GET /api/activeClanBattle`. No live battle means sleep until the next tick; this is the normal
   state most of the time and must not read as an error.
4. `GET /api/clan/{name}`, take `Battles.<configName>.PointContributions[]`.
5. For each entry whose `UserID` matches a saved account, `ReportMetric` with the account's Guid as
   `subject_id`, the configured metric id, `Points` **raw and unmodified**, and
   `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`.
6. Sleep the interval. Repeat.

### When RoRoRo is not there

The pipe is the only thing Ur Score talks to, and it will not always answer: the user may start
Score Watch first, or quit RoRoRo mid-battle. Neither is an error worth stopping for.

- **Unreachable at start:** keep polling the clan API on the normal interval and keep the mapping
  cache warm, but hold reports. The window says RoRoRo is not running. The moment the pipe answers,
  resume reporting from the next poll — never replay a backlog, because a stale observation stamped
  minutes ago is exactly the input a Rate rule cannot use and a Level rule would judge on.
- **Lost mid-session:** same state, no restart, no dialog.
- **`PermissionDenied`:** the capability was revoked in RoRoRo. Stop reporting, say which capability
  in the window, and keep polling nothing — do not retry in a loop against a decision the user made.

### Never return empty quietly

The response shape is known from **one** live verification, and the vendor's own published example
was already wrong about it. So the shape this plugin expects will eventually be wrong too, and the
failure it must never produce is an empty list that reads like a quiet clan.

Every parse step names what it actually saw:

- **Key matching is forgiving.** Case-insensitive, and each field accepts the spellings that
  plausibly occur: `UserID` / `userId` / `userid`, `Points` / `points`. A shape change in casing
  alone should not take the feature down.
- **A miss reports the keys that were there.** If `Battles` is absent, the error names the keys the
  object *did* have. If the battle config key is absent, it lists the battle names present. If
  `PointContributions` is absent, it lists that battle's keys. The user — or whoever they send it
  to — can then see the new shape without reading the vendor's stale docs.
- **The raw response is kept.** The last response for each call goes to
  `%LOCALAPPDATA%\626labs.ur-score\last-response\`, overwritten each poll, never transmitted
  anywhere. A **Copy diagnostics** button puts the parse trail and those keys on the clipboard.
- **Zero contributors is a loud state**, distinct in the window from "clan not found", "no battle
  running", and "shape not understood". Four different causes that all look like silence otherwise.

### The states, and why they must be distinguishable

"Nothing happened" is the enemy. Each of these gets its own sentence in the window, never a shared
"something went wrong":

| State | What the user sees |
| --- | --- |
| No clan name set | Idle. Nothing is polled. |
| Clan name not found | Named, with the spelling as sent, so a typo is visible |
| Clan found, no battle running | Normal and frequent — explicitly *not* an error |
| Battle running, shape not understood | The keys actually present, plus Copy diagnostics |
| Contributions found, none match your accounts | How many contributors were seen, and which of your accounts were looked for |
| Some accounts have no Roblox user id yet | Those accounts named; they are skipped, and that is why |
| Matched and reporting | Per account: last value, last report time |
| RoRoRo not running | Polling continues, reporting held |
| Capability revoked | Which one, and that reporting has stopped |

### Test now

One button that runs a single full cycle and narrates every step: which clan was requested, whether
a battle is live, how many contributors came back, which of your accounts matched, what value was
reported for each. It is the difference between configuring this and guessing at it, and it is the
first thing anyone will reach for when it does not work.

### Closing the metric-id seam

The metric id has to match the `metricId` in RoRoRo's `metric-rules.json` or nothing can ever
alert, and a mismatch is silent on both sides: the plugin reports happily, the host stores history,
no rule matches, no alert fires, nothing looks broken anywhere. For an audience whose bar is "a
common Windows user", leaving that to hand-editing a JSON file in another app's data folder is not
a setup step, it is a trap.

So Ur Score **reads** RoRoRo's rules file — read-only, every cycle — and says plainly whether a rule
for its configured metric id exists. Three states: a matching rule exists; the file exists but has
no rule for this id; no rules file at all.

And it offers a one-click **Add this rule to RoRoRo**, under these conditions, all of which matter:

- It shows the exact JSON it will add, before adding it.
- It fires only on that explicit click. Never on startup, never as a side effect of anything else.
- It **merges**, never replaces: existing rules for other metric ids are preserved byte-for-byte.
- It backs the file up first, beside the original.
- It never edits an existing rule. A rule already present for this metric id is the user's, and the
  button says so instead of overwriting their threshold.

The host re-reads that file live, which the 2026-09-12 smoke run proved end to end, so the rule
takes effect without restarting anything.

### Settings, and their defaults

`%LOCALAPPDATA%\626labs.ur-score\settings.json`:

| Key | Default | Notes |
| --- | --- | --- |
| `clanName` | *empty* | Required. Empty means idle, never a poll. |
| `metricId` | `clan.battle.points` | Opaque to the host; must match the `metricId` in RoRoRo's rules file or nothing can ever alert. |
| `pollSeconds` | `180` | Floored at 180. A smaller configured value is raised, and the window says so. |

The metric id must match a rule in RoRoRo's `metric-rules.json`. That seam is closed by reading the
file and offering to write the rule — see *Closing the metric-id seam* above — rather than by asking
anyone to hand-edit JSON in another app's data folder.

### Rules this loop does not get to break

Every one of these is from the author guide, and every one of them fails silently:

- **Send the raw cumulative number, never a rate.** The host derives rates from consecutive
  samples. A pre-computed rate leaves it with no running total and Rate rules never see anything
  consistent.
- **Never special-case a counter reset.** When a new battle starts and points drop to near zero,
  send the low number as observed. The host treats a decrease as an unmeasurable window rather
  than computing a catastrophic negative rate. Smoothing it here would manufacture the exact false
  alert the host is careful to avoid.
- **`observed_at_unix_ms` is real UTC.** The failure mode is not a crash. A clock running *ahead*
  gets reports dropped with a log line; a clock running *behind* gets every report accepted and
  silently outside the window a Rate rule looks at, so Rate rules never fire while Level and Event
  rules keep working, forever, with nothing anywhere looking broken.
- **Never throttle on the user's behalf.** Cooldown, coalescing and the mute list live in the
  host's router and already serve four other alert kinds. Skipping reports to spare someone's
  phone only starves the host of the samples a rate needs.
- **Do throttle for the vendor.** Three minutes, floored, because that is their cache.

## §6 The report policy

The clan endpoint returns **every** contributor — around seventy-five entries of other people's
Roblox user ids and scores. "Own accounts only" (§1.1) decides what Ur Score does with that, but as
a decision buried in code it is invisible to the user and unverifiable by anyone. The report policy
turns it into a declared boundary that is stated in the window and enforced at one place.

**One chokepoint.** Every outbound report passes `ReportPolicy`, and nothing else in the plugin is
permitted to touch the host client. Three checks, and anything failing one is dropped and counted:

| Check | Rule | Why this one |
| --- | --- | --- |
| Metric id | exactly the configured id | One id, not a family. A shape change that starts yielding new field names cannot invent new metrics to send. |
| Subject | an account in the user's allow list | Per-account, individually toggleable. An alt you do not care about sends nothing at all. |
| Value | a finite number | `NaN` or an infinity would poison the host's history silently, and that arrives from a shape change far more plausibly than from malice. |

**What never leaves.** Other members' user ids and scores are read, compared against the user's
accounts, and dropped. They are never reported, never written to the rules file, and never logged.
The clan name and the raw response stay on the machine — the raw response only in
`last-response\`, for diagnostics, overwritten each poll.

**Stated where it can be read.** The window carries a **Report policy** section in plain words, not
a promise buried in this document: *"Ur Score sends points for 3 of your 8 accounts, as
`clan.battle.points`. Nothing else leaves this plugin."*

**Enforced, not asserted.** Two tests, and the second is the one that survives a refactor:

1. A report for an unlisted account, and a report with an unconfigured metric id, are both dropped
   at the gate and never reach the client.
2. A fence proving the gate is the **only** path to the host client — a direct client call from
   anywhere else fails the build. Same shape as the host's capability map, where absence is denial.

### The one thing it writes

Rule writing (see *Closing the metric-id seam*) sits under the same policy: exactly one rule row,
for exactly the configured metric id, and nothing else in that file is ever touched.

Each rule Ur Score writes carries an **`owner`** field holding this plugin's id. That is provenance,
not security — the file is meant to be hand-editable and always will be — and it exists because one
file has several writers: the user, this plugin, and any future one.

- A rule with **no owner is the user's**, and no plugin ever touches it.
- A rule owned by **someone else** is left alone and shown, not overwritten.
- Ur Score only ever reads, updates or removes rules **it owns**.

This costs nothing in the host: the rules parser deserializes each row with default
`System.Text.Json` behaviour, so an unknown `owner` property is already ignored by shipped code. No
host change is needed for any of it.

**Ur Score also keeps its own inventory** of what it wrote, in its own folder. That is what lets it
say "the rule I added has since been changed" rather than silently re-adding it or overwriting a
threshold the user tuned by hand. Drift is reported, never corrected.

## §6.1 What the plugin is worth on its own

Decided 2026-09-12, after the question "what does this look like if the user is not using the alert
features?" — for which the honest answer was **nothing**. Everything above describes a diagnostic
panel for the alert pipeline: which accounts matched, what was last sent, whether RoRoRo has a rule.
Useful when you are debugging why a phone did not ring, and useless otherwise.

The irony was in the documents already. The clan's rank appeared exactly once across the spec and
the plan, as filler in a test fixture demonstrating a key the parser steps past:

```json
{ "data": { "Battles": { "B": { "Points": 5, "Place": 9 } } } }
```

`Place` is the clan's standing out of 169,161 clans, and it was an example of something thrown away.

**Every poll already fetches what a dashboard needs and discards it.** One response carries the
clan's total `Points`, its `Place`, and all ~75 members' contributions. The parser keeps only the
handful matching the user's accounts. And because the poll cadence is three minutes, consecutive
samples are a real points-per-minute **per member** — which is the thing the clan currently works out
by hand and posts in Discord.

So Ur Score shows, whether or not a single alert is ever configured:

| | |
| --- | --- |
| **Your clan** | Place, total points, and which battle is live |
| **Your accounts** | Points, position within the clan, and rate per minute |
| **The clan** | Every contributor, ranked, with your own accounts marked |
| **Rate** | Computed locally from consecutive polls, labelled as a display figure |

### The rate shown here is not the rate RoRoRo judges

Worth stating because the two will occasionally disagree and that is not a bug. The plugin sends raw
cumulative points and RoRoRo derives its own rate over the window the user's rule names. The figure
in this window is computed from the plugin's own last two samples, for reading. A ten-minute rule and
a three-minute display sample answer different questions, and the window says which it is showing.

### Names come from Roblox, in one call, cached

A leaderboard of user ids is unreadable, and nobody recognises their own clanmates in it. So ids are
resolved to usernames through Roblox's own public batch endpoint, **verified live 2026-09-12**:

```text
POST https://users.roblox.com/v1/users
     {"userIds":[1,156,261],"excludeBannedUsers":false}
  -> {"data":[{"hasVerifiedBadge":true,"id":1,"name":"Roblox","displayName":"Roblox"}, ...]}
```

`name` is the username and `displayName` the display name; both come back. One request covers a clan,
since the endpoint takes up to 100 ids and a clan battle returns around 75 contributors.

**Cached hard, because usernames barely change.** Resolved once and kept; only ids never seen before
are looked up. Re-resolving 75 names every three minutes would be twenty calls an hour against
Roblox for data that changes about never, and this plugin's whole posture toward other people's
services is to ask for as little as it can.

**This is a second endpoint in a plugin that had one, and it lives in its own file.** `ClanClient`
remains the only file that names the game's vendor; Roblox is the platform RoRoRo is built on and
the host already calls the same domain. Keeping them apart keeps that statement precise.

### How this squares with the report policy

§6 says other members' ids and scores are "read, compared, and dropped: never reported, never
written, never logged." A leaderboard appears to contradict that, and the distinction has to be
explicit or the next reader will think one of the two is wrong. There are three different boundaries
here and only one of them is the report policy's:

1. **What reaches RoRoRo** — and therefore the user's toast, Discord channel or phone. Unchanged:
   only the user's own accounts, only the configured metric, only a finite value. The report policy
   governs exactly this and is not relaxed by anything in this section.
2. **What is shown on the user's own screen** — the leaderboard. Nothing leaves the machine. This is
   not an egress at all, and the policy's wording was about egress.
3. **What is sent to Roblox to resolve a name** — other members' user ids. This *is* an outbound
   call carrying other people's identifiers, so it gets said plainly rather than hidden inside "we
   show names now": ids Roblox issued go to Roblox to retrieve names Roblox publishes. Nothing about
   a clan member's participation is disclosed to anyone who did not already have it, and the ids came
   from a public endpoint in the first place.

Boundary 3 is still a choice a user might not want made for them, so `resolveNames` is a setting.
Turned off, the leaderboard shows positions and points with only the user's own accounts named — the
plugin then makes exactly two outbound calls per poll (`activeClanBattle` and `clan/{name}`, both to
the game's own API) and none about anybody else. **Corrected 2026-09-12 (F6):** this section
previously said "one outbound call" — wrong in either state of the setting, since the two calls to
the game's API happen regardless of it. Turned *on*, that becomes three: the same two, plus one
batched call to Roblox for names.

### Attribution

The vendor's terms require attribution for public display of their data (§3). A window on one
person's machine is not public display, so this does not trigger the clause — but the window credits
the source anyway, because the cost is one line of text and the alternative is a tool that presents
someone else's work as though it had gathered it.

## §7 Accounts that cannot be mapped

`SavedAccount.roblox_user_id` is documented as `0` when not yet resolved. Those accounts are
skipped and **listed by name in the window**, because a silently unwatched account is the failure
a user would never diagnose.

**Match on `roblox_user_id` only, never `display_name`.** The host masks display names when
streamer mode is on — which is exactly when someone is streaming a clan battle. A plugin matching
on names would work in every test and break at the only moment that matters.

## §8 What is out of scope

- **REPORTING** on clan members who are not your accounts (§1.1). Since §6.1 they are *shown* — a
  leaderboard of your own three accounts would be a strange thing to look at — but the report
  policy is unchanged and nothing about them reaches RoRoRo, a Discord channel or a phone.
- Any threshold, cooldown or notification logic. RoRoRo owns all of it.
- Posting anything anywhere. Ur Score has no webhook, and its only outbound calls are the game's
  API and Roblox's name lookup (§6.1), neither of which carries anything the user typed.
- Any game action. The macro wall is absolute: this reads HTTP and writes to a named pipe.
- Alerting on other members. Even with the whole clan on screen, the cooldown is keyed
  `(account id, kind)` and unmapped subjects collapse to one bucket (§1.1) — so watching the clan
  for alerts remains the thing that needs host work, and seeing the clan does not.

## §9 Prerequisites outside this repo

1. **Publish `ROROROblox.PluginContract` 0.10.0.** One manual dispatch of `publish-nuget.yml` in
   the host repo. Deferred by decision §1.4 until Ur Score is built.
2. **Ship RoRoRo 1.28.0.0.** The released v1.27.0.0 predates `ReportMetric` — it was tagged
   2026-09-08 and the RPC merged after. Until a release carries it, `minHostVersion` has nothing
   true to point at and Ur Score runs only against a dev build.
3. **A catalog entry** in `ROROROblox/docs/store/plugins-catalog.json`, matching the four existing
   first-party plugins.
4. **An icon**, through the design skill.
