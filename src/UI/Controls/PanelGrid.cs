using System.Windows;
using System.Windows.Controls;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

/// <summary>
/// Lays panels out with <see cref="BoardLayout"/>: 12 columns, a gap between cells, each row as tall as its
/// tallest panel, tall panels across two rows. A one-row panel keeps its own height, top-aligned in its row.
/// Remembers each child's slot (its row's full height), for dragging.
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

    private IReadOnlyList<double> _firstRows = [];

    public static int GetSpan(UIElement element) => (int)element.GetValue(SpanProperty);

    public static void SetSpan(UIElement element, int value) => element.SetValue(SpanProperty, value);

    public static bool GetTall(UIElement element) => (bool)element.GetValue(TallProperty);

    public static void SetTall(UIElement element, bool value) => element.SetValue(TallProperty, value);

    public double Gap { get; set; } = 12;

    /// <summary>Where a panel dropped at <paramref name="point"/>, in this grid's coordinates, would go.</summary>
    public int DropIndexAt(Point point) => BoardLayout.DropIndex(_cells, point.X, point.Y);

    /// <summary>Where to mark that drop, in this grid's coordinates, or null on an empty board.</summary>
    public DropCaret? DropCaretAt(Point point) => BoardLayout.CaretFor(_cells, DropIndexAt(point));

    /// <summary>
    /// Where each panel was arranged, in this grid's coordinates and in the order its children sit in — which is
    /// the order the board's own panel list is in, so an index here indexes that list too.
    /// </summary>
    public IReadOnlyList<CellRect> Cells => _cells;

    /// <summary>The height of the first row the panel at <paramref name="index"/> sits in, for a corner drag; 0 when there is none.</summary>
    public double FirstRowHeightAt(int index) => index >= 0 && index < _firstRows.Count ? _firstRows[index] : 0;

    /// <summary>
    /// The panel whose resize grip is under <paramref name="point"/>, and which grip, or null for neither. The
    /// corner is tried before the edge because it sits inside the edge grip's span and would otherwise be
    /// unreachable.
    /// </summary>
    public (int Index, bool Corner)? GripAt(Point point)
    {
        for (var i = 0; i < _cells.Count; i++)
        {
            var grips = BoardLayout.HandlesFor(_cells[i]);
            if (Holds(grips.Corner, point)) return (i, true);
            if (Holds(grips.Edge, point)) return (i, false);
        }

        return null;
    }

    private static bool Holds(CellRect rect, Point point) =>
        point.X >= rect.Left && point.X <= rect.Left + rect.Width
        && point.Y >= rect.Top && point.Y <= rect.Top + rect.Height;

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
        var firstRows = new List<double>(placements.Count);
        foreach (var placement in placements)
        {
            var child = children[placement.Index];
            var slot = new Rect(
                placement.Column * (column + Gap), tops[placement.Row], CellWidth(finalSize.Width, placement.Span), BoardLayout.CellHeight(placement, rows, Gap));

            child.Arrange(new Rect(slot.X, slot.Y, slot.Width, BoardLayout.ArrangedHeight(placement, rows, Gap, child.DesiredSize.Height)));

            // A drop is placed by the whole slot, so the space under a short panel still counts as that panel's.
            cells.Add(new CellRect(slot.X, slot.Y, slot.Width, slot.Height));
            firstRows.Add(BoardLayout.FirstRowHeight(placement, rows));
        }

        _cells = cells;
        _firstRows = firstRows;
        return finalSize;
    }

    private static List<PanelSize> Sizes(IEnumerable<UIElement> children) =>
        children.Select(child => new PanelSize(GetSpan(child), GetTall(child))).ToList();

    private static List<double> Heights(IEnumerable<UIElement> children) =>
        children.Select(child => child.DesiredSize.Height).ToList();

    private double ColumnWidth(double width) => BoardLayout.ColumnWidth(width, Gap);

    private double CellWidth(double width, int span) => BoardLayout.CellWidth(width, span, Gap);
}
