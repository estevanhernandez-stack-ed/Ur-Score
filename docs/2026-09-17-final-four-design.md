# Final four: membership, claims and board layout

Status: design draft, 2026-09-17. The owner subsequently authorized the lightest backlog item; S1-F.1's bounded last-read conflict explanation is implemented below. Remaining designs are not approved for implementation.

Covers V3-S.20 (roster membership), S1-F.1 (claim explanations), V3-S.9 (snap-grid editing), and S1-14.11 (consistent panel heights). Delivery gates remain in the [wave plan](plans/2026-09-17-visible-backlog-waves.md). S1-F.1 is implemented; the other three backlog items remain open. Earlier desktop smoke remains pending.

## Decisions recorded

The owner selected these directions during design discovery:

- Persist own-account roster membership across restarts.
- Reject overlapping moves/resizes; keep other panels fixed.
- Preserve the canvas at narrow widths, with horizontal scrolling.
- Current membership requires roster confirmation; former membership requires prior confirmation followed by a successful complete roster exclusion. Old points alone are historical contributions. Failed refreshes never imply departure; conflicting roster inclusions remain an explicit discrepancy.
- Current/former affects grouping and explanations only for now. Preserve existing recording/reporting claim rules.
- On 2026-09-17, the owner requested a between-battle summary of the last battle's end and each clan's result, with evidence-based historical comparisons. Exact presentation and comparison rules remain proposals below.
- Reuse responses wherever possible; bounded supporting helpers or fixtures may be built to verify the request contract. Pagination is a possibility to investigate, not a verified API requirement. Failed or incomplete reads retain eligible last-known evidence rather than establish departures.

The recommendations below are proposals, not additional owner decisions. Grid implementation remains after the September 19 battle. No release, installation, or live-data changes are authorized by this draft.

## Membership and contributions

### Evidence and proposed rule

Today [AccountsModel](../src/UI/Setup/AccountsModel.cs) finds membership in contribution rows, and [LiveBoard](../src/Board/PanelModels.cs) also uses those rows for source chips. This cannot reliably identify members who have not contributed or who moved clans.

Proposed rule: a complete, successful roster read establishes observed membership independently of scores. A member without points stays a member; an old contributor does not become a current member merely because their points remain.

The smallest discriminating fixture is one own account on the roster without contributions, and another with contributions but absent from the roster. Membership and score assertions must differ without generating a report or a fabricated zero.

### Current and former clans

Proposed account/source states:

| Evidence | Presentation | Meaning |
| --- | --- | --- |
| Fresh complete roster includes account | Current clan | Confirmed in this source's latest usable roster. |
| Previously confirmed membership, then a fresh complete roster excludes account | Former clan | A departure was observed; the exact departure time is unknown. |
| Contributions exist but membership was never confirmed | Historical contributions | Evidence of points, not proof of former membership. |
| Cached inclusion after restart or a failed refresh | Last known clan, with checked time | Not currently confirmed. Never imply a departure. |
| Cached departure after restart or a failed refresh | Former clan, last checked time | Retain the observation without implying it is fresh. |
| No roster declaration | Membership unavailable | Contributions may still be displayed, explicitly as contributions. |
| Missing, malformed, incomplete or failed roster, no cache | Membership unknown | Not an empty roster and not an absence claim. |
| Successful complete empty roster | No own accounts found | Valid absence evidence for this source only. |

If two fresh rosters include the same own account, show both as reported current memberships with a discrepancy note. Do not infer that one is former from Main status, claim ownership, response arrival order, or points. The owner approved this distinction; exact presentation remains to be reviewed.

If an account returns to a former clan, a fresh inclusion makes it current again. Proposal: keep the latest transition per account/source, not an unbounded membership timeline. Do not say "not in any added clan" until all relevant enabled sources have complete fresh coverage; unsupported or failed sources prevent that conclusion.

Use the recipe's group vocabulary outside the clan recipe. Source role (Main, Mine, Watch), observed membership, contribution history, and recording/reporting ownership remain separate. One shared classification should drive My accounts, Setup > Your accounts, and source-chip membership cues. A saved source role is never changed automatically.

### Persisted evidence

Persist only own-account membership evidence, source identity, recipe identity/revision and input identity, observation time, and the latest confirmed inclusion/departure needed for the states above. Never persist a foreign roster, foreign identifiers, foreign names, or foreign contribution values. Do not store raw responses or secret input values in cache keys.

Proposals requiring approval before implementation:

