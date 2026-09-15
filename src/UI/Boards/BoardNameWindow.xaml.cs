using System.Windows;
using Labs626.UrScore.Board;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

/// <summary>Rename, from a tab's right-click menu (spec §9.2).</summary>
public partial class BoardNameWindow : Window
{
    public BoardNameWindow(string name)
    {
        InitializeComponent();
        ThemeService.Attach(this);

        BoardName = name;
        BoardNameBox.Text = name;
        BoardNameBox.MaxLength = BoardDefs.MaxNameLength;
        Loaded += (_, _) =>
        {
            BoardNameBox.Focus();
            BoardNameBox.SelectAll();
        };
    }

    public string BoardName { get; private set; }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (BoardDefs.CleanName(BoardNameBox.Text) is not { } name)
        {
            NameProblemLine.Text = "Type a name for this board.";
            NameProblemLine.Visibility = Visibility.Visible;
            return;
        }

        BoardName = name;
        DialogResult = true;
    }
}
