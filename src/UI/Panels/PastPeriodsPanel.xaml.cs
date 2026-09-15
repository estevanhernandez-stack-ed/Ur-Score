using System.Windows.Controls;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

public partial class PastPeriodsPanel : UserControl
{
    public PastPeriodsPanel() => InitializeComponent();

    public void Render(PastPeriodsModel model) => DataContext = model;
}
