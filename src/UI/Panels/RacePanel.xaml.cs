using System.Windows.Controls;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

public partial class RacePanel : UserControl
{
    public RacePanel() => InitializeComponent();

    public void Render(RaceModel model) => DataContext = model;
}
