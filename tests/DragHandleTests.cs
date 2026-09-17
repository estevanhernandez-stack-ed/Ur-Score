using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Xml.Linq;

namespace UrScore.Tests;

public class DragHandleTests
{
    [Fact]
    public void GripIsDrawnNamedAndKeepsItsMouseHandlerWithoutAKeyboardStop()
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
                Assert.Equal("OnDragHandleDown", (string?)grip.Attribute("MouseLeftButtonDown"));
                grip.Attribute("MouseLeftButtonDown")!.Remove();
                var label = (Label)XamlReader.Parse(grip.ToString());
                var peer = Assert.IsType<LabelAutomationPeer>(UIElementAutomationPeer.CreatePeerForElement(label));
                Assert.Equal("DragHandle", peer.GetAutomationId());
                Assert.Equal("Drag to move", peer.GetName());
                Assert.Equal("Drag to move", label.ToolTip);
                Assert.False(label.Focusable);
                label.Measure(new Size(100, 100));
                Assert.InRange(label.DesiredSize.Width, 24, 40);
                Assert.InRange(label.DesiredSize.Height, 24, 40);
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
        thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
