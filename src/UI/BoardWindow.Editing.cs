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

    /// <summary>
    /// The board as it was when editing began. Done saves only a draft drawn differently from it, so a look around
    /// in edit mode writes nothing, even if the following starter changed underneath it meanwhile (R1).
    /// </summary>
    private BoardDef? _draftBase;

    private bool Editing => _draft is not null;

    private void HookEditing()
    {
        BoardPanels.AddHandler(PanelFrame.ToolEvent, new EventHandler<PanelToolEventArgs>(OnEditTool));

        // R8: closing Ur Score while editing keeps the arrangement. A save that fails has said so in its message
        // box by now; the window closes anyway, and nothing here may throw out of Closing.
        Closing += (_, _) =>
        {
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
        // Still editing by then, or the move would be saved straight to the board.
        Dispatcher.BeginInvoke(() =>
        {
            if (Editing) ChangeBoard(board => BoardEdits.MoveTo(board, panelId, index));
        }, DispatcherPriority.Background);
    }
}
