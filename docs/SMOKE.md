# Ur Score — the manual smoke list

Everything here is a thing the test suite cannot prove: it either needs a real clan battle, a real
RoRoRo host, or a person reading a real log. A green `dotnet test` run plus an untouched list below
means **unverified**, not verified — same discipline as the host's own metric-alert smoke list.

Tick a row only after it has been done on a real machine, with the date and what was actually seen
(not "should be fine"). If a row fails, write down what happened instead of re-running it until it
passes.

## Needs a real clan battle (Este-gated)

These need Pet Simulator 99 clan battle to actually be live, so they wait on Este's clock, not
this repo's.

- [ ] **A live battle produces real numbers.** Start Score Watch during a real battle with a real
      clan name configured. Contributions come back, at least one contributor matches a saved
      account, and the matched account's points climb between two polls three minutes apart.
- [ ] **A breach reaches the phone, from a real signal.** The whole chain, none of it faked: a
      real account's contribution rate actually falls below the configured rule's threshold, the
      host raises it, the phone rings. This is the row the host's own metric-alert smoke list has
      been waiting on — it is the first time anything closes that loop end to end.
- [ ] **A battle ending is not an error.** When the live battle closes, the window's state returns
      to "No clan battle is running right now" (`WatchState.NoBattle`), not to
      `ShapeNotUnderstood` or any other failure state.
- [ ] **A new battle's counter reset does not produce a false alert.** When the next battle starts
      and points drop to near zero, confirm the low number is sent as observed (never smoothed or
      skipped) and that no catastrophic-rate alert fires from the drop. The host is what protects
      against this (a decrease reads as an unmeasurable window, not a negative rate) — this row is
      confirming that protection actually holds against a real reset, not just the fixture in
      `ReportPolicyTests`/`ScoreWatchTests`.

## Needs a running RoRoRo host, no battle required

- [ ] **The host going away mid-run holds reports and says so**, and nothing is queued. Start
      Score Watch with RoRoRo running, quit RoRoRo, wait past a poll interval, confirm the window
      says RoRoRo is not running and polling continues. Relaunch RoRoRo and confirm exactly one
      report goes out per account on the next cycle — not a backlog of everything missed while it
      was down (`ScoreWatch` deliberately keeps no queue for this).
- [ ] **Declining a capability at reinstall stops reporting** and names the capability. Verified
      live (F4): RoRoRo's Plugins page has no per-capability toggle — its only control is
      **Remove**, which deletes the consent record and the install directory outright. To exercise
      this, remove Ur Score from Plugins, reinstall it, and decline `host.metrics.report` at the
      consent sheet. Start Score Watch and confirm the window shows `WatchState.Rejected` naming
      `host.metrics.report` specifically (not a generic "something unexpected went wrong") and
      says there is no re-grant — remove and reinstall to be asked again. Repeat declining
      `host.queries.accounts` instead and confirm that is named too, not swallowed by the generic
      handler (see `ScoreWatch.RunOnceCoreAsync`'s two `PermissionDenied` catches).
- [ ] **A wrong clan name reads as a wrong clan name**, not as a network failure or an empty clan.
      Set `clanName` in settings.json to something that does not exist and confirm the window's
      detail line names the clan as sent (so a typo is visible), distinct from "could not reach
      the clan data" and from "no battle running."
- [ ] **The rule button writes something the host actually reads.** Click **Add this rule to
      RoRoRo**, confirm it, then trigger a report cycle (Test now, or wait for the next poll) with
      RoRoRo running and its log visible. Read `LocalFileMetricRuleSource`'s output carefully —
      it is **silent on a clean parse and only ever logs when something is wrong** (an unreadable
      file, a non-array root, an unknown rule kind, a bad row). So "parsing it without complaint"
      means confirming the *absence* of a `LocalFileMetricRuleSource` line in RoRoRo's log after
      the rule is added and a report lands, not the presence of a success message — there is no
      success message. If a complaint *does* appear, that is the finding: it means the JSON Ur
      Score wrote is not something the host's rule reader accepts, and our own tests checking our
      writer against our own reader missed it. This is the only step that proves the host agrees
      with what we wrote; the unit tests only prove we agree with ourselves.
- [ ] **The backup is where it says.** With RoRoRo's `metric-rules.json` already holding at least
      one other rule, click **Add this rule to RoRoRo**. Confirm a `metric-rules.json.ur-score-backup`
      appears beside the original in the same folder, and that it holds the file's content from
      immediately before the add (the other rule intact, the new one absent).
- [ ] **Copy diagnostics pastes something useful** into a message, with no credential in it. Click
      **Copy diagnostics**, paste the clipboard somewhere visible, and confirm it names the clan,
      metric id, poll interval, host version (or "not connected"), the user-agent string, and the
      last ~40 trail lines — and that nothing in it is a `.ROBLOSECURITY` value, a pipe token, or
      any other account secret. (There should be none to find — Ur Score never holds one — but the
      point of this row is checking, not assuming.)

## Notes for whoever runs this list

- Every row above traces to a specific behavior in the code, named in parentheses or by class —
  that is deliberate, so a failing row points at what to go read rather than just "it didn't work."
- The rate shown in the dashboard is a display figure computed from Ur Score's own last two polls,
  not the rate RoRoRo's rule judges (spec §6.1). The two disagreeing during the battle rows above
  is expected, not a sign either is broken.
