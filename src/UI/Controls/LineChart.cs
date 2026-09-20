using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

/// <summary>
/// A line chart: <see cref="Polyline"/>s over a faint grid, placed by <see cref="ChartGeometry"/>. Every
/// brush is a theme brush referenced by key, so a theme switch repaints it (ThemeFenceTests).
/// </summary>
public sealed class LineChart : Canvas
{
    /// <summary>Series colours in order: cyan, magenta, white, muted, edge.</summary>
    public static readonly string[] BrushKeys = ["CyanBrush", "MagentaBrush", "WhiteBrush", "MutedTextBrush", "EdgeBrush"];

    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(IReadOnlyList<ChartSeries>), typeof(LineChart), new PropertyMetadata(null, (d, _) => ((LineChart)d).Redraw()));

    public static readonly DependencyProperty FromZeroProperty = DependencyProperty.Register(
        nameof(FromZero), typeof(bool), typeof(LineChart), new PropertyMetadata(false, (d, _) => ((LineChart)d).Redraw()));

    public static readonly DependencyProperty ShowLabelsProperty = DependencyProperty.Register(
        nameof(ShowLabels), typeof(bool), typeof(LineChart), new PropertyMetadata(true, (d, _) => ((LineChart)d).Redraw()));

    public LineChart()
    {
        ClipToBounds = true;
        SizeChanged += (_, _) => Redraw();
    }

    public IReadOnlyList<ChartSeries>? Series
    {
        get => (IReadOnlyList<ChartSeries>?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public bool FromZero
    {
        get => (bool)GetValue(FromZeroProperty);
        set => SetValue(FromZeroProperty, value);
    }

    public bool ShowLabels
    {
        get => (bool)GetValue(ShowLabelsProperty);
        set => SetValue(ShowLabelsProperty, value);
    }

    public static string BrushKeyFor(int colour) => BrushKeys[((colour % BrushKeys.Length) + BrushKeys.Length) % BrushKeys.Length];

    /// <summary>How a line is drawn when its colour is already taken: solid, dashed, then dotted.</summary>
    public static DoubleCollection? DashFor(int dash) => (((dash % 3) + 3) % 3) switch
    {
        1 => [4, 3],
        2 => [1, 2],
        _ => null,
    };

    private void Redraw()
    {
        Children.Clear();
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        var layout = ChartGeometry.Layout(Series ?? [], ActualWidth, ActualHeight, FromZero, ShowLabels);
        var left = ShowLabels ? ChartGeometry.LabelWidth : 0;

        foreach (var grid in layout.Grid)
        {
            var rule = new Line { X1 = left, X2 = ActualWidth, Y1 = grid.Y, Y2 = grid.Y, StrokeThickness = 1, SnapsToDevicePixels = true };
            rule.SetResourceReference(Shape.StrokeProperty, "DividerBrush");
            Children.Add(rule);

            if (grid.Label.Length == 0) continue;

            var label = new TextBlock { Text = grid.Label, FontSize = 10, TextAlignment = TextAlignment.Right, Width = ChartGeometry.LabelWidth - 6 };
            label.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
            label.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            SetLeft(label, 0);
            SetTop(label, grid.Y - 7);
            Children.Add(label);
        }

        foreach (var line in layout.Lines)
        {
            var polyline = new Polyline
            {
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round,
                Points = new PointCollection(line.Points.Select(p => new Point(p.X, p.Y))),
            };
            polyline.SetResourceReference(Shape.StrokeProperty, BrushKeyFor(line.Colour));
            if (DashFor(line.Dash) is { } dashes) polyline.StrokeDashArray = dashes;
            Children.Add(polyline);
        }
    }
}
