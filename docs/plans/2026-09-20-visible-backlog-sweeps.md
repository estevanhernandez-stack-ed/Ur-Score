# Visible backlog sweeps, 2026-09-20

Eight items in the main section are marked "you'd notice". The held-apart 0.3.2 section has none, so
these eight are the whole visible surface. This plans the order to take them in and says plainly which
of them are not sweep work.

Branch point: `fix/one-meaning-of-your-clans` at 03aaf12, off master at v0.5.4. 1394 tests green.

## The constraint that shapes the order

A live Pet Sim 99 battle (SpaceMineBattle2026, roughly 4d19h left at the time of writing) is being
recorded by the installed plugin at `%LOCALAPPDATA%\ROROROblox\plugins\626labs.ur-score`. Ur Score is
single-instance, so launching a local Release build takes the lock and the installed copy stops
recording. That already cost 32 minutes of readings once today.

The standing rule is that built UI gets screenshot-compared before it is called done. Every visible
item below therefore needs the app running at some point, and every launch is a gap in the recording.

**The plan is to take exactly one lock, once.** Code and tests for every sweep land first. All visual
verification batches into a single short launch at the end, announced to `rororoblox-69` beforehand and
closed immediately after. A gap that does open is recoverable but not free: the same battle is being
recorded on the owner's other Surface, and `Setup > Score book > "Bring in stats…"` merges the two books
by matching recipe slug plus inputs, interleaving readings taken at different instants. Nobody has run a
cross-machine merge in anger yet, so treat it as a fallback, not a plan.

## Sweep 1 — finish the thread today opened

The tightest, highest-confidence work, and it closes "which clans are mine / what gets kept" completely.
No app launch needed except the batched visual check for V3-S.33's tint.

1. **V3-S.33** — the Top panel's tint. Already ruled by the owner on 2026-09-20: a watched clan keeps
   its place and its estimate row, and loses the cyan tint. `TopRow`'s shape does not change; `Yours`
   becomes role-aware from `SourceRules.MyClanNames` and the pin/append decisions move to a separate
   local set holding today's membership. Reconcile the name derivation while in there: this site reads
   `live.SourceName(s)` rather than the source's inputs, so the shared predicate will otherwise miss
   rows the old one matched. One file, one failing test first.
2. **V3-S.31, the remaining half** — `ImportText` still describes what a clans list keeps in its own
   words instead of deriving the sentence from the predicate that does the keeping. The two can drift,
   and drifting here means the consent screen lies.
3. **V3-S.25** — `GroupRows.Keep` persists 25 group names per read with no check that a group is a
   clan. The shipped recipes point at clans, so the 2026-09-20 ruling covers them; a third-party recipe
   whose rows are PLAYERS writes strangers' usernames to disk down the same path, which no ruling
   covers. Add an explicit recipe flag (groups are clans, not people) and keep names only when set.

2 and 3 belong together: V3-S.26 already said to fix the consent copy in the same hour V3-S.25 lands and
to derive the sentence from the same predicate the writer uses. Doing either alone re-opens the drift.

## Sweep 2 — the one small visual fix

4. **S1-14.11** — panels in a row keep their own height instead of stretching, so row bottoms are
   ragged. `src/UI/Controls/PanelGrid.cs:65-66`. Small and self-contained, and the only item here whose
   verification IS the screenshot. Natural companion to the batched launch.

## Sweep 3 — look down the leaderboard (V3-S.32)

Needs a decision before any code, and the decision is the owner's:

- An Ur Score metric carries no name, because RoRoRo words an alert from the rule that fired. So a
  metric alert can say "a clan behind passes us in under six hours" but never "H8ER does". Is the
  nameless sentence good enough on the phone, or does the named sentence belong on the board and in a
  report, with the metric only as the trigger?

The other design note is settled by the row itself: track the rival BY NAME, not by the position below,
because a position's points do not reveal a change of occupant on the way up the way they do on the way
down, so the clear-on-fall rule that works for `field-above` does not work below. `GroupSeries` already
keeps a named clan's series.

Positioning, from the same day: Ur Score is built alongside the clan's Discord bot, not to replace it.
The edge is RoRoRo's phone leg — a real push with no Discord app, at most a 250-character title and a
992-character body. Reproducing the Discord post is not the win; getting the same intelligence onto a
phone quietly is.

## Sweep 4 — roster membership (V3-S.20)

Ur Score places accounts in a clan only from battle contributions, so between battles it places none,
although the clan API returns a full roster on every read. Measured 2026-09-16: CCGP's roster held 6 of
8 accounts while the board said "No clan is in a battle right now" for all 8.

Larger than it looks. It touches the clan recipe (does it even read `Members`? — check before
estimating), the engine's membership model, `LiveBoard.ChipRole` (V3-S.19) and both grouping screens.
Privacy is unchanged: other members' ids are compared against yours and dropped, never kept, logged or
written, which is the rule battle contributions already follow.

Take this one only after sweeps 1 and 2 are committed, because it is the first item that can fail
halfway and leave the tree in a state worth reverting.

## Not sweep work, and saying so rather than pretending

- **V3-S.9**, board editing — a snap grid with free placement, corner resize and a live drop outline.
  The row itself says "its own spec and plan". It is a feature with a design cycle, not a backlog fix,
  and it is comfortably the largest thing on this list.
- **V3-S.23 and V3-S.24**, loadout testing and event loadout testing. Both marked "not built". The
  owner asked on 2026-09-20 for these to be explored by several agents before any design happens, so
  the next step is exploration, not implementation.

Killing all eight today is not on. Killing items 1 through 4 is, and that empties the visible list of
everything that is genuinely a fix rather than a feature.
