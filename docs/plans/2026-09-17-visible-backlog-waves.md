# Visible backlog completion in waves

Status: execution started by the owner's "begin" on 2026-09-17. Waves 1-3 are implemented; their coordinated desktop smoke is pending. The owner authorized advancing Waves 2 and 3 with earlier smoke still pending, and explicitly approved the Wave 3 duplication restriction. Later design and release approval gates still apply.

## Scope and baseline

Complete the 15 remaining user-visible items across both sections of [the backlog](../backlog.md), not every technical backlog item. V3-S.13 (themed tooltips) is implemented and excluded from these 15; its desktop hover smoke remains outstanding.

Main-section baseline: 99 OPEN, 105 FIXED, 7 GONE; 11 visible items. Alerts-section baseline: 56 OPEN, 33 FIXED, 2 GONE; 4 visible items. Keep these counts separate and report the combined visible remainder after each wave.

Plan baseline: 1,217 passing tests. Latest verified suite after Wave 3: 1,260 passing tests and Release warnings-as-errors build using isolated artifacts. The default WPF build issue remains unresolved. Existing worktree changes must be preserved; inspect current code and changes again before each item. Backlog descriptions and the September 15 remediation plan contain stale assumptions.

This plan defines ordering and acceptance, not a release version or a replacement for the historical execution records. Existing decisions remain constraints unless explicitly revised with the owner.

## Delivery rules

- Implement one bounded item at a time within each wave. Add a focused regression check and run it immediately after the first substantive change.
- Keep model decisions testable outside WPF; use STA/UI Automation coverage where rendering or accessibility is the behavior being fixed.
- Preserve recipe-driven terminology, live theme resources, automation IDs, own-account privacy, and the single reporting gate. No additional polling or new persisted foreign-player data.
- Waves 1-3 preserve data formats. Any later schema or persistence change needs an approved design, compatibility tests, and a rollback strategy before implementation.
- Run the Release warnings-as-errors build and full test suite at each completed item, using an absolute artifacts directory under obj. Do not claim the default build issue is fixed by that workaround.
- At each wave boundary, run the relevant desktop smoke with the owner's coordination. Smoke scripts can take over the desktop and manipulate live app data; do not run them unattended against the installed setup.
- Update backlog status only with evidence. Keep smoke gaps explicit; do not label an untested visual behavior verified because its model tests pass.
- No automatic commits, branches, pushes, version bumps, releases, or installation. Each needs authorization. Preparing a wave is not permission to deploy it before the battle.

## Wave 1: Small visible fixes

Five items. Low algorithmic risk; isolated UI and documentation changes. Expected visible remainder: 10.

| Item | Work | Acceptance |
| --- | --- | --- |
| AC-B2.18 | Give the My accounts SENT indicator a meaningful accessible representation tied to the account and last-read send state. | UI Automation exposes the account and sent status only when applicable; failed/stopped reads do not announce a send. Verify with Narrator. |
| AC-3.4 | Apply the existing failure styling to orphaned alert results. | Failed orphaned results use the live magenta resource; nonfailures retain their appropriate style; theme switching updates both. |
| AC-6.11 | Correct the extra gap between the two muted lines in Setup > Your accounts. | Spacing matches neighboring page rhythm at normal and increased DPI; wrapped text remains readable without overlap. |
| S2-P.18 | Replace the font-dependent drag glyph using the existing design direction for a small drawn grip. | Grip renders independently of glyph fallback; mouse dragging, themed tooltip, automation identity, and keyboard move controls still work. Recheck the adjacent S2-P.3 automation concern, but do not silently close it without evidence. |
| S2-F.7 | Audit today's README and CHANGELOG against shipped boards, tabs, gallery, pop-outs, and boards.json behavior; fill only remaining gaps. | Each workflow and storage description is accurate; release history distinguishes shipped and unshipped changes. Existing correct content is retained. |

Primary surfaces: MyAccountsPanel.xaml, AlertsPage.xaml, AccountsPage.xaml, PanelFrame.xaml, README.md and CHANGELOG.md.

Exit: focused UI/model tests, full gates, coordinated Setup/My accounts/edit-tools smoke. Include hover checks for the already-implemented V3-S.13. This is the first reviewable delivery checkpoint, not a mandatory release.

## Wave 2: Settings recovery and unusual input

Three items. Expected visible remainder: 7.

