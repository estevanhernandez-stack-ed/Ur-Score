using System.Windows.Controls;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

public partial class RecordsPanel : UserControl
{
    public RecordsPanel() => InitializeComponent();

    public void Render(RecordsModel model) => DataContext = model;
}
