# RoRoRo Ur Score

> A [RoRoRo](https://github.com/estevanhernandez-stack-ed/ROROROblox) plugin that watches the
> numbers a recipe describes, shows you the board whether or not you ever set up an alert, and
> hands RoRoRo the stats you choose — one number per account per stat — so RoRoRo can decide
> whether that number is worth ringing your phone. Ur Score knows no game of its own: it reads
> whatever a recipe file describes.

**You can install this.** The latest release is **v0.3.1**, on the
[releases page](https://github.com/estevanhernandez-stack-ed/Ur-Score/releases).

You need **RoRoRo v1.28.0.0 or newer** — that is the release that added the `ReportMetric` call
this plugin depends on, and the manifest declares `minHostVersion: 1.28.0.0` on purpose. Against
anything older the install is refused: "This plugin requires RoRoRo 1.28.0.0 or newer" — and then
the version you are running. That is correct behaviour, not a bug to work around.

## What it does

RoRoRo shipped metric alerts with a gap in the middle on purpose: RoRoRo keeps history, applies
rules, and routes a breach to a toast, a Discord channel, or a phone — but nothing in RoRoRo
gathers a number, because gathering one means naming somebody's API, and RoRoRo names none. Ur
Score fills that gap without naming one either. A **recipe** — a `.recipe.json` file you import —
says which https address to read, how to find the rows in the answer, and what each number is
called. Ur Score runs it and knows nothing else: no host is written anywhere in its own code except
Roblox's username and picture services, and a test (`NoHostnameFenceTests`) keeps it that way.

**It's a scoreboard on its own, even if you never touch an alert.** Ur Score reads each source on
its recipe's own schedule — never faster than once a minute, whatever the recipe asks for — and
draws the answer as panels you arrange yourself: standing, a race between groups, your accounts by
a stat, a promotion check, an account card, past periods, records, the top of the period, a profile
stat, an accounts table, and the live leaderboard. The tabs across the top are boards; **Edit
board** and **+ Add panel** arrange them, and any panel pops out into a window of its own. Each of
your own accounts shows its Roblox avatar; no other player's picture is ever asked for or kept.

**Every good read is kept.** Your own accounts' numbers and each source's headline go into a score
book under `%LOCALAPPDATA%\626labs.ur-score\scorebook`. Nothing is thinned or deleted, and removing
a recipe keeps its book. That is also why the window opens on the last numbers it saw rather than
on empty panels: those panels are tagged `remembered`, and the state line says how old they are —
"The numbers on screen are the last ones Ur Score read, from 2 h ago."

**And it feeds RoRoRo's alert pipeline**, for the stats and the accounts you tick. It matches the
rows a recipe reads against the Roblox accounts you've saved in RoRoRo, and for each match hands
RoRoRo that account's raw value under the metric id you chose. RoRoRo decides whether that number,
or the rate it derives from it over time, is worth an alert. Ur Score sets no thresholds and cannot
tell whether anything it reported made your phone ring — that part is entirely RoRoRo's.

## What leaves your machine

Four destinations, and they're not the same boundary:

| Destination | What goes | What doesn't |
| --- | --- | --- |
| **RoRoRo**, over a local pipe on your own PC | Only your own saved accounts' values, only under a metric id you set to send, only if the number is a real finite value. This is the whole point of the plugin and it is checked at one gate in the code (`ReportPolicy`), not scattered around. | Anything about anyone else. Other rows' ids and values are read off the source, compared against your accounts, and dropped — never reported, never written to a file, never logged. |
| **Whatever hosts your recipes name** — always https, never anything Ur Score chose | What that recipe's own steps carry: the inputs you filled in, a key you saved for it if it declares one, and your own accounts' Roblox user ids when it reads one account at a time. The import screen lists every host and what each one receives, under **YOUR PC WILL CONTACT**, before the recipe is added. | Anything a step doesn't name. Ur Score adds nothing of its own to a recipe's requests but its User-Agent, `UrScore/<version> (RoRoRo plugin)`. |
| **Roblox's own public username lookup** (`users.roblox.com`) | Other members' Roblox user ids, so the live leaderboard can show names instead of a column of numbers. Those ids came from the recipe's source in the first place, and only names come back. | Your own accounts' ids — their names are already known locally, from RoRoRo, so they never need this call. |
| **Roblox's own public picture service** (`thumbnails.roblox.com`, and the picture host it names on `rbxcdn.com`) | Your own accounts' Roblox user ids, so each of your rows can show that account's avatar, and a recipe's icon id when it has one. The pictures are kept in `%LOCALAPPDATA%\626labs.ur-score\icon-cache` and asked for again after seven days. | Any other player's id. The leaderboard and the top of the period show other members by name only, and no picture of anyone else is ever asked for or kept. |

**You can turn the third one off.** Set `resolveNames` to `false` in settings (see *Settings
reference* below) and the leaderboard still shows positions and values, with everyone but your own
accounts shown as `Member 12345` instead of a name. How many requests the rest of it makes is up to
your recipes, not to Ur Score: the Setup page where you pick what a recipe reads ends with a line
like "Your PC asks example.com about 20 times an hour," counted from that recipe's steps, its
schedule and your account count.

Ur Score has no webhook of its own, posts nothing anywhere, and cannot type or click inside Roblox.
Its only outbound calls are the ones above — your recipes' own hosts, the username lookup, and
Roblox's picture service for your accounts' avatars and a recipe's icon — and its only inbound
connection is the local pipe to RoRoRo.

**Credit where it's due:** the data is somebody else's, and each recipe carries its own credit
sentence, which Ur Score prints along the bottom of the board for as long as that recipe's source
is switched on. Ur Score is not made by, endorsed by, or affiliated with Roblox or with any service
a recipe reads.

## What it needs

- **RoRoRo v1.28.0.0 or later**, running on the same PC.
- **At least one account saved in RoRoRo** with its Roblox user id resolved. RoRoRo fills this in
  automatically once an account has been used — a brand-new saved account that's never been
  launched is listed by name under **Setup › Your accounts** as "Not matched by RoRoRo yet", and Ur
  Score skips it until RoRoRo has a Roblox id for it.
- **The `host.metrics.report` and `host.queries.accounts` capabilities granted**, on the consent
  screen when you install the plugin. Without them Ur Score can neither find your accounts nor
  report anything for them — decline either and the window tells you plainly, rather than sitting
  quietly broken.
- **A recipe**, imported. Ur Score ships with none and reads nothing until you add one.
- **A RoRoRo alert rule for a metric id you're reporting**, if you actually want a phone alert out
  of this (see *Actually getting an alert* below). Without one, Ur Score still runs the board — it's
  the "phone rings" half specifically that needs it.
- **RoRoRo's own metric alerts toggle, turned on.** This is separate from having a rule, off by
  default, and Ur Score has no way to read it over the plugin contract — so it can't warn you.
  With it off, RoRoRo returns before it ever reads the rules file: a perfectly configured Ur Score
  can sit there reporting and your phone will still never ring. Turn **Metric alerts** on in
  RoRoRo's Settings › Alerts.

## Setup

1. **Install it.** In RoRoRo: **Plugins › Install from URL**, and paste the release URL you were
   given. Walk the consent screen — it lists `host.metrics.report` and `host.queries.accounts` —
   and click Install. Ur Score then runs as its own window, launched from RoRoRo's Plugins page
   (autostart is off by default, so it won't launch itself the next time you boot).
2. **Import a recipe, then pick what it reads.** Ur Score knows no game on its own — a recipe file
   tells it what to read. In Ur Score: **Setup › Recipes › Import recipe…**, pick the
   `.recipe.json` file, and walk the review screen, which lists every host that recipe will make
   your PC contact, and what each one receives, before anything is added. A recipe that needs an
   input of its own then gets a Setup page named by its own word for what it reads — **Clans**, for
   a clan recipe — and Setup opens straight on it the first time. Search for yours by name;
   **Make main** marks the one the board leads with, and **Watch it instead** follows one your
   accounts aren't in, which is read and shown but never sent. No file editing, and no restart.
3. **Choose your stats.** In **Setup › Stats**, tick **Show** to put a stat on your board and in
   your score book, and **Send** to also report it to RoRoRo. The **Name RoRoRo uses** box beside
   each stat is the metric id RoRoRo will see; a recipe suggests one, and you can change it. Press
   **Save stats**.
4. **Press "Start."** The board fills in within a few seconds of the first read — its panels, the
   state line above them, and the line along the top naming the period being read (or how often
   this reads) and when the next read is due. If nothing fills in, press **Test now**: it reads
   every source once, and **Setup › Diagnostics** then says per source what happened, when it last
   read, when it reads next, and which of your accounts a source couldn't read and why.
5. **Pick which accounts actually send.** The **Send** checkbox on each row in **Setup › Your
   accounts** controls whether that account's numbers go to RoRoRo, one checkbox per recipe. Every
   account starts on; untick one and it keeps being read, kept and shown, but stops sending — and
   the choice survives a restart.

### Actually getting an alert

The board and the reporting are independent of whether RoRoRo will ever alert on what's sent. For
that, RoRoRo needs a rule in its own `%LOCALAPPDATA%\ROROROblox\metric-rules.json` that matches a
metric id you send — without one, reports land, RoRoRo keeps history, and nothing ever fires, with
nothing anywhere looking broken.

You set that up in **Setup › Alerts**, which gives every stat you send a card, starting at "No
alerts yet." Each alert on a card is a sentence: "Alert me when an account's Diamonds gains fewer
than 100 a minute for 10 minutes." **+ Add an alert** asks which kind — **Stops climbing** (it
gains less than some amount a minute over 10, 15 or 30 minutes) or **Crosses a number** (it goes
above or below one) — and then hands you that sentence with the numbers as boxes to fill in.
**Turn on** writes it; an alert already there carries **Change** and **Remove**, and the card says
what each one did. You never see or edit JSON, and `AlertsPageFenceTests` keeps it that way.

A rule you wrote by hand is marked **yours** and Ur Score won't change it; one belonging to another
plugin is marked **another plugin's** and isn't yours to edit either. Ur Score backs the file up
before every write (`metric-rules.json.ur-score-backup`, right beside the original) and keeps every
rule it doesn't own exactly as it found it. RoRoRo re-reads that file whenever it judges, so a
change takes effect without restarting anything.

**A matching rule is not enough by itself.** RoRoRo's metric alerts toggle is a separate switch,
off by default, and Ur Score cannot see its state over the plugin contract — it can't tell you if
this is the reason nothing is ringing. The page says so under the cards once you have an alert:
"Next, in RoRoRo: Settings › Alerts › turn on Metric alerts and choose where they go (desktop,
Discord, phone)." Without that, a rule that matches exactly still returns before it's ever read.

### If you don't want other members' names

Set `"resolveNames": false` in `settings.json` (see *Settings reference* below). See *What leaves
your machine* for what that changes.

## Settings reference

Almost everything is set in the **Setup** window. What a particular source reads — its inputs,
which stats it shows and sends and under what metric id, and which of your accounts send — belongs
to that recipe and lives beside it, set under **Setup › Recipes**, **Stats**, its own group page
and **Your accounts**. Which sources exist and which are switched on lives in `sources.json`, and
your boards and panels in `boards.json`, both beside the settings file. Thresholds aren't here at
all: RoRoRo does the judging, and you write those in **Setup › Alerts**.

`%LOCALAPPDATA%\626labs.ur-score\settings.json` holds three keys, and only the first is worth
touching by hand:

| Key | Default | What it does |
| --- | --- | --- |
| `resolveNames` | `true` | Whether other members' Roblox ids are sent to Roblox to look up their usernames for the leaderboard. See *What leaves your machine*. There is no checkbox for this. |
| `startOnOpen` | `false` | Whether Ur Score does what pressing Start does as its window opens. Ticked as **Start reading as soon as Ur Score opens** under **Setup › Recipes**; no reason to edit it by hand. It takes effect the next time you open Ur Score, and reading still happens only while the window is open. |
| `activeRecipe` | *(none)* | Nothing reads it. It is left over from before recipes had sources of their own; which sources are on lives in `sources.json`. Leave it alone. |

Beside those, in the same folder: `recipes\` (the recipe files you imported and their state),
`scorebook\`, `accounts.json` (RoRoRo's last account list, so the window has names before RoRoRo
answers), `icon-cache\`, and `keys.dat` — any key a recipe asked you to save, encrypted for your
Windows account. Ur Score masks every saved key as `[key hidden]` in anything it shows, saves or
copies.

## What it doesn't do

- **Alert on anyone but your own accounts.** Even though the leaderboard shows every row a source
  returns, reporting is your accounts only. Everyone else's ids and values are shown on your screen
  and reported nowhere.
- **Set thresholds, cooldowns, or send notifications.** RoRoRo owns all of that.
- **Touch Roblox itself.** Ur Score reads https and writes to a local pipe. It cannot click, type,
  or otherwise act inside a Roblox client.
- **Run itself in the background.** RoRoRo's autostart for this plugin is off by default, and Ur
  Score reads only while its own window is open. Once you're set up you can tick **Start reading as
  soon as Ur Score opens** in **Setup › Recipes** so you don't have to press Start; it still won't
  watch anything you never opened it for.

## Troubleshooting

**The state line**, above the board, answers "what is it doing right now" in one sentence. Before
the first Start it reads "Not started."; after one, "Stopped."; with no source switched on,
"Running, with nothing to read yet."; and while all is well, "Reading 1 source." or "Reading 3
sources." If a source is in trouble, its name and its reason replace that — a source is named by
the input you typed for it, so the line reads "Nebula: Could not reach the data." And whenever any
panel is drawing numbers from the score book rather than from this session, the sentence about
their age is appended to whatever else the line says.

Underneath it, a second and quieter line carries whatever most needs saying: a change to your
boards that couldn't be written, RoRoRo being away, or how many of RoRoRo's 256 history slots your
ticks are using (accounts with Send on times stats with Send on — past 256, RoRoRo drops the newest
series without a word). When RoRoRo is away it reads "RoRoRo is not running. Still reading and
keeping your scores; nothing is being sent." Nothing is queued while it's away, so nothing is
replayed when it comes back; reporting resumes from the next live read.

**Setup › Diagnostics** is the per-source version, and it is where the rest of these sentences
show up — each source's state, its detail, when it last read and when it reads next, and which of
your accounts it couldn't read.

| You see | What it means |
| --- | --- |
| Waiting for a value to be set. | The recipe has an input nobody filled in. Set it on that recipe's page in Setup. Nothing is being read. |
| Could not reach the data. | A network or transport problem reaching the source. Waiting is the remedy; it isn't RoRoRo's fault. |
| Nothing matched what was entered. | The source says the name you typed matches nothing. Waiting won't fix it — check the spelling on that recipe's page. |
| Nothing to read right now. | Normal, not an error. The source itself says there is nothing on; for a recipe that reads a period, this is the usual state between periods. |
| The response was not a shape Ur Score understands. | The source answered with something the recipe doesn't describe. Use **Copy diagnostics** — the detail names the keys actually present. |
| None of your accounts are in what came back. | Rows came back, and none matched a saved RoRoRo account. Check the input you set, and that your accounts have resolved Roblox ids. |
| Reporting to RoRoRo. | Working. At least one account matched and its value was sent. |
| Reading. No stat is set to send to RoRoRo. | Also working. It's being read, kept and shown; nothing is ticked Send. |
| RoRoRo is not running. | Reading and keeping continue; every report is held, and none is queued. |
| RoRoRo refused the report. | A capability — named in the detail, `host.metrics.report` or `host.queries.accounts` — is not granted. RoRoRo's Plugins page has no per-capability re-grant and never re-prompts an existing consent record, so the only way back is **Remove** Ur Score there, then reinstall it, which puts the consent screen in front of you again. |
| The source asked Ur Score to slow down. | A rate limit. The next read tries again. |
| The source wants signing in, which recipes cannot do. | The address needs a session, and recipes never have one. Held until the recipe or its inputs change. |
| A key is needed. | The recipe declares a key that isn't saved, or is saved for a different host. |
| The source rejected the key. | The saved key was refused. Held until a key changes. |

A source can also read `Switched off.`, `Waiting for its first read.` or `Not started.`, which mean
what they say.

If none of that explains it, click **Copy diagnostics** on that page and paste the result into
wherever you're asking for help. It carries each recipe's schedule and which stats it shows and
sends under which metric ids, each source's inputs and last state, RoRoRo's version (or "not
connected"), counts from your score book, and the last 40 lines of narration. Every saved key is
masked; no score book content is included.

## Build from source

A clean clone builds with nothing special. You need the .NET 10 SDK — `global.json` pins 10.0.203
and rolls forward to the latest feature band — and nothing else:

```powershell
dotnet build Ur-Score.csproj -c Release
dotnet test tests/Ur-Score.Tests.csproj
pwsh ./build/build-plugin.ps1
```

`ROROROblox.PluginContract` 0.10.0 — the version that carries `ReportMetric` — is on nuget.org, so
`nuget.config` names nuget.org and nothing else. The test suite needs nothing beyond that package —
it reaches no live RoRoRo and no live source, standing in a fake for each — and is expected to pass
in full.

`build/build-plugin.ps1` checks for `icon.png` at the repo root before doing anything else and
stops with a message naming what's missing if it isn't there. It is there — made by
`build/make-icon.py`, along with the `icon.ico` the window and taskbar use — so the check passes.
Nobody should ever manufacture a placeholder to get past it: RoRoRo's own Store build already
refuses placeholder logos, and shipping a made-up icon is worse than a build that stops and says
why. The script publishes self-contained, so a clan member without the .NET 10 desktop runtime can
still run the plugin, and writes `artifacts/manifest.json`, `artifacts/manifest.sha256` and
`artifacts/plugin.zip` — the three files RoRoRo's installer expects at a release URL.

## License

MIT © 626 Labs LLC. The reference contract bindings (`ROROROblox.PluginContract`) ship under the
same license — see the parent RoRoRo repository.

---

**A 626 Labs product · *Imagine Something Else*.**
