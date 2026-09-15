using System.Windows.Controls;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

public partial class TopPanel : UserControl
{
    public TopPanel() => InitializeComponent();

    public void Render(TopModel model) => DataContext = model;
}
