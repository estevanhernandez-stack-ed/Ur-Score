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
