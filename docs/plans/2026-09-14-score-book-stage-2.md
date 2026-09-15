# Score book, stage 2: the panel system — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ur Score v0.3.0. The fixed starter board becomes boards you shape: several boards as tabs, an edit mode that adds, removes, moves and resizes panels, per-panel settings, a gallery of the ten panels, and panels that pop out into small always-on-top windows that come back where they were after a restart. Boards live in `boards.json`, which holds no other player's id, name or value.

**Architecture:**
- **Pure model (`src/Board`):** `BoardDef`/`PanelDef` and `BoardsFile` (load, save, crash tolerance, the unreadable-file copy), `BoardEdits` (every change as a function from old boards to new), `BoardLayout` (12 columns, tall panels, row heights, where a drop lands), `PanelForms` and `PanelGallery` (what a panel needs, what the form offers, what's wrong with a setting), `PopOutPlacement` (where a pop-out opens and staying on screen).
- **Composition:** `AppServices` loads `boards.json` once, hands out `Boards`, and saves through `SaveBoards`, which strips any account id that isn't yours. Until the first board change there is no file, and the board shown is the stage 1 starter, still following your sources.
- **WPF, kept thin:** `BoardWindow` is split into partial files by concern (tabs, editing, panel settings, pop-outs). `PanelFrame` raises one routed event for every panel tool. Small dialogs (`AddBoardWindow`, `BoardNameWindow`, `PanelGalleryWindow`, `PanelSettingsWindow`) and `PanelPopOutWindow` do no logic of their own.

**Tech Stack:** .NET 10 WPF (`net10.0-windows`), xUnit 2.9, System.Text.Json. No new packages.

**Spec:** `docs/2026-09-14-score-book-design.md`. Section numbers below (§n) refer to it. Stage 2 is §12 steps 9–11, built on §9.2 (boards), §9.3 (pop-outs), §9.4 (gallery and stale settings), §11 (smoke) and §13 Boards.

## Rulings made while planning

These are recorded here so a reviewer doesn't read them as drift from the spec. Each names what was decided, why, and the cost if it is wrong.

- **R1. No `boards.json` until the first board change.** While the file doesn't exist, the window shows the stage 1 starter board, rebuilt from your sources exactly as today (first run, import, main clan, the board fills in). The first board change (Done in edit mode, + Board, Rename, Duplicate, Delete, a panel's settings, a pop-out) writes the file, and from then on the saved boards are the truth. *Why:* writing a starter at first start would freeze an empty "Import a recipe" board forever. *Cost if wrong:* a user who pops out a panel before adding their second clan doesn't get the second Clan standing panel automatically; they add it from the gallery.
- **R2. The following starter's ids are fixed:** board `b-starter`, panels `p-starter-1`, `p-starter-2`, … in order. Materializing keeps them, so a pop-out made on the starter survives the write. Boards added later get random ids. *Cost if wrong:* none known; ids are opaque and `BoardsFile` repairs duplicates.
- **R3. An unreadable `boards.json` is never lost.** The session shows the starter board and the detail line says why. The first save copies the unreadable text to `boards.unreadable-<yyyyMMdd-HHmmss>.json` beside it, then writes. (Sources take the stricter path of refusing writes; a board layout is cheap to redo, and refusing would block all editing.) *Cost if wrong:* a user who hand-broke the file must copy their fix back from the kept copy.
- **R4. Tolerance is per panel, not per file.** A panel with an unknown `type`, a blank or duplicate id, a span outside 1–12, a missing size, or a pop-out with no width or height is repaired or dropped on its own; malformed JSON makes the whole file unreadable (R3). An empty list loads as "no file" (R1). *Cost if wrong:* a panel type from a newer version disappears after a save in this one.
- **R5. The size menu offers Small (3), Half (6) and Wide (12), plus a Tall tick.** Tall panels take two rows. Starter panels keep their 4 and 5 widths until the user picks a size; the menu then shows no size selected. *Why:* §8 uses 4 and 5 and the owner liked that board. *Cost if wrong:* one extra pick per panel.
- **R6. Tall panels flow like everything else.** Panels are placed in order at the first free spot at or after the previous panel's spot, so a tall panel's second row pushes later panels right. The order the user set is always reading order.
- **R7. Moving has buttons as well as dragging.** In edit mode each panel shows **Move earlier** and **Move later** next to the drag handle. *Why:* keyboard users, screen readers and the smoke walks can't drag. *Cost if wrong:* two small buttons per panel in edit mode only.
- **R8. Edit mode edits a draft.** **Done** saves it; there is no Cancel (the spec names none). Closing Ur Score while editing saves the draft too, since losing an arrangement is worse than keeping one. Tabs, + Board and the tab menu are disabled while editing. Panel settings changed from ⋯ while editing go into the draft.
- **R9. No drop marker.** Dragging shows the move cursor only; the drop position is worked out by `BoardLayout.DropIndex`. *Cost if wrong:* a later polish pass adds an insertion line.
- **R10. + Add panel lives in edit mode,** and on an empty board's empty state. From edit mode the new panel joins the draft; from the empty state it is saved at once.
- **R11. The last board can't be deleted.** Delete is disabled when only one board is left, and deleting asks for confirmation. Tabs can't be reordered: the spec lists click, + Board, Rename, Duplicate and Delete only.
- **R12. The board you last looked at isn't remembered.** Ur Score opens on the first tab. *Why:* `boards.json`'s shape is fixed by §9.2 and settings.json is not board state. *Cost if wrong:* one click per start.
- **R13. Duplicate names the copy "<name> copy",** gives every panel a new id, and drops its pop-outs (one panel is out once).
- **R14. What adding asks for, and what ⋯ adds.** Adding asks only for §9.4's "Needs" column. Where `PanelModels` also needs a stat, the stat is filled from the recipe's first ticked stat and appears only in ⋯ settings: Promotion check and Account card (stat), Past periods (stat for "your best account", optional), Profile stat (its source, defaulting to the recipe's first source). A type the defaults can't satisfy is shown in the gallery but disabled, with the reason.
- **R15. Forms offer ticked stats only** (Show or Send), since only those are read, plus a panel's current stat when the recipe still offers it. Race sources must all come from one recipe, because `PanelModels.Race` reads one headline total. Promotion check's *from* is a source your accounts are in (main or mine), and *to* is another source of the same recipe.
- **R16. A panel whose account is gone** keeps `PanelModels`' existing "No reading of your accounts yet." note. Only a removed source or stat shows the stale message with **Choose another** (§9.4), which opens ⋯ settings.
- **R17. Privacy on save:** `SaveBoards` removes any `userId` that isn't one of your known accounts, even when no accounts are known. The form only offers your accounts, so this only ever strips a hand-edited or stale id. *Cost if wrong:* an Account card pinned while RoRoRo had never listed accounts falls back to "Your top account".
- **R18. Pop-outs are not owned by the board,** so minimizing the board leaves them up. Closing a pop-out by its ✕ returns the panel and forgets the position (`popout` is removed, as §9.2's optional field implies); positions are kept for the rest of the session so the next ⧉ opens where it was. Closing Ur Score keeps every `popout` so they reopen at start. A saved position that is now off every screen is pulled back on.
- **R19. While a panel is out, its board shows a short placeholder** in its slot ("… is popped out" with **Bring back**), so the layout doesn't jump and there is a keyboard path back. Pop-outs show no ⧉ and no ⋯; settings are changed on the board. In edit mode a popped-out panel can be removed but not moved or resized; bring it back first.
- **R20. Pop-outs of every board reopen at start,** not only the first tab's. Deleting a board closes its pop-outs; removing a popped-out panel in edit mode closes its window.
- **R21. Release is v0.3.0** (instruction for this plan; spec §1.13 and §12 named 0.2.0 for both stages, and stage 1 shipped as 0.2.0). The repo is already public, so there is no visibility step.

---

## Global Constraints

- **No hostname literal in `src/`** except `NameClient.cs` (users.roblox.com) and `IconClient.cs` (thumbnails.roblox.com). `NoHostnameFenceTests` enforces it. Recipes and fixtures carry hosts; code never does.
- **Other players never reach disk.** Another player's Roblox id, name or value is never written to state, `sources.json`, `accounts.json`, the score book, the trail, diagnostics or the clipboard. Live leaderboards may show them in memory.
- **Keys never reach disk.** A saved key value never appears in files, errors, the trail or the clipboard. `Redactor` masks keys in any text that could carry one.
- **One path to RoRoRo.** `ReportPolicy.SendAsync` is the only caller of `IHostClient.ReportMetricAsync` (`ReportPolicyTests` enforces it).
- **Theme.** Themed brushes are referenced with `DynamicResource` only. No hex colours or literal brushes in `src/UI` XAML, and no colour code in UI `.cs` (`ThemeFenceTests`). Every brush used is one `ThemeService` paints.
- **Ur Score's own text never names a game.** Words like "clan" come from the recipe (input `plural`, labels).
- **Copy style:** sentence case, second person, specific, no emoji.
- **Other data files:** `sources.json`, `accounts.json`, `boards.json` (stage 2), all under `%LOCALAPPDATA%\626labs.ur-score\`.
- **Group-list recipes** are never matched to accounts, never sent, never recorded.
- **`watch` sources** send nothing and record no accounts.
- **Build gate:** `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror` passes, and `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build` passes. Run both at the end of every task.
- **Commits:** one or more per task, message style `area: what changed`, ending with the line `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

Added for stage 2:

- **`boards.json` holds no other player.** Board and panel names, types, sizes, order, recipe slugs, source ids, stat keys, pop-out rectangles, and `userId` only when it is one of your accounts (R17). Never a row, a value, a name read from a source, or a group name.
- **Pure first.** Layout math, the file model, edits, forms and placement live in `src/Board` with unit tests. Windows call them and draw; they decide nothing a test can't see.
- **Automation ids** in the table below are what `tools/smoke` relies on. A change to one changes its script in the same commit.

---

## File structure

New files:

| File | Responsibility |
|---|---|
| `src/Board/BoardDefs.cs` | `PanelSize`, `PopOutRect`, `PanelDef`, `BoardDef`, `BoardDefs` (ids, default sizes, the starter as a board, keys, privacy sanitizing) |
| `src/Board/BoardsFile.cs` | `BoardsLoad`, `BoardsFile` (`boards.json` load/save, per-panel repair, the unreadable copy) |
| `src/Board/BoardEdits.cs` | Every board and panel change, automation ids, the board's anchor source |
| `src/Board/PanelForms.cs` | `PanelField`, `FormChoice`, `FormValues`, `PanelForms` (fields, choices, defaults, building settings, problems) |
| `src/Board/PanelGallery.cs` | `GalleryCard`, `PanelGallery` (the ten cards, titles, whether each can be added) |
| `src/Board/PopOutPlacement.cs` | Where a pop-out opens, and pulling a saved one back on screen |
| `src/UI/Boards/AddBoardWindow.xaml(.cs)` | + Board: empty or from a starter |
| `src/UI/Boards/BoardNameWindow.xaml(.cs)` | Rename a board |
| `src/UI/Boards/PanelGalleryWindow.xaml(.cs)` | The gallery |
| `src/UI/Boards/PanelSettingsWindow.xaml(.cs)` | Adding a panel, and ⋯ settings |
| `src/UI/Boards/PanelPopOutWindow.xaml(.cs)` | One popped-out panel |
| `src/UI/BoardWindow.Editing.cs`, `BoardWindow.PanelSettings.cs`, `BoardWindow.PopOuts.cs` | The board window's partial files by concern |
| `tests/BoardsFileTests.cs`, `tests/BoardEditsTests.cs`, `tests/PanelFormsTests.cs`, `tests/PanelGalleryTests.cs`, `tests/PopOutPlacementTests.cs` | Unit tests |
| `tools/smoke/uia-board.ps1`, `tools/smoke/walk-board-editing.ps1`, `tools/smoke/walk-pop-outs.ps1`, `tools/smoke/check-boards-privacy.ps1` | Stage 2 walks |

Modified files:
- `src/Board/StarterBoards.cs` (`Build` takes an optional starter name; `BoardEmpty.NoPanels`);
- `src/Board/BoardLayout.cs`, `src/UI/Controls/PanelGrid.cs` (tall panels, row heights, drop index);
- `src/UI/BoardText.cs` (the empty-board state);
- `src/UI/Panels/PanelFrame.xaml(.cs)` (panel tools and the routed event);
- `src/Composition/AppServices.cs` (boards);
- `src/UI/BoardWindow.xaml(.cs)` (tabs, edit bar);
- `tests/BoardLayoutTests.cs`, `tests/StarterBoardsTests.cs`, `tests/BoardTextTests.cs`;
- `tools/smoke/README.md`; `manifest.json`, `Ur-Score.csproj` (release).

## Interface contract

Every task implements, or relies on, exactly these names. Namespaces: `Labs626.UrScore.Board` for the model, `Labs626.UrScore.UI` for windows and controls, `Labs626.UrScore.Composition` for `AppServices`. Stage 1's names (`PanelType`, `PanelSettings`, `LiveBoard`, `PanelModels`, `PanelViews`, `StarterBoards`, `StarterBoard`, `PanelSpec`, `BoardEmpty`, `BoardLayout`, `PanelPlacement`, `RecipeWords`, `BoardText`, `PanelGrid`, `PanelFrame`) are used as they are in the tree today.

```csharp
// ---- Task 1: definitions and boards.json (Labs626.UrScore.Board) ----
public sealed record PanelSize(int Span = 6, bool Tall = false) { public const int Small = 3, Half = 6, Wide = 12; }
public sealed record PopOutRect(double X, double Y, double W, double H);
public sealed record PanelDef(string Id, PanelType Type, PanelSize Size, PanelSettings Settings, PopOutRect? PopOut = null);
public sealed record BoardDef(string Id, string Name, IReadOnlyList<PanelDef> Panels);
public static class BoardDefs
{
    public const string StarterBoardId = "b-starter";
    public const int MaxNameLength = 40;
    public static string? CleanName(string? name);                                       // trimmed, cut to 40, null when blank
    public static string NewBoardId();                                                   // "b-" + 8 lowercase hex
    public static string NewPanelId();                                                   // "p-" + 8 lowercase hex
    public static PanelSize DefaultSize(PanelType type);                                 // Standing, AccountCard, Records small; LiveLeaderboard wide; the rest half
    public static BoardDef FromStarter(StarterBoard starter, bool freshIds);             // freshIds false: "b-starter", "p-starter-1".. (R2)
    public static string Key(BoardDef board);                                            // changes when anything drawn changes; a pop-out's position isn't drawn
    public static IReadOnlyList<BoardDef> Sanitize(IReadOnlyList<BoardDef> boards, IReadOnlySet<long> myUserIds);   // R17
}
public sealed record BoardsLoad(IReadOnlyList<BoardDef> Boards, bool Exists, bool Readable);
public sealed class BoardsFile(string path, TimeProvider time)
{
    public static string DefaultPath { get; }
    public BoardsLoad Load();
    public string? Save(IReadOnlyList<BoardDef> boards);                                // the kept copy's path when the old file was unreadable (R3), else null
    public static string Serialize(IReadOnlyList<BoardDef> boards);
    public static IReadOnlyList<BoardDef> Parse(string json);                            // throws JsonException on malformed JSON
}
// StarterBoards.Build gains a last parameter:  string? name = null   (StarterBoards.Battle, StarterBoards.Grind, or null for whichever fits)
// BoardEmpty gains:  NoPanels   (a saved board with no panels)

// ---- Task 2: edits (Labs626.UrScore.Board) ----
public static class BoardEdits
{
    public static string NextName(IReadOnlyList<BoardDef> boards);                       // "Board 2", the first number not taken
    public static IReadOnlyList<BoardDef> Add(IReadOnlyList<BoardDef> boards, BoardDef board);
    public static IReadOnlyList<BoardDef> Replace(IReadOnlyList<BoardDef> boards, BoardDef board);
    public static IReadOnlyList<BoardDef> Rename(IReadOnlyList<BoardDef> boards, string boardId, string name);
    public static IReadOnlyList<BoardDef> Duplicate(IReadOnlyList<BoardDef> boards, string boardId);   // R13, inserted after the original
    public static IReadOnlyList<BoardDef> Delete(IReadOnlyList<BoardDef> boards, string boardId);      // the last board stays (R11)
    public static BoardDef AddPanel(BoardDef board, PanelType type, PanelSettings settings);          // appended, default size, new id
    public static BoardDef RemovePanel(BoardDef board, string panelId);
    public static BoardDef MoveTo(BoardDef board, string panelId, int insertionIndex);   // insertion index counts the panel itself
    public static BoardDef MoveBy(BoardDef board, string panelId, int delta);
    public static BoardDef Resize(BoardDef board, string panelId, PanelSize size);
    public static BoardDef SetSettings(BoardDef board, string panelId, PanelSettings settings);
    public static BoardDef PopOut(BoardDef board, string panelId, PopOutRect rect);
    public static BoardDef Return(BoardDef board, string panelId);
    public static (BoardDef Board, PanelDef Panel)? Find(IReadOnlyList<BoardDef> boards, string panelId);
    public static IReadOnlyList<string> AutomationIds(BoardDef board);                   // "StandingPanel1", "StandingPanel2", "RacePanel1", ...
    public static string? AnchorSourceId(BoardDef board, IReadOnlyList<Source> sources);
}

// ---- Task 3: layout (Labs626.UrScore.Board) ----
// PanelPlacement gains a last parameter:  int Rows = 1
public sealed record CellRect(double Left, double Top, double Width, double Height);
// BoardLayout gains:
//   IReadOnlyList<PanelPlacement> Flow(IReadOnlyList<PanelSize> sizes, double width);   // the int overload stays and calls it
//   IReadOnlyList<double> RowHeights(IReadOnlyList<PanelPlacement> placements, IReadOnlyList<double> desired, double gap);
//   int DropIndex(IReadOnlyList<CellRect> cells, double x, double y);
// PanelGrid gains:  attached bool Tall;  int DropIndexAt(Point point)

// ---- Task 4: forms and gallery (Labs626.UrScore.Board) ----
public enum PanelField { Source, Sources, ToSource, Stat, Account }
public sealed record FormChoice(string Key, string Label);
public sealed record FormValues(string? Source = null, IReadOnlyList<string>? Sources = null, string? ToSource = null, string? Stat = null, string? Account = null);
public static class PanelForms
{
    public const char KeySeparator = (char)0x1F;
    public const string TopAccountKey = "";
    public static string StatKey(string recipe, string stat);
    public static (string Recipe, string Stat)? SplitStatKey(string? key);
    public static bool Fits(PanelType type, Recipe recipe);
    public static IReadOnlyList<PanelField> Fields(PanelType type, bool adding);
    public static IReadOnlyList<FormChoice> SourceChoices(PanelType type, PanelField field, LiveBoard live, FormValues values);
    public static IReadOnlyList<FormChoice> StatChoices(PanelType type, LiveBoard live, FormValues values, PanelSettings? current);
    public static IReadOnlyList<FormChoice> AccountChoices(LiveBoard live);
    public static FormValues Defaults(PanelType type, LiveBoard live);
    public static FormValues From(PanelSettings settings);
    public static PanelSettings Build(PanelType type, FormValues values, LiveBoard live);
    public static string? Problem(PanelType type, PanelSettings settings, LiveBoard live);
}
public sealed record GalleryCard(PanelType Type, string Title, string Needs, string Shows, bool CanAdd, string WhyNot);
public static class PanelGallery
{
    public static IReadOnlyList<PanelType> Order { get; }                                // spec §9.4 table order
    public static string Title(PanelType type, LiveBoard live);
    public static IReadOnlyList<GalleryCard> Cards(LiveBoard live);
}

// ---- Task 5: boards in the composition root (Labs626.UrScore.Composition) ----
// AppServices gains:
//   IReadOnlyList<BoardDef> Boards { get; }          // saved boards, else [the following starter] (R1)
//   bool BoardsFollowStarter { get; }
//   string? BoardsProblem { get; }                   // set when boards.json couldn't be read (R3)
//   void SaveBoards(IReadOnlyList<BoardDef> boards); // sanitizes (R17), saves, raises Changed; throws when the file can't be written
// BoardText.EmptyState handles BoardEmpty.NoPanels

// ---- Task 6: panel tools (Labs626.UrScore.UI) ----
public enum PanelTool { PopOut, Settings, ChooseAnother, DragStart, MoveEarlier, MoveLater, Resize, Remove }
public sealed class PanelToolEventArgs(RoutedEvent routedEvent, PanelTool tool, PanelSize? size = null) : RoutedEventArgs(routedEvent)
{ public PanelTool Tool { get; } public PanelSize? Size { get; } }
// PanelFrame gains:
//   static RoutedEvent ToolEvent   (bubbling, handler type EventHandler<PanelToolEventArgs>)
//   attached inherited bools ShowEditTools, ShowSettings, ShowPopOut  (Get/Set methods)
//   attached inherited PanelSize? CurrentSize  (the size box shows it)

// ---- Task 7: edit mode ----
// BoardEdits gains:  public static bool Changed(BoardDef before, BoardDef after);   // anything drawn, or the name, differs

// ---- Task 8: pop-outs (Labs626.UrScore.Board) ----
public static class PopOutPlacement
{
    public const double MinWidth = 260, MinHeight = 180, DefaultWidth = 360, DefaultHeight = 300, Cascade = 28;
    public static PopOutRect Clamp(PopOutRect rect, PopOutRect screen);
    public static PopOutRect Default(PopOutRect board, int openCount, PopOutRect screen);
}
```

## Automation ids

Stage 1's table (`docs/plans/2026-09-14-score-book-stage-1.md`, before Task 10) still holds. Stage 2 adds:

| Window | Automation ids |
|---|---|
| Board (`RoRoRo Ur Score`) | `BoardTabs` (list items named by board name; context menu items `RenameBoardItem`, `DuplicateBoardItem`, `DeleteBoardItem`), `AddBoardButton`, `EditBoardButton`, `AddPanelButton`, `DoneButton`; a popped-out panel's slot `<Type>Panel<n>PoppedOut` with a button named `Bring back <title>` |
| Inside a panel (on the board) | `DragHandle`, `MoveEarlierButton`, `MoveLaterButton`, `SizeBox` (items `Small`, `Half`, `Wide`), `TallBox`, `PanelSettingsButton`, `RemovePanelButton`, `PopOutButton`, `ChooseAnotherButton` |
| Add a board (`Add a board`) | `NewBoardNameBox`, `EmptyBoardButton`, `BattleBoardButton`, `GrindBoardButton`, `StarterLine` |
| Rename board (`Rename board`) | `BoardNameBox`, `SaveNameButton`, `NameProblemLine` |
| Gallery (`Add a panel`) | `GalleryCards` (buttons named `Add <title>`, their reasons `WhyNot` inside each card) |
| Panel settings (`Add <title>` / `Panel settings`) | `SourceBox`, `RaceSources` (checkboxes named `Race <source>`), `ToSourceBox`, `StatBox`, `AccountBox`, `SettingsProblemLine`, `SaveSettingsButton` |
| Pop-out (window title = the panel's title; window automation id `PopOutWindow`) | `PopOutTitle`, `ReturnPanelButton`; the panel inside keeps its board id `<Type>Panel<n>` |

---

### Task 1: Board definitions and `boards.json`

Spec §9.2 (the file's shape), §13 Boards (round-trip, starter boards from the sources present), rulings R1–R4, R17. Pure model and file only; nothing draws a saved board until Task 5.

`boards.json` is `[{id, name, panels: [{id, type, size: {span, tall}, order, settings, popout?: {x, y, w, h}}]}]`. `type` is the camel-case panel type (`standing`, `myAccounts`, …). `settings` is stage 1's `PanelSettings` in camel case with nulls left out.

**Files:**
- Create: `src/Board/BoardDefs.cs`, `src/Board/BoardsFile.cs`
- Create: `tests/BoardsFileTests.cs`
- Modify: `src/Board/StarterBoards.cs` (`Build` takes an optional name; `BoardEmpty.NoPanels`)
- Modify: `tests/StarterBoardsTests.cs`

**Interfaces:**
- Consumes: `PanelType`, `PanelSettings`, `StarterBoard`, `PanelSpec`, `StarterBoards`, `BoardLayout.Columns` (stage 1).
- Produces: `PanelSize`, `PopOutRect`, `PanelDef`, `BoardDef`, `BoardDefs`, `BoardsLoad`, `BoardsFile`, `StarterBoards.Build(..., string? name = null)`, `BoardEmpty.NoPanels`, exactly as in the contract.

- [ ] **Step 1: Write the failing tests**

Create `tests/BoardsFileTests.cs`:

```csharp
using System.Text.Json;
using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

public class BoardsFileTests
{
    private static readonly Source MainClan = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
    private static readonly Source AltClan = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);

    private static BoardDef Battle() => new("b-0000aaaa", "Battle",
    [
        new PanelDef("p-00000001", PanelType.Standing, new PanelSize(3), new PanelSettings(Clan.Slug, SourceId: MainClan.Id)),
        new PanelDef("p-00000002", PanelType.Race, new PanelSize(6, Tall: true),
            new PanelSettings(Clan.Slug, SourceIds: new List<string> { MainClan.Id, AltClan.Id }), new PopOutRect(1400, 80, 360, 300)),
        new PanelDef("p-00000003", PanelType.AccountCard, new PanelSize(3), new PanelSettings(Clan.Slug, Stat: "value", UserId: Main.RobloxUserId)),
    ]);

    [Fact]
    public void BoardsRoundTripThroughTheFile()
    {
        using var dir = TempDir.Create("urscore-boards");
        var file = new BoardsFile(Path.Combine(dir.Path, "boards.json"), new FixedTime(Now));
        var second = new BoardDef("b-0000bbbb", "Rivals", []);

        Assert.Null(file.Save([Battle(), second]));
        var load = file.Load();

        Assert.True(load.Exists);
        Assert.True(load.Readable);
        Assert.Equal(2, load.Boards.Count);
        AssertSameBoard(Battle(), load.Boards[0]);
        AssertSameBoard(second, load.Boards[1]);
        Assert.False(File.Exists(Path.Combine(dir.Path, "boards.json.tmp")));
    }

    [Fact]
    public void TheFileIsCamelCaseWithAnOrderAndNoEmptyFields()
    {
        var json = BoardsFile.Serialize([Battle()]);

        Assert.Contains("\"type\": \"standing\"", json);
        Assert.Contains("\"type\": \"accountCard\"", json);
        Assert.Contains("\"order\": 2", json);
        Assert.Contains("\"tall\": true", json);
        Assert.Contains("\"popout\": {", json);
        Assert.Single(json.Split("\"popout\"").Skip(1));
        Assert.DoesNotContain("\"toSourceId\"", json);
    }

    [Fact]
    public void AMissingFileIsNoBoardsAndStillReadable()
    {
        using var dir = TempDir.Create("urscore-boards");

        var load = new BoardsFile(Path.Combine(dir.Path, "boards.json"), new FixedTime(Now)).Load();

        Assert.False(load.Exists);
        Assert.True(load.Readable);
        Assert.Empty(load.Boards);
        Assert.Empty(BoardsFile.Parse("[]"));
    }

    [Fact]
    public void AnUnreadableFileIsKeptBesideTheNewOneOnTheFirstSave()
    {
        using var dir = TempDir.Create("urscore-boards");
        var path = Path.Combine(dir.Path, "boards.json");
        const string Broken = "[{ \"id\": \"b-1\", \"panels\": [ ";
        File.WriteAllText(path, Broken);
        var file = new BoardsFile(path, new FixedTime(Now));

        var load = file.Load();
        Assert.True(load.Exists);
        Assert.False(load.Readable);

        var kept = file.Save([Battle()]);

        Assert.Equal(Path.Combine(dir.Path, "boards.unreadable-20260919-180000.json"), kept);
        Assert.Equal(Broken, File.ReadAllText(kept!));
        Assert.True(file.Load().Readable);
        Assert.Null(file.Save([Battle()]));
    }

    [Fact]
    public void MalformedJsonIsNotParsed() =>
        Assert.ThrowsAny<JsonException>(() => BoardsFile.Parse("{ \"id\": 1 }"));

    [Fact]
    public void EachPanelIsRepairedOrDroppedOnItsOwn()
    {
        const string Json = """
            [
              { "id": "b-dup", "name": "  ", "panels": [
                  { "id": "p-a", "type": "standing", "size": { "span": 30 }, "order": 2, "settings": { "recipe": "r", "sourceId": "s-1" } },
                  { "id": "p-a", "type": "race", "order": 1, "settings": { "recipe": "r", "sourceIds": ["s-1", "", "s-2"] } },
                  { "id": "p-c", "type": "hologram", "order": 0 },
                  { "type": "3", "order": 3 },
                  { "id": "p-d", "type": "top", "order": 4, "popout": { "x": 10, "y": 10, "w": 0, "h": 200 } },
                  { "id": "p-e", "type": "records", "order": 5 }
              ] },
              { "id": "b-dup", "name": "Second", "panels": null },
              null
            ]
            """;

        var boards = BoardsFile.Parse(Json);

        Assert.Equal(2, boards.Count);
        Assert.Equal(("b-dup", "Board 1"), (boards[0].Id, boards[0].Name));
        Assert.Matches("^b-[0-9a-f]{8}$", boards[1].Id);
        Assert.Equal("Second", boards[1].Name);
        Assert.Empty(boards[1].Panels);

        var panels = boards[0].Panels;
        Assert.Equal(new[] { PanelType.Race, PanelType.Standing, PanelType.Top, PanelType.Records }, panels.Select(p => p.Type).ToArray());
        Assert.Equal("p-a", panels[0].Id);
        Assert.Matches("^p-[0-9a-f]{8}$", panels[1].Id);
        Assert.Equal(new[] { "s-1", "s-2" }, panels[0].Settings.SourceIds!.ToArray());
        Assert.Equal(BoardDefs.DefaultSize(PanelType.Race), panels[0].Size);
        Assert.Equal(new PanelSize(12), panels[1].Size);
        Assert.Null(panels[2].PopOut);
        Assert.Equal(new PanelSettings(), panels[3].Settings);
    }

    [Fact]
    public void SanitizingKeepsYourAccountsAndDropsAnyOtherId()
    {
        const long Stranger = 987654321;
        var board = Battle();
        var withStranger = board with
        {
            Panels = [.. board.Panels, new PanelDef("p-00000004", PanelType.AccountCard, new PanelSize(3), new PanelSettings(Clan.Slug, Stat: "value", UserId: Stranger))],
        };

        var clean = BoardDefs.Sanitize([withStranger], Accounts.Select(a => a.RobloxUserId).ToHashSet());

        Assert.Equal(Main.RobloxUserId, clean[0].Panels[2].Settings.UserId);
        Assert.Null(clean[0].Panels[3].Settings.UserId);
        Assert.DoesNotContain("987654321", BoardsFile.Serialize(clean));
        Assert.Null(BoardDefs.Sanitize([board], new HashSet<long>())[0].Panels[2].Settings.UserId);
    }

    [Fact]
    public void TheFollowingStarterHasFixedIdsAndAFreshCopyHasNewOnes()
    {
        var starter = StarterBoards.Build([Installed(Clan, "value")], [MainClan, AltClan]);

        var following = BoardDefs.FromStarter(starter, freshIds: false);
        var fresh = BoardDefs.FromStarter(starter, freshIds: true);

        Assert.Equal(BoardDefs.StarterBoardId, following.Id);
        Assert.Equal(StarterBoards.Battle, following.Name);
        Assert.Equal(starter.Panels.Select((_, i) => $"p-starter-{i + 1}"), following.Panels.Select(p => p.Id));
        Assert.Equal(starter.Panels.Select(p => (p.Type, p.Span, p.Settings)), following.Panels.Select(p => (p.Type, p.Size.Span, p.Settings)));
        Assert.All(following.Panels, p => Assert.False(p.Size.Tall));
        Assert.Matches("^b-[0-9a-f]{8}$", fresh.Id);
        Assert.All(fresh.Panels, p => Assert.Matches("^p-[0-9a-f]{8}$", p.Id));
    }

    [Fact]
    public void TheKeyFollowsWhatIsDrawnButNotWhereAPopOutSits()
    {
        var board = Battle();
        var moved = board with { Panels = [.. board.Panels.Select(p => p.PopOut is null ? p : p with { PopOut = new PopOutRect(10, 10, 400, 300) })] };
        var returned = board with { Panels = [.. board.Panels.Select(p => p with { PopOut = null })] };
        var resized = board with { Panels = [board.Panels[0] with { Size = new PanelSize(6) }, .. board.Panels.Skip(1)] };

        Assert.Equal(BoardDefs.Key(board), BoardDefs.Key(moved));
        Assert.NotEqual(BoardDefs.Key(board), BoardDefs.Key(returned));
        Assert.NotEqual(BoardDefs.Key(board), BoardDefs.Key(resized));
    }

    [Theory]
    [InlineData("  Rivals  ", "Rivals")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    [InlineData("A name that is much longer than forty characters in all", "A name that is much longer than forty ch")]
    public void NamesAreTrimmedAndCut(string? given, string? expected) => Assert.Equal(expected, BoardDefs.CleanName(given));

    private static void AssertSameBoard(BoardDef expected, BoardDef actual)
    {
        Assert.Equal((expected.Id, expected.Name, expected.Panels.Count), (actual.Id, actual.Name, actual.Panels.Count));
        for (var i = 0; i < expected.Panels.Count; i++)
        {
            var (e, a) = (expected.Panels[i], actual.Panels[i]);
            Assert.Equal((e.Id, e.Type, e.Size, e.PopOut), (a.Id, a.Type, a.Size, a.PopOut));
            Assert.Equal(e.Settings with { SourceIds = null }, a.Settings with { SourceIds = null });
            Assert.Equal(e.Settings.SourceIds ?? Array.Empty<string>(), a.Settings.SourceIds ?? Array.Empty<string>());
        }
    }
}
```

Add to `tests/StarterBoardsTests.cs`, inside the class, after `TheKeyChangesOnlyWhenThePanelsDo`:

```csharp
    [Fact]
    public void AStarterCanBeAskedForByName()
    {
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);
        InstalledRecipe[] installed = [Installed(Clan, "value"), Installed(Profile, "diamonds")];
        Source[] sources = [MainClan, profile];

        Assert.Equal(StarterBoards.Battle, StarterBoards.Build(installed, sources).Name);

        var grind = StarterBoards.Build(installed, sources, StarterBoards.Grind);
        Assert.Equal(StarterBoards.Grind, grind.Name);
        Assert.Equal(PanelType.ProfileStat, grind.Panels[0].Type);
        Assert.Equal(Profile.Slug, grind.Panels[0].Settings.Recipe);

        Assert.Empty(StarterBoards.Build([Installed(Clan, "value")], [MainClan], StarterBoards.Grind).Panels);
        Assert.Empty(StarterBoards.Build([Installed(Profile, "diamonds")], [profile], StarterBoards.Battle).Panels);
    }
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: FAIL with CS0246 for `BoardDef`, `PanelDef`, `PanelSize`, `BoardsFile` and CS1501 for the three-argument `StarterBoards.Build`.

- [ ] **Step 3: Create the definitions**

Create `src/Board/BoardDefs.cs`:

```csharp
using System.Security.Cryptography;

namespace Labs626.UrScore.Board;

/// <summary>A panel's width in the 12-column grid, and whether it takes two rows (spec §9.2, R5).</summary>
public sealed record PanelSize(int Span = 6, bool Tall = false)
{
    public const int Small = 3;
    public const int Half = 6;
    public const int Wide = 12;
}

/// <summary>Where a popped-out panel's window sits, in device-independent pixels (spec §9.3).</summary>
public sealed record PopOutRect(double X, double Y, double W, double H);

/// <summary>One panel on a saved board. Its place in <see cref="BoardDef.Panels"/> is its order.</summary>
public sealed record PanelDef(string Id, PanelType Type, PanelSize Size, PanelSettings Settings, PopOutRect? PopOut = null);

/// <summary>One tab (spec §9.2). Holds no other player: see <see cref="BoardDefs.Sanitize"/>.</summary>
public sealed record BoardDef(string Id, string Name, IReadOnlyList<PanelDef> Panels);

public static class BoardDefs
{
    /// <summary>The following starter's board id (R2).</summary>
    public const string StarterBoardId = "b-starter";

    public const int MaxNameLength = 40;

    public static string NewBoardId() => "b-" + Hex();

    public static string NewPanelId() => "p-" + Hex();

    /// <summary>Trimmed and cut to <see cref="MaxNameLength"/>; null when nothing is left.</summary>
    public static string? CleanName(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0) return null;
        return trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength].TrimEnd() : trimmed;
    }

    /// <summary>What a panel added from the gallery starts at.</summary>
    public static PanelSize DefaultSize(PanelType type) => type switch
    {
        PanelType.Standing or PanelType.AccountCard or PanelType.Records => new PanelSize(PanelSize.Small),
        PanelType.LiveLeaderboard => new PanelSize(PanelSize.Wide),
        _ => new PanelSize(PanelSize.Half),
    };

    /// <summary>
    /// A starter board as a saved board. The following starter (freshIds false) keeps fixed ids, so a pop-out
    /// made on it survives the first write (R2); a starter added with + Board gets new ones.
    /// </summary>
    public static BoardDef FromStarter(StarterBoard starter, bool freshIds) => new(
        freshIds ? NewBoardId() : StarterBoardId,
        starter.Name,
        [.. starter.Panels.Select((panel, index) => new PanelDef(
            freshIds ? NewPanelId() : $"p-starter-{index + 1}",
            panel.Type,
            new PanelSize(Math.Clamp(panel.Span, 1, BoardLayout.Columns)),
            panel.Settings))]);

    /// <summary>Changes exactly when the board's panels are drawn differently. Where a pop-out window sits is not drawn on the board.</summary>
    public static string Key(BoardDef board) =>
        board.Id + "#" + string.Join("|", board.Panels.Select(p =>
            $"{p.Id}:{p.Type}:{p.Size.Span}:{p.Size.Tall}:{p.Settings.Recipe}:{p.Settings.SourceId}:{string.Join(",", p.Settings.SourceIds ?? [])}:{p.Settings.ToSourceId}:{p.Settings.Stat}:{p.Settings.UserId}:{p.PopOut is not null}"));

    /// <summary>
    /// The privacy rule for <c>boards.json</c> (R17): an account id stays only when it is one of yours. The
    /// forms only offer your accounts, so this strips a hand-edited or stale id and nothing else.
    /// </summary>
    public static IReadOnlyList<BoardDef> Sanitize(IReadOnlyList<BoardDef> boards, IReadOnlySet<long> myUserIds) =>
        [.. boards.Select(board => board with
        {
            Panels = [.. board.Panels.Select(panel => panel.Settings.UserId is { } id && !myUserIds.Contains(id)
                ? panel with { Settings = panel.Settings with { UserId = null } }
                : panel)],
        })];

    private static string Hex() => Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
}
```

- [ ] **Step 4: Create the file store**

Create `src/Board/BoardsFile.cs`:

```csharp
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Labs626.UrScore.Board;

/// <summary>What loading <c>boards.json</c> found: a file that isn't there is not the same as one that can't be read.</summary>
public sealed record BoardsLoad(IReadOnlyList<BoardDef> Boards, bool Exists, bool Readable);

/// <summary>
/// <c>boards.json</c> (spec §9.2). Written to a temp file and moved over, so a crash mid-write leaves the old
/// file. Repair is per panel (R4); malformed JSON is unreadable and its text is kept on the next save (R3).
/// </summary>
public sealed class BoardsFile(string path, TimeProvider time)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "626labs.ur-score", "boards.json");

    public BoardsLoad Load()
    {
        try
        {
            if (!File.Exists(path)) return new BoardsLoad([], Exists: false, Readable: true);
            return new BoardsLoad(Parse(File.ReadAllText(path)), Exists: true, Readable: true);
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return new BoardsLoad([], Exists: true, Readable: false);
        }
    }

    /// <summary>Writes the boards. Returns where an unreadable old file was kept, or null. Throws when the folder can't be written.</summary>
    public string? Save(IReadOnlyList<BoardDef> boards)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var kept = KeepUnreadable();

        var temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(boards));
        File.Move(temp, path, overwrite: true);
        return kept;
    }

    public static string Serialize(IReadOnlyList<BoardDef> boards) =>
        JsonSerializer.Serialize(boards.Select(board => new BoardDto
        {
            Id = board.Id,
            Name = board.Name,
            Panels = [.. board.Panels.Select((panel, index) => new PanelDto
            {
                Id = panel.Id,
                Type = JsonNamingPolicy.CamelCase.ConvertName(panel.Type.ToString()),
                Size = new SizeDto { Span = panel.Size.Span, Tall = panel.Size.Tall },
                Order = index,
                Settings = panel.Settings,
                Popout = panel.PopOut,
            })],
        }).ToList(), Options);

    public static IReadOnlyList<BoardDef> Parse(string json)
    {
        var dtos = JsonSerializer.Deserialize<List<BoardDto?>>(json, Options) ?? new List<BoardDto?>();
        var boards = new List<BoardDef>();
        var boardIds = new HashSet<string>(StringComparer.Ordinal);
        var panelIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var dto in dtos.OfType<BoardDto>())
        {
            var boardId = UniqueId(dto.Id, boardIds, BoardDefs.NewBoardId);
            var panels = new List<PanelDef>();

            foreach (var panel in (dto.Panels ?? new List<PanelDto?>()).OfType<PanelDto>().OrderBy(p => p.Order))
            {
                if (TypeOf(panel.Type) is not { } type) continue;

                panels.Add(new PanelDef(
                    UniqueId(panel.Id, panelIds, BoardDefs.NewPanelId),
                    type,
                    panel.Size is { Span: > 0 } size ? new PanelSize(Math.Clamp(size.Span, 1, BoardLayout.Columns), size.Tall) : BoardDefs.DefaultSize(type),
                    CleanSettings(panel.Settings),
                    ValidPopOut(panel.Popout)));
            }

            boards.Add(new BoardDef(boardId, BoardDefs.CleanName(dto.Name) ?? $"Board {boards.Count + 1}", panels));
        }

        return boards;
    }

    /// <summary>A file that no longer parses is copied aside before it is written over (R3).</summary>
    private string? KeepUnreadable()
    {
        if (!File.Exists(path)) return null;

        var text = File.ReadAllText(path);
        try
        {
            Parse(text);
            return null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            var stamp = time.GetUtcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var copy = Path.Combine(Path.GetDirectoryName(path)!, $"boards.unreadable-{stamp}.json");
            File.WriteAllText(copy, text);
            return copy;
        }
    }

    /// <summary>A known type by name only: a number or a flags list could name a type by accident.</summary>
    private static PanelType? TypeOf(string? text) =>
        text is { Length: > 0 } && char.IsAsciiLetter(text[0]) && !text.Contains(',')
        && Enum.TryParse<PanelType>(text, ignoreCase: true, out var type) && Enum.IsDefined(type)
            ? type
            : null;

    private static string UniqueId(string? given, HashSet<string> taken, Func<string> fresh)
    {
        var id = given?.Trim();
        if (!string.IsNullOrEmpty(id) && taken.Add(id)) return id;

        do
        {
            id = fresh();
        }
        while (!taken.Add(id));

        return id;
    }

    private static PanelSettings CleanSettings(PanelSettings? settings) =>
        settings is null
            ? new PanelSettings()
            : settings with
            {
                Recipe = settings.Recipe ?? "",
                SourceIds = settings.SourceIds?.Where(id => !string.IsNullOrWhiteSpace(id)).ToList(),
            };

    private static PopOutRect? ValidPopOut(PopOutRect? rect) =>
        rect is { W: > 0.0, H: > 0.0 } r && double.IsFinite(r.X) && double.IsFinite(r.Y) && double.IsFinite(r.W) && double.IsFinite(r.H)
            ? r
            : null;

    private sealed class BoardDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public List<PanelDto?>? Panels { get; set; }
    }

    private sealed class PanelDto
    {
        public string? Id { get; set; }

        public string? Type { get; set; }

        public SizeDto? Size { get; set; }

        public int Order { get; set; }

        public PanelSettings? Settings { get; set; }

        public PopOutRect? Popout { get; set; }
    }

    private sealed class SizeDto
    {
        public int Span { get; set; }

        public bool Tall { get; set; }
    }
}
```

- [ ] **Step 5: Let a starter be asked for by name**

In `src/Board/StarterBoards.cs`, change the enum:

```csharp
public enum BoardEmpty { None, NoRecipes, NoStats, NoSources, NoPanels }
```

Replace the whole `Build` method with:

```csharp
    /// <summary>
    /// Stage 1's board, or a named starter for + Board (spec §9.2). With no name, Battle when a recipe with a
    /// period has ticked stats, else Grind. A named starter that can't be built has no panels.
    /// </summary>
    public static StarterBoard Build(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources, string? name = null)
    {
        if (installed.Count == 0) return new StarterBoard(name ?? Battle, BoardEmpty.NoRecipes, [], null, null);

        var ticked = installed.Where(i => !i.Recipe.IsGroupList && i.State.TrackedStats(i.Recipe).Count > 0).ToList();
        if (ticked.Count == 0)
        {
            var first = installed.FirstOrDefault(i => !i.Recipe.IsGroupList) ?? installed[0];
            return new StarterBoard(name ?? Battle, BoardEmpty.NoStats, [], null, first.Recipe.Slug);
        }

        var enabled = sources.Where(s => s.Enabled).ToList();
        var withPeriod = ticked.Where(i => i.Recipe.Period is not null).ToList();
        var withoutPeriod = ticked.Where(i => i.Recipe.Period is null).ToList();

        return name switch
        {
            Battle when withPeriod.Count == 0 => new StarterBoard(Battle, BoardEmpty.NoStats, [], null, ticked[0].Recipe.Slug),
            Battle => BattleBoard(installed, withPeriod, enabled),
            Grind when withoutPeriod.Count == 0 => new StarterBoard(Grind, BoardEmpty.NoStats, [], null, ticked[0].Recipe.Slug),
            Grind => GrindBoard(withoutPeriod, enabled),
            _ => withPeriod.Count > 0 ? BattleBoard(installed, withPeriod, enabled) : GrindBoard(ticked, enabled),
        };
    }
