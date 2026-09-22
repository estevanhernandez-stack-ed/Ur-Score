using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Labs626.UrScore.Book;
using Labs626.UrScore.Composition;
using Microsoft.Win32;
using static Labs626.UrScore.UI.TextLines;

namespace Labs626.UrScore.UI;

/// <summary>Setup › Score book (spec §7.6).</summary>
public partial class ScoreBookPage : UserControl, ISetupPage
{
    private readonly ISetupServices _services;

    /// <summary>An export or an import is running; the two buttons wait for it, since both touch the book.</summary>
    private bool _transferring;

    public ScoreBookPage(ISetupServices services)
    {
        InitializeComponent();
        _services = services;
        Refresh();
    }

    public void Refresh()
    {
        BookFolderLine.Text = _services.Book.Root;

        ShowLine(BookPendingLine, ScoreBookModel.PendingLine(_services.Book.Pending, _services.Book.Dropped));

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
    /// Writes this PC's score book to one file for another PC to import (<see cref="BookPack"/>). The dialog offers a
    /// dated name; the line then says what went into the file, from the file's own manifest.
    /// </summary>
    private async void OnExportStatsClick(object sender, RoutedEventArgs e)
    {
        if (_transferring) return;

        var dialog = new SaveFileDialog
        {
            Title = "Export stats to a file",
            FileName = BookPack.FileName(DateTimeOffset.Now),
            Filter = "Ur Score stats (*.zip)|*.zip",
            DefaultExt = BookPack.Extension,
            AddExtension = true,
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        await TransferAsync("Writing the file…", "That file could not be written", async () =>
        {
            var manifest = await Task.Run(() => _services.ExportStats(dialog.FileName));
            // Task 8 passes the pack ExportStats built, once the preview window can show it; for now the export
            // still writes the setup (ExportStats), the line just doesn't count it yet.
            return (ScoreBookModel.ExportedLine(manifest, Path.GetFileName(dialog.FileName), null), "", false);
        });
    }

    /// <summary>
    /// Imports another PC's stats: the file Export stats made there, or, for a folder somebody copied by hand, a month
    /// file inside it. Readings are matched to this PC's sources by recipe and clan, rewritten to this PC's ids, and
    /// appended; anything already here is skipped, and a clan this PC doesn't follow is named rather than guessed at.
    /// </summary>
    private async void OnImportStatsClick(object sender, RoutedEventArgs e)
    {
        if (_transferring) return;

        var dialog = new OpenFileDialog
        {
            Title = "Import stats from another PC",
            Filter = "Ur Score stats (*.zip)|*.zip|A month file inside a copied Ur Score folder (*.jsonl)|*.jsonl",
            Multiselect = false,
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        await TransferAsync("Reading that file…", "That file could not be imported", async () =>
        {
            var outcome = await Task.Run(() => BookImport.RunFile(dialog.FileName, _services));
            return (outcome.Message, outcome.Problem, outcome.Added > 0);
        });
    }

    /// <summary>
    /// One shape for both transfers: the buttons wait, the line says what is happening, then what happened or what
    /// went wrong (the exception's message redacted, as every screen line is), and the page redraws. A reload of the
    /// book follows an import that added lines.
    /// </summary>
    private async Task TransferAsync(string doing, string failed, Func<Task<(string Said, string Problem, bool Reload)>> transfer)
    {
        _transferring = true;
        ExportStatsButton.IsEnabled = false;
        ImportStatsButton.IsEnabled = false;
        ShowLine(StatsTransferProblemLine, "");
        StatsTransferLine.Text = doing;

        try
        {
            var (said, problem, reload) = await transfer();
            StatsTransferLine.Text = said;
            ShowLine(StatsTransferProblemLine, problem);
            if (reload) await _services.ReloadBookAsync();
        }
        catch (Exception ex)
        {
            StatsTransferLine.Text = "";
            ShowLine(StatsTransferProblemLine, _services.Redactor.Redact($"{failed}: {ex.Message}"));
        }
        finally
        {
            _transferring = false;
            ExportStatsButton.IsEnabled = true;
            ImportStatsButton.IsEnabled = true;
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
            ShowLine(BookFolderProblemLine, "");
        }
        catch (Exception ex)
        {
            _services.AddTrail($"FOLDER NOT OPENED: {ex.GetType().Name}");
            ShowLine(BookFolderProblemLine, ScoreBookModel.FolderNotOpened(ex));
        }
    }
}
