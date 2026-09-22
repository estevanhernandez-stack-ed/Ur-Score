# Testing in Ur Score

A test is worth only what it discriminates. These four rules came out of seven tests, written over two
days by people carrying that very warning in their briefs, that initially passed under the exact regression
each existed to catch. Every one of the seven was found by breaking the implementation and watching; not one
by reading the test. The rules are short because they have to be applied every time, not admired.

## 1. Watch it fail before you trust it

Before a new test counts, break the code it covers in the way it exists to catch, and watch it go red. Then
put it back and watch it go green. A test never seen to fail has not been shown to work — it has only been
shown to pass, which every test does against code that happens to be right.

This is not a formality for hard cases. The tests that passed under the bug they were written for were
ordinary ones: a sign-flip where two values happened to be equal, a fixture whose decoy sat next to the real
answer, a cap tested only on the side that refuses. Each looked right. Each was wrong until the code was
broken under it.

Put the capture in the record: "watched red at X, green at Y" in the commit message or the PR. It is the
evidence, and it is cheap.

## 2. Build the fixture so the decoy is distinguishable

A wrong anchor must change the output. A `Gap` must differ from a `Value`. A cap must have a positive case as
well as a negative one. If two values in a fixture are the same number, the assertion cannot tell which one
the code used, and a test that cannot tell that is not testing the choice.

Examples from this repo, all real:

- The finals-correction test first had two lines with the correction first in the file. "First wins" — the
  bug — picked the correction by coincidence and the test passed before the fix. Three lines, in an order
  where each candidate rule gives a different number (100, 175, 250), was the fix to the test.
- The race-band test placed the clan at 40th, well below the top 25, because the same test at 24th passes
  under the old one-either-side rule and proves nothing: the top-25 rule covers the band by accident.
- The id-0 miss test holds TWO accounts with no id. With one, dropping the filter names that one account and
  the test still looks reasonable; with two it names whichever comes first, and the misattribution fails.
- The timestamp test injects an instant in 2031. The old test asserted only `>= before` against the real
  clock, which passes whether or not the code reads the injected one.

## 3. Assert the noun, not only the clause

When a sentence is assembled from a subject and an explanatory clause, assert the subject. The clause reads
as the interesting half and is usually the half that is already right. "No sources are named here" shipped
through eight task reviews, a whole-branch review and a fix wave, because every test pinned the explanation
and none pinned the word "sources", which should have been "clans".

The same applies to any composed output: a label with a name in it, a line with a count in it, a message
with a cause in it. Pin the part that carries the decision.

## 4. Some things are only reachable by running the app

The noun bug above was invisible to every form of reading. It was found by the owner looking at the screen.
Built UI is screenshot-compared before it is called done, and the smoke walks under `tools/smoke/` exist for
exactly the checks a unit test cannot make: what a control is named to a screen reader, what a row says when
it is drawn, whether a button appears at all.

A walk is not free — it takes the app's single-instance lock and, as of 2026-09-21, must assume it can reach
RoRoRo as the real plugin — so every change that needs one is batched into a single owner-watched launch,
seeded through `Copy-UrControlData`. Read `tools/smoke/README.md` before writing one.

## 5. WPF tests run one class at a time, and a hang gets its stacks taken

Every test class that touches WPF — a control, a window, a XAML parse, the application — carries
`[Collection(WpfCollection.Name)]`, and `WpfCollectionFenceTests` fails the build for one that does not. WPF's
first-touch static initialisation deadlocks when two threads start it in the same instant (the stacks from
2026-09-22 are in `docs/2026-09-22-sta-stall-stacks.txt`; the reasoning is on `WpfCollection`), and a parallel
suite four seconds long starts it on several threads at once by default. Serialising those classes costs
milliseconds. There is also exactly one `Application` per process, ever; `UiThread.RunInApp` builds it and any
test that needs the app's resources goes through that.

When a run hangs anyway: do not kill it first. `dotnet tool install -g dotnet-stack` once, then
`dotnet-stack report -p <testhost pid>` gives every managed thread's stack in a second, and that is the whole
diagnosis — V3-S.40 stayed open for five days for want of it. Then kill the host.

## Where the rules came from

Backlog row V3-S.37 records the seven tests, what each failed to discriminate, and how each was caught. It is
the case file; this page is the standing order. When a new test is caught the same way, add it to the examples
above rather than to the row.
