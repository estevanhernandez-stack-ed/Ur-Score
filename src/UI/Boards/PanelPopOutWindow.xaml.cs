using System.Windows;
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

        LocationChanged += (_, _) => Moved?.Invoke(this);
        SizeChanged += (_, _) => Moved?.Invoke(this);
    }

    public string PanelId { get; }

    /// <summary>The panel control inside, which the board renders like any other.</summary>
    public FrameworkElement View { get; }

    /// <summary>Where the window is now, in device-independent pixels.</summary>
    public PopOutRect Rect => new(Left, Top, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height);

    /// <summary>Raised as the window moves or resizes, for every pixel; the board saves once moving settles.</summary>
    public event Action<PanelPopOutWindow>? Moved;

    public void ShowTitle(string title)
    {
        Title = title;
        PopOutTitle.Text = title;
    }

    private void OnReturnClick(object sender, RoutedEventArgs e) => Close();
}