- Use a separate versioned membership cache, not the score book or board file; atomic writes and isolated corruption recovery.
- Treat cached data as last known until refreshed in this session. A failed read never replaces successful evidence with an empty result.
- Define a freshness deadline from existing source cadence and source-provided timestamps; the exact rule is still open. Stopped and disabled sources cannot imply current confirmation indefinitely.
- Invalidate evidence when the source inputs or relevant recipe declaration change. Filter loaded evidence against currently available own accounts before display or use. Do not purge merely because the host account query failed.
- Apply the approved 30-day retention and cleanup policy below. Storage format and remaining implementation details still require design approval.
- Older builds can ignore the separate cache. Re-upgrade must validate identities/revisions and treat retained evidence as stale; no migration of claims into it.

### Approved retention: 30 days

Status: owner approved the 30-day retention and cleanup policy on 2026-09-17. This approval does not authorize production implementation or approve the complete design. Thirty days provides continuity across short breaks without keeping an indefinite clan history. This is a retention limit, not a freshness guarantee.

Keep at most one membership record per own account/source/configuration identity, not a list of visits. Apply these rules using UTC observation times:

| Evidence | Retention deadline | What refreshes it |
| --- | --- | --- |
| Confirmed inclusion | Last confirmed inclusion plus 30 days | A new complete, successful roster inclusion only. |
| Observed departure from retained confirmed membership | First observed exclusion plus 30 days | Nothing while the account remains absent. Rejoining replaces it with confirmed inclusion. |
| No confirmed membership | No former-membership record | A successful absence can support the current read's classification, but cannot create former membership. |

Expire at or after the deadline, before classification or display, even if disk cleanup has not run. Prune expired records on load and during existing refresh/save activity; do not add polling for cleanup. Reading the cache, restarting, failed refreshes, repeated exclusions and old contribution rows never extend a deadline. Validate loaded timestamps; implausible future evidence must not become fresh or extend retention indefinitely. Exact clock-skew tolerance remains an implementation-design detail.

After expiry, show membership as unknown unless a current usable roster establishes it. A later exclusion cannot reconstruct a former relationship from expired evidence or contribution rows. Expiration changes membership evidence only: it never deletes score-book history, changes source roles, clears claims or modifies reporting.

Cleanup rules:

- Explicit source deletion or recipe removal removes the associated cache records. Relevant input/roster-declaration changes invalidate and remove old-configuration records.
- Remove an account's records after an authoritative successful host account listing confirms removal. A failed, denied or cached account-list response is not removal evidence. Only currently recognized own accounts may be displayed or used.
- Disabling a source or stopping reads keeps unexpired evidence as last known; it does not refresh it. Re-enabling requires a usable roster read for fresh confirmation.
- Cleanup writes use the same atomic cache-save path. A failed write must not allow logically expired or invalidated records back into use; reapply filtering on every load.

Focused acceptance examples: inclusion survives restart on day 29 as last known and expires exactly on day 30; a departure on day 5 expires on day 35 despite daily exclusions; a return on day 20 replaces former status and starts an inclusion deadline at day 50; a failed host listing retains eligible records, while authoritative account removal purges them. Verify that each case leaves score history and reporting/claim behavior unchanged.

### Recipe and read contract

[RecipeEngine](../src/Recipes/RecipeEngine.cs) currently has no roster result, stops before fetching when no stats are selected, and can continue an idle read to retrieve Past data. The clan roster path and whether its response is complete are not yet verified. No schema field or API path is approved here.

Before implementation, verify a trusted fixture or documented response for active, idle, empty and malformed rosters. Design an optional recipe roster declaration with completeness rules. Reuse an already fetched response where possible; preserve cadence and do not add an independent polling loop. An idle score result may carry successful membership without becoming a successful score read.

Approved decision, 2026-09-17: enabled roster-capable sources refresh membership even when no stats are selected, within the existing cadence and Start/Stop controls. Use an explicit roster-only result with no metric recording/reporting. This does not enable a disabled source, start polling while stopped, select any stats, or introduce an independent polling loop. Existing manual-read controls retain their meaning. Request count, roster schema and idle dependency behavior still need fixture verification before implementation.

Focused acceptance: with zero tracked stats, an enabled roster-capable source updates own membership during an otherwise permitted read without creating score-book entries, fabricated values, claims or reports. Automatic reads remain stopped when polling is stopped and exclude disabled sources. A failed roster-only read retains eligible last-known evidence; a successful one must not overwrite score snapshots with empty metrics. Recipes without roster support retain their existing no-stats behavior, and group-list recipes remain excluded from account matching.

