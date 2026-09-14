using System.Globalization;
using System.Text.RegularExpressions;
using ROROROblox.PluginContract;

namespace Labs626.UrScore.Theming;

/// <summary>
/// The slice of RoRoRo's active palette Ur Score paints with. RoRoRo sends resolved colours over the
/// plugin contract (<c>GetTheme</c>, then <c>SubscribeThemeChanged</c>), so Ur Score never reads
/// RoRoRo's settings or theme files and follows built-in and user themes alike.
/// <para>
/// <see cref="Edge"/> is RoRoRo's derived interactive edge: the border a control needs to clear 3:1
/// against the surface behind it. RoRoRo substitutes it when a theme's divider is too faint, so
/// controls use it instead of <see cref="Divider"/>. <see cref="RowHover"/> is derived here, because
/// RoRoRo has no hover slot to send.
/// </para>
/// </summary>
public sealed partial record HostPalette(
    string Bg,
    string Cyan,
    string Magenta,
    string White,
    string MutedText,
    string Divider,
    string RowBg,
    string Edge)
{
    private const double HoverTintStrength = 0.04;

    /// <summary>
    /// RoRoRo's Brand theme, painted before any connection exists and kept whenever RoRoRo is not
    /// running. The first seven slots are RoRoRo's own Brand values; the edge is a 3:1-clearing
    /// stand-in until RoRoRo sends its derived one.
    /// </summary>
    public static readonly HostPalette Brand = new(
        Bg: "#0F1F31", Cyan: "#17D4FA", Magenta: "#F22F89", White: "#FFFFFF",
        MutedText: "#9AA8B8", Divider: "#1F3149", RowBg: "#15263A", Edge: "#6B7F92");

    public string RowHover => Blend(RowBg, White, HoverTintStrength);

    /// <summary>Whether the background is dark, so the native title bar draws light controls on it.</summary>
    public bool IsDark
    {
        get
        {
            var (r, g, b) = Parse(Bg);
            return 0.2126 * r + 0.7152 * g + 0.0722 * b < 128;
        }
    }

    /// <summary>
    /// This palette with every slot RoRoRo sent that is a real colour. A slot that is empty or not
    /// <c>#RRGGBB</c> keeps what is on screen rather than painting black: an older host, or one
    /// with nothing applied yet, can leave a slot empty.
    /// </summary>
    public HostPalette Merge(ThemePalette incoming) => new(
        Bg: Pick(incoming.Bg, Bg),
        Cyan: Pick(incoming.Cyan, Cyan),
        Magenta: Pick(incoming.Magenta, Magenta),
        White: Pick(incoming.White, White),
        MutedText: Pick(incoming.MutedText, MutedText),
        Divider: Pick(incoming.Divider, Divider),
        RowBg: Pick(incoming.RowBg, RowBg),
        Edge: Pick(incoming.InteractiveEdge, Edge));

    public static bool IsColour(string? hex) => hex is not null && Rgb().IsMatch(hex);

    /// <summary><c>#RRGGBB</c> as the 0x00BBGGRR value Windows' title bar attributes take.</summary>
    public static uint ToColorRef(string hex)
    {
        var (r, g, b) = Parse(hex);
        return (uint)(b << 16 | g << 8 | r);
    }

    private static string Pick(string? incoming, string current) => IsColour(incoming) ? incoming!.ToUpperInvariant() : current;

    private static string Blend(string from, string toward, double t)
    {
        var (fr, fg, fb) = Parse(from);
        var (tr, tg, tb) = Parse(toward);
        return $"#{Mix(fr, tr, t):X2}{Mix(fg, tg, t):X2}{Mix(fb, tb, t):X2}";
    }

    private static int Mix(int from, int toward, double t) => (int)Math.Round(from + (toward - from) * t);

    private static (int R, int G, int B) Parse(string hex) => (
        int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex Rgb();
}
