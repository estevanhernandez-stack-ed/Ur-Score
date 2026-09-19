using System.Windows.Controls;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

/// <summary>The Pace panel. Named for the view because <see cref="PacePanel"/> is the model that fills it.</summary>
public partial class PacePanelView : UserControl
{
    public PacePanelView() => InitializeComponent();

    public void Render(PaceModel model) => DataContext = model;
}
