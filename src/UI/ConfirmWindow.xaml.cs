using System.Windows;
using System.Windows.Automation;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

/// <summary>
/// The one confirmation Ur Score asks with, in RoRoRo's theme (owner rule, backlog V3-S.10). Every destructive
/// question goes through here rather than a stock Windows box, which paints itself grey in the middle of a dark
/// app. Something that has already gone wrong is not a question and never comes here: it is said in place, on the
/// page or the line the click came from, the way Setup › Alerts says a failed write on the card.
/// </summary>
public partial class ConfirmWindow : Window
{
    public ConfirmWindow(Confirm confirm)
    {
        InitializeComponent();
        ThemeService.Attach(this);

        Title = confirm.Title;
        QuestionLine.Text = confirm.Question;
        DoButton.Content = confirm.DoText;
        AutomationProperties.SetName(DoButton, confirm.DoName);
        CancelButton.Content = Confirm.CancelText;

        // Keyboard lands on the answer that does nothing, so Enter alone can't delete anything.
        Loaded += (_, _) => CancelButton.Focus();
    }

    /// <summary>
    /// Asks, and answers true only when the button that acts was pressed. Cancel, Escape and the close button are
    /// all no, so anything short of a deliberate yes leaves everything as it was.
    /// </summary>
    public static bool Ask(Window? owner, Confirm confirm)
    {
        var window = new ConfirmWindow(confirm);
        if (owner is null)
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        else
        {
            window.Owner = owner;
        }

        return window.ShowDialog() == true;
    }

    private void OnDoClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
