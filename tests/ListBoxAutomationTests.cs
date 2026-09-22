using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Threading;

namespace UrScore.Tests;

/// <summary>
/// What UI Automation is told a ListBox item is called. Its row's controls are in the control view either way —
/// a ListBox is not an ItemsControl in that respect — but the item itself is named by its record's ToString unless
/// the container style names it, and a screen reader reading the Stats table said "Labs626.UrScore.UI.StatRow"
/// for every row (S1-L.4, found 2026-09-22). Every ListBox in src/UI now sets AutomationProperties.Name in its
/// item container style, and <c>RowListFenceTests</c> keeps it so; this is the behaviour the fence is for.
/// </summary>
[Collection(WpfCollection.Name)]
public class ListBoxAutomationTests
{
    public sealed record TestRow(string Name, string TickName);

    private const string RowTemplate =
        "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>"
        + "<StackPanel Orientation='Horizontal'>"
        + "<TextBlock Text='{Binding Name}' />"
        + "<CheckBox Content='Send' AutomationProperties.Name='{Binding TickName}' />"
        + "</StackPanel>"
        + "</DataTemplate>";

    [Fact]
    public void AnItemWithNoNamedContainerIsReadAsItsRecord() => UiThread.Run(() =>
    {
        var item = Item(namedContainer: false);

        Assert.Contains(nameof(TestRow), item.Name, StringComparison.Ordinal);
        Assert.Contains((AutomationControlType.CheckBox, "Send K0i2"), item.Controls);
    });

    [Fact]
    public void AContainerStyleThatNamesTheItemIsWhatIsRead() => UiThread.Run(() =>
    {
        var item = Item(namedContainer: true);

        Assert.Equal("K0i2", item.Name);
        Assert.True(item.IsControl);
        Assert.Contains((AutomationControlType.CheckBox, "Send K0i2"), item.Controls);
    });

    private sealed record ItemSeen(string Name, bool IsControl, List<(AutomationControlType, string)> Controls);

    /// <summary>Read while the list still has its window: a peer answers for a hidden element differently once the source is gone.</summary>
    private static ItemSeen Item(bool namedContainer)
    {
        var list = new ListBox { ItemTemplate = (DataTemplate)XamlReader.Parse(RowTemplate), ItemsSource = new[] { new TestRow("K0i2", "Send K0i2") } };
        if (namedContainer)
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(AutomationProperties.NameProperty, new Binding(nameof(TestRow.Name))));
            list.ItemContainerStyle = style;
        }

        // A hidden window gives the list a presentation source, so visibility is what it would be on screen.
        using var host = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("ListBoxAutomationTests") { Width = 600, Height = 400, WindowStyle = 0 })
        {
            RootVisual = list,
        };
        list.Measure(new Size(600, 400));
        list.Arrange(new Rect(0, 0, 600, 400));
        list.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

        var item = Assert.Single(UIElementAutomationPeer.CreatePeerForElement(list).GetChildren() ?? []);
        return new ItemSeen(item.GetName(), item.IsControlElement(), [.. (item.GetChildren() ?? []).Select(c => (c.GetAutomationControlType(), c.GetName()))]);
    }
}
