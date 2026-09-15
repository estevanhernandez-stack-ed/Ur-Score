using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Labs626.UrScore.Board;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

/// <summary>
/// One panel in its own always-on-top window (spec §9.3). It shows a panel the board renders into it and
/// decides nothing: the board opens it, draws it, saves where it sits, and returns the panel when it closes.
/// </summary>
public partial class PanelPopOutWindow : Window
{
    public PanelPopOutWindow(string panelId, FrameworkElement view, PopOutRect rect)
    {
        InitializeComponent();
        ThemeService.Attach(this);

        PanelId = panelId;
        View = view;
        PanelHost.Content = view;

        Left = rect.X;
        Top = rect.Y;
        Width = rect.W;
        Height = rect.H;

        // A small tool window, never maximized or minimized: no maximize or minimize box, so a double-click on the
        // strip, Win+Up and Snap to the top edge do nothing, and any state that gets through anyway goes straight back.
        SourceInitialized += (_, _) => DropStateBoxes();
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
        };

        LocationChanged += (_, _) => Moved?.Invoke(this);
        SizeChanged += (_, _) => Moved?.Invoke(this);
    }

    public string PanelId { get; }

    /// <summary>The panel control inside, which the board renders like any other.</summary>
    public FrameworkElement View { get; }

    /// <summary>Where the window is now, in device-independent pixels; in the moment a state change is being undone, where it goes back to.</summary>
    public PopOutRect Rect => WindowState == WindowState.Normal
        ? new(Left, Top, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height)
        : new(RestoreBounds.Left, RestoreBounds.Top, RestoreBounds.Width, RestoreBounds.Height);

    /// <summary>Raised as the window moves or resizes, for every pixel; the board saves once moving settles.</summary>
    public event Action<PanelPopOutWindow>? Moved;

    public void ShowTitle(string title)
    {
        Title = title;
        PopOutTitle.Text = title;
    }

    private void OnReturnClick(object sender, RoutedEventArgs e) => Close();

    private void DropStateBoxes()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        var style = GetWindowLong(handle, StyleIndex);
        SetWindowLong(handle, StyleIndex, style & ~(MaximizeBox | MinimizeBox));
    }

    private const int StyleIndex = -16;
    private const int MaximizeBox = 0x00010000;
    private const int MinimizeBox = 0x00020000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);
}
