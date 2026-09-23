# Board chrome Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the tester-era top bar with a status chip, start reading on open by default, turn Edit board into a
lighter Arrange mode with Cancel/Esc, and add board undo — in four PRs, each released.

**Architecture:** Pure decisions go in small static classes with unit tests (`BoardLayout`, `BoardButtons`,
`BoardText`, a new `StatusChip`, a new `BoardUndo`); `BoardWindow` and its partials only wire them to WPF. Every
board change keeps flowing through `ChangeBoard`, which is where undo snapshots are taken. The UIA smoke walks under
`tools/smoke/` are each stage's acceptance check.

**Tech Stack:** .NET 10 WPF (`net10.0-windows`), xUnit, PowerShell 5.1 UI Automation smoke walks.

**Spec:** `docs/2026-09-23-board-chrome-design.md` (rulings BC1–BC8). Read it before any task.

## Global Constraints

- Build then test, always: `dotnet build Ur-Score.csproj -c Release` then `dotnet test tests/Ur-Score.Tests.csproj`.
  `dotnet test` at the repo root runs NOTHING (no .sln). CS5001 on the app build is a known flake (V3-S.39): retry once.
- Record the test count before and after each task, and read the **exit code**, not the "Passed!" line: a test-host
  crash prints "Passed!" with a smaller total.
- WPF-touching test classes carry `[Collection(WpfCollection.Name)]`; controls needing app resources run inside
  `UiThread.RunInApp(...)`; STA threads are joined with `UiThread.Longest`, never unbounded.
- Every popup and dialog is themed (V3-S.13): brushes by `{DynamicResource …}`, questions through `ConfirmWindow.Ask`,
  never a stock `MessageBox`.
- Trail lines carry exception **type names only** (`ex.GetType().Name`), never messages. Problem lines shown on screen
  go through `_services.Redactor.Redact(...)`.
- Automation ids that smoke walks use are kept: `StartStopButton`, `TestNowButton`, `EditBoardButton`,
  `AddPanelButton`, `DoneButton`, `DragHandle`, `PanelSettingsButton`, `RemovePanelButton`, `SetupButton`.
- Copy is plain English, sentence case; no exclamation marks. Existing comment density: a `<summary>` on every
  non-trivial member saying why, citing the ruling (BCn, R8, A41 …).
- The checkout is shared with other Claude sessions: announce with `SendMessage` (find peers with `ListAgents`) before
  switching branches. Commit before leaving work uncommitted.
- Walks that move `%LOCALAPPDATA%\626labs.ur-score` aside must not run while a battle is recording; quit RoRoRo first
  and check `Get-Process ROROROblox.App` before AND after (a Discord Join can cold-start it).
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`; PR bodies end with
  `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.
- One naming deviation from the spec, on purpose: spec §3.2's `BoardText.ChipState` lives in a new `StatusChip` class
  (`src/UI/StatusChip.cs`), because `BoardText` is already 283 lines of lines and this is state, not a line.

## Review Focus

Most likely to bite a real player, each pinned by a test in the task named:

1. **A player who once unticked Start on open** gets it switched on again by the one-time migration (the owner ruled
   existing installs flip). A second load must leave their re-unticked choice alone. → Task 5, `MigrationRunsOnce`.
2. **A smoke walk's fresh or seeded folder must never start reading by itself**: a folder with no `settings.json`
   now defaults to on. → Task 9, the harness seeds `startOnOpen:false, settingsVersion:2` and the checker refuses
   anything else.
3. **Checking status while a read-now is in flight on a paused board**: the Start gate is closed then, but the chip
   must still open the card. → Task 6, `TheChipOpensWhateverTheStartGateSays`.
4. **Esc while the status card is open during Arrange** must close the card, not throw away the arrangement. → Task 8
   (the card marks Esc handled) and Task 13's walk step.
