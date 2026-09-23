using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Labs626.UrScore.Book;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Core;
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
            // The same pack ExportStats just wrote alongside the book, built again here for the line's counts —
            // recipes, clans and boards — so it matches what the other PC's preview will offer.
            var setup = SetupPack.FromHere(_services.Installed, _services.Sources, _services.SavedBoards, _services.Settings, _services.KnownAccounts);
            return (ScoreBookModel.ExportedLine(manifest, Path.GetFileName(dialog.FileName), setup), "", false);
        });
    }

    /// <summary>
    /// Imports another PC's stats: the file Export stats made there, or, for a folder somebody copied by hand, a month
    /// file inside it. A stats file from 0.5.5, or one with no setup, merges its readings as before. One with a setup
    /// (0.5.6 on) opens a preview first (<see cref="ImportPreviewWindow"/>): nothing is written until it is ticked and
    /// confirmed, then <see cref="SetupMerge.Apply"/> writes the setup and the stats merge follows, in that order.
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
            if (!dialog.FileName.EndsWith(BookPack.Extension, StringComparison.OrdinalIgnoreCase))
            {
                var outcome = await Task.Run(() => BookImport.RunFile(dialog.FileName, _services));
                return (outcome.Message, outcome.Problem, outcome.Added > 0);
            }

            var opened = await Task.Run(() => BookPack.Open(dialog.FileName));
            try
            {
                if (opened.Folder is null) return ("", opened.Problem, false);

                var fileName = Path.GetFileName(dialog.FileName);
                if (opened.Setup is null)
                {
                    // A 0.5.5 file, or a stats-only export: the stats merge as before, no preview needed.
                    var statsOnly = await Task.Run(() => BookImport.Run(opened.Folder, _services));
                    return (statsOnly.Message, statsOnly.Problem, statsOnly.Added > 0);
                }

                var plan = SetupMerge.Plan(opened.Setup, _services.SetupWriter.Here, opened.Manifest!.Readings, opened.Manifest.Finals);
                var preview = new ImportPreviewWindow(plan, opened.Manifest, fileName) { Owner = Window.GetWindow(this) };
                if (preview.ShowDialog() != true || preview.TickedKeys is not { } ticked) return ("Nothing imported.", "", false);

                // Apply runs synchronously on this (UI) thread, but it writes files and can take a moment; the busy
                // line said "Reading that file…" until now, which is stale the instant the yes was clicked.
                StatsTransferLine.Text = "Importing that setup…";
                var applied = SetupMerge.Apply(plan, ticked, _services.SetupWriter, DateTimeOffset.Now);
                if (applied.FailedStep is not null)
                {
                    _services.AddTrail($"SETUP NOT IMPORTED AT {applied.FailedStep.ToUpperInvariant()}: {applied.FailureType}");
                    // A failed step is the page's "something went wrong" case: it belongs on the problem line, not
                    // the muted said-line, same as every other failure this page reports.
                    return ("", ImportPreviewModel.AfterLine(applied, new BookImportOutcome(0, "")), false);
                }

                var stats = ticked.Contains("stats")
                    ? await Task.Run(() => BookImport.Run(opened.Folder, _services))
                    : new BookImportOutcome(0, "");
                return (ImportPreviewModel.AfterLine(applied, stats), stats.Problem, stats.Added > 0);
            }
            finally
            {
                BookPack.Discard(opened);
            }
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
