using System.ComponentModel;
using System.Windows;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

/// <summary>
/// The plan, drawn (design 2026-09-22, §3): every recipe, clan, board and key the file holds, what importing does to
/// each, a tick on each change, and one button that applies the ticked ones. Ur Score's own window, in the theme,
/// never a stock dialog (owner rule, V3-S.10). <see cref="TickedKeys"/> is the answer; the caller applies.
/// </summary>
public partial class ImportPreviewWindow : Window
{
    private readonly SetupMergePlan _plan;
    private readonly List<ImportPreviewRow> _rows;

    public ImportPreviewWindow(SetupMergePlan plan, BookPackManifest manifest, string fileName)
    {
        InitializeComponent();
        ThemeService.Attach(this);
        _plan = plan;

        ImportFromLine.Text = $"Import from {fileName}";
        ImportIntroLine.Text = ImportPreviewModel.Intro(manifest);
        var groups = ImportPreviewModel.Groups(plan);
        _rows = groups.SelectMany(g => g.Rows).ToList();
        foreach (var row in _rows) row.PropertyChanged += OnRowChanged;
        ImportPreviewList.ItemsSource = groups;
        // A setup-less file never reaches this window (Task 8's page opens it only when opened.Setup is not null),
        // so "holds stats and no setup" can never be true here; a setup file with zero recipes just says nothing.
        ImportNothingSentLine.Text = ImportPreviewModel.NothingSent;
        ImportNothingSentLine.Visibility = plan.File.Recipes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // Always shown, unlike the line above it: the settings step runs on every import, whatever is ticked.
        ImportSettingsNoteLine.Text = ImportPreviewModel.SettingsNote;
        ImportAsideLine.Text = ImportPreviewModel.AsideNote;

        Loaded += (_, _) => ImportCancelButton.Focus();
    }

    /// <summary>The keys of every ticked, tickable change; set when Import ticked was pressed, else null.</summary>
    public IReadOnlySet<string>? TickedKeys { get; private set; }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportPreviewRow.Ticked)) ImportPreviewModel.Regrey(_rows, _plan);
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        TickedKeys = ImportPreviewModel.TickedKeys(_rows);
        DialogResult = true;
    }
}
