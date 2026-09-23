using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

/// <summary>Every tool a panel's header can raise (spec §9.2, §9.3, §9.4).</summary>
public enum PanelTool { PopOut, Settings, ChooseAnother, DragStart, Remove }

public sealed class PanelToolEventArgs(RoutedEvent routedEvent, PanelTool tool) : RoutedEventArgs(routedEvent)
{
    public PanelTool Tool { get; } = tool;
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

    /// <summary>The frame in a panel's view (its header), or null when it has none.</summary>
    public static PanelFrame? Of(DependencyObject view)
    {
        if (view is PanelFrame frame) return frame;

        foreach (var child in LogicalTreeHelper.GetChildren(view).OfType<DependencyObject>())
        {
            if (Of(child) is { } found) return found;
        }

        return null;
    }

    /// <summary>
    /// Gives keyboard focus to one of this panel's tools, for the board to call once it has redrawn the panel under
    /// a press (R7). False when it can't take focus.
    /// </summary>
    public bool FocusTool(PanelTool tool)
    {
        Control? target = tool switch
        {
            PanelTool.Remove => RemovePanelButton,
            PanelTool.Settings or PanelTool.ChooseAnother => PanelSettingsButton,
            PanelTool.PopOut => PopOutButton,
            _ => null,
        };

        return target?.Focus() == true;
    }

    /// <summary>
    /// Focus on the first of this panel's own buttons that can take it — ⋯, else ✕ Remove, else ⧉ — for a board that
    /// has just redrawn the panel under a keyboard move. The panel itself is a UserControl and takes no focus.
    /// </summary>
    public bool FocusFirstTool() =>
        new Control[] { PanelSettingsButton, RemovePanelButton, PopOutButton }.Any(tool => tool.IsVisible && tool.Focus());

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

        // Each of the panel's own tools says which panel it belongs to, as the popped-out slot's Bring back and
        // Remove do. A board of five panels otherwise offers five buttons called "Pop out" and five called
        // "Panel settings", which a screen reader cannot tell apart and an automation caller has to reach past.
        var title = (DataContext as PanelHead)?.Title;
        AutomationProperties.SetName(PopOutButton, BoardText.PopOutName(title));
        AutomationProperties.SetName(PanelSettingsButton, BoardText.PanelSettingsName(title));
        AutomationProperties.SetName(ChooseAnotherButton, BoardText.ChooseAnotherName(title));
        AutomationProperties.SetName(RemovePanelButton, BoardText.RemovePanelName(title));

        var arranging = editing ? Visibility.Visible : Visibility.Collapsed;
        DragHandle.Visibility = arranging;
        RemovePanelButton.Visibility = arranging;
        HeaderOutline.Visibility = arranging;

        // The header is the drag target while editing, so it says so under the pointer. Outside edit mode it is
        // an ordinary title again and must not suggest it can be moved.
        TitleRow.Cursor = editing ? Cursors.SizeAll : null;
        if (!editing)
        {
            _pressedAt = null;
            if (TitleRow.IsMouseCaptured) TitleRow.ReleaseMouseCapture();
        }
        PopOutButton.Visibility = GetShowPopOut(this) && !editing ? Visibility.Visible : Visibility.Collapsed;
        PanelSettingsButton.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        ChooseAnotherButton.Visibility = settings && DataContext is PanelHead { HasStale: true } ? Visibility.Visible : Visibility.Collapsed;

    }

    private void OnToolClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<PanelTool>(tag, out var tool))
        {
            RaiseEvent(new PanelToolEventArgs(ToolEvent, tool));
        }
    }


    /// <summary>
    /// Where the pointer went down on the header, until it moves far enough to be a drag. Null when it is not down,
    /// when the panel is not being edited, and after a drag has started.
    /// </summary>
    private Point? _pressedAt;

    /// <summary>
    /// The header drags the panel while editing, so the whole title line is the target rather than the six-dot grip
    /// alone. A press is remembered and nothing happens until the pointer passes the SYSTEM's drag threshold, so a
    /// click on the header is still a click and the buttons sitting in it keep working — they mark the press handled
    /// before it bubbles here (ButtonBase.OnMouseLeftButtonDown), so this never sees one, and a press on a resize
    /// grip is handled on its way DOWN, at the board's PreviewMouseLeftButtonDown, so this never sees that either.
    /// <para>
    /// The press captures the mouse. The title row is about twenty pixels tall, and without the capture a pointer
    /// that left it before crossing the threshold — a quick diagonal move toward the panel below, which is how a
    /// hand drags — never raised this row's MouseMove again, and nothing dragged (UIA walk, 2026-09-23).
    /// </para>
    /// </summary>
    private void OnTitleRowDown(object sender, MouseButtonEventArgs e)
    {
        if (!GetShowEditTools(this)) return;

        _pressedAt = e.GetPosition(this);
        TitleRow.CaptureMouse();
    }

    private void OnTitleRowMove(object sender, MouseEventArgs e)
    {
        if (_pressedAt is not { } from) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            // The button came up somewhere the Up never reached us (another window took it): let the mouse go.
            _pressedAt = null;
            TitleRow.ReleaseMouseCapture();
            return;
        }

        var to = e.GetPosition(this);
        if (Math.Abs(to.X - from.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(to.Y - from.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        // Cleared before the drag, not after: DoDragDrop blocks until the drag ends, and a stale press left behind
        // it would arm a second drag from the next move. The capture goes first too, because DoDragDrop runs inside
        // DragStart's handler and needs the mouse for itself.
        _pressedAt = null;
        TitleRow.ReleaseMouseCapture();
        RaiseEvent(new PanelToolEventArgs(ToolEvent, PanelTool.DragStart));
    }

    private void OnTitleRowUp(object sender, MouseButtonEventArgs e)
    {
        _pressedAt = null;
        TitleRow.ReleaseMouseCapture();
    }

    /// <summary>
    /// Capture can be taken away without a button-up reaching this row (Alt+Tab, a dialog, another capture), and a
    /// press left armed after that would start a drag from the next unrelated move over the header.
    /// </summary>
    private void OnTitleRowLostCapture(object sender, MouseEventArgs e) => _pressedAt = null;
}
