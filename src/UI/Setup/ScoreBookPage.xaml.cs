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

        Show(BookPendingLine, ScoreBookModel.PendingLine(_services.Book.Pending, _services.Book.Dropped));

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
        // On its own line, which Refresh never touches, so the next read's redraw doesn't wipe it; the next click that works
        // takes it away (backlog S1-12.4).
        try
        {
            Directory.CreateDirectory(_services.Book.Root);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_services.Book.Root}\"") { UseShellExecute = true });
            Show(BookFolderProblemLine, "");
        }
        catch (Exception ex)
        {
            _services.AddTrail($"FOLDER NOT OPENED: {ex.GetType().Name}");
            Show(BookFolderProblemLine, ScoreBookModel.FolderNotOpened(ex));
        }
    }

    private static void Show(TextBlock line, string text)
    {
        line.Text = text;
        line.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
