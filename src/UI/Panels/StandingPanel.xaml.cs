using System.Windows.Controls;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

public partial class StandingPanel : UserControl
{
    public StandingPanel() => InitializeComponent();

    public void Render(StandingModel model) => DataContext = model;
}
