# Pace: how fast your clan is going, against the field

Status: design, 2026-09-19, battle day. The owner asked for average, best and target pace for their own clan
and a watched one, then for the field (top 10, bottom 10, average, the leader), then for a "pace to overtake"
calculator. This records their rulings and the shape of the work. Nothing here is released.

## Why now

SpaceMineBattle2026 ends **Friday 25 Sep 11:00 CDT**. Pace is read from a clan's total over time, and the
API gives current totals only — nothing can be worked out backwards. Every hour without recording is an hour
of the field's history that this battle will never have. So recording ships first, and the panel that reads it
follows.

## What is already kept

- **Your clans' totals over time.** Every read of a clan source writes a book line with its headline numbers
  (`clan-points`, `clan-place`). This is true for a watched clan too — a watch line carries the clan's numbers
  and no accounts. So **own-clan pace needs no new data**: average, best hour and projection can be read from
  the book today, for your main and for any clan you watch.
- **Nothing from a clans list.** A group-list recipe (the top-100 board) is drawn live and never written:
  `RecipeWatch` returns early with "Group lists are shown live and never kept."

## The owner's rulings, 2026-09-19

1. **What may be written from the clans list: the summary numbers only.** Per read: the leader's points, the
   top-10 average, the bottom-10 average and the all-100 average. **No clan but your own is named on disk.**
   Your own clan's points and rank are already recorded by its own source, so they are not duplicated here.
   The alternative — the whole top-100 list, with names — was offered and declined.
2. **Both surfaces.** A short pace line in Clan standing, and a Pace panel with the detail.
3. **Accepted, same evening:** where you stand, and the place above you. The owner asked for the catch-up
   pace as an alertable metric ("what we would need to catch up with x place, starting with the next clan
   above us"), and that number cannot survive a restart without the neighbour's history. So a clans-list read
   also keeps `field-mine` (your own clan's points as that read saw them), `field-mine-rank`, `field-above`
   and `field-gap-above`. **The place above is a position, never a clan**: whoever holds it, the series keeps
   meaning "the one to catch". Your own clan may be named; the ruling is about everyone else's.
4. **The metrics to send are the pacing ones**, led by the catch-up pace for the next place up. Sent with no
   account attached — the plugin contract allows that ("empty for a metric that is not about any one
   account"), so RoRoRo keys it globally and every member of a clan can set the same alert.
5. **Ticks live in Setup › Stats**, in a section of their own beside the per-account stats.
6. **Out of reach is a state worth naming.** The end time is known, so a catch-up that needs more than your
   best hour of this battle is reported as out of reach, and one that cannot be made even if the clan above
   stopped is reported as out of reach for certain.

## What gets written

No new line kind and no schema change: the summary numbers are **headline numbers of the clans-list source**,
the same shape `clan-points` already uses, on a line with no accounts.

```
{ "v":1, "kind":"read", "t":"2026-09-19T17:02:00Z", "source":"s-000000NN", "role":"Top",
  "headline": { "field-leader":788623705, "field-top10":350978119,
                "field-avg":82073868, "field-bottom10":21655539, "field-clans":100,
                "field-mine":181549159, "field-mine-rank":9,
                "field-above":185957960, "field-gap-above":4408801 } }
```

- `field-clans` is how many rows the average covers, so a short read can never read as a collapse in the
  field's average.
- **Rank comes from points, not from the list.** Measured 2026-09-19: the list's own `rank` disagreed with its
  own points (rank 10 showed more points than rank 9). Ur Score sorts by points and says the place it worked
  out, never the one it was handed.
- Recording happens only for a group-list source whose rows carry a value. Everything else about group lists
  is unchanged: no account is matched, nothing is sent, no clan name is written.

## What the panels show

**Clan standing** gains one line, for the clan that panel is about:

```
Pace        1.41M/h    avg 1.21M/h · best 2.30M/h
```

**Pace panel**, added per clan source (main, yours, or watched):

| line | meaning |
|---|---|
| Now | the last hour's gain, per hour |
| Average | total gained ÷ time elapsed, from the battle's start, or from the first reading Ur Score has, saying which |
| Best hour | the best hour of this battle, and when |
| On this pace | where the current pace lands by the battle's end |
| Field | leader, top 10, bottom 10, average — each as a pace, once there is history |
| To overtake | see below |

**Pace to overtake** (the calculator): pick the place above you, or any clan in the live list.

```
Rank 8 is 4,408,801 ahead.
They gain 0.9M/h, you gain 1.4M/h.
At these paces you pass them in 9h 12m, with 5d 20h to spare.
To pass them by the end you need 1.05M/h.
```

- A rival that is not the position above you is **live only**: their points come from the current read and
  their pace from this session's readings. Said plainly on the panel rather than implied.
- When you are gaining slower, it says what it costs: "you do not pass them at these paces; you need 1.05M/h".
- Nothing is projected past the battle's end.

## Alerts on these numbers

RoRoRo's contract settles most of the shape: a report may carry **no account** (`subject_id` empty), and rates
are the host's to derive from cumulative values, not the plugin's to send. So:

- **Pace alerts need no pace metric.** Send the clan's total with no account, and RoRoRo's existing "gains
  fewer than N a minute for M minutes" rule is the pace alert, for every member who sets it.
- **The catch-up pace is a level, not a total**, so it is sent as it is and alerted on with "crosses a
  number": "tell me when catching 8th needs more than 2M an hour" is a leader's sentence, and every member
  can set the same one.
- **Out of reach** is that same metric crossing above your best hour, which the panel also says in words.

## Order of work

1. Record the four (or five) summary numbers from a group-list read. Tests + `check-book-privacy` updated to
   prove no clan name reaches disk.
2. Ship `recipes/pet-sim-99-top-clans.recipe.json` (it exists only as a test fixture) and add it to the
   release's files, so the clan can import it.
3. Own-clan pace: the Clan standing line and the Pace panel's own-clan lines, from the book as it is.
4. The field lines and the overtake calculator, as history accrues.
5. Release, so the owner's installed copy starts recording.
