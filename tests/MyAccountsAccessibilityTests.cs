using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Linq;

namespace UrScore.Tests;

public class MyAccountsAccessibilityTests
{
    [Fact]
    public void AccountNameAnnouncesOnlyTheCurrentRowsSentState()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ur-Score.csproj"))) directory = directory.Parent;
                Assert.NotNull(directory);
                XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
                var panel = XDocument.Load(Path.Combine(directory.FullName, "src", "UI", "Panels", "MyAccountsPanel.xaml"));
                var name = Assert.Single(panel.Descendants(presentation + "TextBlock"), element => (string?)element.Attribute("Text") == "{Binding Name}");
                var markup = new XElement(presentation + "StackPanel",
                    new XAttribute(XNamespace.Xmlns + "x", xaml),
                    new XElement(presentation + "StackPanel.Resources",
                        new XElement(presentation + "Style", new XAttribute(xaml + "Key", "RowCell"), new XAttribute("TargetType", "TextBlock"))),
                    new XElement(name));
                var host = (StackPanel)XamlReader.Parse(markup.ToString());
                var text = Assert.IsType<TextBlock>(Assert.Single(host.Children.Cast<UIElement>()));
                var peer = new TextBlockAutomationPeer(text);

                foreach (var row in new[]
                {
                    new { Name = "Account One", Sent = false },
                    new { Name = "Account One", Sent = true },
                    new { Name = "Account Two", Sent = true },
                    new { Name = "Account Two", Sent = false },
                })
                {
                    host.DataContext = row;
                    host.Measure(new Size(600, 100));
                    host.Arrange(new Rect(host.DesiredSize));
                    host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                    Assert.Equal(row.Name, text.Text);
                    Assert.Equal(row.Sent ? $"{row.Name}: sent to RoRoRo in the last read" : row.Name, peer.GetName());
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
}
