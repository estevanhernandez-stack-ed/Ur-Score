using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

/// <summary>
/// The mark that says where a dragged panel will land: a short accent bar between two panels.
/// <para>
/// An adorner rather than a child of the grid, because <see cref="PanelGrid"/> measures and arranges every child
/// it has — a caret added as one would be laid out as a panel and push the board around while you dragged. An
/// adorner draws above the grid in the GRID's own coordinates, which also means it scrolls with the board without
/// anything having to subtract a scroll offset.
/// </para>
/// <para>
/// Never hit-testable: it sits directly under the pointer for the whole drag, and a drop that landed on the mark
/// instead of the board would be a drop that went nowhere.
/// </para>
/// </summary>
public sealed class DropCaretAdorner : Adorner
{
    private const double Thickness = 3;

    private DropCaret? _caret;

    public DropCaretAdorner(UIElement adorned) : base(adorned) => IsHitTestVisible = false;

    /// <summary>Where the mark goes, or null to draw nothing. Setting it redraws only when it has moved.</summary>
    public DropCaret? Caret
    {
        get => _caret;
        set
        {
            if (_caret == value) return;
            _caret = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_caret is not { } caret) return;
        if (TryFindResource("CyanBrush") is not Brush brush) return;

        // Centred on the edge it marks, so it reads as "between these two" rather than as part of either.
        var bar = new Rect(caret.X - (Thickness / 2), caret.Top, Thickness, caret.Height);
        drawingContext.DrawRoundedRectangle(brush, null, bar, Thickness / 2, Thickness / 2);
    }
}