```

- [ ] **Step 6: Run the gates**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: 0 warnings, 0 errors.

Run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~BoardsFileTests|FullyQualifiedName~StarterBoardsTests"`
Expected: all pass.

Run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: all pass (the fences included).

- [ ] **Step 7: Commit**

```text
git add src/Board/BoardDefs.cs src/Board/BoardsFile.cs src/Board/StarterBoards.cs tests/BoardsFileTests.cs tests/StarterBoardsTests.cs
git commit -m "board: boards.json, saved boards and panels repaired one by one, and only your own account ids

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: Board edits

Spec §9.2 (tabs: add, rename, duplicate, delete; edit mode: add, remove, move, resize; settings), §9.3 (pop out, return), rulings R11, R13. Every change is a pure function from the old boards to the new, so the window only calls and saves. An edit that doesn't apply (an unknown id, a blank name, the last board) returns its input unchanged, the same instance, so the window can skip a save.

**Files:**
- Create: `src/Board/BoardEdits.cs`
- Create: `tests/BoardEditsTests.cs`

**Interfaces:**
- Consumes: Task 1 (`BoardDef`, `PanelDef`, `PanelSize`, `PopOutRect`, `BoardDefs.NewBoardId`/`NewPanelId`/`CleanName`/`DefaultSize`); `Source`, `SourceRole` (stage 1).
- Produces: `BoardEdits`, exactly as in the contract.

- [ ] **Step 1: Write the failing tests**

Create `tests/BoardEditsTests.cs`:

```csharp
using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

public class BoardEditsTests
{
    private static BoardDef BoardOf(string id, params PanelType[] types) => new(id, id,
        [.. types.Select((type, i) => new PanelDef($"p-{id}-{i + 1}", type, BoardDefs.DefaultSize(type), new PanelSettings(Clan.Slug)))]);

    private static string[] Ids(BoardDef board) => [.. board.Panels.Select(p => p.Id)];

    [Fact]
    public void ANewBoardTakesTheFirstFreeNumber()
    {
        IReadOnlyList<BoardDef> boards = [BoardOf("b-1") with { Name = "Battle" }, BoardOf("b-2") with { Name = "board 3" }];

        Assert.Equal("Board 4", BoardEdits.NextName(boards));
        Assert.Equal("Board 2", BoardEdits.NextName([BoardOf("b-1") with { Name = "Battle" }]));
    }

    [Fact]
    public void AddAndReplaceKeepTheOrder()
    {
        IReadOnlyList<BoardDef> boards = [BoardOf("b-1"), BoardOf("b-2")];

        var added = BoardEdits.Add(boards, BoardOf("b-3"));
        var replaced = BoardEdits.Replace(added, BoardOf("b-2") with { Name = "Rivals" });

        Assert.Equal(new[] { "b-1", "b-2", "b-3" }, added.Select(b => b.Id).ToArray());
        Assert.Equal("Rivals", replaced[1].Name);
    }

    [Fact]
    public void RenameTrimsAndABlankNameChangesNothing()
    {
        IReadOnlyList<BoardDef> boards = [BoardOf("b-1"), BoardOf("b-2")];

        Assert.Equal("Rivals", BoardEdits.Rename(boards, "b-2", "  Rivals ")[1].Name);
        Assert.Same(boards, BoardEdits.Rename(boards, "b-2", "   "));
        Assert.Same(boards, BoardEdits.Rename(boards, "b-9", "Rivals"));
    }

    [Fact]
    public void DuplicateGoesRightAfterWithNewIdsAndNoPopOuts()
    {
        var original = BoardOf("b-1", PanelType.Standing, PanelType.Race) with { Name = "Battle" };
        original = BoardEdits.PopOut(original, "p-b-1-2", new PopOutRect(10, 10, 360, 300));
        IReadOnlyList<BoardDef> boards = [original, BoardOf("b-2")];

        var result = BoardEdits.Duplicate(boards, "b-1");

        Assert.Equal(3, result.Count);
        var copy = result[1];
        Assert.Equal("Battle copy", copy.Name);
        Assert.NotEqual("b-1", copy.Id);
        Assert.Equal(original.Panels.Select(p => (p.Type, p.Size, p.Settings)), copy.Panels.Select(p => (p.Type, p.Size, p.Settings)));
        Assert.Empty(copy.Panels.Select(p => p.Id).Intersect(Ids(original)));
        Assert.All(copy.Panels, p => Assert.Null(p.PopOut));
        Assert.Equal("b-2", result[2].Id);
    }

    [Fact]
    public void TheLastBoardCantBeDeleted()
    {
        IReadOnlyList<BoardDef> one = [BoardOf("b-1")];
        IReadOnlyList<BoardDef> two = [BoardOf("b-1"), BoardOf("b-2")];

        Assert.Same(one, BoardEdits.Delete(one, "b-1"));
        Assert.Equal(new[] { "b-2" }, BoardEdits.Delete(two, "b-1").Select(b => b.Id).ToArray());
        Assert.Same(two, BoardEdits.Delete(two, "b-9"));
    }

    [Fact]
    public void AnAddedPanelGoesLastAtItsDefaultSize()
    {
        var board = BoardEdits.AddPanel(BoardOf("b-1", PanelType.Standing), PanelType.LiveLeaderboard, new PanelSettings(Clan.Slug, SourceId: "s-1"));

        var added = board.Panels[^1];
        Assert.Equal(PanelType.LiveLeaderboard, added.Type);
        Assert.Equal(new PanelSize(PanelSize.Wide), added.Size);
        Assert.Equal("s-1", added.Settings.SourceId);
        Assert.Matches("^p-[0-9a-f]{8}$", added.Id);
    }

    [Fact]
    public void MovingCountsThePanelItselfInTheInsertionIndex()
    {
        var board = BoardOf("b", PanelType.Standing, PanelType.Race, PanelType.Records, PanelType.Top);
        string a = "p-b-1", b = "p-b-2", c = "p-b-3", d = "p-b-4";

        Assert.Equal(new[] { b, c, a, d }, Ids(BoardEdits.MoveTo(board, a, 3)));
        Assert.Equal(new[] { d, a, b, c }, Ids(BoardEdits.MoveTo(board, d, 0)));
        var atEnd = BoardEdits.MoveTo(board, a, 99);
        Assert.Equal(new[] { b, c, d, a }, Ids(atEnd));
        Assert.Equal(new[] { a, b, c, d }, Ids(BoardEdits.MoveTo(atEnd, a, 0)));
        Assert.Same(board, BoardEdits.MoveTo(board, b, 2));
        Assert.Same(board, BoardEdits.MoveTo(board, b, 1));
        Assert.Equal(new[] { a, c, b, d }, Ids(BoardEdits.MoveBy(board, c, -1)));
        Assert.Equal(new[] { a, c, b, d }, Ids(BoardEdits.MoveBy(board, b, 1)));
        Assert.Same(board, BoardEdits.MoveBy(board, a, -1));
        Assert.Same(board, BoardEdits.MoveBy(board, d, 1));
        Assert.Same(board, BoardEdits.MoveTo(board, "p-gone", 0));
    }

    [Fact]
    public void RemoveResizeSettingsAndPopOutsChangeOnlyTheirPanel()
    {
        var board = BoardOf("b", PanelType.Standing, PanelType.Race);
        var rect = new PopOutRect(1400, 80, 360, 300);

        Assert.Equal(new[] { "p-b-2" }, Ids(BoardEdits.RemovePanel(board, "p-b-1")));
        Assert.Equal(new PanelSize(12, Tall: true), BoardEdits.Resize(board, "p-b-2", new PanelSize(40, Tall: true)).Panels[1].Size);
        Assert.Equal(new PanelSize(1), BoardEdits.Resize(board, "p-b-2", new PanelSize(0)).Panels[1].Size);
        Assert.Equal("s-9", BoardEdits.SetSettings(board, "p-b-1", new PanelSettings(Clan.Slug, SourceId: "s-9")).Panels[0].Settings.SourceId);

        var popped = BoardEdits.PopOut(board, "p-b-2", rect);
        Assert.Equal(rect, popped.Panels[1].PopOut);
        Assert.Null(popped.Panels[0].PopOut);
        Assert.Null(BoardEdits.Return(popped, "p-b-2").Panels[1].PopOut);
        Assert.Same(board, BoardEdits.RemovePanel(board, "p-gone"));
    }

    [Fact]
    public void APanelIsFoundOnWhicheverBoardHoldsIt()
    {
        IReadOnlyList<BoardDef> boards = [BoardOf("b-1", PanelType.Standing), BoardOf("b-2", PanelType.Top, PanelType.Records)];

        var found = BoardEdits.Find(boards, "p-b-2-2");

        Assert.True(found.HasValue);
        var (onBoard, panel) = found.GetValueOrDefault();
        Assert.Equal("b-2", onBoard.Id);
        Assert.Equal(PanelType.Records, panel.Type);
        Assert.Null(BoardEdits.Find(boards, "p-gone"));
    }

    [Fact]
    public void AutomationIdsCountEachTypeInBoardOrder() =>
        Assert.Equal(
            new[] { "StandingPanel1", "RacePanel1", "StandingPanel2", "ProfileStatPanel1" },
            BoardEdits.AutomationIds(BoardOf("b", PanelType.Standing, PanelType.Race, PanelType.Standing, PanelType.ProfileStat)).ToArray());

    [Fact]
    public void TheAnchorIsTheFirstPanelsSourceThatIsStillOn()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var alt = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
        var board = new BoardDef("b", "b",
        [
            new PanelDef("p-1", PanelType.MyAccounts, new PanelSize(6), new PanelSettings(Clan.Slug, Stat: "value")),
            new PanelDef("p-2", PanelType.Standing, new PanelSize(3), new PanelSettings(Clan.Slug, SourceId: "s-gone")),
            new PanelDef("p-3", PanelType.Race, new PanelSize(6), new PanelSettings(Clan.Slug, SourceIds: new List<string> { alt.Id, main.Id })),
        ]);

