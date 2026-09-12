# Changelog

All notable changes to RoRoRo Ur Score are documented here. Format roughly follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning follows [SemVer](https://semver.org/).

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
