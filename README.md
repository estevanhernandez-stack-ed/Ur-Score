# RoRoRo Ur Score

> A [RoRoRo](https://github.com/estevanhernandez-stack-ed/ROROROblox) plugin that knows a game's
> modes, shows you the board whether or not you ever set up an alert, and hands RoRoRo the stats
> you choose — one number per account per stat — so RoRoRo can decide whether that number is worth
> ringing your phone. Pet Sim 99's two modes, Battle and Profile, are built in.

**You can install this.** The newest release is always on the
[releases page](https://github.com/estevanhernandez-stack-ed/Ur-Score/releases). In RoRoRo, use
**Plugins → Install from URL** with
`https://github.com/estevanhernandez-stack-ed/Ur-Score/releases/latest/download/` — that address
always points at the newest release, so it never goes stale.

You need **RoRoRo v1.28.0.0 or newer** — that is the release that added the `ReportMetric` call
this plugin depends on, and the manifest declares `minHostVersion: 1.28.0.0` on purpose. Against
anything older the install is refused: "This plugin requires RoRoRo 1.28.0.0 or newer" — and then
the version you are running. That is correct behaviour, not a bug to work around.

## What it does

RoRoRo shipped metric alerts with a gap in the middle on purpose: RoRoRo keeps history, applies
rules, and routes a breach to a toast, a Discord channel, or a phone — but nothing in RoRoRo
gathers a number, because gathering one means naming somebody's API, and RoRoRo names none. Ur
Score fills that gap. It knows a game as a list of **modes**, and a mode is one thing worth
watching: for Pet Sim 99, **Battle** is your clan's battle (place, race, pace, the top of the
battle) and **Profile** is each of your accounts' rank, diamonds, hatches and playtime. Under each
mode sit small reader files that say which https address to read, how to find the rows in the
answer, and what each number is called. They ship inside the app, so a mode is as current as the
Ur Score you run, and there is nothing to import. Every host a mode reads is written in those files
and shown on the game page, not in Ur Score's code: apart from Roblox's username and picture
services, no host appears in the code, and a test (`NoHostnameFenceTests`) keeps it that way.

**It's a scoreboard on its own, even if you never touch an alert.** Ur Score reads each mode on its
own schedule — never faster than once a minute — and draws the answer as panels you arrange
yourself: standing, a race between groups, your accounts by a stat, a promotion check, an account
card, past periods, records, the top of the period, a profile stat, an accounts table, and the live
leaderboard. The tabs across the top are boards; **Arrange** and **+ Add panel** arrange them, and
any panel pops out into a window of its own. Each of your own accounts shows its Roblox avatar; no
other player's picture is ever asked for or kept. When Battle reads a clan's icon, that clan's
standing panel shows it, and your main clan's icon is the window and taskbar icon. After its first
read, it's there as soon as Ur Score opens.

**Every good read is kept.** Your own accounts' numbers and each source's headline go into a score
book under `%LOCALAPPDATA%\626labs.ur-score\scorebook`. Nothing is thinned or deleted, and turning a
mode off keeps its book. That is also why the window opens on the last numbers it saw rather than
on empty panels: those panels are tagged `remembered`, and the state line says how old they are —
"The numbers on screen are the last ones Ur Score read, from 2 h ago."

**And it feeds RoRoRo's alert pipeline**, for the stats and the accounts you tick. It matches the
rows a mode reads against the Roblox accounts you've saved in RoRoRo, and for each match hands
RoRoRo that account's raw value under the metric id you chose. RoRoRo decides whether that number,
or the rate it derives from it over time, is worth an alert. Ur Score sets no thresholds and cannot
tell whether anything it reported made your phone ring — that part is entirely RoRoRo's.

## The game page

**Setup › Pet Sim 99** is where a game lives. A switch at the top turns the whole game on or off,
and each mode below it has its own switch, a line saying what it is, and a line saying exactly
what it contacts and how often ("Reads ps99.biggamesapi.io every 3 min"). Battle also asks the one
thing it can't know, which clan is yours: search for it by name, **Make main** marks the one the
board leads with, and **Watch it instead** follows one your accounts aren't in, which is read and
shown but never sent. Profile asks nothing; it reads the accounts you saved in RoRoRo, and it only
has numbers for an account that is linked on db.biggames.io with its Profile view public. An
account that isn't linked reads as empty, and Ur Score shows dashes and says why beside them.

**A mode that is off reads nothing.** No request leaves your PC for it, nothing is recorded or
sent, and its starter board says "Battle is off." with a **Turn on** button. Your choices inside it
(which clans, which stats are ticked) are kept while it's off and are there when you switch it back
on. Both modes are on in a fresh install.

**A new install walks you to the one question.** The first time Setup opens with Battle on and no
clan picked, it opens on the game page with the clan search ready. There is no file to find.

## What leaves your machine

Each mode has its own boundary, and so does the pipe to RoRoRo:

| Destination | What goes | What doesn't |
| --- | --- | --- |
| **Battle**: `ps99.biggamesapi.io` every 3 minutes, plus `thumbnails.roblox.com` and the picture host it names on `rbxcdn.com` for clan icons | The clan name you searched for, as part of a request for that clan's battle, and a request for the clans list and the current battle. For a clan's icon, the icon id the answer names. Icons are kept in `%LOCALAPPDATA%\626labs.ur-score\icon-cache` and asked for again after seven days. | Your accounts' ids: Battle reads whole clans and compares them with your accounts on your PC. |
| **Profile**: `ps99.biggamesapi.io` every 30 minutes | Your own accounts' Roblox user ids, one account at a time, since a profile is looked up by id. | Anyone else's. |
| **RoRoRo**, over a local pipe on your own PC | Only your own saved accounts' values, only under a metric id you set to send, only if the number is a real finite value. This is the whole point of the plugin and it is checked at one gate in the code (`ReportPolicy`), not scattered around. | Anything about anyone else. Other rows' ids and values are read off the source, compared against your accounts, and dropped — never reported, never written to a file, never logged. |
| **Roblox's own public username lookup** (`users.roblox.com`) | Other members' Roblox user ids, so the live leaderboard can show names instead of a column of numbers. Those ids came from the source in the first place, and only names come back. | Your own accounts' ids — their names are already known locally, from RoRoRo, so they never need this call. |
| **Roblox's own public picture service** (`thumbnails.roblox.com`, and the picture host it names on `rbxcdn.com`) | Your own accounts' Roblox user ids, so each of your rows can show that account's avatar. The pictures are kept in the same `icon-cache` and asked for again after seven days. | Any other player's id. The leaderboard and the top of the period show other members by name only, and no picture of anyone else is ever asked for or kept. |

A mode that is switched off contacts none of the first two. The game page states each mode's hosts
and cadence on the mode itself, worked out from the same reader files, so the page and this table
can't drift apart quietly.

**You can turn the username lookup off.** Set `resolveNames` to `false` in settings (see *Settings
reference* below) and the leaderboard still shows positions and values, with everyone but your own
accounts shown as `Member 12345` instead of a name. How many requests Battle makes is also on the
game page: the line under the clan search ends with something like "Your PC asks
ps99.biggamesapi.io about 20 times an hour," counted from the schedule and your clans.

Ur Score has no webhook of its own, posts nothing anywhere, and cannot type or click inside Roblox.
Its only outbound calls are the ones above, and its only inbound connection is the local pipe to
RoRoRo.

**Credit where it's due:** the data is Big Games' public Pet Simulator 99 API, and each reader
carries its own credit sentence, which Ur Score prints along the bottom of the board for as long as
that mode is on. Ur Score is not made by, endorsed by, or affiliated with Roblox, Big Games or any
service a mode reads.

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
- **A RoRoRo alert rule for a metric id you're reporting**, if you actually want a phone alert out
  of this (see *Actually getting an alert* below). Without one, Ur Score still runs the board — it's
  the "phone rings" half specifically that needs it.
- **RoRoRo's own metric alerts toggle, turned on.** This is separate from having a rule, off by
  default, and Ur Score has no way to read it over the plugin contract — so it can't warn you.
  With it off, RoRoRo returns before it ever reads the rules file: a perfectly configured Ur Score
  can sit there reporting and your phone will still never ring. Turn **Metric alerts** on in
  RoRoRo's Settings › Alerts.

## Install by hand

Every RoRoRo plugin installs the same way by hand, with no marketplace needed. Each release on the
[releases page](https://github.com/estevanhernandez-stack-ed/Ur-Score/releases/latest) carries four files:
`plugin.zip`, `manifest.json`, `manifest.sha256` and `install.ps1`.

**With the script:** download `plugin.zip`, `manifest.sha256` and `install.ps1` into one folder, quit RoRoRo
(right-click its tray icon › Quit) and Ur Score, then right-click `install.ps1` › **Run with PowerShell**. It
checks the zip against `manifest.sha256`, moves any existing install aside to `%TEMP%` (named for its
version), and installs into `%LOCALAPPDATA%\ROROROblox\plugins\626labs.ur-score\`. It never starts anything.
If Windows won't run scripts: `powershell -ExecutionPolicy Bypass -File install.ps1`.

**Fully by hand:** quit RoRoRo and Ur Score, right-click `plugin.zip` › Properties › tick **Unblock** › OK, then
extract it into `%LOCALAPPDATA%\ROROROblox\plugins\626labs.ur-score\` (the files sit at the zip's root; replace
what is there).

Either way, start RoRoRo and launch Ur Score from its Plugins page: the first launch asks for its permissions,
exactly as a marketplace install does. Your data lives elsewhere (`%LOCALAPPDATA%\626labs.ur-score\`), so
reinstalling never touches your clans, boards, choices or score book.


## Setup

1. **Install it.** In RoRoRo: **Plugins**, where the marketplace offers Ur Score (or **Install from
   URL**, pasting the release URL you were given), or by hand (see *Install by hand* above). Walk
   the consent screen — it lists `host.metrics.report` and `host.queries.accounts` — and click
   Install. Ur Score then runs as its own window, launched from RoRoRo's Plugins page (autostart is
   off by default, so it won't launch itself the next time you boot).
2. **Pick your clan.** Ur Score already knows Pet Sim 99's modes, so there is nothing to import. The
   first time Setup opens it lands on **Setup › Pet Sim 99** with the clan search ready: search for
   yours by name, **Make main** marks the one the board leads with, and **Watch it instead** follows
   one your accounts aren't in. Turn a mode off on the same page if you don't want it. No file
   editing, and no restart.
3. **Choose your stats.** In **Setup › Stats**, tick **Show** to put a stat on your board and in
   your score book, and **Send** to also report it to RoRoRo. The **Name RoRoRo uses** box beside
   each stat is the metric id RoRoRo will see; a mode suggests one, and you can change it. Press
   **Save stats**.
4. **Reading starts on its own.** Ur Score starts reading as soon as it opens (turn that off in
   **Setup › Pet Sim 99**, under **Start reading when Ur Score opens**); if it hasn't, click the
   status chip's **▶ Start reading**. The board fills in within a few seconds of the first read —
   its panels, the state line above them, and the line along the top naming the period being read
   (or how often this reads) and when the next read is due. If nothing fills in, press **⟳** (or
   **F5**): it reads every source once, and **Setup › Diagnostics** then says per mode what
   happened, when it last read, when it reads next, and which of your accounts a source couldn't
   read and why.
5. **Pick which accounts actually send.** The **Send** checkbox on each row in **Setup › Your
   accounts** controls whether that account's numbers go to RoRoRo, one checkbox per mode. Every
   account starts on; untick one and it keeps being read, kept and shown, but stops sending — and
   the choice survives a restart.

### The status chip

The top bar carries one control for reading, where Start/Stop and Test now used to sit side by
side. Before reading has run this session it shows **▶ Start reading**, filled cyan — click it and
it starts. From then on the chip only shows status and never pauses by itself: **● Live** (cyan),
**❚❚ Paused** (amber — the loudest state, since paused silences your phone alerts), or **▲ Trouble**
(a source is unhealthy, or RoRoRo isn't running). Clicking it opens a card: the state line, one
line per switched-on source (when it last read and when it reads next, or its trouble), whether
phone alerts are going out, and **Pause reading** / **Resume reading**. Esc or a click elsewhere
closes the card. While paused, the window title also carries "(Paused)", so it shows on the
taskbar or a second screen without hovering over anything.

**⟳**, beside the chip, reads every source once — the same as **F5** anywhere on the board window
(a popped-out panel doesn't take it). The state and detail lines under the bar show only when
there's something worth saying: paused, trouble, not yet started, a problem, or numbers the board
is still showing from an earlier read.

### Boards and panels

The tabs across the top are separate boards. Starter tabs follow your sources until you change
that tab; editing one does not freeze the others. Use **+ Board** for a board of your own. The
**...** menu beside the tabs offers **Rename**, **Duplicate**, and **Delete** for the current board;
right-clicking its tab or pressing **Shift+F10** opens the same menu.

**+ Add panel** opens the gallery. Choose a panel, then pick the source, stat, or accounts its form
asks for. Each panel's **Settings** button changes those choices later. A panel whose source or
stat was removed says so and offers **Choose another**.

**Arrange** opens a draft of the board on screen, in a banner where the state and detail lines usually sit:
`Arranging "Battle" · drag a header to move · drag an edge or corner to resize · ←/→ move`. Drag a panel by its
header to move it; drag its right edge to change its width, or its bottom-right corner to change its width and
make it one or two rows tall. With the keyboard, focus one of a panel's buttons and use Left and Right to move it,
Ctrl+Left and Ctrl+Right to change its width, and Ctrl+Up and Ctrl+Down for one or two rows; the panel a keyboard
is moving shows a cyan focus ring. **✕**, in each panel's header, removes that panel, and **+ Add panel**, in the
banner, opens the gallery. The banner's **Done** saves the draft, counting the changes it will save ("Done (3)";
no count once the draft is back as it was); **Cancel**, or Esc
from anywhere in the window, leaves arranging instead — with nothing changed it just leaves, with a change it
asks first, in Ur Score's own themed window. The tabs stay clickable while arranging (**+ Board** and the tab
menu are off until Done or Cancel): clicking another tab saves the draft the same way Done does, then switches, and a save that fails
keeps you on the board and says why, in the banner. Closing Ur Score while arranging saves the draft too. Panels
flow in reading order, so a move or a resize can move the panels after it, and arranging never changes a panel's
height on the way in or out.

**Undo** is Ctrl+Z, and works any time the board window has focus (a popped-out panel doesn't take it). Every
change to a board — moving or resizing a panel, adding or removing one, and a panel's **Settings** — can be undone,
per board, for as long as Ur Score stays open (the last 20, not saved anywhere). Outside Arrange, each change is
its own undo step. While arranging, Ctrl+Z steps back through the draft one change at a time, and **Done** counts
them ("Done (3)"); pressing Done folds the whole arrangement into a single step, so one Ctrl+Z after Done puts the
board back exactly as it was before you started arranging. A themed toast at the bottom of the board says what an
undo is offered or what it did — "Arranged Battle · Undo", "Changed Past battles · Undo", "Undid: Removed Battle
race" — and shows for about six seconds; a new change replaces it. It shows while arranging too, where its Undo
steps back through the draft; entering Arrange hides any toast from before, and Cancel hides the draft's, and so
does Done when arranging ends without saving a change. A rename is never undone. A save that fails leaves the undo
history as it was and the toast says so, redacted like every other problem line. Undoing back onto a board a
starter tab still matches re-follows your clans again; if your sources changed in between, the toast says so
instead: "Restored, but Battle no longer follows your clans." Pop-outs are never undone, and deleting a board
clears its history.

**Pop out** puts a panel in its own always-on-top window. It keeps updating with the board, and
its saved position is restored on the next start. Use the pop-out's **Return to the board** button
or **Bring back** on its board slot to return it; **Alt+F4** on that pop-out also returns it.
Closing Ur Score keeps pop-outs for the next start. A direct external close preserves their saved
placement and does not reopen them during that session. Resizing a pop-out does not resize its
board tile. Panel slots and pop-out windows wait until the score book loads successfully; a
failed load offers **Try again** without clearing their saved placement.

Boards, panel choices, and pop-out placement are stored in
`%LOCALAPPDATA%\626labs.ur-score\boards.json`. The score book is separate: removing a panel or a
board does not delete your recorded history. **Duplicate** is disabled for an empty starter that
still follows its sources. Populated starters and user-created boards can be duplicated normally.

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

Almost everything is set in the **Setup** window. What a mode reads — its clans, which stats it
shows and sends and under what metric id, and which of your accounts send — is set on **Setup ›
Pet Sim 99**, **Stats** and **Your accounts**. Which sources exist and which are switched on lives
in `sources.json`, and your boards and panels in `boards.json`, both beside the settings file.
Thresholds aren't here at all: RoRoRo does the judging, and you write those in **Setup › Alerts**.

`%LOCALAPPDATA%\626labs.ur-score\settings.json` holds four keys, and only the first is worth
touching by hand:

| Key | Default | What it does |
| --- | --- | --- |
| `resolveNames` | `true` | Whether other members' Roblox ids are sent to Roblox to look up their usernames for the leaderboard. See *What leaves your machine*. There is no checkbox for this. |
| `startOnOpen` | `true` | Whether Ur Score starts reading as its window opens. Ticked as **Start reading when Ur Score opens** under **Setup › Pet Sim 99**; no reason to edit it by hand. It takes effect the next time you open Ur Score, or when you close Setup if reading hasn't started yet, and reading still happens only while the window is open. Pausing from the status chip lasts only until Ur Score closes; the next open reads again. |
| `modes` | *(none)* | The switches on the game page. A key is a game (`"pet-sim-99"`) or a mode (`"pet-sim-99/battle"`, `"pet-sim-99/profile"`), the value `true` or `false`. A missing key means the default, which is on, so a fresh install writes none. A mode reads only when its game and the mode itself are both on. Use the switches on the game page rather than editing this by hand. |
| `settingsVersion` | `3` | Ur Score's own marker that the modes decision has been made. It is 3 once 0.7.0 has looked at your install. Leave it alone. |

**Upgrading from 0.6.3 keeps what you had.** The first time 0.7.0 starts over an existing data
folder whose settings are below version 3, it writes the `modes` map once: a mode comes on if 0.6.3
had any of its readers installed, and off if it had none. A player who only ever read Battle stays
on Battle, with Profile off and no profile source added; nothing starts reading that wasn't
reading before. A fresh install gets both modes on. If `settings.json` is missing or can't be read,
Ur Score does not guess: it runs on the defaults and writes nothing until you change something.

Older settings files may still carry an `activeRecipe` key. Nothing reads it (it has been inert
since sources moved to `sources.json`), 0.7.0 no longer writes it, and you can leave it or delete
it.

Beside those, in the same folder: `recipes\` (the ticks and metric ids you chose per reader; any
reader file an older version saved there is ignored, since the readers come from the app now, and
Setup › Diagnostics lists one that no mode names under "Not part of any mode (kept, not read)"),
`scorebook\`, `accounts.json` (RoRoRo's last account list, so the window has names before RoRoRo
answers), `icon-cache\` (the pictures, and `source-icons.json`, which says which clan each icon
belongs to), and `keys.dat` — any key a reader asked you to save, encrypted for your Windows
account. Ur Score masks every saved key as `[key hidden]` in anything it shows, saves or copies.

### Moving your setup to another PC

**Setup › Score book** exports your score book, and with it your setup: which modes are on, your
clans, your ticks and metric ids, and your boards. Reader text isn't carried, because both PCs
get their readers from the app. A file that carries a setup is a version 3 file, and an Ur Score
older than 0.7.0 refuses it as made by a newer version; a stats-only file (a score book with no
setup) stays version 2 and any Ur Score reads it. An export from 0.6.3 still imports: its saved
reader text is ignored and its choices are kept for the readers a mode names.

## What it doesn't do

- **Alert on anyone but your own accounts.** Even though the leaderboard shows every row a source
  returns, reporting is your accounts only. Everyone else's ids and values are shown on your screen
  and reported nowhere.
- **Set thresholds, cooldowns, or send notifications.** RoRoRo owns all of that.
- **Touch Roblox itself.** Ur Score reads https and writes to a local pipe. It cannot click, type,
  or otherwise act inside a Roblox client.
- **Run itself in the background.** RoRoRo's autostart for this plugin is off by default, and Ur
  Score reads only while its own window is open. Ur Score starts reading as soon as it opens;
  untick **Start reading when Ur Score opens** in **Setup › Pet Sim 99** if you'd rather start it
  yourself from the status chip. Either way, it never watches anything you haven't opened it for.

## Troubleshooting

**The state line**, above the board, answers "what is it doing right now" in one sentence. Before
reading has run this session it reads "Not started."; once paused, "Paused. Nothing is read or
sent, so phone alerts are off."; with no source switched on, "Running, with nothing to read yet.";
and while all is well, "Reading 1 source." or "Reading 3 sources." If a source is in trouble, its
name and its reason replace that — a source is named by the input you typed for it, so the line
reads "Nebula: Could not reach the data." While reading waits for RoRoRo's list of your accounts
(up to 20 seconds) it reads "Starting. Asking RoRoRo for your accounts…", and while ⟳ reads,
"Reading every source once…". A read you ask for while paused says what it found after "Not
started." or "Paused. …": "Last read: Reported to RoRoRo.", or the source in trouble by name. If
the score book itself can't be read, the line says "Your score book couldn't be read.", the line
under it says why, and the board offers Try again until it can be.
And whenever any panel is drawing numbers from the score book rather than from this session, the
sentence about their age is appended to whatever else the line says.

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
| Waiting for a value to be set. | A mode has an input nobody filled in, such as Battle with no clan picked. Set it on the game page.  Nothing is being read. |
| Could not reach the data. | A network or transport problem reaching the source. Waiting is the remedy; it isn't RoRoRo's fault. |
| Nothing matched what was entered. | The source says the name you typed matches nothing. Waiting won't fix it — check the spelling on the game page. |
| Nothing to read right now. | Normal, not an error. The source itself says there is nothing on; for a battle, this is the usual state between periods. |
| The response was not a shape Ur Score understands. | The source answered with something the reader doesn't expect. Use **Copy diagnostics** — the detail names the keys actually present. |
| None of your accounts are in what came back. | Rows came back, and none matched a saved RoRoRo account. Check the input you set, and that your accounts have resolved Roblox ids. |
| Reporting to RoRoRo. | Working. At least one account matched and its value was sent. |
| Reading. No stat is set to send to RoRoRo. | Also working. It's being read, kept and shown; nothing is ticked Send. |
| RoRoRo is not running. | Reading and keeping continue; every report is held, and none is queued. |
| RoRoRo refused the report. | A capability — named in the detail, `host.metrics.report` or `host.queries.accounts` — is not granted. RoRoRo's Plugins page has no per-capability re-grant and never re-prompts an existing consent record, so the only way back is **Remove** Ur Score there, then reinstall it, which puts the consent screen in front of you again. |
| The source asked Ur Score to slow down. | A rate limit. The next read tries again. |
| The source wants signing in, which readers cannot do. | The address needs a session, and readers never have one. Held until the inputs change. |
| A key is needed. | A reader declares a key that isn't saved, or is saved for a different host. |
| The source rejected the key. | The saved key was refused. Held until a key changes. |

A source can also read `Switched off.`, `Waiting for its first read.` or `Not started.`, which mean
what they say.

If none of that explains it, click **Copy diagnostics** on that page and paste the result into
wherever you're asking for help. It carries each reader's schedule and which stats it shows and
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

The .NET 10.0.203 build has intermittently generated WPF application code without a `Main`
entry point (CS5001). Absolute intermediate-path normalization initially passed clean and
incremental builds, but the error returned after a XAML edit; the root cause remains unresolved.
A fresh absolute `--artifacts-path` under this repo's `obj` directory has worked for validation.
Use the same path for the build and `dotnet test --no-build`. Keep temporary build outputs under
`obj`, not `artifacts`: the normal build can otherwise compile their generated C# files as source.

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
`artifacts/plugin.zip` — the three files RoRoRo's installer expects at a release URL. A release
adds `build/install.ps1` as a fourth, for installing by hand; how a release is cut, and the RoRoRo
catalog bump that must follow it, is in [docs/releasing.md](docs/releasing.md).

## License

Apache License 2.0, © 2026 626Labs LLC. See [LICENSE](LICENSE) and [NOTICE](NOTICE). Releases published before 2026-09-24 were released under the MIT License. The contract bindings (`ROROROblox.PluginContract`) come from the parent RoRoRo repository under its own license (MIT).

---

**A 626 Labs product · *Imagine Something Else*.**
