using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

/// <summary>Edit mode (spec §9.2, R7–R10): a draft of the board on screen, changed by the panel tools, saved by Done.</summary>
public partial class BoardWindow
{
    private const string PanelDragFormat = "Labs626.UrScore.PanelId";

    /// <summary>The board being edited, or null outside edit mode.</summary>
    private BoardDef? _draft;

    /// <summary>
    /// The board as it was when editing began. Done saves only a draft drawn differently from it, so a look around
    /// in edit mode writes nothing, even if the following starter changed underneath it meanwhile (R1).
    /// </summary>
    private BoardDef? _draftBase;

    private EditHintAdorner? _hints;

    /// <summary>The panel being resized by its grip, which grip, and the cell it started from. Null when not.</summary>
    private (int Index, bool Corner, CellRect From)? _resizing;

    private bool Editing => _draft is not null;

    private void HookEditing()
    {
        BoardPanels.AddHandler(PanelFrame.ToolEvent, new EventHandler<PanelToolEventArgs>(OnEditTool));

        // R8: closing Ur Score while editing keeps the arrangement. A save that fails has said so in its message
        // box by now; the window closes anyway, and nothing here may throw out of Closing.
        Closing += (_, _) =>
        {
            _popOutLifecycle.BeginShutdown();
            try
            {
                FinishEditing();
            }
            catch (Exception ex)
            {
                _services.AddTrail($"BOARDS NOT SAVED ON CLOSE: {ex.GetType().Name}");
            }
        };
    }

    private void OnEditBoardClick(object sender, RoutedEventArgs e)
    {
        // The button is collapsed and disabled while editing; this only catches a press already on its way.
        if (!ButtonStates().EditBoard) return;

        _draft = _draftBase = ShownBoard(_services.Boards);
        ShowEditMode();
        Render();

        // Edit board has just hidden itself; focus goes to what replaced it.
        FocusLater(DoneButton);
    }

    private void OnAddPanelClick(object sender, RoutedEventArgs e)
    {
        if (Editing) AddPanelFromGallery();
    }

    private void OnDoneClick(object sender, RoutedEventArgs e)
    {
        FinishEditing();

        // Done has hidden itself, unless the save failed and edit mode stays.
        if (!Editing) FocusLater(EditBoardButton);
    }

    /// <summary>
    /// Done: a draft drawn differently from the board as editing began replaces its board and is saved through
    /// <see cref="SaveBoards"/> (<see cref="BoardEdits.Finish"/>), with every pop-out as the saved boards have it now.
    /// A changed draft of a tab whose starter went empty meanwhile is saved as a board of its own. One that isn't
    /// changed writes nothing. A save that fails stays in edit mode, so the arrangement isn't lost.
    /// </summary>
    private void FinishEditing()
    {
        if (_draft is not { } draft) return;

        var boards = _services.Boards;
        var finished = _draftBase is { } atEdit
            ? BoardEdits.Finish(boards, atEdit, draft)
            : BoardEdits.Replace(boards, BoardEdits.CarryPopOuts(draft, boards));
        if (!ReferenceEquals(finished, boards) && !SaveBoards(finished)) return;

        _draft = _draftBase = null;
        ShowEditMode();
        Render();
    }

    /// <summary>What shows in edit mode. Which buttons take a press is <see cref="ApplyButtons"/>'s, as always.</summary>
    private void ShowEditMode()
    {
        var editing = Editing;
        PanelFrame.SetShowEditTools(BoardPanels, editing);

        EditBoardButton.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        AddPanelButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        DoneButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;

        ApplyButtons();

        // After the grid has arranged, not now: a cell has no rectangle until it has been placed, and entering edit
        // mode changes every panel's height by adding the tools row above it.
        Dispatcher.BeginInvoke(ShowGrips, DispatcherPriority.Loaded);
    }

