# Threat alerts: the clans behind us, by name

V3-S.32. Ur Score models catching the place above and nothing at all about being caught, which is the
direction that decides a battle. The owner ruled on 2026-09-20 that the threat must be NAMED, on the
phone, not only on the board.

The bar is the clan's own Discord bot, screenshots 2026-09-20:

> Threat Alert — outpaced / H8ER passes us in 5h 56m / 553.59M behind — 748.36M/hr vs our 654.98M/hr
> (+93.38M/hr) / We're at 9.10B points with 4d 23h left in the battle

Ur Score is built alongside that bot, not to replace it. Its edge is RoRoRo's phone leg: a real push
with no Discord app, at most a 250-character title and a 992-character body. Reproducing the Discord
post is not the win. Getting the same intelligence onto a phone, quietly and only when it matters, is.

## 1. What the host allows, established not assumed

Read out of the RoRoRo host by `rororoblox-69` on 2026-09-20, against v1.30's code.

- **A rule's label is read late.** `LocalFileMetricRuleSource.CurrentRules()` reads the file on every
  report and hashes it; the cache skips only the re-parse when bytes are identical. There is no stale
  cached label.
- **The binding point is the observation, not the push.** The `MetricRule` read at report time is
  captured on the `AlertTrigger`, and `WebhookPayload` reads `rule.Label` off that captured object up
  to five seconds later (`MetricBreachBatcher.Window`) plus dispatch.
  → **Ordering rule: write the label, THEN report the number. Never report-then-rename.**
- **No per-fire free text.** Title is `{noun} — {label}` plus a tail the host generates from the rule's
  kind, threshold and window. Body is `• {name} — now {value}`. The only per-fire dynamics are the
  observed value and numbers already in the rule. The label really is the only channel a name can ride.
- **The host has no concept of rule ownership.** Its `RuleRow` has no `owner` field; `RuleOwner` is Ur
  Score's own bookkeeping. The host will never object to who wrote a row. Preserving other plugins'
  rows is entirely `RulesFile`'s job, which `OursFor` already guarantees.
- **Churn is cheap.** A rewrite costs one re-parse of a small JSON array on the next report.
- **Concurrency is safe.** Ur Score writes temp-then-`File.Replace`, so the host never sees a partial
  file; a sharing violation during the swap costs one report of silence and retries.
- **The batcher groups by `(Kind, MetricId, Rule)` and `MetricRule` is a record**, so equality includes
  the label. A label change inside the five-second window splits one grouped push into two.

## 2. Managed labels, a capability rather than a special case

Ruled by the owner, 2026-09-20: option 1 plus the backup carve-out.

A label a human typed and a label a machine computes are **different kinds of thing**, and the code has
to say which it is holding. Every bug fixed earlier today was one field serving two masters with nothing
to say which: `MineNames` answering both "what is drawn" and "what is mine"; `TopRow.Yours` doing both
"keep this visible" and "this is mine"; `IsGroupList` standing in for "these are clans, not people".
A label that a human types and Ur Score overwrites is that same shape, and building it deliberately
hours after pulling three instances out would be the session's own lesson unlearned.

So Ur Score gains a named capability: **a rule whose label it maintains.**

- **The marker lives on the metric, not in the rules file.** `FieldMetric` gains a flag saying this
  number's label is Ur Score's to maintain. Nothing new is written to `rules.json` and the host's
  parser is not involved at all, which is the cheapest possible place to put a decision that is purely
  Ur Score's business. A rule is then managed when it is ours (`owner: "626labs.ur-score"`, which
  `OursFor` already matches on) AND its metric declares a managed label. A rule a human made on the
  same metric has no owner marker, so it is never ours and never rewritten — that is existing
  behaviour, not new work.
- Considered and rejected: a new field in `rules.json`. The host ignores fields it does not parse —
  `owner` is already exactly that, an Ur Score field the host's `RuleRow` has never had — so it would
  have worked. It is rejected because it puts an Ur Score-only concern into a file two other parties
  read, for no gain over a flag in our own catalogue.
- Setup shows a managed rule's label as live and does not offer it for editing. The card says it names
  the clan closest behind you and renames itself. Threshold, window and the recovery tick stay the
  owner's to set: only the label is managed.
- The ordering rule from §1 is enforced in ONE place — the writer that owns managed labels — rather
  than remembered correctly at every future call site. This is the invariant-versus-prose distinction
  V3-S.31's row named as the codebase's biggest weakness, so it gets an invariant.

