using System.Text.RegularExpressions;

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

    [GeneratedRegex(@"""(\w+Brush)""")]
    private static partial Regex BrushKeys();

    [GeneratedRegex(@"\{DynamicResource\s+(\w+Brush)\}")]
    private static partial Regex UsedBrush();

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
