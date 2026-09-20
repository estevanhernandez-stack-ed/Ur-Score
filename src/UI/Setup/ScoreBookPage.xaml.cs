using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Labs626.UrScore.Book;
using Labs626.UrScore.Composition;
using Microsoft.Win32;

namespace Labs626.UrScore.UI;

/// <summary>Setup › Score book (spec §7.6).</summary>
public partial class ScoreBookPage : UserControl, ISetupPage
{
    private readonly ISetupServices _services;

    private bool _bringing;

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

    /// <summary>
    /// Brings another PC's book into this one. The folder picked is that machine's data folder (or its scorebook):
    /// its readings are matched to this PC's sources by recipe and clan, rewritten to this PC's ids, and appended.
    /// Anything already here is skipped, and a clan this PC doesn't follow is named rather than guessed at.
    /// </summary>
    private async void OnBringBookClick(object sender, RoutedEventArgs e)
    {
        if (_bringing) return;

        var dialog = new OpenFolderDialog
        {
            Title = "Pick the other PC's Ur Score folder",
            Multiselect = false,
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        _bringing = true;
        BringBookButton.IsEnabled = false;
        Show(BringBookProblemLine, "");
        BringBookLine.Text = "Reading that book…";

        try
        {
            var outcome = await Task.Run(() => BookImport.Run(dialog.FolderName, _services));
            BringBookLine.Text = outcome.Message;
            Show(BringBookProblemLine, outcome.Problem);
            if (outcome.Added > 0) await _services.ReloadBookAsync();
        }
        catch (Exception ex)
        {
            BringBookLine.Text = "";
            Show(BringBookProblemLine, _services.Redactor.Redact($"That book could not be brought in: {ex.Message}"));
        }
        finally
        {
            _bringing = false;
            BringBookButton.IsEnabled = true;
            Refresh();
        }
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
