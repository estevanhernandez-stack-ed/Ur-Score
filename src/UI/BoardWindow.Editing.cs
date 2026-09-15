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
    }

    private void OnAddPanelClick(object sender, RoutedEventArgs e)
    {
        if (Editing) AddPanelFromGallery();
    }

    private void OnDoneClick(object sender, RoutedEventArgs e) => FinishEditing();

    /// <summary>
    /// Done: a draft that changed replaces its board and is saved through <see cref="SaveBoards"/>. A draft that
    /// didn't writes nothing. A save that fails stays in edit mode, so the arrangement isn't lost.
    /// </summary>
    private void FinishEditing()
    {
        if (_draft is not { } draft) return;

        if (_draftBase is not { } before || BoardEdits.Changed(before, draft))
        {
            var boards = _services.Boards;
            var replaced = BoardEdits.Replace(boards, draft);

            // A board that is no longer there has nothing to replace (tabs are off while editing, so this is a guard).
            if (!ReferenceEquals(replaced, boards) && !SaveBoards(replaced)) return;
        }

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
        // Still editing by then, or the move would be saved straight to the board.
        Dispatcher.BeginInvoke(() =>
        {
            if (Editing) ChangeBoard(board => BoardEdits.MoveTo(board, panelId, index));
        }, DispatcherPriority.Background);
    }
}
