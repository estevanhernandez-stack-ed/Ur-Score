# Releasing Ur Score

A release is a version tag on `master`. `.github/workflows/release.yml` builds and publishes it; the steps
around the tag are ours. Nothing here is automatic unless it says so.

## Before the tag

1. **Everything is merged.** Work lands through a PR with the `test` check green. Merging, tagging and
   releasing wait for the owner's word.
2. **Tell the RoRoRo session** (`rororoblox-*` in `ListAgents`, one lane per repo) that a tag is coming:
   the catalog moves with it (step 8), and the shared working tree must not switch under it.
3. **Bump the version in three places that must agree** (the workflow refuses a tag where they don't):
   - `Ur-Score.csproj` `<Version>`
   - `manifest.json` `"version"` (leave `minHostVersion` and `contractVersion` alone unless the host
     contract changed; `minHostVersion` 1.28.0.0 is the host that introduced `host.metrics.report`)
   - `CHANGELOG.md`: rename `## Unreleased` to `## X.Y.Z - YYYY-MM-DD` (local date). This section **is the
     release page**, so write it for players: what they will notice, in their words. The workflow refuses a
     tag with no section, and the `test` check runs the same script for the current manifest version.
4. **Build Release and check the build's own exit code**, not a grep of its output:
   `dotnet build Ur-Score.csproj -c Release`. An `MSB3027`/`MSB3021` copy failure means a running Ur
   Score holds `bin\Release`; the code compiled, but close it and rebuild before calling it clean. `CS5001`
   on a first build is the known flake (V3-S.39): build once more.
5. **Commit on `master`:** `chore(release): X.Y.Z — <what it is, in a line>`, noting that
   `minHostVersion` and `contractVersion` stay where they are.

## The tag

6. `git tag -a vX.Y.Z -m "Ur Score X.Y.Z"`, then `git push origin master` and `git push origin vX.Y.Z`.
7. **Watch the release workflow** (`gh run watch <id> --exit-status`). It checks that the tag, manifest and
   csproj match, runs the tests, builds `plugin.zip` with `build/build-plugin.ps1`, writes the notes with
   `build/release-notes.ps1`, and publishes the release with `plugin.zip`, `manifest.json`,
   `manifest.sha256`, `install.ps1` and every recipe in `recipes/`. The release stays a draft until every
   file is on it, so `releases/latest` never points at a release without `plugin.zip`.

## After the tag

8. **Bump the RoRoRo catalog**, or the marketplace never offers the update. In the ROROROblox repo:
   - set `latestVersion` for `626labs.ur-score` in `docs/store/plugins-catalog.json`, and commit
   - upload it to **the release marked Latest**, not a tag named from memory:
     `gh release list --limit 5` shows which, then
     `gh release upload <that tag> docs/store/plugins-catalog.json --clobber`
   - push `main`
   - **verify the live file**, not the one on disk:
     `curl -sL https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/plugins-catalog.json`
     must show the new version.
   If the RoRoRo session is online, ask whether it wants to do this itself.
9. **Install it on the owner's PC** when asked: download `plugin.zip`, `manifest.sha256` and
   `install.ps1` from the release into one folder, quit RoRoRo and Ur Score, run `install.ps1`. Check the
   installed `626labs.ur-score.dll` version names the tagged commit. Never run the plugin's exe to "check its
   version": it has no such option and starts the app.

## Fixing a release page afterwards

`pwsh build/release-notes.ps1 -Version X.Y.Z -OutPath notes.md`, then
`gh release edit vX.Y.Z --notes-file notes.md`. Edit the CHANGELOG first if the notes are wrong: the page
should never say something the CHANGELOG doesn't.
