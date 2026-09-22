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
        ImportIntroLine.Text = ImportPreviewModel.Intro(manifest, fileName);
        var groups = ImportPreviewModel.Groups(plan);
        _rows = groups.SelectMany(g => g.Rows).ToList();
        foreach (var row in _rows) row.PropertyChanged += OnRowChanged;
        ImportPreviewList.ItemsSource = groups;
        ImportNothingSentLine.Text = plan.File.Recipes.Count > 0 ? ImportPreviewModel.NothingSent : ImportPreviewModel.StatsOnly;
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