| Item | Work | Acceptance |
| --- | --- | --- |
| S2-FR.2 | Expose a preselected switched-off Profile stat source honestly in the form. Keep the already-fixed board explanation. | Test enabled-only, mixed, all-off, and saved-off cases; the selected source is visible and its off state explained. Saving never silently switches source or enables reads. |
| S2-FR.1 | Let a Past periods panel recover when its saved stat disappeared and no stat remains ticked. Revalidate the earlier proposed no-account-stat option against current forms. | The user can save headline/history-only content without inventing or enabling a stat; ordinary new-panel defaults remain unchanged; cancel and remove still work. |
| AC-2.2 | Bound display of extreme hand-edited thresholds and make the Change form's validation usable. | Very large finite values, unsupported exponents, and non-finite input yield concise text and actionable validation. No overflow, enormous control, silent numeric clamping, or automatic rewrite of unrelated rules. Valid ordinary thresholds still round-trip. |

Primary surfaces: PanelForms, PanelSettingsWindow, AlertCards and existing rule validation/formatting helpers.

Exit: table-driven edge-case tests and coordinated settings smoke at narrow width. Existing saved selections and alert files must survive unchanged unless the user explicitly saves an edit.

## Wave 3: Board and window lifecycle

Three items. Higher risk: window messages, saved state, and first-run behavior. Expected visible remainder: 4.

| Item | Work | Acceptance |
| --- | --- | --- |
| S2-F.8 | Revisit the accepted empty-starter duplication behavior with the owner before changing it. Recommended: disable Duplicate only for an empty source-following starter, retaining the existing first-save rule. | An approved decision is recorded; a rejected action cannot drop a tab or write state; duplicating populated starters and user-created boards remains correct. If current behavior is retained, explicitly record that disposition rather than call it fixed. |
| S2-8.3 | Make failed score-book loading a coherent recoverable window state, including saved pop-outs. | Inject a failed load, verify no phantom popped-out slots, retry successfully, and restore each expected window once. Failure must not erase saved pop-out state. |
| S2-8.2 | Distinguish an explicit return-to-board action from app-wide or external shutdown. Review the earlier delayed-close proposal against actual message ordering before selecting an implementation. | Test explicit return, Alt+F4, main-window close, external close ordering, repeated close, and restart. External shutdown preserves pop-out placement; explicit return does not reopen it. No timing sleeps in unit tests; inject scheduling if needed. |

Primary surfaces: BoardEdits/BoardButtons, BoardWindow startup and pop-out lifecycle, AppServices.LoadBookAsync, and the existing window smoke scripts.

Exit: deterministic lifecycle tests plus an isolated, coordinated restart/external-close smoke. Do not send taskkill or close messages to the owner's normal session. Confirm saved board data before and after the walk.

## Wave 4: Roster membership and claim explanations

Two related items: V3-S.20 and S1-F.1. A design checkpoint precedes implementation. Expected visible remainder: 2.

Design discovery is recorded in the [final-four draft](../2026-09-17-final-four-design.md). The owner selected persistent own-account membership, roster-evidenced current/former grouping with existing claim rules unchanged, rejected grid overlaps and a horizontally scrollable fixed canvas. The 30-day membership retention/cleanup policy and roster refresh with no selected stats were approved on 2026-09-17. Roster-only reads respect existing cadence and Start/Stop controls and never record or report metrics. Recipe schema/request details, grid dimensions and migration remain unapproved. This draft also covers Wave 5 and does not authorize implementation.

1. Specify the contract for membership independent of points. Confirm how the recipe declares its roster and how an idle battle response still permits that roster read. Keep absent roster, empty roster, failed read, and stale membership distinct.
2. Approve compatibility and privacy behavior: recipes without roster support, own-account filtering, watch sources, any persistence decision, and no extra reporting or unbounded polling. Foreign roster IDs are compared against own IDs and discarded, never logged, copied, or stored.
3. Implement engine/snapshot membership first with fixtures, then the shared classification used by chips, My accounts, and Setup > Your accounts. Membership alone must not create a score or a report, change the saved source role, or claim an account's metrics.
4. Surface claim ownership in Diagnostics using only own-account identities and the owning source. Keep observed membership separate from the source allowed to record/report points. Claims still expire and transfer under the existing rules.

Acceptance matrix: active battle, idle battle with roster, own member with no contribution, contributor no longer on roster, no roster support, failed or stale roster, multiple source membership, accounts absent from all successfully read added rosters, watch sources, claim conflict/expiry/transfer, and recipe update during a read.

