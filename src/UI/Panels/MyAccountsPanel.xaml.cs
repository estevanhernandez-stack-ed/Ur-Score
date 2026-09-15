using System.Windows.Controls;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

public partial class MyAccountsPanel : UserControl
{
    public MyAccountsPanel() => InitializeComponent();

    public void Render(MyAccountsModel model) => DataContext = model;
}
