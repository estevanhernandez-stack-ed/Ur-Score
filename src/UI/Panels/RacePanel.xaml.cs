using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

public partial class RacePanel : UserControl
{
    /// <summary>Whether the standings are open. Kept on the panel, so a read's redraw doesn't shut them.</summary>
    private bool _standingsOpen;

    public RacePanel() => InitializeComponent();

    public void Render(RaceModel model)
    {
        DataContext = model;
        ShowStandings(_standingsOpen && model.Standings.Count > 0);
    }

    private void OnStandingsClick(object sender, RoutedEventArgs e) => ShowStandings(!_standingsOpen);

    private void ShowStandings(bool open)
    {
        _standingsOpen = open;
        StandingsList.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        StandingsButton.Content = open ? "Hide standings" : "Standings";
        AutomationProperties.SetName(StandingsButton, open ? "Hide the standings" : "Show the standings");
    }
}