    private void OnEditTool(object? sender, PanelToolEventArgs e)
    {
        if (!Editing || PanelAt(e.OriginalSource) is not { } def) return;

        // R19: a popped-out panel can be removed in edit mode, and nothing else; bring it back first. BoardEdits
        // refuses to move or resize it too.
        if (def.PopOut is not null && e.Tool != PanelTool.Remove) return;

        // Every tool but the drag rebuilds the grid, destroying the control that was pressed; focus goes back to the
        // same tool on the redrawn panel (R7), so a keyboard user can press it again.
        switch (e.Tool)
        {
            case PanelTool.MoveEarlier:
                e.Handled = true;
                ChangeBoard(board => BoardEdits.MoveBy(board, def.Id, -1));
                FocusToolLater(def.Id, PanelTool.MoveEarlier);
                break;
            case PanelTool.MoveLater:
                e.Handled = true;
                ChangeBoard(board => BoardEdits.MoveBy(board, def.Id, 1));
                FocusToolLater(def.Id, PanelTool.MoveLater);
                break;
            case PanelTool.Resize when e.Size is { } size:
                e.Handled = true;
                var tall = BoardEdits.IsTallTick(def.Size, size);

                // After the size box's SelectionChanged (or Tall's Checked) returns, as the drop waits for its drag:
                // the redraw tears that control down.
                Dispatcher.BeginInvoke(() =>
                {
                    if (!Editing) return;

                    ChangeBoard(board => BoardEdits.Resize(board, def.Id, size));
                    FocusToolLater(def.Id, PanelTool.Resize, tall);
                }, DispatcherPriority.Background);
                break;
            case PanelTool.Remove:
                e.Handled = true;
                var next = _draft is { } shown ? BoardEdits.FocusAfterRemove(shown, def.Id) : null;
                ChangeBoard(board => BoardEdits.RemovePanel(board, def.Id));

                // A neighbour's ⋯, never its Remove: a button clicks on every repeated Enter, and the draft is saved on close (R8).
                FocusToolLater(next, PanelTool.Settings);
                break;
            case PanelTool.DragStart:
                e.Handled = true;
                StartDrag(def);
                break;
        }
    }

    /// <summary>
    /// Once a redraw has laid the grid out, puts keyboard focus on <paramref name="tool"/> of the panel
    /// <paramref name="panelId"/> and brings that panel into view. A panel that wasn't redrawn and still holds focus
    /// (a press that changed nothing, a dialog closed unchanged) keeps it. With no such panel, focus goes to
    /// + Add panel while editing.
    /// </summary>
    private void FocusToolLater(string? panelId, PanelTool tool, bool tall = false)
    {
        var before = ViewOf(panelId);
        Dispatcher.BeginInvoke(() =>
        {
            var view = ViewOf(panelId);
            if (view is null)
            {
                if (Editing) AddPanelButton.Focus();
                return;
            }

            if (ReferenceEquals(view, before) && view.IsKeyboardFocusWithin) return;

            // A popped-out panel's slot has no tools of its own, only its buttons (R19).
            if (view is PoppedOutSlot slot) slot.FocusButton(Editing);
            else PanelFrame.Of(view)?.FocusTool(tool, tall);
            view.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Focus on a top bar button once it shows: Edit board and Done hide themselves when pressed.</summary>
    private void FocusLater(UIElement element) => Dispatcher.BeginInvoke(() => { element.Focus(); }, DispatcherPriority.Loaded);

    private FrameworkElement? ViewOf(string? panelId) =>
        panelId is null ? null : _panels.FirstOrDefault(p => p.Def.Id == panelId).View;

    private void StartDrag(PanelDef def)
    {
        if (ViewOf(def.Id) is not { } view) return;

        // DoDragDrop blocks until the drag ends, however it ends — dropped, cancelled, or escaped — so clearing the
        // mark after it returns covers every one of those without a handler for each.
        try
        {
            DragDrop.DoDragDrop(view, new DataObject(PanelDragFormat, def.Id), DragDropEffects.Move);
        }
        finally
        {
            ShowDropCaret(null);
        }
    }

    /// <summary>
    /// Draws the mark that says where the drop will land, or clears it with null. The adorner is made once and kept:
    /// the board rebuilds its panels on every redraw, but the grid it adorns outlives them.
    /// </summary>
    private void ShowDropCaret(DropCaret? caret)
    {
        if (_hints is null)
        {
            if (AdornerLayer.GetAdornerLayer(BoardPanels) is not { } layer) return;
            _hints = new EditHintAdorner(BoardPanels);
            layer.Add(_hints);
        }

        _hints.Caret = caret;
    }

    /// <summary>
    /// Shows a grip on every panel while editing and none otherwise. Called after each rebuild, because the cells
    /// are only known once the grid has arranged and a rebuild replaces every one of them.
    /// </summary>
    private void ShowGrips()
    {
        ShowDropCaret(null);
        if (_hints is not null) _hints.Grips = Editing ? [.. BoardPanels.Cells.Select(BoardLayout.HandlesFor)] : [];
    }

    private void OnBoardMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!Editing || BoardPanels.GripAt(e.GetPosition(BoardPanels)) is not { } grip) return;
        if (grip.Index >= BoardPanels.Cells.Count) return;

        _resizing = (grip.Index, grip.Corner, BoardPanels.Cells[grip.Index]);
        BoardPanels.CaptureMouse();
        e.Handled = true;
    }

    private void OnBoardMouseMove(object sender, MouseEventArgs e)
    {
        if (_resizing is not { } resizing || _hints is null) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndResize(commit: false);
            return;
        }

        var at = e.GetPosition(BoardPanels);
        var wide = Math.Max(0, at.X - resizing.From.Left);
        var tall = resizing.Corner ? Math.Max(0, at.Y - resizing.From.Top) : resizing.From.Height;
        _hints.Preview = new Rect(resizing.From.Left, resizing.From.Top, wide, tall);
        e.Handled = true;
    }

