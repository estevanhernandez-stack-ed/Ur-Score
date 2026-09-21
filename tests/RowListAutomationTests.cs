using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Markup;
using System.Windows.Threading;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

/// <summary>
/// The live walk of Setup › Clans found "Make K0i2 main" only in UI Automation's raw view, under an item named
/// "ClanRow { SourceId = ..., Name = K0i2, ... }". A RowList's peer must give the control view the rows' own
/// buttons and lines, with their accessible names, and never an item named by a record.
/// </summary>
public class RowListAutomationTests
{
    public sealed record TestRow(string Name, string MakeMainName, string RemoveName);

    private const string RowTemplate =
        "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>"
        + "<StackPanel Orientation='Horizontal'>"
        + "<TextBlock Text='{Binding Name}' />"
        + "<Button Content='Make main' AutomationProperties.Name='{Binding MakeMainName}' />"
        + "<Button Content='Remove' AutomationProperties.Name='{Binding RemoveName}' />"
        + "</StackPanel>"
        + "</DataTemplate>";

    [Fact]
    public void TheListIsAListNamedForItsClass()
    {
        OnSta(() =>
        {
            var peer = UIElementAutomationPeer.CreatePeerForElement(new RowList());

            Assert.IsType<RowListAutomationPeer>(peer);
            Assert.Equal(AutomationControlType.List, peer.GetAutomationControlType());
            Assert.Equal("RowList", peer.GetClassName());
        });
    }

    [Fact]
    public void EachRowsOwnControlsAreInTheControlViewWithTheirNames()
    {
        OnSta(() =>
        {
            TestRow[] rows = [new("K0i2", "Make K0i2 main", "Remove K0i2"), new("CCGP", "Make CCGP main", "Remove CCGP")];
            var list = new RowList { ItemTemplate = (DataTemplate)XamlReader.Parse(RowTemplate), ItemsSource = rows };

            // A hidden window gives the list a presentation source, so visibility is what it would be on screen.
            using var host = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("RowListAutomationTests") { Width = 600, Height = 400, WindowStyle = 0 })
            {
                RootVisual = list,
            };
            list.Measure(new Size(600, 400));
            list.Arrange(new Rect(0, 0, 600, 400));
            list.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

            var children = UIElementAutomationPeer.CreatePeerForElement(list).GetChildren() ?? [];
            var named = children.Select(c => (Type: c.GetAutomationControlType(), Name: c.GetName(), Control: c.IsControlElement())).ToList();

            Assert.Contains((AutomationControlType.Button, "Make K0i2 main", true), named);
            Assert.Contains((AutomationControlType.Button, "Remove K0i2", true), named);
            Assert.Contains((AutomationControlType.Button, "Remove CCGP", true), named);
            Assert.Contains((AutomationControlType.Text, "K0i2", true), named);
            Assert.DoesNotContain(children, c => c is ItemAutomationPeer);
            Assert.DoesNotContain(named, n => n.Name.Contains(nameof(TestRow), StringComparison.Ordinal));
        });
    }

    /// <summary>WPF elements need an STA thread; the test runner's threads are MTA.</summary>
    private static void OnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        // A Join with no timeout cannot fail, only wait. On 2026-09-17 six of the eight tests built this way
        // were stuck on one at once and the whole run hung until a human noticed (V3-S.38). A bounded wait
        // turns that into a named failing test, which is the difference between a diagnosis and a mystery.
        Assert.True(thread.Join(UiThread.Longest), $"the UI thread did not finish within {UiThread.Longest}");
        failure?.Throw();
    }
}
