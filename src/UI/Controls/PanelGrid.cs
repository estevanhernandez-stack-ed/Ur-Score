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
            var slot = new Rect(
                placement.Column * (column + Gap), tops[placement.Row], CellWidth(finalSize.Width, placement.Span), BoardLayout.CellHeight(placement, rows, Gap));

            // The mock's align-items: start. A one-row panel keeps its own height at the top of its row, so a short card
            // doesn't stretch beside a tall neighbour; a tall panel still fills both its rows, which is what Tall asks for.
            var height = placement.Rows > 1 ? slot.Height : Math.Min(child.DesiredSize.Height, slot.Height);
            child.Arrange(new Rect(slot.X, slot.Y, slot.Width, height));

            // A drop is placed by the whole slot, so the space under a short panel still counts as that panel's.
            cells.Add(new CellRect(slot.X, slot.Y, slot.Width, slot.Height));
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
