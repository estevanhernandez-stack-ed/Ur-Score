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

    /// <summary>
    /// R6d: with Pause disabled as the card opens (e.g. a Test now read in flight), nothing inside the card can
    /// take keyboard focus unless the card's own Border can — so it is Focusable, with the same ring a control gets.
    /// </summary>
    [Fact]
    public void TheCardsBorderCanTakeFocusWhenPauseCannot()
    {
        var border = Named(Board(), "CardBorder");
        Assert.Equal("Border", border.Name.LocalName);
        Assert.Equal("True", (string?)border.Attribute("Focusable"));
        Assert.Equal("{StaticResource FocusRing}", (string?)border.Attribute("FocusVisualStyle"));
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

    /// <summary>
    /// R8 (task 14): the slot holding StateLines and ArrangeBanner used to carry the 16,8,16,0 margin itself, so an
    /// 8 px strip showed under the bar even when both children were Collapsed (a healthy, non-arranging board). The
    /// margin now lives on StateLines and ArrangeBanner individually, so a Collapsed child draws nothing at all.
    /// </summary>
    [Fact]
    public void TheStatusSlotCarriesNoMarginOfItsOwn()
    {
        var doc = Board();
        var slot = Named(doc, "StatusSlot");
        Assert.Equal("Grid", slot.Name.LocalName);
        Assert.Null(slot.Attribute("Margin"));

        Assert.Equal("16,8,16,0", (string?)Named(doc, "StateLines").Attribute("Margin"));
        Assert.Equal("16,8,16,0", (string?)Named(doc, "ArrangeBanner").Attribute("Margin"));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ur-Score.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