**The backup carve-out.** `RulesFile.Write` swaps the whole file's backup on every successful write
(`File.Replace(temp, path, path + BackupSuffix)`), and the backup is per FILE, not per rule. Repeated
automatic rewrites would therefore consume the undo point for rules the owner typed by hand. A
managed-label rewrite takes a path that leaves the backup file untouched. Two things already blunt the
problem and neither is sufficient alone: `Unchanged` means a rewrite happens only when the name really
changes, which over a battle is a handful of times; and a managed rule's own label is never something
anyone typed. Neither protects the OTHER rules in the file, which is why the carve-out is in the design
rather than argued away.

## 3. The numbers

New ids under the existing fixed-id scheme, so a clan leader can still say "set an alert on that
number" and every member sets the same one:

- `clan.standing.threat-gap` — how far the nearest real threat is behind, in points.
- `clan.standing.threat-hours` — hours until that threat passes us at both current paces. This is the
  one a rule fires on, with an "alert when below" threshold: "tell me when someone is within six hours
  of taking our place".

Pace is not sent, for the reason already recorded in `FieldMetrics`: RoRoRo derives a rate from a rising
number itself. What it cannot derive is a crossing, because that needs both clans' paces, so that one is
computed here. `Pace.Over` and `Pace.Chase` already exist and already do this for the place above.

## 4. Track the rival by NAME, and do not name the wrong one

The row's first design note, and it is load-bearing. `field-above` can use a POSITION because a change
of occupant above you shows up as the position's points falling, and `Pace` refuses a window that fell.
Below you that signal does not exist: a position's points on the way up reveal nothing about a change of
occupant. So a threat is tracked by name. `ScoreBookReader.GroupSeries(sourceId, groupName, period)`
already keeps a named clan's series, so the mechanism exists.

**The trap, and the reason this section is here.** A clan three places back with a much higher pace can
pass us sooner than the one directly behind. Naming the immediate follower and calling it "the soonest"
would be a confident false statement on a phone mid-battle, which is worse than sending nothing. So the
computation considers a BAND of clans behind, works out a crossing time for each, and names the soonest
by TIME, never the nearest by place.

**Corrected 2026-09-20, while planning.** An earlier draft of this section said `GroupRows.Keep` had to
be extended from `at ± 1` to a band below, because only the immediately following clan has a series in
the score book. That is true of the book and irrelevant here: the alert path never reads the book.
`RecipeWatch.SendFieldAsync` works from the LIVE `reading.Groups`, which carries the whole field, plus
its own in-memory series kept for `Pace.LongestCurrent`. So every clan behind us is already visible at
send time and no change to `Keep` is needed for this work.

What IS needed is a per-name in-memory series, because the existing ones will not do. `_fieldAbove`
tracks a POSITION and clears itself whenever the value falls, since a fall is what a change of occupant
looks like from a position. A threat is tracked by name, where that rule is both wrong and unnecessary:
a named clan's points do not fall, and its identity is the key rather than something inferred. So
threats get their own store keyed by clan name, pruned of names that leave the band.

The inherited limitation is the same one the place-above metrics already carry and accept: the series
does not outlive the process, so after a restart the numbers that need a pace go quiet until there are
`Pace.Shortest` of readings again.

**This does NOT depend on V3-S.25.** That dependency was a consequence of reading the book, and the
alert path does not. A list that makes no claim still yields live groups, so a threat is still
computable. Kept names matter only if the named sentence later goes on the board, which is out of scope
here (§7).

## 5. The label

Mirrors the 0.5.3 pattern that put the clan's own name on a label ("K0i2 clan points") via
`AlertCards.ClanLabel`, with the difference that this one is recomputed. Shape:

`H8ER catching K0i2` → the host renders `{noun} — H8ER catching K0i2` plus its own generated tail.

The name is the threat's, the second is yours, and the owner sees their own clan on their own phone
while the metric id stays shared across the clan.

## 6. Testing

- The ordering rule has a test that fails if a report can be issued before its label is written.
- A managed label is not editable from Setup, and a human's rule on the same metric is never rewritten.
- A managed-label rewrite leaves the backup file untouched; an ordinary write still replaces it.
- `Keep` holds the band below yours, and a threat is computed only for a clan actually in the book.
- The nearest threat by TIME is named, not the nearest by place, with a fixture where those differ.
  This is the test that would have caught the trap in §4.
- No threat is invented from a single reading: with no pace for the chaser, the number is absent rather
  than a guess, matching how `Needed` already refuses.

## 7. Not in this work

- Batching several threats into one message, as the Discord bot does with LXCC and R0W together. The
  host already batches by rule within its five-second window; whether that is enough is a question for
  after the single-threat case is real.
- Any change to the place-above metrics. They are correct and shipped.
- Naming on the board and in a report. Worth having, and cheap once the computation exists, but the
  ruling was about the phone and that is what this delivers.
