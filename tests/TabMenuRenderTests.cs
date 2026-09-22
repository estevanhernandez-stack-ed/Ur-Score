using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Labs626.UrScore.Theming;

namespace UrScore.Tests;

/// <summary>
/// The board tab's context menu drawn under a LIGHT palette and written to a picture a person can look at
/// (S2-FR.4). RoRoRo ships no light theme — its four built-ins are all dark (rororoblox-fc, from
/// <c>ThemeStore.BuildBuiltIns</c>, 2026-09-22) — so there is no light palette to cite and none to see live. A user
/// can author one, though, and the likeliest way is a near-white base with the brand's accents kept, which is
/// what <see cref="UserLight"/> is: a plausible user-authored light theme, not RoRoRo's. Its Edge is pinned, since
/// RoRoRo derives that slot rather than authoring it.
/// <para>
/// What the test asserts is that the menu renders under that palette with every brush resolved (no transparent
/// text, no black-on-black) and that the picture was written; what the picture shows is for eyes. The menu has
/// three flat items and nothing else — no submenu, no check mark — which is also all the app's menu style handles.
/// </para>
/// </summary>
[Collection(WpfCollection.Name)]
public class TabMenuRenderTests
{
    /// <summary>A user-authored light theme as a user would most likely make one: the base inverted, the accents kept, the edge pinned.</summary>
    private static readonly HostPalette UserLight = new(
        Bg: "#F4F6F9", Cyan: "#0A7FA3", Magenta: "#C41A6B", White: "#101820",
        MutedText: "#5B6774", Divider: "#D9DEE5", RowBg: "#FFFFFF", Edge: "#8A96A5");

    [Fact]
    public void TheTabMenuRendersUnderALightPaletteAndIsWrittenForEyes() => UiThread.RunInApp(() =>
    {
        var before = ThemeService.Current;
        try
        {
            ThemeService.Apply(UserLight);
            Assert.False(ThemeService.Current.IsDark);

            var menu = new ContextMenu
            {
                Items =
                {
                    new MenuItem { Header = "Rename…" },
                    new MenuItem { Header = "Duplicate", IsEnabled = false },
                    new MenuItem { Header = "Delete…" },
                },
            };
            menu.SetResourceReference(Control.BackgroundProperty, "RowBgBrush");
            menu.SetResourceReference(Control.ForegroundProperty, "WhiteBrush");
            menu.SetResourceReference(Control.BorderBrushProperty, "EdgeBrush");

            // A ContextMenu cannot be anybody's child: it is opened, off screen, and rendered as the root of its own
            // popup. The page behind it is drawn here, in the palette's background, as it sits over the board.
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint;
            menu.HorizontalOffset = -4000;
            menu.VerticalOffset = -4000;
            menu.IsOpen = true;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            menu.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
            Assert.True(menu.ActualWidth > 0 && menu.ActualHeight > 0, "the menu never laid out");

            var items = menu.Items.Cast<MenuItem>().ToList();
            Assert.All(items, item => Assert.True(item.ActualHeight > 0, "an item has no height"));
            var text = ((SolidColorBrush)items[0].Foreground).Color;
            var bg = ((SolidColorBrush)menu.Background).Color;
            Assert.NotEqual(text, bg);
            Assert.True(Luminance(bg) > 0.8, $"the menu's background is not light: {bg}");
            Assert.True(Luminance(text) < 0.2, $"the item text is not dark on it: {text}");
            Assert.Equal(Brush("MutedTextBrush").Color, ((SolidColorBrush)items[1].Foreground).Color);

            const int scale = 2, pad = 24;
            var width = (int)((menu.ActualWidth + 2 * pad) * scale);
            var height = (int)((menu.ActualHeight + 2 * pad) * scale);
            var page = new DrawingVisual();
            using (var drawing = page.RenderOpen())
            {
                drawing.DrawRectangle(Brush("BgBrush"), null, new Rect(0, 0, menu.ActualWidth + 2 * pad, menu.ActualHeight + 2 * pad));
                drawing.DrawRectangle(new VisualBrush(menu) { Stretch = Stretch.None }, null, new Rect(pad, pad, menu.ActualWidth, menu.ActualHeight));
            }

            var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(page);
            menu.IsOpen = false;
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var folder = Path.Combine(RepoRoot(), "artifacts", "smoke");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, "tab-menu-user-light.png");
            using (var stream = File.Create(file)) encoder.Save(stream);
            Assert.True(new FileInfo(file).Length > 0);
        }
        finally
        {
            ThemeService.Apply(before);
        }
    });

    private static SolidColorBrush Brush(string key) => (SolidColorBrush)Application.Current.Resources[key];

    private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ur-Score.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.False(dir is null, "Could not locate Ur-Score.csproj above the test assembly.");
        return dir!.FullName;
    }
}
