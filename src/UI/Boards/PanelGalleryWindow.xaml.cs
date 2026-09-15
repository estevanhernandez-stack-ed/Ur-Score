using System.Windows;
using Labs626.UrScore.Board;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

/// <summary>The panel gallery (spec §9.4). Picking a card closes it; the form for that type comes next.</summary>
public partial class PanelGalleryWindow : Window
{
    public PanelGalleryWindow(IReadOnlyList<GalleryCard> cards)
    {
        InitializeComponent();
        ThemeService.Attach(this);
        GalleryCards.ItemsSource = cards;
    }

    public PanelType? Picked { get; private set; }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GalleryCard { CanAdd: true } card }) return;

        Picked = card.Type;
        DialogResult = true;
    }
}