5. **Ctrl+Z while arranging with nothing undone in the draft** must not reach past the draft into the saved board's
   history (that would save a change behind Arrange's back). → Task 15, `WhileArrangingOnlyTheDraftIsUndone`.

---

# Stage 0 — fixes (PR 1, release 0.5.10)

Branch: `fix/drag-defects`, created from `docs/board-chrome-spec` so the spec and this plan travel with PR 1.

### Task 1: A tall panel's corner drag compares against its own first row

**Files:**
- Modify: `src/Board/BoardLayout.cs` (add `FirstRowHeight` beside `RowsFor`, ~line 210)
- Modify: `src/UI/Controls/PanelGrid.cs:82-107` (remember each cell's first-row height)
- Modify: `src/UI/BoardWindow.Editing.cs:27, 315-323, 352-368`
- Test: `tests/BoardLayoutTests.cs`

**Interfaces:**
- Produces: `BoardLayout.FirstRowHeight(PanelPlacement placement, IReadOnlyList<double> rows) : double`;
  `PanelGrid.FirstRowHeightAt(int index) : double`.

- [ ] **Step 1: Write the failing test** (append to `BoardLayoutTests`)

```csharp
    /// <summary>
    /// A corner drag on a tall panel, let go at the height it already had, kept it tall only if the comparison is
    /// with ONE row. EndResize compared it with the whole two-row slot, and RowsFor answers two only at 1.5 times
    /// its row argument, so the same height came back short. Rows of unequal height, because they are unequal on
    /// every real board and an average of the two would hide the mistake.
    /// </summary>
    [Fact]
    public void ACornerDragOnATallPanelLetGoAtItsOwnHeightStaysTall()
    {
        const double gap = 12;
        var placed = BoardLayout.Flow([new PanelSize(6, Tall: true), new PanelSize(6), new PanelSize(6)], 1400);
        var rows = BoardLayout.RowHeights(placed, [100, 180, 90], gap);
        var tall = placed[0];
        var slot = BoardLayout.CellHeight(tall, rows, gap);

        Assert.Equal(2, BoardLayout.RowsFor(slot, BoardLayout.FirstRowHeight(tall, rows)));

        // What the old code asked, which is the defect: the slot measured against itself is "one row".
        Assert.Equal(1, BoardLayout.RowsFor(slot, slot));

        // A one-row panel let go at its own height stays one row.
        var single = placed[1];
        Assert.Equal(1, BoardLayout.RowsFor(BoardLayout.CellHeight(single, rows, gap), BoardLayout.FirstRowHeight(single, rows)));
    }

    [Fact]
    public void APlacementPastTheRowsHasNoFirstRow() =>
        Assert.Equal(0, BoardLayout.FirstRowHeight(new PanelPlacement(0, 3, 0, 6), [120, 80]));
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet build Ur-Score.csproj -c Release; dotnet test tests/Ur-Score.Tests.csproj --filter "FullyQualifiedName~BoardLayoutTests"`
Expected: build error CS0117 `'BoardLayout' does not contain a definition for 'FirstRowHeight'`.

- [ ] **Step 3: Add `FirstRowHeight`** (in `BoardLayout`, directly after `RowsFor`)

```csharp
    /// <summary>
    /// The height of the first row a panel sits in: what a corner drag is measured against. Not the panel's slot,
    /// which for a tall panel is two rows and a gap, and measured against itself calls every tall panel short.
    /// Zero for a placement past the rows, which <see cref="RowsFor"/> reads as one row.
    /// </summary>
    public static double FirstRowHeight(PanelPlacement placement, IReadOnlyList<double> rows) =>
        placement.Row >= 0 && placement.Row < rows.Count ? rows[placement.Row] : 0;
```

- [ ] **Step 4: Remember it per cell in `PanelGrid`**

Add a field beside `_cells`:

```csharp
    private IReadOnlyList<double> _firstRows = [];
```

Add a member after `Cells`:

```csharp
    /// <summary>The height of the first row the panel at <paramref name="index"/> sits in, for a corner drag; 0 when there is none.</summary>
    public double FirstRowHeightAt(int index) => index >= 0 && index < _firstRows.Count ? _firstRows[index] : 0;
```

In `ArrangeOverride`, beside `var cells = new List<CellRect>(placements.Count);` add
`var firstRows = new List<double>(placements.Count);`, inside the loop after `cells.Add(...)` add
`firstRows.Add(BoardLayout.FirstRowHeight(placement, rows));`, and after `_cells = cells;` add `_firstRows = firstRows;`.

- [ ] **Step 5: Use it in the resize**

In `BoardWindow.Editing.cs` change the field (line 27) to

```csharp
    /// <summary>The panel being resized by its grip, which grip, the cell it started from and the height of its first row. Null when not.</summary>
    private (int Index, bool Corner, CellRect From, double RowHeight)? _resizing;
```

in `OnBoardMouseDown` set it with
`_resizing = (grip.Index, grip.Corner, BoardPanels.Cells[grip.Index], BoardPanels.FirstRowHeightAt(grip.Index));`
and in `EndResize` replace `BoardLayout.RowsFor(shown.Height, grip.From.Height)` with
`BoardLayout.RowsFor(shown.Height, grip.RowHeight)`.

- [ ] **Step 6: Build and run the whole suite**

Run: `dotnet build Ur-Score.csproj -c Release; dotnet test tests/Ur-Score.Tests.csproj; echo "exit $LASTEXITCODE"`
Expected: `exit 0`, total = before + 2.

- [ ] **Step 7: Commit**

```bash
git add src/Board/BoardLayout.cs src/UI/Controls/PanelGrid.cs src/UI/BoardWindow.Editing.cs tests/BoardLayoutTests.cs
git commit -m "fix(board): a corner drag measures a tall panel against one row, so letting go at its height keeps it tall"
```

---

### Task 2: A keyboard move keeps focus on the moved panel

**Files:**
- Modify: `src/UI/Panels/PanelFrame.xaml.cs` (add `FocusFirstTool`)
- Modify: `src/UI/BoardWindow.Editing.cs:278-313`
- Modify: `tools/smoke/uia-board.ps1:175-184` (`Focus-Panel`)
- Test: `tests/PanelToolNamingTests.cs` (a fence, in that file's style)

**Interfaces:**
- Produces: `PanelFrame.FocusFirstTool() : bool`; `BoardWindow.FocusPanelLater(string panelId)` (private).

- [ ] **Step 1: Write the failing fence** (append to `PanelToolNamingTests`)

```csharp
    /// <summary>
    /// A keyboard move put focus back with Focus() on the board child, a UserControl nothing makes focusable, so
    /// focus fell out of the board after one arrow and the next arrow moved nothing. It goes to the panel's first
    /// tool now, found by the panel's id after the redraw, the way every other tool hand-off already works.
    /// </summary>
    [Fact]
    public void AKeyboardMoveHandsFocusToAToolOfTheMovedPanelNotToThePanel()
    {
        var editing = File.ReadAllText(Path.Combine(RepoRoot(), "src", "UI", "BoardWindow.Editing.cs"));
        var frame = File.ReadAllText(Path.Combine(RepoRoot(), "src", "UI", "Panels", "PanelFrame.xaml.cs"));

        Assert.DoesNotContain("panel.Focus()", editing, StringComparison.Ordinal);
        Assert.DoesNotContain("FocusPanelLater(index", editing, StringComparison.Ordinal);
        Assert.Contains("FocusPanelLater(panel.Id)", editing, StringComparison.Ordinal);
        Assert.Contains("public bool FocusFirstTool()", frame, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Ur-Score.Tests.csproj --filter "FullyQualifiedName~PanelToolNamingTests"`
Expected: FAIL, `Assert.DoesNotContain() Failure` on `panel.Focus()`.

- [ ] **Step 3: Add `FocusFirstTool` to `PanelFrame`** (after `FocusTool`)

```csharp
    /// <summary>
    /// Focus on the first of this panel's own buttons that can take it — ⋯, else ✕ Remove, else ⧉ — for a board that
    /// has just redrawn the panel under a keyboard move. The panel itself is a UserControl and takes no focus.
    /// </summary>
    public bool FocusFirstTool() =>
        new Control[] { PanelSettingsButton, RemovePanelButton, PopOutButton }.Any(tool => tool.IsVisible && tool.Focus());
```

- [ ] **Step 4: Replace `FocusPanelLater` in `BoardWindow.Editing.cs`**

Replace the three calls `FocusPanelLater(index + step);`, `FocusPanelLater(index);`, `FocusPanelLater(index);` with
`FocusPanelLater(panel.Id);`, and replace the method (lines 304-313) with

```csharp
    /// <summary>
    /// Puts focus back in the moved panel once the grid has redrawn it, on its first tool: every edit rebuilds the
    /// grid and destroys the element that had focus, and the panel itself can't hold it. Found by id, not index, so
    /// it follows the panel to wherever the move put it.
    /// </summary>
    private void FocusPanelLater(string panelId) => Dispatcher.BeginInvoke(
        () =>
        {
            if (ViewOf(panelId) is not { } view) return;

            if (view is PoppedOutSlot slot) slot.FocusButton(Editing);
            else PanelFrame.Of(view)?.FocusFirstTool();
            view.BringIntoView();
        },
        DispatcherPriority.Loaded);
```

- [ ] **Step 5: Fix the smoke helper the same way** (`tools/smoke/uia-board.ps1`, replace `Focus-Panel`)

```powershell
# The panel is a UserControl and takes no focus; its first tool does, which is also where the app puts focus back
# after a keyboard move. So the walk focuses that tool and the arrow keys reach the board's handler from there.
function Focus-Panel($board, [string]$panelId) {
    $panel = Find-ByAutomationId $board $panelId
    if (-not $panel) { throw "no panel '$panelId' to focus" }
    $tool = @('PanelSettingsButton', 'RemovePanelButton') | ForEach-Object { Find-ByAutomationId $panel $_ } |
        Where-Object { $_ -and $_.Current.IsKeyboardFocusable } | Select-Object -First 1
    if (-not $tool) { throw "panel '$panelId' shows no tool that takes focus" }
    $tool.SetFocus()
    Start-Sleep -Milliseconds 250
}
```

- [ ] **Step 6: Build and run the suite**

Run: `dotnet build Ur-Score.csproj -c Release; dotnet test tests/Ur-Score.Tests.csproj; echo "exit $LASTEXITCODE"`
Expected: `exit 0`, total = before + 1.

- [ ] **Step 7: Commit**

```bash
git add src/UI/Panels/PanelFrame.xaml.cs src/UI/BoardWindow.Editing.cs tools/smoke/uia-board.ps1 tests/PanelToolNamingTests.cs
git commit -m "fix(board): a keyboard move hands focus to the moved panel's first tool, so the next arrow still moves it"
```

---

### Task 3: No grips on popped-out slots; the dead arrow tools go; ✕ Remove is named per panel

**Files:**
- Modify: `src/Board/BoardLayout.cs` (a pure `GripAt`)
- Modify: `src/UI/Controls/PanelGrid.cs:46-65` (delegate to it, add `NoGrips`)
- Modify: `src/UI/BoardWindow.xaml.cs:235-251` (`BuildPanels` sets `NoGrips`)
- Modify: `src/UI/BoardWindow.Editing.cs:115-191, 235-239`
- Modify: `src/UI/Panels/PanelFrame.xaml.cs:10-18, 81-92, 111-114`
- Modify: `src/UI/BoardWindow.PopOuts.cs:217`
- Modify: `src/Board/BoardEdits.cs:129` (delete `IsTallTick`)
- Modify: `src/UI/BoardText.cs` (add `RemovePanelName`)
- Test: `tests/BoardLayoutTests.cs`, `tests/PanelToolNamingTests.cs`, `tests/BoardEditsTests.cs:480-490`

**Interfaces:**
- Produces: `BoardLayout.GripAt(IReadOnlyList<CellRect> cells, double x, double y, IReadOnlySet<int> skip) : (int Index, bool Corner)?`;
  `PanelGrid.NoGrips : IReadOnlySet<int>`; `BoardText.RemovePanelName(string? title) : string`.
- `PanelTool` becomes `{ PopOut, Settings, ChooseAnother, DragStart, Remove }`; `PanelToolEventArgs` loses `Size`;
  `PanelFrame.FocusTool(PanelTool tool)` loses `tall`.

- [ ] **Step 1: Write the failing tests**

Append to `BoardLayoutTests`:

```csharp
    /// <summary>
    /// A popped-out panel's slot is a short card that refuses a resize (R19), so it shows no grips and a press over
    /// where they would be falls through to whatever is under it.
    /// </summary>
    [Fact]
    public void ASkippedCellOffersNoGrip()
    {
        CellRect[] cells = [new(0, 0, 200, 100), new(212, 0, 200, 100)];
        var corner0 = BoardLayout.HandlesFor(cells[0]).Corner;
        var corner1 = BoardLayout.HandlesFor(cells[1]).Corner;

        Assert.Null(BoardLayout.GripAt(cells, corner0.Left + 2, corner0.Top + 2, new HashSet<int> { 0 }));
        Assert.Equal((1, true), BoardLayout.GripAt(cells, corner1.Left + 2, corner1.Top + 2, new HashSet<int> { 0 }));
        Assert.Equal((0, true), BoardLayout.GripAt(cells, corner0.Left + 2, corner0.Top + 2, new HashSet<int>()));
    }
```

Append to `PanelToolNamingTests`:

```csharp
    [Fact]
    public void RemoveSaysWhichPanelItRemoves()
    {
        Assert.Equal("Remove Battle race", BoardText.RemovePanelName("Battle race"));
        Assert.Equal("Remove panel", BoardText.RemovePanelName("  "));
    }
```

Delete the `IsTallTick` test in `BoardEditsTests.cs` (the four `Assert` lines at 486-489 and their `[Fact]` method).

- [ ] **Step 2: Run to see them fail**

Run: `dotnet build Ur-Score.csproj -c Release; dotnet test tests/Ur-Score.Tests.csproj --filter "FullyQualifiedName~BoardLayoutTests|FullyQualifiedName~PanelToolNamingTests"`
Expected: build errors for `GripAt` and `RemovePanelName`.

- [ ] **Step 3: Move `GripAt` into `BoardLayout`** (after `HandlesFor`)

```csharp
    /// <summary>
    /// The panel whose resize grip is under (x, y), and which grip, or null for neither. The corner is tried before
    /// the edge because it sits inside the edge grip's span and would otherwise be unreachable. A cell in
    /// <paramref name="skip"/> — a popped-out panel's slot, which refuses a resize (R19) — offers neither.
    /// </summary>
    public static (int Index, bool Corner)? GripAt(IReadOnlyList<CellRect> cells, double x, double y, IReadOnlySet<int> skip)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (skip.Contains(i)) continue;

            var grips = HandlesFor(cells[i]);
            if (Holds(grips.Corner, x, y)) return (i, true);
            if (Holds(grips.Edge, x, y)) return (i, false);
        }

        return null;
    }

    private static bool Holds(CellRect rect, double x, double y) =>
        x >= rect.Left && x <= rect.Left + rect.Width && y >= rect.Top && y <= rect.Top + rect.Height;
```

In `PanelGrid`, replace `GripAt` and `Holds` with

```csharp
    /// <summary>Cells that show no grips: the popped-out slots (R19). Set by the board as it builds the grid.</summary>
    public IReadOnlySet<int> NoGrips { get; set; } = new HashSet<int>();

    /// <summary>The panel whose resize grip is under <paramref name="point"/>, and which grip; see <see cref="BoardLayout.GripAt"/>.</summary>
    public (int Index, bool Corner)? GripAt(Point point) => BoardLayout.GripAt(_cells, point.X, point.Y, NoGrips);
```

In `BoardWindow.BuildPanels`, after the loop, add
`BoardPanels.NoGrips = board.Panels.Select((p, i) => (p, i)).Where(x => x.p.PopOut is not null).Select(x => x.i).ToHashSet();`.

In `ShowGrips`, replace the grips line with

```csharp
        if (_hints is not null)
        {
            _hints.Grips = Editing
                ? [.. BoardPanels.Cells.Where((_, i) => !BoardPanels.NoGrips.Contains(i)).Select(BoardLayout.HandlesFor)]
                : [];
        }
```

- [ ] **Step 4: Delete the dead tools**

- `PanelFrame.xaml.cs:10`: `public enum PanelTool { PopOut, Settings, ChooseAnother, DragStart, Remove }`
- `PanelToolEventArgs`: drop the `size` parameter and the `Size` property:
  `public sealed class PanelToolEventArgs(RoutedEvent routedEvent, PanelTool tool) : RoutedEventArgs(routedEvent) { public PanelTool Tool { get; } = tool; }`
- `FocusTool(PanelTool tool, bool tall = false)` → `FocusTool(PanelTool tool)`; its summary loses the sentence about Tall.
- `BoardWindow.Editing.cs`: delete the `MoveEarlier`, `MoveLater` and `Resize` cases (lines 127-150);
  `FocusToolLater(string? panelId, PanelTool tool, bool tall = false)` → `FocusToolLater(string? panelId, PanelTool tool)`
  and `PanelFrame.Of(view)?.FocusTool(tool, tall)` → `PanelFrame.Of(view)?.FocusTool(tool)`.
- `BoardWindow.PopOuts.cs:217`: `FocusToolLater(def.Id, Editing ? PanelTool.Settings : PanelTool.PopOut);` — ⋯ is the
  panel's first tool while editing (Task 2).
- `BoardEdits.cs:129`: delete `IsTallTick`.

- [ ] **Step 5: Name ✕ Remove per panel**

In `BoardText`, after `ChooseAnotherName`:

```csharp
    /// <summary>As <see cref="PopOutName"/>, for ✕ in edit mode: five panels gave five buttons called "Remove panel".</summary>
    public static string RemovePanelName(string? title) =>
        string.IsNullOrWhiteSpace(title) ? "Remove panel" : $"Remove {title.Trim()}";
```

In `PanelFrame.UpdateTools`, after the `ChooseAnotherButton` line:
`AutomationProperties.SetName(RemovePanelButton, BoardText.RemovePanelName(title));`
and in `PanelFrame.xaml` delete `AutomationProperties.Name="Remove panel"` from `RemovePanelButton`. Add
`"RemovePanelButton"` to the `foreach` list in `TheFrameNamesAllThreeToolsFromTheHeadAndTheXamlFixesNone` and the
matching `Assert.Contains("AutomationProperties.SetName(RemovePanelButton, BoardText.RemovePanelName(title));", code);`.

Smoke: `Invoke-PanelTool ... 'RemovePanelButton'` finds by automation id, which is the x:Name, so the walks are unaffected.

- [ ] **Step 6: Build and run the suite**

Run: `dotnet build Ur-Score.csproj -c Release; dotnet test tests/Ur-Score.Tests.csproj; echo "exit $LASTEXITCODE"`
Expected: `exit 0`, total = before + 2 − 1.

- [ ] **Step 7: Commit**

```bash
git add src tests
git commit -m "fix(board): no grips on popped-out slots; the retired arrow tools are gone; Remove says which panel"
```

---

### Task 4: Paper, the board-editing walk green, and release 0.5.10

**Files:**
- Modify: `README.md:160-163`
- Modify: `src/UI/BoardWindow.xaml:58` (the top bar comment)
- Modify: `CHANGELOG.md`, `manifest.json:5`, `Ur-Score.csproj:10`
- Modify: `docs/backlog.md` (a row for the two defects, opened and closed FIXED, counted by rows)

- [ ] **Step 1: README** — replace the "**Edit board** opens a draft…" paragraph with

```markdown
**Edit board** opens a draft of the board on screen. Drag a panel by its header to move it; drag its right edge
to change its width, or its bottom-right corner to change its width and make it one or two rows tall. With the
keyboard, focus one of a panel's buttons and use Left and Right to move it, Ctrl+Left and Ctrl+Right to change its
width, and Ctrl+Up and Ctrl+Down for one or two rows. **✕** removes a panel. **Done** saves the draft; closing Ur
Score while editing saves it too. Panels flow in reading order, so a move or a resize can move the panels after it.
```

- [ ] **Step 2: The comment at `BoardWindow.xaml:58`** →
`<!-- The top bar (spec §8): tabs, ⋯ and + Board, then the period line; Start, Test now, Edit board and Setup on the right. -->`

- [ ] **Step 3: Run the board-editing walk** (RoRoRo quit; nothing recording)

Run (Windows PowerShell 5.1): `powershell -ExecutionPolicy Bypass -File tools/smoke/walk-board-editing.ps1`
Expected: every step `PASS`; the results line reports 0 failed. If step 3 or 4 fails, the keyboard route is still
broken: debug with superpowers:systematic-debugging before going on. **A red walk blocks this PR.**
Then run `walk-visible-fixes.ps1` (its step 2 drags by the grip) and record both results in the PR body.

- [ ] **Step 4: Version and CHANGELOG** — bump `0.5.9` → `0.5.10` in `manifest.json` and `Ur-Score.csproj`; add

```markdown
## 0.5.10 - 2026-09-23

### Fixed

- **A tall panel stays tall when you let go of its corner.** Dragging the corner of a two-row panel and letting
  go at the height it had made it one row; it is measured against one row now.
- **Moving a panel with the keyboard keeps going.** After the first Left or Right, focus fell out of the board and
  the next key moved nothing. It stays on the moved panel.
- **Popped-out panels show no resize grips**, since their slot can't be resized.
- **✕ says which panel it removes**, for screen readers.
```

- [ ] **Step 5: Backlog row** — add one row in `docs/backlog.md`'s open-and-you'd-notice section, FIXED the same day,
naming both defects and this plan; recount the Counts block by ROWS per its stated method (first bold marker).

- [ ] **Step 6: Suite, commit, PR, release**

```bash
dotnet build Ur-Score.csproj -c Release && dotnet test tests/Ur-Score.Tests.csproj; echo "exit $?"
git add README.md src/UI/BoardWindow.xaml CHANGELOG.md manifest.json Ur-Score.csproj docs/backlog.md
git commit -m "chore(release): 0.5.10 — a tall panel stays tall, keyboard moves keep going, no grips on popped-out slots"
```

Push, open the PR (base `master`), and after Este says merge, follow `docs/releasing.md` end to end — including the
RoRoRo catalog bump on the release marked Latest.

---

# Stage 1 — the top bar and reading (PR 2, release 0.6.0)

Branch: `feat/status-chip` from `master` after PR 1 merges.

### Task 5: Settings: reading on open by default, migrated once

**Files:**
- Modify: `src/Core/Settings.cs`
- Modify: `src/UI/Setup/RecipesPage.xaml:92-101`
- Test: `tests/SettingsTests.cs`

**Interfaces:**
- Produces: `Settings(bool ResolveNames = true, string? ActiveRecipe = null, bool StartOnOpen = false, int SettingsVersion = 0)`;
  `Settings.CurrentVersion = 2`; `Settings.Defaults` has `StartOnOpen = true, SettingsVersion = 2`;
  `Settings.Migrate(Settings) : Settings`.

- [ ] **Step 1: Rewrite the two A31 tests and add the migration tests** (in `SettingsTests`)

Replace `StartingFromOpenIsOffUntilYouTurnItOn` and `StartOnOpenRoundTripsUnderItsCamelCaseKeyWithoutDisturbingTheOthers` with

```csharp
    /// <summary>BC1 (2026-09-23, superseding A31): reading starts on open by default, for a new install.</summary>
    [Fact]
    public void ANewInstallReadsOnOpen()
    {
        Assert.True(Settings.Defaults.StartOnOpen);
        Assert.Equal(Settings.CurrentVersion, Settings.Defaults.SettingsVersion);
        Assert.True(Settings.Load(File()).StartOnOpen);
    }

    /// <summary>
    /// BC1, "yes for both": an existing install turns on too. Every settings.json written before 0.6 holds an explicit
    /// false only because Load wrote the defaults on first launch, so the file is migrated once, and saved.
    /// </summary>
    [Fact]
    public void AnExistingInstallIsTurnedOnOnceAndSaved()
    {
        Write("""{ "resolveNames": false, "activeRecipe": "pet-sim-99-profile", "startOnOpen": false }""");

        var loaded = Settings.Load(File());

        Assert.Equal(new Settings(false, "pet-sim-99-profile", StartOnOpen: true, SettingsVersion: Settings.CurrentVersion), loaded);
        Assert.Contains("\"settingsVersion\": 2", System.IO.File.ReadAllText(File()));
    }

    /// <summary>Review Focus 1: the migration runs once. A player who unticks it afterwards keeps it off.</summary>
    [Fact]
    public void MigrationRunsOnce()
    {
        Settings.Save(new Settings(StartOnOpen: false, SettingsVersion: Settings.CurrentVersion), File());

        Assert.False(Settings.Load(File()).StartOnOpen);
        Assert.False(Settings.Load(File()).StartOnOpen);
    }

    [Fact]
    public void StartOnOpenAndTheVersionRoundTripUnderCamelCaseKeys()
    {
        var saved = new Settings(ResolveNames: false, ActiveRecipe: "x", StartOnOpen: true, SettingsVersion: Settings.CurrentVersion);
        Settings.Save(saved, File());
        var json = System.IO.File.ReadAllText(File());

        Assert.Contains("\"startOnOpen\"", json);
        Assert.Contains("\"settingsVersion\"", json);
        Assert.DoesNotContain("\"StartOnOpen\"", json);
        Assert.Equal(saved, Settings.Load(File()));
    }

    /// <summary>A migrated file that can't be written still reads on open: the next open tries the write again.</summary>
    [Fact]
    public void MigrateIsPureAndIdempotent()
    {
        var old = new Settings(false, "x", StartOnOpen: false, SettingsVersion: 0);
        var once = Settings.Migrate(old);

        Assert.Equal(old with { StartOnOpen = true, SettingsVersion = Settings.CurrentVersion }, once);
        Assert.Same(once, Settings.Migrate(once));
    }
```

In `RoundTripsThroughDisk`, save and expect `new Settings(false, "pet-sim-99-clan-battle-points", SettingsVersion: Settings.CurrentVersion)`.
Any other test in this file that compares a loaded pre-0.6 shape to `Settings.Defaults` or to a literal record
now expects the migrated value (`StartOnOpen: true, SettingsVersion: 2`); change the EXPECTATION, never loosen the
comparison.

- [ ] **Step 2: Run to see them fail**

Run: `dotnet build Ur-Score.csproj -c Release; dotnet test tests/Ur-Score.Tests.csproj --filter "FullyQualifiedName~SettingsTests"`
Expected: build errors on `SettingsVersion`, `CurrentVersion`, `Migrate`.

- [ ] **Step 3: Implement** (replace the record header and `Load` in `src/Core/Settings.cs`)

```csharp
/// <summary>
/// What the user configures for Ur Score as a whole. …(keep the first paragraph)…
/// <para>
/// <c>StartOnOpen</c> is on by default and was switched on once for every existing install (BC1, 2026-09-23,
/// superseding A31). The constructor's defaults are what a file WITHOUT those keys means — off, version 0 — so an
/// old file is recognisable and <see cref="Migrate"/> can turn it on exactly once; <see cref="Defaults"/> is what a
/// new install writes. It is about this app's own window, not about RoRoRo launching the plugin, which is the
/// manifest's <c>autostartDefault</c> and stays off (A33).
/// </para>
/// </summary>
public sealed record Settings(bool ResolveNames = true, string? ActiveRecipe = null, bool StartOnOpen = false, int SettingsVersion = 0)
{
    /// <summary>2: BC1's migration has run. A file below it is from before 0.6.</summary>
    public const int CurrentVersion = 2;

    public static Settings Defaults { get; } = new(StartOnOpen: true, SettingsVersion: CurrentVersion);

    /// <summary>BC1: an install from before 0.6 reads on open from now on; once migrated, what the player chooses stands.</summary>
    public static Settings Migrate(Settings settings) =>
        settings.SettingsVersion >= CurrentVersion ? settings : settings with { StartOnOpen = true, SettingsVersion = CurrentVersion };

    // DefaultPath and Options unchanged.

    public static Settings Load(string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;

            if (!File.Exists(file))
            {
                // "Start it once and it creates the file" has to be true.
                Save(Defaults, file);
                return Defaults;
            }

            var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(file), Options) ?? Defaults;
            var migrated = Migrate(loaded);
            if (ReferenceEquals(migrated, loaded)) return loaded;

            try
            {
                Save(migrated, file);
            }
            catch (Exception)
            {
                // Reading on open is still what this install should do; the next open writes it.
            }

            return migrated;
        }
        catch (Exception)
        {
            // An unreadable file is not a reason not to start.
            return Defaults;
        }
    }
```

`SetupPack` keeps building `new Settings(ResolveNames, ActiveRecipe)` (version 0, off): it never reaches `Load`, and
`SetupMerge` lays only those two over this PC's record, so `StartOnOpen` and the version still never travel. Leave
both files alone; `SetupMergeTests` proves it.

- [ ] **Step 4: Setup › Recipes copy** (`RecipesPage.xaml`)

```xml
        <CheckBox x:Name="StartOnOpenBox" Content="Start reading when Ur Score opens"
                  Checked="OnStartOnOpenChanged" Unchecked="OnStartOnOpenChanged"
                  AutomationProperties.Name="Start reading when Ur Score opens" />
        <TextBlock Style="{StaticResource Muted}" Margin="24,4,0,0"
                   Text="On unless you turn it off. It takes effect the next time you open Ur Score. Reading happens only while the window is open, and Pause still pauses it, until Ur Score closes." />
```

Search the walks for the old label and update them: `grep -rn "as soon as Ur Score opens" tools/smoke`.

- [ ] **Step 5: Build, suite, commit**

Run: `dotnet build Ur-Score.csproj -c Release; dotnet test tests/Ur-Score.Tests.csproj; echo "exit $LASTEXITCODE"`
Expected: `exit 0`.

```bash
git add src/Core/Settings.cs src/UI/Setup/RecipesPage.xaml tests/SettingsTests.cs tools/smoke
git commit -m "feat(settings): reading starts on open by default, and every existing install is switched on once (BC1)"
```

---

### Task 6: The status chip's state, the card's lines, and when the lines under the bar show

**Files:**
- Create: `src/UI/StatusChip.cs`
- Modify: `src/UI/BoardText.cs` (`Paused`, `InTrouble`, `CardRows`, `AlertsLine`, `AsksBeforeClose`; StateLine's stopped branch)
- Test: create `tests/StatusChipTests.cs`; modify `tests/BoardTextTests.cs` ("Stopped." expectations)

**Interfaces:**
- Produces:
  - `enum ChipState { Starting, StartReading, Live, Paused, Trouble }`
  - `StatusChip.StateOf(bool running, bool everStarted, bool starting, bool trouble) : ChipState`
  - `StatusChip.Enabled(ChipState state, bool loaded, bool startGate) : bool`
  - `StatusChip.Glyph(ChipState) : string`, `Word(ChipState) : string`, `Name(ChipState) : string`, `BrushKey(ChipState) : string?`
  - `StatusChip.Title(ChipState) : string`
  - `StatusChip.ShowsLines(bool loaded, ChipState state, bool failed, string detail, bool remembered) : bool`
  - `StatusChip.TabBudget(double bar, double fixedWidth, double share) : double`
  - `BoardText.Paused : string`; `BoardText.InTrouble(LiveBoard) : bool`;
    `record StatusRow(string Name, string Line)`; `BoardText.CardRows(LiveBoard) : IReadOnlyList<StatusRow>`;
    `BoardText.AlertsLine(bool running, bool sending, bool hostDown) : string`;
    `BoardText.AsksBeforeClose(bool running, bool sending) : bool`

- [ ] **Step 1: Write the failing tests** — `tests/StatusChipTests.cs`

```csharp
using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.UI;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

/// <summary>BC2: one chip says what reading is doing; clicking it shows status and never pauses.</summary>
public class StatusChipTests
{
    [Theory]
    [InlineData(false, false, false, false, ChipState.StartReading)]
    [InlineData(false, false, true, false, ChipState.Starting)]
    [InlineData(true, true, false, false, ChipState.Live)]
    [InlineData(true, true, false, true, ChipState.Trouble)]
    [InlineData(false, true, false, false, ChipState.Paused)]
    [InlineData(false, true, false, true, ChipState.Paused)]   // paused wins: nothing is being read to be in trouble
    public void TheChipSaysWhatReadingIsDoing(bool running, bool everStarted, bool starting, bool trouble, ChipState expected) =>
        Assert.Equal(expected, StatusChip.StateOf(running, everStarted, starting, trouble));

    /// <summary>
    /// Review Focus 3: a read-now on a paused board closes the Start gate, but looking at the status must still work.
    /// Only "Start reading" (which starts) and "Starting" (which is busy) answer to the gate.
    /// </summary>
    [Fact]
    public void TheChipOpensWhateverTheStartGateSays()
    {
        Assert.True(StatusChip.Enabled(ChipState.Paused, loaded: true, startGate: false));
        Assert.True(StatusChip.Enabled(ChipState.Live, loaded: true, startGate: false));
        Assert.True(StatusChip.Enabled(ChipState.Trouble, loaded: true, startGate: false));
        Assert.False(StatusChip.Enabled(ChipState.StartReading, loaded: true, startGate: false));
        Assert.True(StatusChip.Enabled(ChipState.StartReading, loaded: true, startGate: true));
        Assert.False(StatusChip.Enabled(ChipState.Starting, loaded: true, startGate: true));
        Assert.False(StatusChip.Enabled(ChipState.Live, loaded: false, startGate: true));
    }

    /// <summary>Colour is never the only signal: every state has its own glyph and word, and a name that says it in words.</summary>
    [Fact]
    public void EveryStateHasItsOwnGlyphWordAndName()
    {
        var states = Enum.GetValues<ChipState>().Where(s => s != ChipState.Starting).ToList();

        Assert.Equal(states.Count, states.Select(StatusChip.Glyph).Distinct().Count());
        Assert.Equal(states.Count, states.Select(StatusChip.Word).Distinct().Count());
        Assert.Equal(Enum.GetValues<ChipState>().Length, Enum.GetValues<ChipState>().Select(StatusChip.Name).Distinct().Count());
        Assert.Equal("Reading is paused. Press for status.", StatusChip.Name(ChipState.Paused));
        Assert.Equal("AmberBrush", StatusChip.BrushKey(ChipState.Paused));
        Assert.Equal("MagentaBrush", StatusChip.BrushKey(ChipState.Trouble));
        Assert.NotEqual(StatusChip.BrushKey(ChipState.Paused), StatusChip.BrushKey(ChipState.Trouble));
    }

    [Fact]
    public void APausedBoardSaysSoInItsTitle()
    {
        Assert.Equal("RoRoRo Ur Score (Paused)", StatusChip.Title(ChipState.Paused));
        Assert.All(Enum.GetValues<ChipState>().Where(s => s != ChipState.Paused), s => Assert.Equal("RoRoRo Ur Score", StatusChip.Title(s)));
    }

    /// <summary>BC3: the line under the bar shows only when it has something to say, and A41's sentence always is.</summary>
    [Theory]
    [InlineData(true, ChipState.Live, false, "", false, false)]
    [InlineData(false, ChipState.Live, false, "", false, true)]      // the book is still being read
    [InlineData(true, ChipState.Paused, false, "", false, true)]
    [InlineData(true, ChipState.StartReading, false, "", false, true)]
    [InlineData(true, ChipState.Trouble, false, "", false, true)]
    [InlineData(true, ChipState.Live, true, "", false, true)]        // a press failed
    [InlineData(true, ChipState.Live, false, "Over budget", false, true)]
    [InlineData(true, ChipState.Live, false, "", true, true)]        // numbers from the score book (A41)
    public void TheLinesUnderTheBarShowOnlyWhenTheyMatter(bool loaded, ChipState state, bool failed, string detail, bool remembered, bool shows) =>
        Assert.Equal(shows, StatusChip.ShowsLines(loaded, state, failed, detail, remembered));

    [Fact]
    public void TheTabsTakeTheirShareOfWhatIsLeftAndNeverLessThanNothing()
    {
        Assert.Equal(300, StatusChip.TabBudget(1000, 500, 0.6), 6);
        Assert.Equal(0, StatusChip.TabBudget(400, 500, 0.6));
    }

    [Fact]
    public void TheCloseQuestionIsAskedOnlyWhileReadingAndSending()
    {
        Assert.True(BoardText.AsksBeforeClose(running: true, sending: true));
        Assert.False(BoardText.AsksBeforeClose(running: false, sending: true));
        Assert.False(BoardText.AsksBeforeClose(running: true, sending: false));
    }

    [Fact]
    public void TheCardSaysWhetherAlertsAreGoingOut()
    {
        Assert.Equal("Phone alerts: sending.", BoardText.AlertsLine(running: true, sending: true, hostDown: false));
        Assert.Equal("Phone alerts: off while reading is paused.", BoardText.AlertsLine(running: false, sending: true, hostDown: false));
        Assert.Equal("Phone alerts: nothing is set to send.", BoardText.AlertsLine(running: true, sending: false, hostDown: false));
        Assert.Equal("Phone alerts: RoRoRo is not running, so nothing is sent.", BoardText.AlertsLine(running: true, sending: true, hostDown: true));
    }

    [Fact]
    public void TheCardHasALinePerSwitchedOnSource()
    {
        var snaps = new Dictionary<string, RecipeSnapshot>
        {
            [MainClan.Id] = Snapshot(MainClan.Id, []),
            [AltClan.Id] = new RecipeSnapshot(WatchState.SourceUnreachable, "timed out", [], [], 0) { SourceId = AltClan.Id },
        };
        var live = Live([MainClan, AltClan], [Installed(Clan, "value")], snaps, running: true,
            lastRead: new Dictionary<string, DateTimeOffset> { [MainClan.Id] = Now.AddMinutes(-2) });

        var rows = BoardText.CardRows(live);

        Assert.Equal(2, rows.Count);
        Assert.Equal(live.SourceName(MainClan), rows[0].Name);
        // "· next in …" or "· next read due", depending on the fixture recipe's interval; either way it was read 2m ago.
        Assert.StartsWith("read 2m ago · next", rows[0].Line, StringComparison.Ordinal);
        Assert.Equal("Could not reach the data.", rows[1].Line);
        Assert.True(BoardText.InTrouble(live));
        Assert.False(BoardText.InTrouble(Live([MainClan], [Installed(Clan, "value")], snaps, running: true)));
    }
}
```

In `BoardTextTests`, every expectation of `"Stopped."` (lines ~56, 331-338) becomes `BoardText.Paused`, and
`"Stopped. Last read…"` shapes become `BoardText.Paused + " Last read…"`. Check `BoardFixtures` for the exact
`Snapshot`/`Live` signatures before running; adjust the new test's calls to them, not the other way round.

- [ ] **Step 2: Run to see them fail**

Run: `dotnet build Ur-Score.csproj -c Release; dotnet test tests/Ur-Score.Tests.csproj --filter "FullyQualifiedName~StatusChipTests|FullyQualifiedName~BoardTextTests"`
Expected: build errors for `StatusChip`, `ChipState`, `BoardText.Paused`, …

- [ ] **Step 3: Create `src/UI/StatusChip.cs`**

```csharp
namespace Labs626.UrScore.UI;

/// <summary>What the top bar's chip says (BC2). Starting is busy; Start reading is the one state a click acts on.</summary>
public enum ChipState { Starting, StartReading, Live, Paused, Trouble }

/// <summary>
/// The status chip that replaced Start/Stop and Test now as top-bar buttons (spec §3.2, BC2). A click on it never
/// pauses: it opens the status card, which holds Pause. Worked out from the same inputs as the state line, so the chip,
/// the card and the line tell one story.
/// </summary>
public static class StatusChip
{
    public const string AppTitle = "RoRoRo Ur Score";

    /// <summary>Paused wins over trouble: nothing is being read to be in trouble.</summary>
    public static ChipState StateOf(bool running, bool everStarted, bool starting, bool trouble) =>
        starting ? ChipState.Starting
        : running ? (trouble ? ChipState.Trouble : ChipState.Live)
        : everStarted ? ChipState.Paused
        : ChipState.StartReading;

    /// <summary>
    /// Looking at the status is always allowed once the book is read (Review Focus 3). Only Start reading, which
    /// starts, answers to the Start gate (<see cref="BoardButtons.For"/>), and Starting is busy.
    /// </summary>
    public static bool Enabled(ChipState state, bool loaded, bool startGate) => state switch
    {
        ChipState.Starting => false,
        ChipState.StartReading => startGate,
        _ => loaded,
    };

    public static string Glyph(ChipState state) => state switch
    {
        ChipState.Live => "●",
        ChipState.Paused => "❚❚",
        ChipState.Trouble => "▲",
        ChipState.StartReading => "▶",
        _ => "",
    };

    public static string Word(ChipState state) => state switch
    {
        ChipState.Live => "Live",
        ChipState.Paused => "Paused",
        ChipState.Trouble => "Trouble",
        ChipState.StartReading => "Start reading",
        _ => "Starting…",
    };

    /// <summary>The chip's automation name and tooltip: the state in words, and what a press does.</summary>
    public static string Name(ChipState state) => state switch
    {
        ChipState.Live => "Reading is on. Press for status.",
        ChipState.Paused => "Reading is paused. Press for status.",
        ChipState.Trouble => "Reading has a problem. Press for status.",
        ChipState.StartReading => "Start reading",
        _ => "Starting",
    };

    /// <summary>
    /// The glyph's colour. Paused is amber and the loudest state after first run, because paused silences the phone
    /// alerts; trouble is magenta, the colour this app already uses for a read that should have happened.
    /// </summary>
    public static string? BrushKey(ChipState state) => state switch
    {
        ChipState.Live => "CyanBrush",
        ChipState.Paused => "AmberBrush",
        ChipState.Trouble => "MagentaBrush",
        _ => null,
    };

    /// <summary>A paused board shows it on the taskbar and a second screen, where nobody is hovering over a chip.</summary>
    public static string Title(ChipState state) => state == ChipState.Paused ? $"{AppTitle} (Paused)" : AppTitle;

    /// <summary>
    /// BC3: the state and detail lines show only when there is something to say. A41's "the numbers on screen are …"
    /// sentence is a ruling, so a board drawing remembered numbers always shows it.
    /// </summary>
    public static bool ShowsLines(bool loaded, ChipState state, bool failed, string detail, bool remembered) =>
        !loaded || failed || state != ChipState.Live || detail.Length > 0 || remembered;

    /// <summary>The tabs' share of what the rest of the top bar leaves; the period line, never the chip or ⟳, gives way first.</summary>
    public static double TabBudget(double bar, double fixedWidth, double share) => Math.Max(0, (bar - fixedWidth) * share);
}
```

- [ ] **Step 4: Add to `BoardText`**

After `ReadingOnce`:

```csharp
    /// <summary>The state line while paused (BC2): the one sentence that says what pausing costs.</summary>
    public const string Paused = "Paused. Nothing is read or sent, so phone alerts are off.";
```

In `StateLine`, replace `(everStarted ? "Stopped." : "Not started.")` with `(everStarted ? Paused : "Not started.")`.
Update the `LastReadNews` summary's `"Stopped."` to `"Paused."` too.

After `DetailLine(...)` overloads:

```csharp
    /// <summary>
    /// Whether reading is in trouble, for the chip: a source this session reads that isn't healthy, or RoRoRo down
    /// (which the state line counts as healthy, since reading goes on, but nothing reaches the phone).
    /// </summary>
    public static bool InTrouble(LiveBoard live) =>
        live.Snapshots.Values.Any(s => s.State == WatchState.HostDown)
        || live.Sources.Where(s => s.Enabled).Any(s => live.LiveOf(s.Id) is { } snapshot && !Healthy(snapshot.State));

    /// <summary>One line per switched-on source on the status card: its trouble, else when it was read and when it reads next.</summary>
    public static IReadOnlyList<StatusRow> CardRows(LiveBoard live)
    {
        var rows = new List<StatusRow>();
        foreach (var source in live.Sources.Where(s => s.Enabled))
        {
            if (live.LiveOf(source.Id) is { } snapshot && !Healthy(snapshot.State))
            {
                rows.Add(new StatusRow(live.SourceName(source), DiagnosticsModel.StateText(snapshot.State)));
                continue;
            }

            if (!live.LastRead.TryGetValue(source.Id, out var last))
            {
                rows.Add(new StatusRow(live.SourceName(source), "not read yet"));
                continue;
            }

            var line = $"read {StatText.Span(live.Now - last)} ago";
            if (live.Running && live.FindRecipe(source.Recipe)?.Recipe is { } recipe)
            {
                var due = last.AddSeconds(recipe.EffectiveEverySeconds);
                line += due > live.Now ? $" · next in {StatText.Span(due - live.Now)}" : " · next read due";
            }

            rows.Add(new StatusRow(live.SourceName(source), line));
        }

        return rows;
    }

    /// <summary>Whether the phone is hearing anything, on the status card.</summary>
    public static string AlertsLine(bool running, bool sending, bool hostDown) =>
        !sending ? "Phone alerts: nothing is set to send."
        : !running ? "Phone alerts: off while reading is paused."
        : hostDown ? "Phone alerts: RoRoRo is not running, so nothing is sent."
        : "Phone alerts: sending.";

    /// <summary>BC8: closing asks only while something is being read AND sent; a paused board closes silencing nothing.</summary>
    public static bool AsksBeforeClose(bool running, bool sending) => running && sending;
```

At the end of the file (outside the class):

```csharp
/// <summary>One source's line on the status card.</summary>
public sealed record StatusRow(string Name, string Line);
```

- [ ] **Step 5: Build, suite, commit**

Run: `dotnet build Ur-Score.csproj -c Release; dotnet test tests/Ur-Score.Tests.csproj; echo "exit $LASTEXITCODE"`
Expected: `exit 0`.

```bash
git add src/UI/StatusChip.cs src/UI/BoardText.cs tests/StatusChipTests.cs tests/BoardTextTests.cs
git commit -m "feat(ui): the status chip's state, the card's lines, and when the lines under the bar show (BC2, BC3, BC8)"
```

---

### Task 7: Start on open is asked again once there is something to read

**Files:**
- Modify: `src/UI/BoardButtons.cs` (add `StartsLater`)
- Test: `tests/BoardButtonsTests.cs`

**Interfaces:**
- Produces: `BoardButtons.StartsLater(bool startOnOpen, bool loaded, bool running, bool everStarted, bool starting, int installed, bool anySourceOn) : bool`.

- [ ] **Step 1: Write the failing test** (append to `BoardButtonsTests`)

```csharp
    /// <summary>
    /// Spec §3.6, the first-run gap: opening decides once and skips while Setup opens on a recipe with no clan, so a new
    /// player's board never started. It is asked again when Setup closes and when the first source is switched on —
    /// only while this session has never started, so a pause is never undone behind the player's back (BC7).
    /// </summary>
    [Theory]
    [InlineData(true, true, false, false, false, 1, true, true)]    // the case: never started, now something to read
    [InlineData(true, true, false, true, false, 1, true, false)]    // started earlier and paused: a pause stands
    [InlineData(true, true, false, false, true, 1, true, false)]    // already starting
    [InlineData(false, true, false, false, false, 1, true, false)]  // turned off in Setup
    [InlineData(true, true, false, false, false, 1, false, false)]  // still nothing switched on
    [InlineData(true, true, true, false, false, 1, true, false)]    // already running
    public void StartOnOpenIsAskedAgainOnlyForABoardThatNeverStarted(
        bool startOnOpen, bool loaded, bool running, bool everStarted, bool starting, int installed, bool anySourceOn, bool starts) =>
        Assert.Equal(starts, BoardButtons.StartsLater(startOnOpen, loaded, running, everStarted, starting, installed, anySourceOn));
```

- [ ] **Step 2: Run to see it fail** — `dotnet test tests/Ur-Score.Tests.csproj --filter "FullyQualifiedName~BoardButtonsTests"` → build error.

- [ ] **Step 3: Implement** (in `BoardButtons`, after `StartsOnOpen`)

```csharp
    /// <summary>
    /// Start on open, asked again after opening (spec §3.6): when Setup closes, and when the switched-on sources go from
    /// none to some. Only for a session that has never started — a pause lasts until Ur Score closes (BC7) — and
    /// through <see cref="StartsOnOpen"/> itself, so the two can't disagree.
    /// </summary>
    public static bool StartsLater(bool startOnOpen, bool loaded, bool running, bool everStarted, bool starting, int installed, bool anySourceOn) =>
        !everStarted && !starting && StartsOnOpen(startOnOpen, loaded, running, installed, anySourceOn, firstRunPage: false);
```

- [ ] **Step 4: Suite and commit**

```bash
dotnet test tests/Ur-Score.Tests.csproj; echo "exit $?"
git add src/UI/BoardButtons.cs tests/BoardButtonsTests.cs
git commit -m "feat(ui): start on open is asked again once the first source is on, for a board that never started"
```

---

### Task 8: The new top bar: chip, status card, ⟳ and F5, lines that hide, title, close prompt

**Files:**
- Modify: `src/App.xaml` (add `AmberBrush` beside the Series brushes; add a `ChipButton` style after `FlatButton`)
- Modify: `src/UI/BoardWindow.xaml:1-127`
- Modify: `src/UI/BoardWindow.xaml.cs` (`RenderLinesCore`, `ApplyButtons`, `OnTopBarSizeChanged`, `OnStartStopClick`,
  `OpenSetup`, `RenderBoard`, `OnClosing`, new handlers)
- Test: create `tests/TopBarFenceTests.cs`

**Interfaces:**
- Consumes: everything Task 6 and Task 7 produce; `BoardButtons.For(...).StartStop/TestNow`.
- Produces (XAML names the walks use): `StartStopButton` (the chip), `TestNowButton` (⟳), `StatusCard` (Popup),
  `PauseResumeButton`, `StateLines` (the collapsible pair), `CardStateLine`, `CardSources`, `CardAlertsLine`, `CardDetailLine`.

- [ ] **Step 1: Write the failing fence** — `tests/TopBarFenceTests.cs`

```csharp
using System.Xml.Linq;

namespace UrScore.Tests;

/// <summary>
/// The top bar's shape, as the smoke walks and screen readers depend on it (spec §3). Read from the XAML, the way
/// DragHandleTests reads PanelFrame, because none of it is visible to a unit test of the running window.
/// </summary>
public class TopBarFenceTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XDocument Board() => XDocument.Load(Path.Combine(RepoRoot(), "src", "UI", "BoardWindow.xaml"));

    private static XElement Named(XDocument doc, string name) =>
        doc.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);

    [Fact]
    public void TheChipAndReadNowKeepTheIdsTheWalksPress()
    {
        var doc = Board();
        Assert.Equal("OnStatusChipClick", (string?)Named(doc, "StartStopButton").Attribute("Click"));
        Assert.Equal("OnTestNowClick", (string?)Named(doc, "TestNowButton").Attribute("Click"));
        Assert.Equal("OnPauseResumeClick", (string?)Named(doc, "PauseResumeButton").Attribute("Click"));
    }

    /// <summary>BC2: Pause lives in the card, never on the bar, so checking status can't pause.</summary>
    [Fact]
    public void PauseIsInsideTheStatusCard()
    {
        var card = Named(Board(), "StatusCard");
        Assert.Equal("Popup", card.Name.LocalName);
        Assert.Equal("False", (string?)card.Attribute("StaysOpen"));
        Assert.Contains(card.Descendants(), e => (string?)e.Attribute(X + "Name") == "PauseResumeButton");
    }

    /// <summary>Tab order follows what is seen: the bar is a Grid in left-to-right order, the right-hand buttons last.</summary>
    [Fact]
    public void TheRightHandButtonsComeLastInTheBar()
    {
        var bar = Named(Board(), "TopBar");
        Assert.Equal("Grid", bar.Name.LocalName);
        // Property elements (Grid.ColumnDefinitions) and the Popup, which is out of the layout and the Tab order, don't count.
        var children = bar.Elements().Where(e => !e.Name.LocalName.Contains('.') && e.Name.LocalName != "Popup").ToList();
        Assert.Equal("TopButtons", (string?)children[^1].Attribute(X + "Name"));
    }

    [Fact]
    public void StartAndTestNowAreNoLongerWordsOnTheBar()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "UI", "BoardWindow.xaml"));
        Assert.DoesNotContain("Content=\"Start\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Test now\"", text, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ur-Score.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
```

- [ ] **Step 2: Run to see it fail** — `dotnet test tests/Ur-Score.Tests.csproj --filter "FullyQualifiedName~TopBarFenceTests"` → FAIL (`StatusCard` not found).

- [ ] **Step 3: App resources** (`src/App.xaml`)

Beside the Series brushes add
`<SolidColorBrush x:Key="AmberBrush" Color="#FFB020" />` with the comment
`<!-- Paused (BC2): not a host palette slot, so it stays this colour in every theme; the chip's glyph and word carry the meaning too. -->`.
If `ThemeFenceTests` or `ChangeColourFenceTests` rejects a new literal colour, follow what that test asks for rather
than working around it.

After `FlatButton` add:

```xml
            <!-- The status chip (BC2): a flat pill that reads as a control, with a hover and pressed state. -->
            <Style x:Key="ChipButton" TargetType="Button" BasedOn="{StaticResource FlatButton}">
                <Setter Property="Padding" Value="10,4" />
                <Setter Property="BorderBrush" Value="{DynamicResource EdgeBrush}" />
                <Setter Property="HorizontalAlignment" Value="Left" />
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="Button">
                            <Grid>
                                <Border x:Name="Fill" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                                        BorderThickness="1" CornerRadius="999" />
                                <Border x:Name="Sheen" Background="{DynamicResource WhiteBrush}" CornerRadius="999" Opacity="0" IsHitTestVisible="False" />
                                <ContentPresenter Margin="{TemplateBinding Padding}" VerticalAlignment="Center" />
                            </Grid>
                            <ControlTemplate.Triggers>
                                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Sheen" Property="Opacity" Value="0.08" /></Trigger>
                                <Trigger Property="IsPressed" Value="True"><Setter TargetName="Sheen" Property="Opacity" Value="0.16" /></Trigger>
                                <Trigger Property="IsEnabled" Value="False"><Setter Property="Foreground" Value="{DynamicResource MutedTextBrush}" /></Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
```

- [ ] **Step 4: The bar** (`BoardWindow.xaml`)

Add `PreviewKeyDown="OnWindowKeyDown"` to the `<Window>` element. Replace the top-bar `Border` (lines 58-121) with a
Border whose child is:

```xml
            <!-- The top bar (spec §3.1): icon, tabs, ⋯, + Board, the status chip, ⟳, the period line; Arrange and Setup on the
                 right. A Grid in the order it is seen, so Tab and a screen reader go left to right. The period line is the
                 star column: it trims first, then the tabs scroll (OnTopBarSizeChanged); the chip and ⟳ never give way. -->
            <Grid x:Name="TopBar" SizeChanged="OnTopBarSizeChanged">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <Image x:Name="BoardIcon" Grid.Column="0" … (unchanged attributes) />
                <ListBox x:Name="BoardTabs" Grid.Column="1" … (unchanged, with its ItemsPanel and ContextMenu) />
                <Button x:Name="BoardMenuButton" Grid.Column="2" … (unchanged) />
                <Button x:Name="AddBoardButton" Grid.Column="3" … (unchanged) />
                <!-- BC2: the chip keeps StartStopButton's id so the walks' "ready" check still finds it. A click opens the
                     status card and never pauses; only "Start reading" acts, because there is no status yet to show. -->
                <Button x:Name="StartStopButton" Grid.Column="4" Style="{StaticResource ChipButton}" VerticalAlignment="Center"
                        Margin="0,0,6,0" Click="OnStatusChipClick" SizeChanged="OnTopBarSizeChanged">
                    <StackPanel Orientation="Horizontal">
                        <TextBlock x:Name="ChipGlyph" VerticalAlignment="Center" Margin="0,0,6,0" />
                        <TextBlock x:Name="ChipWord" VerticalAlignment="Center" FontWeight="Medium" />
                    </StackPanel>
                </Button>
                <Button x:Name="TestNowButton" Grid.Column="5" Content="⟳" Style="{StaticResource FlatButton}" Padding="6,2"
                        VerticalAlignment="Center" Margin="0,0,8,0" Foreground="{DynamicResource MutedTextBrush}"
                        ToolTip="Read every source now (F5)" AutomationProperties.Name="Read every source now" Click="OnTestNowClick" />
                <!-- Ticks between reads (WithClock off: this line is one line wide, and the panel gives the end as a time). -->
                <ui:CountdownText x:Name="PeriodLine" Grid.Column="6" WithClock="False" Style="{StaticResource Muted}"
                                  VerticalAlignment="Center" TextWrapping="NoWrap" TextTrimming="CharacterEllipsis" />
                <StackPanel x:Name="TopButtons" Grid.Column="7" Orientation="Horizontal" VerticalAlignment="Center" Margin="12,0,0,0"
                            SizeChanged="OnTopBarSizeChanged">
                    <Button x:Name="EditBoardButton" Content="Edit board" Click="OnEditBoardClick" AutomationProperties.Name="Edit board" />
                    <Button x:Name="AddPanelButton" Content="+ Add panel" Margin="8,0,0,0" Visibility="Collapsed" Click="OnAddPanelClick"
                            AutomationProperties.Name="Add panel" />
                    <Button x:Name="DoneButton" Content="Done" Style="{StaticResource PrimaryButton}" Margin="8,0,0,0" Visibility="Collapsed"
                            Click="OnDoneClick" AutomationProperties.Name="Done" />
                    <Button x:Name="SetupButton" Margin="8,0,0,0" Click="OnSetupClick" AutomationProperties.Name="Setup"> (unchanged content) </Button>
                </StackPanel>
                <!-- The status card (spec §3.3): opening it changes nothing (BC2). Themed (V3-S.13). Esc and a click away close it. -->
                <Popup x:Name="StatusCard" PlacementTarget="{Binding ElementName=StartStopButton}" Placement="Bottom" VerticalOffset="6"
                       StaysOpen="False" AllowsTransparency="True" Closed="OnStatusCardClosed">
                    <Border Background="{DynamicResource RowBgBrush}" BorderBrush="{DynamicResource EdgeBrush}" BorderThickness="1"
                            CornerRadius="8" Padding="14" MinWidth="320" MaxWidth="460" PreviewKeyDown="OnStatusCardKeyDown"
                            KeyboardNavigation.TabNavigation="Cycle" AutomationProperties.AutomationId="StatusCardBody"
                            AutomationProperties.Name="Reading status">
                        <StackPanel>
                            <TextBlock x:Name="CardStateLine" FontWeight="SemiBold" TextWrapping="Wrap" Foreground="{DynamicResource WhiteBrush}" />
                            <ItemsControl x:Name="CardSources" Margin="0,10,0,0" AutomationProperties.Name="Sources">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <DockPanel Margin="0,2">
                                            <TextBlock DockPanel.Dock="Right" Text="{Binding Line}" Style="{StaticResource Muted}" Margin="16,0,0,0" />
                                            <TextBlock Text="{Binding Name}" TextTrimming="CharacterEllipsis" Foreground="{DynamicResource WhiteBrush}" />
                                        </DockPanel>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                            <TextBlock x:Name="CardAlertsLine" Style="{StaticResource Muted}" Margin="0,10,0,0" />
                            <TextBlock x:Name="CardDetailLine" Style="{StaticResource Muted}" TextWrapping="Wrap" Margin="0,4,0,0" />
                            <Button x:Name="PauseResumeButton" HorizontalAlignment="Right" Margin="0,12,0,0" Click="OnPauseResumeClick" />
                        </StackPanel>
                    </Border>
                </Popup>
            </Grid>
```

Keep every attribute of the four reused elements exactly as it is today except `DockPanel.Dock`, which becomes
`Grid.Column`. Delete `LiveDot` (the chip replaces it) and the old `Start`/`Test now` buttons.

Name the under-bar pair so it can collapse:
`<StackPanel x:Name="StateLines" DockPanel.Dock="Top" Margin="16,8,16,0">` (its two TextBlocks unchanged).

- [ ] **Step 5: Wire it** (`BoardWindow.xaml.cs`)

Add fields beside `_importing`:

```csharp
    /// <summary>Whether any source was switched on at the last draw, so the first one to come on asks start-on-open again (§3.6).</summary>
    private bool _hadSources;
```

In `RenderLinesCore` replace the `LiveDot`/`StartStopButton.Content` lines, and the two tails that set
`StateLine.Text`/`DetailLine.Text`, so it reads:

```csharp
    private void RenderLinesCore(LiveBoard live)
    {
        PeriodLine.Lead = BoardText.TopLine(live, _anchorSourceId);
        PeriodLine.Ends = BoardText.TopEnds(live, _anchorSourceId);
        AttributionLine.Text = BoardText.Attribution(live);

        var chip = StatusChip.StateOf(live.Running, _services.EverStarted, _starting, BoardText.InTrouble(live));
        RenderChip(chip);

        var boardsProblem = _boardsNote ?? _services.BoardsProblem;
        if (!_services.ReaderLoaded)
        {
            // …(unchanged comment)…
            StateLine.Text = BoardText.BookStateLine(unread: _bookProblem is not null);
            DetailLine.Text = _bookProblem ?? boardsProblem ?? _importNote ?? "";
            StateLines.Visibility = Visibility.Visible;
            return;
        }

        // …(unchanged comment)…
        var activity = new BoardActivity(_starting, _testing, _services.AskedReadAt, _services.StoppedAt, _failed);
        StateLine.Text = BoardText.StateLine(live, _services.EverStarted, activity);
        DetailLine.Text = BoardText.DetailLine(live, _services.BudgetWarning, boardsProblem, _failed ? BoardText.UnexpectedDetail : _importNote);

        // BC3: the pair shows only when it has something to say; the card always has the lot.
        StateLines.Visibility = StatusChip.ShowsLines(true, chip, _failed, DetailLine.Text, live.OldestRemembered is not null)
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (StatusCard.IsOpen) RenderCard(live);
    }

    /// <summary>The chip, its name and the window title, from one state (BC2). Enabled-ness is ApplyButtons'.</summary>
    private void RenderChip(ChipState chip)
    {
        ChipGlyph.Text = StatusChip.Glyph(chip);
        ChipWord.Text = StatusChip.Word(chip);
        if (StatusChip.BrushKey(chip) is { } key) ChipGlyph.SetResourceReference(TextBlock.ForegroundProperty, key);
        else ChipGlyph.ClearValue(TextBlock.ForegroundProperty);

        StartStopButton.Style = (Style)FindResource(chip == ChipState.StartReading ? "PrimaryButton" : "ChipButton");
        AutomationProperties.SetName(StartStopButton, StatusChip.Name(chip));
        StartStopButton.ToolTip = StatusChip.Name(chip);
        Title = StatusChip.Title(chip);
    }

    /// <summary>The status card (spec §3.3): the state line, a line per source, whether alerts go out, and Pause.</summary>
    private void RenderCard(LiveBoard live)
    {
        CardStateLine.Text = StateLine.Text;
        CardSources.ItemsSource = BoardText.CardRows(live);
        var hostDown = live.Snapshots.Values.Any(s => s.State == WatchState.HostDown);
        CardAlertsLine.Text = BoardText.AlertsLine(live.Running, BoardText.Sending(_services.Installed, _services.Sources), hostDown);
        CardDetailLine.Text = DetailLine.Text;
        CardDetailLine.Visibility = DetailLine.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        PauseResumeButton.Content = live.Running ? "Pause reading" : "Resume reading";
        AutomationProperties.SetName(PauseResumeButton, live.Running ? "Pause reading" : "Resume reading");
    }
```

(`using Labs626.UrScore.Core;` is already present for `WatchState`.)

In `ApplyButtons` replace the `StartStopButton`/`TestNowButton` lines with

```csharp
        var chip = StatusChip.StateOf(_services.Running, _services.EverStarted, _starting, trouble: false);
        StartStopButton.IsEnabled = StatusChip.Enabled(chip, _services.ReaderLoaded, states.StartStop);
        PauseResumeButton.IsEnabled = states.StartStop;
        TestNowButton.IsEnabled = states.TestNow;
```

(`trouble` doesn't change enabled-ness, so passing false here is exact.)

Rename `OnStartStopClick` to `OnPauseResumeClick` (body unchanged), and add:

```csharp
    /// <summary>
    /// BC2: a click on the chip opens the status card and never pauses. "Start reading" is the exception, because a
    /// board that has never read has no status to show and starting is what that press means.
    /// </summary>
    private async void OnStatusChipClick(object sender, RoutedEventArgs e)
    {
        var chip = StatusChip.StateOf(_services.Running, _services.EverStarted, _starting, trouble: false);
        if (chip == ChipState.Starting) return;

        if (chip == ChipState.StartReading)
        {
            if (ButtonStates().StartStop) await StartReadingAsync();
            return;
        }

        StatusCard.IsOpen = !StatusCard.IsOpen;
        if (!StatusCard.IsOpen) return;

        RenderCard(_services.CurrentBoard());
        FocusLater(PauseResumeButton);
    }

    /// <summary>Esc closes the card and nothing else: marked handled so Arrange's Cancel (IsCancel) never sees it (Review Focus 4).</summary>
    private void OnStatusCardKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        StatusCard.IsOpen = false;
        e.Handled = true;
    }

    private void OnStatusCardClosed(object? sender, EventArgs e) => FocusLater(StartStopButton);

    /// <summary>F5 is ⟳, through the same gate (spec §3.5). Pop-outs have no ⟳ and don't take it.</summary>
    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F5 || Keyboard.Modifiers != ModifierKeys.None) return;
        e.Handled = true;
        OnTestNowClick(TestNowButton, new RoutedEventArgs());
    }
```

Add `using System.Windows.Input;` at the top.

`OnTopBarSizeChanged` becomes

```csharp
    /// <summary>
    /// The tabs never take more than their share of what the rest of the bar leaves (<see cref="TabStripShare"/>). The period
    /// line is the star column and trims first; the chip and ⟳ are counted as fixed, so they never give way (spec §3.1).
    /// </summary>
    private void OnTopBarSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var fixedWidth = BoardIcon.ActualWidth + BoardMenuButton.ActualWidth + AddBoardButton.ActualWidth
            + StartStopButton.ActualWidth + TestNowButton.ActualWidth + TopButtons.ActualWidth + 40;
        BoardTabs.MaxWidth = StatusChip.TabBudget(TopBar.ActualWidth, fixedWidth, TabStripShare);
    }
```

(40 is the margins between those elements: 10 + 10 + 14 + 6 − margins already inside `ActualWidth`. Measure it once
in the running app at 1024 DIP and correct the constant if the tabs overrun; say what you measured in the commit.)

Start on open, again (§3.6). In `OpenSetup`, change the `Closed` handler to

```csharp
        _setup.Closed += async (_, _) =>
        {
            _setup = null;
            await StartIfOpenWouldHaveAsync("after Setup closed");
        };
```

At the end of `RenderBoard`, after `RenderLines(live);`:

```csharp
        // §3.6: the first source switched on asks start-on-open again, for a board that never started.
        var hasSources = _services.Sources.Any(s => s.Enabled);
        if (hasSources && !_hadSources && _services.ReaderLoaded) Unawaited.TrailFailures(StartIfOpenWouldHaveAsync("when the first clan was switched on"), _services.AddTrail, "START ON OPEN FAILED");
        _hadSources = hasSources;
```

and the method, beside `StartReadingAsync`:

```csharp
    /// <summary>
    /// Start on open, asked again once there is something to read (spec §3.6): the one gate is BoardButtons.StartsLater,
    /// so a board that was paused, or is already starting, is left alone.
    /// </summary>
    private async Task StartIfOpenWouldHaveAsync(string when)
    {
        if (!BoardButtons.StartsLater(_services.Settings.StartOnOpen, _services.ReaderLoaded, _services.Running,
                _services.EverStarted, _starting, _services.Installed.Count, _services.Sources.Any(s => s.Enabled)))
        {
            return;
        }

        _services.AddTrail($"START ON OPEN: reading started {when}.");
        await StartReadingAsync();
    }
```

Check `Unawaited.TrailFailures`'s signature (used at `RenderBoard` for names) and match it. Also set
`_hadSources = _services.Sources.Any(s => s.Enabled);` at the end of `OpenOnTheBookAsync` before its start-on-open
check, so opening with sources already on doesn't count as "the first one came on".

`OnClosing` (BC8): replace `BoardText.Sending(_services.Installed, _services.Sources)` with
`BoardText.AsksBeforeClose(_services.Running, BoardText.Sending(_services.Installed, _services.Sources))`, and update
its summary: "While something is being read and sent, …; a paused board closes silencing nothing".

`BoardText.EmptyState`'s `BookUnread` line: `("Reading is off", "It comes back once Ur Score can read your score book. The line above says what stopped it.", "Try again")`;
update the `BoardTextTests` expectation that pins the old words.

- [ ] **Step 6: Build, suite, and look at it**

Run: `dotnet build Ur-Score.csproj -c Release; dotnet test tests/Ur-Score.Tests.csproj; echo "exit $LASTEXITCODE"`
Expected: `exit 0`.

Launch the Release build against a scratch data folder (Task 9's harness does this) and look at every chip state at
1280 and at 1024 DIP wide with three tabs. Screenshot with `tools/smoke/shot.ps1`.

- [ ] **Step 7: Commit**

```bash
git add src/App.xaml src/UI/BoardWindow.xaml src/UI/BoardWindow.xaml.cs src/UI/BoardText.cs tests/TopBarFenceTests.cs tests/BoardTextTests.cs
git commit -m "feat(ui): a status chip with a card that holds Pause, ⟳ and F5 beside the countdown, lines that show only when they matter"
```

---

### Task 9: The smoke harness and walks for the new bar

**Files:**
- Modify: `tools/smoke/uia.ps1` (`Get-BoardWindow`, `Move-UrDataAside`, `Copy-UrControlData`, `Assert-UrDataSendsNothing`)
- Modify: `tools/smoke/uia-board.ps1` (add `Invoke-PauseResume`)
- Modify: `tools/smoke/walk-starter-board.ps1:80-84, 133-137`
- Create: `tools/smoke/walk-top-bar.ps1`
- Modify: `tools/smoke/README.md`, `docs/SMOKE.md` (rows for the new walk)

- [ ] **Step 1: The board window by either title** (`uia.ps1:75`)

```powershell
# The board's title gains " (Paused)" while reading is paused (BC2), so it is matched by pattern, not equality.
function Get-BoardWindow { Get-UrWindows | Where-Object { $_.Current.Name -match '^RoRoRo Ur Score( \(Paused\))?$' } | Select-Object -First 1 }
```

`grep -rn "'\^RoRoRo Ur Score\$'" tools/smoke` and widen any other exact match the same way.

- [ ] **Step 2: A walk folder never reads on its own** (Review Focus 2)

At the end of the `try` in `Move-UrDataAside` (after `New-Item … $UrData`), add

```powershell
    # 0.6 reads on open by default and migrates any settings file without a version to on (BC1). A walk decides
    # when reading starts, so its folder is born with start-on-open off and already migrated.
    Set-Content (Join-Path $UrData 'settings.json') -Encoding UTF8 -Value '{ "resolveNames": true, "activeRecipe": null, "startOnOpen": false, "settingsVersion": 2 }'
```

In `Copy-UrControlData`, beside the `startOnOpen` line add
`$settings | Add-Member -NotePropertyName 'settingsVersion' -NotePropertyValue 2 -Force`, and when there is no
`settings.json` in the control folder write the same line `Move-UrDataAside` writes.

In `Assert-UrDataSendsNothing`, after the `startOnOpen` check:

```powershell
        if (-not $settings.settingsVersion -or $settings.settingsVersion -lt 2) { $problems += 'settings.json has no settingsVersion 2, so opening would switch startOnOpen on' }
    } else {
        $problems += 'no settings.json, so opening would write one with startOnOpen on'
```

(closing the `if (Test-Path $settingsPath)` with that `else`).

- [ ] **Step 3: Pause through the card** (`uia-board.ps1`, after `Complete-EditMode`)

```powershell
# BC2: the chip opens the status card; Pause and Resume are inside it. The card is a popup, a top-level window of its
# own, so its button is found across the process's windows. Returns the button's new name.
function Invoke-PauseResume([int]$seconds = 10) {
    Invoke-Element (Find-ByAutomationId (Get-BoardWindow) 'StartStopButton')
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        $button = Find-InUrWindows 'PauseResumeButton'
        if ($button) {
            Invoke-Element $button
            Start-Sleep -Milliseconds 600
            return (Find-InUrWindows 'PauseResumeButton').Current.Name
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    throw 'the status card never opened'
}
```

Before relying on it, confirm by hand once that `Find-InUrWindows 'PauseResumeButton'` finds the button while the
card is open (a WPF Popup is its own HWND). If it doesn't, give the Popup's Border an AutomationId and walk
`$AE::RootElement` children by process id for it; record which worked in the helper's comment.

- [ ] **Step 4: walk-starter-board** — step 2 still presses `StartStopButton` (on a fresh folder the chip is
"Start reading" and starts). Step 4 becomes

```powershell
    Invoke-PauseResume | Out-Null
    $stopped = Wait-Line (Get-BoardWindow) 'StateLine' '^Paused\.' 20
    Check '4 Pause pauses' ($stopped -match '^Paused\.') $stopped
```

- [ ] **Step 5: walk-top-bar.ps1** (new; header comment in the style of the others; ASCII only)

```powershell
# The top bar after testing (spec 3, BC1-BC3, BC8): the chip starts a board that never read, opens a card without
# changing anything, pauses from inside the card, shows Paused in the title and the line; the read-now button and
# F5 read; Esc closes the card; a paused board closes without asking; start on open reads by itself.
param([string]$Main = 'CCGP')

. (Join-Path $PSScriptRoot 'uia-board.ps1')
$ErrorActionPreference = 'Stop'
$backup = $null

function Chip { (Find-ByAutomationId (Get-BoardWindow) 'StartStopButton').Current.Name }

try {
    $backup = Move-UrDataAside
    Note-RoRoRo 'before'
    $board = Initialize-ClanBoard $Main

    Check '1 A board that never read offers Start reading' ((Chip) -eq 'Start reading') (Chip)

    Invoke-Element (Find-ByAutomationId $board 'StartStopButton')
    $live = Wait-Until { (Chip) -match '^Reading (is on|has a problem)' } 90
    Check '2 The chip starts it, and says it is reading' $live (Chip)

    Invoke-Element (Find-ByAutomationId (Get-BoardWindow) 'StartStopButton')
    Start-Sleep -Milliseconds 600
    $card = Find-InUrWindows 'PauseResumeButton'
    Check '3 A click opens the card' ([bool]$card) 'PauseResumeButton present'
    Check '3b ...and pauses nothing' ((Chip) -match '^Reading (is on|has a problem)') (Chip)

    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Milliseconds 500
    Check '4 Esc closes the card' (-not (Find-InUrWindows 'PauseResumeButton')) 'card gone'

    $name = Invoke-PauseResume
    Check '5 Pause is in the card and pauses' ((Chip) -eq 'Reading is paused. Press for status.') "chip='$(Chip)' button='$name'"
    Check '5b The title says Paused' ((Get-BoardWindow).Current.Name -eq 'RoRoRo Ur Score (Paused)') (Get-BoardWindow).Current.Name
    $line = Line (Get-BoardWindow) 'StateLine'
    Check '5c The line says what pausing costs' ($line -match '^Paused\. Nothing is read or sent') $line
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')

    Invoke-Element (Find-ByAutomationId (Get-BoardWindow) 'TestNowButton')
    $went = Wait-Until { -not (Find-ByAutomationId (Get-BoardWindow) 'TestNowButton').Current.IsEnabled } 10
    $back = Wait-Until { (Find-ByAutomationId (Get-BoardWindow) 'TestNowButton').Current.IsEnabled } 240
    Check '6 The read-now button reads while paused' ($went -and $back) "went=$went back=$back"

    (Get-BoardWindow).SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('{F5}')
    $f5 = Wait-Until { -not (Find-ByAutomationId (Get-BoardWindow) 'TestNowButton').Current.IsEnabled } 10
    Wait-Until { (Find-ByAutomationId (Get-BoardWindow) 'TestNowButton').Current.IsEnabled } 240 | Out-Null
    Check '7 F5 reads too' $f5 "f5 went=$f5"

    & (Join-Path $PSScriptRoot 'shot.ps1') -OutPath (Join-Path $UrShots 'top-bar-paused.png') | Out-Null

    # 8. Paused closes without asking (BC8).
    Close-UrWindow (Get-BoardWindow)
    $asked = Wait-UrConfirm '^Close Ur Score$' 4
    Check '8 A paused board closes without asking' (-not $asked) "confirm=$([bool]$asked)"
    Stop-UrScore

    # 9. Start on open reads by itself (BC1): tick it the way a player would, then open again.
    Start-UrScore | Out-Null
    $setup = Open-SetupPage 'Recipes'
    Set-Tick (Get-Check $setup 'Start reading when Ur Score opens') $true
    Close-UrWindow (Get-SetupWindow)
    Stop-UrScoreFromBoard
    Start-UrScore | Out-Null
    $auto = Wait-Until { (Chip) -match '^Reading (is on|has a problem)' } 90
    Check '9 Start on open reads without a press' $auto (Chip)
    Stop-UrScoreFromBoard
    Note-RoRoRo 'after'
}
finally {
    if ($null -ne $backup) { Restore-UrData $backup }
    Show-Results
}
```

`Stop-UrScoreFromBoard` on a running, sending board raises the close question; if it does, answer it with
`Invoke-UrConfirm (Wait-UrConfirm '^Close Ur Score$' 5) 'Close Ur Score'` before `Stop-UrScore`. Check how
`Wait-UrConfirm` and `Set-Tick` are called elsewhere and match them.

Also take a 1024-DIP screenshot of the bar with three tabs in each chip state (spec §7): resize the board window with
`$board.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern).Resize(1024, 800)` at 100% scale.

- [ ] **Step 6: Run every walk the bar touches** (RoRoRo quit, nothing recording)

`walk-top-bar`, `walk-starter-board`, `walk-alts`, `walk-score-book`, `walk-pop-outs`, `window-smoke`.
Expected: 0 failed each. The five Test-now walks pass without edits beyond Step 1 — that is this PR's acceptance
check (spec §3.8). Record each result line in the PR body.

- [ ] **Step 7: Commit**

```bash
git add tools/smoke docs/SMOKE.md
git commit -m "test(smoke): walk folders never read by themselves; pause through the card; walk-top-bar"
```

---

### Task 10: Paper and release 0.6.0

**Files:** `README.md` (the board's top bar section and the Start-on-open lines ~225, 243-246),
`docs/2026-09-14-score-book-design.md` §8 (a superseded note pointing at the board-chrome spec),
`docs/plans/2026-09-15-alerts-card.md` (a line under A31: "Superseded by BC1, 2026-09-23"), `CHANGELOG.md`,
`manifest.json`, `Ur-Score.csproj`.

- [ ] **Step 1:** README: describe the chip ("● Live / ❚❚ Paused / ▲ Trouble; click it for status; Pause is in the
card"), ⟳ and F5, and "Ur Score starts reading as soon as it opens; turn that off in Setup › Recipes".
- [ ] **Step 2:** CHANGELOG `## 0.6.0 - <date>` with **Changed** (reading starts on open, including existing installs,
once; Start/Stop and Test now became the status chip and ⟳; closing asks only while reading and sending) and
**Added** (the status card; F5; "(Paused)" in the title).
- [ ] **Step 3:** Versions `0.5.10` → `0.6.0`. Suite (exit 0), commit
`chore(release): 0.6.0 — the status chip, reading on open`, PR, and on Este's word follow `docs/releasing.md`.

---

# Stage 2 — Arrange (PR 3, release 0.6.1)

Branch: `feat/arrange` from `master` after PR 2 merges.

### Task 11: Tabs stay clickable while arranging, and there is a Cancel

**Files:** `src/UI/BoardButtons.cs`; `tests/BoardButtonsTests.cs`

**Interfaces:** `BoardButtonStates` gains `bool Cancel = false`; `For(...)` returns `Tabs: true` and `Cancel: editing`.

- [ ] **Step 1: Update the test** — replace `WhileEditingOnlyTheDraftsBoardIsReachable` with

```csharp
    [Fact]
    public void WhileArrangingTheTabsStillSwitchAndOnlyTheBoardCommandsWait()
    {
        // BC5 and spec §4.5: a tab click saves the draft and switches, so the tabs stay on; + Board and the tab menu
        // still wait, and Cancel exists only while arranging.
        var states = BoardButtons.For(loaded: true, running: false, starting: false, testing: false, importing: false, boards: 2, editing: true);

        Assert.False(states.EditBoard);
        Assert.True(states.AddPanel);
        Assert.True(states.Done);
        Assert.True(states.Cancel);
        Assert.True(states.Tabs);
        Assert.False(states.AddBoard);
        Assert.False(states.RenameBoard);
        Assert.False(states.DuplicateBoard);
        Assert.False(states.DeleteBoard);
        Assert.False(states.BoardMenu);
        Assert.False(BoardButtons.For(true, false, false, false, false, editing: false).Cancel);
    }
```

- [ ] **Step 2:** Run → FAIL (`Cancel` missing).
- [ ] **Step 3:** Add `bool Cancel = false` as the last record parameter; in `For`, `Tabs: true` and `Cancel: editing`;
the `editing` param doc: "…the tab menu and + Board wait for Done; the tabs don't, since a tab click saves and switches (BC5)".
- [ ] **Step 4:** Suite → exit 0. Commit `feat(ui): the tabs stay on while arranging, and Arrange has a Cancel`.

---

### Task 12: Edit affordances inside the header, so panels keep their height

**Files:** `src/UI/Panels/PanelFrame.xaml`, `src/UI/Panels/PanelFrame.xaml.cs`, `src/App.xaml` (`PanelCard` focus ring),
`tests/DragHandleTests.cs`

**Interfaces:** `DragHandle` stays a non-focusable `Label` named "Drag to move" with the six dots, now inside
`TitleRow`, with NO mouse handler of its own; `RemovePanelButton` moves into `PanelTools`; `EditTools` is gone.

- [ ] **Step 1: Rewrite `DragHandleTests`' first test's XAML assertions**

In `GripIsDrawnNamedAndKeepsItsMouseHandlerWithoutAKeyboardStop` (rename to
`GripIsDrawnNamedInsideTheHeaderAndDragsThroughIt`): assert the grip's parent chain reaches `TitleRow`
(`grip.Ancestors().Any(a => (string?)a.Attribute(xaml + "Name") == "TitleRow")`), assert
`grip.Attribute("MouseLeftButtonDown") is null`, remove the line that deletes that attribute, replace the
`DesiredSize` 24–40 checks with `Assert.InRange(label.DesiredSize.Height, 8, 20)` (the header must not grow) and
`Assert.InRange(label.DesiredSize.Width, 12, 30)`, and keep the name, tooltip, focusable and dots assertions. Add:

```csharp
    /// <summary>Spec §4.3: nothing is inserted above the title while arranging, so no panel changes height on the way in.</summary>
    [Fact]
    public void ArrangingAddsNoRowAboveTheTitle()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "Ur-Score.csproj"))) directory = directory.Parent;
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var panel = XDocument.Load(System.IO.Path.Combine(directory!.FullName, "src", "UI", "Panels", "PanelFrame.xaml"));

        Assert.DoesNotContain(panel.Descendants(), e => (string?)e.Attribute(xaml + "Name") == "EditTools");
        var remove = panel.Descendants().Single(e => (string?)e.Attribute(xaml + "Name") == "RemovePanelButton");
        Assert.Contains(remove.Ancestors(), a => (string?)a.Attribute(xaml + "Name") == "PanelTools");
    }
```

- [ ] **Step 2:** Run `--filter "FullyQualifiedName~DragHandleTests"` → FAIL.

- [ ] **Step 3: PanelFrame.xaml** — delete the `EditTools` WrapPanel. Wrap `TitleRow` in a Grid with a dashed outline:

```xml
        <!-- Arranging (spec §4.3): the header says it drags, with a dashed edge, the grip and ✕, all inside the line that is
             already there, so no panel changes height on the way in. Moving is dragging the header; the grip is the
             visible cue and has no handler of its own: its press bubbles to TitleRow, which waits for the system's
             drag threshold. -->
        <Grid>
            <Rectangle x:Name="HeaderOutline" Margin="-6,-4" RadiusX="4" RadiusY="4" Stroke="{DynamicResource EdgeBrush}"
                       StrokeThickness="1" StrokeDashArray="4 3" IsHitTestVisible="False" Visibility="Collapsed" />
            <DockPanel x:Name="TitleRow" … (unchanged attributes) >
                <StackPanel x:Name="PanelTools" DockPanel.Dock="Right" …>
                    (PopOutButton, PanelSettingsButton unchanged)
                    <Button x:Name="RemovePanelButton" Content="✕" Tag="Remove" Style="{StaticResource FlatButton}" Padding="6,2"
                            Visibility="Collapsed" Foreground="{DynamicResource MutedTextBrush}" Click="OnToolClick" />
                </StackPanel>
                <Label x:Name="DragHandle" DockPanel.Dock="Left" Background="Transparent" Cursor="SizeAll" Padding="0,0,8,0"
                       VerticalAlignment="Center" Focusable="False" Visibility="Collapsed" ToolTip="Drag to move"
                       AutomationProperties.AutomationId="DragHandle" AutomationProperties.Name="Drag to move">
                    (the same UniformGrid of six Ellipses)
                </Label>
                (PanelTitle, chip Border, PanelOverdue, PanelRemembered unchanged)
            </DockPanel>
        </Grid>
```

- [ ] **Step 4: PanelFrame.xaml.cs** — delete `OnDragHandleDown`. In `UpdateTools` replace
`EditTools.Visibility = …` with

```csharp
        var arranging = editing ? Visibility.Visible : Visibility.Collapsed;
        DragHandle.Visibility = arranging;
        RemovePanelButton.Visibility = arranging;
        HeaderOutline.Visibility = arranging;
```

- [ ] **Step 5: The keyboard target shows** (`App.xaml`, `PanelCard` style) — add

```xml
                <!-- Spec §4.6: while arranging, the panel a keyboard is moving shows which one it is. -->
                <Style.Triggers>
                    <MultiTrigger>
                        <MultiTrigger.Conditions>
                            <Condition Property="IsKeyboardFocusWithin" Value="True" />
                            <Condition Property="ui:PanelFrame.ShowEditTools" Value="True" />
                        </MultiTrigger.Conditions>
                        <Setter Property="BorderBrush" Value="{DynamicResource CyanBrush}" />
                    </MultiTrigger>
                </Style.Triggers>
```

Check every panel view under `src/UI/Panels/*.xaml` uses `Style="{StaticResource PanelCard}"` on its outer Border
(12 do today); `ShowEditTools` is an inherited attached property, so it reaches the Border.

- [ ] **Step 6:** Suite → exit 0. Commit `feat(ui): arranging draws its tools inside the header, so no panel changes height`.

---

### Task 13: The Arrange banner, Cancel and Esc, Done (n), and a tab click that saves

**Files:** `src/UI/BoardWindow.xaml`, `src/UI/BoardWindow.xaml.cs`, `src/UI/BoardWindow.Editing.cs`,
`src/UI/BoardWindow.PanelSettings.cs`, `src/UI/BoardText.cs`, `tests/BoardTextTests.cs`

**Interfaces:**
- Produces: `BoardText.ArrangingLine(string boardName)`, `BoardText.DoneLabel(int changes)`,
  `BoardText.CancelArrangeQuestion(BoardDef board) : Confirm`; XAML `ArrangeBanner`, `ArrangeLine`,
  `CancelArrangeButton` (with `AddPanelButton` and `DoneButton` moved into the banner, ids unchanged).

- [ ] **Step 1: Failing text tests** (append to `BoardTextTests`)

```csharp
    [Fact]
    public void TheArrangeBannerTeachesTheGestures()
    {
        Assert.Equal("Arranging \"Battle\" · drag a header to move · drag an edge or corner to resize · ←/→ move",
            BoardText.ArrangingLine("Battle"));
    }

    [Fact]
    public void DoneCountsTheChanges()
    {
        Assert.Equal("Done", BoardText.DoneLabel(0));
        Assert.Equal("Done (3)", BoardText.DoneLabel(3));
    }

    /// <summary>BC5: "your changes", because ⋯ settings changed while arranging are in the draft too.</summary>
    [Fact]
    public void CancelAsksAboutAllTheChangesToTheBoard()
    {
        var question = BoardText.CancelArrangeQuestion(new BoardDef("b-1", "Battle", []));

        Assert.Equal("Throw away your changes to the Battle board?", question.Question);
        Assert.Equal("Throw away", question.Answer);
        Assert.Equal("Keep arranging", question.CancelButton);
    }
```

Check `Confirm`'s property names in `src/UI/Confirm.cs` and use them.

- [ ] **Step 2:** Run → FAIL.

- [ ] **Step 3: BoardText** (after `DeleteBoardQuestion`)

```csharp
    /// <summary>The Arrange banner (spec §4.2): what arranging this board is, and how to do it.</summary>
    public static string ArrangingLine(string boardName) =>
        $"Arranging \"{boardName}\" · drag a header to move · drag an edge or corner to resize · ←/→ move";

    /// <summary>Done, with how many changes it will save (spec §4.2).</summary>
    public static string DoneLabel(int changes) => changes == 0 ? "Done" : $"Done ({changes})";

    /// <summary>BC5: asked only when something changed; "your changes", since ⋯ settings changed while arranging are in the draft.</summary>
    public static Confirm CancelArrangeQuestion(BoardDef board) => new(
        "Cancel arranging",
        $"Throw away your changes to the {board.Name} board?",
        "Throw away",
        $"Throw away your changes to {board.Name}") { CancelButton = "Keep arranging" };
```

`EmptyState`'s `NoPanels` detail outside editing: `"Add panels from the gallery, then arrange them with Arrange."` —
update its test expectation.

- [ ] **Step 4: XAML.** In `TopButtons`, `EditBoardButton` becomes
`Content="Arrange" AutomationProperties.Name="Arrange"` and stays visible; delete `AddPanelButton` and `DoneButton`
from there. Replace the `StateLines` StackPanel with a Grid holding both it and the banner in one slot:

```xml
        <!-- The lines under the bar, and while arranging the banner in their place (spec §4.2). -->
        <Grid DockPanel.Dock="Top" Margin="16,8,16,0">
            <StackPanel x:Name="StateLines"> (StateLine and DetailLine unchanged) </StackPanel>
            <Border x:Name="ArrangeBanner" Visibility="Collapsed" Background="{DynamicResource RowBgBrush}"
                    BorderBrush="{DynamicResource CyanBrush}" BorderThickness="0,0,0,2" CornerRadius="4" Padding="12,6">
                <DockPanel>
                    <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" VerticalAlignment="Center">
                        <Button x:Name="AddPanelButton" Content="+ Add panel" Click="OnAddPanelClick" AutomationProperties.Name="Add panel" />
                        <Button x:Name="CancelArrangeButton" Content="Cancel" Margin="8,0,0,0" IsCancel="True" Click="OnCancelArrangeClick"
                                AutomationProperties.Name="Cancel arranging" />
                        <Button x:Name="DoneButton" Content="Done" Style="{StaticResource PrimaryButton}" Margin="8,0,0,0"
                                Click="OnDoneClick" AutomationProperties.Name="Done" />
                    </StackPanel>
                    <TextBlock x:Name="ArrangeLine" VerticalAlignment="Center" TextWrapping="Wrap" Foreground="{DynamicResource WhiteBrush}" />
                </DockPanel>
            </Border>
        </Grid>
```

Known limit, to flag in the PR for the owner's look: when the board was healthy and its lines hidden (BC3), the
banner adds its own height once on the way in; no panel changes size.

- [ ] **Step 5: Code.**

`BoardWindow.Editing.cs` — add the change count beside `_draftBase`:

```csharp
    /// <summary>How many changes the draft holds, for Done (n). Stage 3 replaces this with the draft's undo count.</summary>
    private int _draftSteps;
```

`ShowEditMode` becomes

```csharp
    private void ShowEditMode()
    {
        var editing = Editing;
        PanelFrame.SetShowEditTools(BoardPanels, editing);

        ArrangeBanner.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        if (editing) StateLines.Visibility = Visibility.Collapsed;
        ArrangeLine.Text = _draft is { } draft ? BoardText.ArrangingLine(draft.Name) : "";
        DoneButton.Content = BoardText.DoneLabel(_draftSteps);

        ApplyButtons();
        RenderLines();

        // After the grid has arranged, not now: a cell has no rectangle until it has been placed.
        Dispatcher.BeginInvoke(ShowGrips, DispatcherPriority.Loaded);
    }
```

and in `RenderLinesCore` (Task 8) guard the lines' visibility with `&& !Editing` so the banner keeps the slot.
In `OnEditBoardClick` set `_draftSteps = 0;` before `ShowEditMode()`; in `FinishEditing`, after
`_draft = _draftBase = null;`, set `_draftSteps = 0;`; in `OnDoneClick` keep `FocusLater(EditBoardButton)`.

Add

```csharp
    /// <summary>
    /// BC5: Cancel and Esc (IsCancel) leave arranging. With nothing changed they just leave; with changes they ask first,
    /// in the theme. Closing the window still saves the draft (R8).
    /// </summary>
    private void OnCancelArrangeClick(object sender, RoutedEventArgs e)
    {
        if (!Editing || _draft is not { } draft) return;
        if (_draftBase is { } atEdit && BoardEdits.Changed(atEdit, draft) && !ConfirmWindow.Ask(this, BoardText.CancelArrangeQuestion(atEdit))) return;

        _draft = _draftBase = null;
        _draftSteps = 0;
        ShowEditMode();
        Render();
        FocusLater(EditBoardButton);
    }
```

`BoardWindow.PanelSettings.cs` `ChangeBoard`, draft branch, after `_draft = edited;`:
`_draftSteps++; DoneButton.Content = BoardText.DoneLabel(_draftSteps);`.

`ApplyButtons`: add `CancelArrangeButton.IsEnabled = states.Cancel;` (a disabled IsCancel button ignores Esc outside
arranging).

`OnTabChanged` becomes

```csharp
    private void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectingTab || !ButtonStates().Tabs || BoardTabs.SelectedItem is not BoardTabItem tab || tab.Id == _boardId) return;

        // Spec §4.5: a tab click while arranging saves the draft the way Done does, then switches. A save that fails keeps
        // arranging on this board, and the redraw puts the tab selection back where it was.
        if (Editing)
        {
            FinishEditing();
            if (Editing)
            {
                Render();
                return;
            }
        }

        ShowBoard(tab.Id);
    }
```

- [ ] **Step 6:** Suite → exit 0. Commit `feat(ui): Arrange — a banner that teaches, Cancel and Esc, Done (n), tabs that save and switch (BC5)`.

---

### Task 14: Walks, paper, release 0.6.1

**Files:** `tools/smoke/walk-board-editing.ps1`, `tools/smoke/walk-visible-fixes.ps1`, `tools/smoke/walk-top-bar.ps1`,
`README.md`, `docs/plans/2026-09-14-score-book-stage-2.md` (note under R8), `docs/2026-09-17-final-four-design.md`
(superseded banner on "Fixed board canvas"), `CHANGELOG.md`, versions.

- [ ] **Step 1: walk-board-editing** — step 2b becomes
`Check '2b The tabs stay on while arranging' ((Find-ByAutomationId $board 'BoardTabs').Current.IsEnabled) 'BoardTabs enabled'`;
add after step 2c:

```powershell
    # 2d. Cancel with a change asks, and throws the change away (BC5).
    $before = @(Get-PanelIds (Get-BoardWindow))
    Enter-EditMode (Get-BoardWindow)
    Move-PanelEarlier (Get-BoardWindow) 'RacePanel1'
    Invoke-Element (Find-ByAutomationId (Get-BoardWindow) 'CancelArrangeButton')
    $ask = Wait-UrConfirm '^Cancel arranging$' 10
    Check '2d Cancel with a change asks first' ([bool]$ask) "confirm=$([bool]$ask)"
    Invoke-UrConfirm $ask 'Throw away your changes to Battle'
    Start-Sleep -Milliseconds 800
    Check '2e ...and the order is as it was' ((@(Get-PanelIds (Get-BoardWindow)) -join ',') -eq ($before -join ',')) "$(@(Get-PanelIds (Get-BoardWindow)) -join ',')"
    Check '2f ...and nothing was written' (-not (Test-Path $boardsFile)) "exists=$(Test-Path $boardsFile)"

    # 2g. Esc with the status card open closes the card, not the arrangement (Review Focus 4).
    Enter-EditMode (Get-BoardWindow)
    Invoke-Element (Find-ByAutomationId (Get-BoardWindow) 'StartStopButton')
    Start-Sleep -Milliseconds 500
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Milliseconds 500
    Check '2g Esc on the status card leaves arranging on' ((Find-ByAutomationId (Get-BoardWindow) 'ArrangeBanner').Current.IsOffscreen -eq $false) 'banner still shown'
    Complete-EditMode (Get-BoardWindow)
```

(The chip here is "Start reading", which starts rather than opening a card, on a fresh walk folder: press it once
first so reading has run, then this step opens the card. Put `Invoke-Element StartStopButton; Wait-Until { chip is Live }`
before 2g.)

- [ ] **Step 2:** walk-visible-fixes step 2 checks the grip by id, name, and focusability, which hold; its drag now
crosses the header's threshold (12 steps) — run it and fix only what fails.
- [ ] **Step 3:** Run walk-board-editing, walk-visible-fixes, walk-starter-board (its step 6 uses Remove), walk-top-bar →
0 failed each; record results.
- [ ] **Step 4:** Paper, CHANGELOG `## 0.6.1` (**Changed**: Edit board is Arrange; panels keep their height;
tabs switch while arranging and save first. **Added**: Cancel and Esc; Done (n); the arranged panel shows a focus
ring), versions `0.6.0` → `0.6.1`. Suite exit 0; commit; PR; release on Este's word.

---

# Stage 3 — undo (PR 4, release 0.6.2)

Branch: `feat/undo` from `master` after PR 3 merges.

### Task 15: `BoardUndo`, a stack of board snapshots per board

**Files:** Create `src/Board/BoardUndo.cs`; create `tests/BoardUndoTests.cs`

**Interfaces:**
- Produces: `record UndoStep(BoardDef Before, string What)`; `sealed class BoardUndo` with `const int Depth = 20`,
  `Push(BoardDef before, string what)`, `Peek(string boardId) : UndoStep?`, `Pop(string boardId) : UndoStep?`,
  `Count(string boardId) : int`, `Clear(string boardId)`, `ClearAll()`.

- [ ] **Step 1: Failing tests** — `tests/BoardUndoTests.cs`

```csharp
using Labs626.UrScore.Board;

namespace UrScore.Tests;

/// <summary>BC6: every board change can be taken back, per board, for the session.</summary>
public class BoardUndoTests
{
    private static BoardDef Board(string id, int panels) =>
        new(id, id, [.. Enumerable.Range(0, panels).Select(i => new PanelDef($"p-{i}", PanelType.Standing, new PanelSize(), new PanelSettings("r")))]);

    [Fact]
    public void TheLastChangeComesBackFirst()
    {
        var undo = new BoardUndo();
        undo.Push(Board("b-1", 1), "Added Race");
        undo.Push(Board("b-1", 2), "Moved Race");

        Assert.Equal("Moved Race", undo.Pop("b-1")!.What);
        Assert.Equal("Added Race", undo.Pop("b-1")!.What);
        Assert.Null(undo.Pop("b-1"));
    }

    [Fact]
    public void EachBoardHasItsOwnHistory()
    {
        var undo = new BoardUndo();
        undo.Push(Board("b-1", 1), "one");
        undo.Push(Board("b-2", 1), "two");

        Assert.Equal("two", undo.Peek("b-2")!.What);
        Assert.Equal(1, undo.Count("b-1"));
        undo.Clear("b-2");
        Assert.Equal(0, undo.Count("b-2"));
        Assert.Equal(1, undo.Count("b-1"));
    }

    [Fact]
    public void ItKeepsTheLatestTwenty()
    {
        var undo = new BoardUndo();
        for (var i = 0; i < 25; i++) undo.Push(Board("b-1", 1), $"step {i}");

        Assert.Equal(BoardUndo.Depth, undo.Count("b-1"));
        Assert.Equal("step 24", undo.Peek("b-1")!.What);
    }

    [Fact]
    public void PeekTakesNothing()
    {
        var undo = new BoardUndo();
        undo.Push(Board("b-1", 1), "one");
        undo.Peek("b-1");

        Assert.Equal(1, undo.Count("b-1"));
    }
}
```

- [ ] **Step 2:** Run → build error.

- [ ] **Step 3: Implement** — `src/Board/BoardUndo.cs`

```csharp
namespace Labs626.UrScore.Board;

/// <summary>One change that can be taken back: the board as it was before it, and what it was, for the toast.</summary>
public sealed record UndoStep(BoardDef Before, string What);

/// <summary>
/// BC6: board-level undo. A snapshot of the board is pushed before every change that goes through ChangeBoard; boards are
/// immutable records, so a snapshot is a reference. One history per board, the latest <see cref="Depth"/> steps, for
/// this session only. Pop-out-only changes are never pushed (the caller passes no label for them).
/// </summary>
public sealed class BoardUndo
{
    public const int Depth = 20;

    private readonly Dictionary<string, List<UndoStep>> _stacks = new(StringComparer.Ordinal);

    public void Push(BoardDef before, string what)
    {
        if (!_stacks.TryGetValue(before.Id, out var stack)) _stacks[before.Id] = stack = [];
        stack.Add(new UndoStep(before, what));
        if (stack.Count > Depth) stack.RemoveAt(0);
    }

    public UndoStep? Peek(string boardId) => _stacks.TryGetValue(boardId, out var stack) && stack.Count > 0 ? stack[^1] : null;

    public UndoStep? Pop(string boardId)
    {
        if (Peek(boardId) is not { } step) return null;
        _stacks[boardId].RemoveAt(_stacks[boardId].Count - 1);
        return step;
    }

    public int Count(string boardId) => _stacks.TryGetValue(boardId, out var stack) ? stack.Count : 0;

    public void Clear(string boardId) => _stacks.Remove(boardId);

    public void ClearAll() => _stacks.Clear();
}
```

- [ ] **Step 4:** Suite → exit 0. Commit `feat(board): BoardUndo, a per-board history of snapshots (BC6)`.

---

### Task 16: Undo wired through ChangeBoard, with Ctrl+Z and the toast

**Files:** `src/UI/BoardWindow.PanelSettings.cs`, `src/UI/BoardWindow.Editing.cs`, `src/UI/BoardWindow.PopOuts.cs:62`,
`src/UI/BoardWindow.xaml`, `src/UI/BoardWindow.xaml.cs`, `src/UI/BoardText.cs`, `tests/BoardTextTests.cs`

**Interfaces:**
- Consumes: `BoardUndo`, `UndoStep`.
- Produces: `ChangeBoard(Func<BoardDef, BoardDef> edit, string? what)` (null = not undoable);
  `BoardText.Undone(UndoStep step, bool unfollowed) : string`; `BoardText.Changed(string verb, string title) : string`;
  `BoardText.Arranged(string boardName) : string`; `BoardText.UndoNotSaved : string`.

- [ ] **Step 1: Failing text tests** (append to `BoardTextTests`)

```csharp
    [Fact]
    public void TheUndoToastSaysWhatWasDoneAndWhatUndoingDid()
    {
        var board = new BoardDef("b-starter-battle", "Battle", [], Follows: "battle");

        Assert.Equal("Removed Battle race", BoardText.Changed("Removed", "Battle race"));
        Assert.Equal("Arranged Battle", BoardText.Arranged("Battle"));
        Assert.Equal("Undid: Removed Battle race", BoardText.Undone(new UndoStep(board, "Removed Battle race"), unfollowed: false));

        // Spec §5.4: an undo that can't make a starter tab follow again says so.
        Assert.Equal("Restored, but Battle no longer follows your clans.", BoardText.Undone(new UndoStep(board, "Moved Race"), unfollowed: true));
    }
```

- [ ] **Step 2:** Run → FAIL.

- [ ] **Step 3: BoardText**

```csharp
    /// <summary>What a board change was, for the undo toast: "Removed Battle race".</summary>
    public static string Changed(string verb, string title) => $"{verb} {title}";

    /// <summary>A whole arranging session, which Done makes one undo step (spec §5.2).</summary>
    public static string Arranged(string boardName) => $"Arranged {boardName}";

    /// <summary>
    /// What an undo did. A starter tab re-follows only when the restored board matches what its starter draws now
    /// (Following.cs); when the sources changed in between it can't, and the toast says so (spec §5.4).
    /// </summary>
    public static string Undone(UndoStep step, bool unfollowed) =>
        unfollowed ? $"Restored, but {step.Before.Name} no longer follows your clans." : $"Undid: {step.What}";

    public const string UndoNotSaved = "That undo wasn't saved; the line above says why.";
```

- [ ] **Step 4: ChangeBoard takes a label, and pushes**

`BoardWindow.PanelSettings.cs`:

```csharp
    /// <summary>The saved boards' history, per board, for this session (BC6).</summary>
    private readonly BoardUndo _undo = new();

    /// <summary>The draft's own history while arranging; Done folds it into one step on <see cref="_undo"/>.</summary>
    private readonly BoardUndo _draftUndo = new();

    /// <summary>
    /// Applies one edit to the board on screen … (keep the existing summary) … <paramref name="what"/> names the change
    /// for undo ("Removed Battle race"); null for a change undo doesn't cover, such as a pop-out (spec §5.1).
    /// </summary>
    private void ChangeBoard(Func<BoardDef, BoardDef> edit, string? what)
    {
        if (_draft is { } draft)
        {
            var edited = edit(draft);
            if (ReferenceEquals(edited, draft)) return;

            if (what is not null) _draftUndo.Push(draft, what);
            _draft = edited;
            DoneButton.Content = BoardText.DoneLabel(_draftUndo.Count(draft.Id));
            Render();
            Dispatcher.BeginInvoke(ShowGrips, DispatcherPriority.Loaded);
            return;
        }

        var boards = _services.Boards;
        var board = ShownBoard(boards);
        var changed = edit(board);
        if (ReferenceEquals(changed, board)) return;

        if (SaveBoards(BoardEdits.Replace(boards, changed)))
        {
            if (what is not null)
            {
                _undo.Push(board, what);
                ShowToast(what, canUndo: true);
            }

            Render();
        }
    }
```

Delete `_draftSteps` (Task 13) and its three uses; Done's count is `_draftUndo.Count(board id)` now, and it is
cleared with `_draftUndo.ClearAll()` in `OnEditBoardClick`, `FinishEditing` and `OnCancelArrangeClick`.

Every caller passes a label. Title of a panel: `PanelGallery.TitleOf(def, _services.CurrentBoard())`.

| Call site | `what` |
|---|---|
| `OnEditTool` Remove | `BoardText.Changed("Removed", title)` |
| `OnBoardKeyDown` move | `BoardText.Changed("Moved", title)` |
| `OnBoardKeyDown` resize (both) | `BoardText.Changed("Resized", title)` |
| `EndResize` | `BoardText.Changed("Resized", title)` |
| `OnBoardDrop` | `BoardText.Changed("Moved", title)` (look up the def by `panelId` in `_draft`) |
| `OpenPanelSettings` | `BoardText.Changed("Changed", title)` |
| `AddPanelFromGallery` | `BoardText.Changed("Added", PanelGallery.Title(type, live))` |
| `BoardWindow.PopOuts.cs:62` | `null` |

Done folds the draft into one step. In `FinishEditing`, capture `var before = ShownBoard(_services.Boards);` at the
top (the board as saved, before the draft replaces it), and after a save that changed something (the
`!ReferenceEquals(finished, boards)` branch succeeding), push `_undo.Push(before, BoardText.Arranged(before.Name))` and
`ShowToast(BoardText.Arranged(before.Name), canUndo: true)`.

- [ ] **Step 5: Undo itself** (in `BoardWindow.PanelSettings.cs`)

```csharp
    /// <summary>
    /// Ctrl+Z and the toast's Undo (BC6). While arranging it only ever steps back through the draft — never past it into
    /// the saved history, which would save a change behind Arrange's back (Review Focus 5). Otherwise it restores the
    /// shown board's last snapshot through the same save every change uses; a save that fails leaves the history as it was.
    /// </summary>
    private void UndoLast()
    {
        // The one place the choice is made, and it is BoardUndo.Target's, which a unit test pins (Review Focus 5).
        var history = BoardUndo.Target(Editing, _draftUndo, _undo);

        if (_draft is { } draft)
        {
            if (history.Pop(draft.Id) is not { } draftStep) return;

            _draft = draftStep.Before;
            DoneButton.Content = BoardText.DoneLabel(_draftUndo.Count(draft.Id));
            Render();
            Dispatcher.BeginInvoke(ShowGrips, DispatcherPriority.Loaded);
            ShowToast(BoardText.Undone(draftStep, unfollowed: false), canUndo: false);
            return;
        }

        var boards = _services.Boards;
        var board = ShownBoard(boards);
        if (history.Peek(board.Id) is not { } step) return;

        if (!SaveBoards(BoardEdits.Replace(boards, step.Before)))
        {
            ShowToast(BoardText.UndoNotSaved, canUndo: false);
            return;
        }

        history.Pop(board.Id);
        Render();
        var now = _services.Boards.FirstOrDefault(b => b.Id == board.Id);
        ShowToast(BoardText.Undone(step, unfollowed: step.Before.Follows is not null && now?.Follows is null), canUndo: false);
    }
```

Add a unit-level guard for Review Focus 5 by making the arranging branch testable without WPF: add to `BoardUndo`

```csharp
    /// <summary>Which history an undo reads: the draft's while arranging, whatever the saved one holds (Review Focus 5).</summary>
    public static BoardUndo Target(bool arranging, BoardUndo draft, BoardUndo saved) => arranging ? draft : saved;
```

with the test, in `BoardUndoTests`,

```csharp
    [Fact]
    public void WhileArrangingOnlyTheDraftIsUndone()
    {
        var draft = new BoardUndo();
        var saved = new BoardUndo();
        saved.Push(Board("b-1", 1), "an earlier change");

        Assert.Same(draft, BoardUndo.Target(arranging: true, draft, saved));
        Assert.Null(BoardUndo.Target(arranging: true, draft, saved).Pop("b-1"));
        Assert.Equal(1, saved.Count("b-1"));
    }
```

(`UndoLast` above already reads its history through `Target`; add `Target` to `BoardUndo` in this step.)

`OnDeleteBoardClick`: after a successful save, `_undo.Clear(board.Id);`.

`OnWindowKeyDown` (Task 8) gains, before the F5 check:

```csharp
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            UndoLast();
            return;
        }
```

- [ ] **Step 6: The toast** (`BoardWindow.xaml`, inside the board's `<Grid>` after `EmptyState`)

```xml
            <!-- The undo toast (spec §5.3): themed, bottom centre, one at a time, about six seconds. A polite live region, so a
                 screen reader hears it; its Undo is a Tab stop while it shows, and Ctrl+Z works with or without it. -->
            <Border x:Name="UndoToast" Visibility="Collapsed" HorizontalAlignment="Center" VerticalAlignment="Bottom" Margin="0,0,0,16"
                    Background="{DynamicResource RowBgBrush}" BorderBrush="{DynamicResource EdgeBrush}" BorderThickness="1" CornerRadius="8" Padding="14,8">
                <StackPanel Orientation="Horizontal">
                    <TextBlock x:Name="UndoToastText" VerticalAlignment="Center" Foreground="{DynamicResource WhiteBrush}"
                               AutomationProperties.LiveSetting="Polite" AutomationProperties.AutomationId="UndoToastText" />
                    <Button x:Name="UndoToastButton" Content="Undo" Style="{StaticResource FlatButton}" Margin="12,0,0,0"
                            Foreground="{DynamicResource CyanBrush}" Click="OnUndoToastClick" AutomationProperties.Name="Undo" />
                </StackPanel>
            </Border>
```

`BoardWindow.xaml.cs`:

```csharp
    private readonly DispatcherTimer _toastClock = new() { Interval = TimeSpan.FromSeconds(6) };

    /// <summary>Shows the undo toast, replacing any other; a live region raises its change for a screen reader.</summary>
    private void ShowToast(string text, bool canUndo)
    {
        UndoToastText.Text = text;
        UndoToastButton.Visibility = canUndo ? Visibility.Visible : Visibility.Collapsed;
        UndoToast.Visibility = Visibility.Visible;
        var peer = UIElementAutomationPeer.FromElement(UndoToastText) ?? UIElementAutomationPeer.CreatePeerForElement(UndoToastText);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);

        _toastClock.Stop();
        _toastClock.Start();
    }

    private void OnUndoToastClick(object sender, RoutedEventArgs e) => UndoLast();
```

In the constructor: `_toastClock.Tick += (_, _) => { _toastClock.Stop(); UndoToast.Visibility = Visibility.Collapsed; };`
and in `Closed`: `_toastClock.Stop();`. Add `using System.Windows.Automation.Peers;`.

- [ ] **Step 7:** Suite → exit 0. Commit `feat(ui): undo — Ctrl+Z and a toast, per board; a whole Arrange is one step (BC6)`.

---

### Task 17: The undo walk, paper, release 0.6.2, and the always-on question

**Files:** `tools/smoke/walk-board-editing.ps1` (new steps), `README.md`, `CHANGELOG.md`, versions, `docs/backlog.md`.

- [ ] **Step 1: Walk steps** (append to walk-board-editing before the restart step)

```powershell
    # 12. Undo (BC6): outside Arrange, a Remove from the tools is undone by Ctrl+Z; a whole Arrange is one step.
    $before = @(Get-PanelIds (Get-BoardWindow))
    Enter-EditMode (Get-BoardWindow)
    Move-PanelEarlier (Get-BoardWindow) 'RacePanel1'
    Move-PanelEarlier (Get-BoardWindow) 'RacePanel1'
    Complete-EditMode (Get-BoardWindow)
    $toast = Line (Get-BoardWindow) 'UndoToastText'
    Check '12 Done says the arrangement can be undone' ($toast -match '^Arranged ') $toast
    (Get-BoardWindow).SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('^z')
    Start-Sleep -Milliseconds 1000
    Check '12b Ctrl+Z puts the whole arrangement back' ((@(Get-PanelIds (Get-BoardWindow)) -join ',') -eq ($before -join ',')) "$(@(Get-PanelIds (Get-BoardWindow)) -join ',')"

    # 12c. While arranging, Ctrl+Z steps back through the draft only.
    Enter-EditMode (Get-BoardWindow)
    Move-PanelEarlier (Get-BoardWindow) 'RacePanel1'
    [System.Windows.Forms.SendKeys]::SendWait('^z')
    Start-Sleep -Milliseconds 800
    Check '12c Ctrl+Z while arranging undoes the draft move' ((@(Get-PanelIds (Get-BoardWindow)) -join ',') -eq ($before -join ',')) "$(@(Get-PanelIds (Get-BoardWindow)) -join ',')"
    Check '12d ...and Done is back to no changes' ((Find-ByAutomationId (Get-BoardWindow) 'DoneButton').Current.Name -eq 'Done') (Find-ByAutomationId (Get-BoardWindow) 'DoneButton').Current.Name
    Complete-EditMode (Get-BoardWindow)
```

(`DoneButton`'s automation name is fixed to "Done" by `AutomationProperties.Name`; if 12d reads the name rather than
the content, assert on `Get-AllTexts` of the button instead. Check which the helper returns and use that.)

- [ ] **Step 2:** Run walk-board-editing, walk-starter-board, walk-top-bar → 0 failed; record.
- [ ] **Step 3:** README (undo, Ctrl+Z), CHANGELOG `## 0.6.2` (**Added**: undo for moving, resizing, adding, removing
and changing panels, per board, 20 steps; a whole Arrange is one step), versions `0.6.1` → `0.6.2`. Backlog: open a
row for BC4's re-ask ("always-on drag, now that undo exists: hold-to-pick-up, or always-on with the toast"), owner's
call. Suite exit 0; commit; PR; release on Este's word.
- [ ] **Step 4:** Tell Este undo has shipped and ask BC4's question again, with the two candidates in spec §5.6.

---

## Self-review record

- **Spec coverage:** §2 → Tasks 1–4; §3.1–3.5, 3.7 → Tasks 6, 8; §3.6 → Tasks 5, 7, 8; §3.8 → Task 9; §4.1–4.7 →
  Tasks 11–14; §5.1–5.6 → Tasks 15–17; §7 testing → each task's tests plus the walks; §8 risks → Review Focus.
- **Known deviations, stated where they occur:** `StatusChip` instead of `BoardText.ChipState` (Global Constraints);
  the grip keeps its six dots instead of the mockup's ✥ so `DragHandleTests` keeps checking a drawing (Task 12);
  the banner adds its height once when the lines were hidden (Task 13, flagged for the owner).
- **Names checked across tasks:** `StatusChip.StateOf/Enabled/Glyph/Word/Name/BrushKey/Title/ShowsLines/TabBudget`,
  `BoardText.Paused/InTrouble/CardRows/AlertsLine/AsksBeforeClose/ArrangingLine/DoneLabel/CancelArrangeQuestion/Changed/Arranged/Undone/UndoNotSaved`,
  `BoardButtons.StartsLater`, `BoardButtonStates.Cancel`, `BoardUndo.Push/Peek/Pop/Count/Clear/ClearAll/Target`,
  `ChangeBoard(edit, what)`, `FocusPanelLater(string)`, `PanelFrame.FocusFirstTool`, `PanelGrid.FirstRowHeightAt/NoGrips`,
  `BoardLayout.FirstRowHeight/GripAt`, `Settings.CurrentVersion/Migrate`.
