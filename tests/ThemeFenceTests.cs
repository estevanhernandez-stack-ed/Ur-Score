using Labs626.UrScore.Board;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace UrScore.Tests;

/// <summary>
/// Ur Score paints in RoRoRo's theme, so a colour written into a window is a colour that stays put
/// when the user switches theme — the exact defect that made the first window unreadable. The only
/// literal colours allowed are the fallback palette in App.xaml, which RoRoRo's feed replaces.
/// </summary>
public partial class ThemeFenceTests
{
    [GeneratedRegex(@"#[0-9A-Fa-f]{3,8}\b")]
    private static partial Regex HexColour();

    [GeneratedRegex(@"\b(?:Foreground|Background|BorderBrush|Fill|Stroke|CaretBrush|SelectionBrush|RowBackground|AlternatingRowBackground)=""(?!\{)(?!Transparent"")[^""]*""")]
    private static partial Regex LiteralBrush();

    [GeneratedRegex(@"\bBrushes\.|new\s+SolidColorBrush\s*\(|Color\.FromRgb|Colors\.")]
    private static partial Regex CodeColour();

    [Fact]
    public void NoWindowWritesItsOwnColour()
    {
        var ui = Path.Combine(RepoRoot(), "src", "UI");
        var xaml = Directory.EnumerateFiles(ui, "*.xaml", SearchOption.AllDirectories).ToList();
        Assert.NotEmpty(xaml);

        var offenders = xaml
            .SelectMany(f => HexColour().Matches(File.ReadAllText(f)).Cast<Match>()
                .Concat(LiteralBrush().Matches(File.ReadAllText(f)))
                .Select(m => $"{Path.GetFileName(f)}: {m.Value}"))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"These window files set a colour of their own: {string.Join(", ", offenders)}. "
            + "Use a {DynamicResource ...Brush} from App.xaml so RoRoRo's theme reaches it.");
    }

    [Fact]
    public void NoWindowCodePaintsAColour()
    {
        var ui = Path.Combine(RepoRoot(), "src", "UI");

        var offenders = Directory.EnumerateFiles(ui, "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => CodeColour().Matches(File.ReadAllText(f)).Select(m => $"{Path.GetFileName(f)}: {m.Value}"))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Window code paints a colour directly: {string.Join(", ", offenders)}. Only ThemeService replaces brushes.");
    }

    [Fact]
    public void EveryBrushAWindowUsesIsOneThemeServicePaints()
    {
        var root = RepoRoot();
        var painted = BrushKeys().Matches(File.ReadAllText(Path.Combine(root, "src", "Theming", "ThemeService.cs")))
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(painted);

        var files = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.xaml", SearchOption.AllDirectories);
        var unpainted = files
            .SelectMany(f => UsedBrush().Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Key: m.Groups[1].Value)))
            .Where(u => !painted.Contains(u.Key))
            .Select(u => $"{u.File}: {u.Key}")
            .Distinct()
            .ToList();

        Assert.True(unpainted.Count == 0,
            $"These brushes are used but never repainted from RoRoRo's palette: {string.Join(", ", unpainted)}.");
    }

    [Theory]
    [InlineData("Drag to move")]
    [InlineData("Return to the board")]
    public void ToolTipsUseTheSharedTemplateAndFollowPaletteReplacements(string label)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
                var app = XDocument.Load(Path.Combine(RepoRoot(), "src", "App.xaml"));
                var style = Assert.Single(app.Descendants(presentation + "Style"),
                    element => (string?)element.Attribute("TargetType") == "ToolTip");
                Assert.Null(style.Attribute(xaml + "Key"));
                var dictionary = new XElement(presentation + "ResourceDictionary",
                    new XAttribute(XNamespace.Xmlns + "x", xaml),
                    app.Descendants(presentation + "SolidColorBrush").Select(element => new XElement(element)),
                    new XElement(app.Descendants(presentation + "FontFamily").Single(element => (string?)element.Attribute(xaml + "Key") == "BodyFont")),
                    new XElement(style));
                var resources = (System.Windows.ResourceDictionary)System.Windows.Markup.XamlReader.Parse(dictionary.ToString());
                var tooltip = new System.Windows.Controls.ToolTip { Content = label };
                tooltip.Resources.MergedDictionaries.Add(resources);
                Assert.True(tooltip.ApplyTemplate());
                tooltip.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                tooltip.Arrange(new System.Windows.Rect(tooltip.DesiredSize));
                var border = Assert.IsType<System.Windows.Controls.Border>(System.Windows.Media.VisualTreeHelper.GetChild(tooltip, 0));
                var presenter = Assert.IsType<System.Windows.Controls.ContentPresenter>(border.Child);
                presenter.ApplyTemplate();
                var text = Assert.IsType<System.Windows.Controls.TextBlock>(System.Windows.Media.VisualTreeHelper.GetChild(presenter, 0));
                Assert.Equal(label, text.Text);
                Assert.Equal(System.Windows.TextWrapping.Wrap, text.TextWrapping);
                Assert.InRange(tooltip.ActualWidth, 1, 320);
                Assert.Equal(new System.Windows.Thickness(1), border.BorderThickness);

                foreach (var shade in new[] { System.Windows.Media.Colors.Black, System.Windows.Media.Colors.White })
                {
                    var background = new System.Windows.Media.SolidColorBrush(shade);
                    var foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Cyan);
                    var edge = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Magenta);
                    resources["RowBgBrush"] = background;
                    resources["WhiteBrush"] = foreground;
                    resources["EdgeBrush"] = edge;
                    Assert.Same(background, border.Background);
                    Assert.Same(foreground, text.Foreground);
                    Assert.Same(edge, border.BorderBrush);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [GeneratedRegex(@"""(\w+Brush)""")]
    private static partial Regex BrushKeys();

    [GeneratedRegex(@"\{DynamicResource\s+(\w+Brush)\}")]
    private static partial Regex UsedBrush();

    /// <summary>
    /// The one set of brushes a theme deliberately leaves alone. A line colour is what says which clan a line
    /// is, so repainting the scale from the host palette would move a clan from green to grey while the legend
    /// beside it still read green. They are declared in App.xaml and never touched by ThemeService - stated
    /// here so the next reader can tell the exception from an oversight.
    /// </summary>
    [Fact]
    public void TheChartsSeriesColoursAreNeverRepainted()
    {
        var root = RepoRoot();
        var theme = File.ReadAllText(Path.Combine(root, "src", "Theming", "ThemeService.cs"));
        var app = File.ReadAllText(Path.Combine(root, "src", "App.xaml"));

        Assert.NotEmpty(ChartPalette.Keys);
        Assert.All(ChartPalette.Keys, key =>
        {
            Assert.Contains("x:Key=" + Quote + key + Quote, app, StringComparison.Ordinal);
            Assert.DoesNotContain(Quote + key + Quote, theme, StringComparison.Ordinal);
        });
    }

    /// <summary>A double quote, so the two searches above can be written without escaping one.</summary>
    private const string Quote = "\"";

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
