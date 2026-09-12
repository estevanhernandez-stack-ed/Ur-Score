# RoRoRo Ur Score

> A [RoRoRo](https://github.com/estevanhernandez-stack-ed/ROROROblox) plugin that watches Pet
> Simulator 99 clan battle scores, shows you the clan's leaderboard whether or not you ever set up
> an alert, and hands RoRoRo one number per account so RoRoRo can decide whether that number is
> worth ringing your phone.

**You cannot install this yet.** Two things have to happen first, and neither is in your control
or this repo's:

1. **RoRoRo needs to ship v1.28.0.0 or later.** The current released RoRoRo is **v1.27.0.0**,
   which predates the `ReportMetric` call this plugin depends on. This plugin's manifest declares
   `minHostVersion: 1.28.0.0` on purpose — installing it against 1.27 or older gets refused with
   "This plugin requires RoRoRo 1.28.0.0 or newer. You're running 1.27.0.0. Update RoRoRo and try
   again." That is correct behavior, not a bug to work around.
2. **A packaged release has to exist.** As of this writing, none does — see *Build from source*
   below for what that build needs and where it currently stops.

If you found this repo before either of those landed, you're early. Nothing below is wrong, it
just isn't runnable yet.

## What it does

RoRoRo shipped metric alerts with a gap in the middle on purpose: RoRoRo keeps history, applies
rules, and routes a breach to a toast, a Discord channel, or a phone — but nothing in RoRoRo
gathers a number, because gathering one means naming a specific game's API, and RoRoRo names none.
Ur Score is the piece that fills that gap for one game: Pet Simulator 99 clan battles.

**It's a clan dashboard on its own, even if you never touch an alert.** Every three minutes it
reads the clan's current battle: the clan's overall place and total points, every contributor
ranked with your own accounts marked, and — because it's already fetching two consecutive samples
— a live points-per-minute figure per account. That's the same math your clan is probably already
doing by hand and pasting into Discord.

**And it feeds RoRoRo's alert pipeline**, for the accounts you tell it to watch. It maps the clan
members it finds against the Roblox accounts you've saved in RoRoRo, and for each match, hands
RoRoRo the account's raw cumulative points. RoRoRo decides whether that number, or the rate it
derives from it over time, is worth an alert. Ur Score sets no thresholds and cannot tell whether
anything it reported made your phone ring — that part is entirely RoRoRo's.

## What leaves your machine

Three separate destinations, and they're not the same boundary:

| Destination | What goes | What doesn't |
| --- | --- | --- |
| **RoRoRo**, over a local pipe on your own PC | Only your own saved accounts' points, only under the metric id you configured, only if the number is a real finite value. This is the whole point of the plugin and it is checked at one gate in the code (`ReportPolicy`), not scattered around. | Anything about other clan members. Their ids and points are read off the clan leaderboard, compared against your accounts, and dropped — never reported, never written to a file, never logged. |
| **Pet Simulator 99's own public clan API** (`ps99.biggamesapi.io`, run by Big Games) | The clan name you configure, so it can hand back that clan's current battle standings. No login, no account identifiers of yours. | Nothing about your RoRoRo accounts. This call doesn't know they exist. |
| **Roblox's own public username-lookup API** (`users.roblox.com`) | Other clan members' Roblox user ids, so the leaderboard can show names instead of a column of numbers. These ids came from the public clan API in the first place, and only names come back — nothing is disclosed about anyone that a public API didn't already show. | Your own accounts' ids — their names are already known locally, from RoRoRo, so they never need this call. |

**You can turn the third one off.** Set `resolveNames` to `false` in settings (see *Configure it*
below) and the leaderboard still shows positions and points, with everyone but your own accounts
shown as `Member <id>` instead of a name. With it off, Ur Score makes exactly one outbound call
per poll — to the game's own API — and none to Roblox for anyone else's identity.

Ur Score has no webhook of its own, posts nothing anywhere, and cannot type or click inside
Roblox. Its only outbound calls are the two named above, and its only inbound connection is the
local pipe to RoRoRo.

**Credit where it's due:** clan battle data comes from Big Games' public Pet Simulator 99 API. Ur
Score is not made by, endorsed by, or affiliated with Big Games or Roblox — the window says this
too, every time it's open.

## What it needs

- **RoRoRo v1.28.0.0 or later**, running on the same PC. (See the callout at the top — this
  doesn't exist yet.)
- **At least one account saved in RoRoRo** with its Roblox user id resolved. RoRoRo fills this in
  automatically once an account has been used — a brand-new saved account that's never been
  launched shows up in Ur Score's window as unresolved and named, and Ur Score skips it until
  RoRoRo has a Roblox id for it.
- **The `host.metrics.report` and `host.queries.accounts` capabilities granted**, on the consent
  screen when you install the plugin. Without them Ur Score can neither find your accounts nor
  report anything for them — decline either and the window tells you plainly, rather than sitting
  quietly broken.
- **A RoRoRo alert rule for the metric id you're reporting**, if you actually want a phone alert
  out of this (see *Actually getting an alert* below). Without one, Ur Score still runs the
  dashboard — it's the "phone rings" half specifically that needs it.
- **RoRoRo's own metric alerts toggle, turned on.** This is separate from having a rule, off by
  default, and Ur Score has no way to check it over the plugin contract — so it can't warn you.
  With it off, RoRoRo returns before it ever reads the rules file: a perfectly configured Ur Score
  can show "Reporting" next to "Ready: RoRoRo has a rule" and your phone will still never ring.
  Turn it on in RoRoRo's Settings.

## Setup

Once RoRoRo 1.28+ is out and a release exists:

1. **Install it.** In RoRoRo: **Plugins → Install**, and paste the release URL you were given.
   Walk the consent screen — it lists `host.metrics.report` and `host.queries.accounts` — and
   click Install. Ur Score starts as its own window (autostart is off by default, so it won't
   launch itself the next time you boot; start it yourself when a battle's on).
2. **Set your clan.** There's no text box for this yet — it lives in a settings file. Press
   `Win + R`, type `%LOCALAPPDATA%\626labs.ur-score`, and press Enter. That opens the folder.
   If `settings.json` isn't there yet, start Ur Score once and it'll create it. Open it in
   Notepad and set `"clanName"` to your clan's exact name, the way it appears in-game — a typo
   here shows up in the window as "clan not found," naming exactly what was searched for, so you
   can tell right away if you got it wrong. Save the file, then (re)start Ur Score.
3. **Press "Start Score Watch."** If a battle is live, the dashboard fills in within a few
   seconds — your clan's place and points at the top, the leaderboard below it, and your own
   accounts' rows beneath that. If nothing fills in, press **Test now**: it runs one cycle and
   narrates exactly what happened (which clan was asked for, whether a battle is live, how many
   contributors came back, which of your accounts matched), which is the fastest way to see what
   went wrong.
4. **Pick which accounts actually send.** The **Send** checkbox on each row in "Your accounts"
   controls whether that account's number goes to RoRoRo. Every account starts on; untick one and
   it keeps climbing on the dashboard (you can still see it) but stops sending — the choice
   survives a restart.

### Actually getting an alert

The dashboard and the reporting are independent of whether RoRoRo will ever alert on what's sent.
For that, RoRoRo needs a rule in its own `metric-rules.json` that matches Ur Score's metric id
(`clan.battle.points` by default) — without one, reports land, RoRoRo keeps history, and nothing
ever fires, with nothing anywhere looking broken.

Ur Score reads that file every cycle and tells you plainly whether a matching rule exists, in the
window's **Report policy** section. If it doesn't, click **Add this rule to RoRoRo** — it shows you
the exact JSON it's about to add before it adds it, backs up the file first
(`metric-rules.json.ur-score-backup`, right beside the original), and only ever adds — it never
edits a rule that's already there, whether you wrote it or another plugin did. RoRoRo re-reads
that file live, so the rule takes effect without restarting anything.

**A matching rule is not enough by itself.** RoRoRo's metric alerts toggle is a separate switch,
off by default, and Ur Score cannot see its state over the plugin contract — it can't tell you if
this is the reason nothing is ringing. Turn **Metric alerts** on in RoRoRo's own Settings; without
that, a rule that matches exactly still returns before it's ever read.

### If you don't want other members' names

Set `"resolveNames": false` in `settings.json` (see step 2 above). See *What leaves your machine*
for what that changes.

## Settings reference

`%LOCALAPPDATA%\626labs.ur-score\settings.json`, hand-edited (there is currently no settings UI):

| Key | Default | What it does |
| --- | --- | --- |
| `clanName` | *(empty)* | Required. Empty means idle — nothing is polled until this is set. |
| `metricId` | `clan.battle.points` | The id reported to RoRoRo. Must match the `metricId` in RoRoRo's rules file, or nothing can ever alert (see *Actually getting an alert*). Most people should leave this alone. |
| `pollSeconds` | `180` | How often the clan is polled. Floored at 180 — the game's own servers cache clan data for three minutes, so anything faster just re-fetches the same bytes. A smaller value in the file is raised automatically. |
| `resolveNames` | `true` | Whether other members' Roblox ids are sent to Roblox to look up their usernames for the leaderboard. See *What leaves your machine*. |
| `excludedAccountIds` | *(none)* | Managed by the **Send** checkboxes in the window — you shouldn't need to hand-edit this. |

## What it doesn't do

- **Alert on anyone but your own accounts.** Even though the leaderboard shows the whole clan,
  reporting is your accounts only. The other ~75 contributors' ids and points are shown on your
  screen and reported nowhere.
- **Set thresholds, cooldowns, or send notifications.** RoRoRo owns all of that.
- **Touch Roblox itself.** Ur Score reads HTTP and writes to a local pipe. It cannot click, type,
  or otherwise act inside a Roblox client.
- **Run itself in the background.** Autostart defaults to off. You start Score Watch when a
  battle's on; it won't watch a battle you forgot to start it for.

## Troubleshooting

The window's state line is meant to answer "what is it doing right now" without guessing — these
are the states it can be in and what each one means:

| You see | What it means |
| --- | --- |
| Idle — no clan name set | `clanName` is empty in settings.json. Nothing is being polled. |
| Could not reach the clan data | A network or transport problem reaching the game's API. Not RoRoRo's fault. |
| No clan battle running | Normal, and the common state between battles. Not an error. |
| The response was not a shape Ur Score understands | The game's API returned something the parser didn't expect. Use **Copy diagnostics** and check `%LOCALAPPDATA%\626labs.ur-score\last-response\` — the message names the keys actually present. |
| None of your accounts are in this battle's contributions | Contributors came back, but none matched a saved RoRoRo account. Check the clan name and that your accounts have resolved Roblox ids. |
| Reporting to RoRoRo | Working. At least one account matched and was sent. |
| RoRoRo is not running | Ur Score keeps polling the clan and keeps its account mapping warm, but holds every report — nothing is queued to send once RoRoRo comes back; it resumes from the next live poll. |
| RoRoRo refused the report | A capability (named in the message — `host.metrics.report` or `host.queries.accounts`) is not granted. RoRoRo's Plugins page has no per-capability re-grant and never re-prompts an existing consent record — the only way back is **Remove** Ur Score there, then reinstall it, which puts the consent screen in front of you again. |

If none of that explains it, click **Copy diagnostics** and paste the result into wherever you're
asking for help — it carries the clan name, metric id, poll interval, RoRoRo's version (or "not
connected"), and the last several cycles' worth of narration. It carries no credential of any
kind; Ur Score never holds one to begin with.

## Build from source

You need two things a clean clone doesn't hand you, and both are real:

1. **`ROROROblox.PluginContract` 0.10.0 is not on nuget.org yet** (nuget.org's newest is 0.9.0;
   0.10.0 is the version that adds `ReportMetric`). `nuget.config` points at a local feed
   (`../.local-nuget`) for it instead. Either:
   - get a copy of `ROROROblox.PluginContract.0.10.0.nupkg` (and `.snupkg`) into a
     `.local-nuget` folder one level above wherever you clone this repo, or
   - pack it yourself from a checkout of the RoRoRo host repo:
     ```powershell
     dotnet pack src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj -c Release -o ../.local-nuget
     ```
     (run from the ROROROblox repo root, with `../.local-nuget` landing next to it — adjust the
     path to match where you cloned Ur Score relative to it).

   Once 0.10.0 is published to nuget.org, delete the `local-contract` source from `nuget.config`
   and nothing else changes — the csproj already references it as a normal package at that
   version.

2. **`icon.png` does not exist yet.** It's owed through the `626labs-design` skill and hasn't been
   made. `build/build-plugin.ps1` checks for it before doing anything else and stops with a clear
   message naming what's missing if it isn't there — on purpose. Nobody should manufacture a
   placeholder to get past this: RoRoRo's own Store build already refuses placeholder logos, and
   shipping a made-up icon is worse than a build that stops and says why.

With those in place:

```powershell
dotnet build Ur-Score.csproj -c Release
dotnet test tests/Ur-Score.Tests.csproj
pwsh ./build/build-plugin.ps1
```

The test suite needs neither of the two prerequisites above beyond the contract package (it
doesn't touch a live RoRoRo, a live clan API, or the icon) and is expected to pass in full. The
build script currently stops at the icon check — that's correct, not a bug, until the icon lands.

## License

MIT © 626 Labs LLC. The reference contract bindings (`ROROROblox.PluginContract`) ship under the
same license — see the parent RoRoRo repository.

---

**A 626 Labs product · *Imagine Something Else*.**
