using System.Windows.Controls;
using System.Windows.Data;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

/// <summary>Every row live, a column per shown stat (bound by position: a stat key isn't a binding path), and a Yours column.</summary>
public partial class LiveLeaderboardPanel : UserControl
{
    private IReadOnlyList<string> _columns = [];

    public LiveLeaderboardPanel() => InitializeComponent();

    public void Render(LeaderboardModel model)
    {
        if (!model.Columns.SequenceEqual(_columns))
        {
            while (LeaderGrid.Columns.Count > 2) LeaderGrid.Columns.RemoveAt(2);

            for (var index = 0; index < model.Columns.Count; index++)
            {
                LeaderGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = model.Columns[index],
                    Binding = new Binding($"Cells[{index}]"),
                    Width = new DataGridLength(120),
                });
            }

            LeaderGrid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "Yours",
                Binding = new Binding(nameof(LeaderRow.Yours)),
                Width = new DataGridLength(60),
                ElementStyle = (System.Windows.Style)FindResource("GridCheckBoxDisplay"),
            });

            _columns = model.Columns;
        }

        DataContext = model;
    }
}
