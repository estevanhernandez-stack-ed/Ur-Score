namespace Labs626.UrScore.Board;

/// <summary>
/// The colours a chart tells its lines apart with.
/// <para>
/// These are the one set of brushes in Ur Score that a theme change does NOT repaint, and that is deliberate: a
/// line's colour is what says which clan it is. Repainting them from the host palette would move a clan's line
/// from green to grey because somebody changed a theme, and the legend beside it would still say green.
/// </para>
/// <para>
/// The first two are the brand's own, so a default board looks the way it always has, and the rest are hues
/// chosen to be told apart from each other and from those two on the navy ground. The owner's verdict on
/// 2026-09-20, on a board of seven lines: "all of the other lines are the same color" — the old set was cyan,
/// magenta, white and two greys, so six of seven rivals really were shades of the same thing.
/// </para>
/// </summary>
public static class ChartPalette
{
    /// <summary>Series colours in order: cyan, magenta, yellow, green, violet, orange, periwinkle, near-white.</summary>
    public static readonly string[] Keys =
    [
        "Series1Brush", "Series2Brush", "Series3Brush", "Series4Brush",
        "Series5Brush", "Series6Brush", "Series7Brush", "Series8Brush",
    ];

    /// <summary>How many lines the chart can draw before it starts repeating a colour with a dash.</summary>
    public static int Count => Keys.Length;
}
