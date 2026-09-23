using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

/// <summary>Panel settings and the gallery on the board (spec §9.4).</summary>
public partial class BoardWindow
{
    /// <summary>The saved boards' history, per board, for this session (BC6, spec §5.1).</summary>
    private readonly BoardUndo _undo = new();

    /// <summary>The draft's own history while arranging; Done folds it into one step on <see cref="_undo"/> (spec §5.2).</summary>
    private readonly BoardUndo _draftUndo = new();

    private void OnSettingsTool(object? sender, PanelToolEventArgs e)
    {
        if (e.Tool is not (PanelTool.Settings or PanelTool.ChooseAnother) || PanelAt(e.OriginalSource) is not { } def) return;

        e.Handled = true;
        OpenPanelSettings(def);

        // A saved change (or a draft one) redraws the panel; focus goes back to its ⋯, on the board or in edit mode.
        FocusToolLater(def.Id, PanelTool.Settings);
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

    /// <summary>⋯ and Choose another: the panel's whole form. Closed with nothing changed, it writes nothing (<see cref="BoardEdits.SetSettings"/>).</summary>
    private void OpenPanelSettings(PanelDef def)
    {
        var live = _services.CurrentBoard();
        var form = new PanelSettingsWindow(def.Type, PanelGallery.TitleOf(def, live), live, def.Settings, adding: false) { Owner = this };
        if (form.ShowDialog() != true || form.Result is not { } settings) return;

        ChangeBoard(board => BoardEdits.SetSettings(board, def.Id, def.Type, settings), BoardText.Changed("Changed", PanelGallery.TitleOf(def, live)));
    }

    /// <summary>+ Add panel: the gallery, then the form asking only for what that panel needs (R14).</summary>
    private void AddPanelFromGallery()
    {
        var live = _services.CurrentBoard();
        var gallery = new PanelGalleryWindow(PanelGallery.Cards(live)) { Owner = this };
        if (gallery.ShowDialog() != true || gallery.Picked is not { } type) return;

        // What is installed may have changed while the gallery was open.
        live = _services.CurrentBoard();
        var form = new PanelSettingsWindow(type, PanelGallery.Title(type, live), live, null, adding: true) { Owner = this };
        if (form.ShowDialog() != true || form.Result is not { } settings) return;

        ChangeBoard(board => BoardEdits.AddPanel(board, type, settings), BoardText.Changed("Added", PanelGallery.Title(type, live)));
    }

    /// <summary>
    /// Applies one edit to the board on screen: into the draft while editing (R8, R10), else, as the boards are once
    /// the dialog that asked for it has closed, saved at once. Either way the board is redrawn before this returns.
    /// An edit that changes nothing does nothing. <paramref name="what"/> names the change for undo ("Removed Battle
    /// race"), and the board as it was is pushed on the draft's history or the saved one (BC6); null is a change undo
    /// doesn't cover, such as a pop-out (spec §5.1).
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

            // Every panel's cell may have moved, so the grips drawn over them are stale until the grid re-arranges.
            Dispatcher.BeginInvoke(ShowGrips, DispatcherPriority.Loaded);
            return;
        }

        var boards = _services.Boards;
        var board = ShownBoard(boards);
        var changed = edit(board);
        if (ReferenceEquals(changed, board)) return;

        // Redrawn now, not when Changed's posted redraw comes: a focus hand-off queued after this must find the new panel.
        if (!SaveBoards(BoardEdits.Replace(boards, changed))) return;

        // Pushed only once saved: a change that didn't happen has nothing to take back (spec §5.5).
        if (what is not null)
        {
            _undo.Push(board, what);
            ShowToast(what, canUndo: true);
        }

        Render();
    }

    /// <summary>
    /// Ctrl+Z and the toast's Undo (BC6). While arranging it only ever steps back through the draft — never past it into
    /// the saved history, which would save a change behind Arrange's back (Review Focus 5). Otherwise it restores the
    /// shown board's last snapshot through the same save every change uses; a save that fails leaves the history as it
    /// was (spec §5.5). The snapshot takes each panel's pop-out as the board has it now: a pop-out is not an undoable
    /// change (spec §5.1), so undoing an earlier change must not also bring a panel back or send one out.
    /// </summary>
    private void UndoLast()
    {
        // Not mid-resize: swapping the draft under a held grip would land its size on whichever panel now has that index.
        if (_resizing is not null) return;

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

        if (!SaveBoards(BoardEdits.Replace(boards, BoardEdits.CarryPopOuts(step.Before, boards))))
        {
            ShowToast(BoardText.UndoNotSaved, canUndo: false);
            return;
        }

        history.Pop(board.Id);
        Render();

        // Following.ToSave re-follows the tab only if the snapshot is what its starter draws now (spec §5.4).
        // A re-followed tab whose starter draws nothing now isn't shown at all; that is still following, not unfollowed.
        var now = _services.Boards.FirstOrDefault(b => b.Id == board.Id);
        ShowToast(BoardText.Undone(step, unfollowed: step.Before.Follows is not null && now is not null && now.Follows is null), canUndo: false);
    }
}
