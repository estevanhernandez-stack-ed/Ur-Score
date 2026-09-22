using System.Text.RegularExpressions;

namespace UrScore.Tests;

/// <summary>
/// A stock ItemsControl names each row by its record's ToString and puts the row's controls under that item, and the
/// live walk of Setup › Clans found "Make K0i2 main" only in UI Automation's raw view as a result. Every list of rows
/// in src/UI is a RowList instead, whose peer hands UI Automation the rows' real controls, flat and named.
/// <para>
/// A ListBox is the other kind of list here (the board tabs, Setup's page list, the search matches, the stats rows),
/// and it has half of the same problem: its items are in the control view with their controls, but each is NAMED by
/// its record's ToString unless the container style says otherwise — a screen reader read a stats row as
/// "Labs626.UrScore.UI.StatRow" (S1-L.4, found 2026-09-22 by <c>ListBoxAutomationTests</c>). So every ListBox's item
/// container style sets AutomationProperties.Name, and the second fence below is what makes a fifth ListBox visible.
/// </para>
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

    /// <summary>The ListBoxes in src/UI when this fence went in. Lower it only in the commit that removes one.</summary>
    private const int ListBoxFloor = 4;

    [GeneratedRegex(@"<ListBox(?=[\s/>])[^>]*>", RegexOptions.Singleline)]
    private static partial Regex ListBoxElement();

    [GeneratedRegex(@"ItemContainerStyle=""\{StaticResource\s+(?<key>\w+)\}""")]
    private static partial Regex KeyedContainerStyle();

    [GeneratedRegex(@"<Setter\s+Property=""AutomationProperties\.Name""")]
    private static partial Regex NameSetter();

    /// <summary>
    /// Every ListBox names its items through its container style: inline as <c>&lt;ListBox.ItemContainerStyle&gt;</c>,
    /// or by a keyed style declared in the same file. A ListBox with neither, or whose style has no
    /// AutomationProperties.Name setter, is named here.
    /// </summary>
    [Fact]
    public void EveryListBoxNamesItsItemsForAutomation()
    {
        var offenders = new List<string>();
        var seen = 0;
        foreach (var (name, text) in UiXaml())
        {
            foreach (Match list in ListBoxElement().Matches(text))
            {
                seen++;
                var where = $"{name}: line {LineOf(text, list.Index)}";
                string? style = null;
                if (KeyedContainerStyle().Match(list.Value) is { Success: true } keyed)
                {
                    var key = keyed.Groups["key"].Value;
                    var start = text.IndexOf($"<Style x:Key=\"{key}\"", StringComparison.Ordinal);
                    var end = start < 0 ? -1 : text.IndexOf("</Style>", start, StringComparison.Ordinal);
                    style = start < 0 || end < 0 ? null : text[start..end];
                }
                else
                {
                    var start = text.IndexOf("<ListBox.ItemContainerStyle>", list.Index, StringComparison.Ordinal);
                    var end = start < 0 ? -1 : text.IndexOf("</ListBox.ItemContainerStyle>", start, StringComparison.Ordinal);
                    var nextList = text.IndexOf("<ListBox", list.Index + 8, StringComparison.Ordinal);
                    style = start < 0 || end < 0 || (nextList >= 0 && start > nextList) ? null : text[start..end];
                }

                if (style is null) offenders.Add($"{where} (no item container style found)");
                else if (!NameSetter().IsMatch(style)) offenders.Add($"{where} (its item container style sets no AutomationProperties.Name)");
            }
        }

        Assert.True(seen >= ListBoxFloor, $"Found {seen} <ListBox> elements in src/UI, fewer than the {ListBoxFloor} this fence was written against.");
        Assert.True(offenders.Count == 0,
            $"These ListBoxes leave their items named by the record's ToString: {string.Join("; ", offenders)}. "
            + "Set AutomationProperties.Name in the item container style so a screen reader reads the item, not the type.");
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
