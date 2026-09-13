using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Labs626.UrScore.Theming;

/// <summary>
/// Paints Ur Score in RoRoRo's theme. Two surfaces: the application brushes every window references
/// through <c>{DynamicResource}</c>, and each window's native title bar.
/// <para>
/// Brushes are REPLACED in <c>Application.Current.Resources</c>, never mutated: a DynamicResource
/// re-binds when the dictionary entry changes, while a mutated brush may be frozen or captured.
/// Same rule as RoRoRo's own ThemeService and Ur Task's.
/// </para>
/// </summary>
public static class ThemeService
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;

    /// <summary>What is on screen now. UI thread only.</summary>
    public static HostPalette Current { get; private set; } = HostPalette.Brand;

    /// <summary>Paints the fallback palette. Call once at startup, before the first window.</summary>
    public static void Start() => Apply(HostPalette.Brand);

    /// <summary>Paints a palette. UI thread only.</summary>
    public static void Apply(HostPalette palette)
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        Current = palette;
        Replace(resources, "BgBrush", palette.Bg);
        Replace(resources, "CyanBrush", palette.Cyan);
        Replace(resources, "MagentaBrush", palette.Magenta);
        Replace(resources, "WhiteBrush", palette.White);
        Replace(resources, "MutedTextBrush", palette.MutedText);
        Replace(resources, "DividerBrush", palette.Divider);
        Replace(resources, "RowBgBrush", palette.RowBg);
        Replace(resources, "RowHoverBrush", palette.RowHover);
        Replace(resources, "EdgeBrush", palette.Edge);

        foreach (Window window in Application.Current!.Windows)
        {
            PaintTitleBar(window);
        }
    }

    /// <summary>Gives a window a title bar in the current palette once it has a handle.</summary>
    public static void Attach(Window window) => window.SourceInitialized += (_, _) => PaintTitleBar(window);

    private static void Replace(ResourceDictionary resources, string key, string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        resources[key] = brush;
    }

    /// <summary>
    /// Windows 11 takes caption, text and border colours; Windows 10 ignores those and takes only the
    /// dark-mode flag, which still keeps a white bar off a dark window. Failures are ignored: a
    /// title bar in the system colour is cosmetic, never a reason to stop.
    /// </summary>
    private static void PaintTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var palette = Current;
        var dark = palette.IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));

        var caption = (int)HostPalette.ToColorRef(palette.Bg);
        var text = (int)HostPalette.ToColorRef(palette.White);
        var border = (int)HostPalette.ToColorRef(palette.Divider);
        _ = DwmSetWindowAttribute(handle, DwmCaptionColor, ref caption, sizeof(int));
        _ = DwmSetWindowAttribute(handle, DwmTextColor, ref text, sizeof(int));
        _ = DwmSetWindowAttribute(handle, DwmBorderColor, ref border, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
