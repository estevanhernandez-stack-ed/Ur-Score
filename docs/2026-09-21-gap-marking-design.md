# Gap marking: a chart that says when nobody was watching

A chart draws every series as one polyline with all its points joined (`LineChart.cs:88`). There is no
notion of a gap anywhere in the code. So two readings ten hours apart are drawn as a single straight
segment, and that segment is indistinguishable from ten hours of steady climbing.

The owner turned his PC off at 23:23 on 2026-09-20 and back on at 08:48. His race chart will draw that
night as a clean diagonal. Nothing was read; the chart says otherwise.

The codebase already holds the rule this breaks, and applies it at the right-hand edge of a series:

> "on a chart a line to 'now' IS the claim that it was read now" — `PanelModels.cs:374`

That comment exists because plotting a remembered total out to the current minute would claim a read
that never happened. A straight line across a hole in the middle makes the same claim, for ten hours
instead of one minute, and nothing stops it.

## What this is not

It is not filling gaps in. The owner's framing was right and worth recording, because it corrects the
instinct to call any derived number an invention: **interpolating between two real readings is
calculation, not invention.** Both endpoints were measured. "K0i2 gained 2.1B over ten hours" is a
fact, and so is the average per hour that follows from it.

What is not known is the SHAPE inside the span. The clan may have climbed steadily, or sat idle until
someone logged on at eight. Both fit the same endpoints.

So the line worth holding is narrower than "do not invent". It is: **an aggregate across a gap is
honest; per-moment detail across a gap is not.** This design shows the aggregate and refuses the
detail, and the mechanism for refusing it is simply that no points are created.

## 1. What counts as a gap

A gap is a span between consecutive readings longer than:

    max(Pace.Shortest, 3 x the recipe's EffectiveEverySeconds)

`Pace.Shortest` is 15 minutes and is not a number chosen here: it is already the smallest span this
codebase will call a measurement (`Pace.Over` returns null below it). Ruled by the owner 2026-09-21,
with the reasoning that a few missed reads are ordinary — a slow API, a closed lid, a retry — and
marking those would turn every chart into dashes and teach the reader to ignore the marking.

The second term matters for a recipe that reads slowly. At the clan list's three minutes the threshold
is 15 minutes. At thirty minutes it becomes 90, so a slow recipe does not flag every normal interval as
a hole.

## 2. How it draws

Dashed across the span, chosen by the owner over a broken line. The line still connects, so a clan can
be followed across the night and the race chart stays readable — the band was redrawn on 2026-09-20
precisely because lines that cannot be told apart are useless — but the span is visibly not measured.

`ChartPoint` gains one flag meaning "the span from the previous point to this one was not measured".
`LineChart` draws those spans as dashed segments and the rest solid, emitting more than one polyline
per series where it needs to.

Nothing else changes shape: no new record, the legend is untouched, and the band's colour-per-line
logic is untouched because the number of `ChartSeries` does not change. `ChartSeries` already carries a
`Dash` field, but that one is a whole-line style used when the palette runs out of colours, so it is
not the mechanism here and must not be confused with it.

## 3. What it says

When a gap falls inside the drawn window, the panel's note gains a sentence about the LONGEST one:

> Nothing was read for 10h 19m from 23:21. K0i2 gained 2.1B over that span, an average of 204M an hour.

The longest rather than the most recent, because that is the one that changes how the chart should be
read. Listing every gap would clutter a note that already carries other parts.

`PanelHead.Note` is the existing mechanism and already joins parts with a space; both chart-bearing
models carry a `PanelHead`. The figure is about the series the panel is about: the best placed of your
clans on the race chart — the same anchor the band already uses — and the card's own account and stat
on the account card.

The wording says "an average of", not a rate, because the average is what the endpoints support.

## 4. The property that makes it safe

**No point is created anywhere.** The gap figure is computed at display time from the two real
endpoints and never enters a series, the score book, a pace, a best hour, a record or an alert.

This is the whole safety argument and it is worth being explicit about why, because the obvious
implementation — synthesising a reading every three minutes across the night so every chart is
continuous — would quietly destroy a protection that already works. `Pace.Over` refuses a window longer
than `Pace.LongestCurrent` with this reasoning recorded on it:

> "That last one is what a closed app does to a chart. Seen on the owner's board 2026-09-20: Ur Score
> was off for fourteen hours, so the newest reading before 'the last hour' was yesterday's, and a
> fourteen-hour average was labelled 'Current'."

A filled series has no gaps left to see. `Pace.Over` would stop refusing, `BestHour` would happily
select an hour that never happened, and a calculated aggregate would have become per-moment detail
without anyone deciding that it should.

## 5. Where it lives

A new pure helper in `Board/`: `ChartGaps`, taking points and a threshold and saying which spans were
not measured. Called from `PanelModels` where the series are built, which is the only place that knows
both the points and the recipe's read interval.

That matches how `Pace`, `Records`, `BoardLayout` and `ChartGeometry` are already organised — pure,
windowless, testable without a UI — and it keeps the rule in one place rather than in two panels.

Two surfaces only, because they are the only models carrying a chart: `RaceModel` and
`AccountCardModel`.

## 6. Testing

- A span over the threshold is marked; one under it is not.
- The threshold scales with the recipe's read interval, so a slow recipe's ordinary spacing is not a gap.
- The note names the LONGEST gap in the window, with the right duration, delta and average per hour.
- **The point count is unchanged by gap detection.** This is the test that pins §4, and it should fail
  loudly if anyone ever implements this by filling.
- `Pace.Over` still refuses a window that spans a gap, and `BestHour` still skips one.
- A series with no gaps draws exactly as it does today.

Per V3-S.37: each of these is to be proved by breaking the implementation and watching the test fail,
not by reading it. In particular the marked-versus-unmarked pair must use a fixture where a wrong
threshold changes the answer, and the note test must assert the NOUN and the numbers, not only the
sentence's shape.

## 7. Not in this work

- Filling, smoothing or estimating per-moment values. §4 is the reason.
- A shaded band behind the gap. Considered and set aside: it needs a drawing primitive `LineChart` does
  not have, since it draws only polylines, a grid and labels. Worth revisiting only if the dash alone
  turns out too subtle on a small panel.
- Any change to what Ur Score reports to RoRoRo. Nothing here touches the send path.
- Marking gaps anywhere other than a chart. The standings, the score book screen and the panels that
  show a single number are unaffected.