        Assert.Equal(alt.Id, BoardEdits.AnchorSourceId(board, [main, alt]));
        Assert.Equal(main.Id, BoardEdits.AnchorSourceId(board, [main, alt with { Enabled = false }]));
        Assert.Equal(main.Id, BoardEdits.AnchorSourceId(board with { Panels = [] }, [alt, main]));
        Assert.Null(BoardEdits.AnchorSourceId(board with { Panels = [] }, [alt]));
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: FAIL with CS0103 `The name 'BoardEdits' does not exist in the current context`.

- [ ] **Step 3: Implement the edits**

Create `src/Board/BoardEdits.cs`:

```csharp
using Labs626.UrScore.Core;

namespace Labs626.UrScore.Board;

using Source = Labs626.UrScore.Core.Source;

/// <summary>
/// Every change to boards and panels (spec §9.2, §9.3), as pure functions. An edit that doesn't apply returns
/// its input, the same instance, so a caller can tell nothing changed.
/// </summary>
public static class BoardEdits
{
    /// <summary>"Board 2", "Board 3"...: the first number past the board count that no board is named.</summary>
    public static string NextName(IReadOnlyList<BoardDef> boards)
    {
        var taken = boards.Select(b => b.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var number = boards.Count + 1;
        while (taken.Contains($"Board {number}")) number++;
        return $"Board {number}";
    }

    public static IReadOnlyList<BoardDef> Add(IReadOnlyList<BoardDef> boards, BoardDef board) => [.. boards, board];

    public static IReadOnlyList<BoardDef> Replace(IReadOnlyList<BoardDef> boards, BoardDef board) =>
        IndexOf(boards, board.Id) < 0 ? boards : [.. boards.Select(b => b.Id == board.Id ? board : b)];

    public static IReadOnlyList<BoardDef> Rename(IReadOnlyList<BoardDef> boards, string boardId, string name) =>
        BoardDefs.CleanName(name) is not { } clean || IndexOf(boards, boardId) < 0
            ? boards
            : [.. boards.Select(b => b.Id == boardId ? b with { Name = clean } : b)];

    /// <summary>R13: right after the original, "name copy", new panel ids, and nothing popped out.</summary>
    public static IReadOnlyList<BoardDef> Duplicate(IReadOnlyList<BoardDef> boards, string boardId)
    {
        var index = IndexOf(boards, boardId);
        if (index < 0) return boards;

        var original = boards[index];
        var copy = new BoardDef(
            BoardDefs.NewBoardId(),
            BoardDefs.CleanName($"{original.Name} copy") ?? BoardEdits.NextName(boards),
            [.. original.Panels.Select(p => p with { Id = BoardDefs.NewPanelId(), PopOut = null })]);

        var list = boards.ToList();
        list.Insert(index + 1, copy);
        return list;
    }

    /// <summary>R11: the last board stays.</summary>
    public static IReadOnlyList<BoardDef> Delete(IReadOnlyList<BoardDef> boards, string boardId) =>
        boards.Count <= 1 || IndexOf(boards, boardId) < 0 ? boards : [.. boards.Where(b => b.Id != boardId)];

    public static BoardDef AddPanel(BoardDef board, PanelType type, PanelSettings settings) =>
        board with { Panels = [.. board.Panels, new PanelDef(BoardDefs.NewPanelId(), type, BoardDefs.DefaultSize(type), settings)] };

    public static BoardDef RemovePanel(BoardDef board, string panelId) =>
        PanelIndex(board, panelId) < 0 ? board : board with { Panels = [.. board.Panels.Where(p => p.Id != panelId)] };

    /// <summary>
    /// Drops the panel before the panel now at <paramref name="insertionIndex"/>, counting the panel itself, so
    /// a drop on its own left or right half changes nothing (the index <see cref="BoardLayout.DropIndex"/> gives).
    /// </summary>
    public static BoardDef MoveTo(BoardDef board, string panelId, int insertionIndex)
    {
        var from = PanelIndex(board, panelId);
        if (from < 0) return board;

        var to = Math.Clamp(insertionIndex, 0, board.Panels.Count);
        if (to > from) to--;
        if (to == from) return board;

        var list = board.Panels.ToList();
        var panel = list[from];
        list.RemoveAt(from);
        list.Insert(to, panel);
        return board with { Panels = list };
    }

    /// <summary>Move earlier (-1) or later (+1), for the buttons beside the drag handle (R7).</summary>
    public static BoardDef MoveBy(BoardDef board, string panelId, int delta)
    {
        var from = PanelIndex(board, panelId);
        if (from < 0 || delta == 0) return board;

        return MoveTo(board, panelId, delta > 0 ? from + delta + 1 : from + delta);
    }

    public static BoardDef Resize(BoardDef board, string panelId, PanelSize size) =>
        Update(board, panelId, p => p with { Size = new PanelSize(Math.Clamp(size.Span, 1, BoardLayout.Columns), size.Tall) });

    public static BoardDef SetSettings(BoardDef board, string panelId, PanelSettings settings) =>
        Update(board, panelId, p => p with { Settings = settings });

    public static BoardDef PopOut(BoardDef board, string panelId, PopOutRect rect) =>
        Update(board, panelId, p => p with { PopOut = rect });

    public static BoardDef Return(BoardDef board, string panelId) =>
        Update(board, panelId, p => p with { PopOut = null });

    public static (BoardDef Board, PanelDef Panel)? Find(IReadOnlyList<BoardDef> boards, string panelId)
    {
        foreach (var board in boards)
        {
            if (board.Panels.FirstOrDefault(p => p.Id == panelId) is { } panel) return (board, panel);
        }

        return null;
    }

    /// <summary>Stage 1's ids, per board: "StandingPanel1", "StandingPanel2", "RacePanel1"... in board order.</summary>
    public static IReadOnlyList<string> AutomationIds(BoardDef board)
    {
        var counts = new Dictionary<PanelType, int>();
        var ids = new List<string>(board.Panels.Count);
        foreach (var panel in board.Panels)
        {
            counts[panel.Type] = counts.GetValueOrDefault(panel.Type) + 1;
            ids.Add($"{panel.Type}Panel{counts[panel.Type]}");
        }

        return ids;
    }

    /// <summary>The source the top bar's period line follows: the first panel's source that is still on, else the main.</summary>
    public static string? AnchorSourceId(BoardDef board, IReadOnlyList<Source> sources)
    {
        var enabled = sources.Where(s => s.Enabled).ToList();
        foreach (var panel in board.Panels)
        {
            var ids = new[] { panel.Settings.SourceId, panel.Settings.ToSourceId }.Concat(panel.Settings.SourceIds ?? Array.Empty<string>());
            foreach (var id in ids)
            {
                if (enabled.FirstOrDefault(s => s.Id == id) is { } found) return found.Id;
            }
        }

        return enabled.FirstOrDefault(s => s.Role == SourceRole.Main)?.Id;
    }

    private static BoardDef Update(BoardDef board, string panelId, Func<PanelDef, PanelDef> change) =>
        PanelIndex(board, panelId) < 0 ? board : board with { Panels = [.. board.Panels.Select(p => p.Id == panelId ? change(p) : p)] };

    private static int IndexOf(IReadOnlyList<BoardDef> boards, string boardId)
    {
        for (var i = 0; i < boards.Count; i++)
        {
            if (boards[i].Id == boardId) return i;
        }

        return -1;
    }

    private static int PanelIndex(BoardDef board, string panelId)
    {
        for (var i = 0; i < board.Panels.Count; i++)
        {
            if (board.Panels[i].Id == panelId) return i;
        }

        return -1;
    }
}
```

- [ ] **Step 4: Run the gates**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: 0 warnings, 0 errors.

Run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: all pass, `BoardEditsTests` included.

- [ ] **Step 5: Commit**

```text
git add src/Board/BoardEdits.cs tests/BoardEditsTests.cs
git commit -m "board: every board and panel edit as a pure function, the last board kept

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: Layout: tall panels, row heights and where a drop lands

Spec §9.2 ("panels flow in order in a 12-column grid", "tall panels take two rows", "dragging changes order"), rulings R5, R6, R9. The math is pure in `BoardLayout`; `PanelGrid` measures, arranges, and remembers where each child went so a drop can ask which index it lands on.

**Files:**
- Modify: `src/Board/BoardLayout.cs`
- Modify: `src/UI/Controls/PanelGrid.cs`
- Modify: `tests/BoardLayoutTests.cs`

**Interfaces:**
- Consumes: Task 1 (`PanelSize`).
- Produces: `PanelPlacement(..., int Rows = 1)`, `CellRect`, `BoardLayout.Flow(IReadOnlyList<PanelSize>, double)`, `BoardLayout.RowHeights`, `BoardLayout.DropIndex`, `PanelGrid.TallProperty` with `GetTall`/`SetTall`, `PanelGrid.DropIndexAt(Point)`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/BoardLayoutTests.cs`, inside the class, after `SpansStayInsideTheGrid`:

```csharp
    [Fact]
    public void ATallPanelTakesTwoRowsAndLaterPanelsFlowAroundIt() =>
        Assert.Equal(
            new[] { (0, 0, 6, 2), (0, 6, 6, 1), (1, 6, 3, 1), (1, 9, 3, 1), (2, 0, 12, 1) },
            BoardLayout.Flow([new PanelSize(6, Tall: true), new PanelSize(6), new PanelSize(3), new PanelSize(3), new PanelSize(12)], 1280)
                .Select(p => (p.Row, p.Column, p.Span, p.Rows)).ToArray());

    [Fact]
    public void PanelsNeverJumpAheadOfATallPanelsSecondRow() =>
        Assert.Equal(
            new[] { (0, 0), (0, 3), (2, 0), (3, 0) },
            BoardLayout.Flow([new PanelSize(3), new PanelSize(3, Tall: true), new PanelSize(12), new PanelSize(6)], 1280)
                .Select(p => (p.Row, p.Column)).ToArray());

    [Fact]
    public void WithoutTallPanelsBothFlowsAgree() =>
        Assert.Equal(
            BoardLayout.Flow(Battle, 900),
            BoardLayout.Flow([.. Battle.Select(span => new PanelSize(span))], 900));

    [Fact]
    public void RowsAreAsTallAsTheirTallestPanelAndATallPanelGrowsItsSecondRow()
    {
        var placed = BoardLayout.Flow([new PanelSize(6, Tall: true), new PanelSize(6), new PanelSize(6)], 1280);

        Assert.Equal(new[] { 200.0, 288.0 }, BoardLayout.RowHeights(placed, [500, 200, 150], 12).ToArray());
        Assert.Equal(new[] { 200.0, 150.0 }, BoardLayout.RowHeights(placed, [100, 200, 150], 12).ToArray());
    }

    private static readonly CellRect[] Cells =
    [
        new(0, 0, 300, 200), new(312, 0, 300, 200), new(624, 0, 600, 200),
        new(0, 212, 600, 150),
    ];

    [Theory]
    [InlineData(100.0, 50.0, 0)]
    [InlineData(250.0, 50.0, 1)]
    [InlineData(400.0, 100.0, 1)]
    [InlineData(1000.0, 100.0, 3)]
    [InlineData(306.0, 100.0, 1)]
    [InlineData(900.0, 300.0, 4)]
    [InlineData(100.0, 206.0, 3)]
    [InlineData(100.0, 900.0, 4)]
    public void ADropLandsBeforeOrAfterThePanelUnderIt(double x, double y, int expected) =>
        Assert.Equal(expected, BoardLayout.DropIndex(Cells, x, y));
```

The theory rows are, in order: the first panel's left half, its right half, the second's left half, the third's right half, the gap between the first two, right of the last panel on its row, the gap under the first row, and below everything.

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: FAIL with CS1503 (no `Flow` overload takes `PanelSize`), CS0246 `CellRect`, and CS0117 for `RowHeights`, `DropIndex`, `Rows`.

- [ ] **Step 3: Implement the layout**

Replace `src/Board/BoardLayout.cs` with:

```csharp
namespace Labs626.UrScore.Board;

/// <summary>Where one panel sits: its row and column, how many columns, and how many rows (2 for a tall panel).</summary>
public sealed record PanelPlacement(int Index, int Row, int Column, int Span, int Rows = 1);

/// <summary>Where a panel was arranged, in the grid's own coordinates.</summary>
public sealed record CellRect(double Left, double Top, double Width, double Height);

/// <summary>
/// Panels flow in order across a 12-column grid (spec §9.2), each at the first free spot at or after the
/// previous one's, so a tall panel's second row pushes later panels along and order stays reading order (R6).
/// Narrow windows widen panels the way the mock does: 3- and 4-wide become half, 5 and 7+ take the row.
/// </summary>
public static class BoardLayout
{
    public const int Columns = 12;
    public const double NarrowWidth = 1100;
    public const double SingleWidth = 720;

    public static int EffectiveSpan(int span, double width) =>
        width < SingleWidth ? Columns
        : width < NarrowWidth ? span switch { <= 4 => 6, 6 => 6, _ => Columns }
        : Math.Clamp(span, 1, Columns);

    public static IReadOnlyList<PanelPlacement> Flow(IReadOnlyList<int> spans, double width) =>
        Flow(spans.Select(span => new PanelSize(span)).ToList(), width);

    public static IReadOnlyList<PanelPlacement> Flow(IReadOnlyList<PanelSize> sizes, double width)
    {
        var placements = new List<PanelPlacement>(sizes.Count);
        var taken = new HashSet<(int Row, int Column)>();
        int row = 0, column = 0;

        for (var index = 0; index < sizes.Count; index++)
        {
            var span = EffectiveSpan(sizes[index].Span, width);
            var rows = sizes[index].Tall ? 2 : 1;

            while (true)
            {
                if (column + span > Columns)
                {
                    row++;
                    column = 0;
                }

                if (Free(taken, row, column, span, rows)) break;
                column++;
            }

            for (var r = row; r < row + rows; r++)
            {
                for (var c = column; c < column + span; c++) taken.Add((r, c));
            }

            placements.Add(new PanelPlacement(index, row, column, span, rows));
            column += span;
        }

        return placements;
    }

    /// <summary>
    /// Each row is as tall as its tallest one-row panel. A tall panel that needs more than its two rows and the
    /// gap between them grows its second row by the difference.
    /// </summary>
    public static IReadOnlyList<double> RowHeights(IReadOnlyList<PanelPlacement> placements, IReadOnlyList<double> desired, double gap)
    {
        var rows = new List<double>();

        foreach (var placement in placements.Where(p => p.Rows == 1))
        {
            while (rows.Count <= placement.Row) rows.Add(0);
            rows[placement.Row] = Math.Max(rows[placement.Row], desired[placement.Index]);
        }

        foreach (var placement in placements.Where(p => p.Rows > 1))
        {
            var last = placement.Row + placement.Rows - 1;
            while (rows.Count <= last) rows.Add(0);

            var have = Enumerable.Range(placement.Row, placement.Rows).Sum(r => rows[r]) + gap * (placement.Rows - 1);
            if (desired[placement.Index] > have) rows[last] += desired[placement.Index] - have;
        }

        return rows;
    }

    /// <summary>
    /// The insertion index for a drop at (x, y), counting the dragged panel itself (<see cref="BoardEdits.MoveTo"/>).
    /// Over a panel: before it on its left half, after it on its right half. Elsewhere: after every panel that ends
    /// above the point or sits to its left on the same line.
    /// </summary>
    public static int DropIndex(IReadOnlyList<CellRect> cells, double x, double y)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            if (x >= cell.Left && x < cell.Left + cell.Width && y >= cell.Top && y < cell.Top + cell.Height)
            {
                return x < cell.Left + cell.Width / 2 ? i : i + 1;
            }
        }

        return cells.Count(cell => cell.Top + cell.Height <= y || (cell.Top <= y && cell.Left + cell.Width <= x));
    }

    private static bool Free(HashSet<(int Row, int Column)> taken, int row, int column, int span, int rows)
    {
        for (var r = row; r < row + rows; r++)
        {
            for (var c = column; c < column + span; c++)
            {
                if (taken.Contains((r, c))) return false;
            }
        }

        return true;
    }
}
```

- [ ] **Step 4: Let the grid lay out tall panels and answer where a drop lands**

Replace `src/UI/Controls/PanelGrid.cs` with:

```csharp
using System.Windows;
using System.Windows.Controls;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

/// <summary>
/// Lays panels out with <see cref="BoardLayout"/>: 12 columns, a gap between cells, each row as tall as its
/// tallest panel, tall panels across two rows. Remembers where each child was arranged, for dragging.
/// </summary>
public sealed class PanelGrid : Panel
{
    public static readonly DependencyProperty SpanProperty = DependencyProperty.RegisterAttached(
        "Span", typeof(int), typeof(PanelGrid),
        new FrameworkPropertyMetadata(BoardLayout.Columns, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty TallProperty = DependencyProperty.RegisterAttached(
        "Tall", typeof(bool), typeof(PanelGrid),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    private IReadOnlyList<CellRect> _cells = [];

    public static int GetSpan(UIElement element) => (int)element.GetValue(SpanProperty);

    public static void SetSpan(UIElement element, int value) => element.SetValue(SpanProperty, value);

    public static bool GetTall(UIElement element) => (bool)element.GetValue(TallProperty);

    public static void SetTall(UIElement element, bool value) => element.SetValue(TallProperty, value);

    public double Gap { get; set; } = 12;

    /// <summary>Where a panel dropped at <paramref name="point"/>, in this grid's coordinates, would go.</summary>
    public int DropIndexAt(Point point) => BoardLayout.DropIndex(_cells, point.X, point.Y);

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 1200 : availableSize.Width;
        var children = InternalChildren.Cast<UIElement>().ToList();
        var placements = BoardLayout.Flow(Sizes(children), width);

        foreach (var placement in placements)
        {
            children[placement.Index].Measure(new Size(CellWidth(width, placement.Span), double.PositiveInfinity));
        }

        var rows = BoardLayout.RowHeights(placements, Heights(children), Gap);
        return new Size(width, rows.Sum() + Gap * Math.Max(0, rows.Count - 1));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren.Cast<UIElement>().ToList();
        var placements = BoardLayout.Flow(Sizes(children), finalSize.Width);
        var rows = BoardLayout.RowHeights(placements, Heights(children), Gap);

        var tops = new double[rows.Count];
        for (var row = 1; row < rows.Count; row++) tops[row] = tops[row - 1] + rows[row - 1] + Gap;

        var column = ColumnWidth(finalSize.Width);
        var cells = new List<CellRect>(placements.Count);
        foreach (var placement in placements)
        {
            var child = children[placement.Index];
            var height = placement.Rows == 1
                ? child.DesiredSize.Height
                : Enumerable.Range(placement.Row, placement.Rows).Sum(r => rows[r]) + Gap * (placement.Rows - 1);
            var rect = new Rect(placement.Column * (column + Gap), tops[placement.Row], CellWidth(finalSize.Width, placement.Span), height);

            child.Arrange(rect);
            cells.Add(new CellRect(rect.X, rect.Y, rect.Width, rect.Height));
        }

        _cells = cells;
        return finalSize;
    }

    private static List<PanelSize> Sizes(IEnumerable<UIElement> children) =>
        children.Select(child => new PanelSize(GetSpan(child), GetTall(child))).ToList();

    private static List<double> Heights(IEnumerable<UIElement> children) =>
        children.Select(child => child.DesiredSize.Height).ToList();

    private double ColumnWidth(double width) => Math.Max(0, (width - Gap * (BoardLayout.Columns - 1)) / BoardLayout.Columns);

    private double CellWidth(double width, int span) => ColumnWidth(width) * span + Gap * (span - 1);
}
```

- [ ] **Step 5: Run the gates**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: 0 warnings, 0 errors.

Run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: all pass. The four stage 1 `BoardLayoutTests` still pass unchanged.

- [ ] **Step 6: Commit**

```text
git add src/Board/BoardLayout.cs src/UI/Controls/PanelGrid.cs tests/BoardLayoutTests.cs
git commit -m "board: tall panels take two rows, and a drop lands before or after the panel under it

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: What a panel needs: forms and the gallery

Spec §9.4 (the gallery: each card says what the panel needs, adding asks only for that, any type more than once; stale settings), §3.4 (recipe words), rulings R14, R15, R16. Pure: which fields a panel's form shows, what each field offers, sensible defaults, turning picks into `PanelSettings`, and what's wrong with a setting. The gallery's ten cards come from the same rules, so a card is only enabled when its form can be filled.

A stat choice's key is `recipe + (char)0x1F + stat key`, because a stat-only panel (My accounts, Records, Profile stat, Account card) can pick from more than one recipe. An account choice's key is the Roblox user id as invariant text, or `""` for "Your top account".

**Files:**
- Create: `src/Board/PanelForms.cs`, `src/Board/PanelGallery.cs`
- Create: `tests/PanelFormsTests.cs`, `tests/PanelGalleryTests.cs`

**Interfaces:**
- Consumes: `PanelType`, `PanelSettings`, `LiveBoard` (with `FindSource`, `FindRecipe`, `SourceName`, `MyUserIds`), `PanelModels.MaxRace`, `RecipeWords`, `RecipeStats.Offered`/`Find`, `RecipeState.TrackedStats`, `SourceRole` (stage 1).
- Produces: `PanelField`, `FormChoice`, `FormValues`, `PanelForms`, `GalleryCard`, `PanelGallery`, exactly as in the contract, plus `internal static (string Group, string Groups) PanelForms.GroupWords(LiveBoard)`.

- [ ] **Step 1: Write the failing form tests**

Create `tests/PanelFormsTests.cs`:

```csharp
using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

public class PanelFormsTests
{
    private static readonly Source MainClan = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
    private static readonly Source AltClan = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
    private static readonly Source Rival = SourceOf("s-00000003", Clan, "NovaForge", SourceRole.Watch);
    private static readonly Source TopSource = SourceOf("s-0000000a", TopList, null, SourceRole.Watch);
    private static readonly Source ProfileSource = SourceOf("s-00000009", Profile, null, SourceRole.Mine);

    private static LiveBoard Everything() => Live(
        [Rival, AltClan, MainClan, TopSource, ProfileSource],
        [Installed(Clan, "value"), Installed(TopList), Installed(Profile, "diamonds", "eggs")],
        new Dictionary<string, RecipeSnapshot>());

    private static string ClanStat => PanelForms.StatKey(Clan.Slug, "value");

    private static string[] Keys(IEnumerable<FormChoice> choices) => [.. choices.Select(c => c.Key)];

    [Theory]
    [InlineData(PanelType.Standing, true, new[] { PanelField.Source })]
    [InlineData(PanelType.Standing, false, new[] { PanelField.Source })]
    [InlineData(PanelType.Race, true, new[] { PanelField.Sources })]
    [InlineData(PanelType.MyAccounts, true, new[] { PanelField.Stat })]
    [InlineData(PanelType.PromotionCheck, true, new[] { PanelField.Source, PanelField.ToSource })]
    [InlineData(PanelType.PromotionCheck, false, new[] { PanelField.Source, PanelField.ToSource, PanelField.Stat })]
    [InlineData(PanelType.AccountCard, true, new[] { PanelField.Account })]
    [InlineData(PanelType.AccountCard, false, new[] { PanelField.Account, PanelField.Stat })]
    [InlineData(PanelType.PastPeriods, true, new[] { PanelField.Source })]
    [InlineData(PanelType.PastPeriods, false, new[] { PanelField.Source, PanelField.Stat })]
    [InlineData(PanelType.Records, true, new[] { PanelField.Stat })]
    [InlineData(PanelType.Top, true, new[] { PanelField.Source })]
    [InlineData(PanelType.ProfileStat, true, new[] { PanelField.Stat })]
    [InlineData(PanelType.ProfileStat, false, new[] { PanelField.Stat, PanelField.Source })]
    [InlineData(PanelType.LiveLeaderboard, true, new[] { PanelField.Source })]
    public void AddingAsksOnlyForWhatThePanelNeeds(PanelType type, bool adding, PanelField[] expected) =>
        Assert.Equal(expected, PanelForms.Fields(type, adding).ToArray());

    [Fact]
    public void SourcesAreOfferedMainFirstAndOnlyWhereThePanelFits()
    {
        var live = Everything();
        var none = new FormValues();

        var standing = PanelForms.SourceChoices(PanelType.Standing, PanelField.Source, live, none);
        Assert.Equal(new[] { MainClan.Id, AltClan.Id, Rival.Id }, Keys(standing));
        Assert.Equal(new[] { "★ CCGP", "K0i2", "NovaForge · watching" }, standing.Select(c => c.Label).ToArray());

        Assert.Equal(new[] { TopSource.Id }, Keys(PanelForms.SourceChoices(PanelType.Top, PanelField.Source, live, none)));
        Assert.Equal(new[] { MainClan.Id, AltClan.Id, Rival.Id }, Keys(PanelForms.SourceChoices(PanelType.Race, PanelField.Sources, live, none)));
        Assert.Equal(new[] { MainClan.Id, AltClan.Id, Rival.Id }, Keys(PanelForms.SourceChoices(PanelType.PastPeriods, PanelField.Source, live, none)));
        Assert.Equal(new[] { MainClan.Id, AltClan.Id, Rival.Id }, Keys(PanelForms.SourceChoices(PanelType.LiveLeaderboard, PanelField.Source, live, none)));
        Assert.Equal(new[] { MainClan.Id, AltClan.Id }, Keys(PanelForms.SourceChoices(PanelType.PromotionCheck, PanelField.Source, live, none)));
        Assert.Equal(new[] { MainClan.Id, Rival.Id },
            Keys(PanelForms.SourceChoices(PanelType.PromotionCheck, PanelField.ToSource, live, new FormValues(Source: AltClan.Id))));
        Assert.Empty(PanelForms.SourceChoices(PanelType.PromotionCheck, PanelField.ToSource, live, none));
        Assert.Equal(new[] { ProfileSource.Id },
            Keys(PanelForms.SourceChoices(PanelType.ProfileStat, PanelField.Source, live, new FormValues(Stat: PanelForms.StatKey(Profile.Slug, "diamonds")))));
    }

    [Fact]
    public void ASourceThatIsOffSaysSo()
    {
        var live = Live([MainClan with { Enabled = false }], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot>());

        Assert.Equal("★ CCGP · off", PanelForms.SourceChoices(PanelType.Standing, PanelField.Source, live, new FormValues()).Single().Label);
    }

    [Fact]
    public void StatsAreTheTickedOnesNamedByRecipeWhenThereIsMoreThanOne()
    {
        var live = Everything();

        Assert.Equal(
            new[]
            {
                (ClanStat, "Points · Pet Sim 99 clan battle points"),
                (PanelForms.StatKey(Profile.Slug, "diamonds"), "Diamonds · Pet Sim 99 profile"),
                (PanelForms.StatKey(Profile.Slug, "eggs"), "Eggs hatched · Pet Sim 99 profile"),
            },
            PanelForms.StatChoices(PanelType.MyAccounts, live, new FormValues(), null).Select(c => (c.Key, c.Label)).ToArray());
        Assert.Equal(new[] { "Diamonds", "Eggs hatched" },
            PanelForms.StatChoices(PanelType.ProfileStat, live, new FormValues(), null).Select(c => c.Label).ToArray());
        Assert.Equal(new[] { ClanStat }, Keys(PanelForms.StatChoices(PanelType.PastPeriods, live, new FormValues(Source: MainClan.Id), null)));
        Assert.Empty(PanelForms.StatChoices(PanelType.PromotionCheck, live, new FormValues(), null));
        Assert.Contains(PanelForms.StatKey(Profile.Slug, "rank"),
            Keys(PanelForms.StatChoices(PanelType.ProfileStat, live, new FormValues(), new PanelSettings(Profile.Slug, Stat: "rank"))));
    }

    [Fact]
    public void AccountsAreYoursByNameAfterYourTopAccount() =>
        Assert.Equal(
            new[] { ("", "Your top account"), ("201", "CElCPapa"), ("101", "estehernandez"), ("301", "ItsJustEste"), ("202", "ItsJustEstePapa") },
            PanelForms.AccountChoices(Everything()).Select(c => (c.Key, c.Label)).ToArray());

    [Fact]
    public void DefaultsFollowTheMain()
    {
        var live = Everything();

        Assert.Equal(new FormValues(Source: MainClan.Id), PanelForms.Defaults(PanelType.Standing, live));
        Assert.Equal(new[] { MainClan.Id, AltClan.Id, Rival.Id }, PanelForms.Defaults(PanelType.Race, live).Sources!.ToArray());
        Assert.Equal(new FormValues(Source: AltClan.Id, ToSource: MainClan.Id, Stat: ClanStat), PanelForms.Defaults(PanelType.PromotionCheck, live));
        Assert.Equal(new FormValues(Account: PanelForms.TopAccountKey, Stat: ClanStat), PanelForms.Defaults(PanelType.AccountCard, live));
        Assert.Equal(new FormValues(Source: MainClan.Id, Stat: ClanStat), PanelForms.Defaults(PanelType.PastPeriods, live));
        Assert.Equal(new FormValues(Source: TopSource.Id), PanelForms.Defaults(PanelType.Top, live));
        Assert.Equal(new FormValues(Stat: PanelForms.StatKey(Profile.Slug, "diamonds")), PanelForms.Defaults(PanelType.ProfileStat, live));
    }

    [Fact]
    public void EveryTypesDefaultsBuildSettingsWithNoProblem()
    {
        var live = Everything();

        Assert.All(PanelGallery.Order, type =>
            Assert.Null(PanelForms.Problem(type, PanelForms.Build(type, PanelForms.Defaults(type, live), live), live)));
    }

    [Fact]
    public void BuildingGivesTheSettingsThePanelsRead()
    {
        var live = Everything();

        Assert.Equal(new PanelSettings(Clan.Slug, SourceId: AltClan.Id), PanelForms.Build(PanelType.Standing, new FormValues(Source: AltClan.Id, Stat: ClanStat), live));
        Assert.Equal(new PanelSettings(Clan.Slug, SourceId: AltClan.Id, ToSourceId: MainClan.Id, Stat: "value"),
            PanelForms.Build(PanelType.PromotionCheck, new FormValues(Source: AltClan.Id, ToSource: MainClan.Id, Stat: ClanStat), live));
        Assert.Equal(new PanelSettings(Clan.Slug, Stat: "value", UserId: 201),
            PanelForms.Build(PanelType.AccountCard, new FormValues(Account: "201", Stat: ClanStat), live));
        Assert.Equal(new PanelSettings(Clan.Slug, Stat: "value"),
            PanelForms.Build(PanelType.AccountCard, new FormValues(Account: PanelForms.TopAccountKey, Stat: ClanStat), live));
        Assert.Equal(new PanelSettings(Profile.Slug, SourceId: ProfileSource.Id, Stat: "diamonds"),
            PanelForms.Build(PanelType.ProfileStat, new FormValues(Stat: PanelForms.StatKey(Profile.Slug, "diamonds")), live));
        Assert.Equal(new PanelSettings(Clan.Slug, Stat: "value"),
            PanelForms.Build(PanelType.MyAccounts, new FormValues(Source: MainClan.Id, Stat: ClanStat), live));
        Assert.Equal(new PanelSettings(Clan.Slug, SourceId: MainClan.Id),
            PanelForms.Build(PanelType.PastPeriods, new FormValues(Source: MainClan.Id, Stat: PanelForms.StatKey(Profile.Slug, "diamonds")), live));

        var race = PanelForms.Build(PanelType.Race, new FormValues(Sources: [MainClan.Id, Rival.Id]), live);
        Assert.Equal(Clan.Slug, race.Recipe);
        Assert.Equal(new[] { MainClan.Id, Rival.Id }, race.SourceIds!.ToArray());
        Assert.Null(race.SourceId);

        var promotion = new PanelSettings(Clan.Slug, SourceId: AltClan.Id, ToSourceId: MainClan.Id, Stat: "value");
        Assert.Equal(promotion, PanelForms.Build(PanelType.PromotionCheck, PanelForms.From(promotion), live));
    }

    [Fact]
    public void ProblemsUseTheRecipesWordsAndNameWhatToFix()
    {
        var live = Everything();
        PanelSettings Clans(string? source = null, string? to = null, string? stat = null, long? user = null, IReadOnlyList<string>? race = null) =>
            new(Clan.Slug, source, race, to, stat, user);

        Assert.Equal("Choose a clan.", PanelForms.Problem(PanelType.Standing, Clans(), live));
        Assert.Equal("This panel's clan was removed. Choose another.", PanelForms.Problem(PanelType.Standing, Clans("s-gone0000"), live));
        Assert.Equal("Choose 2 to 5 clans.", PanelForms.Problem(PanelType.Race, Clans(race: [MainClan.Id]), live));
        Assert.Equal("Every line in a race comes from the same recipe.", PanelForms.Problem(PanelType.Race, Clans(race: [MainClan.Id, TopSource.Id]), live));
        Assert.Null(PanelForms.Problem(PanelType.Race, Clans(race: [MainClan.Id, Rival.Id]), live));
        Assert.Equal("Choose a clan your accounts are in.", PanelForms.Problem(PanelType.PromotionCheck, Clans(Rival.Id, MainClan.Id, "value"), live));
        Assert.Equal("Choose a different clan of the same recipe to compare with.", PanelForms.Problem(PanelType.PromotionCheck, Clans(AltClan.Id, AltClan.Id, "value"), live));
        Assert.Equal("Choose a stat.", PanelForms.Problem(PanelType.PromotionCheck, Clans(AltClan.Id, MainClan.Id), live));
        Assert.Equal("This panel's stat was removed. Choose another.", PanelForms.Problem(PanelType.MyAccounts, Clans(stat: "gone-stat"), live));
        Assert.Equal("Choose one of your accounts.", PanelForms.Problem(PanelType.AccountCard, Clans(stat: "value", user: 987654321), live));
        Assert.Null(PanelForms.Problem(PanelType.PastPeriods, Clans(MainClan.Id), live));
        Assert.Null(PanelForms.Problem(PanelType.ProfileStat, new PanelSettings(Profile.Slug, Stat: "diamonds"), live));
        Assert.Equal("This panel can't show that clan.", PanelForms.Problem(PanelType.Standing, new PanelSettings(TopList.Slug, SourceId: TopSource.Id), live));
    }
}
```

- [ ] **Step 2: Write the failing gallery tests**

Create `tests/PanelGalleryTests.cs`:

```csharp
using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

public class PanelGalleryTests
{
    private static readonly Source MainClan = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
    private static readonly Source AltClan = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
    private static readonly Source TopSource = SourceOf("s-0000000a", TopList, null, SourceRole.Watch);
    private static readonly Source ProfileSource = SourceOf("s-00000009", Profile, null, SourceRole.Mine);

    private static readonly Dictionary<string, RecipeSnapshot> NoReads = new();

    private static GalleryCard Card(IReadOnlyList<GalleryCard> cards, PanelType type) => cards.Single(c => c.Type == type);

    [Fact]
    public void TenCardsInTheSpecsOrderTitledInTheRecipesWords()
    {
        var cards = PanelGallery.Cards(Live(
            [MainClan, AltClan, TopSource, ProfileSource],
            [Installed(Clan, "value"), Installed(TopList), Installed(Profile, "diamonds")], NoReads));

        Assert.Equal(
            new[] { "Clan standing", "Battle race", "My accounts", "Promotion check", "Account card", "Past battles", "Records", "Top of the battle", "Profile stat", "Live leaderboard" },
            cards.Select(c => c.Title).ToArray());
        Assert.Equal(PanelGallery.Order, cards.Select(c => c.Type));
        Assert.All(cards, c => Assert.True(c.CanAdd, c.Title));
        Assert.All(cards, c => Assert.Equal("", c.WhyNot));
        Assert.Equal("Needs a clan.", Card(cards, PanelType.Standing).Needs);
        Assert.Equal("Needs 2 to 5 clans of one recipe.", Card(cards, PanelType.Race).Needs);
    }

    [Fact]
    public void WithNothingInstalledEveryCardIsOffAndSaysWhy()
    {
        var cards = PanelGallery.Cards(Live([], [], NoReads));

        Assert.Equal(
            new[] { "Standing", "Race", "My accounts", "Promotion check", "Account card", "Past periods", "Records", "Top of the list", "Profile stat", "Live leaderboard" },
            cards.Select(c => c.Title).ToArray());
        Assert.All(cards, c => Assert.False(c.CanAdd, c.Title));
        Assert.All(cards, c => Assert.NotEqual("", c.WhyNot));
        Assert.Equal("Add a source in Setup first.", Card(cards, PanelType.Standing).WhyNot);
    }

    [Fact]
    public void OneClanCanStandButNotRaceOrBePromotedFrom()
    {
        var cards = PanelGallery.Cards(Live([MainClan], [Installed(Clan, "value")], NoReads));

        Assert.True(Card(cards, PanelType.Standing).CanAdd);
        Assert.True(Card(cards, PanelType.MyAccounts).CanAdd);
        Assert.False(Card(cards, PanelType.Race).CanAdd);
        Assert.False(Card(cards, PanelType.PromotionCheck).CanAdd);
        Assert.False(Card(cards, PanelType.Top).CanAdd);
        Assert.False(Card(cards, PanelType.ProfileStat).CanAdd);
    }

    [Fact]
    public void WithNoTickedStatTheStatPanelsAskForOne()
    {
        var cards = PanelGallery.Cards(Live([MainClan, AltClan], [Installed(Clan)], NoReads));

        Assert.True(Card(cards, PanelType.Standing).CanAdd);
        Assert.True(Card(cards, PanelType.Race).CanAdd);
        Assert.False(Card(cards, PanelType.MyAccounts).CanAdd);
        Assert.False(Card(cards, PanelType.Records).CanAdd);
        Assert.False(Card(cards, PanelType.AccountCard).CanAdd);
        Assert.False(Card(cards, PanelType.PromotionCheck).CanAdd);
        Assert.StartsWith("Tick Show or Send on a stat", Card(cards, PanelType.MyAccounts).WhyNot);
    }
}
```

- [ ] **Step 3: Run the tests to see them fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: FAIL with CS0246 for `PanelField`, `FormValues`, `FormChoice`, `GalleryCard` and CS0103 for `PanelForms`, `PanelGallery`.

- [ ] **Step 4: Implement the forms**

Create `src/Board/PanelForms.cs`:

```csharp
using System.Globalization;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

using Source = Labs626.UrScore.Core.Source;

/// <summary>One thing a panel's form can ask for.</summary>
public enum PanelField { Source, Sources, ToSource, Stat, Account }

/// <summary>One entry in a form's list. Its key is a source id, a stat key (<see cref="PanelForms.StatKey"/>) or an account id.</summary>
public sealed record FormChoice(string Key, string Label)
{
    public override string ToString() => Label;
}

/// <summary>What a form holds before it becomes <see cref="PanelSettings"/>.</summary>
public sealed record FormValues(string? Source = null, IReadOnlyList<string>? Sources = null, string? ToSource = null, string? Stat = null, string? Account = null);

/// <summary>
/// What each panel asks for, offers and refuses (spec §9.4, R14, R15). Pure. Adding asks for the "Needs"
/// column only; the rest is defaulted and shows in ⋯ settings.
/// </summary>
public static class PanelForms
{
    public const char KeySeparator = (char)0x1F;

    public const string TopAccountKey = "";

    public static string StatKey(string recipe, string stat) => recipe + KeySeparator + stat;

    public static (string Recipe, string Stat)? SplitStatKey(string? key)
    {
        if (key is null) return null;

        var at = key.IndexOf(KeySeparator);
        return at <= 0 || at >= key.Length - 1 ? null : (key[..at], key[(at + 1)..]);
    }

