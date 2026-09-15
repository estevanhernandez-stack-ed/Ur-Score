using System.Xml.Linq;

namespace UrScore.Tests;

/// <summary>
/// Other players never reach the clipboard (Global Constraint 2). Every panel table shares the PanelTable style, so the
/// style copies nothing and the one table that may copy says so itself: AccountsGrid, whose rows are your own accounts
/// (D14). A grid of other players placed on the style, or a tidy-up of "redundant" attributes, can't start copying
/// with a green suite.
/// </summary>
public class ClipboardFenceTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private const string CopiesYourAccounts = "AccountsGrid";

    [Fact]
    public void ThePanelTableStyleCopiesNothing()
    {
        var app = XDocument.Load(Path.Combine(RepoRoot(), "src", "App.xaml"));
        var style = Assert.Single(app.Descendants(Presentation + "Style"), s => (string?)s.Attribute(Xaml + "Key") == "PanelTable");

        var setter = Assert.Single(style.Elements(Presentation + "Setter"), s => (string?)s.Attribute("Property") == "ClipboardCopyMode");
        Assert.Equal("None", (string?)setter.Attribute("Value"));
    }

    [Fact]
    public void OnlyTheAccountsTableCanCopy()
    {
        var files = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.xaml", SearchOption.AllDirectories)
            .Select(f => (Name: Path.GetFileName(f), Xaml: XDocument.Load(f)))
            .ToList();
        var offenders = new List<string>();

        // A style setter anywhere, the application's own styles included, may only turn copying off.
        foreach (var (name, xaml) in files)
        {
            offenders.AddRange(xaml.Descendants(Presentation + "Setter")
                .Where(s => (string?)s.Attribute("Property") is "ClipboardCopyMode" or "DataGrid.ClipboardCopyMode" && (string?)s.Attribute("Value") != "None")
                .Select(s => $"{name}: a style sets ClipboardCopyMode to {(string?)s.Attribute("Value")}"));
        }

        var grids = files.SelectMany(f => f.Xaml.Descendants(Presentation + "DataGrid").Select(g => (File: f.Name, Grid: g))).ToList();
        foreach (var (file, grid) in grids)
        {
            var gridName = (string?)grid.Attribute(Xaml + "Name") ?? (string?)grid.Attribute("Name") ?? "(unnamed)";
            var mode = (string?)grid.Attribute("ClipboardCopyMode");
            if (grid.Elements(Presentation + "DataGrid.ClipboardCopyMode").Any()) offenders.Add($"{file}: {gridName} sets ClipboardCopyMode as an element");

            if (gridName == CopiesYourAccounts)
            {
                if (mode != "IncludeHeader") offenders.Add($"{file}: {gridName} should copy your own accounts with their headings, but its ClipboardCopyMode is {mode ?? "not set"}");
                continue;
            }

            // Not set here: the grid copies only if its style lets it, so it must be on PanelTable, which copies nothing.
            if (mode is null && (string?)grid.Attribute("Style") != "{StaticResource PanelTable}")
            {
                offenders.Add($"{file}: {gridName} isn't on the PanelTable style and doesn't turn copying off");
            }
            else if (mode is not null && mode != "None")
            {
                offenders.Add($"{file}: {gridName} copies ({mode}), and only {CopiesYourAccounts} holds nothing but your own accounts");
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));

        // Vacuity: the fence still sees the Accounts table and at least one other grid (Live leaderboard, other players).
        Assert.Single(grids, g => ((string?)g.Grid.Attribute(Xaml + "Name")) == CopiesYourAccounts);
        Assert.True(grids.Count >= 2, $"Found {grids.Count} DataGrid elements in src; the fence was written against 2.");
    }

    [Fact]
    public void NoCodeChangesWhatATableCopies()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("ClipboardCopyMode", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0, $"Code sets what a table copies: {string.Join(", ", offenders)}. Say it in XAML, where the clipboard fence reads it.");
    }

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
