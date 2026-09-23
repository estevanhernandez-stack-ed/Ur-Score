# Board chrome: a status chip, reading on open, Arrange, and undo

Testing is done, and the board's top bar still carries the controls a tester needed. The loudest button on the
board is **Start**, filled cyan, for something most players decide once. Reading state is said three times (the
button's word, the LiveDot, the StateLine). **Test now** sits beside Setup as if it were a debug tool, when it is the
only whole-board "read now". Drag-to-move and edge-drag resize (PR #13) replaced the arrows and the owner likes them,
but they live behind an **Edit board** mode that makes every panel taller on the way in, locks the tabs, and has no way
out except Done.

This spec redesigns that chrome. It comes from an eight-agent read-only study on 2026-09-23 (five lenses: top bar,
drag mechanics, conventions, accessibility and tests, design history; one synthesis; two critics) and the owner's
rulings the same day, given in chat in answer to it.

It ships as four PRs, each with its own release, in this order: **§2 fixes → §3 top bar and reading → §4 Arrange →
§5 undo**. One plan covers all four.

## 1. Rulings, 2026-09-23

In the owner's words where he gave them. They continue as **BC** numbers so later docs can cite them.

- **BC1. Reading starts on open, by default, on new and existing installs.** "reading start on open by default, yes.
  Yes for both." This **supersedes A31** (`docs/plans/2026-09-15-alerts-card.md`, "something a user enables after
  setting up"). It does not touch A33: RoRoRo launching the plugin stays `autostartDefault: "off"` in
  `manifest.json`. Design decision 3 of the first spec is about RoRoRo launching the plugin, not about the window
  reading while it is deliberately open, so it is not in conflict.
- **BC2. One status chip replaces Start/Stop and Test now as buttons; clicking it shows status and holds Pause.**
  "One click status chip is enough, if you can click on it and it tells you status and it has a pause button within
  it. I don't want it to pause when they click to check the status though. That'd be silly." So a click on the chip
  **never pauses**. While reading has run this session it opens a card, and Pause is a button inside the card. The
  one exception is the "▶ Start reading" state (§3.2): nothing has run yet, so there is no status to check, and the
  click starts reading. That exception was part of design part 1 as approved.
- **BC3. The line under the bar shows only when it matters.** Chosen from three options (keep always / show only
  when it matters / remove).
- **BC4. The gate stays, as Arrange, until undo exists.** "Keep Arrange as a button until Undo exists. Yeah, let's
  just see how long it takes for us to add Undo." Always-on drag is re-asked after §5 ships and has been used, not
  before.
- **BC5. Arrange gets Cancel and Esc.** Answered yes to "May edit mode get Cancel/Esc?". This **revises R8**
  (`docs/plans/2026-09-14-score-book-stage-2.md`) in one respect only: R8's "there is no Cancel" goes; "closing Ur
  Score while editing saves the draft" stays.
- **BC6. Undo.** Board-level undo with a toast, built so the owner can try it in his own edit-mode game work.
- **BC7. A pause lasts until Ur Score closes.** Never saved; the next open reads again (BC1). Proposed in design
  part 1 and approved with it.
- **BC8. The close prompt asks only while reading and sending.** Proposed in design part 1 and approved with it.

Rulings that stand and that this design respects: **V3-S.9** (push-and-reflow, header-only drag, no free
placement), **R8's close-saves**, **R19** (no pop-out while editing), **V3-S.13** (every popup themed), the
**Following** rule (`src/Board/Following.cs`: a starter tab follows your sources until its first saved change).

## 2. Stage 0 — fix what the study found (PR 1)

These are defects in the system the owner already likes, and §4 rewrites the same paths, so they go first. None
needs a ruling.

1. **A corner drag on a tall panel makes it short.** `EndResize` passes `grip.From.Height`, the whole two-row slot,
   to `BoardLayout.RowsFor` (`src/UI/BoardWindow.Editing.cs:364`), which answers 2 only at 1.5× its row argument
   (`src/Board/BoardLayout.cs:209-210`). Released at the same height, a tall panel lands short. Fix: compare against
   the height of the panel's own **first row**, which `PanelGrid` exposes beside `Cells` (a `RowHeightAt(index)`
   from the same `RowHeights` call its arrange uses). Not `(From.Height − Gap) / 2`: rows differ in height. Test: a
   corner drag on a tall panel over rows of **unequal** heights, released at its own height, stays tall.
2. **The keyboard route loses focus after one move.** `FocusPanelLater` calls `Focus()` on the board child
   (`Editing.cs:308-313`), a `UserControl` that is not focusable, and nothing in `src/UI/Panels` makes it so. Fix:
   focus the first focusable descendant of the moved panel (⋯ when it shows, else ✕ Remove), found by panel id from
   the draft after the move, reusing `FocusToolLater`. `FocusedPanelIndex` already walks up from any descendant.
   The smoke helper `Focus-Panel` (`tools/smoke/uia-board.ps1:179-184`) does the same instead of `SetFocus` on the
   UserControl.
3. **Grips on popped-out slots.** `ShowGrips` and `GripAt` skip `PoppedOutSlot` cells (`Editing.cs:238, 315-323`).
4. **Dead tools.** `MoveEarlier`, `MoveLater`, `Resize` leave `PanelTool` (`src/UI/Panels/PanelFrame.xaml.cs:10`)
   and their handlers (`Editing.cs:127-150`); `IsTallTick` leaves `BoardEdits`; `BoardWindow.PopOuts.cs:217`
   focuses a tool that exists.
5. **✕ Remove is named per panel** ("Remove Battle race"), the way ⧉ and ⋯ already are
   (`PanelFrame.xaml.cs:112-114`); `PanelToolNamingTests` extended.
6. **Paper.** `README.md:160-163` stops describing the arrows; the comment at `src/UI/BoardWindow.xaml:58` names
   what the bar actually holds.

**Done means** `walk-board-editing.ps1` runs green. It has not run since 05436b5 retired the arrows, and by reading
it most likely fails at its first `Focus-Panel`. A red run is a blocker for this PR, not a note.

## 3. Stage 1 — the top bar and reading (PR 2)

### 3.1 Layout

```
LIVE         [ic] Battle │ Grind │ Alts  ⋯  + Board   ● Live   ⟳  Autumn · ends 3d 4h · next read 12m     [Arrange] [⚙ Setup]
PAUSED       [ic] Battle │ Grind │ Alts  ⋯  + Board   ❚❚ Paused ⟳  Autumn · ends 3d 4h                    [Arrange] [⚙ Setup]
              Paused. Nothing is read or sent, so phone alerts are off.
NOT STARTED  [ic] Battle  ⋯  + Board   [ ▶ Start reading ]  ⟳  Reads every 30m                           [Arrange] [⚙ Setup]
              Not started. The numbers on screen are from 2h ago.
TROUBLE      [ic] Battle │ Grind │ Alts  ⋯  + Board   ▲ Trouble ⟳  Autumn · ends 3d 4h · next read 2m     [Arrange] [⚙ Setup]
              CCGP: source unreachable
```

- The bar becomes a **Grid**, not a DockPanel: tabs | ⋯ | + Board | chip | ⟳ | period line (star) | right buttons.
  Tab order and screen-reader order follow what is seen (today Start is the first Tab stop though it sits on the
  right, because it is the DockPanel's first child). Reordering the DockPanel will not do it: `LastChildFill` would
  make the right group the fill child.
- **What gives way first when narrow:** the period line (it already trims with an ellipsis), then the tabs (they
  already scroll past their share). Never the chip or ⟳. `OnTopBarSizeChanged` / `TabStripShare`
  (`BoardWindow.xaml.cs:517-518`) are re-derived for the new groups. Checked at 1024 DIP (1280 px at 125%) with
  three tabs.
- The right group is **Arrange** and **Setup** only.

### 3.2 The chip

One button, kept as `x:Name="StartStopButton"` so the automation id survives. Its state comes from a new pure
`BoardText.ChipState(loaded, running, everStarted, starting, testing, unhealthy)` with its own tests, reading the
same inputs `BoardText.StateLine` does, so the chip and the line can't disagree.

| State | Look | Click |
|---|---|---|
| Live | `●` + "Live", cyan | opens the card |
| Paused | `❚❚` + "Paused", **amber** (paused silences phone alerts; it is the loudest non-first-run state) | opens the card |
| Trouble | `▲` + "Trouble", its own colour, not amber | opens the card |
| Not started this session | filled PrimaryButton, "▶ Start reading" | **starts reading** (there is no status worth a card yet, and this is the one moment Start deserves to be loud) |
| Starting | "Starting…", disabled | — |

- Its tooltip and `AutomationProperties.Name` say the state in words ("Reading is on. Press for status."). Colour is
  never the only signal: each state has its glyph.
- A clear hover and pressed state, so it reads as a control, not a label.
- It stays enabled exactly as `BoardButtons.For(...).StartStop` says; that gate is unchanged and still the only one.
- The window title gains " (Paused)" while paused, so a paused board shows on the taskbar and a second screen
  without anyone hovering.

### 3.3 The status card

A themed `Popup` (V3-S.13), anchored under the chip, `StaysOpen="False"`; Esc and a click away close it, and
focus goes back to the chip. Opening it changes nothing (BC2).

```
┌──────────────────────────────────────────────┐
│ ● Reading 3 sources.                         │
│   The numbers on screen are 2m old.          │
│                                              │
│   CCGP clan battle    read 2m ago · next 28m │
│   Alts                read 2m ago · next 28m │
│   Watch: Top clans    read 9m ago · next 21m │
│                                              │
│   Phone alerts: sending                      │
│                                     [ Pause reading ] │
└──────────────────────────────────────────────┘
```

- The first line is `BoardText.StateLine` exactly (so the card, the chip and the §3.4 line tell one story), then one
  row per enabled source (last read, next read, its unhealthy state if any), then whether alerts are going out
  (`BoardText.Sending` && running).
- One button: **Pause reading** / **Resume reading**, `AutomationId="PauseResumeButton"`, calling the existing
  `OnStartStopClick` path. Its enabled state is `BoardButtons.For(...).StartStop`.
- The DetailLine's problems (boards problem, press note, host down, budget warning) show in the card too.

### 3.4 The line under the bar (BC3)

StateLine and DetailLine collapse when there is nothing to say. They **show** when any of these hold:

- paused, not started, starting, or trouble (an unhealthy source, host down);
- the DetailLine has anything (boards problem, press note, budget warning);
- **any panel is drawing numbers from the score book** — A41's sentence ("The numbers on screen are …") is a ruling,
  and it stays on screen whenever it applies.

Healthy and live with fresh numbers, the board gets that line's height back. Paused reads: "Paused. Nothing is read
or sent, so phone alerts are off." The empty-state copy (`BoardText.cs:271`, "Start and Test now are off") is
reworded to match.

### 3.5 ⟳ read now

A small flat button between the chip and the period line, kept as `x:Name="TestNowButton"`. Tooltip "Read every
source now (F5)". A Window `KeyBinding` for **F5** goes through the same `For(...).TestNow` guard
(`BoardWindow.xaml.cs:604`). Disabled while a read-now is in flight. Pop-out windows don't take F5; that is
deliberate (they have no ⟳).

### 3.6 Reading on open (BC1, BC7)

- **Migration, one key.** `Settings` gains `SettingsVersion`. `Settings.Load` sees it missing or below 2, sets
  `StartOnOpen = true` once, writes version 2, and never touches it again. Every existing `settings.json` holds an
  explicit `false` only because `Load` wrote `Defaults` on first launch (`Settings.cs:38-42`), not because the
  player chose it — the owner ruled existing installs flip too. A player who deliberately unticked it before 0.6 is
  indistinguishable and flips as well; they untick it once more. The record default becomes `true` for new files.
  `StartOnOpen` still never travels in a setup pack (`SetupPack.cs:18`), and the version doesn't either.
- **Setup › Recipes:** the checkbox reads "Start reading when Ur Score opens", ticked; its help text and
  `AutomationProperties.Name` (`RecipesPage.xaml:94-100`) drop "Off unless you turn it on" and "Stop still stops it"
  for "Pause still pauses it, until Ur Score closes."
- **The first-run gap.** `OpenOnTheBookAsync` decides once (`BoardWindow.xaml.cs:128-147`) and skips when Setup opens
  on the first-run page, so a new player's board never starts. Now, while this session has never started, the same
  `BoardButtons.StartsOnOpen` is asked again when Setup closes and when the enabled sources go from none to some.
  Tests for both triggers in `BoardButtonsTests` (and AC-B2.15's window wiring gains one).
- **Pause is not saved.** `EverStarted` and `StoppedAt` are session state already.
- **Smoke guard.** `tools/smoke/uia.ps1:304-351` forces `startOnOpen:false` into seeded folders and checks it; it
  also writes `settingsVersion: 2`, so the migration never turns a seeded walk folder on (V3-S.43's hazard).
  Not optional.

### 3.7 Closing (BC8)

`OnClosing` asks only when `Running && BoardText.Sending` (`BoardWindow.xaml.cs:769-780`); its wording about phone
alerts stays. A paused or never-started board closes without claiming alerts will stop.

### 3.8 What the smoke walks see

- `Start-UrScore`'s "ready" signal (`uia.ps1:223-224`) waits on the chip (`StartStopButton`) being enabled, as now.
- A walk that pressed Start/Stop to **stop** now invokes the chip, then `PauseResumeButton` in the card. A walk that
  pressed it to **start** on a fresh folder still invokes the chip (the Start-reading state starts directly).
  Walks read the chip's Name, not the old "Start"/"Stop" text: `walk-starter-board.ps1:80-95, 135-137`,
  `walk-alts.ps1`, `walk-score-book.ps1`, `window-smoke.ps1` are re-read and fixed.
- `TestNowButton` keeps its id, is still an invokable Button, still goes disabled during a read: the five Test-now
  walks (`walk-alts.ps1:58-60, 109-111`, `walk-score-book.ps1:21-26`, `walk-pop-outs.ps1:42`,
  `window-smoke.ps1:60-66`, `walk-starter-board.ps1:86-95`) pass unchanged — that is this PR's acceptance check.

## 4. Stage 2 — Arrange (PR 3)

### 4.1 Entering

**Edit board** becomes **Arrange**, `x:Name="EditBoardButton"` kept. Arranging never stops reading, the chip, or ⟳.

### 4.2 The banner, not a button swap

While arranging, the §3.4 line's space holds a banner instead, so the board below doesn't move:

```
│ [ic] Battle │ Grind │ Alts  ⋯  + Board   ● Live  ⟳  Autumn · ends 3d 4h                        [Arrange] [⚙ Setup] │
├─ Arranging "Battle" · drag a header to move · drag an edge or corner to resize · ←/→ move ── [+ Add panel] [Cancel] [Done (3)] ┤
┌╌ ✥ CLAN STANDING (MAIN) ╌╌╌╌╌╌╌╌╌ ⋯ ✕ ╌┐ ▌┌╌ ✥ BATTLE RACE ╌╌╌╌╌╌╌╌╌╌╌╌╌ ⋯ ✕ ╌┐
┆ ...                                    ┃ ▌┆ ...                                ┃     ┃ edge grip
┆                                        ◢  ┆                                    ◢     ◢ corner grip
└╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┘  └╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┘     ▌ drop caret
```

- `AddPanelButton` and `DoneButton` move into the banner with their ids; `uia-board.ps1:141-149` finds them there.
- **Done (n)** counts the changes in the draft (`BoardEdits` against `_draftBase`); plain "Done" at zero.

### 4.3 No layout jump

The `EditTools` row above each title (`PanelFrame.xaml:10-26`, shown at `PanelFrame.xaml.cs:116`) goes. Edit
affordances are drawn **inside the existing header**: a dashed outline, the SizeAll cursor over the header, a ✥
glyph at its start carrying `AutomationId="DragHandle"` (so `walk-board-editing.ps1:29` and
`walk-visible-fixes.ps1:225` keep their target, and `DragHandleTests` move to it), and ✕ in the tools where ⧉ sits
(⧉ is already hidden while editing, `PanelFrame.xaml.cs:122`). The ✥ glyph takes the same system drag threshold as
the header (`PanelFrame.xaml.cs:160-179`); today's six-dot grip starts on mouse-down with none. Panels keep their
height; `Editing.cs:110-112`'s relayout on entry has nothing to relayout.

### 4.4 Cancel and Esc (BC5)

- **Cancel** in the banner, `IsCancel="True"`, so **Esc** works wherever focus is (the grid's `PreviewKeyDown` alone
  would miss Esc on Done, + Add panel or the banner).
- With no changes, it just leaves. With changes, a themed `ConfirmWindow`: "Throw away your changes to this board?"
  — "your changes", not "your arrangement", because ⋯ panel settings changed while arranging are in the draft too
  (`BoardWindow.PanelSettings.cs:68-78`).
- Leaving sets `_draft = _draftBase = null` and redraws.
- Closing the window while arranging still saves the draft (R8, `Editing.cs:37-48`).

### 4.5 Tabs stay clickable

Clicking another tab while arranging runs `FinishEditing` (the same save Done does) and then switches. A failed save
keeps you on the board, as Done does today. `+ Board` and the tab menu (Rename, Duplicate, Delete) stay off while
arranging. `BoardButtons.For` changes accordingly: `Tabs: true` while editing; `AddBoard`, `RenameBoard`,
`DuplicateBoard`, `DeleteBoard`, `BoardMenu` stay `!editing`; a new `Cancel: editing`.

### 4.6 Keyboard

←/→ move and the existing size keys keep working on the focused panel (after §2.2 fixes them). The panel whose
button has focus draws a focus ring on its card, so "←/→ move" in the banner has a visible target.

### 4.7 Paper

A note under R8 in the stage-2 plan recording BC5. A "superseded by V3-S.9's rulings" banner on the "Fixed board
canvas" section of `docs/2026-09-17-final-four-design.md`. Copy: `BoardText.cs` (~275, "arrange them with Edit
board") and the README.

## 5. Stage 3 — undo (PR 4)

### 5.1 What is undone

Every board change already goes through one method, `ChangeBoard` (`BoardWindow.PanelSettings.cs:66-88`), in draft
and live mode: move, resize, add panel, remove panel, a panel's ⋯ settings. Before each change, it pushes the board
as it was — a `BoardDef`, an immutable record, so a snapshot is a reference.

- A **stack per board**, keyed by `BoardDef` id, **20 deep**, session only, cleared when that board is deleted.
- Pop-out-only changes are not pushed (`CarryPopOuts` doesn't count them as changes either).
- **Out:** board delete (it confirms already), rename, duplicate.

### 5.2 Inside and outside Arrange

- **Arranging:** Ctrl+Z pops the draft's own stack, one step at a time. On **Done**, the whole session collapses to
  **one** entry on the board's stack: the board as it was when Arrange was pressed. Cancel discards the draft stack.
  ✕ Remove only exists while arranging (§4.3: it is drawn in the panel's tools in place of ⧉, which is hidden while
  editing), so a Remove is covered by this bullet, not the one below.
- **Not arranging:** ⋯ settings changes save at once today (the live branch of `ChangeBoard`); each is now one
  entry. Ctrl+Z restores the snapshot through the same path, which saves (`SaveBoards`).

  *Corrected 2026-09-23, task 17: this bullet used to also say ✕ Remove saves at once outside Arrange. It doesn't —
  ✕ Remove is drawn only while arranging (§4.3) and was already covered by the Arranging bullet above.*

### 5.3 The toast

A themed toast at the bottom centre of the board: "Removed Battle race · **Undo**", "Arranged Battle · **Undo**",
"Changed Past battles · **Undo**". About 6 seconds; a new change replaces it; Ctrl+Z works whenever the board window
has focus, toast or not. It is a live region (`AutomationProperties.LiveSetting="Polite"`) so a screen reader hears
it; the Undo button is reachable by Tab while it shows.

### 5.4 Starter tabs

`Following.ToSave` re-follows a board only when it is drawn exactly as its starter draws it **now**
(`Following.cs:25-30`). An undo that restores that exact board makes the tab follow your sources again. If sources
changed between the edit and the undo, the restored board no longer matches, the tab stays unfollowed, and the
toast says so: "Restored, but Battle no longer follows your clans." Tested with a source change in between.

### 5.5 Failure

A failed save leaves the stack as it was and the toast says what failed (redacted, as every problem line is).

### 5.6 Then

After §5 ships and the owner has used it, the always-on question (BC4) is asked again with two candidates: hold a
header ~450 ms to pick it up without entering Arrange, or always-on header drag with an undo toast. Hover-only grips
were rejected in 17cbf91 and would need that ruling reversed.

## 6. Not doing

Drag to another tab, drag out to pop out, a whole-board ghost preview of the reflow: they conflict with R19 and the
single-board draft, and none answers what was asked. A lock toggle instead of a mode: it makes the fragile state the
default on a read-mostly window. A status chip whose click pauses: ruled out by BC2. A Pause item in a menu only:
the chip's card is the menu, and its button is visible to UIA once the card is open.

## 7. Testing

- Unit: `RowsFor` over unequal rows; `BoardText.ChipState` for every state; §3.4's show/hide rule;
  `BoardButtons.For` (Tabs while editing, Cancel) and `StartsOnOpen`'s two new triggers; `Settings` migration (missing
  version → on and v2; v2 with false → stays false; seeded smoke shape stays off); close-prompt gate; undo stack
  (depth, per board, Done collapse, Cancel discard, delete clears, pop-out changes not pushed, Following re-follow and
  the changed-sources case, failed save).
- UI (`WpfCollection`): the chip's name per state; the card opens without changing state; Esc on Done cancels;
  panels keep their height entering Arrange; ✕ Remove's name.
- Smoke: each PR's acceptance walk is named in its section — `walk-board-editing` (§2), the five Test-now walks and
  `Start-UrScore` (§3), `walk-board-editing` and `walk-visible-fixes` drag steps (§4). A 1024 DIP screenshot of the
  bar with three tabs in every chip state (§3).
- Counts before and after every run, and the exit code, not the "Passed!" line.

## 8. Risks

- **Reading more.** BC1 starts reads nobody pressed for. Budget warnings in the DetailLine (and the card) are the
  guard for players with many accounts; §3.4 keeps them on screen.
- **A stray chip click** can't pause (BC2); a stray **Pause** in the card is one click, amber everywhere, in the
  title bar, and in the line — and it ends when Ur Score closes.
- **Esc throwing away work** is behind a confirm whenever there are changes.
- **Smoke drift** is the largest cost of §3 and §4; each section lists the walks it touches, and a walk that isn't
  re-run green isn't done.