    /// <summary>Whether a recipe's sources or stats can feed this panel type.</summary>
    public static bool Fits(PanelType type, Recipe recipe) => type switch
    {
        PanelType.Top => recipe.IsGroupList,
        _ when recipe.IsGroupList => false,
        PanelType.Standing => recipe.Headline.Count > 0,
        PanelType.Race => recipe.Headline.Any(h => h.Sum),
        PanelType.PromotionCheck or PanelType.LiveLeaderboard => !recipe.LastStep.PerAccount,
        PanelType.PastPeriods => recipe.Period?.Past is not null,
        PanelType.ProfileStat => recipe.Period is null,
        _ => true,
    };

    public static IReadOnlyList<PanelField> Fields(PanelType type, bool adding)
    {
        var needs = type switch
        {
            PanelType.Race => new[] { PanelField.Sources },
            PanelType.PromotionCheck => new[] { PanelField.Source, PanelField.ToSource },
            PanelType.AccountCard => new[] { PanelField.Account },
            PanelType.MyAccounts or PanelType.Records or PanelType.ProfileStat => new[] { PanelField.Stat },
            _ => new[] { PanelField.Source },
        };
        if (adding) return needs;

        var extra = type switch
        {
            PanelType.PromotionCheck or PanelType.AccountCard or PanelType.PastPeriods => new[] { PanelField.Stat },
            PanelType.ProfileStat => new[] { PanelField.Source },
            _ => Array.Empty<PanelField>(),
        };
        return [.. needs, .. extra];
    }

    /// <summary>Sources that fit, main first, then yours, then watched. Promotion check's "to" is another source of the "from" recipe.</summary>
    public static IReadOnlyList<FormChoice> SourceChoices(PanelType type, PanelField field, LiveBoard live, FormValues values)
    {
        var fitting = live.Sources.Where(s => live.FindRecipe(s.Recipe) is { } installed && Fits(type, installed.Recipe));

        if (type == PanelType.PromotionCheck)
        {
            var origin = live.FindSource(values.Source);
            fitting = field == PanelField.ToSource
                ? fitting.Where(s => origin is not null && s.Recipe == origin.Recipe && s.Id != origin.Id)
                : fitting.Where(s => s.Role != SourceRole.Watch);
        }
        else if (type == PanelType.ProfileStat)
        {
            var recipe = SplitStatKey(values.Stat)?.Recipe;
            fitting = fitting.Where(s => s.Recipe == recipe);
        }

        var list = fitting.OrderBy(s => s.Role).ToList();
        var severalRecipes = list.Select(s => s.Recipe).Distinct(StringComparer.Ordinal).Count() > 1;
        return [.. list.Select(s => new FormChoice(s.Id, SourceLabel(live, s, severalRecipes)))];
    }

    /// <summary>Ticked stats (only they are read), plus the panel's current stat while its recipe still offers it.</summary>
    public static IReadOnlyList<FormChoice> StatChoices(PanelType type, LiveBoard live, FormValues values, PanelSettings? current)
    {
        string? only = null;
        if (type is PanelType.PromotionCheck or PanelType.PastPeriods)
        {
            only = live.FindSource(values.Source)?.Recipe;
            if (only is null) return [];
        }

        var found = new List<(InstalledRecipe Installed, List<RecipeStat> Stats)>();
        foreach (var installed in live.Installed.Where(i => Fits(type, i.Recipe) && (only is null || i.Recipe.Slug == only)))
        {
            var tracked = installed.State.TrackedStats(installed.Recipe);
            var keep = current is { Stat: { } stat } && current.Recipe == installed.Recipe.Slug ? stat : null;
            var stats = RecipeStats.Offered(installed.Recipe, installed.State.StatChoices.Keys)
                .Where(s => tracked.Contains(s.Key) || s.Key == keep)
                .ToList();
            if (stats.Count > 0) found.Add((installed, stats));
        }

        return [.. found.SelectMany(f => f.Stats.Select(s => new FormChoice(
            StatKey(f.Installed.Recipe.Slug, s.Key),
            found.Count > 1 ? $"{s.Label} · {f.Installed.Recipe.Name}" : s.Label)))];
    }

    /// <summary>"Your top account", then your accounts by name. Never anyone else's.</summary>
    public static IReadOnlyList<FormChoice> AccountChoices(LiveBoard live) =>
    [
        new FormChoice(TopAccountKey, "Your top account"),
        .. live.Accounts
            .Where(a => a.RobloxUserId != 0)
            .DistinctBy(a => a.RobloxUserId)
            .OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(a => new FormChoice(a.RobloxUserId.ToString(CultureInfo.InvariantCulture), a.DisplayName)),
    ];

    /// <summary>What a new panel's form starts with: the main, the main's other sources, the first ticked stat (§9.4 "default: → main").</summary>
    public static FormValues Defaults(PanelType type, LiveBoard live)
    {
        var none = new FormValues();
        switch (type)
        {
            case PanelType.Race:
            {
                var fitting = SourceChoices(type, PanelField.Sources, live, none)
                    .Select(c => live.FindSource(c.Key))
                    .OfType<Source>()
                    .Where(s => s.Enabled)
                    .ToList();
                var recipe = fitting.FirstOrDefault()?.Recipe;
                return new FormValues(Sources: fitting.Where(s => s.Recipe == recipe).Take(PanelModels.MaxRace).Select(s => s.Id).ToList());
            }

            case PanelType.PromotionCheck:
            {
                var main = live.Sources.FirstOrDefault(s =>
                    s.Enabled && s.Role == SourceRole.Main && live.FindRecipe(s.Recipe) is { } installed && Fits(type, installed.Recipe));
                var origin = main is null ? null : live.Sources.FirstOrDefault(s => s.Enabled && s.Role == SourceRole.Mine && s.Recipe == main.Recipe);
                var values = new FormValues(Source: origin?.Id, ToSource: main?.Id);
                return values with { Stat = StatChoices(type, live, values, null).FirstOrDefault()?.Key };
            }

            case PanelType.AccountCard:
                return new FormValues(Account: TopAccountKey, Stat: StatChoices(type, live, none, null).FirstOrDefault()?.Key);

            case PanelType.MyAccounts or PanelType.Records or PanelType.ProfileStat:
                return new FormValues(Stat: StatChoices(type, live, none, null).FirstOrDefault()?.Key);

            default:
            {
                var source = SourceChoices(type, PanelField.Source, live, none).FirstOrDefault(c => live.FindSource(c.Key)?.Enabled == true)?.Key;
                var values = new FormValues(Source: source);
                return type == PanelType.PastPeriods ? values with { Stat = StatChoices(type, live, values, null).FirstOrDefault()?.Key } : values;
            }
        }
    }

    /// <summary>A saved panel's settings as form values, for ⋯ settings.</summary>
    public static FormValues From(PanelSettings settings) => new(
        settings.SourceId,
        settings.SourceIds,
        settings.ToSourceId,
        settings.Stat is { } stat && settings.Recipe.Length > 0 ? StatKey(settings.Recipe, stat) : null,
        settings.UserId is { } id ? id.ToString(CultureInfo.InvariantCulture) : TopAccountKey);

    /// <summary>
    /// The settings a panel type reads (stage 1's <see cref="PanelModels"/>). The recipe comes from the source
    /// (or the first raced source) for source panels and from the stat for stat panels; a stat from another
    /// recipe than the source's is dropped.
    /// </summary>
    public static PanelSettings Build(PanelType type, FormValues values, LiveBoard live)
    {
        var stat = SplitStatKey(values.Stat);
        var source = live.FindSource(values.Source);
        var raced = (values.Sources ?? Array.Empty<string>()).Select(live.FindSource).OfType<Source>().ToList();

        var recipe = type switch
        {
            PanelType.Race => raced.FirstOrDefault()?.Recipe,
            PanelType.MyAccounts or PanelType.Records or PanelType.ProfileStat or PanelType.AccountCard => stat?.Recipe,
            _ => source?.Recipe,
        } ?? "";

        var sourceId = type switch
        {
            PanelType.ProfileStat => source is not null && source.Recipe == recipe
                ? source.Id
                : live.Sources.FirstOrDefault(s => s.Enabled && s.Recipe == recipe)?.Id,
            PanelType.Race or PanelType.MyAccounts or PanelType.Records or PanelType.AccountCard => null,
            _ => source?.Id,
        };

        var keepsStat = type is not (PanelType.Standing or PanelType.Race or PanelType.Top or PanelType.LiveLeaderboard);
        long? userId = type == PanelType.AccountCard
                       && long.TryParse(values.Account, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;

        return new PanelSettings(
            recipe,
            SourceId: sourceId,
            SourceIds: type == PanelType.Race ? raced.Select(s => s.Id).ToList() : null,
            ToSourceId: type == PanelType.PromotionCheck ? values.ToSource : null,
            Stat: keepsStat && stat is { } picked && picked.Recipe == recipe ? picked.Stat : null,
            UserId: userId);
    }

    /// <summary>What's wrong with a panel's settings, in the recipe's words, or null. The window refuses to save while there is one.</summary>
    public static string? Problem(PanelType type, PanelSettings settings, LiveBoard live)
    {
        var installed = live.FindRecipe(settings.Recipe);
        var (word, words) = installed is { Recipe.IsGroupList: false }
            ? (RecipeWords.Group(installed.Recipe), RecipeWords.GroupsLower(installed.Recipe))
            : type == PanelType.Top ? ("source", "sources") : GroupWords(live);

        foreach (var field in Fields(type, adding: false))
        {
            var problem = field switch
            {
                PanelField.Source => SourceProblem(type, settings, live, word),
                PanelField.Sources => RaceProblem(settings, live, word, words),
                PanelField.ToSource => ToSourceProblem(settings, live, word),
                PanelField.Stat => StatProblem(type, settings, installed),
                _ => settings.UserId is { } id && !live.MyUserIds.Contains(id) ? "Choose one of your accounts." : null,
            };
            if (problem is not null) return problem;
        }

        return null;
    }

    /// <summary>The words for a form with no recipe picked yet: the first recipe with inputs, else "source".</summary>
    internal static (string Group, string Groups) GroupWords(LiveBoard live) =>
        live.Installed.FirstOrDefault(i => !i.Recipe.IsGroupList && i.Recipe.Inputs.Count > 0)?.Recipe is { } recipe
            ? (RecipeWords.Group(recipe), RecipeWords.GroupsLower(recipe))
            : ("source", "sources");

    private static string SourceLabel(LiveBoard live, Source source, bool withRecipe)
    {
        var name = live.SourceName(source);
        var label = source.Role switch
        {
            SourceRole.Main => $"★ {name}",
            SourceRole.Watch => $"{name} · watching",
            _ => name,
        };

        if (withRecipe && live.FindRecipe(source.Recipe) is { } installed && installed.Recipe.Name != name) label += $" · {installed.Recipe.Name}";
        return source.Enabled ? label : $"{label} · off";
    }

    private static string? SourceProblem(PanelType type, PanelSettings settings, LiveBoard live, string word)
    {
        if (settings.SourceId is null) return type == PanelType.ProfileStat ? null : $"Choose a {word}.";
        if (live.FindSource(settings.SourceId) is not { } source) return $"This panel's {word} was removed. Choose another.";
        if (source.Recipe != settings.Recipe || live.FindRecipe(source.Recipe) is not { } installed || !Fits(type, installed.Recipe))
        {
            return $"This panel can't show that {word}.";
        }

        return type == PanelType.PromotionCheck && source.Role == SourceRole.Watch ? $"Choose a {word} your accounts are in." : null;
    }

    private static string? RaceProblem(PanelSettings settings, LiveBoard live, string word, string words)
    {
        var ids = settings.SourceIds ?? Array.Empty<string>();
        if (ids.Count < 2 || ids.Count > PanelModels.MaxRace) return $"Choose 2 to {PanelModels.MaxRace} {words}.";
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count) return $"Choose each {word} once.";

        var sources = ids.Select(live.FindSource).ToList();
        if (sources.Any(s => s is null)) return $"One of this race's {words} was removed. Choose another.";
        if (sources.Any(s => s!.Recipe != settings.Recipe)) return "Every line in a race comes from the same recipe.";

        return live.FindRecipe(settings.Recipe) is { } installed && Fits(PanelType.Race, installed.Recipe) ? null : $"These {words} have no total to race.";
    }

    private static string? ToSourceProblem(PanelSettings settings, LiveBoard live, string word)
    {
        if (settings.ToSourceId is null) return $"Choose a {word} to compare with.";
        if (live.FindSource(settings.ToSourceId) is not { } to) return $"This panel's {word} was removed. Choose another.";

        return to.Recipe != settings.Recipe || to.Id == settings.SourceId ? $"Choose a different {word} of the same recipe to compare with." : null;
    }

    private static string? StatProblem(PanelType type, PanelSettings settings, InstalledRecipe? installed)
    {
        if (settings.Stat is null) return type == PanelType.PastPeriods ? null : "Choose a stat.";
        if (installed is null || RecipeStats.Find(installed.Recipe, settings.Stat) is null) return "This panel's stat was removed. Choose another.";

        return Fits(type, installed.Recipe) ? null : "This panel can't show that stat.";
    }
}
```

- [ ] **Step 5: Implement the gallery**

Create `src/Board/PanelGallery.cs`:

```csharp
using Labs626.UrScore.Core;

namespace Labs626.UrScore.Board;

/// <summary>One card in the gallery: what the panel needs and shows, and why it can't be added yet.</summary>
public sealed record GalleryCard(PanelType Type, string Title, string Needs, string Shows, bool CanAdd, string WhyNot);

/// <summary>
/// The panel gallery (spec §9.4): the ten panels in the spec's order, in the recipe's words (§3.4). A card
/// is enabled only when its form can be filled from what is installed now.
/// </summary>
public static class PanelGallery
{
    private const string TickFirst = "Tick Show or Send on a stat in Setup › Stats first.";

    public static IReadOnlyList<PanelType> Order { get; } =
    [
        PanelType.Standing, PanelType.Race, PanelType.MyAccounts, PanelType.PromotionCheck, PanelType.AccountCard,
        PanelType.PastPeriods, PanelType.Records, PanelType.Top, PanelType.ProfileStat, PanelType.LiveLeaderboard,
    ];

    /// <summary>The same titles <see cref="PanelModels"/> gives the panels: "Clan standing", "Battle race", "Past battles".</summary>
    public static string Title(PanelType type, LiveBoard live)
    {
        var groupRecipe = live.Installed.FirstOrDefault(i => !i.Recipe.IsGroupList && i.Recipe.Inputs.Count > 0 && i.Recipe.Headline.Count > 0)?.Recipe;
        var periodRecipe = live.Installed.FirstOrDefault(i => !i.Recipe.IsGroupList && i.Recipe.Period is not null)?.Recipe;

        return type switch
        {
            PanelType.Standing => groupRecipe is null ? "Standing" : $"{RecipeWords.Capital(RecipeWords.Group(groupRecipe))} standing",
            PanelType.Race => periodRecipe is null ? "Race" : $"{RecipeWords.Capital(RecipeWords.Period(periodRecipe))} race",
            PanelType.MyAccounts => "My accounts",
            PanelType.PromotionCheck => "Promotion check",
            PanelType.AccountCard => "Account card",
            PanelType.PastPeriods => periodRecipe is null ? "Past periods" : $"Past {RecipeWords.Periods(periodRecipe)}",
            PanelType.Records => "Records",
            PanelType.Top => $"Top of the {(periodRecipe is null ? "list" : RecipeWords.Period(periodRecipe))}",
            PanelType.ProfileStat => "Profile stat",
            _ => "Live leaderboard",
        };
    }

    public static IReadOnlyList<GalleryCard> Cards(LiveBoard live)
    {
        var (group, groups) = PanelForms.GroupWords(live);
        var period = live.Installed.FirstOrDefault(i => !i.Recipe.IsGroupList && i.Recipe.Period is not null)?.Recipe is { } withPeriod
            ? RecipeWords.Period(withPeriod)
            : "period";

        return [.. Order.Select(type =>
        {
            var (needs, shows, whyNot) = type switch
            {
                PanelType.Standing => ($"Needs a {group}.", $"Place, total, the last hour's gain and the {period} line.", $"Add a {group} in Setup first."),
                PanelType.Race => ($"Needs 2 to 5 {groups} of one recipe.", $"Each {group}'s total over the current {period}, one line each.",
                    $"Needs at least 2 {groups} of one recipe. Add them in Setup."),
                PanelType.MyAccounts => ("Needs a stat.", $"Your accounts by that stat, grouped by {group}, with rank, change and what was sent.", TickFirst),
                PanelType.PromotionCheck => ($"Needs a {group} your accounts are in, and one to compare with (your main unless you pick another).",
                    $"Where each of your accounts would place in the other {group} now. Live only.",
                    $"Needs a {group} your accounts are in, another to compare with, and a ticked stat."),
                PanelType.AccountCard => ("Needs one of your accounts.", $"Big numbers, a line over time, rank, best {period} and when it was last read.", TickFirst),
                PanelType.PastPeriods => ($"Needs a {group}.", $"Finished {period}s newest first: place, total and your best account.",
                    $"Needs a {group} whose recipe keeps past {period}s."),
                PanelType.Records => ("Needs a stat.", $"Best {period}, best rank, highest value, biggest day and fastest 7 days.", TickFirst),
                PanelType.Top => ("Needs a recipe that lists groups.", $"The top of the {period} live, with your {groups} placed where they'd rank.",
                    "Import a recipe that lists groups, and turn it on in Setup."),
                PanelType.ProfileStat => ("Needs a stat from a recipe that reads without a period.", "Your accounts with value, today's gain and 7-day gain.",
                    "Needs a ticked stat from a recipe that reads without a period."),
                _ => ($"Needs a {group}.", "Every row live, your accounts marked. Never saved.", $"Add a {group} in Setup first."),
            };

            var canAdd = CanAdd(type, live);
            return new GalleryCard(type, Title(type, live), needs, shows, canAdd, canAdd ? "" : whyNot);
        })];
    }

    private static bool CanAdd(PanelType type, LiveBoard live)
    {
        var none = new FormValues();
        return type switch
        {
            PanelType.Race => live.Sources
                .Where(s => live.FindRecipe(s.Recipe) is { } installed && PanelForms.Fits(type, installed.Recipe))
                .GroupBy(s => s.Recipe, StringComparer.Ordinal)
                .Any(g => g.Count() >= 2),
            PanelType.PromotionCheck => PanelForms.SourceChoices(type, PanelField.Source, live, none).Any(origin =>
                PanelForms.SourceChoices(type, PanelField.ToSource, live, new FormValues(Source: origin.Key)).Count > 0
                && PanelForms.StatChoices(type, live, new FormValues(Source: origin.Key), null).Count > 0),
            PanelType.MyAccounts or PanelType.Records or PanelType.ProfileStat or PanelType.AccountCard =>
                PanelForms.StatChoices(type, live, none, null).Count > 0,
            _ => PanelForms.SourceChoices(type, PanelField.Source, live, none).Count > 0,
        };
    }
}
```

- [ ] **Step 6: Run the gates**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: 0 warnings, 0 errors.

Run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: all pass, `PanelFormsTests` and `PanelGalleryTests` included. `NoHostnameFenceTests` stays green (no host in the new files).

- [ ] **Step 7: Commit**

```text
git add src/Board/PanelForms.cs src/Board/PanelGallery.cs tests/PanelFormsTests.cs tests/PanelGalleryTests.cs
git commit -m "board: what each panel needs, what its form offers, and a gallery card only when it can be filled

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: Saved boards and tabs

Spec §9.2 (tabs: click to switch, + Board adds one empty or from a starter, right-click offers Rename, Duplicate or Delete), §8 (the top bar gains tabs), rulings R1, R3, R11, R12, R13, R17. `AppServices` owns `boards.json`; the board window draws whichever board is selected, from its `PanelDef`s instead of stage 1's `StarterBoard`. With no file yet it still draws the starter, rebuilt from your sources, so stage 1's first run is unchanged.

The window is split into partial files from here on: `BoardWindow.xaml.cs` (this task: drawing, tabs, the top bar), then `BoardWindow.PanelSettings.cs` (Task 6), `BoardWindow.Editing.cs` (Task 7) and `BoardWindow.PopOuts.cs` (Task 8). Each later task hooks in through a small, named edit to this file.

A saved board with no panels shows "This board has no panels yet". Its **Add panel** button arrives with the gallery in Task 6; until then the button is hidden.

**Files:**
- Modify: `src/Composition/AppServices.cs`
- Modify: `src/UI/BoardText.cs`, `tests/BoardTextTests.cs`
- Modify: `src/UI/BoardWindow.xaml`, `src/UI/BoardWindow.xaml.cs` (replaced)
- Create: `src/UI/Boards/AddBoardWindow.xaml`, `src/UI/Boards/AddBoardWindow.xaml.cs`
- Create: `src/UI/Boards/BoardNameWindow.xaml`, `src/UI/Boards/BoardNameWindow.xaml.cs`

**Interfaces:**
- Consumes: Tasks 1–3 (`BoardDef`, `BoardsFile`, `BoardDefs.FromStarter`/`Key`/`Sanitize`/`CleanName`/`NewBoardId`, `BoardEdits.Add`/`Rename`/`Duplicate`/`Delete`/`NextName`/`AutomationIds`/`AnchorSourceId`, `PanelGrid.SetTall`); stage 1's `StarterBoards`, `PanelViews`, `BoardText`, `ImportFlow`, `SetupWindow`, `SetupPages`.
- Produces:
  - `AppServices.Boards`, `BoardsFollowStarter`, `BoardsProblem`, `SaveBoards(IReadOnlyList<BoardDef>)`
  - `BoardText.EmptyFor(StarterBoard starter, bool followsStarter, BoardDef board)` and the `NoPanels` empty state
  - `sealed record BoardTabItem(string Id, string Name)` (`Labs626.UrScore.UI`)
  - `AddBoardWindow(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources, string suggestedName)` with `BoardDef? Result`
  - `BoardNameWindow(string name)` with `string BoardName`
  - in `BoardWindow`, the seams later tasks edit: `ShownBoard`, `ViewKey`, `BuildPanels`, `CreatePanelView`, `RenderPanel`, `ShownPanels`, `OnEmptyStateClick`, `SaveBoards`, and the field `_boardId`

- [ ] **Step 1: Write the failing tests**

Add to `tests/BoardTextTests.cs`, inside the class (after the last test):

```csharp
    [Fact]
    public void NoRecipesShowsOverEveryBoardAndTheStartersStatesOnlyWhileItFollows()
    {
        var noStats = StarterBoards.Build([Installed(Clan)], [MainClan]);
        var saved = new BoardDef("b-00000001", "Rivals", []);
        var withPanel = saved with
        {
            Panels = [new PanelDef("p-00000001", PanelType.Standing, new PanelSize(3), new PanelSettings(Clan.Slug, SourceId: MainClan.Id))],
        };

        Assert.Equal(BoardEmpty.NoRecipes, BoardText.EmptyFor(StarterBoards.Build([], []), followsStarter: false, withPanel));
        Assert.Equal(BoardEmpty.NoStats, BoardText.EmptyFor(noStats, followsStarter: true, withPanel));
        Assert.Equal(BoardEmpty.NoPanels, BoardText.EmptyFor(noStats, followsStarter: false, saved));
        Assert.Equal(BoardEmpty.None, BoardText.EmptyFor(noStats, followsStarter: false, withPanel));
    }

    [Fact]
    public void ASavedBoardWithNoPanelsSaysSo() =>
        Assert.Equal(
            ("This board has no panels yet", "Add panels from the gallery, then arrange them with Edit board.", ""),
            BoardText.EmptyState(BoardEmpty.NoPanels, Clan));
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: FAIL with CS0117 `'BoardText' does not contain a definition for 'EmptyFor'`.

- [ ] **Step 3: The empty states**

In `src/UI/BoardText.cs`, add after `Attribution`:

```csharp
    /// <summary>
    /// Which empty state a board shows (R1): no recipes over every board; the starter's own states while the
    /// board still follows your sources; a saved board with no panels; else none.
    /// </summary>
    public static BoardEmpty EmptyFor(StarterBoard starter, bool followsStarter, BoardDef board) =>
        starter.Empty == BoardEmpty.NoRecipes ? BoardEmpty.NoRecipes
        : followsStarter ? starter.Empty
        : board.Panels.Count == 0 ? BoardEmpty.NoPanels
        : BoardEmpty.None;
```

In `EmptyState`, add this arm before `_ => ("", "", ""),`:

```csharp
            BoardEmpty.NoPanels => ("This board has no panels yet",
                "Add panels from the gallery, then arrange them with Edit board.",
                ""),
```

- [ ] **Step 4: Boards in the composition root**

In `src/Composition/AppServices.cs`:

After `private readonly SourceStore _sourceStore = new(SourceStore.DefaultPath);` add:

```csharp
    private readonly BoardsFile _boardsFile = new(BoardsFile.DefaultPath, TimeProvider.System);

    /// <summary>What <c>boards.json</c> holds, or null while there is no file (R1) or it couldn't be read (R3).</summary>
    private IReadOnlyList<BoardDef>? _savedBoards;
```

In the constructor, replace the last line `LoadAtStart();` with:

```csharp
        LoadAtStart();
        LoadBoards();
```

After `CurrentBoard()` add:

```csharp
    /// <summary>
    /// The saved boards, or, while nothing is saved, the starter board rebuilt from your sources with fixed ids
    /// (R1, R2). Never empty.
    /// </summary>
    public IReadOnlyList<BoardDef> Boards =>
        _savedBoards ?? [BoardDefs.FromStarter(StarterBoards.Build(Installed, Sources), freshIds: false)];

    /// <summary>True until the first board change writes <c>boards.json</c>.</summary>
    public bool BoardsFollowStarter => _savedBoards is null;

    /// <summary>Why the saved boards aren't showing, or null.</summary>
    public string? BoardsProblem { get; private set; }

    /// <summary>
    /// Writes <c>boards.json</c> with only your own account ids (R17), keeps an unreadable old file beside it
    /// (R3), and redraws. Throws when the file can't be written; nothing changes then.
    /// </summary>
    public void SaveBoards(IReadOnlyList<BoardDef> boards)
    {
        var mine = KnownAccounts.Where(a => a.RobloxUserId != 0).Select(a => a.RobloxUserId).ToHashSet();
        var clean = BoardDefs.Sanitize(boards, mine);

        var kept = _boardsFile.Save(clean);
        _savedBoards = clean;
        BoardsProblem = null;
        if (kept is not null) AddTrail($"BOARDS: the unreadable boards file was kept as {Path.GetFileName(kept)}.");

        RaiseChanged();
    }
```

After the `LoadAtStart` method add:

```csharp
    /// <summary>
    /// No file, or an empty list, leaves the starter following your sources (R1). A file that can't be read shows
    /// the starter too, and says why until the next save keeps a copy of it (R3).
    /// </summary>
    private void LoadBoards()
    {
        var load = _boardsFile.Load();
        if (!load.Readable)
        {
            BoardsProblem = "Your boards file couldn't be read, so the starter board is showing. The next change to a board keeps a copy of the old file beside the new one.";
            AddTrail("BOARDS NOT READ: showing the starter board.");
            return;
        }

        if (load.Boards.Count > 0) _savedBoards = load.Boards;
    }
```

- [ ] **Step 5: The add-a-board and rename dialogs**

Create `src/UI/Boards/AddBoardWindow.xaml`:

```xml
<Window x:Class="Labs626.UrScore.UI.AddBoardWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Add a board" Width="440" SizeToContent="Height" ResizeMode="NoResize"
        WindowStartupLocation="CenterOwner" ShowInTaskbar="False"
        Background="{DynamicResource BgBrush}" Foreground="{DynamicResource WhiteBrush}"
        FontFamily="{StaticResource BodyFont}">
    <StackPanel Margin="20,18,20,18">
        <TextBlock Text="Add a board" Style="{StaticResource Heading}" />

        <TextBlock Text="NAME" Style="{StaticResource SectionLabel}" Margin="0,16,0,4" />
        <TextBox x:Name="NewBoardNameBox" AutomationProperties.Name="Board name" />

        <TextBlock Text="START FROM" Style="{StaticResource SectionLabel}" Margin="0,16,0,4" />
        <Button x:Name="EmptyBoardButton" Content="An empty board" HorizontalContentAlignment="Left" Click="OnEmptyClick"
                AutomationProperties.Name="Empty board" />
        <Button x:Name="BattleBoardButton" Margin="0,8,0,0" Click="OnBattleClick" />
        <Button x:Name="GrindBoardButton" Margin="0,8,0,0" Click="OnGrindClick" />
        <TextBlock x:Name="StarterLine" Style="{StaticResource Muted}" Margin="0,8,0,0" />

        <Button Content="Cancel" IsCancel="True" HorizontalAlignment="Right" Margin="0,18,0,0" />
    </StackPanel>
</Window>
```

Create `src/UI/Boards/AddBoardWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Labs626.UrScore.Board;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

using Source = Labs626.UrScore.Core.Source;

/// <summary>+ Board (spec §9.2): an empty board, or a starter built from your sources now.</summary>
public partial class AddBoardWindow : Window
{
    private readonly StarterBoard _battle;
    private readonly StarterBoard _grind;
    private readonly string _suggestedName;

    public AddBoardWindow(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources, string suggestedName)
    {
        InitializeComponent();
        ThemeService.Attach(this);

        _suggestedName = suggestedName;
        _battle = StarterBoards.Build(installed, sources, StarterBoards.Battle);
        _grind = StarterBoards.Build(installed, sources, StarterBoards.Grind);

        NewBoardNameBox.Text = suggestedName;
        Describe(BattleBoardButton, _battle);
        Describe(GrindBoardButton, _grind);
        StarterLine.Text = _battle.Panels.Count == 0 && _grind.Panels.Count == 0
            ? "The starters fill in once a recipe has ticked stats and a source."
            : "A starter is built from your sources as they are now, and stays as you arrange it.";

        Loaded += (_, _) =>
        {
            NewBoardNameBox.Focus();
            NewBoardNameBox.SelectAll();
        };
    }

    /// <summary>The board to add, once a choice was made.</summary>
    public BoardDef? Result { get; private set; }

    private static void Describe(Button button, StarterBoard starter)
    {
        var count = starter.Panels.Count;
        button.Content = count == 0 ? $"{starter.Name}: nothing to show yet" : $"{starter.Name}: {count} panel{(count == 1 ? "" : "s")}";
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.IsEnabled = count > 0;
        AutomationProperties.SetName(button, $"{starter.Name} starter");
    }

    private void OnEmptyClick(object sender, RoutedEventArgs e) =>
        Finish(new BoardDef(BoardDefs.NewBoardId(), NameFor(null), []));

    private void OnBattleClick(object sender, RoutedEventArgs e) => FinishStarter(_battle);

    private void OnGrindClick(object sender, RoutedEventArgs e) => FinishStarter(_grind);

    private void FinishStarter(StarterBoard starter) =>
        Finish(BoardDefs.FromStarter(starter, freshIds: true) with { Name = NameFor(starter.Name) });

    /// <summary>The typed name. A starter picked with the suggested name left alone takes the starter's name.</summary>
    private string NameFor(string? starterName)
    {
        var typed = BoardDefs.CleanName(NewBoardNameBox.Text);
        if (starterName is not null && (typed is null || typed == _suggestedName)) return starterName;
        return typed ?? _suggestedName;
    }