Watch sources may compare roster IDs with own IDs, but must still never record or report account metrics. Keep own-membership comparison separate from the account map used for reporting. Group-list recipes remain excluded from account matching. Filter before exposing roster results to snapshots, diagnostics, logs, copying or persistence. Recipe changes during a read must prevent stale results from being applied to the new configuration.

### Pagination and shared responses

Verify the actual API's request parameters, roster path, continuation signal, completion guarantee and any snapshot/version marker before choosing recipe fields. Do not invent a cursor or assume that a short or empty page proves completion. A complete traversal also needs a consistency guarantee: if pages can change underneath the read, absence is not reliable without an API-supported snapshot or another verified consistency rule.

Proposed first implementation contract: accumulate own-account matches privately and publish a new membership snapshot only after a complete, usable traversal. An account missing from an early page is not absent. On a later-page failure, repeated cursor, inconsistent snapshot, malformed page, cancellation or traversal limit, keep eligible prior evidence and label the attempted refresh incomplete; do not commit a partial empty roster or mark departures. This deliberately postpones new positive confirmations from partial reads as well.

Bound page count, elapsed time and response sizes within the existing read budget. Honor transport rate limits and cancellation; never retry pages indefinitely. Exact bounds depend on documented API limits and remain open. Deduplicate own IDs across pages and discard foreign IDs immediately after comparison. Group-list endpoints remain excluded from account matching.

Reuse a fetched clan response for roster, battle headline, contribution and Past extraction when those fields actually coexist. Fetch additional pages only when required by the verified contract. Share within one read/configuration/authentication context, not across unrelated sources or credentials. Membership failure must not discard independently valid score data; membership success must not disguise a failed score read. Roster-only mode must still avoid score-book writes and reporting.

If needed, build an isolated request-shape probe and synthetic fixture harness before production changes. Output only field names, types and pagination/completeness findings; never write raw live payloads, foreign IDs/names/values, credentials or opaque cursor values to logs or fixtures. Use invented identities for tests. Do not read installed app data or increase production polling to investigate the API.

Discriminating fixtures: an own member appears only on page two; all pages complete with no match; page two fails; a cursor repeats; pages disagree on snapshot identity; duplicate members occur; and a valid complete roster is empty. Only the complete consistent cases may establish absence. Verify request counts and that extracting multiple result types does not refetch the same response.

## Between-battle results

This is an owner-requested addition to the design scope, not a completed backlog item or an approved implementation. Keep the idle status, but pair it with useful per-clan history rather than treating the absence of a running battle as the absence of information.

### Evidence available and missing

The [clan recipe](../recipes/pet-sim-99-clan-battle.recipe.json) already requests the active battle's StartTime/FinishTime, extracts clan Place/Points, and reads Past from data.Battles in the clan response. [BookPeriod](../src/Book/BookLine.cs) supports optional end times and stored headlines. However, the engine's PastPeriodReading carries no per-battle start/end time. A past battle entry's chronology, finality, end timestamp and history completeness still need API evidence; dictionary order, a book write time or a local record called "final" alone cannot prove an official final result.

Distinguish a globally idle battle schedule from a running battle this clan has not joined. The latter is not "the battle ended." Identify the most recent completed battle using verified metadata, not key sorting or JSON order. If ordering cannot be established, label the record as a saved battle result rather than claiming it is the latest.

### Proposed presentation and comparison rules

Within the existing clan-focused panel, propose a compact result block containing battle name, ended date/time when known, clan place, points and at most one relevant historical comparison. Exact placement awaits UI review. Illustrative copy:

- "No clan battle running."
- "Last battle ended September 16 at 18:00." Use the actual verified end time in the user's display timezone, never the first idle read's timestamp. A scheduled end that is not confirmed must be labeled scheduled.
- "Finished 1st. Best recorded finish." For an equal earlier best, use "Joint-best recorded finish."
- "5th recorded first-place finish." Count this clan's distinct completed battles, not reads or account ranks.

Compare each clan with its own history, scoped by stable clan identity, compatible game/recipe semantics and battle identity. Switching inputs or recreating a source must not mix unrelated clans or count the same battle twice. Resolve corrected/reimported results deterministically before comparison; define result precedence before implementation.

Use only valid confirmed final placements: positive integer ranks, lower is better. Exclude running, provisional, missing and invalid results. A last-seen standing can be shown as "Last recorded standing," but cannot establish a victory or best finish. Retained past results survive roster-cache expiry and clan membership changes because they belong to score history, not membership evidence.

