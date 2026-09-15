using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

/// <summary>
/// Type-to-search over an input's search list (spec §7.1): matches anywhere in the name, ignoring case,
/// top 8. Enter or a click picks. The list is handed in; this control reads nothing itself.
/// </summary>
public partial class ClanSearchBox : UserControl
{
    private IReadOnlyList<string> _names = [];

    public ClanSearchBox() => InitializeComponent();

    public event Action<string>? Picked;

    /// <summary>When the list couldn't be read, or the input has none, Enter picks the typed text as it is.</summary>
    public bool AllowTyped { get; set; }

    public void SetLabel(string label) => AutomationProperties.SetName(SearchText, label);

    public void SetNames(IReadOnlyList<string> names)
    {
        _names = names;
        UpdateMatches();
    }

    public void SetStatus(string text)
    {
        SearchStatus.Text = text;
        SearchStatus.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    public void FocusSearch() => SearchText.Focus();

    private void OnTextChanged(object sender, TextChangedEventArgs e) => UpdateMatches();

    private void UpdateMatches()
    {
        var matches = SearchLists.Match(_names, SearchText.Text);
        SearchMatches.ItemsSource = matches;
        SearchMatches.SelectedIndex = matches.Count > 0 ? 0 : -1;
        SearchMatches.Visibility = matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        var count = SearchMatches.Items.Count;

        switch (e.Key)
        {
            case Key.Down when count > 0:
                SearchMatches.SelectedIndex = Math.Min(count - 1, SearchMatches.SelectedIndex + 1);
                SearchMatches.ScrollIntoView(SearchMatches.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up when count > 0:
                SearchMatches.SelectedIndex = Math.Max(0, SearchMatches.SelectedIndex - 1);
                SearchMatches.ScrollIntoView(SearchMatches.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                if (SearchMatches.SelectedItem is string chosen) Pick(chosen);
                else if (AllowTyped && SearchText.Text.Trim() is { Length: > 0 } typed) Pick(typed);
                e.Handled = true;
                break;
            case Key.Escape when SearchText.Text.Length > 0:
                SearchText.Text = "";
                e.Handled = true;
                break;
        }
    }

    private void OnMatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: string name }) Pick(name);
    }

    private void Pick(string name)
    {
        SearchText.Text = "";
        Picked?.Invoke(name);
    }
}
