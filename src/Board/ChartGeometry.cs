namespace Labs626.UrScore.Board;

public sealed record ChartPoint(DateTimeOffset T, double Value);

/// <summary>One line: its label, its points in time order, and which theme brush draws it (0 cyan, 1 magenta, 2 white, 3 muted, 4 edge).</summary>
public sealed record ChartSeries(string Label, IReadOnlyList<ChartPoint> Points, int Colour);

public sealed record ChartLine(int Colour, IReadOnlyList<(double X, double Y)> Points);

public sealed record ChartGridLine(double Y, string Label);

public sealed record ChartLayout(IReadOnlyList<ChartLine> Lines, IReadOnlyList<ChartGridLine> Grid);

/// <summary>
/// Where each point of a line chart lands in a box of pixels. Pure, so the chart control only draws. Time
/// runs left to right across the whole span of every series; values run bottom to top.
/// </summary>
public static class ChartGeometry
{
    public const int GridLines = 4;

    public const double LabelWidth = 40;

    private const double Top = 6;
    private const double Bottom = 6;
    private const double RightPad = 4;

    public static ChartLayout Layout(IReadOnlyList<ChartSeries> series, double width, double height, bool fromZero, bool labels)
    {
        var left = labels ? LabelWidth : 0;
        var plotWidth = width - left - RightPad;
        var plotHeight = height - Top - Bottom;
        var points = series.SelectMany(s => s.Points).ToList();
        if (points.Count == 0 || plotWidth <= 0 || plotHeight <= 0) return new ChartLayout([], []);

        var minT = points.Min(p => p.T);
        var maxT = points.Max(p => p.T);
        var minV = points.Min(p => p.Value);
        var maxV = points.Max(p => p.Value);
        if (fromZero)
        {
            // Zero stays on the axis at whichever end it belongs, so values below zero plot under a zero line instead of on an
            // axis "from zero" that has no zero on it.
            minV = Math.Min(0, minV);
            maxV = Math.Max(0, maxV);
        }

        var spanSeconds = (maxT - minT).TotalSeconds;

        double X(DateTimeOffset t) => left + (spanSeconds <= 0 ? plotWidth : (t - minT).TotalSeconds / spanSeconds * plotWidth);

        // Every value the same: there is no range to draw. The line used to sit on the bottom of an axis made up around the
        // one value (7.5K to 7.5K+1, four labels saying "7.5K"), which drew a range that doesn't exist. It now runs through
        // the middle, and the one grid line names the one value (backlog S1-13.11).
        if (maxV <= minV)
        {
            var middle = Top + plotHeight / 2;
            var flat = series
                .Where(s => s.Points.Count > 0)
                .Select(s => new ChartLine(s.Colour, [.. s.Points.OrderBy(p => p.T).Select(p => (X(p.T), middle))]))
                .ToList();
            return new ChartLayout(flat, [new ChartGridLine(middle, labels ? PanelText.Short(minV) : "")]);
        }

        double Y(double value) => Top + (1 - (value - minV) / (maxV - minV)) * plotHeight;

        var lines = series
            .Where(s => s.Points.Count > 0)
            .Select(s => new ChartLine(s.Colour, [.. s.Points.OrderBy(p => p.T).Select(p => (X(p.T), Y(p.Value)))]))
            .ToList();

        var grid = Enumerable.Range(0, GridLines)
            .Select(i =>
            {
                var value = minV + (maxV - minV) * i / (GridLines - 1);
                return new ChartGridLine(Y(value), labels ? PanelText.Short(value) : "");
            })
            .ToList();

        return new ChartLayout(lines, grid);
    }
}