Scope claims to available history: "best recorded" and "5th recorded" unless complete lifetime coverage is verified. Do not imply a new record from a single known result or absent earlier coverage; show the result and history coverage instead. Treat ties explicitly. Default comparisons use finishing place, not raw points across battles whose scoring may differ. Cross-clan comparisons and point-based records need a separate comparability rule and are not assumed here.

When the end time, finality or result is unknown, say so or omit the unavailable field; do not invent a date, zero points or rank. A failed refresh retains a labeled last-known result, not a freshly confirmed result. With no stats selected, display already recorded battle history where available, but roster-only reads still cannot create metric history or reports. Any new headline-only recording policy would require separate approval.

Proposed insight priority: a repeated first-place result may show the recorded-win count; otherwise show a new or joint-best recorded finish when supported. Avoid multiple competing badges. Exact wording and priority remain subject to owner review.

### Acceptance and remaining evidence

Test active-to-idle transition, app startup after a battle ended, running-but-not-joined, verified end versus unknown/scheduled end, unordered past entries, provisional versus confirmed results, first available result, better/worse/tied finishes, fifth distinct first-place result, duplicate reads/imports, corrected results, changed source inputs and incomplete history. Include an idle summary alongside a failed roster refresh to prove the two states remain independent.

Before implementation, verify the endpoint metadata needed for dates/finality/history coverage and determine whether the existing book stores enough provenance. Prefer existing clan headlines and history; any added recipe/book schema needs explicit compatibility approval. No new metrics, claim transfers, foreign-account persistence or extra polling loop follow from this summary feature.

## Claim explanations

### Bounded implementation: S1-F.1

On 2026-09-17 the owner requested implementation of the lightest remaining item. The selected scope is last-read conflict explanations in Diagnostics and copied diagnostics. This supersedes the live-owner-query proposal for this backlog fix, without changing claim policy or implementing roster grouping.

TryClaim returns the rejecting owner under its existing lock; RecipeWatch captures own-account conflicts in that read's snapshot before recording/reporting. Diagnostics names the own account and configured owner, using a generic fallback for a removed owner. A later read replaces the evidence; expiry does not rewrite an older snapshot's history. Viewing/copying Diagnostics never queries or refreshes claims. Text explicitly describes the last read, not current ownership, membership or successful sending. No claim persistence or new expiry clock is introduced.

Validation: 46 focused tests, Release warnings-as-errors build and 1,281 full tests passed with isolated outputs under obj/claim-diagnostics-validation. Coverage includes rejection, exact expiry boundary, transfer, per-read reset, no-book operation, own-only evidence, removed owners/accounts, recipe mismatch and redacted copy output. Default-output WPF build remains unresolved. Coordinated desktop wrapping, theme and clipboard smoke is pending. The live-owner presentation below remains a future proposal, not shipped behavior.

### Broader presentation proposal

[AccountClaims](../src/Core/AccountClaims.cs) currently stores a source and last claim time for each recipe/account. Its caller supplies the expiry window. There is no read-only diagnostic query.

Proposed Diagnostics presentation, using only own-account names and configured source names:

- Membership: "Current clan: B. Former clan: A."
- Contributions: "Battle contributions remain in A."
- Recording source: "A currently holds the recording claim. B's duplicate contribution was skipped."
- No active claim: "No source currently holds the recording claim."

These are illustrative states, not a promise that every example occurs together. Only show a skipped-read explanation when a real rejection was observed; ownership alone does not prove a skip. A claim is not proof that sending succeeded. Keep the existing last-read send result separate.

Approved initial scope: explain existing claim policy without changing it. A roster transition must not claim metrics, force transfer, refresh expiry, change Send, or bypass ReportPolicy.SendAsync. Diagnostics must not mutate claims. Expiry must use the same effective window as reporting, including the exact boundary; do not add an independent guessed diagnostic timeout. Decide how to obtain that authoritative window at the caller boundary before implementing the read-only query.

The owner selected grouping and explanations only for now. Current-clan priority for recording/sending is outside this scope; any later proposal needs separate approval and duplicate/history tests.

## Fixed board canvas

Superseded by V3-S.9's rulings, closed 2026-09-21, and the board-chrome spec, 2026-09-23
(`docs/2026-09-23-board-chrome-design.md` §4): the owner chose **push, not stay-put** and **reflow as today plus
drag, not free placement**, both the other way from the independent column/row/span coordinates this section
proposes. The board stays a 12-column grid in reading order; a drag or a resize reflows the panels after it,
there is no free positioning, and dragging is header-only (`docs/backlog.md`, V3-S.9).