    private void Finish(BoardDef board)
    {
        Result = board;
        DialogResult = true;
    }
}
```

Create `src/UI/Boards/BoardNameWindow.xaml`:

```xml
<Window x:Class="Labs626.UrScore.UI.BoardNameWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Rename board" Width="400" SizeToContent="Height" ResizeMode="NoResize"
        WindowStartupLocation="CenterOwner" ShowInTaskbar="False"
        Background="{DynamicResource BgBrush}" Foreground="{DynamicResource WhiteBrush}"
        FontFamily="{StaticResource BodyFont}">
    <StackPanel Margin="20,18,20,18">
        <TextBlock Text="NAME" Style="{StaticResource SectionLabel}" />
        <TextBox x:Name="BoardNameBox" AutomationProperties.Name="Board name" />
        <TextBlock x:Name="NameProblemLine" Style="{StaticResource Refusal}" Margin="0,6,0,0" Visibility="Collapsed" />
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,16,0,0">
            <Button Content="Cancel" IsCancel="True" Margin="0,0,8,0" />
            <Button x:Name="SaveNameButton" Content="Save" IsDefault="True" Style="{StaticResource PrimaryButton}" Click="OnSaveClick" />
        </StackPanel>
    </StackPanel>
</Window>
```

Create `src/UI/Boards/BoardNameWindow.xaml.cs`:

```csharp
using System.Windows;
using Labs626.UrScore.Board;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

/// <summary>Rename, from a tab's right-click menu (spec §9.2).</summary>
public partial class BoardNameWindow : Window
{
    public BoardNameWindow(string name)
    {
        InitializeComponent();
        ThemeService.Attach(this);

        BoardName = name;
        BoardNameBox.Text = name;
        BoardNameBox.MaxLength = BoardDefs.MaxNameLength;
        Loaded += (_, _) =>
        {
            BoardNameBox.Focus();
            BoardNameBox.SelectAll();
        };
    }

    public string BoardName { get; private set; }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (BoardDefs.CleanName(BoardNameBox.Text) is not { } name)
        {
            NameProblemLine.Text = "Type a name for this board.";
            NameProblemLine.Visibility = Visibility.Visible;
            return;
        }

        BoardName = name;
        DialogResult = true;
    }
}
```

- [ ] **Step 6: Tabs in the top bar**

Replace `src/UI/BoardWindow.xaml` with:

```xml
<Window x:Class="Labs626.UrScore.UI.BoardWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="clr-namespace:Labs626.UrScore.UI"
        Title="RoRoRo Ur Score" Height="900" Width="1280" MinHeight="560" MinWidth="720"
        Background="{DynamicResource BgBrush}" Foreground="{DynamicResource WhiteBrush}"
        FontFamily="{StaticResource BodyFont}">
    <Window.Resources>
        <!-- A tab reads like Setup's list: no fill until hovered, and a cyan bar under the board on screen. -->
        <Style x:Key="BoardTab" TargetType="ListBoxItem">
            <Setter Property="Foreground" Value="{DynamicResource MutedTextBrush}" />
            <Setter Property="Padding" Value="12,6" />
            <Setter Property="Margin" Value="0,0,2,0" />
            <Setter Property="Cursor" Value="Hand" />
            <Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}" />
            <Setter Property="AutomationProperties.Name" Value="{Binding Name}" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ListBoxItem">
                        <Grid>
                            <Border x:Name="Item" Background="Transparent" CornerRadius="6" Padding="{TemplateBinding Padding}">
                                <ContentPresenter />
                            </Border>
                            <Border x:Name="Bar" Height="2" VerticalAlignment="Bottom" Margin="8,0"
                                    Background="{DynamicResource CyanBrush}" Visibility="Collapsed" />
                        </Grid>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="Item" Property="Background" Value="{DynamicResource RowHoverBrush}" />
                            </Trigger>
                            <Trigger Property="IsSelected" Value="True">
                                <Setter TargetName="Bar" Property="Visibility" Value="Visible" />
                                <Setter Property="Foreground" Value="{DynamicResource WhiteBrush}" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>
    <DockPanel>
        <!-- The top bar (spec §8): tabs and the period line on the left; Start, Test now and Setup on the right. -->
        <Border DockPanel.Dock="Top" BorderBrush="{DynamicResource DividerBrush}" BorderThickness="0,0,0,1" Padding="16,8">
            <DockPanel LastChildFill="True">
                <StackPanel x:Name="TopButtons" DockPanel.Dock="Right" Orientation="Horizontal" VerticalAlignment="Center">
                    <Button x:Name="StartStopButton" Content="Start" Style="{StaticResource PrimaryButton}" Click="OnStartStopClick"
                            AutomationProperties.Name="Start or stop reading" />
                    <Button x:Name="TestNowButton" Content="Test now" Margin="8,0,0,0" Click="OnTestNowClick"
                            AutomationProperties.Name="Read every source once now" />
                    <Button x:Name="SetupButton" Margin="8,0,0,0" Click="OnSetupClick" AutomationProperties.Name="Setup">
                        <StackPanel Orientation="Horizontal">
                            <TextBlock Text="&#xE713;" FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets" VerticalAlignment="Center" Margin="0,0,6,0" />
                            <TextBlock Text="Setup" VerticalAlignment="Center" />
                        </StackPanel>
                    </Button>
                </StackPanel>
                <Image x:Name="BoardIcon" DockPanel.Dock="Left" Width="22" Height="22" Margin="0,0,10,0" Visibility="Collapsed"
                       VerticalAlignment="Center" AutomationProperties.Name="Main source icon" />
                <ListBox x:Name="BoardTabs" DockPanel.Dock="Left" ItemContainerStyle="{StaticResource BoardTab}" DisplayMemberPath="Name"
                         Background="Transparent" BorderThickness="0" VerticalAlignment="Center" SelectionChanged="OnTabChanged"
                         ScrollViewer.HorizontalScrollBarVisibility="Disabled" AutomationProperties.Name="Boards">
                    <ListBox.ItemsPanel>
                        <ItemsPanelTemplate>
                            <StackPanel Orientation="Horizontal" />
                        </ItemsPanelTemplate>
                    </ListBox.ItemsPanel>
                    <ListBox.ContextMenu>
                        <ContextMenu x:Name="TabMenu" Opened="OnTabMenuOpened" Background="{DynamicResource RowBgBrush}"
                                     Foreground="{DynamicResource WhiteBrush}" BorderBrush="{DynamicResource EdgeBrush}">
                            <MenuItem x:Name="RenameBoardItem" Header="Rename…" Click="OnRenameBoardClick" />
                            <MenuItem x:Name="DuplicateBoardItem" Header="Duplicate" Click="OnDuplicateBoardClick" />
                            <MenuItem x:Name="DeleteBoardItem" Header="Delete…" Click="OnDeleteBoardClick" />
                        </ContextMenu>
                    </ListBox.ContextMenu>
                </ListBox>
                <Button x:Name="AddBoardButton" DockPanel.Dock="Left" Content="+ Board" Style="{StaticResource FlatButton}"
                        HorizontalAlignment="Left" VerticalAlignment="Center" Margin="4,0,14,0" Click="OnAddBoardClick"
                        AutomationProperties.Name="Add a board" />
                <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                    <Ellipse x:Name="LiveDot" Width="8" Height="8" Margin="0,0,8,0" Fill="{DynamicResource CyanBrush}" Visibility="Collapsed" />
                    <TextBlock x:Name="PeriodLine" Style="{StaticResource Muted}" VerticalAlignment="Center" TextWrapping="NoWrap"
                               TextTrimming="CharacterEllipsis" />
                </StackPanel>
            </DockPanel>
        </Border>

        <StackPanel DockPanel.Dock="Top" Margin="16,8,16,0">
            <TextBlock x:Name="StateLine" FontWeight="SemiBold" TextWrapping="Wrap" AutomationProperties.HelpText="What Ur Score is doing" />
            <TextBlock x:Name="DetailLine" Style="{StaticResource Muted}" Margin="0,2,0,0" />
        </StackPanel>

        <TextBlock x:Name="AttributionLine" DockPanel.Dock="Bottom" Style="{StaticResource Muted}" FontSize="11" Margin="16,4,16,8" />

        <Grid>
            <ScrollViewer x:Name="BoardScroll" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled" Padding="14">
                <ui:PanelGrid x:Name="BoardPanels" AutomationProperties.Name="Board" />
            </ScrollViewer>
            <StackPanel x:Name="EmptyState" Visibility="Collapsed" HorizontalAlignment="Center" VerticalAlignment="Center" MaxWidth="480">
                <TextBlock x:Name="EmptyStateLine" Style="{StaticResource Heading}" TextAlignment="Center" />
                <TextBlock x:Name="EmptyStateDetail" Style="{StaticResource Muted}" TextAlignment="Center" Margin="0,8,0,0" />
                <Button x:Name="EmptyStateButton" Style="{StaticResource PrimaryButton}" HorizontalAlignment="Center" Margin="0,16,0,0"
                        Click="OnEmptyStateClick" />
            </StackPanel>
        </Grid>
    </DockPanel>
</Window>
```

- [ ] **Step 7: The window draws the selected saved board**

Replace `src/UI/BoardWindow.xaml.cs` with:

```csharp
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Labs626.UrScore.Board;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

using Source = Labs626.UrScore.Core.Source;

/// <summary>One tab. Its text is the board's name, so a screen reader and UI Automation read the name.</summary>
public sealed record BoardTabItem(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// The board (spec §8, §9.2): tabs and the top bar, the selected board's panels in a 12-column grid, and the
/// empty states. Everything it shows comes from <see cref="AppServices"/>; every board change goes through
/// <see cref="BoardEdits"/> and <see cref="AppServices.SaveBoards"/>.
/// </summary>
public partial class BoardWindow : Window
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(20) };

    /// <summary>What is on the grid now, in order: each panel's definition, its view and its automation id.</summary>
    private readonly List<(PanelDef Def, FrameworkElement View, string AutomationId)> _panels = [];

    /// <summary>Other members' names for a Live leaderboard panel, in memory only, each id asked once.</summary>
    private readonly Dictionary<long, string> _names = [];
    private readonly HashSet<long> _askedNames = [];

    private SetupWindow? _setup;
    private string? _boardId;
    private string? _renderedKey;
    private string? _tabsKey;
    private string? _anchorSourceId;
    private string? _emptyRecipe;
    private string? _boardsNote;
    private BoardEmpty _empty;
    private bool _busy;
    private bool _selectingTab;

    public BoardWindow(AppServices services)
    {
        InitializeComponent();
        ThemeService.Attach(this);
        _services = services;
        _services.Changed += Render;
        _services.IconChanged += ApplyIcon;
        _clock.Tick += (_, _) => RenderLines();
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _clock.Stop();
            _services.Changed -= Render;
            _services.IconChanged -= ApplyIcon;
        };

        StartStopButton.IsEnabled = false;
        TestNowButton.IsEnabled = false;
        Render();
        StateLine.Text = "Reading your score book…";
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _services.StartFollowingTheme();
        _clock.Start();

        try
        {
            await _services.LoadBookAsync();
        }
        catch (Exception ex)
        {
            StateLine.Text = "Your score book could not be read.";
            DetailLine.Text = _services.Redactor.Redact(ex.Message);
            _services.AddTrail($"BOOK NOT LOADED: {ex}");
            return;
        }

        StartStopButton.IsEnabled = true;
        TestNowButton.IsEnabled = true;
        Render();

        // Spec §7.1: a recipe with inputs and no sources opens Setup on its Clans page.
        if (SetupPages.FirstRunPage(_services.Installed, _services.Sources) is { } page) OpenSetup(page);
    }

    /// <summary>The window must never die on a redraw: what fails goes to the trail, by type only, and the last drawing stays.</summary>
    private void Render()
    {
        try
        {
            RenderBoard();
        }
        catch (Exception ex)
        {
            _services.AddTrail($"BOARD NOT DRAWN: {ex.GetType().Name}");
        }
    }

    private void RenderBoard()
    {
        var boards = _services.Boards;
        var board = ShownBoard(boards);
        _boardId = board.Id;

        RenderTabs(boards, board);
        RenderEmpty(board);

        var key = ViewKey(board);
        if (key != _renderedKey)
        {
            _renderedKey = key;
            BuildPanels(board);
        }

        _anchorSourceId = BoardEdits.AnchorSourceId(board, _services.Sources);
        var live = _services.CurrentBoard();

        // The reader is filled on a worker thread until the book has loaded; nothing may read it before then.
        if (_services.ReaderLoaded)
        {
            foreach (var (def, view, _) in _panels) RenderPanel(def, view, live);
        }

        _ = ResolveNamesAsync(live);
        RenderLines(live);
    }

    /// <summary>The board on screen: the selected tab's, else the first (R12).</summary>
    private BoardDef ShownBoard(IReadOnlyList<BoardDef> boards) =>
        boards.FirstOrDefault(b => b.Id == _boardId) ?? boards[0];

    /// <summary>The panels are rebuilt only when this changes.</summary>
    private string ViewKey(BoardDef board) => BoardDefs.Key(board);

    private void BuildPanels(BoardDef board)
    {
        BoardPanels.Children.Clear();
        _panels.Clear();

        var ids = BoardEdits.AutomationIds(board);
        for (var i = 0; i < board.Panels.Count; i++)
        {
            var def = board.Panels[i];
            var view = CreatePanelView(def, ids[i]);
            PanelGrid.SetSpan(view, def.Size.Span);
            PanelGrid.SetTall(view, def.Size.Tall);
            BoardPanels.Children.Add(view);
            _panels.Add((def, view, ids[i]));
        }
    }

    /// <summary>The control for one panel on the board, named by its automation id.</summary>
    private FrameworkElement CreatePanelView(PanelDef def, string automationId)
    {
        var view = PanelViews.Create(def.Type);
        AutomationProperties.SetAutomationId(view, automationId);
        return view;
    }

    private void RenderPanel(PanelDef def, FrameworkElement view, LiveBoard live)
    {
        try
        {
            PanelViews.Render(view, def.Settings, live, _services.Reader, _names);
        }
        catch (Exception ex)
        {
            // A panel never takes the window down. The type only: a message could name another player's id.
            _services.AddTrail($"PANEL NOT DRAWN: {def.Type}: {ex.GetType().Name}");
        }
    }

    /// <summary>Every panel showing anywhere, for looking up Live leaderboard names.</summary>
    private IEnumerable<PanelDef> ShownPanels() => _panels.Select(p => p.Def);

    private void RenderTabs(IReadOnlyList<BoardDef> boards, BoardDef shown)
    {
        var key = string.Join("|", boards.Select(b => $"{b.Id}={b.Name}"));
        _selectingTab = true;
        try
        {
            if (key != _tabsKey)
            {
                _tabsKey = key;
                BoardTabs.ItemsSource = boards.Select(b => new BoardTabItem(b.Id, b.Name)).ToList();
            }

            if ((BoardTabs.SelectedItem as BoardTabItem)?.Id != shown.Id)
            {
                BoardTabs.SelectedItem = BoardTabs.Items.OfType<BoardTabItem>().FirstOrDefault(t => t.Id == shown.Id);
            }
        }
        finally
        {
            _selectingTab = false;
        }
    }

    private void RenderLines(LiveBoard? live = null)
    {
        try
        {
            RenderLinesCore(live ?? _services.CurrentBoard());
        }
        catch (Exception ex)
        {
            _services.AddTrail($"LINES NOT DRAWN: {ex.GetType().Name}");
        }
    }

    private void RenderLinesCore(LiveBoard live)
    {
        PeriodLine.Text = BoardText.TopLine(live, _anchorSourceId);
        LiveDot.Visibility = live.Running ? Visibility.Visible : Visibility.Collapsed;
        StartStopButton.Content = live.Running ? "Stop" : "Start";
        AttributionLine.Text = BoardText.Attribution(live);

        if (!_services.ReaderLoaded) return;
        StateLine.Text = BoardText.StateLine(live, _services.EverStarted);

        var detail = BoardText.DetailLine(live, _services.BudgetWarning);
        DetailLine.Text = detail.Length > 0 ? detail : _boardsNote ?? _services.BoardsProblem ?? "";
    }

    private void RenderEmpty(BoardDef board)
    {
        var starter = StarterBoards.Build(_services.Installed, _services.Sources);
        _empty = BoardText.EmptyFor(starter, _services.BoardsFollowStarter, board);
        _emptyRecipe = starter.RecipeSlug;

        var recipe = _services.Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, _emptyRecipe, StringComparison.Ordinal))?.Recipe;
        var (line, detail, button) = BoardText.EmptyState(_empty, recipe);
        var empty = _empty != BoardEmpty.None;

        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        BoardScroll.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyStateLine.Text = line;
        EmptyStateDetail.Text = detail;
        EmptyStateButton.Content = button;
        EmptyStateButton.Visibility = button.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(EmptyStateButton, button);
    }

    // ---- tabs ----

    private void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectingTab || BoardTabs.SelectedItem is not BoardTabItem tab || tab.Id == _boardId) return;

        _boardId = tab.Id;
        Render();
    }

    private void OnTabMenuOpened(object sender, RoutedEventArgs e) =>
        DeleteBoardItem.IsEnabled = _services.Boards.Count > 1;

    private void OnAddBoardClick(object sender, RoutedEventArgs e)
    {
        var boards = _services.Boards;
        var dialog = new AddBoardWindow(_services.Installed, _services.Sources, BoardEdits.NextName(boards)) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } board) return;

        if (SaveBoards(BoardEdits.Add(boards, board))) ShowBoard(board.Id);
    }

    private void OnRenameBoardClick(object sender, RoutedEventArgs e)
    {
        var boards = _services.Boards;
        var board = ShownBoard(boards);
        var dialog = new BoardNameWindow(board.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var renamed = BoardEdits.Rename(boards, board.Id, dialog.BoardName);
        if (!ReferenceEquals(renamed, boards)) SaveBoards(renamed);
    }

    private void OnDuplicateBoardClick(object sender, RoutedEventArgs e)
    {
        var boards = _services.Boards;
        var duplicated = BoardEdits.Duplicate(boards, ShownBoard(boards).Id);
        if (ReferenceEquals(duplicated, boards)) return;

        var copy = duplicated.First(b => boards.All(old => old.Id != b.Id));
        if (SaveBoards(duplicated)) ShowBoard(copy.Id);
    }

    private void OnDeleteBoardClick(object sender, RoutedEventArgs e)
    {
        var boards = _services.Boards;
        var board = ShownBoard(boards);
        if (boards.Count <= 1) return;

        var answer = MessageBox.Show(this,
            $"Delete the {board.Name} board? Its panels go with it. Your score book isn't touched.",
            "Ur Score", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;

        var remaining = BoardEdits.Delete(boards, board.Id);
        if (!ReferenceEquals(remaining, boards) && SaveBoards(remaining)) ShowBoard(remaining[0].Id);
    }

    private void ShowBoard(string boardId)
    {
        _boardId = boardId;
        Render();
    }

    /// <summary>Saves through the services. A file that can't be written says so on the detail line, and the board stays as it was.</summary>
    private bool SaveBoards(IReadOnlyList<BoardDef> boards)
    {
        try
        {
            _services.SaveBoards(boards);
            _boardsNote = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _boardsNote = $"Your boards couldn't be saved: {_services.Redactor.Redact(ex.Message)}";
            _services.AddTrail($"BOARDS NOT SAVED: {ex.GetType().Name}");
            RenderLines();
            return false;
        }
    }

    // ---- names, the top bar, Setup ----

    /// <summary>Names for Live leaderboard panels only, and only when resolveNames allows. In memory only.</summary>
    private async Task ResolveNamesAsync(LiveBoard live)
    {
        if (!_services.Settings.ResolveNames) return;

        var mine = live.MyUserIds;
        var ids = ShownPanels()
            .Where(p => p.Type == PanelType.LiveLeaderboard)
            .Select(p => live.FindSource(p.Settings.SourceId))
            .OfType<Source>()
            .SelectMany(s => live.SnapshotOf(s.Id)?.Rows ?? [])
            .Select(r => r.UserId)
            .Where(id => !mine.Contains(id) && _askedNames.Add(id))
            .ToList();
        if (ids.Count == 0) return;

        var resolved = await _services.Names.ResolveAsync(ids, CancellationToken.None);
        foreach (var (id, name) in resolved) _names[id] = name;
        if (resolved.Count > 0) Render();
    }

    private async void OnStartStopClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        StartStopButton.IsEnabled = false;

        try
        {
            if (_services.Running)
            {
                _services.Stop();
            }
            else if (_services.Installed.Count == 0)
            {
                StateLine.Text = "No recipe to run.";
                DetailLine.Text = "Import a recipe first.";
            }
            else
            {
                await _services.StartAsync();
            }
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
        finally
        {
            _busy = false;
            StartStopButton.IsEnabled = _services.ReaderLoaded;
            RenderLines();
        }
    }

    private async void OnTestNowClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_services.Installed.Count == 0)
        {
            StateLine.Text = "No recipe to test.";
            DetailLine.Text = "Import a recipe first.";
            return;
        }

        _busy = true;
        TestNowButton.IsEnabled = false;
        StateLine.Text = "Reading every source once…";

        try
        {
            await _services.TestNowAsync();
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
        finally
        {
            _busy = false;
            TestNowButton.IsEnabled = _services.ReaderLoaded;
            RenderLines();
        }
    }

    private void OnSetupClick(object sender, RoutedEventArgs e) => OpenSetup(null);

    private async void OnEmptyStateClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        if (_empty == BoardEmpty.NoStats)
        {
            OpenSetup(SetupPages.Stats);
            return;
        }

        if (_empty == BoardEmpty.NoSources && _emptyRecipe is { } slug)
        {
            OpenSetup(SetupPages.ClansId(slug));
            return;
        }

        if (_empty != BoardEmpty.NoRecipes) return;

        _busy = true;
        try
        {
            var outcome = await ImportFlow.RunAsync(this, _services, text => DetailLine.Text = text);
            if (outcome is null) return;

            DetailLine.Text = outcome.Message;
            if (outcome.ChooseSources) OpenSetup(SetupPages.ClansId(outcome.Slug));
        }
        finally
        {
            _busy = false;
        }
    }

    private void OpenSetup(string? page)
    {
        if (_setup is not null)
        {
            if (page is not null) _setup.ShowPage(page);
            _setup.Activate();
            return;
        }

        _setup = new SetupWindow(_services, page) { Owner = this };
        _setup.Closed += (_, _) => _setup = null;
        _setup.Show();
    }

    private void ShowFailure(Exception ex)
    {
        // The window must never die on a cycle.
        StateLine.Text = "Something unexpected went wrong.";
        DetailLine.Text = _services.Redactor.Redact(ex.Message);
        _services.AddTrail($"EXCEPTION: {ex}");
    }

    /// <summary>The main source's icon on the window, the taskbar and the top bar; anything that fails keeps Ur Score's own.</summary>
    private void ApplyIcon(string? file)
    {
        if (file is null)
        {
            ResetIcon();
            return;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(file);
            image.EndInit();
            image.Freeze();

            Icon = image;
            BoardIcon.Source = image;
            BoardIcon.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            ResetIcon();
            _services.AddTrail($"ICON: the picture did not decode ({ex.GetType().Name}), so the window keeps Ur Score's.");
        }
    }

    private void ResetIcon()
    {
        ClearValue(IconProperty);
        BoardIcon.Source = null;
        BoardIcon.Visibility = Visibility.Collapsed;
    }
}
```

- [ ] **Step 8: Run the gates**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: 0 warnings, 0 errors.

Run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: all pass. `ThemeFenceTests` stays green: the new XAML uses only `DynamicResource` brushes and `Background="Transparent"`.

- [ ] **Step 9: Look at it once**

Close any running Ur Score, then `dotnet build Ur-Score.csproj -c Release` and start `bin\Release\net10.0-windows\626labs.ur-score.exe` with your own data folder:
1. With no `boards.json` in `%LOCALAPPDATA%\626labs.ur-score`, the board looks exactly as in stage 1, with one tab named Battle (or Grind), and no `boards.json` appears.
2. **+ Board** → **An empty board**: a second tab "Board 2" is selected and says "This board has no panels yet". `boards.json` now exists and holds both boards.
3. Right-click the first tab → **Rename…** → "Main": the tab reads Main. **Duplicate**: "Main copy" appears after it, selected. **Delete…** → OK on "Main copy": it's gone. With one board left, **Delete…** is disabled.
4. Quit and restart: the same tabs come back, the first one selected.

- [ ] **Step 10: Commit**

```text
git add src/Composition/AppServices.cs src/UI/BoardText.cs tests/BoardTextTests.cs src/UI/BoardWindow.xaml src/UI/BoardWindow.xaml.cs src/UI/Boards/AddBoardWindow.xaml src/UI/Boards/AddBoardWindow.xaml.cs src/UI/Boards/BoardNameWindow.xaml src/UI/Boards/BoardNameWindow.xaml.cs
git commit -m "board: saved boards as tabs, with + Board, rename, duplicate and delete

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: Panel tools, panel settings and the gallery

Spec §9.2 ("outside edit mode, each panel shows ⧉ and ⋯"), §9.4 (the gallery; adding asks only for what the panel needs; "This panel's clan was removed" with **Choose another**), rulings R10, R14–R16. Every panel's header gets its tools, all raised as one bubbling routed event, `PanelFrame.ToolEvent`. Which tools show is set by three inherited attached flags on the grid, so each task turns on only the tools it handles: this task turns on ⋯ and **Choose another**; Task 7 turns on the edit tools and Task 8 the ⧉ button.

⋯ opens the panel's full form; saving it saves the board. **Choose another** opens the same form. A saved board with no panels gets **Add panel**, which opens the gallery and then the form for the picked type, and saves the board with the new panel.

