using System.Windows;
using System.Windows.Controls;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

/// <summary>
/// An account card's sections in equal columns across the card, as many as fit (<see cref="CardLayout"/>): one on a
/// Small card or in a pop-out, two or three on a wide one. Each column stacks its own sections at their own heights.
/// </summary>
public sealed class SectionColumnsPanel : Panel
{
    /// <summary>The space between two sections stacked in one column.</summary>
    public double RowGap { get; set; } = 10;

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = InternalChildren.Cast<UIElement>().ToList();
        if (children.Count == 0) return default;

        var width = availableSize.Width;
        if (double.IsInfinity(width))
        {
            // Unbounded: every section side by side at its own width.
            foreach (var child in children) child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            width = children.Sum(c => c.DesiredSize.Width) + CardLayout.ColumnGap * (children.Count - 1);
        }

        var columns = CardLayout.SectionColumns(width, children.Count);
        var columnWidth = CardLayout.ColumnWidth(width, columns);
        foreach (var child in children) child.Measure(new Size(columnWidth, double.PositiveInfinity));

        return new Size(width, ColumnHeights(children, columns).Max());
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren.Cast<UIElement>().ToList();
        if (children.Count == 0) return finalSize;

        var columns = CardLayout.SectionColumns(finalSize.Width, children.Count);
        var columnWidth = CardLayout.ColumnWidth(finalSize.Width, columns);
        var tops = new double[columns];

        for (var index = 0; index < children.Count; index++)
        {
            var column = CardLayout.ColumnOf(index, columns);
            var child = children[index];
            if (index >= columns) tops[column] += RowGap;

            child.Arrange(new Rect(column * (columnWidth + CardLayout.ColumnGap), tops[column], columnWidth, child.DesiredSize.Height));
            tops[column] += child.DesiredSize.Height;
        }

        return finalSize;
    }

    private double[] ColumnHeights(IReadOnlyList<UIElement> children, int columns)
    {
        var heights = new double[columns];
        for (var index = 0; index < children.Count; index++)
        {
            var column = CardLayout.ColumnOf(index, columns);
            heights[column] += (index >= columns ? RowGap : 0) + children[index].DesiredSize.Height;
        }

        return heights;
    }
}
