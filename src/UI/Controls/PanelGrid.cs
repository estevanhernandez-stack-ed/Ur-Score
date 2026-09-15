using System.Windows;
using System.Windows.Controls;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

/// <summary>Lays panels out with <see cref="BoardLayout"/>: 12 columns, a gap between cells, each row as tall as its tallest panel.</summary>
public sealed class PanelGrid : Panel
{
    public static readonly DependencyProperty SpanProperty = DependencyProperty.RegisterAttached(
        "Span", typeof(int), typeof(PanelGrid),
        new FrameworkPropertyMetadata(BoardLayout.Columns, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static int GetSpan(UIElement element) => (int)element.GetValue(SpanProperty);

    public static void SetSpan(UIElement element, int value) => element.SetValue(SpanProperty, value);

    public double Gap { get; set; } = 12;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 1200 : availableSize.Width;
        var children = InternalChildren.Cast<UIElement>().ToList();
        var placements = BoardLayout.Flow([.. children.Select(GetSpan)], width);

        foreach (var placement in placements)
        {
            children[placement.Index].Measure(new Size(CellWidth(width, placement.Span), double.PositiveInfinity));
        }

        var rows = RowHeights(children, placements);
        return new Size(width, rows.Sum() + Gap * Math.Max(0, rows.Count - 1));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren.Cast<UIElement>().ToList();
        var placements = BoardLayout.Flow([.. children.Select(GetSpan)], finalSize.Width);
        var rows = RowHeights(children, placements);

        var tops = new double[rows.Count];
        for (var row = 1; row < rows.Count; row++) tops[row] = tops[row - 1] + rows[row - 1] + Gap;

        var column = ColumnWidth(finalSize.Width);
        foreach (var placement in placements)
        {
            var child = children[placement.Index];
            child.Arrange(new Rect(placement.Column * (column + Gap), tops[placement.Row], CellWidth(finalSize.Width, placement.Span), child.DesiredSize.Height));
        }

        return finalSize;
    }

    private static List<double> RowHeights(IReadOnlyList<UIElement> children, IReadOnlyList<PanelPlacement> placements)
    {
        var rows = new List<double>();
        foreach (var placement in placements)
        {
            while (rows.Count <= placement.Row) rows.Add(0);
            rows[placement.Row] = Math.Max(rows[placement.Row], children[placement.Index].DesiredSize.Height);
        }

        return rows;
    }

    private double ColumnWidth(double width) => Math.Max(0, (width - Gap * (BoardLayout.Columns - 1)) / BoardLayout.Columns);

    private double CellWidth(double width, int span) => ColumnWidth(width) * span + Gap * (span - 1);
}