**Files:**
- Modify: `src/UI/Panels/PanelFrame.xaml`, `src/UI/Panels/PanelFrame.xaml.cs`
- Create: `src/UI/Boards/PanelGalleryWindow.xaml`, `src/UI/Boards/PanelGalleryWindow.xaml.cs`
- Create: `src/UI/Boards/PanelSettingsWindow.xaml`, `src/UI/Boards/PanelSettingsWindow.xaml.cs`
- Create: `src/UI/BoardWindow.PanelSettings.cs`
- Modify: `src/UI/BoardWindow.xaml.cs` (constructor and `OnEmptyStateClick`)
- Modify: `src/UI/BoardText.cs`, `tests/BoardTextTests.cs` (the empty board's button)

**Interfaces:**
- Consumes: Task 1 (`PanelSize`, `PanelDef`), Task 2 (`BoardEdits.SetSettings`/`AddPanel`/`Replace`), Task 4 (`PanelForms`, `PanelGallery`, `FormChoice`, `FormValues`, `PanelField`), Task 5 (`BoardWindow` seams: `_panels`, `ShownBoard`, `SaveBoards`, `OnEmptyStateClick`).
- Produces:
  - `enum PanelTool`, `PanelToolEventArgs`, `PanelFrame.ToolEvent`, attached inherited `ShowEditTools`, `ShowSettings`, `ShowPopOut` (bool) and `CurrentSize` (`PanelSize?`), exactly as in the contract
  - `PanelGalleryWindow(IReadOnlyList<GalleryCard> cards)` with `PanelType? Picked`
  - `PanelSettingsWindow(PanelType type, string title, LiveBoard live, PanelSettings? current, bool adding)` with `PanelSettings? Result`; `sealed class RaceChoice`
  - in `BoardWindow`: `PanelDef? PanelAt(object? source)`, `void ChangeBoard(Func<BoardDef, BoardDef> edit)`, `void AddPanelFromGallery()`, `void OpenPanelSettings(PanelDef def)`

- [ ] **Step 1: Update the empty board's test**

In `tests/BoardTextTests.cs`, replace the body of `ASavedBoardWithNoPanelsSaysSo` so the button reads **Add panel**:

```csharp
    [Fact]
    public void ASavedBoardWithNoPanelsSaysSo() =>
        Assert.Equal(
            ("This board has no panels yet", "Add panels from the gallery, then arrange them with Edit board.", "Add panel"),
            BoardText.EmptyState(BoardEmpty.NoPanels, Clan));
```

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror` then `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter FullyQualifiedName~BoardTextTests`
Expected: `ASavedBoardWithNoPanelsSaysSo` FAILS (the button is still empty).

In `src/UI/BoardText.cs`, in the `BoardEmpty.NoPanels` arm, replace the third element `""` with `"Add panel"`:

```csharp
            BoardEmpty.NoPanels => ("This board has no panels yet",
                "Add panels from the gallery, then arrange them with Edit board.",
                "Add panel"),
```

- [ ] **Step 2: Every panel tool on the panel header**

Replace `src/UI/Panels/PanelFrame.xaml` with:

```xml
<UserControl x:Class="Labs626.UrScore.UI.PanelFrame"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel>
        <!-- Edit mode (spec §9.2): drag, move, size, tall, remove. Its own row, so a small panel wraps it instead of hiding its title. -->
        <WrapPanel x:Name="EditTools" Margin="0,0,0,8" Visibility="Collapsed">
            <Border x:Name="DragHandle" Background="Transparent" Cursor="SizeAll" Padding="6,3" VerticalAlignment="Center"
                    MouseLeftButtonDown="OnDragHandleDown" ToolTip="Drag to move" AutomationProperties.Name="Drag to move">
                <TextBlock Text="⠿" Foreground="{DynamicResource MutedTextBrush}" />
            </Border>
            <Button x:Name="MoveEarlierButton" Content="←" Tag="MoveEarlier" Style="{StaticResource FlatButton}" Padding="6,2"
                    Click="OnToolClick" AutomationProperties.Name="Move earlier" />
            <Button x:Name="MoveLaterButton" Content="→" Tag="MoveLater" Style="{StaticResource FlatButton}" Padding="6,2"
                    Click="OnToolClick" AutomationProperties.Name="Move later" />
            <ComboBox x:Name="SizeBox" Width="96" MinHeight="26" Margin="4,0" SelectionChanged="OnSizeChanged" AutomationProperties.Name="Size">
                <ComboBoxItem x:Name="SmallItem" Content="Small" AutomationProperties.Name="Small" />
                <ComboBoxItem x:Name="HalfItem" Content="Half" AutomationProperties.Name="Half" />
                <ComboBoxItem x:Name="WideItem" Content="Wide" AutomationProperties.Name="Wide" />
            </ComboBox>
            <!-- Checked and Unchecked, not Click: a UI Automation toggle changes IsChecked without a click. -->
            <CheckBox x:Name="TallBox" Content="Tall" VerticalAlignment="Center" Margin="4,0" Checked="OnTallChanged" Unchecked="OnTallChanged"
                      AutomationProperties.Name="Tall" />
            <Button x:Name="RemovePanelButton" Content="✕" Tag="Remove" Style="{StaticResource FlatButton}" Padding="6,2"
                    Click="OnToolClick" AutomationProperties.Name="Remove panel" />
        </WrapPanel>
        <DockPanel LastChildFill="False">
            <StackPanel x:Name="PanelTools" DockPanel.Dock="Right" Orientation="Horizontal">
                <Button x:Name="PopOutButton" Content="⧉" Tag="PopOut" Style="{StaticResource FlatButton}" Padding="6,2" Visibility="Collapsed"
                        Click="OnToolClick" AutomationProperties.Name="Pop out" />
                <Button x:Name="PanelSettingsButton" Content="⋯" Tag="Settings" Style="{StaticResource FlatButton}" Padding="6,2" Visibility="Collapsed"
                        Click="OnToolClick" AutomationProperties.Name="Panel settings" />
            </StackPanel>
            <TextBlock x:Name="PanelTitle" DockPanel.Dock="Left" Text="{Binding Title}" Style="{StaticResource SectionLabel}"
                       Margin="0" VerticalAlignment="Center" />
            <Border DockPanel.Dock="Left" Margin="8,0,0,0" Padding="7,1" CornerRadius="9" BorderThickness="1"
                    BorderBrush="{DynamicResource EdgeBrush}" VerticalAlignment="Center"
                    Visibility="{Binding HasChip, Converter={StaticResource BoolToVisible}}">
                <TextBlock Text="{Binding Chip}" FontFamily="{StaticResource MonoFont}" FontSize="10.5"
                           Foreground="{DynamicResource MutedTextBrush}" />
            </Border>
            <TextBlock x:Name="PanelOverdue" DockPanel.Dock="Left" Text="overdue" Margin="8,0,0,0" FontSize="11"
                       VerticalAlignment="Center" Foreground="{DynamicResource MagentaBrush}"
                       Visibility="{Binding Overdue, Converter={StaticResource BoolToVisible}}" />
        </DockPanel>
        <TextBlock x:Name="PanelSubtitle" Text="{Binding Subtitle}" Style="{StaticResource Muted}" Margin="0,4,0,0"
                   Visibility="{Binding HasSubtitle, Converter={StaticResource BoolToVisible}}" />
        <TextBlock x:Name="PanelStale" Text="{Binding Stale}" TextWrapping="Wrap" Margin="0,10,0,0"
                   Visibility="{Binding HasStale, Converter={StaticResource BoolToVisible}}" />
        <Button x:Name="ChooseAnotherButton" Content="Choose another" Tag="ChooseAnother" Style="{StaticResource FlatButton}"
                HorizontalAlignment="Left" Margin="0,6,0,0" Visibility="Collapsed" Click="OnToolClick"
                AutomationProperties.Name="Choose another" />
    </StackPanel>
</UserControl>
```

Replace `src/UI/Panels/PanelFrame.xaml.cs` with:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

/// <summary>Every tool a panel's header can raise (spec §9.2, §9.3, §9.4).</summary>
public enum PanelTool { PopOut, Settings, ChooseAnother, DragStart, MoveEarlier, MoveLater, Resize, Remove }

public sealed class PanelToolEventArgs(RoutedEvent routedEvent, PanelTool tool, PanelSize? size = null) : RoutedEventArgs(routedEvent)
{
    public PanelTool Tool { get; } = tool;

    /// <summary>The size picked, for <see cref="PanelTool.Resize"/>.</summary>
    public PanelSize? Size { get; } = size;
}

/// <summary>
/// A panel's header, bound to its <c>PanelHead</c>, with the panel's tools. The tools decide nothing: each raises
/// <see cref="ToolEvent"/>, and whoever holds the panel (the board, a pop-out) handles it. Which tools show is
/// inherited from the grid through <see cref="ShowEditToolsProperty"/>, <see cref="ShowSettingsProperty"/> and
/// <see cref="ShowPopOutProperty"/>.
/// </summary>
public partial class PanelFrame : UserControl
{
    public static readonly RoutedEvent ToolEvent = EventManager.RegisterRoutedEvent(
        "Tool", RoutingStrategy.Bubble, typeof(EventHandler<PanelToolEventArgs>), typeof(PanelFrame));

    public static readonly DependencyProperty ShowEditToolsProperty = RegisterFlag("ShowEditTools");

    public static readonly DependencyProperty ShowSettingsProperty = RegisterFlag("ShowSettings");

    public static readonly DependencyProperty ShowPopOutProperty = RegisterFlag("ShowPopOut");

    public static readonly DependencyProperty CurrentSizeProperty = DependencyProperty.RegisterAttached(
        "CurrentSize", typeof(PanelSize), typeof(PanelFrame),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits, OnToolsChanged));

    /// <summary>True while the size box is being set from the panel's size, so that isn't read as a pick.</summary>
    private bool _showingSize;

    public PanelFrame()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => UpdateTools();
        Loaded += (_, _) => UpdateTools();
    }

    public static bool GetShowEditTools(DependencyObject element) => (bool)element.GetValue(ShowEditToolsProperty);

    public static void SetShowEditTools(DependencyObject element, bool value) => element.SetValue(ShowEditToolsProperty, value);

    public static bool GetShowSettings(DependencyObject element) => (bool)element.GetValue(ShowSettingsProperty);

    public static void SetShowSettings(DependencyObject element, bool value) => element.SetValue(ShowSettingsProperty, value);

    public static bool GetShowPopOut(DependencyObject element) => (bool)element.GetValue(ShowPopOutProperty);

    public static void SetShowPopOut(DependencyObject element, bool value) => element.SetValue(ShowPopOutProperty, value);

    public static PanelSize? GetCurrentSize(DependencyObject element) => (PanelSize?)element.GetValue(CurrentSizeProperty);

    public static void SetCurrentSize(DependencyObject element, PanelSize? value) => element.SetValue(CurrentSizeProperty, value);

    private static DependencyProperty RegisterFlag(string name) => DependencyProperty.RegisterAttached(
        name, typeof(bool), typeof(PanelFrame),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, OnToolsChanged));

    private static void OnToolsChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is PanelFrame frame) frame.UpdateTools();
    }

    private void UpdateTools()
    {
        var editing = GetShowEditTools(this);
        var settings = GetShowSettings(this);

        EditTools.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        PopOutButton.Visibility = GetShowPopOut(this) && !editing ? Visibility.Visible : Visibility.Collapsed;
        PanelSettingsButton.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        ChooseAnotherButton.Visibility = settings && DataContext is PanelHead { HasStale: true } ? Visibility.Visible : Visibility.Collapsed;

        var size = GetCurrentSize(this);
        _showingSize = true;
        try
        {
            // R5: a starter's 4- or 5-wide panel shows no size picked until one is.
            SizeBox.SelectedItem = size?.Span switch
            {
                PanelSize.Small => SmallItem,
                PanelSize.Half => HalfItem,
                PanelSize.Wide => WideItem,
                _ => null,
            };
            TallBox.IsChecked = size?.Tall == true;
        }
        finally
        {
            _showingSize = false;
        }
    }

    private void OnToolClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<PanelTool>(tag, out var tool))
        {
            RaiseEvent(new PanelToolEventArgs(ToolEvent, tool));
        }
    }

    private void OnSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        e.Handled = true;
        if (_showingSize || SizeBox.SelectedItem is not ComboBoxItem item) return;

        var span = ReferenceEquals(item, SmallItem) ? PanelSize.Small : ReferenceEquals(item, HalfItem) ? PanelSize.Half : PanelSize.Wide;
        RaiseEvent(new PanelToolEventArgs(ToolEvent, PanelTool.Resize, new PanelSize(span, TallBox.IsChecked == true)));
    }

    private void OnTallChanged(object sender, RoutedEventArgs e)
    {
        if (_showingSize) return;

        var span = GetCurrentSize(this)?.Span ?? PanelSize.Half;
        RaiseEvent(new PanelToolEventArgs(ToolEvent, PanelTool.Resize, new PanelSize(span, TallBox.IsChecked == true)));
    }

    private void OnDragHandleDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        RaiseEvent(new PanelToolEventArgs(ToolEvent, PanelTool.DragStart));
    }
}
```

- [ ] **Step 3: The gallery window**

Create `src/UI/Boards/PanelGalleryWindow.xaml`:

```xml
<Window x:Class="Labs626.UrScore.UI.PanelGalleryWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Add a panel" Width="760" Height="680" MinWidth="440" MinHeight="380"
        WindowStartupLocation="CenterOwner" ShowInTaskbar="False"
        Background="{DynamicResource BgBrush}" Foreground="{DynamicResource WhiteBrush}"
        FontFamily="{StaticResource BodyFont}">
    <DockPanel Margin="20,18,20,18">
        <TextBlock DockPanel.Dock="Top" Text="Add a panel" Style="{StaticResource Heading}" />
        <TextBlock DockPanel.Dock="Top" Style="{StaticResource Muted}" Margin="0,4,0,12"
                   Text="Each panel asks only for what it needs. Any panel can be added more than once." />
        <Button DockPanel.Dock="Bottom" Content="Cancel" IsCancel="True" HorizontalAlignment="Right" Margin="0,12,0,0" />
        <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
            <ItemsControl x:Name="GalleryCards" AutomationProperties.Name="Panels you can add">
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate>
                        <UniformGrid Columns="2" />
                    </ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border Style="{StaticResource PanelCard}" Margin="0,0,10,10">
                            <StackPanel>
                                <TextBlock Text="{Binding Title}" Style="{StaticResource SectionLabel}" />
                                <TextBlock Text="{Binding Shows}" TextWrapping="Wrap" />
                                <TextBlock Text="{Binding Needs}" Style="{StaticResource Muted}" Margin="0,6,0,0" />
                                <TextBlock x:Name="WhyNot" Text="{Binding WhyNot}" Margin="0,6,0,0">
                                    <TextBlock.Style>
                                        <Style TargetType="TextBlock" BasedOn="{StaticResource Refusal}">
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding CanAdd}" Value="True">
                                                    <Setter Property="Visibility" Value="Collapsed" />
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </TextBlock.Style>
                                </TextBlock>
                                <Button Content="Add" HorizontalAlignment="Left" Margin="0,10,0,0" IsEnabled="{Binding CanAdd}" Click="OnAddClick"
                                        AutomationProperties.Name="{Binding Title, StringFormat='Add {0}'}" />
                            </StackPanel>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
    </DockPanel>
</Window>
```

Create `src/UI/Boards/PanelGalleryWindow.xaml.cs`:

```csharp
using System.Windows;
using Labs626.UrScore.Board;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

/// <summary>The panel gallery (spec §9.4). Picking a card closes it; the form for that type comes next.</summary>
public partial class PanelGalleryWindow : Window
{
    public PanelGalleryWindow(IReadOnlyList<GalleryCard> cards)
    {
        InitializeComponent();
        ThemeService.Attach(this);
        GalleryCards.ItemsSource = cards;
    }

    public PanelType? Picked { get; private set; }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GalleryCard { CanAdd: true } card }) return;

        Picked = card.Type;
        DialogResult = true;
    }
}
```

- [ ] **Step 4: The panel settings window**

Create `src/UI/Boards/PanelSettingsWindow.xaml`:

```xml
<Window x:Class="Labs626.UrScore.UI.PanelSettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Panel settings" Width="460" SizeToContent="Height" ResizeMode="NoResize"
        WindowStartupLocation="CenterOwner" ShowInTaskbar="False"
        Background="{DynamicResource BgBrush}" Foreground="{DynamicResource WhiteBrush}"
        FontFamily="{StaticResource BodyFont}">
    <StackPanel Margin="20,18,20,18">
        <TextBlock x:Name="FormTitle" Style="{StaticResource Heading}" />

        <StackPanel x:Name="SourceRow" Visibility="Collapsed">
            <TextBlock x:Name="SourceLabel" Style="{StaticResource SectionLabel}" Margin="0,16,0,4" />
            <ComboBox x:Name="SourceBox" DisplayMemberPath="Label" SelectionChanged="OnPickChanged" />
        </StackPanel>

        <StackPanel x:Name="SourcesRow" Visibility="Collapsed">
            <TextBlock x:Name="SourcesLabel" Style="{StaticResource SectionLabel}" Margin="0,16,0,4" />
            <ItemsControl x:Name="RaceSources" AutomationProperties.Name="Race lines">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <CheckBox Content="{Binding Label}" IsChecked="{Binding Picked, Mode=TwoWay}" Margin="0,3,0,0" Checked="OnRaceChanged" Unchecked="OnRaceChanged"
                                  AutomationProperties.Name="{Binding Label, StringFormat='Race {0}'}" />
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </StackPanel>

        <StackPanel x:Name="ToSourceRow" Visibility="Collapsed">
            <TextBlock Text="TO" Style="{StaticResource SectionLabel}" Margin="0,16,0,4" />
            <ComboBox x:Name="ToSourceBox" DisplayMemberPath="Label" SelectionChanged="OnPickChanged" AutomationProperties.Name="To" />
        </StackPanel>

        <StackPanel x:Name="StatRow" Visibility="Collapsed">
            <TextBlock Text="STAT" Style="{StaticResource SectionLabel}" Margin="0,16,0,4" />
            <ComboBox x:Name="StatBox" DisplayMemberPath="Label" SelectionChanged="OnPickChanged" AutomationProperties.Name="Stat" />
        </StackPanel>

        <StackPanel x:Name="AccountRow" Visibility="Collapsed">
            <TextBlock Text="ACCOUNT" Style="{StaticResource SectionLabel}" Margin="0,16,0,4" />
            <ComboBox x:Name="AccountBox" DisplayMemberPath="Label" SelectionChanged="OnPickChanged" AutomationProperties.Name="Account" />
        </StackPanel>

        <TextBlock x:Name="SettingsProblemLine" Style="{StaticResource Refusal}" Margin="0,12,0,0" Visibility="Collapsed" />

        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,18,0,0">
            <Button Content="Cancel" IsCancel="True" Margin="0,0,8,0" />
            <Button x:Name="SaveSettingsButton" IsDefault="True" Style="{StaticResource PrimaryButton}" Click="OnSaveClick" />
        </StackPanel>
    </StackPanel>
</Window>
```

Create `src/UI/Boards/PanelSettingsWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Labs626.UrScore.Board;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

/// <summary>One source a race can draw, ticked or not.</summary>
public sealed class RaceChoice(string key, string label, bool picked)
{
    public string Key { get; } = key;

    public string Label { get; } = label;

    public bool Picked { get; set; } = picked;
}

/// <summary>
/// Adding a panel, and ⋯ settings (spec §9.4). The form's fields, choices, defaults and problems all come from
/// <see cref="PanelForms"/>; this window only shows them. Fields the form doesn't ask for keep their defaults.
/// </summary>
public partial class PanelSettingsWindow : Window
{
    private readonly PanelType _type;
    private readonly LiveBoard _live;
    private readonly PanelSettings? _current;
    private readonly IReadOnlyList<PanelField> _fields;
    private bool _filling;

    public PanelSettingsWindow(PanelType type, string title, LiveBoard live, PanelSettings? current, bool adding)
    {
        InitializeComponent();
        ThemeService.Attach(this);

        _type = type;
        _live = live;
        _current = current;
        _fields = PanelForms.Fields(type, adding);

        Title = adding ? $"Add {title}" : "Panel settings";
        FormTitle.Text = title;
        SaveSettingsButton.Content = adding ? "Add panel" : "Save";

        var (group, groups) = PanelForms.GroupWords(live);
        var sourceWord = type switch
        {
            PanelType.PromotionCheck => "from",
            PanelType.Top or PanelType.ProfileStat => "source",
            _ => group,
        };
        SourceLabel.Text = sourceWord.ToUpperInvariant();
        AutomationProperties.SetName(SourceBox, RecipeWords.Capital(sourceWord));
        SourcesLabel.Text = $"{groups.ToUpperInvariant()} TO RACE (2 TO {PanelModels.MaxRace})";

        SourceRow.Visibility = Shows(PanelField.Source);
        SourcesRow.Visibility = Shows(PanelField.Sources);
        ToSourceRow.Visibility = Shows(PanelField.ToSource);
        StatRow.Visibility = Shows(PanelField.Stat);
        AccountRow.Visibility = Shows(PanelField.Account);

        Fill(current is null ? PanelForms.Defaults(type, live) : PanelForms.From(current));
        Check();
    }

    /// <summary>The settings to save, once the form was saved with no problem.</summary>
    public PanelSettings? Result { get; private set; }

    private Visibility Shows(PanelField field) => _fields.Contains(field) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Refills every list from the values, keeping each pick while it is still offered. A field the form doesn't show falls back to its first choice.</summary>
    private void Fill(FormValues values)
    {
        _filling = true;
        try
        {
            Pick(SourceBox, PanelField.Source, PanelForms.SourceChoices(_type, PanelField.Source, _live, values), values.Source);
            var withSource = values with { Source = (SourceBox.SelectedItem as FormChoice)?.Key };

            Pick(ToSourceBox, PanelField.ToSource, PanelForms.SourceChoices(_type, PanelField.ToSource, _live, withSource), values.ToSource);
            Pick(StatBox, PanelField.Stat, PanelForms.StatChoices(_type, _live, withSource, _current), values.Stat);
            Pick(AccountBox, PanelField.Account, PanelForms.AccountChoices(_live), values.Account);

            // Profile stat's source list follows the stat just picked.
            if (_type == PanelType.ProfileStat)
            {
                var withStat = withSource with { Stat = (StatBox.SelectedItem as FormChoice)?.Key };
                Pick(SourceBox, PanelField.Source, PanelForms.SourceChoices(_type, PanelField.Source, _live, withStat), values.Source);
            }

            var picked = (values.Sources ?? []).ToHashSet(StringComparer.Ordinal);
            RaceSources.ItemsSource = PanelForms.SourceChoices(_type, PanelField.Sources, _live, values)
                .Select(c => new RaceChoice(c.Key, c.Label, picked.Contains(c.Key)))
                .ToList();
        }
        finally
        {
            _filling = false;
        }
    }

    private void Pick(ComboBox box, PanelField field, IReadOnlyList<FormChoice> choices, string? key)
    {
        box.ItemsSource = choices;
        box.SelectedItem = choices.FirstOrDefault(c => c.Key == key) ?? (_fields.Contains(field) ? null : choices.FirstOrDefault());
    }

    private FormValues Values() => new(
        Source: (SourceBox.SelectedItem as FormChoice)?.Key,
        Sources: RaceSources.Items.OfType<RaceChoice>().Where(c => c.Picked).Select(c => c.Key).ToList(),
        ToSource: (ToSourceBox.SelectedItem as FormChoice)?.Key,
        Stat: (StatBox.SelectedItem as FormChoice)?.Key,
        Account: (AccountBox.SelectedItem as FormChoice)?.Key);

    /// <summary>Shows what's wrong with the form as it stands, and returns it.</summary>
    private string? Check()
    {
        var problem = PanelForms.Problem(_type, PanelForms.Build(_type, Values(), _live), _live);
        SettingsProblemLine.Text = problem ?? "";
        SettingsProblemLine.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
        return problem;
    }

    private void OnPickChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling) return;

        Fill(Values());
        Check();
    }

    private void OnRaceChanged(object sender, RoutedEventArgs e)
    {
        if (!_filling) Check();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (Check() is not null) return;

        Result = PanelForms.Build(_type, Values(), _live);
        DialogResult = true;
    }
}
```

- [ ] **Step 5: The board handles ⋯, Choose another and Add panel**

Create `src/UI/BoardWindow.PanelSettings.cs`:

```csharp
using System.Windows;
using System.Windows.Media;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

/// <summary>Panel settings and the gallery on the board (spec §9.4).</summary>
public partial class BoardWindow
{
    private void OnSettingsTool(object sender, PanelToolEventArgs e)
    {
        if (e.Tool is not (PanelTool.Settings or PanelTool.ChooseAnother) || PanelAt(e.OriginalSource) is not { } def) return;

        e.Handled = true;
        OpenPanelSettings(def);
    }

    /// <summary>The panel on the grid that holds <paramref name="source"/>, walking up from it; null when none does.</summary>
    private PanelDef? PanelAt(object? source)
    {
        for (var node = source as DependencyObject; node is not null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            foreach (var panel in _panels)
            {
                if (ReferenceEquals(panel.View, node)) return panel.Def;
            }
        }

        return null;
    }

    /// <summary>⋯ and Choose another: the panel's whole form.</summary>
    private void OpenPanelSettings(PanelDef def)
    {
        var live = _services.CurrentBoard();
        var form = new PanelSettingsWindow(def.Type, PanelGallery.Title(def.Type, live), live, def.Settings, adding: false) { Owner = this };
        if (form.ShowDialog() != true || form.Result is not { } settings) return;

        ChangeBoard(board => BoardEdits.SetSettings(board, def.Id, settings));
    }

    /// <summary>+ Add panel: the gallery, then the form asking only for what that panel needs (R14).</summary>
    private void AddPanelFromGallery()
    {
        var live = _services.CurrentBoard();
        var gallery = new PanelGalleryWindow(PanelGallery.Cards(live)) { Owner = this };
        if (gallery.ShowDialog() != true || gallery.Picked is not { } type) return;

        var form = new PanelSettingsWindow(type, PanelGallery.Title(type, live), live, null, adding: true) { Owner = this };
        if (form.ShowDialog() != true || form.Result is not { } settings) return;

        ChangeBoard(board => BoardEdits.AddPanel(board, type, settings));
    }

    /// <summary>Applies one edit to the board on screen and saves every board. An edit that changes nothing saves nothing.</summary>
    private void ChangeBoard(Func<BoardDef, BoardDef> edit)
    {
        var boards = _services.Boards;
        var board = ShownBoard(boards);
        var changed = edit(board);
        if (ReferenceEquals(changed, board)) return;

        SaveBoards(BoardEdits.Replace(boards, changed));
    }
}
```

In `src/UI/BoardWindow.xaml.cs`, in the constructor, replace:

```csharp
        InitializeComponent();
        ThemeService.Attach(this);
        _services = services;
```

with:

```csharp
        InitializeComponent();
        ThemeService.Attach(this);
        _services = services;

        // Panel tools raise one routed event; each concern handles its own tools (Tasks 6-8).
        PanelFrame.SetShowSettings(BoardPanels, true);
        BoardPanels.AddHandler(PanelFrame.ToolEvent, new EventHandler<PanelToolEventArgs>(OnSettingsTool));
```

In `OnEmptyStateClick`, replace:

```csharp
        if (_busy) return;

        if (_empty == BoardEmpty.NoStats)
```

with:

```csharp
        if (_busy) return;

        if (_empty == BoardEmpty.NoPanels)
        {
            AddPanelFromGallery();
            return;
        }

        if (_empty == BoardEmpty.NoStats)
```

- [ ] **Step 6: Run the gates**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: 0 warnings, 0 errors.

Run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: all pass, `ASavedBoardWithNoPanelsSaysSo` included. `ThemeFenceTests` stays green.

- [ ] **Step 7: Look at it once**

Build Release and start Ur Score with a clan recipe, a main clan and a clan your accounts are in:
1. Every panel's header shows ⋯. On the Clan standing panel, ⋯ → pick the other clan → **Save**: the panel now names that clan, and `boards.json` exists.
2. **+ Board** → **An empty board** → **Add panel**: the gallery shows ten cards titled in the recipe's words; with one clan only, Battle race and Promotion check are disabled with their reasons. **Add** on Clan standing → the form asks only for a clan, prefilled with ★ main → **Add panel**: the board shows it.
3. In Setup › Clans, remove the clan a panel shows: the panel says "This panel's clan was removed." with **Choose another**, which opens its form with the problem line saying what to pick.

- [ ] **Step 8: Commit**

```text
git add src/UI/Panels/PanelFrame.xaml src/UI/Panels/PanelFrame.xaml.cs src/UI/Boards/PanelGalleryWindow.xaml src/UI/Boards/PanelGalleryWindow.xaml.cs src/UI/Boards/PanelSettingsWindow.xaml src/UI/Boards/PanelSettingsWindow.xaml.cs src/UI/BoardWindow.PanelSettings.cs src/UI/BoardWindow.xaml.cs src/UI/BoardText.cs tests/BoardTextTests.cs
git commit -m "board: panel tools raise one event, with panel settings, choose another, and the gallery

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 7: Edit mode

Spec §9.2 ("Edit board: panels show drag handles, a size menu (small = 3, half = 6, wide = 12 columns; tall panels take two rows), ⋯ settings and ✕ remove; panels flow in order in a 12-column grid; dragging changes order; Done saves"), rulings R5–R10. **Edit board** copies the board on screen into a draft. Every tool changes the draft through `BoardEdits` and redraws; nothing is written until **Done** (or closing Ur Score, R8). A draft that ends up the same as the board saves nothing, so a look-around in edit mode never turns the following starter into a saved board.

**Files:**
- Create: `src/UI/BoardWindow.Editing.cs`
- Modify: `src/UI/BoardWindow.xaml` (edit buttons; the grid accepts drops)
- Modify: `src/UI/BoardWindow.xaml.cs` (constructor, `ShownBoard`, `BuildPanels`)
- Modify: `src/UI/BoardWindow.PanelSettings.cs` (`ChangeBoard` edits the draft while editing)
- Modify: `src/Board/BoardEdits.cs`, `tests/BoardEditsTests.cs` (`Changed`)

**Interfaces:**
- Consumes: Task 2 (`BoardEdits.MoveBy`/`MoveTo`/`Resize`/`RemovePanel`/`Replace`), Task 3 (`PanelGrid.DropIndexAt`), Task 6 (`PanelFrame.ToolEvent`, `ShowEditTools`, `CurrentSize`, `PanelTool`, `PanelAt`, `ChangeBoard`, `AddPanelFromGallery`), Task 5 seams.
- Produces:
  - `public static bool BoardEdits.Changed(BoardDef before, BoardDef after)` (true when anything drawn or the name differs)
  - in `BoardWindow`: `BoardDef? _draft`, `bool Editing`, `void HookEditing()`, `void FinishEditing()`, and the automation ids `EditBoardButton`, `AddPanelButton`, `DoneButton`

- [ ] **Step 1: Write the failing test**

Add to `tests/BoardEditsTests.cs`, inside the class:

```csharp
    [Fact]
    public void DoneHasSomethingToSaveOnlyWhenTheBoardIsDrawnDifferently()
    {
        var board = BoardOf("b", PanelType.Standing, PanelType.Race);

        Assert.False(BoardEdits.Changed(board, board with { Panels = [.. board.Panels] }));
        Assert.True(BoardEdits.Changed(board, BoardEdits.MoveBy(board, "p-b-2", -1)));
        Assert.True(BoardEdits.Changed(board, BoardEdits.Resize(board, "p-b-1", new PanelSize(6, Tall: true))));
        Assert.True(BoardEdits.Changed(board, board with { Name = "Other" }));

        var popped = BoardEdits.PopOut(board, "p-b-1", new PopOutRect(1, 2, 300, 200));
        Assert.False(BoardEdits.Changed(popped, BoardEdits.PopOut(popped, "p-b-1", new PopOutRect(50, 60, 400, 300))));
    }
```

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: FAIL with CS0117 `'BoardEdits' does not contain a definition for 'Changed'`.

- [ ] **Step 2: Implement `Changed`**

In `src/Board/BoardEdits.cs`, add after `NextName`:

```csharp
    /// <summary>Whether a draft has anything to save: a panel added, removed, moved, resized or re-set, or a new name. Where a pop-out sits doesn't count.</summary>
    public static bool Changed(BoardDef before, BoardDef after) =>
        BoardDefs.Key(before) != BoardDefs.Key(after) || before.Name != after.Name;
```

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror` then `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter FullyQualifiedName~BoardEditsTests`
Expected: all pass.

- [ ] **Step 3: The edit buttons, and a grid that takes drops**

In `src/UI/BoardWindow.xaml`, replace:

```xml
                    <Button x:Name="TestNowButton" Content="Test now" Margin="8,0,0,0" Click="OnTestNowClick"
                            AutomationProperties.Name="Read every source once now" />
```

with:

```xml
                    <Button x:Name="TestNowButton" Content="Test now" Margin="8,0,0,0" Click="OnTestNowClick"
                            AutomationProperties.Name="Read every source once now" />
                    <Button x:Name="EditBoardButton" Content="Edit board" Margin="8,0,0,0" Click="OnEditBoardClick"
                            AutomationProperties.Name="Edit board" />
                    <Button x:Name="AddPanelButton" Content="+ Add panel" Margin="8,0,0,0" Visibility="Collapsed" Click="OnAddPanelClick"
                            AutomationProperties.Name="Add panel" />
                    <Button x:Name="DoneButton" Content="Done" Style="{StaticResource PrimaryButton}" Margin="8,0,0,0" Visibility="Collapsed"
                            Click="OnDoneClick" AutomationProperties.Name="Done" />
```

Replace:

```xml
                <ui:PanelGrid x:Name="BoardPanels" AutomationProperties.Name="Board" />
```

with:

```xml
                <!-- Transparent so a drop between panels still lands on the grid. -->
                <ui:PanelGrid x:Name="BoardPanels" Background="Transparent" AllowDrop="True" DragOver="OnBoardDragOver" Drop="OnBoardDrop"
                              AutomationProperties.Name="Board" />
```

- [ ] **Step 4: Edit mode on the board**

Create `src/UI/BoardWindow.Editing.cs`:

```csharp
using System.Windows;
using System.Windows.Threading;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

/// <summary>Edit mode (spec §9.2, R7–R10): a draft of the board on screen, changed by the panel tools, saved by Done.</summary>
public partial class BoardWindow
{
    private const string PanelDragFormat = "Labs626.UrScore.PanelId";

    /// <summary>The board being edited, or null outside edit mode.</summary>
    private BoardDef? _draft;

    private bool Editing => _draft is not null;

    private void HookEditing()
    {
        BoardPanels.AddHandler(PanelFrame.ToolEvent, new EventHandler<PanelToolEventArgs>(OnEditTool));

        // R8: closing Ur Score while editing keeps the arrangement.
        Closing += (_, _) => FinishEditing();
    }

    private void OnEditBoardClick(object sender, RoutedEventArgs e)
    {
        if (Editing) return;

        _draft = ShownBoard(_services.Boards);
        ShowEditMode();
        Render();
    }

    private void OnAddPanelClick(object sender, RoutedEventArgs e) => AddPanelFromGallery();

    private void OnDoneClick(object sender, RoutedEventArgs e) => FinishEditing();

    /// <summary>Done: the draft replaces its board and is saved. A save that fails stays in edit mode, so nothing is lost.</summary>
    private void FinishEditing()
    {
        if (_draft is not { } draft) return;

        var boards = _services.Boards;
        var original = boards.FirstOrDefault(b => b.Id == draft.Id);
        _draft = null;

        if (original is not null && BoardEdits.Changed(original, draft) && !SaveBoards(BoardEdits.Replace(boards, draft)))
        {
            _draft = draft;
        }

        ShowEditMode();
        Render();
    }

    private void ShowEditMode()
    {
        var editing = Editing;
        PanelFrame.SetShowEditTools(BoardPanels, editing);

        EditBoardButton.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        AddPanelButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        DoneButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;

        // R8: one draft at a time.
        BoardTabs.IsEnabled = !editing;
        AddBoardButton.IsEnabled = !editing;
    }

    private void OnEditTool(object sender, PanelToolEventArgs e)
    {
        if (!Editing || PanelAt(e.OriginalSource) is not { } def) return;

        switch (e.Tool)
        {
            case PanelTool.MoveEarlier:
                e.Handled = true;
                ChangeBoard(board => BoardEdits.MoveBy(board, def.Id, -1));
                break;
            case PanelTool.MoveLater:
                e.Handled = true;
                ChangeBoard(board => BoardEdits.MoveBy(board, def.Id, 1));
                break;
            case PanelTool.Resize when e.Size is { } size:
                e.Handled = true;
                ChangeBoard(board => BoardEdits.Resize(board, def.Id, size));
                break;
            case PanelTool.Remove:
                e.Handled = true;
                ChangeBoard(board => BoardEdits.RemovePanel(board, def.Id));
                break;
            case PanelTool.DragStart:
                e.Handled = true;
                StartDrag(def);
                break;
        }
    }

    private void StartDrag(PanelDef def)
    {
        var view = _panels.FirstOrDefault(p => p.Def.Id == def.Id).View;
        if (view is null) return;

        DragDrop.DoDragDrop(view, new DataObject(PanelDragFormat, def.Id), DragDropEffects.Move);
    }

    private void OnBoardDragOver(object sender, DragEventArgs e)
    {
        e.Effects = Editing && e.Data.GetDataPresent(PanelDragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnBoardDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!Editing || e.Data.GetData(PanelDragFormat) is not string panelId) return;

        var index = BoardPanels.DropIndexAt(e.GetPosition(BoardPanels));

        // After the drag returns: redrawing rebuilds the grid, and the dragged panel is still the drag's source until then.
        Dispatcher.BeginInvoke(() => ChangeBoard(board => BoardEdits.MoveTo(board, panelId, index)), DispatcherPriority.Background);
    }
}
```

- [ ] **Step 5: Hook edit mode into the window**

In `src/UI/BoardWindow.xaml.cs`, in the constructor, replace:

```csharp
        BoardPanels.AddHandler(PanelFrame.ToolEvent, new EventHandler<PanelToolEventArgs>(OnSettingsTool));
```

with:

```csharp
        BoardPanels.AddHandler(PanelFrame.ToolEvent, new EventHandler<PanelToolEventArgs>(OnSettingsTool));
        HookEditing();
```

Replace the `ShownBoard` method with:

```csharp
    /// <summary>The board on screen: the draft while editing, else the selected tab's, else the first (R12).</summary>
    private BoardDef ShownBoard(IReadOnlyList<BoardDef> boards) =>
        _draft ?? boards.FirstOrDefault(b => b.Id == _boardId) ?? boards[0];
```

In `BuildPanels`, replace:

```csharp
            PanelGrid.SetTall(view, def.Size.Tall);
```

with:

```csharp
            PanelGrid.SetTall(view, def.Size.Tall);
            PanelFrame.SetCurrentSize(view, def.Size);
```

In `src/UI/BoardWindow.PanelSettings.cs`, replace the whole `ChangeBoard` method with:

```csharp
    /// <summary>
    /// Applies one edit to the board on screen: into the draft while editing (R8, R10), else saved at once. An edit
    /// that changes nothing does nothing.
    /// </summary>
    private void ChangeBoard(Func<BoardDef, BoardDef> edit)
    {
        if (_draft is { } draft)
        {
            var edited = edit(draft);
            if (ReferenceEquals(edited, draft)) return;

            _draft = edited;
            Render();
            return;
        }

        var boards = _services.Boards;
        var board = ShownBoard(boards);
        var changed = edit(board);
        if (ReferenceEquals(changed, board)) return;

        SaveBoards(BoardEdits.Replace(boards, changed));
    }
```

- [ ] **Step 6: Run the gates**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: 0 warnings, 0 errors.

Run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: all pass.

- [ ] **Step 7: Look at it once**

Build Release and start Ur Score on a board with several panels:
1. **Edit board**: every panel shows the handle, ←, →, the size box, Tall and ✕ above its title; ⋯ stays; **+ Add panel** and **Done** replace **Edit board**; the tabs and **+ Board** are disabled.
2. → on the first panel moves it one place later. Drag a panel by its handle onto the left half of another: it lands before that one.
3. Size box → **Wide**: the panel takes the whole row. Tick **Tall** on a half panel: it spans two rows and the next half panel sits beside its lower half.
4. ✕ removes a panel; **+ Add panel** adds one to the end.
5. **Done**: `boards.json` holds the new order and sizes. Restart: the board is the same.
6. **Edit board** then **Done** with no change on a fresh data folder: no `boards.json` is written.

- [ ] **Step 8: Commit**

```text
git add src/UI/BoardWindow.Editing.cs src/UI/BoardWindow.xaml src/UI/BoardWindow.xaml.cs src/UI/BoardWindow.PanelSettings.cs src/Board/BoardEdits.cs tests/BoardEditsTests.cs
git commit -m "board: edit mode moves, drags, sizes and removes panels in a draft that Done saves

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 8: Pop-out panels

Spec §9.3 ("⧉ opens the panel in its own small window, always on top, borderless title strip with the panel's name, resizable, and position remembered. It updates live. Closing it returns the panel to its board. Several can be out at once. Pop-outs reopen where they were when Ur Score starts"), §9.2 (`popout?: {x, y, w, h}`), rulings R18–R20. A panel's `popout` in `boards.json` is the truth: the board keeps one window open for every panel that has one, on every board, and closes any whose panel lost it. Where the window sits is saved once moving settles. Placement math is pure in `PopOutPlacement`.

**Files:**
- Create: `src/Board/PopOutPlacement.cs`, `tests/PopOutPlacementTests.cs`
- Create: `src/UI/Boards/PanelPopOutWindow.xaml`, `src/UI/Boards/PanelPopOutWindow.xaml.cs`
- Create: `src/UI/BoardWindow.PopOuts.cs`
- Modify: `src/UI/BoardWindow.xaml.cs` (constructor, `RenderBoard`, `CreatePanelView`, `ShownPanels`)

**Interfaces:**
- Consumes: Task 1 (`PopOutRect`), Task 2 (`BoardEdits.PopOut`/`Return`/`Find`/`Replace`/`AutomationIds`), Task 4 (`PanelGallery.Title`), Task 6 (`PanelFrame.ShowPopOut`, `PanelTool.PopOut`, `PanelAt`, `ChangeBoard`), Task 7 (`_draft`), Task 5 seams.
- Produces: `PopOutPlacement` exactly as in the contract; `PanelPopOutWindow(string panelId, FrameworkElement view, PopOutRect rect)` with `PanelId`, `View`, `Rect`, `event Action<PanelPopOutWindow>? Moved`, `ShowTitle(string)`; in `BoardWindow`: `HookPopOuts()`, `SyncPopOuts()`, `RenderPopOuts(LiveBoard)`, `ReturnPanel(string)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/PopOutPlacementTests.cs`:

```csharp
using Labs626.UrScore.Board;

namespace UrScore.Tests;

public class PopOutPlacementTests
{
    private static readonly PopOutRect Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void AWindowOnScreenStaysWhereItWas() =>
        Assert.Equal(new PopOutRect(100, 100, 360, 300), PopOutPlacement.Clamp(new PopOutRect(100, 100, 360, 300), Screen));

    [Fact]
    public void AWindowOffTheScreenIsPulledBackOn()
    {
        Assert.Equal(new PopOutRect(1560, 100, 360, 300), PopOutPlacement.Clamp(new PopOutRect(3000, 100, 360, 300), Screen));
        Assert.Equal(new PopOutRect(0, 780, 360, 300), PopOutPlacement.Clamp(new PopOutRect(-900, 2000, 360, 300), Screen));
    }

    [Fact]
    public void AMonitorLeftOfTheMainOneCounts() =>
        Assert.Equal(new PopOutRect(-1800, 50, 400, 300), PopOutPlacement.Clamp(new PopOutRect(-1800, 50, 400, 300), new PopOutRect(-1920, 0, 3840, 1080)));

    [Fact]
    public void SizeStaysBetweenTheSmallestUsefulAndTheScreen()
    {
        Assert.Equal(new PopOutRect(10, 10, PopOutPlacement.MinWidth, PopOutPlacement.MinHeight), PopOutPlacement.Clamp(new PopOutRect(10, 10, 100, 50), Screen));
        Assert.Equal(Screen, PopOutPlacement.Clamp(new PopOutRect(0, 0, 5000, 3000), Screen));
    }

    [Fact]
    public void ANonsenseRectOpensAtTheScreensCornerAtTheDefaultSize() =>
        Assert.Equal(
            new PopOutRect(0, 0, PopOutPlacement.DefaultWidth, PopOutPlacement.DefaultHeight),
            PopOutPlacement.Clamp(new PopOutRect(double.NaN, 10, double.PositiveInfinity, 300), Screen));

    [Fact]
    public void NewPopOutsCascadeFromTheBoardsTopRight()
    {
        var board = new PopOutRect(100, 100, 1280, 900);

        Assert.Equal(new PopOutRect(996, 172, 360, 300), PopOutPlacement.Default(board, 0, Screen));
        Assert.Equal(new PopOutRect(968, 200, 360, 300), PopOutPlacement.Default(board, 1, Screen));
        Assert.Equal(new PopOutRect(1560, 780, 360, 300), PopOutPlacement.Default(new PopOutRect(1500, 800, 1280, 900), 0, Screen));
    }
}
```

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: FAIL with CS0103 `The name 'PopOutPlacement' does not exist in the current context`.

- [ ] **Step 2: Implement the placement**

Create `src/Board/PopOutPlacement.cs`:

```csharp
namespace Labs626.UrScore.Board;

/// <summary>Where a pop-out window opens and stays (spec §9.3, R18). The screen is the whole virtual desktop.</summary>
public static class PopOutPlacement
{
    public const double MinWidth = 260;
    public const double MinHeight = 180;
    public const double DefaultWidth = 360;
    public const double DefaultHeight = 300;

    /// <summary>How far each further new pop-out steps from the last.</summary>
    public const double Cascade = 28;

    /// <summary>At least the smallest useful size, no bigger than the screen, and wholly on it.</summary>
    public static PopOutRect Clamp(PopOutRect rect, PopOutRect screen)
    {
        if (!double.IsFinite(rect.X) || !double.IsFinite(rect.Y) || !double.IsFinite(rect.W) || !double.IsFinite(rect.H))
        {
            rect = new PopOutRect(screen.X, screen.Y, DefaultWidth, DefaultHeight);
        }

        var width = Math.Min(Math.Max(rect.W, MinWidth), screen.W);
        var height = Math.Min(Math.Max(rect.H, MinHeight), screen.H);
        var x = Math.Clamp(rect.X, screen.X, screen.X + screen.W - width);
        var y = Math.Clamp(rect.Y, screen.Y, screen.Y + screen.H - height);
        return new PopOutRect(x, y, width, height);
    }

    /// <summary>A panel's first pop-out: inside the board's top-right corner, each further one stepped down and left.</summary>
    public static PopOutRect Default(PopOutRect board, int openCount, PopOutRect screen) =>
        Clamp(new PopOutRect(
            board.X + board.W - DefaultWidth - 24 - openCount * Cascade,
            board.Y + 72 + openCount * Cascade,
            DefaultWidth,
            DefaultHeight), screen);
}
```

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror` then `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter FullyQualifiedName~PopOutPlacementTests`
Expected: all pass.

- [ ] **Step 3: The pop-out window**

Create `src/UI/Boards/PanelPopOutWindow.xaml`:

```xml
<Window x:Class="Labs626.UrScore.UI.PanelPopOutWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Panel" Width="360" Height="300" MinWidth="260" MinHeight="180"
        WindowStyle="None" ResizeMode="CanResize" Topmost="True" ShowInTaskbar="False" ShowActivated="False"
        AutomationProperties.AutomationId="PopOutWindow"
        Background="{DynamicResource BgBrush}" Foreground="{DynamicResource WhiteBrush}"
        BorderBrush="{DynamicResource EdgeBrush}" BorderThickness="1"
        FontFamily="{StaticResource BodyFont}">
    <!-- Spec §9.3: no system title bar; the strip is the caption, so it drags the window, and the edges resize it. -->
    <WindowChrome.WindowChrome>
        <WindowChrome CaptionHeight="30" ResizeBorderThickness="6" GlassFrameThickness="0" CornerRadius="0" UseAeroCaptionButtons="False" />
    </WindowChrome.WindowChrome>
    <DockPanel>
        <Border DockPanel.Dock="Top" Height="30" Background="{DynamicResource RowBgBrush}" Padding="10,0,2,0">
            <DockPanel>
                <Button x:Name="ReturnPanelButton" DockPanel.Dock="Right" Content="✕" Style="{StaticResource FlatButton}" Padding="8,2"
                        WindowChrome.IsHitTestVisibleInChrome="True" Click="OnReturnClick"
                        ToolTip="Return to the board" AutomationProperties.Name="Return to the board" />
                <TextBlock x:Name="PopOutTitle" VerticalAlignment="Center" FontWeight="SemiBold" TextTrimming="CharacterEllipsis" />
            </DockPanel>
        </Border>
        <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled" Padding="6">
            <ContentControl x:Name="PanelHost" Focusable="False" />
        </ScrollViewer>
    </DockPanel>
</Window>
```

Create `src/UI/Boards/PanelPopOutWindow.xaml.cs`:

```csharp
using System.Windows;
using Labs626.UrScore.Board;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

/// <summary>
/// One panel in its own always-on-top window (spec §9.3). It shows a panel the board renders into it and
/// decides nothing: the board opens it, draws it, saves where it sits, and returns the panel when it closes.
/// </summary>
public partial class PanelPopOutWindow : Window
{
    public PanelPopOutWindow(string panelId, FrameworkElement view, PopOutRect rect)
    {
        InitializeComponent();
        ThemeService.Attach(this);

        PanelId = panelId;
        View = view;
        PanelHost.Content = view;

        Left = rect.X;
        Top = rect.Y;
        Width = rect.W;
        Height = rect.H;

        LocationChanged += (_, _) => Moved?.Invoke(this);
        SizeChanged += (_, _) => Moved?.Invoke(this);
    }

    public string PanelId { get; }

    /// <summary>The panel control inside, which the board renders like any other.</summary>
    public FrameworkElement View { get; }

    /// <summary>Where the window is now, in device-independent pixels.</summary>
    public PopOutRect Rect => new(Left, Top, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height);

    /// <summary>Raised as the window moves or resizes.</summary>
    public event Action<PanelPopOutWindow>? Moved;

    public void ShowTitle(string title)
    {
        Title = title;
        PopOutTitle.Text = title;
    }

    private void OnReturnClick(object sender, RoutedEventArgs e) => Close();
}
```

- [ ] **Step 4: Pop-outs on the board**

Create `src/UI/BoardWindow.PopOuts.cs`:

```csharp
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

/// <summary>Pop-out panels (spec §9.3, R18–R20). A panel's saved <c>popout</c> is the truth; windows follow it.</summary>
public partial class BoardWindow
{
    private readonly Dictionary<string, PanelPopOutWindow> _popOuts = new(StringComparer.Ordinal);

    /// <summary>Where each panel's window last sat this session, so the next ⧉ opens there (R18).</summary>
    private readonly Dictionary<string, PopOutRect> _lastPopOut = new(StringComparer.Ordinal);

    private readonly DispatcherTimer _popOutSave = new() { Interval = TimeSpan.FromMilliseconds(700) };

    /// <summary>Set as Ur Score closes, so the windows it closes keep their <c>popout</c> and reopen next start.</summary>
    private bool _closingApp;

    private void HookPopOuts()
    {
        PanelFrame.SetShowPopOut(BoardPanels, true);
        BoardPanels.AddHandler(PanelFrame.ToolEvent, new EventHandler<PanelToolEventArgs>(OnPopOutTool));

        _popOutSave.Tick += (_, _) =>
        {
            _popOutSave.Stop();
            SavePopOutPositions();
        };

        Closing += (_, _) =>
        {
            _closingApp = true;
            _popOutSave.Stop();
            SavePopOutPositions();
        };
    }

    private void OnPopOutTool(object sender, PanelToolEventArgs e)
    {
        if (e.Tool != PanelTool.PopOut || Editing || PanelAt(e.OriginalSource) is not { } def) return;

        e.Handled = true;
        var screen = VirtualScreen();
        var rect = _lastPopOut.TryGetValue(def.Id, out var last)
            ? PopOutPlacement.Clamp(last, screen)
            : PopOutPlacement.Default(new PopOutRect(Left, Top, ActualWidth, ActualHeight), _popOuts.Count, screen);

        ChangeBoard(board => BoardEdits.PopOut(board, def.Id, rect));
        Render();
    }

    /// <summary>
    /// One window for every panel that has a <c>popout</c> on any board (R20), counting the draft while editing;
    /// a window whose panel lost it, or was removed, closes without returning anything.
    /// </summary>
    private void SyncPopOuts()
    {
        var boards = _draft is { } draft ? BoardEdits.Replace(_services.Boards, draft) : _services.Boards;
        var wanted = new Dictionary<string, PanelDef>(StringComparer.Ordinal);
        foreach (var panel in boards.SelectMany(b => b.Panels))
        {
            if (panel.PopOut is not null) wanted[panel.Id] = panel;
        }

        foreach (var id in _popOuts.Keys.Where(id => !wanted.ContainsKey(id)).ToList())
        {
            var window = _popOuts[id];
            _popOuts.Remove(id);
            window.Close();
        }

        var screen = VirtualScreen();
        foreach (var (id, def) in wanted)
        {
            if (_popOuts.ContainsKey(id)) continue;

            var view = PanelViews.Create(def.Type);
            AutomationProperties.SetAutomationId(view, AutomationIdIn(boards, id));

            var window = new PanelPopOutWindow(id, view, PopOutPlacement.Clamp(def.PopOut!, screen));
            window.ShowTitle(PanelGallery.Title(def.Type, _services.CurrentBoard()));
            window.Moved += OnPopOutMoved;
            window.Closed += OnPopOutClosed;
            _popOuts[id] = window;
            window.Show();
        }
    }

    /// <summary>Every open pop-out updates live, from the same builders as the board.</summary>
    private void RenderPopOuts(LiveBoard live)
    {
        foreach (var window in _popOuts.Values)
        {
            if (BoardEdits.Find(_services.Boards, window.PanelId) is not { } found) continue;

            RenderPanel(found.Panel, window.View, live);
            window.ShowTitle(PanelGallery.Title(found.Panel.Type, live));
        }
    }

    private void OnPopOutMoved(PanelPopOutWindow window)
    {
        _popOutSave.Stop();
        _popOutSave.Start();
    }

    private void OnPopOutClosed(object? sender, EventArgs e)
    {
        if (sender is not PanelPopOutWindow window) return;

        window.Moved -= OnPopOutMoved;
        _lastPopOut[window.PanelId] = window.Rect;

        var tracked = _popOuts.TryGetValue(window.PanelId, out var current) && ReferenceEquals(current, window);
        if (!tracked) return;

        _popOuts.Remove(window.PanelId);
        if (!_closingApp) ReturnPanel(window.PanelId);
    }

    /// <summary>The panel goes back to its board, whichever board that is (spec §9.3: "closing it returns the panel").</summary>
    private void ReturnPanel(string panelId)
    {
        if (_draft is { } draft && draft.Panels.Any(p => p.Id == panelId)) _draft = BoardEdits.Return(draft, panelId);

        var boards = _services.Boards;
        if (BoardEdits.Find(boards, panelId) is { } found && found.Panel.PopOut is not null)
        {
            SaveBoards(BoardEdits.Replace(boards, BoardEdits.Return(found.Board, panelId)));
        }

        Render();
    }

    /// <summary>Every open pop-out's place, saved once moving settles (and as Ur Score closes).</summary>
    private void SavePopOutPositions()
    {
        var boards = _services.Boards;
        var changed = boards;

        foreach (var (id, window) in _popOuts)
        {
            if (BoardEdits.Find(changed, id) is not { } found || found.Panel.PopOut is null || found.Panel.PopOut == window.Rect) continue;

            changed = BoardEdits.Replace(changed, BoardEdits.PopOut(found.Board, id, window.Rect));
            if (_draft is { } draft && draft.Panels.Any(p => p.Id == id)) _draft = BoardEdits.PopOut(draft, id, window.Rect);
        }

        if (!ReferenceEquals(changed, boards)) SaveBoards(changed);
    }

    /// <summary>A popped-out panel's slot on its board (R19): a short card with Bring back.</summary>
    private FrameworkElement PoppedOutPlaceholder(PanelDef def, string automationId)
    {
        var title = PanelGallery.Title(def.Type, _services.CurrentBoard());

        var line = new TextBlock { Text = $"{title} is popped out.", VerticalAlignment = VerticalAlignment.Center };
        line.SetResourceReference(StyleProperty, "Muted");

        var bringBack = new Button { Content = "Bring back", Margin = new Thickness(12, 0, 0, 0) };
        bringBack.SetResourceReference(StyleProperty, "FlatButton");
        AutomationProperties.SetName(bringBack, $"Bring back {title}");
        bringBack.Click += (_, _) => ReturnPanel(def.Id);

        var row = new DockPanel();
        DockPanel.SetDock(bringBack, Dock.Right);
        row.Children.Add(bringBack);
        row.Children.Add(line);

        var card = new Border { Child = row };
        card.SetResourceReference(StyleProperty, "PanelCard");

        // A Border has no automation peer; the UserControl around it is what UI Automation finds by id.
        var slot = new UserControl { Content = card };
        AutomationProperties.SetAutomationId(slot, automationId + "PoppedOut");
        return slot;
    }

    /// <summary>A pop-out's panel keeps its board automation id, "StandingPanel1", inside its own window.</summary>
    private static string AutomationIdIn(IReadOnlyList<BoardDef> boards, string panelId)
    {
        foreach (var board in boards)
        {
            for (var i = 0; i < board.Panels.Count; i++)
            {
                if (board.Panels[i].Id == panelId) return BoardEdits.AutomationIds(board)[i];
            }
        }

        return "PopOutPanel";
    }

    private static PopOutRect VirtualScreen() => new(
        SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
        SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
}
```

- [ ] **Step 5: Hook pop-outs into the window**

In `src/UI/BoardWindow.xaml.cs`, in the constructor, replace:

```csharp
        HookEditing();
```

with:

```csharp
        HookEditing();
        HookPopOuts();
```

In `RenderBoard`, replace:

```csharp
        if (key != _renderedKey)
        {
            _renderedKey = key;
            BuildPanels(board);
        }
```

with:

```csharp
        if (key != _renderedKey)
        {
            _renderedKey = key;
            BuildPanels(board);
        }

        // Only once shown: the constructor's first draw must not open windows ahead of the board.
        if (IsLoaded) SyncPopOuts();
```

and replace:

```csharp
            foreach (var (def, view, _) in _panels) RenderPanel(def, view, live);
```

with:

```csharp
            foreach (var (def, view, _) in _panels) RenderPanel(def, view, live);
            RenderPopOuts(live);
```

Replace the `CreatePanelView` method with:

```csharp
    /// <summary>The control for one panel on the board, named by its automation id; a popped-out panel's slot holds a placeholder (R19).</summary>
    private FrameworkElement CreatePanelView(PanelDef def, string automationId)
    {
        if (def.PopOut is not null) return PoppedOutPlaceholder(def, automationId);

        var view = PanelViews.Create(def.Type);
        AutomationProperties.SetAutomationId(view, automationId);
        return view;
    }
```

Replace the `ShownPanels` method with:

```csharp
    /// <summary>Every panel showing anywhere, on the board or popped out, for looking up Live leaderboard names.</summary>
    private IEnumerable<PanelDef> ShownPanels()
    {
        foreach (var (def, _, _) in _panels)
        {
            if (def.PopOut is null) yield return def;
        }

        foreach (var id in _popOuts.Keys.ToList())
        {
            if (BoardEdits.Find(_services.Boards, id) is { } found) yield return found.Panel;
        }
    }
```

- [ ] **Step 6: Run the gates**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: 0 warnings, 0 errors.

Run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: all pass. `ThemeFenceTests` stays green: the placeholder takes its styles by resource reference and paints no colour in code.

- [ ] **Step 7: Look at it once**

Build Release and start Ur Score on a board that reads:
1. ⧉ on Clan standing: a small window titled "Clan standing" opens inside the board's top-right, on top of other windows; the board's slot says "Clan standing is popped out." with **Bring back**.
2. Press **Test now**: the pop-out's numbers change with the board's.
3. Pop out a second panel: it opens stepped down and left of the first. Drag both somewhere else and resize one; within a second `boards.json` holds their new `popout` rectangles.
4. Minimize the board: both pop-outs stay up.
5. Quit Ur Score from the board and start it again: both pop-outs reopen where they were.
6. ✕ on one: it closes and its panel is back on the board; `boards.json` has no `popout` for it. ⧉ again: it opens where it was.
7. **Bring back** on the other's slot closes its window too.

- [ ] **Step 8: Commit**

```text
git add src/Board/PopOutPlacement.cs tests/PopOutPlacementTests.cs src/UI/Boards/PanelPopOutWindow.xaml src/UI/Boards/PanelPopOutWindow.xaml.cs src/UI/BoardWindow.PopOuts.cs src/UI/BoardWindow.xaml.cs
git commit -m "board: panels pop out into always-on-top windows that update live and reopen where they were

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 9: Smoke walks for stage 2, and the live walk

Spec §11 ("walks for … (stage 2) board editing and pop-outs"), §12 step 11 (a live walk of stage 2), §13 Boards and Live. Two new walks and a privacy check join `tools/smoke/`, on the same helpers and the same rules as stage 1's: ASCII only, paths from `$PSScriptRoot`, and a clean data folder moved aside and restored in a `finally`. They use the automation ids in this plan's table.

Two details the walks depend on:
- **Tab menu:** a WPF context menu can't be opened through UI Automation, so the walk focuses the selected tab and sends Shift+F10. The menu is a top-level window of the Ur Score process, so its items are found through `Get-UrWindows`.
- **Quitting with pop-outs open:** pop-outs have no owner, so `Process.MainWindowHandle` may name one, and `Stop-UrScore`'s `CloseMainWindow` could close a pop-out (returning its panel) instead of the board. The pop-out walk quits by closing the board window itself.

**Files:**
- Create: `tools/smoke/uia-board.ps1`, `tools/smoke/walk-board-editing.ps1`, `tools/smoke/walk-pop-outs.ps1`, `tools/smoke/check-boards-privacy.ps1`
- Modify: `tools/smoke/README.md`

**Interfaces:**
- Consumes: `uia.ps1` and `uia-import.ps1` (stage 1: `Get-UrWindows`, `Wait-UrWindow`, `Get-BoardWindow`, `Find-ByAutomationId`, `Find-All`, `Get-Button`, `Invoke-Element`, `Set-ElementValue`, `Set-Tick`, `Line`, `Wait-Line`, `Wait-Until`, `Get-AllTexts`, `Start-UrScore`, `Stop-UrScore`, `Open-SetupPage`, `Close-UrWindow`, `Select-SearchName`, `Move-UrDataAside`, `Restore-UrData`, `Check`, `Show-Results`, `Close-MessageBox`, `Complete-ClanImport`); the automation ids of Tasks 5–8.
- Produces: PowerShell functions `Find-InUrWindows`, `Get-PanelIds`, `Get-TabNames`, `Get-SelectedTabName`, `Invoke-TabMenu`, `Enter-EditMode`, `Complete-EditMode`, `Invoke-PanelTool`, `Set-PanelSize`, `Select-ComboItem`, `Add-PanelFromGallery`, `Read-Boards`, `Get-PopOutWindows`, `Initialize-ClanBoard`, `Stop-UrScoreFromBoard`.

- [ ] **Step 1: The board helpers**

Create `tools/smoke/uia-board.ps1`:

```powershell
# Board helpers for the stage 2 walks: tabs and their menu, edit mode, panel tools, the gallery, the panel
# form, pop-outs and boards.json. Dot-source this file; it only defines things.
# ASCII only on purpose: Windows PowerShell 5.1 reads a BOM-less file as ANSI.
. (Join-Path $PSScriptRoot 'uia-import.ps1')

Add-Type -AssemblyName System.Windows.Forms

$script:IsListItem = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::ListItem)