### Geometry and interaction

Today [BoardLayout](../src/Board/BoardLayout.cs) derives placement from list order, changes spans at width thresholds, and computes row heights from content. Ordinary cards can stop short of their slot. Proposed geometry stores each panel's column, row, width span and height span independently of logical reading order.

- Retain 12 canonical columns as the starting proposal. Choose the canvas minimum width, row unit and per-panel minimum dimensions through an isolated layout mock before approval; do not treat current breakpoints as a final specification.
- Narrow windows scroll horizontally; they never rewrite coordinates or collapse the board into a stack. Keep edit controls reachable and bring the focused panel into view.
- Preview the snapped bounds during dragging and corner resizing. Reject collisions and out-of-bounds horizontal positions; release on an invalid target restores the original bounds. Other panels never move.
- Use a clear themed outline plus an accessible valid/invalid status, not color alone. Preserve existing theme resources, typography, automation identities and focus conventions.
- Propose keyboard move/resize modes with arrow-key grid steps, Enter to accept and Escape to cancel the gesture. Retain board-level Done/Cancel semantics. Exact shortcuts and controls await an interaction mock.
- Keep logical reading order explicit and deterministic. A spatial move must not silently change screen-reader order; decide a deliberate reorder command if needed.
- Grid row/column calculations use device-independent units. Monitor/DPI changes affect rendering and viewport, not saved geometry.

### Consistent heights and overflow

Panels with the same row and height span have identical outer bounds. Explicitly taller panels occupy more rows. Content must not grow a row or move neighbors during refresh.

Proposed overflow policy: keep panel header/actions visible; give tables/history an internal scroll area; wrap text within the content width. Each panel type needs a measured minimum that accommodates its error/empty states and controls at agreed DPI. Do not solve overflow by clipping controls or shrinking all text. Pop-out content may size to its independent window; returning restores its reserved board footprint.

Adding and duplicating panels should choose a deterministic free slot without moving existing panels. Removing a panel leaves the space empty. Source-following starter updates and pop-out returns need explicit collision cases; never compact the board silently.

### Persistence and rollback gate

[BoardsFile](../src/Board/BoardsFile.cs) currently saves an unversioned JSON array with order and Span/Tall. Older writers would discard unfamiliar coordinate fields. Simply adding coordinates to the existing file is not a rollback strategy.

Recommended direction for review: a distinct versioned board document with one-time migration, preserving an untouched legacy copy. Once migrated, use one authoritative document; do not silently merge edits from an older app. Detect legacy-file changes on re-upgrade and offer an explicit choice before overwriting either version. Final filename, version, conflict UX and backup lifecycle remain open.

Migration must preserve IDs, settings, logical order, starter-following identity, and pop-out rectangles. Derive initial positions deterministically at a documented canonical width; map Span/Tall to approved minimum heights without losing panels. Repair invalid coordinates locally and place colliding panels in deterministic free slots, retaining a recovery copy. Unknown future formats must not be overwritten as empty boards.

Rollback must explain that the older app sees the preserved pre-migration layout, not later grid edits. Test interrupted migration, repeated upgrade, rollback/re-upgrade, corrupt documents, duplicate IDs, collisions and disk-write failure before any installed-data migration.

## Acceptance and next checkpoint

Membership fixtures must cover active/idle reads, member without points, former contributor, return to a clan, simultaneous reported memberships, no roster support, valid empty roster, incomplete/malformed/failed roster, restart cache, stale evidence, zero tracked stats, Watch, group lists and mid-read recipe changes. Assert own-only persistence/copy/log behavior and no membership-triggered report, score, claim or source-role mutation.

Claim tests must cover actual conflict evidence, expiry boundary, refresh, transfer, removed sources, restart and diagnostic reads that cannot prolong a claim. Keep sent-success evidence independent from ownership.

Grid checks must cover fixed neighbors, valid/invalid move and resize, keyboard parity, gesture and board cancellation, deterministic add/duplicate, starter changes, pop-out return, save/restart and migration. Coordinated visual acceptance must include narrow horizontal scrolling, focus visibility, long text, empty/error states, all panel types and multiple DPI settings.

Next checkpoint: verify the roster contract and request path, then review an isolated grid interaction mock and explicit migration proposal. Current/former semantics, explanation-only reporting scope, the 30-day retention/cleanup policy and roster refresh with no selected stats are approved directions, not approval of the complete design. Approve each design before coding its production slice. Live roster validation and earlier desktop smoke remain separate gates.
