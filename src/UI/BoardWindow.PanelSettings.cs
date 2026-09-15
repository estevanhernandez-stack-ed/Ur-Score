using System.Windows;
using System.Windows.Media;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

/// <summary>Panel settings and the gallery on the board (spec §9.4).</summary>
public partial class BoardWindow
{
    private void OnSettingsTool(object? sender, PanelToolEventArgs e)
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

    /// <summary>⋯ and Choose another: the panel's whole form. Closed with nothing changed, it writes nothing (<see cref="BoardEdits.SetSettings"/>).</summary>
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

        // What is installed may have changed while the gallery was open.
        live = _services.CurrentBoard();
        var form = new PanelSettingsWindow(type, PanelGallery.Title(type, live), live, null, adding: true) { Owner = this };
        if (form.ShowDialog() != true || form.Result is not { } settings) return;

        ChangeBoard(board => BoardEdits.AddPanel(board, type, settings));
    }

    /// <summary>
    /// Applies one edit to the board on screen, as the boards are once the dialog that asked for it has closed, and
    /// saves every board. An edit that changes nothing saves nothing.
    /// </summary>
    private void ChangeBoard(Func<BoardDef, BoardDef> edit)
    {
        var boards = _services.Boards;
        var board = ShownBoard(boards);
        var changed = edit(board);
        if (ReferenceEquals(changed, board)) return;

        SaveBoards(BoardEdits.Replace(boards, changed));
    }
}
