namespace Labs626.UrScore.Board;

/// <summary>
/// Where a pop-out window opens and stays (spec §9.3, R18). Screens are given as their work areas, in
/// device-independent pixels; the single-screen overloads treat one rectangle as the only area.
/// </summary>
public static class PopOutPlacement
{
    public const double MinWidth = 260;
    public const double MinHeight = 180;
    public const double DefaultWidth = 360;
    public const double DefaultHeight = 300;

    /// <summary>How far each further new pop-out steps from the last.</summary>
    public const double Cascade = 28;

    /// <summary>
    /// The top band of a pop-out that must be on a screen for it to stay where it is: its title strip, by which it is
    /// dragged, with room to spare.
    /// </summary>
    public const double TitleStrip = 40;

    /// <summary>At least the smallest useful size, no bigger than the screen, and wholly on it.</summary>
    public static PopOutRect Clamp(PopOutRect rect, PopOutRect screen) => Clamp(rect, [screen]);

    /// <summary>
    /// A saved pop-out back where it can be reached (R18). A window whose title strip lies on a work area (its top
    /// <see cref="TitleStrip"/> wholly within the area's height, across at least half of <see cref="MinWidth"/>) stays
    /// where it is, sized to fit that area. Any other window moves wholly onto the work area nearest it, keeping its
    /// size where that fits. A rect that isn't a number opens at the first area's corner at the default size. With no
    /// areas, the rect is only sized.
    /// </summary>
    public static PopOutRect Clamp(PopOutRect rect, IReadOnlyList<PopOutRect> workAreas)
    {
        if (!IsFinite(rect))
        {
            var corner = workAreas.Count > 0 ? workAreas[0] : new PopOutRect(0, 0, DefaultWidth, DefaultHeight);
            rect = new PopOutRect(corner.X, corner.Y, DefaultWidth, DefaultHeight);
        }

        rect = rect with { W = Math.Max(rect.W, MinWidth), H = Math.Max(rect.H, MinHeight) };
        if (workAreas.Count == 0) return rect;

        PopOutRect? holding = null;
        var held = 0.0;
        foreach (var area in workAreas)
        {
            var across = Overlap(rect.X, rect.W, area.X, area.W);
            var titleOn = rect.Y >= area.Y && rect.Y + TitleStrip <= area.Y + area.H;
            if (titleOn && across >= MinWidth / 2 && across > held)
            {
                holding = area;
                held = across;
            }
        }

        return holding is { } on ? SizeTo(rect, on) : Fit(rect, Nearest(rect, workAreas));
    }

    /// <summary>A panel's first pop-out: inside the board's top-right corner, each further one stepped down and left.</summary>
    public static PopOutRect Default(PopOutRect board, int openCount, PopOutRect screen) => Default(board, openCount, [screen]);

    /// <summary>As <see cref="Default(PopOutRect, int, PopOutRect)"/>, wholly on the work area most of it falls on, else the nearest.</summary>
    public static PopOutRect Default(PopOutRect board, int openCount, IReadOnlyList<PopOutRect> workAreas)
    {
        var rect = new PopOutRect(
            board.X + board.W - DefaultWidth - 24 - openCount * Cascade,
            board.Y + 72 + openCount * Cascade,
            DefaultWidth,
            DefaultHeight);

        if (!IsFinite(rect) || workAreas.Count == 0) return Clamp(rect, workAreas);
        return Fit(rect, Nearest(rect, workAreas));
    }

    /// <summary>No bigger than the area, and wholly on it.</summary>
    private static PopOutRect Fit(PopOutRect rect, PopOutRect area)
    {
        var sized = SizeTo(rect, area);
        return sized with
        {
            X = Math.Clamp(sized.X, area.X, area.X + area.W - sized.W),
            Y = Math.Clamp(sized.Y, area.Y, area.Y + area.H - sized.H),
        };
    }

    /// <summary>At least the smallest useful size and no bigger than the area; where it is stays.</summary>
    private static PopOutRect SizeTo(PopOutRect rect, PopOutRect area) => rect with
    {
        W = Math.Min(Math.Max(rect.W, MinWidth), area.W),
        H = Math.Min(Math.Max(rect.H, MinHeight), area.H),
    };

    /// <summary>The area the least distance from the rect; among areas it overlaps, the one it overlaps most; then the first.</summary>
    private static PopOutRect Nearest(PopOutRect rect, IReadOnlyList<PopOutRect> areas)
    {
        var best = areas[0];
        var bestDistance = double.MaxValue;
        var bestOverlap = -1.0;
        foreach (var area in areas)
        {
            var dx = Math.Max(0, Math.Max(area.X - (rect.X + rect.W), rect.X - (area.X + area.W)));
            var dy = Math.Max(0, Math.Max(area.Y - (rect.Y + rect.H), rect.Y - (area.Y + area.H)));
            var distance = Math.Sqrt(dx * dx + dy * dy);
            var overlap = Overlap(rect.X, rect.W, area.X, area.W) * Overlap(rect.Y, rect.H, area.Y, area.H);

            if (distance < bestDistance || (distance == bestDistance && overlap > bestOverlap))
            {
                best = area;
                bestDistance = distance;
                bestOverlap = overlap;
            }
        }

        return best;
    }

    /// <summary>How much of one span lies within another, or 0.</summary>
    private static double Overlap(double start, double length, double otherStart, double otherLength) =>
        Math.Max(0, Math.Min(start + length, otherStart + otherLength) - Math.Max(start, otherStart));

    private static bool IsFinite(PopOutRect rect) =>
        double.IsFinite(rect.X) && double.IsFinite(rect.Y) && double.IsFinite(rect.W) && double.IsFinite(rect.H);
}
