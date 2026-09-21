using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

/// <summary>
/// What edit mode draws over the board to answer "where will this land" and "how big will it be": a caret between
/// two panels while dragging, and an outline of the size an edge drag has reached.
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
public sealed class EditHintAdorner : Adorner
{
    private const double Thickness = 3;

    private DropCaret? _caret;
    private Rect? _preview;
    private IReadOnlyList<PanelHandles> _grips = [];

    public EditHintAdorner(UIElement adorned) : base(adorned) => IsHitTestVisible = false;

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

    /// <summary>
    /// The size an edge drag has reached, in the grid's coordinates, or null to draw nothing. An outline rather
    /// than a live resize: applying it on every mouse move would rebuild the grid and destroy the thumb being
    /// dragged, which is the same reason every other edit tool rebuilds only once it is finished with.
    /// </summary>
    public Rect? Preview
    {
        get => _preview;
        set
        {
            if (_preview == value) return;
            _preview = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// The resize grips to show, one pair per panel, or empty outside edit mode. Drawn for every panel rather than
    /// only the one under the pointer: a grip that appears on hover is a grip nobody discovers.
    /// </summary>
    public IReadOnlyList<PanelHandles> Grips
    {
        get => _grips;
        set
        {
            _grips = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (TryFindResource("CyanBrush") is not Brush brush) return;

        if (_grips.Count > 0 && TryFindResource("MutedTextBrush") is Brush idle)
        {
            foreach (var grips in _grips)
            {
                // The edge reads as a seam rather than a button: a thin bar down the middle of its catch area, so
                // the target is ten pixels wide while the mark is two.
                var seam = new Rect(grips.Edge.Left + (grips.Edge.Width / 2) - 1, grips.Edge.Top + (grips.Edge.Height / 4), 2, grips.Edge.Height / 2);
                drawingContext.DrawRoundedRectangle(idle, null, seam, 1, 1);

                var corner = new Rect(grips.Corner.Left + 3, grips.Corner.Top + 3, grips.Corner.Width - 6, grips.Corner.Height - 6);
                drawingContext.DrawRoundedRectangle(null, new Pen(idle, 1.5), corner, 2, 2);
            }
        }

        if (_preview is { } preview)
        {
            // Outline and a wash rather than a solid fill: the panel underneath has to stay readable, because the
            // size you are choosing is only meaningful against the content it will hold.
            var wash = brush.Clone();
            wash.Opacity = 0.12;
            wash.Freeze();
            drawingContext.DrawRoundedRectangle(wash, new Pen(brush, 2), preview, 6, 6);
        }

        if (_caret is { } caret)
        {
            // Centred on the edge it marks, so it reads as "between these two" rather than as part of either.
            var bar = new Rect(caret.X - (Thickness / 2), caret.Top, Thickness, caret.Height);
            drawingContext.DrawRoundedRectangle(brush, null, bar, Thickness / 2, Thickness / 2);
        }
    }
}