# An element by automation id in any Ur Score window, the windows themselves included (menus and pop-outs are top-level).
function Find-InUrWindows([string]$id) {
    foreach ($w in Get-UrWindows) {
        if ($w.Current.AutomationId -eq $id) { return $w }
        $e = Find-ByAutomationId $w $id
        if ($e) { return $e }
    }
    return $null
}

# Every panel in a window, in board order: "StandingPanel1", "RacePanel1", "AccountCardPanel1PoppedOut".
function Get-PanelIds($root) {
    @($root.FindAll($TS::Descendants, $Cond::TrueCondition) |
        ForEach-Object { $_.Current.AutomationId } |
        Where-Object { $_ -match '^[A-Za-z]+Panel[0-9]+(PoppedOut)?$' })
}

function Get-TabItems($board) {
    $tabs = Find-ByAutomationId $board 'BoardTabs'
    if (-not $tabs) { return @() }
    @($tabs.FindAll($TS::Children, $IsListItem))
}

function Get-TabNames($board) { @(Get-TabItems $board | ForEach-Object { $_.Current.Name }) }

function Get-SelectedTabName($board) {
    $selected = Get-TabItems $board |
        Where-Object { $_.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } |
        Select-Object -First 1
    if ($selected) { $selected.Current.Name } else { '(none)' }
}

# Opens the selected tab's right-click menu with Shift+F10 and invokes one item by automation id.
# Returns $false (and closes the menu) when that item is disabled.
function Invoke-TabMenu($board, [string]$itemId) {
    $selected = Get-TabItems $board |
        Where-Object { $_.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } |
        Select-Object -First 1
    if (-not $selected) { throw 'no tab is selected' }
    $selected.SetFocus()
    Start-Sleep -Milliseconds 400
    [System.Windows.Forms.SendKeys]::SendWait('+{F10}')

    $item = $null
    $deadline = (Get-Date).AddSeconds(6)
    do {
        $item = Find-InUrWindows $itemId
        if ($item) { break }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    if (-not $item) { throw "the tab menu has no $itemId" }

    if (-not $item.Current.IsEnabled) {
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Start-Sleep -Milliseconds 300
        return $false
    }
    Invoke-Element $item
    Start-Sleep -Milliseconds 700
    return $true
}

function Enter-EditMode($board) {
    Invoke-Element (Find-ByAutomationId $board 'EditBoardButton')
    Start-Sleep -Milliseconds 800
}

function Complete-EditMode($board) {
    Invoke-Element (Find-ByAutomationId $board 'DoneButton')
    Start-Sleep -Milliseconds 1000
}

# Invokes a tool button inside one panel, found by the panel's automation id.
function Invoke-PanelTool($root, [string]$panelId, [string]$toolId) {
    $panel = Find-ByAutomationId $root $panelId
    if (-not $panel) { throw "no panel $panelId" }
    $tool = Find-ByAutomationId $panel $toolId
    if (-not $tool) { throw "$panelId shows no $toolId" }
    Invoke-Element $tool
    Start-Sleep -Milliseconds 800
}

# Picks the first item whose name matches a wildcard pattern in a combo box.
function Select-ComboItem($box, [string]$like) {
    if (-not $box) { throw 'combo box not found' }
    $box.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 500
    $item = Find-All $box $CT::ListItem | Where-Object { $_.Current.Name -like $like } | Select-Object -First 1
    if (-not $item) { throw "no item like '$like'" }
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 300
    # Picking can rebuild the panel, which takes the box with it.
    try { $box.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse() } catch { }
    Start-Sleep -Milliseconds 800
}

# Small, Half or Wide in one panel's size box.
function Set-PanelSize($board, [string]$panelId, [string]$size) {
    Select-ComboItem (Find-ByAutomationId (Find-ByAutomationId $board $panelId) 'SizeBox') $size
}

# With the gallery about to open: picks a card by title, then saves its form with the defaults it offers.
function Add-PanelFromGallery([string]$title) {
    $gallery = Wait-UrWindow '^Add a panel$' 15
    if (-not $gallery) { throw 'the gallery did not open' }
    $add = Get-Button $gallery "Add $title"
    if (-not $add) { throw "the gallery has no card '$title'" }
    Invoke-Element $add
    $form = Wait-UrWindow "^Add $title$" 15
    if (-not $form) { throw "no form for '$title'" }
    Invoke-Element (Find-ByAutomationId $form 'SaveSettingsButton')
    Start-Sleep -Milliseconds 1000
}

# boards.json, one board per item (see Read-Sources for why this unrolls with foreach).
function Read-Boards {
    $file = Join-Path $UrData 'boards.json'
    if (-not (Test-Path $file)) { return @() }
    $text = Get-Content $file -Raw
    if (-not $text.Trim()) { return @() }
    foreach ($b in ($text | ConvertFrom-Json)) { $b }
}

function Get-PopOutWindows { @(Get-UrWindows | Where-Object { $_.Current.AutomationId -eq 'PopOutWindow' }) }

# Quits by closing the board window, so a pop-out is never mistaken for the main window.
function Stop-UrScoreFromBoard([int]$seconds = 20) {
    $board = Get-BoardWindow
    if ($board) { Close-UrWindow $board }
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-UrProcessId) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 400 }
    Stop-UrScore
}

# A clean start with the clan fixture imported (Points shown and sent), the main clan, and optionally a clan
# your accounts are in. Returns the board once its first Clan standing panel has drawn.
function Initialize-ClanBoard([string]$Main, [string]$Alt) {
    Start-UrScore | Out-Null
    Invoke-Element (Find-ByAutomationId (Get-BoardWindow) 'SetupButton')
    Wait-UrWindow '^Setup$' 15 | Out-Null
    Complete-ClanImport (Join-Path $UrFixtures 'petsim99-clan-battle.recipe.json') @('Points') @('Points') | Out-Null
    $setup = Wait-UrWindow '^Setup$' 30
    Select-SearchName $setup 'Your main clan' $Main
    Wait-Line $setup 'MainFoundLine' '^(Found |None of your accounts|Read |Added )' 120 | Out-Null
    if ($Alt) {
        Invoke-Element (Find-ByAutomationId $setup 'AddMineButton')
        Select-SearchName $setup 'Add a clan your accounts are in' $Alt
        Wait-Line $setup 'MineFoundLine' '^(Found |None of your accounts|Read |Added )' 120 | Out-Null
    }
    Close-UrWindow (Get-SetupWindow)
    Wait-Until {
        $p = Find-ByAutomationId (Get-BoardWindow) 'StandingPanel1'
        $p -and (Line $p 'PanelTitle') -eq 'Clan standing'
    } 30 | Out-Null
    return Get-BoardWindow
}
```

- [ ] **Step 2: The boards privacy check**

Create `tools/smoke/check-boards-privacy.ps1`:

```powershell
# Stage 2: boards.json holds no other player. Every userId a panel keeps must be one of your own Roblox user ids
# from accounts.json, a panel carries only its own fields, and no stat key is all digits.
# Prints counts only, never an id. Exit 0 when clean, 1 when something isn't right, 2 when there is nothing to check.
param([string]$DataFolder = (Join-Path $env:LOCALAPPDATA '626labs.ur-score'))

