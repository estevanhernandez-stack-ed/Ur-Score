using System.Windows.Controls;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

public partial class PromotionCheckPanel : UserControl
{
    public PromotionCheckPanel() => InitializeComponent();

    public void Render(PromotionModel model) => DataContext = model;
}
