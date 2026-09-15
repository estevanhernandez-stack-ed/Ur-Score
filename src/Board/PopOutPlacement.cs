namespace Labs626.UrScore.Board;

/// <summary>Where a pop-out window opens and stays (spec §9.3, R18). The screen is the whole virtual desktop.</summary>
public static class PopOutPlacement
{
    public const double MinWidth = 260;
    public const double MinHeight = 180;
    public const double DefaultWidth = 360;
    public const double DefaultHeight = 300;

    /// <summary>How far each further new pop-out steps from the last.</summary>
    public const double Cascade = 28;

    /// <summary>At least the smallest useful size, no bigger than the screen, and wholly on it.</summary>
    public static PopOutRect Clamp(PopOutRect rect, PopOutRect screen)
    {
        if (!double.IsFinite(rect.X) || !double.IsFinite(rect.Y) || !double.IsFinite(rect.W) || !double.IsFinite(rect.H))
        {
            rect = new PopOutRect(screen.X, screen.Y, DefaultWidth, DefaultHeight);
        }

        var width = Math.Min(Math.Max(rect.W, MinWidth), screen.W);
        var height = Math.Min(Math.Max(rect.H, MinHeight), screen.H);
        var x = Math.Clamp(rect.X, screen.X, screen.X + screen.W - width);
        var y = Math.Clamp(rect.Y, screen.Y, screen.Y + screen.H - height);
        return new PopOutRect(x, y, width, height);
    }

    /// <summary>A panel's first pop-out: inside the board's top-right corner, each further one stepped down and left.</summary>
    public static PopOutRect Default(PopOutRect board, int openCount, PopOutRect screen) =>
        Clamp(new PopOutRect(
            board.X + board.W - DefaultWidth - 24 - openCount * Cascade,
            board.Y + 72 + openCount * Cascade,
            DefaultWidth,
            DefaultHeight), screen);
}
