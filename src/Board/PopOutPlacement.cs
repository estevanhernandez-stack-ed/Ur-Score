namespace Labs626.UrScore.Board;

/// <summary>
/// Where a pop-out window opens and stays (spec §9.3, R18). Screens are given as their work areas, in
/// device-independent pixels; the single-screen overloads treat one rectangle as the only area.
/// </summary>
public static class PopOutPlacement
{
    public const double MinWidth = 260;
    public const double MinHeight = 180;

    /// <summary>What a pop-out opens at when nothing has measured the board or the panel. Everything else is sized from the panel.</summary>
    public const double DefaultWidth = 360;
    public const double DefaultHeight = 300;

    /// <summary>
    /// What <c>PanelPopOutWindow</c> puts around the panel, in device-independent pixels: its 1 px border on each
    /// side, its 30 px title strip, and the scroller's 6 px padding — plus, across the width, the room a vertical
    /// scroll bar takes, allowed for whether or not the window ends up having to scroll, so a table keeps its
    /// columns either way. <c>PopOutPlacementTests</c> holds these against the window's own XAML.
    /// </summary>
    public const double ChromeWidth = 1 + 1 + 6 + 6 + 17;
    public const double ChromeHeight = 1 + 1 + 30 + 6 + 6;

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

    /// <summary>
    /// The size a panel's first pop-out opens at: what the panel already has on the board it was popped out of, and
    /// nothing guessed per panel. Its width is the width its own span (<see cref="PanelSize.Span"/>) has on that
    /// board's grid, through the same column maths the board arranges it with — so a four-column table opens as wide
    /// as it is on the board, a short list of label/value pairs stays small, and a narrow board, which widens every
    /// panel, widens its pop-outs too. Its height is the height the panel asked for at that width, so a table opens
    /// on its rows rather than five of them. Both then carry the window's own furniture
    /// (<see cref="ChromeWidth"/>, <see cref="ChromeHeight"/>). A board or a panel nothing has measured yet — no
    /// window on screen to ask — falls back to <see cref="DefaultWidth"/> by <see cref="DefaultHeight"/>.
    /// The result is only a starting size: <see cref="Default(PopOutRect, int, IReadOnlyList{PopOutRect}, double, double)"/>
    /// caps it to the screen, and the window stays freely resizable, with where the user leaves it saved as before.
    /// </summary>
    public static (double Width, double Height) SizeFor(int span, double boardWidth, double gap, double panelHeight) => (
        double.IsFinite(boardWidth) && boardWidth > 0
            ? BoardLayout.CellWidth(boardWidth, BoardLayout.EffectiveSpan(span, boardWidth), gap) + ChromeWidth
            : DefaultWidth,
        double.IsFinite(panelHeight) && panelHeight > 0 ? panelHeight + ChromeHeight : DefaultHeight);

    /// <summary>A panel's first pop-out: inside the board's top-right corner, each further one stepped down and left.</summary>
    public static PopOutRect Default(PopOutRect board, int openCount, PopOutRect screen) => Default(board, openCount, [screen]);

    /// <summary>As <see cref="Default(PopOutRect, int, PopOutRect)"/>, wholly on the work area most of it falls on, else the nearest.</summary>
    public static PopOutRect Default(PopOutRect board, int openCount, IReadOnlyList<PopOutRect> workAreas) =>
        Default(board, openCount, workAreas, DefaultWidth, DefaultHeight);

    /// <summary>As <see cref="Default(PopOutRect, int, IReadOnlyList{PopOutRect})"/>, at a size <see cref="SizeFor"/> worked out from the panel.</summary>
    public static PopOutRect Default(PopOutRect board, int openCount, IReadOnlyList<PopOutRect> workAreas, double width, double height)
    {
        var rect = new PopOutRect(
            board.X + board.W - width - 24 - openCount * Cascade,
            board.Y + 72 + openCount * Cascade,
            width,
            height);

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
