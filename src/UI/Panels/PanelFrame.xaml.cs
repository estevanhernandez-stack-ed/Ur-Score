using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

/// <summary>Every tool a panel's header can raise (spec §9.2, §9.3, §9.4).</summary>
public enum PanelTool { PopOut, Settings, ChooseAnother, DragStart, MoveEarlier, MoveLater, Resize, Remove }

public sealed class PanelToolEventArgs(RoutedEvent routedEvent, PanelTool tool, PanelSize? size = null) : RoutedEventArgs(routedEvent)
{
    public PanelTool Tool { get; } = tool;

    /// <summary>The size picked, for <see cref="PanelTool.Resize"/>.</summary>
    public PanelSize? Size { get; } = size;
}

/// <summary>
/// A panel's header, bound to its <c>PanelHead</c>, with the panel's tools. The tools decide nothing: each raises
/// <see cref="ToolEvent"/>, and whoever holds the panel (the board, a pop-out) handles it. Which tools show is
/// inherited from the grid through <see cref="ShowEditToolsProperty"/>, <see cref="ShowSettingsProperty"/> and
/// <see cref="ShowPopOutProperty"/>.
/// </summary>
public partial class PanelFrame : UserControl
{
    public static readonly RoutedEvent ToolEvent = EventManager.RegisterRoutedEvent(
        "Tool", RoutingStrategy.Bubble, typeof(EventHandler<PanelToolEventArgs>), typeof(PanelFrame));

    public static readonly DependencyProperty ShowEditToolsProperty = RegisterFlag("ShowEditTools");

    public static readonly DependencyProperty ShowSettingsProperty = RegisterFlag("ShowSettings");

    public static readonly DependencyProperty ShowPopOutProperty = RegisterFlag("ShowPopOut");

    public static readonly DependencyProperty CurrentSizeProperty = DependencyProperty.RegisterAttached(
        "CurrentSize", typeof(PanelSize), typeof(PanelFrame),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits, OnToolsChanged));

    /// <summary>True while the size box is being set from the panel's size, so that isn't read as a pick.</summary>
    private bool _showingSize;

    public PanelFrame()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => UpdateTools();
        Loaded += (_, _) => UpdateTools();
    }

    public static bool GetShowEditTools(DependencyObject element) => (bool)element.GetValue(ShowEditToolsProperty);

    public static void SetShowEditTools(DependencyObject element, bool value) => element.SetValue(ShowEditToolsProperty, value);

    public static bool GetShowSettings(DependencyObject element) => (bool)element.GetValue(ShowSettingsProperty);

    public static void SetShowSettings(DependencyObject element, bool value) => element.SetValue(ShowSettingsProperty, value);

    public static bool GetShowPopOut(DependencyObject element) => (bool)element.GetValue(ShowPopOutProperty);

    public static void SetShowPopOut(DependencyObject element, bool value) => element.SetValue(ShowPopOutProperty, value);

    public static PanelSize? GetCurrentSize(DependencyObject element) => (PanelSize?)element.GetValue(CurrentSizeProperty);

    public static void SetCurrentSize(DependencyObject element, PanelSize? value) => element.SetValue(CurrentSizeProperty, value);

    private static DependencyProperty RegisterFlag(string name) => DependencyProperty.RegisterAttached(
        name, typeof(bool), typeof(PanelFrame),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, OnToolsChanged));

    private static void OnToolsChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is PanelFrame frame) frame.UpdateTools();
    }

    private void UpdateTools()
    {
        var editing = GetShowEditTools(this);
        var settings = GetShowSettings(this);

        EditTools.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        PopOutButton.Visibility = GetShowPopOut(this) && !editing ? Visibility.Visible : Visibility.Collapsed;
        PanelSettingsButton.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        ChooseAnotherButton.Visibility = settings && DataContext is PanelHead { HasStale: true } ? Visibility.Visible : Visibility.Collapsed;

        var size = GetCurrentSize(this);
        _showingSize = true;
        try
        {
            // R5: a starter's 4- or 5-wide panel shows no size picked until one is.
            SizeBox.SelectedItem = size?.Span switch
            {
                PanelSize.Small => SmallItem,
                PanelSize.Half => HalfItem,
                PanelSize.Wide => WideItem,
                _ => null,
            };
            TallBox.IsChecked = size?.Tall == true;
        }
        finally
        {
            _showingSize = false;
        }
    }

    private void OnToolClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<PanelTool>(tag, out var tool))
        {
            RaiseEvent(new PanelToolEventArgs(ToolEvent, tool));
        }
    }

    private void OnSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        e.Handled = true;
        if (_showingSize || SizeBox.SelectedItem is not ComboBoxItem item) return;

        var span = ReferenceEquals(item, SmallItem) ? PanelSize.Small : ReferenceEquals(item, HalfItem) ? PanelSize.Half : PanelSize.Wide;
        RaiseEvent(new PanelToolEventArgs(ToolEvent, PanelTool.Resize, new PanelSize(span, TallBox.IsChecked == true)));
    }

    private void OnTallChanged(object sender, RoutedEventArgs e)
    {
        if (_showingSize) return;

        var span = GetCurrentSize(this)?.Span ?? PanelSize.Half;
        RaiseEvent(new PanelToolEventArgs(ToolEvent, PanelTool.Resize, new PanelSize(span, TallBox.IsChecked == true)));
    }

    private void OnDragHandleDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        RaiseEvent(new PanelToolEventArgs(ToolEvent, PanelTool.DragStart));
    }
}
