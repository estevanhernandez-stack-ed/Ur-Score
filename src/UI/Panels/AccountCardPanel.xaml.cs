using System.Windows.Controls;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

public partial class AccountCardPanel : UserControl
{
    public AccountCardPanel() => InitializeComponent();

    public void Render(AccountCardModel model) => DataContext = model;
}