    private void OnBoardMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_resizing is null) return;
        EndResize(commit: true);
        e.Handled = true;
    }

    /// <summary>
    /// Finishes a grip drag. The size is worked out from the preview the person was actually looking at, not from
    /// the pointer, so what lands is what the outline promised.
    /// </summary>
    private void EndResize(bool commit)
    {
        var resizing = _resizing;
        var preview = _hints?.Preview;
        _resizing = null;
        if (_hints is not null) _hints.Preview = null;
        BoardPanels.ReleaseMouseCapture();

        if (!commit || resizing is not { } grip || preview is not { } shown) return;
        if (_draft is not { } draft || grip.Index >= draft.Panels.Count) return;

        var span = BoardLayout.SpanFor(shown.Width, BoardPanels.ActualWidth, BoardPanels.Gap);
        var rows = grip.Corner ? BoardLayout.RowsFor(shown.Height, grip.From.Height) : (PanelGrid.GetTall(BoardPanels.Children[grip.Index]) ? 2 : 1);
        var panelId = draft.Panels[grip.Index].Id;

        ChangeBoard(board => BoardEdits.Resize(board, panelId, new PanelSize(span, rows > 1)));
    }

    private void OnBoardDragOver(object sender, DragEventArgs e)
    {
        var ours = Editing && e.Data.GetDataPresent(PanelDragFormat);
        e.Effects = ours ? DragDropEffects.Move : DragDropEffects.None;
        ShowDropCaret(ours ? BoardPanels.DropCaretAt(e.GetPosition(BoardPanels)) : null);
        e.Handled = true;
    }

    private void OnBoardDragLeave(object sender, DragEventArgs e)
    {
        ShowDropCaret(null);
        e.Handled = true;
    }

    private void OnBoardDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ShowDropCaret(null);
        if (!Editing || e.Data.GetData(PanelDragFormat) is not string panelId) return;

        var index = BoardPanels.DropIndexAt(e.GetPosition(BoardPanels));

        // After the drag returns: redrawing rebuilds the grid, and the dragged panel is still the drag's source until then.
        // Still editing by then, or the move would be saved straight to the board.
        Dispatcher.BeginInvoke(() =>
        {
            if (Editing) ChangeBoard(board => BoardEdits.MoveTo(board, panelId, index));
        }, DispatcherPriority.Background);
    }
}
