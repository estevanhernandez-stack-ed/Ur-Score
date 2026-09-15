using System.Text.RegularExpressions;

namespace UrScore.Tests;

/// <summary>
/// A stock ItemsControl hides its rows' controls from UI Automation's control view and names each row by its
/// record's ToString, so Narrator and the smoke walks could not find a row's "Make K0i2 main" button. Every
/// list of rows in src/UI is a RowList instead, whose peer hands UI Automation the rows' real controls.
/// </summary>
public partial class RowListFenceTests
{
    /// <summary>The RowLists in src/UI when the fence went in. Lower it only in the commit that removes one.</summary>
    private const int RowListFloor = 22;

    [GeneratedRegex(@"<ItemsControl\b")]
    private static partial Regex BareItemsControl();

    [GeneratedRegex(@"<ui:RowList(?=[\s/>])")]
    private static partial Regex RowListElement();

    [Fact]
    public void NoWindowUsesABareItemsControl()
    {
        var offenders = UiXaml()
            .SelectMany(f => BareItemsControl().Matches(f.Text).Select(m => $"{f.Name}: line {LineOf(f.Text, m.Index)}"))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"These window files use a bare ItemsControl: {string.Join(", ", offenders)}. "
            + "Use <ui:RowList> so each row's controls reach UI Automation with their own names.");
    }

    [Fact]
    public void TheFenceStillSeesEveryRowList()
    {
        var found = UiXaml().Sum(f => RowListElement().Matches(f.Text).Count);

        Assert.True(found >= RowListFloor,
            $"Found {found} <ui:RowList> elements in src/UI, fewer than the {RowListFloor} this fence was written against. "
            + "Either the XAML moved and the fence is looking in the wrong place, or a list was removed and the floor should drop with it.");
    }

    private static List<(string Name, string Text)> UiXaml()
    {
        var ui = Path.Combine(RepoRoot(), "src", "UI");
        var files = Directory.EnumerateFiles(ui, "*.xaml", SearchOption.AllDirectories)
            .Select(f => (Name: Path.GetRelativePath(ui, f), Text: File.ReadAllText(f)))
            .ToList();
        Assert.NotEmpty(files);
        return files;
    }

    private static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

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
