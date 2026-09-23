# Changelog

All notable changes to RoRoRo Ur Score are documented here. Format roughly follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning follows [SemVer](https://semver.org/).

## 0.6.2 - 2026-09-23

### Added

- **Undo, per board.** Ctrl+Z takes back moving, resizing, adding, removing and changing (**Settings**) a panel,
  the last 20 steps for the session. Outside Arrange each change is its own step; while arranging, Ctrl+Z steps
  back through the draft and **Done** folds the whole arrangement into one step, "Arranged Battle". A themed
  toast at the bottom of the board names the change and offers **Undo** for about six seconds; entering Arrange
  hides it. A starter tab re-follows your clans again if the board an undo restores still matches what the
  starter draws; otherwise the toast says so. Pop-outs are never undone, and deleting a board clears its history.

## 0.6.1 - 2026-09-23

### Added

- **Arrange gets Cancel and Esc.** With nothing changed either just leaves; with a change, a themed
  window asks first, naming the board. **Done** now counts the changes it will save ("Done (3)").
  The panel a keyboard is moving shows a cyan focus ring.

### Changed

- **Edit board is Arrange.** Panels keep their height going in and out — the drag handle, ✕, and a
  dashed outline are drawn inside the panel's own header instead of adding a row above it. The tabs,
  **+ Board**, and the tab menu stay on and clickable while arranging: a tab click saves the draft
  first, the same way Done does, then switches, and a save that fails keeps you on the board and
  says why, in the banner.

## 0.6.0 - 2026-09-23

### Added

- **A status card.** Click the status chip and a card opens: the state line, one line per
  switched-on source (when it last read and when it reads next, or its trouble), whether phone
  alerts are going out, and **Pause reading** / **Resume reading**. Esc or a click elsewhere
  closes it; opening it never pauses.
- **F5 reads every source, the same as ⟳.** Not on a popped-out panel, which has no ⟳.
- **"(Paused)" in the window title** while reading is paused, so a paused board shows on the
  taskbar or a second screen without hovering over anything.

### Changed

- **Reading starts on open, by default.** New installs and existing ones alike; existing
  installs are switched on once, and a player who unticks it keeps it off from then on. Turn it
  off in **Setup › Recipes**, under **Start reading when Ur Score opens**. It is asked again
  when Setup closes and when the first source is switched on, for a board that hasn't started yet.
- **Start/Stop and Test now became one status chip and ⟳.** **▶ Start reading** starts a board
  that hasn't read this session; once it has, the chip only shows status — **● Live**, **❚❚
  Paused** (amber), or **▲ Trouble** — and never pauses by itself. Pause moved into the card.
- **Closing Ur Score asks only while it is reading and sending.** A paused or never-started
  board now closes without asking.

## 0.5.10 - 2026-09-23

### Fixed

- **A tall panel stays tall when you let go of its corner.** Dragging the corner of a two-row panel and letting
  go at the height it had made it one row; it is measured against one row now.
- **Moving a panel with the keyboard keeps going.** After the first Left or Right, focus fell out of the board and
  the next key moved nothing. It stays on the moved panel.
- **Popped-out panels show no resize grips**, since their slot can't be resized.
- **✕ says which panel it removes**, for screen readers.

## 0.5.9 - 2026-09-22

### Added

- **Install by hand.** Every release now carries `install.ps1` beside `plugin.zip`: right-click › Run with
  PowerShell checks the zip against its checksum, refuses while RoRoRo or Ur Score is running, moves the old
  install aside, and installs where RoRoRo looks. The README's new *Install by hand* section also gives the
  fully manual route. The same script ships with every RoRoRo plugin.

## 0.5.8 - 2026-09-22

### Added

- **Closing Ur Score asks while it is sending.** Closing the board ends Ur Score, and with it the reads,
  the score book and every report to RoRoRo, so your phone alerts stop. While a send tick or field metric
  is on, closing now asks first; **Keep running** is the default, so Enter or Esc keeps it open. With
  nothing sending it closes as before, and a Windows shutdown or sign-out never asks.

## 0.5.7 - 2026-09-22

### Fixed

- **The import preview names every clan.** A profile or clans-list source showed its recipe's slug
  (`pet-sim-99-profile`); it now shows the recipe's name.
- **The import's stats line counts battles apart.** "Imported 2,054 readings and 62 finished battles",
  as the preview counts them, instead of adding the battles to the readings.
- **Promotion check keeps the other clan's lowest.** When your clan's read brought nothing back, the
  panel hid the other clan's lowest as well; it is still true, so it now shows beside the note.

## 0.5.6 - 2026-09-22

### Added

- **Your setup travels with your stats.** Export stats now carries your recipes with their ticks, your
  clans, your boards and two settings, applied whatever you tick (Start on open stays this PC's own), and
  Import stats shows you what will change — added, updated,
  replaced, kept — with a tick on each, before anything is written. Keys never travel (the file names
  the ones to enter again). **Nothing arrives set to send:** every send tick is off until you tick it on
  the new PC, so a file from elsewhere can never start reports on its own. Your previous setup is copied
  aside, dated, before the import. A 0.5.5 stats file still imports.

## 0.5.5 - 2026-09-22

### Added

- **Export stats and Import stats.** Setup > Score book writes your score book to one dated file, and
  reads such a file in on another PC — the month files and the recipe texts the book keeps, with a
  one-line manifest, and nothing else. No keys (they are bound to you on the machine that made them),
  no clans, boards or settings. The other PC matches the readings to its own clans by recipe and clan
  name, skips what it already has, and names any clan it does not follow rather than guessing. A month
  file inside a folder you copied by hand still imports, so the old way keeps working.
- **Edit a board the way 2026 does.** Drag a panel by its header, haul its edge or corner to resize, and
  a caret shows where it will land. Push and reflow: the other panels move out of the way and back.
- **Update a recipe when a newer one ships.** Setup > Recipes shows an Update button on an installed
  recipe when the built-in copy is newer (same name and author, different text), and says what changed
  before you take it. You are no longer pinned to whatever you first imported.
- **If installing or updating Ur Score has failed for you before, try again on RoRoRo 1.30.** The download
  used to give up after 100 seconds, so on a slow connection it could never finish; RoRoRo 1.30 gives it
  ten minutes. (Ur Score itself still runs on RoRoRo 1.28 or later.)
- **The clans behind you, by name.** Ur Score modelled catching the clan above and nothing about being
  caught, which is the direction that decides a battle. Two new numbers in Setup > Stats' "Clan and
  field" section: **Points the threat is behind**, and **Hours until the threat passes you**. Set the
  second to alert below six hours to hear about it while you can still answer.
- The clan named is the one that takes your place **soonest**, not the one nearest you on the board. A
  clan three places back going much faster passes you before the one directly behind.
- Your phone names it. An Ur Score number carries no name of its own, so the name rides on the alert's
  label and is rewritten as the threat changes: "H8ER catching K0i2". The number's id stays the same on
  every machine, so a clan leader can still say "set an alert on that number" and every member sets the
  same one. The Alerts card tells you this, because that screen deliberately shows the number's own name
  rather than the one your phone will say.

### Fixed

- **A clan you only WATCH is no longer treated as one of yours.** "Your clans" was worked out separately
  in three places and two had drifted apart. On the race chart a watched rival placed above you anchored
  the whole band on itself, marked itself as yours in the standings, and every other clan's gap was
  measured from its points instead of yours. The Top panel tinted it as yours too. A watched clan now
  keeps its place on the Top list, because seeing it is the point of watching it, and loses the tint.
- **A list of groups keeps names only when its recipe says those groups are clans.** "Group" is whatever
  a recipe points at, so a list of PLAYERS has the same shape as a list of clans. Recipes now say
  `"groupsAreClans": true` to keep names; without it a read still records how the whole field is doing
  and writes no names at all. Both shipped lists say it. The import screen tells you which is happening,
  and a race chart with no board says why rather than coming up empty.
- The import consent screen no longer promises that a list like this holds no players. It could not know
  that for a recipe we did not write, and it is the one screen whose job is telling you what you are
  agreeing to.
- **Setting up a clan number no longer lets you past RoRoRo's history limit.** The count of what you send
  now includes each clan number a list sends, and the Clan and field save refuses a tick that would go
  over, the way a stat tick already did. The start-up warning counts lists too.
- **A screen reader names what it reads.** Rows in the Stats table, the pages in Setup's list and the
  clan-search matches are read by their names ("Points", "Diagnostics", the clan) instead of the name of
  the thing behind them.
- The score book opens faster and charts draw faster: a five-week book loads in one pass instead of two,
  and a board's charts read their own clan's lines rather than every clan's.
- A stat tick the limit refuses is undone with one redraw instead of three; the race note says "clans";
  a stopped read on a list recipe still names the battle on the board's top line; a locked boards.json
  is copied once, not once per retry; a line in the score book is filed under the clan that was actually
  read even when the clan's settings changed mid-read; a picture fetch that failed is asked again on the
  next read; the trail names an error's type and never its text.

## 0.5.4 - 2026-09-20

### Added

- **"Also tell me when it comes right again."** A tick on any alert rule. Ticked, you get a second
  alert when the number comes back over its line — "K0i2 clan points is back above 9,000,000,000", or
  "is climbing again" for a rate rule. Off unless you tick it, and it goes wherever that rule's alerts
  already go.

  This is the other half of a change in RoRoRo: an alert fires once now, on the crossing, so silence
  afterwards means "still bad" — which on a lock screen looks exactly like nothing being wrong. The
  tick is what tells you it is over. **It needs RoRoRo 1.30 or newer.** On an older RoRoRo the tick
  saves and does nothing; the rule still alerts as it always did.

### Fixed

- **The alert editor said "an account's" about numbers that belong to no account.** All six clan
  numbers described themselves as something they are not, in the one place you set them up. It names
  the right subject now — and where the label already carries your clan's name, it does not say it
  twice: "Alert me when K0i2 clan points goes below ...".

## 0.5.3 - 2026-09-20

### Added

- **Your clan's name rides on its alerts.** A clan number carries no account, so RoRoRo had nothing to put at
  the head of the buzz: it said "Clan points went above 9,000,000,000" and never said whose. The alert's label
  now starts with your clan — "K0i2 clan points" — while the metric id stays the same for everyone. That split
  is what lets a leader name one id, have forty people set the same alert, and have each of them see their own
  clan on their own phone.

### Fixed

- **`clan.standing.idle-members` had never sent once.** Contributors can exceed members, because it counts
  everyone who has scored in this battle including people who have since left the clan — 72 members and 73
  contributors on the owner's own board. The guard treated that as nonsense and returned nothing. Members on
  zero is now floored at none.
- **Setup › Clans said "Shown live, never kept"** on the screen where you switch the clans list on. It now says
  what every read keeps.
- **Every alert sentence said "an account's"**, including the six clan numbers that go out with no account. A
  clan number now reads "your clan's Clan place goes above 10".
- **The import screen said nothing from a clans list is kept.** It names what it keeps instead: where yours
  stands, how the field is doing, the top 25 by name.

### Changed

- **Setup › Score book lists a clans list** like anything else you keep — readings, first reading, size, and its
  reason when it is not recording — so "how much is this holding" has an answer on screen.
- **No pace is projected from a window shorter than an hour**, and the chase row measures the clan above over
  the same two-hour window the sent metric uses instead of averaging the whole battle.

## 0.5.2 - 2026-09-20

Three things a multi-agent review found in 0.5.0 before the clan did.

### Fixed

- **A clan you WATCH is no longer treated as one of yours.** Watching a rival made its standing your standing:
  if it placed above you, its points were written to your score book as your clan's and its place, gap and
  roster counts went to RoRoRo under your clan's ids. The set a read uses to find your row now excludes watched
  and switched-off sources, which is what the role has always promised.
- **A ticked clan number gets an alert card.** 0.5.0 let you tick six numbers and then gave you nowhere to set
  an alert on them, because Setup › Alerts filtered clans lists out of its cards. They get cards now, and the
  "nothing is sent yet" line names Clan and field as well as Stats.
- **No pace is projected from a window too short to mean it.** "On this pace" needs an hour behind it — twenty-five
  minutes carried across a five-day battle is the 26-billion nonsense the design doc warned about — and the chase
  row now measures the clan above over the same two-hour window the sent metric uses, instead of averaging the
  whole battle.

### Changed

- The clan-points tick says a rate rule gives your clan's pace **per minute**, which is what RoRoRo's rate rules
  measure.

## 0.5.1 - 2026-09-20

### Changed

- **The battle race lines up.** Your own clan is in the book from the battle's first minute; a rival only from
  the first clans-list read that kept it — so the chart drew your line across the whole width and squeezed the
  whole band into its last tenth. The band sets the window now, every line uses the full width, and the card's
  subtitle says which window it is instead of still claiming the whole battle. With nothing to race, your whole
  history is drawn as before.
- Two clans of yours read "K0i2 and CCGP", not "K0i2, CCGP", and the gap number reads "the clan one place above
  you", not "the clan in the place above you".

## 0.5.0 - 2026-09-20

### Added

- **Alerts on your clan's standing.** Setup › Stats has a new **Clan and field** section: tick a number and it goes
  to RoRoRo, so an alert can fire on it. A clan leader can name one and tell the clan to set the same alert, because
  the ids are fixed by the version rather than chosen per install:

  | tick | id |
  |---|---|
  | Clan points (a rate rule on it is your clan's pace) | `clan.standing.points` |
  | Clan place | `clan.standing.place` |
  | Points behind the place above | `clan.standing.gap-above` |
  | Points an hour needed to pass them | `clan.standing.pace-needed` |
  | Free clan slots | `clan.standing.free-slots` |
  | Members on zero | `clan.standing.idle-members` |

  Each goes out with **no account attached**, so the alert is worded about the clan, not a player. Nothing is ticked
  until you tick it. The catch-up number needs a pace for the clan above as well as yours, so it stays quiet for the
  first fifteen minutes after Ur Score starts, and again for fifteen minutes whenever you gain or lose a place — the
  place above is a position, and when it changes hands its history is somebody else's.

### Changed

- **The battle race has real colours.** The chart used to draw from cyan, magenta, white and two greys, so on a
  seven-line band six rivals were shades of the same thing. There are now eight series colours — cyan, magenta,
  yellow, green, violet, orange, periwinkle and near-white — which is enough for the whole band, so no line falls
  back to a dash. They are Ur Score's own and a theme change leaves them alone: a line's colour is what says which
  clan it is.
- **Setup › Alerts lists the clans list too.** It used to be left out of the "what leaves this plugin" cards
  because it sends for no account. Now that it can send clan numbers, it has a card that names them — or says
  where to tick one.

## 0.4.2 - 2026-09-20

### Changed

- **The race chart draws the clans you are racing, by name.** Three either side of yours, each with its own colour
  and its own legend entry, and a dash pattern once the theme's five colours repeat. Twenty clans sharing one grey
  could not be told apart, and the ones that decide your place are your neighbours, not the leader.
- **The whole board is on the card.** A Standings button opens the top 25 by place: clan, points, and how far each
  is from yours, with yours in cyan. It stays open across reads, and there is no button without a clans list.
- **"Bring in stats"**, not "bring in a book", on Setup › Score book.

## 0.4.1 - 2026-09-20

### Changed

- **The race chart reads at a glance with the board on it.** Taller (240 px), six value lines rather than four,
  and the axis follows the data instead of anchoring at zero once other clans are drawn - twenty lines pinned to
  zero squeezed the pack into a band. A race of your own clans still starts at zero, where growth from nothing is
  the story.

## 0.4.0 - 2026-09-20

### Added

- **The recipes come with Ur Score.** Setup › Recipes lists what it ships - the clan battle, your accounts'
  profiles, the top clans - each one press away, with the hosts it would contact shown beside it. No download, no
  file picker, no JSON. Adding one still opens the same review screen, because seeing what a recipe will contact
  before it is added is the promise, whatever the text came from. Importing a file works exactly as before.
- **Bring in a score book from another PC.** Setup › Score book takes the other machine's Ur Score folder and joins
  its readings to this one's. Every PC mints its own source ids, so readings are matched to a source by its recipe
  and the clan it follows, then rewritten to this PC's ids - the two machines become one history instead of two
  halves. Anything already here is skipped, a clan this PC doesn't follow is named rather than guessed at, and a
  recipe that isn't installed here is named too.

## 0.3.12 - 2026-09-20

### Fixed

- **The other clans on the race chart are visible.** They were drawn in the edge colour, which on navy is the
  background: the legend said nineteen clans and the chart showed one line. They use the muted grey now, with your
  own clans still in the bright palette.

## 0.3.11 - 2026-09-20

### Fixed

- **A pace never reaches across a gap in recording.** With Ur Score closed for fourteen hours, "Current" was a
  fourteen-hour average and "Best hour" was the same span called an hour. A current pace now comes from the last
  two hours or says there has been no reading in the last hour, and a best hour must be about an hour.
- **A hopeless chase is still called hopeless** when there is no best hour to measure against: the current pace
  stands in as the ceiling, instead of the verdict softening to "needs a lift".

## 0.3.10 - 2026-09-20

### Added

- **The race chart draws the board.** The top ten while one of your clans is in it, the top twenty when none is,
  so the clans around you appear without you listing them. Yours keep their own colours; the rest share the faint
  one and get a single legend entry. A clan with no readings is simply not drawn.
- **Your own pace, on the Pace panel.** Your accounts' combined pace and their share of the clan's points, from
  readings the book already kept.

### Changed

- **"Now" is called "Current"**, and it was always the clan's pace, never your own - which is why your accounts
  now have lines of their own beneath it.
- **A clans list keeps the clans a chart can draw**, by name: the top 25, your own clans and the place either side
  of each. Public game standings, capped so the book stays small - about 200 KB a day rather than 700 KB. The
  field's own numbers (leader, top ten, average, bottom ten) still cover the whole board.

## 0.3.9 - 2026-09-19

### Added

- **A Pace panel.** How fast a clan is going now and on average, its best hour, where that lands by the end,
  the field's own pace once a clans list is switched on, and what catching the place above would take. Add it
  per clan, your own or a watched one.
- **Out of reach is said out loud.** A chase is measured against this clan's best hour of this battle: it says
  it is passed in so long, or what pace it needs, or that it is out of reach - and the certain version, out of
  reach even if they stop now.
- **Every pace states its window**, and a window under fifteen minutes says "too early to say" instead of a
  number. Measured on the owner's board: 25 minutes carried across 140 hours projected 26 billion points.

## 0.3.8 - 2026-09-19

### Added

- **Your clan's roster counts ride with the field.** A clans-list read also keeps your clan's members, what it
  can hold, and how many of them have scored in this battle. Members against capacity is whether there is a
  slot to move an alt into; members against contributors is how much of the roster is sitting out. Counts only,
  naming nobody, and only what the list actually carried - an absent count never reads as a full clan.

## 0.3.7 - 2026-09-19

### Added

- **A clans-list read also keeps where you stand.** Beside the field's four numbers: your own clan's points as
  that read saw them, the place you hold by points, the points of the place directly above you, and the gap to
  it. The place above is a position, never a clan - whoever holds it, the series keeps meaning "the one to
  catch". It is what a catch-up pace needs to survive a restart.

## 0.3.6 - 2026-09-19

### Added

- **The battle's clock, to the second.** Clan standing and the board's top bar count the period down
  as it runs, without waiting for a read: "SpaceMineBattle2026 · ends in 5d 21:20:02 · Fri 25 Sep
  11:00". The panel also gives the end as a time you can plan around; the top bar keeps to the
  countdown, which is all one line has room for. "ends in 3d" covered anything from 60 to 84 hours.
- **The field's pace is now recordable.** A clans list used to be drawn live and thrown away, so nothing
  could say how fast the rest of the battle was moving. Each read of one now keeps four numbers and a count:
  the leader's points, the top-10 average, the bottom-10 average, the whole field's average, and how many
  clans that covered. **No clan but your own is named on disk**, no account is matched to a list, and nothing
  is sent. Ranked by points, never by the rank the list hands over: the live board disagreed with itself on
  2026-09-19, showing rank 10 above rank 9.
- **`pet-sim-99-top-clans.recipe.json` ships with the release**, so the top-100 board can be imported like any
  other recipe. Every recipe in `recipes/` now rides each release, rather than a list kept in the workflow.

## 0.3.5 - 2026-09-17

### Fixed

- With both recipes imported, Setup › Your accounts' "Found in" text wraps instead of running into
  the first Send box.

## 0.3.4 - 2026-09-17

### Changed

- **Ur Score says what is true, or that it doesn't know yet.** Before anything was read, My accounts
  and Setup › Your accounts filed every account under "Not in a watched clan". They now say "No clans
  read yet", "No clan is in a battle right now" once every clan was read between battles, "Not found
  in the clans read so far", "Only in clans you're watching" or "Not matched by RoRoRo yet", and keep
  "Not in a watched clan" for when it is true. An Account card, Profile stat, Race and Diagnostics
  after Stop give their read's own reason too, and a stat a read didn't bring back says "Not in the
  last read." instead of a data path.
- **What Ur Score just said stays on screen.** While Start waits it says it is asking RoRoRo for your
  accounts, Test now says it is reading, and a read you ask for while stopped says what it found. A
  failure's message stays until your next press, and the lines on Your accounts, Score book and
  Recipes no longer vanish on the next refresh. A stopped board with RoRoRo closed no longer claims
  to be reading.
- **Each clan keeps its own picture.** The window wears your main clan's picture from the moment it
  opens, instead of whichever clan was read last. Clan standing shows the logo of the clan it is
  about, and a recipe's row in Setup shows its main clan's.
- **A clan's chip says "watching" when the read in hand holds none of your accounts,** instead of
  "yours" only because you added it that way.
- **Numbers count what their labels say.** "#1 of 3" counts only players with a value for that stat,
  the same players Promotion check ranks against. Standing's change is magenta for a fall. The sent
  dot means sent in the last read, not at some point this session. A chart whose values are all
  equal draws through the middle and names the value.
- **A score book that can't be read offers Try again** instead of leaving Start and Test now off for
  the session. It says why once, on the state line, and the board says what that stops.
- **A close from outside keeps a pop-out popped out.** Closing every window from outside, as an
  updater or taskkill does, used to bring pop-outs back to the board. Now they stay out and reopen at
  the next start. The pop-out's own close, Return and Bring back still bring a panel back.
- **Duplicate is off for the empty starter tab,** where it used to make the original tab disappear.
  A starter with panels, or a board you made, duplicates as before.

### Fixed

- The drag handle is drawn, instead of a Braille character some PCs couldn't show, and dragging a
  panel by it works as before. Its tooltip and the pop-out's "Return to the board" wear Ur Score's
  theme.
- Past battles can drop its best-account line ("Don't show your best account"), so a panel whose
  stat was removed can be saved again.
- Profile stat settings say when the chosen source is switched off, without blocking Save.
- Setup › Diagnostics names an account a read skipped because another source of the same recipe
  already held it, and which source.
- A huge threshold typed into the rules file by hand reads short, and Change opens with the refusal
  showing. A result for an alert that is no longer in the file shows in magenta.
- Updating a recipe installed before stats had ticks shows its Show and Send ticks, and the change
  list says when a recipe starts tracking the battle or reading past battles.
- The import screen's "Kept in your score book" list names the period and its times beside the
  headline items.
- The gallery's Race card no longer counts switched-off clans, and Promotion check's card agrees with
  its form about which recipes qualify.
- Screen readers hear the sent dot with its account, the drag handle by name, and "Choose another"
  with the panel it belongs to.
- The two notes on Setup › Your accounts sit closer together.

### Score book

- Each rank also keeps how many players it was counted among, as `ranked`, beside `of`. A place kept
  before this shows without a count.

### Documentation

- README covers boards, tabs, the gallery, pop-outs and boards.json. This file gains the 0.3.1, 0.3.0
  and 0.2.0 sections that had tags but no entries.

## 0.3.3 - 2026-09-16

### Added

- **Your finished battles fill in on their own.** A clan between battles used to read as
  "nothing to see": the read stopped before it reached the finished ones, so the Past battles panel
  stayed empty however long a clan had been playing. It now hands over the finished periods either
  way, and a first read backfills every one the source still remembers - 23 of them on the clan this
  was found on. Only your own accounts' numbers are kept, exactly as before.
- **Rename, Duplicate and Delete are on screen.** They were only ever on a board tab's right-click
  menu, so they read as missing. A `...` button now sits on the tab row beside + Board and opens the
  same menu. Right-click and Shift+F10 still work.

### Changed

- **Confirmation dialogs are Ur Score's own.** The seven remaining stock Windows message boxes are gone.
  Three questions that need asking - delete a board, remove a recipe, add a clan past the limit -
  share one themed confirmation whose safe answer is the default. The two that were only reporting a
  failure say it where you are looking instead: a board that would not save keeps its reason on the
  board until a save works, and a recipe that would not import says why under Import recipe.
- **The import screen fits its window again.** The stats list was a fixed height whatever the recipe
  offered, so a recipe with one stat pushed Cancel, Import, and the line telling you to tick a stat
  off the bottom of the screen. It now grows to its rows.
- **A popped-out panel opens at its panel's size** rather than one size for all of them, so a table
  opens as a table. Each panel's Pop out and Settings buttons now say which panel they belong to.
- **Past battles reads as a table.** Total and Your best no longer run together, the battle name
  keeps its width, and the panel says what it is ordered by instead of inheriting an order by
  accident.
- The line for a clan with no finished battles yet said you had none. It now says Ur Score has not
  read them yet, and that the next read fills them in.

## 0.3.2 - 2026-09-15

### Added

- **An Alerts page that says what will alert you.** Every stat you send gets a card on
  Setup > Alerts, and its alerts read as sentences you fill in: stops climbing (fewer than a number
  a minute, over 10, 15 or 30 minutes) and crosses a number (above or below). + Add an alert,
  Change and Remove, with what happened said on the card itself. Rules you or another plugin wrote
  are listed and never changed. A standing line names the one switch to turn on in RoRoRo. Rules
  Ur Score writes carry a `label`; RoRoRo 1.28 ignores it, and RoRoRo 1.29 uses it to word the
  alert from the rule that fired.
- **Your accounts have faces.** Each of your own accounts shows its Roblox avatar beside its name -
  in the accounts table, My accounts, Promotion check, the account card and Setup > Your accounts.
  Pictures load after the numbers and never hold a row up. Other players are never looked up: the
  leaderboard and the top of the period show names only.
- **Start reading when the window opens.** Once you are set up, tick **Start reading as soon as
  Ur Score opens** on Setup > Recipes and you no longer have to press Start. Off until you turn it
  on, and it takes effect the next time you open Ur Score.
- **The window opens on the last numbers it saw.** Rather than empty panels, Ur Score draws the
  last good reading from your score book, marks those panels `remembered`, and says in the state
  line how old they are. A remembered number is never sent to RoRoRo, never written back to the
  book as a new reading, and never given a rank it did not earn. A read that fails leaves them
  alone - it replaced nothing.
- **The reason an account has no numbers, beside the numbers it has none of.** When a recipe cannot
  read an account, its own explanation now appears on My accounts and Setup > Your accounts, where
  the dashes are, instead of only in places you might not be looking.

### Changed

- No stock message box and no JSON on the Alerts page. A failed write is said on the card and
  changes nothing. RoRoRo's rules file is backed up and written through a temporary file on every
  change, and every rule Ur Score does not own is kept exactly as it was found.
- The Stats table's rule line counts a stat's alerts.
- The board credits each source once. Recipes for one service share an opening sentence, and the
  footer used to repeat it once per recipe.
- The README describes the window that ships. It had described the build before recipes - including
  telling you to set your clan by editing a file, when Setup > Clans does it - and two of its
  claims about what leaves your machine were wrong: Ur Score does hold a recipe's key, in a
  DPAPI-encrypted store, and a per-account recipe does send your own Roblox user ids to that
  recipe's host.

### Removed

- `written-rules.json` is no longer written.

## 0.3.1

### Added

- Battle and Alts starter tabs follow their sources independently until each tab is edited.
- An accounts-table panel shows your own accounts across the selected stats, with sorting,
  totals, and a session-only selected account.
- Recipe values can describe durations, dates, counted entries, suggested Show choices, and
  sections used by the account card.

## 0.3.0

### Added

- Saved boards as tabs, with new-board, rename, duplicate, and delete actions.
- A panel gallery and settings forms for choosing each panel's source, stat, and accounts.
- Draft board editing with drag reordering, keyboard move controls, sizes, Tall, and removal.
- Always-on-top panel pop-outs that update live and restore their saved placement on restart.
- `boards.json` stores board layouts, panel settings, and pop-out placement separately from the
  score book. Account selections in layouts are restricted to your own accounts.

## 0.2.0

### Added

- **Recipes.** A recipe file says where a number is and how to read it; Ur Score runs it and hands
  your own accounts' values to RoRoRo. The Pet Simulator 99 clan battle is now a recipe
  (`tests/Fixtures/petsim99-clan-battle.recipe.json`) rather than code, and Ur Score itself names no
  vendor. Importing shows every host a recipe contacts and what each receives before anything runs.

### Changed

- **Ur Score paints in RoRoRo's theme.** It asks RoRoRo for its palette on connect and follows every
  theme switch after that, including the title bar. RoRoRo's Brand colours are used whenever RoRoRo
  is not running. No new capability: both theme calls are ungated.
- **Readable tables.** Cell text, headers, selection, checkboxes and scrollbars all use the theme,
  where the stock controls had painted black text on dark rows.
- Screen readers now read the status, clan and headline lines instead of a fixed label.
- The clan line reads "Live: ArcadeBattle2026" instead of the raw "battle=ArcadeBattle2026".
- The update screen lists each change on its own line.

## 0.1.0 — 2026-09-12

Initial build. Not yet installable — see the README's status callout: it needs RoRoRo v1.28.0.0,
which has not shipped, and a packaged release, which does not exist yet (`icon.png` is still
owed).

### Added

- **The clan dashboard.** Reads the live Pet Simulator 99 clan battle (via Big Games' public API)
  every three minutes: the clan's place and total points, every contributor ranked with your own
  RoRoRo accounts marked, and a points-per-minute figure per account computed from consecutive
  polls.
- **Reporting to RoRoRo.** Maps clan contributors to your saved RoRoRo accounts by Roblox user id,
  and hands each match's raw cumulative points to RoRoRo's `ReportMetric` so RoRoRo's alert rules
  can judge it. Reporting is gated by one chokepoint (`ReportPolicy`) enforcing three checks —
  configured metric id, allow-listed account, finite value — and nothing else in the plugin can
  reach the host client.
- **Eight distinguishable states** in the window instead of one shared "error": idle, unreachable,
  no battle running, unrecognised response shape, no matching contributors, reporting, host not
  running, and capability revoked. "No battle running" reads as normal, not as a failure.
- **Never a quiet empty list.** Every parse failure names the keys it actually found instead of
  returning nothing; the last raw response from each call is kept on disk (never transmitted) for
  a **Copy diagnostics** button to draw from.
- **Reads RoRoRo's `metric-rules.json`** every cycle and says plainly whether a rule exists for
  the configured metric id — and a one-click **Add this rule to RoRoRo** that previews the exact
  JSON before writing it, backs the file up first, merges rather than replaces, and never touches
  a rule someone else (the user, or another plugin) already owns.
- **Per-account send toggle.** Every saved account defaults to reporting; unticking one in the
  window stops it from sending while it keeps showing on the dashboard, and the choice survives a
  restart.
- **Leaderboard names, resolved through Roblox's own public batch lookup**, cached for the life of
  the process. Optional — turn off `resolveNames` in settings and the leaderboard still shows
  positions and points, just not other members' names, and the plugin then makes exactly two
  outbound calls per poll (both to the game's own API) instead of three.
- **Accounts with no Roblox id yet are named, not silently skipped** — RoRoRo hasn't resolved one
  yet (a brand-new saved account that's never been launched), and the window says so per account.
- **Attribution line** crediting Big Games' public API in the window itself, on every launch.

### Known gaps

- No packaged release exists. `build/build-plugin.ps1` stops at a missing `icon.png` by design —
  see README, *Build from source*.
- `ROROROblox.PluginContract` 0.10.0 is not yet on nuget.org; building from a clean clone needs a
  local feed or a local pack — see README.
- `minHostVersion` is `1.28.0.0`, which has not shipped. This plugin cannot be installed against
  any released RoRoRo yet.
