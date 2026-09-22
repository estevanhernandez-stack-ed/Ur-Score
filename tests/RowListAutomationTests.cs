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
[Collection(WpfCollection.Name)]
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

    /// <summary>
    /// The test above run against the bug's own shape, which it never had been (S1-L.2): the same rows in a stock
    /// ItemsControl. What this WPF gives for one is not quite what the fence's first sentence said — the row's
    /// controls ARE in the control view, nested under an item that is named by the record's ToString — and the
    /// RowList test discriminates on exactly that: its assertions want the buttons as direct children with no
    /// item peer between and no record name anywhere, and here every one of those three fails. So the test can
    /// catch the bug coming back by another route, which was the question.
    /// </summary>
    [Fact]
    public void AStockItemsControlIsWhatTheRowListTestWouldCatch()
    {
        OnSta(() =>
        {
            TestRow[] rows = [new("K0i2", "Make K0i2 main", "Remove K0i2")];
            var list = new System.Windows.Controls.ItemsControl { ItemTemplate = (DataTemplate)XamlReader.Parse(RowTemplate), ItemsSource = rows };

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

            var item = Assert.Single(children);
            Assert.IsAssignableFrom<ItemAutomationPeer>(item);
            Assert.Contains(nameof(TestRow), item.GetName(), StringComparison.Ordinal);
            Assert.DoesNotContain((AutomationControlType.Button, "Make K0i2 main", true), named);
            Assert.Contains(item.GetChildren() ?? [], c => c.GetAutomationControlType() == AutomationControlType.Button && c.GetName() == "Make K0i2 main");
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
