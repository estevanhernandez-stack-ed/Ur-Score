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
}
