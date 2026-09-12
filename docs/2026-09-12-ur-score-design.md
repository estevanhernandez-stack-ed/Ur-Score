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
is the piece that fills the hole for one game. It reads clan battle point totals, matches the
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

2. **The endpoint is compiled in; the clan is configured.** A plugin is the one place a vendor's
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
    Source/ClanClient.cs   the two HTTP calls and their parsing
    Host/HostClient.cs     pipe connect, handshake, GetAccounts, ReportMetric
    UI/MainWindow.xaml     mapped accounts, last poll, last value, errors, start/stop
  tests/                   ClanClient parsing, mapping, cadence flooring
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

### Settings, and their defaults

`%LOCALAPPDATA%\626labs.ur-score\settings.json`:

| Key | Default | Notes |
| --- | --- | --- |
| `clanName` | *empty* | Required. Empty means idle, never a poll. |
| `metricId` | `clan.battle.points` | Opaque to the host; must match the `metricId` in RoRoRo's rules file or nothing can ever alert. |
| `pollSeconds` | `180` | Floored at 180. A smaller configured value is raised, and the window says so. |

The metric id is the one setting a user must copy into RoRoRo's `metric-rules.json` by hand, and a
mismatch there is silent on both sides — the plugin reports happily, the host stores history, and no
rule ever matches. The window shows the configured id prominently for exactly that reason.

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

## §6 Accounts that cannot be mapped

`SavedAccount.roblox_user_id` is documented as `0` when not yet resolved. Those accounts are
skipped and **listed by name in the window**, because a silently unwatched account is the failure
a user would never diagnose.

**Match on `roblox_user_id` only, never `display_name`.** The host masks display names when
streamer mode is on — which is exactly when someone is streaming a clan battle. A plugin matching
on names would work in every test and break at the only moment that matters.

## §7 What is out of scope

- Watching clan members who are not your accounts (§1.1).
- Any threshold, cooldown or notification logic. RoRoRo owns all of it.
- Posting anything anywhere. Ur Score has no webhook and no outbound destination but the host pipe.
- Any game action. The macro wall is absolute: this reads HTTP and writes to a named pipe.

## §8 Prerequisites outside this repo

1. **Publish `ROROROblox.PluginContract` 0.10.0.** One manual dispatch of `publish-nuget.yml` in
   the host repo. Deferred by decision §1.4 until Ur Score is built.
2. **Ship RoRoRo 1.28.0.0.** The released v1.27.0.0 predates `ReportMetric` — it was tagged
   2026-09-08 and the RPC merged after. Until a release carries it, `minHostVersion` has nothing
   true to point at and Ur Score runs only against a dev build.
3. **A catalog entry** in `ROROROblox/docs/store/plugins-catalog.json`, matching the four existing
   first-party plugins.
4. **An icon**, through the design skill.