$boardsFile = Join-Path $DataFolder 'boards.json'
$accountsFile = Join-Path $DataFolder 'accounts.json'
if (-not (Test-Path $boardsFile)) { "No boards.json in $DataFolder yet."; exit 2 }

# Windows PowerShell 5.1 passes a parsed JSON array down the pipeline as one object, so unroll it first.
$mine = @()
if (Test-Path $accountsFile) {
    $parsedAccounts = Get-Content $accountsFile -Raw | ConvertFrom-Json
    $mine = @(@($parsedAccounts) | ForEach-Object { [string]$_.robloxUserId } | Where-Object { $_ -and $_ -ne '0' })
}

$panelKeys = @('id', 'type', 'size', 'order', 'settings', 'popout')
$settingKeys = @('recipe', 'sourceId', 'sourceIds', 'toSourceId', 'stat', 'userId')
$boards = 0; $panels = 0; $notYours = 0; $unexpected = 0; $digitStats = 0

$parsedBoards = Get-Content $boardsFile -Raw | ConvertFrom-Json
foreach ($board in @($parsedBoards)) {
    if (-not $board) { continue }
    $boards++
    foreach ($key in $board.PSObject.Properties.Name) { if (@('id', 'name', 'panels') -notcontains $key) { $unexpected++ } }
    foreach ($panel in @($board.panels)) {
        if (-not $panel) { continue }
        $panels++
        foreach ($key in $panel.PSObject.Properties.Name) { if ($panelKeys -notcontains $key) { $unexpected++ } }
        $settings = $panel.settings
        if (-not $settings) { continue }
        foreach ($key in $settings.PSObject.Properties.Name) { if ($settingKeys -notcontains $key) { $unexpected++ } }
        if ($null -ne $settings.userId -and ($mine -notcontains [string]$settings.userId)) { $notYours++ }
        if ($settings.stat -and ([string]$settings.stat -match '^(counter:)?[0-9]+$')) { $digitStats++ }
    }
}

$bad = $notYours + $unexpected + $digitStats
"Your ids: $($mine.Count). Boards: $boards. Panels: $panels."
"Problems: $bad (account ids not yours: $notYours, unexpected fields: $unexpected, all-digit stat keys: $digitStats)."
if ($bad -gt 0) { exit 1 }
exit 0
```

- [ ] **Step 3: The board editing walk**

Create `tools/smoke/walk-board-editing.ps1`:

```powershell
# Stage 2 board editing on a clean data folder: the starter still follows your sources with no boards.json,
# edit mode moves, sizes and removes panels and Done saves them, the gallery adds one, a removed clan's panel
# says so and Choose another fixes it, + Board with Add panel, rename, duplicate and delete, and a restart.
param(
    [string]$Main = 'CCGP',
    [string]$Alt = 'K0i2'
)

. (Join-Path $PSScriptRoot 'uia-board.ps1')
$ErrorActionPreference = 'Stop'
$boardsFile = Join-Path $UrData 'boards.json'
$backup = $null

function Get-SavedPanels([int]$board = 0) { @(@(Read-Boards)[$board].panels) }

try {
    $backup = Move-UrDataAside
    $board = Initialize-ClanBoard $Main $Alt

    # 1. The starter still follows your sources, and nothing is written for it (R1).
    $tabs = @(Get-TabNames $board)
    Check '1 One tab, the starter' ($tabs.Count -eq 1 -and $tabs[0] -eq 'Battle') ($tabs -join ', ')
    Check '1b No boards.json yet' (-not (Test-Path $boardsFile)) "exists=$(Test-Path $boardsFile)"
    Check '1c Each panel shows panel settings' ([bool](Find-ByAutomationId (Find-ByAutomationId $board 'StandingPanel1') 'PanelSettingsButton')) 'PanelSettingsButton'

    # 2. Edit mode with no change writes nothing (R8).
    Enter-EditMode $board
    Check '2 Edit mode shows the panel tools' ([bool](Find-ByAutomationId (Find-ByAutomationId $board 'RacePanel1') 'MoveEarlierButton')) 'MoveEarlierButton'
    Check '2b The tabs are off while editing' (-not (Find-ByAutomationId $board 'BoardTabs').Current.IsEnabled) 'BoardTabs disabled'
    Complete-EditMode $board
    Check '2c Done with no change writes nothing' (-not (Test-Path $boardsFile)) "exists=$(Test-Path $boardsFile)"

    # 3. Move earlier; Done saves the order.
    $before = @(Get-PanelIds $board)
    Enter-EditMode $board
    Invoke-PanelTool $board 'RacePanel1' 'MoveEarlierButton'
    Complete-EditMode $board
    $after = @(Get-PanelIds (Get-BoardWindow))
    $was = [array]::IndexOf($before, 'RacePanel1')
    $now = [array]::IndexOf($after, 'RacePanel1')
    Check '3 The race moved one place earlier' ($was -gt 0 -and $now -eq $was - 1) "before: $($before -join ','); after: $($after -join ',')"
    $race = Get-SavedPanels | Where-Object { $_.type -eq 'race' } | Select-Object -First 1
    Check '3b boards.json holds the order' ((Test-Path $boardsFile) -and $race.order -eq $now) "race order=$($race.order)"

    # 4. Wide and tall.
    Enter-EditMode (Get-BoardWindow)
    Set-PanelSize (Get-BoardWindow) 'RacePanel1' 'Wide'
    Set-Tick (Find-ByAutomationId (Find-ByAutomationId (Get-BoardWindow) 'StandingPanel1') 'TallBox') $true
    Start-Sleep -Milliseconds 800
    Complete-EditMode (Get-BoardWindow)
    $race = Get-SavedPanels | Where-Object { $_.type -eq 'race' } | Select-Object -First 1
    $standing = Get-SavedPanels | Where-Object { $_.type -eq 'standing' } | Select-Object -First 1
    Check '4 The race is wide' ($race.size.span -eq 12) "span=$($race.size.span)"
    Check '4b The first standing panel is tall' ($standing.size.tall -eq $true) "tall=$($standing.size.tall)"

    # 5. Remove.
    Enter-EditMode (Get-BoardWindow)
    Invoke-PanelTool (Get-BoardWindow) 'PromotionCheckPanel1' 'RemovePanelButton'
    Complete-EditMode (Get-BoardWindow)
    Check '5 The promotion check is off the board' (-not (Find-ByAutomationId (Get-BoardWindow) 'PromotionCheckPanel1')) 'PromotionCheckPanel1 absent'
    Check '5b ...and out of boards.json' (-not (Get-SavedPanels | Where-Object { $_.type -eq 'promotionCheck' })) 'no promotionCheck'

    # 6. The gallery adds a panel.
    Enter-EditMode (Get-BoardWindow)
    Invoke-Element (Find-ByAutomationId (Get-BoardWindow) 'AddPanelButton')
    Add-PanelFromGallery 'Live leaderboard'
    Complete-EditMode (Get-BoardWindow)
    Check '6 The gallery added a live leaderboard' ([bool](Find-ByAutomationId (Get-BoardWindow) 'LiveLeaderboardPanel1')) 'LiveLeaderboardPanel1'

    # 7. A removed clan's panel says so; Choose another fixes it (spec 9.4).
    $setup = Open-SetupPage 'Clans'
    Invoke-Element (Get-Button $setup "Remove $Alt")
    $box = Wait-UrWindow '^Ur Score$' 3
    if ($box) { Close-MessageBox $box }
    Start-Sleep -Seconds 1
    Close-UrWindow (Get-SetupWindow)
    $altPanel = Find-ByAutomationId (Get-BoardWindow) 'StandingPanel2'
    Check '7 The panel says its clan was removed' ((Line $altPanel 'PanelStale') -eq "This panel's clan was removed.") (Line $altPanel 'PanelStale')
    Invoke-Element (Find-ByAutomationId $altPanel 'ChooseAnotherButton')
    $form = Wait-UrWindow '^Panel settings$' 15
    Check '7b Choose another opens its settings' ([bool]$form) "form=$([bool]$form)"
    Select-ComboItem (Find-ByAutomationId $form 'SourceBox') "*$Main"
    Invoke-Element (Find-ByAutomationId $form 'SaveSettingsButton')
    Start-Sleep -Seconds 1
    $altPanel = Find-ByAutomationId (Get-BoardWindow) 'StandingPanel2'
    Check '7c It now shows the main clan' ((Line $altPanel 'PanelSubtitle') -eq $Main -and (Line $altPanel 'PanelStale') -eq '(absent)') "subtitle=$(Line $altPanel 'PanelSubtitle')"

    # 8. + Board, an empty board, and its Add panel.
    Invoke-Element (Find-ByAutomationId (Get-BoardWindow) 'AddBoardButton')
    $add = Wait-UrWindow '^Add a board$' 15
    Invoke-Element (Find-ByAutomationId $add 'EmptyBoardButton')
    Start-Sleep -Seconds 1
    $board = Get-BoardWindow
    Check '8 A new empty board is selected' ((Get-SelectedTabName $board) -eq 'Board 2' -and (Line $board 'EmptyStateLine') -eq 'This board has no panels yet') "tab=$(Get-SelectedTabName $board) line=$(Line $board 'EmptyStateLine')"
    Invoke-Element (Find-ByAutomationId $board 'EmptyStateButton')
    Add-PanelFromGallery 'Clan standing'
    $first = Find-ByAutomationId (Get-BoardWindow) 'StandingPanel1'
    Check '8b Add panel put a clan standing on it' ((Line $first 'PanelSubtitle') -eq $Main) "subtitle=$(Line $first 'PanelSubtitle')"

    # 9. Rename, duplicate, delete.
    Invoke-TabMenu (Get-BoardWindow) 'RenameBoardItem' | Out-Null
    $rename = Wait-UrWindow '^Rename board$' 10
    Set-ElementValue (Find-ByAutomationId $rename 'BoardNameBox') 'Rivals'
    Invoke-Element (Find-ByAutomationId $rename 'SaveNameButton')
    Start-Sleep -Seconds 1
    $names = @(Get-TabNames (Get-BoardWindow))
    Check '9 Rename' ($names -contains 'Rivals' -and $names -notcontains 'Board 2') ($names -join ', ')
    Invoke-TabMenu (Get-BoardWindow) 'DuplicateBoardItem' | Out-Null
    Start-Sleep -Seconds 1
    Check '9b Duplicate adds the copy and selects it' ((Get-SelectedTabName (Get-BoardWindow)) -eq 'Rivals copy') ((Get-TabNames (Get-BoardWindow)) -join ', ')
    Invoke-TabMenu (Get-BoardWindow) 'DeleteBoardItem' | Out-Null
    $box = Wait-UrWindow '^Ur Score$' 10
    if ($box) { Close-MessageBox $box }
    Start-Sleep -Seconds 1
    $names = @(Get-TabNames (Get-BoardWindow))
    Check '9c Delete removes it' ($names.Count -eq 2 -and $names -notcontains 'Rivals copy') ($names -join ', ')

    # 10. A restart keeps it all.
    Stop-UrScoreFromBoard
    $board = Start-UrScore
    $names = @(Get-TabNames $board)
    Check '10 The tabs come back, the first selected' ($names.Count -eq 2 -and $names[0] -eq 'Battle' -and $names[1] -eq 'Rivals' -and (Get-SelectedTabName $board) -eq 'Battle') ($names -join ', ')
    $ids = @(Get-PanelIds $board)
    Check '10b The order comes back' ($ids.Count -gt 1 -and $ids[1] -eq 'RacePanel1') ($ids -join ',')

    # 11. boards.json holds only your own account ids.
    & (Join-Path $PSScriptRoot 'check-boards-privacy.ps1') | Out-Host
    $privacy = $LASTEXITCODE
    Check '11 boards.json holds no other player' ($privacy -eq 0) "exit=$privacy"

    & (Join-Path $PSScriptRoot 'shot.ps1') -OutPath (Join-Path $UrShots 'board-editing.png') | Out-Null
}
finally {
    if ($null -ne $backup) { Restore-UrData $backup }
    Show-Results
}
exit $LASTEXITCODE
```

- [ ] **Step 4: The pop-out walk**

Create `tools/smoke/walk-pop-outs.ps1`:

```powershell
# Stage 2 pop-outs on a clean data folder: pop out a panel, it sits on top with no tools of its own and its
# slot on the board says so, it updates after a read, a second one opens, a moved window's place is saved,
# a restart reopens both, and closing one or Bring back returns each panel.
# Step 3 needs a clan battle the source reports (activeClanBattle keeps the last one); an idle source shows dashes.
param([string]$Main = 'CCGP')

. (Join-Path $PSScriptRoot 'uia-board.ps1')
$ErrorActionPreference = 'Stop'
$backup = $null

function Get-PopOutFor([string]$panelId) {
    Get-PopOutWindows | Where-Object { Find-ByAutomationId $_ $panelId } | Select-Object -First 1
}

function Get-SavedPanel([string]$type) {
    foreach ($b in @(Read-Boards)) {
        foreach ($p in @($b.panels)) { if ($p -and $p.type -eq $type) { return $p } }
    }
    return $null
}

try {
    $backup = Move-UrDataAside
    $board = Initialize-ClanBoard $Main ''

    # 1. Pop out the clan standing.
    Invoke-PanelTool $board 'StandingPanel1' 'PopOutButton'
    Wait-Until { Get-PopOutFor 'StandingPanel1' } 15 | Out-Null
    $out = Get-PopOutFor 'StandingPanel1'
    Check '1 Clan standing opens in its own window' ([bool]$out -and $out.Current.Name -eq 'Clan standing') "window='$(if ($out) { $out.Current.Name })'"
    Check '1b It stays on top' ([bool]$out -and $out.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Current.IsTopmost) 'IsTopmost'
    Check '1c Its slot on the board says it is out' ([bool](Find-ByAutomationId (Get-BoardWindow) 'StandingPanel1PoppedOut')) 'StandingPanel1PoppedOut'
    Check '1d boards.json keeps where it is' ($null -ne (Get-SavedPanel 'standing').popout) 'popout saved'
    Check '1e A pop-out has no tools of its own' (-not (Find-ByAutomationId $out 'PopOutButton') -and -not (Find-ByAutomationId $out 'PanelSettingsButton')) 'no pop out or settings button inside'

    # 2. A second one.
    Invoke-PanelTool (Get-BoardWindow) 'AccountCardPanel1' 'PopOutButton'
    Wait-Until { Get-PopOutFor 'AccountCardPanel1' } 15 | Out-Null
    Check '2 Two pop-outs at once' ((Get-PopOutWindows).Count -eq 2) "count=$((Get-PopOutWindows).Count)"

    # 3. It updates live.
    Invoke-Element (Find-ByAutomationId (Get-BoardWindow) 'TestNowButton')
    Start-Sleep -Seconds 20
    $texts = @(Get-AllTexts (Find-ByAutomationId (Get-PopOutFor 'StandingPanel1') 'StandingPanel1'))
    Check '3 The pop-out shows numbers after a read' (@($texts | Where-Object { $_ -match '[0-9]' }).Count -gt 0) ($texts -join ' | ')
    & (Join-Path $PSScriptRoot 'shot.ps1') -Title 'Clan standing' -OutPath (Join-Path $UrShots 'pop-out.png') | Out-Null

    # 4. A moved window's place is saved.
    $beforeX = (Get-SavedPanel 'standing').popout.x
    (Get-PopOutFor 'StandingPanel1').GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern).Move(120, 140)
    Start-Sleep -Seconds 2
    $afterX = (Get-SavedPanel 'standing').popout.x
    Check '4 Moving it saves the new place' ($afterX -ne $beforeX) "x: $beforeX -> $afterX"

    # 5. A restart reopens both where they were.
    Stop-UrScoreFromBoard
    Start-UrScore | Out-Null
    Wait-Until { (Get-PopOutWindows).Count -eq 2 } 30 | Out-Null
    Check '5 Both pop-outs reopen' ((Get-PopOutWindows).Count -eq 2) "count=$((Get-PopOutWindows).Count)"
    Check '5b ...where they were' ((Get-SavedPanel 'standing').popout.x -eq $afterX) "x=$((Get-SavedPanel 'standing').popout.x)"

    # 6. Closing a pop-out returns its panel.
    Invoke-Element (Find-ByAutomationId (Get-PopOutFor 'StandingPanel1') 'ReturnPanelButton')
    Start-Sleep -Seconds 1
    Check '6 Closing it returns the panel' (-not (Get-PopOutFor 'StandingPanel1') -and [bool](Find-ByAutomationId (Get-BoardWindow) 'StandingPanel1')) 'StandingPanel1 back on the board'
    Check '6b ...and boards.json forgets its place' ($null -eq (Get-SavedPanel 'standing').popout) 'no popout'

    # 7. Bring back returns the other.
    Invoke-Element (Get-Button (Get-BoardWindow) 'Bring back Account card')
    Start-Sleep -Seconds 1
    Check '7 Bring back closes its window' ((Get-PopOutWindows).Count -eq 0) "count=$((Get-PopOutWindows).Count)"

    & (Join-Path $PSScriptRoot 'check-boards-privacy.ps1') | Out-Host
    $privacy = $LASTEXITCODE
    Check '8 boards.json holds no other player' ($privacy -eq 0) "exit=$privacy"
}
finally {
    if ($null -ne $backup) { Restore-UrData $backup }
    Show-Results
}
exit $LASTEXITCODE
```

- [ ] **Step 5: The README**

In `tools/smoke/README.md`, add these rows to the table under "The scripts", after the `walk-score-book.ps1` row:

```markdown
| `walk-board-editing.ps1 [-Main CCGP] [-Alt K0i2]` | The starter with no `boards.json`, edit mode (move, wide, tall, remove, Done), the gallery, a removed clan with Choose another, + Board with Add panel, rename, duplicate, delete, a restart |
| `walk-pop-outs.ps1 [-Main CCGP]` | Two pop-outs on top with their slots, a live update, a moved window saved, a restart reopening both, closing and Bring back |
| `check-boards-privacy.ps1 [-DataFolder path]` | Every account id in `boards.json` is one of yours and no panel carries more than its settings (exit 0), else counts the problems and exits 1 -- it never prints an id |
```

Replace the first paragraph under "Helpers" with:

```markdown
`uia.ps1` (UI Automation, starting and stopping Ur Score, the data folder, results), `uia-import.ps1` (the file picker, the import screen, `sources.json`) and `uia-board.ps1` (tabs and their menu, edit mode, panel tools, the gallery and panel form, pop-outs, `boards.json`) are dot-sourced by the walks. The automation ids they rely on are listed in `docs/plans/2026-09-14-score-book-stage-1.md` (before its UI tasks) and `docs/plans/2026-09-14-score-book-stage-2.md` (before its tasks); a change to an id changes its script in the same commit.

The tab menu opens with Shift+F10 on the selected tab, and the pop-out walk quits by closing the board window: leave the keyboard and mouse alone while a walk runs.
```

- [ ] **Step 6: Run the fast gates and the parse check**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror` and `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: both pass (no source changed in this task).

Run each script through the PowerShell parser without executing it:

```powershell
Get-ChildItem tools/smoke -Filter *.ps1 | ForEach-Object {
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$null, [ref]$errors) | Out-Null
    "{0}: {1} parse error(s)" -f $_.Name, @($errors).Count
}
```

Expected: every file reports `0 parse error(s)`.

Run: `Get-ChildItem tools/smoke -Filter *.ps1 | Where-Object { [System.IO.File]::ReadAllBytes($_.FullName) | Where-Object { $_ -gt 127 } | Select-Object -First 1 }`
Expected: no output (every script is ASCII).

Run: `git grep -n -I "C:\\\\Users\|estev" -- tools/smoke`
Expected: no output.

- [ ] **Step 7: Commit**

```text
git add tools/smoke/uia-board.ps1 tools/smoke/walk-board-editing.ps1 tools/smoke/walk-pop-outs.ps1 tools/smoke/check-boards-privacy.ps1 tools/smoke/README.md
git commit -m "smoke: walks for board editing and pop-outs, and a boards.json privacy check

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Step 8: Live walk of stage 2 (controller)**

Run this against RoRoRo 1.28 with your own accounts. Record each numbered result (PASS/FAIL plus what was seen) in the execution record.

1. **Build.** Quit Ur Score so `bin\Release` is free. From the repo root:
   ```text
   dotnet build Ur-Score.csproj -c Release
   dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
   dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build
   ```
   All three succeed.
2. **RoRoRo.** Start RoRoRo 1.28 and confirm it lists your accounts. `Get-Process ROROROblox.App | Select-Object Path` shows the build you mean to test.
3. **Scripted walks, clean data.** Each moves your data aside and restores it. Run them one at a time and read each results table:
   ```powershell
   powershell -ExecutionPolicy Bypass -File tools/smoke/walk-starter-board.ps1 -Main CCGP -Alt K0i2
   powershell -ExecutionPolicy Bypass -File tools/smoke/walk-board-editing.ps1 -Main CCGP -Alt K0i2
   powershell -ExecutionPolicy Bypass -File tools/smoke/walk-pop-outs.ps1 -Main CCGP
   powershell -ExecutionPolicy Bypass -File tools/smoke/window-smoke.ps1 -Main CCGP
   ```
   Every step passes (the starter walk and window smoke prove stage 1 still holds with no `boards.json`). After each, no `626labs.ur-score.smoke-backup-*` folder remains.
4. **Your own data folder** (don't move it aside), with stage 1's sources and score book in place. Launch `bin\Release\net10.0-windows\626labs.ur-score.exe`:
   1. Before any change: the board is stage 1's starter, one tab, and `Test-Path "$env:LOCALAPPDATA\626labs.ur-score\boards.json"` is `False`.
   2. Press **Start**. **Edit board**: move Battle race to the front, make it Wide, make My accounts Tall, remove Account card, **+ Add panel** → Records (Points). **Done**. `boards.json` now exists.
   3. **+ Board** → **Battle** starter, named "Alts". On it, ⋯ on Clan standing → pick K0i2 → **Save**.
   4. Pop out Clan standing (CCGP) from the first board and Promotion check from Alts. Put them on a second monitor if you have one, or in a corner.
   5. Wait for two read cycles (about 6 minutes): both pop-outs and both boards' panels change with the reads, and Battle race's CCGP line gains points.
   6. Quit from the board and start again: both tabs, the first board's order and sizes, and both pop-outs where you left them.
   7. Close one pop-out with ✕ and bring the other back with **Bring back**: both panels are on their boards, and `boards.json` has no `popout` left.
5. **No other player's id on disk.**
   ```powershell
   powershell -ExecutionPolicy Bypass -File tools/smoke/check-boards-privacy.ps1
   powershell -ExecutionPolicy Bypass -File tools/smoke/check-book-privacy.ps1
   ```
   Both print `Problems: 0.` and exit 0.
6. **An unreadable file is kept (R3).** Quit Ur Score. Copy `boards.json` to the session scratchpad, then write `[{ broken` into it. Start Ur Score: the starter board shows, and the detail line says the boards file couldn't be read. Rename a tab (any board change): a `boards.unreadable-*.json` holding `[{ broken` appears beside the new `boards.json`. Quit, put your copy back, start again: your boards return.
7. **Screenshots** for the record:
   ```powershell
   powershell -ExecutionPolicy Bypass -File tools/smoke/shot.ps1 -OutPath artifacts/smoke/live-stage2-board.png
   powershell -ExecutionPolicy Bypass -File tools/smoke/shot.ps1 -Title 'Clan standing' -OutPath artifacts/smoke/live-stage2-pop-out.png
   ```

---

## Self-review record

- **Spec §12 stage 2 coverage:**
  - Step 9 `boards.json` → Task 1 (file, repair, unreadable copy, privacy), Task 5 (loaded and saved by `AppServices`; the starter until the first change).
  - Step 9 tabs (§9.2: switch, + Board empty or from a starter, Rename, Duplicate, Delete) → Task 5; the pure edits → Task 2.
  - Step 9 edit mode (§9.2: drag handles, size menu small/half/wide and tall, ⋯, ✕, order flow, drag to reorder, Done saves) → Task 3 (layout, drop index), Task 7 (the mode).
  - Step 9 panel settings (§9.4 needs; stale settings with Choose another) → Task 4 (pure), Task 6 (window).
  - Step 9 the gallery (§9.4, any type more than once) → Task 4 (cards), Task 6 (window).
  - Step 10 pop-outs (§9.3: own window, on top, title strip, resizable, position remembered, live, closing returns, several, reopen at start) → Task 8.
  - Step 11 a live walk → Task 9; §11's stage 2 walks → Task 9.
  - §13 Boards: `boards.json` round-trip (Task 1), starter boards built from the sources present (stage 1 tests plus Task 1's named starters), a stale panel setting shows its message (stage 1's `PanelModelsTests` plus Task 4's problems and Task 9 step 7).
- **Placeholders:** none; every step names exact files, code and commands.
- **Types against the stage 1 tree:** `PanelSettings(Recipe, SourceId, SourceIds, ToSourceId, Stat, UserId)`, `PanelType`, `LiveBoard.FindSource/FindRecipe/SourceName/MyUserIds/Accounts`, `StarterBoard(Name, Empty, Panels, AnchorSourceId, RecipeSlug)`, `PanelSpec(Type, Span, Settings)`, `BoardLayout.Flow(IReadOnlyList<int>, double)`, `PanelViews.Create/Render`, `BoardText.EmptyState/TopLine/StateLine/DetailLine/Attribution`, `AppServices.CurrentBoard/KnownAccounts/RaiseChanged/AddTrail/Redactor`, `RecipeStats.Offered/Find`, `RecipeState.TrackedStats/StatChoices`, `RecipeWords.Group/GroupsLower/Period/Periods/Capital/Lower` are used as they are in `src/` on master at v0.2.0 (`f0353ee`).

## After stage 2: release (the controller does these)

1. **Version:** set `0.3.0` in `manifest.json` (`"version"`) and in `Ur-Score.csproj` (`<Version>`). Commit `release: 0.3.0`.
2. **Merge (ask Este first; v0.3.0 is not pre-authorized):** open the PR from `feat/boards` to `master`, wait for the `test` workflow to pass, then merge.
3. **Tag and release:** tag `v0.3.0` on `master` and push the tag. The `release` workflow checks that the tag, `manifest.json` and the csproj agree, runs the tests, builds the plugin with .NET included, and attaches `manifest.json`, `manifest.sha256` and `plugin.zip`. Confirm all three are on the release.
4. **Install from the release, not the build folder:**
   - Back up `%LOCALAPPDATA%\ROROROblox\plugins\626labs.ur-score` to the session scratchpad and remove it. Leave `%LOCALAPPDATA%\626labs.ur-score` (your data, including `boards.json`) in place.
   - In RoRoRo's Plugins page, use Install from URL with `https://github.com/estevanhernandez-stack-ed/Ur-Score/releases/latest/download/` and accept the consent.
   - Start the installed Ur Score from RoRoRo. Your saved boards and pop-outs come back; **Edit board**, the gallery, a tab rename and one pop-out work; RoRoRo's Plugins page names version 0.3.0.
5. **Clan post:** draft the "what's new in 0.3.0" note for the clan (boards you arrange, several boards as tabs, the gallery, pop-outs that stay on top while you play, and that nothing about other players is saved). Este posts it.

---

## Execution record (2026-09-15)

Built on `feat/boards` with subagent-driven development: each task implemented, reviewed against its brief, fixed until approved; then a whole-branch review, one fix wave and a scoped re-review (no Critical or Important findings left). 776 tests pass; both builds are clean with `-warnaserror`.

**Tasks:** 1 `e899544` · 2 `f7430f9` · 3 `cdbd712` · 4 `1b21402` `75375a2` `2e18235` · 5 `93651bf` `10eb325` `da74cb2` · 6 `028e24d` · 7 `ae24feb` `fa169ac` · 8 `4383253` `cd40bd0` · 9 `6c7fe0e` · final fix wave `e942e1d`..`73e911a`.

**Rulings made during execution** (they change or add to the plan above):
- Every list in `src/UI` is a `ui:RowList` (the gallery cards and race sources included), so row controls reach UI Automation by name.
- Board-window button state is decided only by `BoardButtons.For` and applied by `ApplyButtons`, with inputs for the board count and edit mode. Stop keeps working during a Test now read. Task 5 edited the existing window instead of the plan's replacement file.
- One save path: a failed board save shows a message box owned by the board; a failed pop-out position save goes to the trail only. The boards-file problem (R3) takes precedence on the detail line and shows before the score book loads.
- Edits that change nothing return the same instance and write nothing (rename to the same name, settings closed unchanged, Done with no change). Handlers re-read the boards after a dialog closes. `BoardsFile.Save(keepExisting)` keeps the unreadable copy when the file failed to load at start, even if it reads by the first save.
- Titles and a source's role label live in `PanelText` and are shared by `PanelModels`, the gallery and the forms. A gallery card speaks for one recipe; a panel's pop-out, placeholder and settings titles use the panel's own recipe.
- Edit mode: keyboard focus returns to the same tool after every rebuild; after Remove it goes to a neighbour's ⋯. Done compares against the board as it was at Edit, and carries each panel's current pop-out state onto the draft. A popped-out panel's slot shows Remove in edit mode and ignores every other tool.
- Pop-outs clamp to the monitor work areas (`PopOutPlacement.Clamp(rect, workAreas)`), never maximize, and keep current automation ids after a renumber.
- `boards.json`: a wrong JSON type costs one panel, not the file; an untouched empty starter is never saved; a duplicated long name still ends in " copy".
- Smoke: `Skip` in `uia.ps1` marks a step that needs a live battle; `check-boards-privacy.ps1` exits 2 when there is nothing to check.

**Live walk (RoRoRo 1.28, the owner's accounts):** window-smoke 12/12, walk-setup-clans 11/11, walk-stats-table 10/10, walk-starter-board 15/15, walk-board-editing 24/24, walk-pop-outs 14/14 and walk-score-book 6/6 with `-Main K0i2` (a clan whose battle can be read: 39 finished battles backfilled, the pop-out showed live numbers), check-boards-privacy and check-book-privacy clean. On the owner's own data folder: a pop-out survived a restart, and a broken `boards.json` showed the starter with its reason and was kept as `boards.unreadable-*.json` on the first change; the folder was returned to no `boards.json`.

**Parked (not fixed in v0.3.0):**
- A Past periods panel whose stat was removed can't be saved when its recipe has no ticked stat left.
- When every source of a recipe is off, the settings form picks an off source while the board shows the stale state.
- walk-pop-outs step 3 can skip, not fail, when a read fails.
- The app-wide menu item style handles flat items only (no submenus); the menu in the light theme and the settings forms' unavailable-choice states weren't seen live.
- The starter board is rebuilt on every `Boards` read (cost only); a pop-out doesn't reopen until a redraw when the score book fails to load; an outside close sent to every window returns pop-outs.
- Stage 1 leftovers still open: claim conflicts not in Diagnostics, the book read twice at startup, reader indexing at scale, a non-atomic recipe text write, silent per-line book skips with no trail breadcrumb.

**Owner action before a battle:** a clan battle recipe installed before v0.2.0 has no `period`, so the starter is Grind and nothing is backfilled. Re-import the current clan battle recipe.
