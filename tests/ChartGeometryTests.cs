using Labs626.UrScore.Board;
using Labs626.UrScore.Core;

namespace UrScore.Tests;

public class ChartGeometryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheLowestValueSitsAtTheBottomTheHighestAtTheTopAndTimeRunsLeftToRight()
    {
        ChartSeries[] series =
        [
            new("A", [new(T0, 0), new(T0.AddHours(1), 50)], 0),
            new("B", [new(T0.AddMinutes(30), 100)], 1),
        ];

        var layout = ChartGeometry.Layout(series, width: 240, height: 112, fromZero: true, labels: true);

        // Plot area: left 40 (labels), right pad 4, top 6, bottom 6 -> 196 wide, 100 tall.
        Assert.Equal(new[] { (40.0, 106.0), (236.0, 56.0) }, layout.Lines[0].Points.ToArray());
        Assert.Equal(new[] { (138.0, 6.0) }, layout.Lines[1].Points.ToArray());
        Assert.Equal(0, layout.Lines[0].Colour);
        Assert.Equal(1, layout.Lines[1].Colour);
    }

    [Fact]
    public void TheGridHasFourLinesLabelledFromBottomToTop()
    {
        ChartSeries[] series = [new("A", [new(T0, 0), new(T0.AddHours(1), 90)], 0)];

        var layout = ChartGeometry.Layout(series, 240, 112, fromZero: true, labels: true);

        Assert.Equal(ChartGeometry.GridLines, layout.Grid.Count);
        Assert.Equal(106.0, layout.Grid[0].Y);
        Assert.Equal(6.0, layout.Grid[^1].Y);
        Assert.Equal(StatText.Abbrev(0), layout.Grid[0].Label);
        Assert.Equal(StatText.Abbrev(90), layout.Grid[^1].Label);
    }

    [Fact]
    public void WithoutLabelsTheGridIsStillDrawnButUnlabelled()
    {
        ChartSeries[] series = [new("A", [new(T0, 10), new(T0.AddHours(1), 20)], 0)];

        var layout = ChartGeometry.Layout(series, 200, 60, fromZero: false, labels: false);

        Assert.All(layout.Grid, line => Assert.Equal("", line.Label));
        Assert.Equal(0.0, layout.Lines[0].Points[0].X);
        Assert.Equal(54.0, layout.Lines[0].Points[0].Y);
        Assert.Equal(6.0, layout.Lines[0].Points[1].Y);
    }

    [Fact]
    public void ASinglePointSitsAtTheRightEdgeAndNoPointsDrawNothing()
    {
        var single = ChartGeometry.Layout([new ChartSeries("A", [new(T0, 5)], 0)], 240, 112, fromZero: true, labels: true);
        var empty = ChartGeometry.Layout([new ChartSeries("A", [], 0)], 240, 112, fromZero: true, labels: true);

        Assert.Equal(236.0, single.Lines[0].Points[0].X);
        Assert.Empty(empty.Lines);
        Assert.Empty(empty.Grid);
    }

    // ---- Backlog S1-13.11: a range that isn't there is never drawn ----

    [Fact]
    public void EqualValuesRunThroughTheMiddleWithTheirOneValueLabelled()
    {
        ChartSeries[] series = [new("A", [new(T0, 7_500), new(T0.AddHours(1), 7_500)], 0), new("B", [new(T0.AddMinutes(30), 7_500)], 1)];

        var layout = ChartGeometry.Layout(series, 240, 112, fromZero: false, labels: true);

        // The plot is 100 tall from y = 6, so its middle is 56. There is no range, so no axis is made up around the one value:
        // it used to sit on the bottom line of a 7.5K to 7.5K+1 axis, four labels all reading "7.5K".
        Assert.Equal(new[] { (40.0, 56.0), (236.0, 56.0) }, layout.Lines[0].Points.ToArray());
        Assert.Equal(56.0, Assert.Single(layout.Lines[1].Points).Y);
        var grid = Assert.Single(layout.Grid);
        Assert.Equal((56.0, PanelText.Short(7_500)), (grid.Y, grid.Label));
    }

    [Fact]
    public void ASinglePointAndAnUnlabelledFlatLineHaveNoRangeEither()
    {
        var single = ChartGeometry.Layout([new ChartSeries("A", [new(T0, 5)], 0)], 240, 112, fromZero: false, labels: true);
        var unlabelled = ChartGeometry.Layout([new ChartSeries("A", [new(T0, 12), new(T0.AddHours(1), 12)], 0)], 200, 60, fromZero: false, labels: false);

        Assert.Equal((236.0, 56.0), Assert.Single(single.Lines[0].Points));
        Assert.Equal(PanelText.Short(5), Assert.Single(single.Grid).Label);
        // 60 tall less 6 top and bottom is 48, so the middle is 30.
        Assert.All(unlabelled.Lines[0].Points, p => Assert.Equal(30.0, p.Y));
        Assert.Equal((30.0, ""), (Assert.Single(unlabelled.Grid).Y, unlabelled.Grid[0].Label));
    }

    [Fact]
    public void AChartFromZeroOfOnlyZerosIsFlatInTheMiddleToo()
    {
        var layout = ChartGeometry.Layout([new ChartSeries("A", [new(T0, 0), new(T0.AddHours(1), 0)], 0)], 240, 112, fromZero: true, labels: true);

        Assert.All(layout.Lines[0].Points, p => Assert.Equal(56.0, p.Y));
        Assert.Equal((56.0, PanelText.Short(0)), (Assert.Single(layout.Grid).Y, layout.Grid[0].Label));
    }

    [Fact]
    public void EqualValuesDrawnFromZeroHaveARealRangeFromZero()
    {
        // From zero, 7.5K and 7.5K do have a range: zero to 7.5K. The line sits on the top of it, and zero is the bottom label.
        var layout = ChartGeometry.Layout([new ChartSeries("A", [new(T0, 7_500), new(T0.AddHours(1), 7_500)], 0)], 240, 112, fromZero: true, labels: true);

        Assert.All(layout.Lines[0].Points, p => Assert.Equal(6.0, p.Y));
        Assert.Equal(ChartGeometry.GridLines, layout.Grid.Count);
        Assert.Equal((PanelText.Short(0), PanelText.Short(7_500)), (layout.Grid[0].Label, layout.Grid[^1].Label));
    }

    [Fact]
    public void NegativeValuesPlotBelowZeroAndFromZeroKeepsZeroOnTheAxis()
    {
        ChartSeries[] falling = [new("A", [new(T0, -20), new(T0.AddHours(1), -10)], 0)];
        ChartSeries[] flatBelow = [new("A", [new(T0, -5), new(T0.AddHours(1), -5)], 0)];
        ChartSeries[] across = [new("A", [new(T0, -10), new(T0.AddHours(1), 30)], 0)];

        var free = ChartGeometry.Layout(falling, 240, 112, fromZero: false, labels: true);
        var fromZero = ChartGeometry.Layout(falling, 240, 112, fromZero: true, labels: true);
        var below = ChartGeometry.Layout(flatBelow, 240, 112, fromZero: true, labels: true);
        var both = ChartGeometry.Layout(across, 240, 112, fromZero: true, labels: true);

        // Free: -20 at the bottom, -10 at the top, and the labels say so.
        Assert.Equal(new[] { 106.0, 6.0 }, free.Lines[0].Points.Select(p => p.Y).ToArray());
        Assert.Equal((PanelText.Short(-20), PanelText.Short(-10)), (free.Grid[0].Label, free.Grid[^1].Label));

        // From zero, zero is the top of an all-negative axis: -10 half way down, -20 at the bottom. It used to end at -10,
        // which is an axis "from zero" with no zero on it.
        Assert.Equal(new[] { 106.0, 56.0 }, fromZero.Lines[0].Points.Select(p => p.Y).ToArray());
        Assert.Equal((PanelText.Short(-20), PanelText.Short(0)), (fromZero.Grid[0].Label, fromZero.Grid[^1].Label));

        // A flat line below zero drawn from zero has a real range, -5 to 0, so it sits on the bottom with zero above it.
        Assert.All(below.Lines[0].Points, p => Assert.Equal(106.0, p.Y));
        Assert.Equal(PanelText.Short(0), below.Grid[^1].Label);

        // Across zero: -10 at the bottom, 30 at the top, and zero a quarter of the way up.
        Assert.Equal(new[] { 106.0, 6.0 }, both.Lines[0].Points.Select(p => p.Y).ToArray());
        Assert.Equal((PanelText.Short(-10), PanelText.Short(30)), (both.Grid[0].Label, both.Grid[^1].Label));
    }
}
