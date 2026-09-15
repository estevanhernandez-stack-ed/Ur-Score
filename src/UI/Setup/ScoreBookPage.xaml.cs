using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Labs626.UrScore.Composition;

namespace Labs626.UrScore.UI;

/// <summary>Setup › Score book (spec §7.6).</summary>
public partial class ScoreBookPage : UserControl, ISetupPage
{
    private readonly ISetupServices _services;

    public ScoreBookPage(ISetupServices services)
    {
        InitializeComponent();
        _services = services;
        Refresh();
    }

    public void Refresh()
    {
        BookFolderLine.Text = _services.Book.Root;

        var pending = _services.Book.Pending;
        var dropped = _services.Book.Dropped;
        Show(BookPendingLine, pending == 0 && dropped == 0
            ? ""
            : $"{pending} lines are waiting to be written, and {dropped} readings were dropped because the file couldn't be written.");

        BookLoadingLine.Visibility = _services.ReaderLoaded ? Visibility.Collapsed : Visibility.Visible;
        BookRecipesList.ItemsSource = _services.ReaderLoaded
            ? ScoreBookModel.Recipes(_services.Installed, _services.Sources, _services.Reader)
            : [];

        var everListed = _services.AccountsCache.SavedAt() is not null || _services.Accounts.Last is { Accounts.Count: > 0 };
        var items = ScoreBookModel.NotRecording(_services.Installed, _services.Sources, _services.Latest, _services.Running, everListed);
        NotRecordingList.ItemsSource = items;
        AllRecordingLine.Visibility = items.Count == 0 && _services.Sources.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_services.Book.Root);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_services.Book.Root}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Show(BookPendingLine, $"Could not open the folder: {ex.Message}");
        }
    }

    private static void Show(TextBlock line, string text)
    {
        line.Text = text;
        line.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
