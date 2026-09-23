using System.Xml.Linq;

namespace UrScore.Tests;

/// <summary>
/// The top bar's shape, as the smoke walks and screen readers depend on it (spec §3). Read from the XAML, the way
/// DragHandleTests reads PanelFrame, because none of it is visible to a unit test of the running window.
/// </summary>
public class TopBarFenceTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XDocument Board() => XDocument.Load(Path.Combine(RepoRoot(), "src", "UI", "BoardWindow.xaml"));

    private static XElement Named(XDocument doc, string name) =>
        doc.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);

    [Fact]
    public void TheChipAndReadNowKeepTheIdsTheWalksPress()
    {
        var doc = Board();
        Assert.Equal("OnStatusChipClick", (string?)Named(doc, "StartStopButton").Attribute("Click"));
        Assert.Equal("OnTestNowClick", (string?)Named(doc, "TestNowButton").Attribute("Click"));
        Assert.Equal("OnPauseResumeClick", (string?)Named(doc, "PauseResumeButton").Attribute("Click"));
    }

    /// <summary>BC2: Pause lives in the card, never on the bar, so checking status can't pause.</summary>
    [Fact]
    public void PauseIsInsideTheStatusCard()
    {
        var card = Named(Board(), "StatusCard");
        Assert.Equal("Popup", card.Name.LocalName);
        Assert.Equal("False", (string?)card.Attribute("StaysOpen"));
        Assert.Contains(card.Descendants(), e => (string?)e.Attribute(X + "Name") == "PauseResumeButton");
    }

    /// <summary>Tab order follows what is seen: the bar is a Grid in left-to-right order, the right-hand buttons last.</summary>
    [Fact]
    public void TheRightHandButtonsComeLastInTheBar()
    {
        var bar = Named(Board(), "TopBar");
        Assert.Equal("Grid", bar.Name.LocalName);
        // Property elements (Grid.ColumnDefinitions) and the Popup, which is out of the layout and the Tab order, don't count.
        var children = bar.Elements().Where(e => !e.Name.LocalName.Contains('.') && e.Name.LocalName != "Popup").ToList();
        Assert.Equal("TopButtons", (string?)children[^1].Attribute(X + "Name"));
    }

    [Fact]
    public void StartAndTestNowAreNoLongerWordsOnTheBar()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "UI", "BoardWindow.xaml"));
        Assert.DoesNotContain("Content=\"Start\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Test now\"", text, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ur-Score.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
