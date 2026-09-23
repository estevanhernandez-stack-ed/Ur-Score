using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Xml.Linq;

namespace UrScore.Tests;

[Collection(WpfCollection.Name)]
public class DragHandleTests
{
    [Fact]
    public void GripIsDrawnNamedInsideTheHeaderAndDragsThroughIt()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "Ur-Score.csproj"))) directory = directory.Parent;
                Assert.NotNull(directory);
                XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
                var panel = XDocument.Load(System.IO.Path.Combine(directory.FullName, "src", "UI", "Panels", "PanelFrame.xaml"));
                var grip = panel.Descendants().Single(element => (string?)element.Attribute(xaml + "Name") == "DragHandle");
                Assert.Equal(presentation + "Label", grip.Name);
                Assert.Contains(grip.Ancestors(), a => (string?)a.Attribute(xaml + "Name") == "TitleRow");
                Assert.Null(grip.Attribute("MouseLeftButtonDown"));
                var label = (Label)XamlReader.Parse(grip.ToString());
                var peer = Assert.IsType<LabelAutomationPeer>(UIElementAutomationPeer.CreatePeerForElement(label));
                Assert.Equal("DragHandle", peer.GetAutomationId());
                Assert.Equal("Drag to move", peer.GetName());
                Assert.Equal("Drag to move", label.ToolTip);
                Assert.False(label.Focusable);
                // The grip starts Collapsed outside Arrange; measure it as shown, since a Collapsed element always
                // desired-sizes to zero regardless of content.
                label.Visibility = Visibility.Visible;
                label.Measure(new Size(100, 100));
                Assert.InRange(label.DesiredSize.Height, 8, 20);
                Assert.InRange(label.DesiredSize.Width, 12, 30);
                var dots = Assert.IsType<UniformGrid>(label.Content);
                Assert.Equal((3, 2, 8d, 12d), (dots.Rows, dots.Columns, dots.Width, dots.Height));
                Assert.False(dots.IsHitTestVisible);
                Assert.Equal(6, dots.Children.Count);
                foreach (var color in new[] { Colors.Gray, Colors.White })
                {
                    var brush = new SolidColorBrush(color);
                    label.Resources["MutedTextBrush"] = brush;
                    Assert.All(dots.Children.Cast<UIElement>(), child =>
                    {
                        var dot = Assert.IsType<Ellipse>(child);
                        Assert.Equal((2d, 2d), (dot.Width, dot.Height));
                        Assert.Same(brush, dot.Fill);
                    });
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        // A Join with no timeout cannot fail, only wait. On 2026-09-17 six of the eight tests built this way
        // were stuck on one at once and the whole run hung until a human noticed (V3-S.38). A bounded wait
        // turns that into a named failing test, which is the difference between a diagnosis and a mystery.
        Assert.True(thread.Join(UiThread.Longest), $"the UI thread did not finish within {UiThread.Longest}");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    /// <summary>Spec §4.3: nothing is inserted above the title while arranging, so no panel changes height on the way in.</summary>
    [Fact]
    public void ArrangingAddsNoRowAboveTheTitle()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "Ur-Score.csproj"))) directory = directory.Parent;
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var panel = XDocument.Load(System.IO.Path.Combine(directory!.FullName, "src", "UI", "Panels", "PanelFrame.xaml"));

        Assert.DoesNotContain(panel.Descendants(), e => (string?)e.Attribute(xaml + "Name") == "EditTools");
        var remove = panel.Descendants().Single(e => (string?)e.Attribute(xaml + "Name") == "RemovePanelButton");
        Assert.Contains(remove.Ancestors(), a => (string?)a.Attribute(xaml + "Name") == "PanelTools");
    }

    /// <summary>
    /// The whole title line drags the panel while editing, not the six-dot grip alone. Two things have to hold in
    /// the XAML for that to work at all, and neither is visible by looking at the running app:
    /// <list type="bullet">
    /// <item>the three mouse handlers are wired, since a press with no move handler can never become a drag;</item>
    /// <item>the row carries a Background. A DockPanel with no brush is hit-test INVISIBLE over its empty space, so
    /// without one a press between the title and the tools falls straight through and the header would only drag
    /// where there happens to be text — which reads as a header that works sometimes.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void TheWholeTitleRowDragsAndIsHitTestableAcrossItsEmptySpace()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "Ur-Score.csproj"))) directory = directory.Parent;
        Assert.NotNull(directory);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var panel = XDocument.Load(System.IO.Path.Combine(directory.FullName, "src", "UI", "Panels", "PanelFrame.xaml"));
        var row = panel.Descendants().Single(element => (string?)element.Attribute(xaml + "Name") == "TitleRow");

        Assert.Equal(presentation + "DockPanel", row.Name);
        Assert.Equal("OnTitleRowDown", (string?)row.Attribute("MouseLeftButtonDown"));
        Assert.Equal("OnTitleRowMove", (string?)row.Attribute("MouseMove"));
        Assert.Equal("OnTitleRowUp", (string?)row.Attribute("MouseLeftButtonUp"));
        Assert.Equal("OnTitleRowLostCapture", (string?)row.Attribute("LostMouseCapture"));

        var background = (string?)row.Attribute("Background");
        Assert.False(string.IsNullOrWhiteSpace(background), "the title row needs a Background or its empty space is hit-test invisible");
    }

    /// <summary>
    /// A press on the header captures the mouse while arranging, and the drag lets it go before it starts. Without
    /// the capture a pointer that left the ~20px title row before crossing the drag threshold (a quick diagonal
    /// move, which is how a real hand drags a panel to the one below) never raised TitleRow's MouseMove again, and
    /// nothing dragged; the UIA walk's real-pointer drag from the grip failed exactly this way (2026-09-23). The
    /// release has to come BEFORE DragStart is raised, because DoDragDrop runs inside that event and needs the
    /// mouse. Read from source: capture needs a real window and a real pointer, which no unit test here has.
    /// </summary>
    [Fact]
    public void AHeaderPressCapturesTheMouseAndTheDragReleasesItFirst()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "Ur-Score.csproj"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(System.IO.Path.Combine(directory!.FullName, "src", "UI", "Panels", "PanelFrame.xaml.cs"));

        string Body(string signature, string next)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            var end = source.IndexOf(next, start + 1, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start, $"{signature} has moved or been renamed; this fence is looking in the wrong place.");
            return source[start..end];
        }

        var down = Body("private void OnTitleRowDown(", "private void OnTitleRowMove(");
        Assert.Contains("TitleRow.CaptureMouse()", down, StringComparison.Ordinal);

        var move = Body("private void OnTitleRowMove(", "private void OnTitleRowUp(");
        var release = move.LastIndexOf("TitleRow.ReleaseMouseCapture()", StringComparison.Ordinal);
        var drag = move.IndexOf("PanelTool.DragStart", StringComparison.Ordinal);
        Assert.True(release >= 0 && drag > release, "the drag must release the capture before it raises DragStart");

        var up = Body("private void OnTitleRowUp(", "private void OnTitleRowLostCapture(");
        Assert.Contains("TitleRow.ReleaseMouseCapture()", up, StringComparison.Ordinal);
        Assert.Contains("_pressedAt = null", source[source.IndexOf("private void OnTitleRowLostCapture(", StringComparison.Ordinal)..], StringComparison.Ordinal);
    }
}
