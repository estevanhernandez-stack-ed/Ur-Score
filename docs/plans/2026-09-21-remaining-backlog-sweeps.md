# Remaining backlog sweeps, 2026-09-21

74 rows open, counted as rows on the backlog's stated bounds. This sorts every one of them by what it
actually needs — a code change, a launch, a measurement, a decision — and puts them in an order where each
sweep leaves the tree green and mergeable on its own. The day's earlier sweeps took 96 open rows to 74; 11
of the 22 closed by finding the row was wrong and saying so rather than fixing what was not broken, which
is a rate worth planning for below.

Branch point: master at e1ad6c8 (PR #19 merged). 1,492 tests green.

## What the day taught, and what it changes about the order

- **A third of the rows are wrong or half-wrong.** S1-7.1, S1-9.5, S2-2.1, S1-9.4, S1-14.15 and S1-8.6 all
  described a defect that was not there, or not there as described. Each was settled by *doing the thing the
  row said and watching* — deleting the alias, putting a real deny on the file — never by reading harder.
  So every sweep below budgets for GONE as an outcome, and a row closed GONE with the evidence is as much a
  win as one closed FIXED.
- **The test comes first and must be seen red.** V3-S.37's rule held every time it was applied and bit every
  time it was skipped (the two-line finals test, the timestamp test that asserted only "not in the past").
  Nothing below is done until its test has been watched failing.
- **Rows cluster by file.** Six rows touch `AppServices`, four touch `SourceHost`, three touch `RecipeWatch`,
  three touch `BoardsFile`. Sweeping a file's rows together means reading it once and one PR per cluster.
- **A launch is expensive and now safe.** `Copy-UrControlData` scrubs a seeded folder so nothing can reach
  RoRoRo, and the dev build is indistinguishable from the installed plugin to the host, so every walk must
  assume it connects. Everything that needs the app running goes into ONE owner-watched launch at the end.

## Sweep A — paper and comments (10 rows, one PR, no risk)

Everything that changes prose and nothing that runs. Fastest possible return, and it clears the noise so
the later sweeps' counts mean something.

| Row | What | Likely outcome |
|---|---|---|
| S2-P.4, S2-P.7, S2-P.19, S2-8.5 | Corrections to a historical plan doc's text | GONE — the plan is a record of what was planned, not a living spec; fixing typos in it rewrites history. Say so once and close all four. |
| S2-9.1 | Two smoke README rows don't say they end with the privacy check | FIXED, one line each |
| S1-1.3, S1-5.2, S2-1.2, S2-2.3 | Comment-only: a rule with no "why", a comment untrue in test mode, a `Save` comment missing a throw, uneven doc comments in `BoardEdits` | FIXED, comments |
| S1-2.3 | Merged commits carry a different co-author line | GONE — merged history is not rewritten |
| V3-S.9 | Board editing — PR #13 landed header drag, edge and corner resize, the drop caret; the owner chose push and reflow over free placement and stay-put | FIXED with the ruling recorded, since the row's stated goal was explicitly ruled against |
| V3-S.37 | The testing standard, currently living as a backlog row | FIXED by moving the four rules into `docs/testing.md` where a contributor finds them, and pointing the row at it |

## Sweep B — the remaining behaviour edges (14 rows, three PRs by file)

Real behaviour, each bounded, each with a test seen red first. Grouped by the file that holds them so each
PR reads one thing once.

**B1 — `SourceHost` and the read lifecycle (4 rows).** S1-8.2 (a read in flight is not waited for on exit),
S1-8.3 (Test now keeps sending a clan removed mid-read), S1-8.4 (off-and-on during a read runs two watches
briefly), S1-6.5 (switching to Watch mid-read lets the rest of that read send). All four are the same
question — what a watch does with a change that lands while it is reading — and want one answer, written
once. Expect at least one to be GONE: S1-6.5 is already documented as "applies next read", which may be the
right ruling rather than a defect.

**B2 — `AppServices` and the account list (5 rows).** S1-14.7 (an icon fetch that throws is never retried),
S1-14.8 (changing tracked stats releases a held sign-in stop), S1-14.13 (a policy refresh landing out of
order), S2-5.10 (a second unreadable-file copy on retry), V3-S.28 (the history budget counts no term for a
subjectless metric — bounded: add the field-metric count and run the check on the field save).

**B3 — `BoardsFile` and the book (5 rows).** S2-P.9 (an unreadable boards.json copied verbatim — it is the
user's own text, so the likely answer is GONE with that said), S2-1.1 (a locked boards.json throws while
reading the old file), S1-5.1 (the dropped count off by one in a race), S1-11.2 (the Top switch reads the
first list recipe rather than the page's own), S1-12.6 (the Stats page asks for counter names again).

## Sweep C — tidiness in clusters (20 rows, three PRs, then one on its own)

Code-only, testable where there is behaviour, comment-and-shape where there is not. Same clustering.

**C1 — the core (8 rows).** `RecipeEngine`: S1-2.1 (a read that stops on its last step still carries a period, and no test says whether that is right — settle it with one). `RecipeWatch`: S1-6.4, S1-6.6, S1-6.11. `SourceHost`: S1-8.5 (the factory
inside the lock — a real concurrency change, do it last in the PR and with the Start/Stop test watching).
`SharedAccounts`: S1-F.9. `AccountClaims`/`SpacedTransport`: S1-4.3. `App.xaml.cs`: S1-14.16.

**C2 — the UI and composition (8 rows).** S1-10.1 (one `Show` helper for seven files), S1-10.2, S1-11.1,
S1-12.9, S1-14.17, S1-14.6 (async paths with no catch — pairs with S1-11.1), S1-14.12 (undisposed client
and token), S1-14.14 (trail lines carrying exception text — a privacy tidiness, so it goes first in the PR).

**C3 — the board (3 rows + fixtures).** S1-13.10, S2-4.7, S2-6.7, and S2-P.13 (the fixtures redefined in
five test classes).

**C4 — `PanelModels.cs` on its own.** S1-13.15 said 761 lines were worth splitting; it is 1,373 now. A
mechanical split by panel type, one PR, no behaviour change, the suite as the proof. Done after C1–C3 so it
does not collide with them.

## Sweep D — performance, measured (4 rows, one or two PRs)

None of these is done until there is a number before and a number after, because "faster" without a
measurement is the same claim as "fixed" without a red test.

- S1-F.3 first — each chart scans every kept reading of every clan, and five weeks of book may hold
  100–170 MB. Potentially the only one a person would feel. Measure a five-week book's chart render, then
  index by clan.
- S1-F.2 — startup reads the whole book twice. One pass feeding both the finals index and the reader.
- S2-5.1 — the starter board rebuilt on every read of the board list, including the 20 s clock.
- S1-12.5 — disk reads on the UI thread on every Alerts refresh.

## Sweep E — test infrastructure (5 rows, two PRs)

- A `TimeProvider` with a real `CreateTimer`, which closes S1-8.7's two remaining gaps (timer-driven reads
  and the removal-during-read race) and makes every future timer testable without waiting one out. The
  actual work is the timer; the tests follow in minutes.
- S1-5.6 — the 24-second drop-limit test. Either a non-flushing test mode or a smaller limit under test; the
  durability choice stays for real use.
- S1-L.2, S1-L.4 — the RowList fence: run it against the original bug's shape, and extend it to ListBox lists.
- S1-14.9 — startup composition coverage. The largest single test gap and the last, because it is the one
  most likely to need a seam added to `AppServices`, and the tidiness sweeps will have shown where.

## Sweep F — one launch, owner-watched (8 rows)

Code every walk change first, against the helpers and without launching. Then one launch through
`Copy-UrControlData`, with RoRoRo quit for the duration if the owner prefers, and every one of these verified
in the same session.

S1-15.4 (`--try` against a real battle — needs one running), S1-L.1 and S1-L.3 (two walk fixes), S1-16.1
(a leftover `.smoke-backup-*` should be noticed by the next run — this one can be coded and tested on the
helpers alone), S2-FR.3 (the last hole in the pop-out walk), S2-FR.4 (the light-theme menu never seen
live), S1-L.5 (a strict UI Automation checker over the lists), S2-8.6 (mixed-DPI pop-outs — only if a second
monitor is to hand; otherwise it stays open and says so).

## Not sweep work, and what each is waiting on

| Row | Waiting on |
|---|---|
| V3-S.44 | The owner's go-ahead for a 31-file namespace rename against a quiet tree. Recommended early: mechanical, compiler-checked, and it stops a finding that has now been filed wrongly three times. |
| S2-P.16 | The owner's call on changing automation ids the smoke walks hard-code. Real defect; fix lands in `tools/smoke/` too. |
| S1-F.11 | The owner's timing: before the repo goes public, a rename of real-looking names and ids in tests and comments. Bounded, touches many files. |
| V3-S.20 | A design cycle: roster membership between battles. |
| V3-S.36 | A design cycle: per-clan metric ids, with the migration and name-mutability hazards already written on the row. |
| V3-S.23, V3-S.24 | Exploration before design, as the owner asked on 2026-09-20. |
| V3-S.41 | Host-side work first, by agreement with `rororoblox-77`; the Ur Score half buys the smaller face. |
| V3-S.39 | Days of ordinary work with no CS5001 after the `obj` exclusion. Not to be closed on a clean afternoon; it has been closed on one of those twice. |
| V3-S.40 | The next STA stall, with a full hang dump captured. Cannot be forced. |

## The order, and why

A, then B, then C, then D, then E, then F. A is free and clears noise. B is the highest value per row and
the most likely to turn up GONE rows, which is cheap to learn early. C is safe volume. D needs measurement
tooling that C's reading of the code will have made obvious. E's timer work is its own small design and is
worth doing once, properly, after everything that would use it exists. F batches every launch into one.

At the end of F, what is left open is exactly the table above: nine rows, each with a named thing it is
waiting on, none of them a fix somebody could simply have done.