Owner scope addition, 2026-09-17: investigate possible roster pagination and reuse clan responses; supporting isolated request/fixture helpers may be built as needed. Between battles, show the last battle's end and each clan's result, with historical comparisons when evidence supports them. The [draft](../2026-09-17-final-four-design.md#between-battle-results) proposes recorded-best and recorded-win-count wording, complete/consistent pagination before departure inference, and failure handling that retains last-known evidence. API fields, completeness/finality guarantees, comparison details and presentation are not yet verified or fully approved. This addition does not change claim policy, authorize production changes or close a backlog item.

Primary surfaces: clan recipe and recipe schema if required, RecipeEngine, RecipeWatch, AccountClaims, snapshots, LiveBoard.ChipRole, My accounts, AccountsModel, DiagnosticsModel.

Exit: approved design, privacy/reporting regression coverage, both grouping screens consistent, and owner-coordinated live verification between battles and during an active battle when available. Fixture success does not close a live verification gap. Do not rush this cross-layer change into the September 19 battle setup.

Wave 4 bounded execution, 2026-09-17: at the owner's request to implement the lightest remaining item, S1-F.1 now exposes actual last-read claim conflicts in Diagnostics and copied output. AccountClaims returns the rejecting owner atomically; own-only snapshot evidence preserves existing expiry/reporting behavior. This implements historical explanations, not the proposed live-owner query or roster grouping. 46 focused tests, Release warnings-as-errors build and 1,281 full tests passed using obj/claim-diagnostics-validation. Diagnostics desktop wrapping/theme/clipboard smoke remains pending. Three visible backlog items remain; roster and grid design gates still apply.

## Wave 5: Snap-grid board editing

Two items: V3-S.9 and S1-14.11. Implementation remains after the September 19 battle, as already agreed. Expected visible remainder: 0, subject to the explicitly accepted S2-F.8 decision.

1. Approve a dedicated layout design: coordinates, spans, minimum sizes, collision handling, live drop outline, corner resizing, keyboard equivalents, narrow-window behavior, and monitor/DPI changes. Unmoved panels must not shift merely because another panel is resized.
2. Decide old-board migration, persistence versioning, invalid-position repair, and rollback before writing a new layout format. Preserve panel identities, settings, order semantics needed for accessibility, and pop-out placements.
3. Resolve S1-14.11 in this design instead of introducing a temporary row-stretch fix that the new layout discards. Define equal card bounds for panels sharing a grid row/height, and deliberate behavior for explicitly different heights.
4. Implement and test pure layout/placement first; then pointer and keyboard interaction; then persistence and migration; then WPF integration and pop-outs.

Primary surfaces: BoardLayout, PanelGrid, BoardWindow.Editing, panel-frame edit tools, board persistence and migration, and layout smoke coverage.

Exit: old boards load without lost panels; drag/resize/cancel/save/restart work; other panels stay put; collision and narrow-window rules are predictable; keyboard users can perform equivalent edits; no clipped/overlapping text across agreed DPI and window sizes. Owner accepts the actual editing experience, not just the tests.

## Wave 1 execution record

- [x] AC-B2.18: account text automation name carries last-read SENT status; STA peer test and existing sent-state tests pass.
- [x] AC-3.4: orphaned alert results use the failure flag and dynamic palette resources; state transitions and palette replacement tested.
- [x] AC-6.11: compact 4-pixel explanatory gap, 12 pixels before table; markup/wrapping contract tested.
- [x] S2-P.18: font-independent six-dot grip, named Label peer, existing mouse handler and keyboard alternatives preserved. S2-P.3 also closed by direct peer coverage.
- [x] S2-F.7: current README audited and supplemented; missing shipped board release summaries restored; new changes kept under Unreleased.
- [ ] Coordinated desktop checks: Narrator sent/unsent rows, orphaned failure/success colors and theme changes, Setup notes at normal/increased DPI, grip dragging and keyboard moves, and V3-S.13 tooltip hover.

Each code item passed a focused check, Release warnings-as-errors build, and full suite using isolated artifacts under obj. Final code suite: 1,221 passed. No release, installation, or live data manipulation performed. Main counts are now 96 OPEN / 109 FIXED / 7 GONE; Alerts counts 53 OPEN / 36 FIXED / 2 GONE. The row-level recount corrected an existing one-item undercount of fixed main-section items (212 total, not 211). Ten visible items remain, in addition to this wave's pending smoke verification.

## Wave 2 execution record

- [x] S2-FR.2: non-blocking Profile stat source warning; enabled, mixed, all-off and saved-off choices tested without source mutation. 38 form tests; full suite 1,225 passed.
- [x] S2-FR.1: explicit no-account-stat option for Past periods, retaining existing null-stat persistence, removed-stat refusal until chosen, reopened selection, and new-panel defaults. 58 form/board-file tests; full suite 1,227 passed.
- [x] AC-2.2: compact round-trip extreme values, immediate existing-limit validation, explicit replacement, unchanged ordinary formatting and non-finite remove-only behavior. 89 alert-card/rule-file tests; full suite 1,237 passed.
- [ ] Coordinated narrow-width settings smoke: off-source warning clears on enabled selection; Past periods recovery, Cancel, Remove and reopen; extreme alert replacement and Cancel leave unrelated rules untouched.

Each item passed its Release warnings-as-errors build using absolute isolated artifacts under obj. Final output: obj/wave2-threshold-validation. No default-output build fix, release, installation, or live data manipulation. Main counts: 94 OPEN / 111 FIXED / 7 GONE; Alerts counts: 52 OPEN / 37 FIXED / 2 GONE. Seven visible items remain across both sections, plus the explicitly pending smoke checks.

## Wave 3 execution record

Owner decision, 2026-09-17: disable Duplicate only for empty source-following starters. This supersedes the accepted F4 behavior for S2-F.8; it does not alter the first-save format or populated/user-board duplication.

- [x] S2-F.8: shared BoardEdits eligibility guards both duplication and BoardButtons. Rejection returns the unchanged list; populated starters and user-created boards remain eligible. 78 focused checks; full suite 1,245 passed.
- [x] S2-8.3: gate panel construction, grid visibility and pop-out synchronization on successful reader loading. Existing failure/Retry view retained; BookLoader failure injection verifies retry and one successful application. Production wiring fenced. 69 focused checks; full suite 1,247 passed.
- [x] S2-8.2: mark explicit Return and native SC_CLOSE as return intent; bare external closes preserve placement and suppress reopening for that session. Shutdown is marked before saving an edit draft. Closed-external window positions remain eligible for saving. A late book load cannot open Setup or start reads during shutdown.
- [x] Deterministic close-order, repeat-close, return and restart-eligibility tests, plus hidden test-owned WPF windows receiving SC_CLOSE and WM_CLOSE, without timers or sleeps. Final 74 focused checks; full suite 1,260 passed.
- [ ] Coordinated application smoke: empty-starter menu, failed book load with saved pop-outs, successful retry restoring each window once, explicit Return/Bring back/Alt+F4, both external close orders, and restart. Capture saved board data before/after in an isolated setup. Hidden-window message tests are not evidence that the full installed workflow was walked.

Each item passed its Release warnings-as-errors build using absolute isolated artifacts under obj. Final output: obj/wave3-close-validation. Saved formats are unchanged. No deployment, installation, commit, push, or live-data manipulation. Main counts: 91 OPEN / 114 FIXED / 7 GONE; Alerts remain 52 OPEN / 37 FIXED / 2 GONE. Four visible items remain: Wave 4 roster/claim explanations and Wave 5 snap-grid/row heights. Their existing design and post-battle approval gates still apply.

## Completion and handoff

- [ ] Wave 1 complete and smoke evidence recorded.
- [ ] Wave 2 complete and saved-state behavior verified.
- [ ] Wave 3 complete, including the explicit S2-F.8 decision.
- [ ] Wave 4 design approved, implemented, and live evidence/gaps recorded.
- [ ] Wave 5 design approved after the battle, migrated safely, and owner-accepted.
- [ ] Final docs/backlog reconciliation across both sections; no stale claims of missing features.
- [ ] Final integrated build/test/smoke report and release candidate prepared for explicit approval.

Wave counts are planning checkpoints, not permission to mark an unresolved design decision or missing live check complete. If new visible defects emerge, record them separately and re-estimate the remainder rather than silently widening a wave.

The unresolved default-output WPF build is a separate release-readiness risk. Isolated builds allow development to proceed; before a release, either verify the actual packaging path independently or address the build issue as an explicitly scoped task. This plan does not absorb the remaining technical backlog.
