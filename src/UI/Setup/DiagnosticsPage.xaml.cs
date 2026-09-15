using System.Windows;
using System.Windows.Controls;
using Labs626.UrScore.Composition;

namespace Labs626.UrScore.UI;

/// <summary>Setup › Diagnostics (spec §7.7).</summary>
public partial class DiagnosticsPage : UserControl, ISetupPage
{
    private readonly ISetupServices _services;
    private IReadOnlyList<SourceDiagnostic> _rows = [];

    public DiagnosticsPage(ISetupServices services)
    {
        InitializeComponent();
        _services = services;
        Refresh();
    }

    public void Refresh()
    {
        _rows = DiagnosticsModel.Sources(_services.Installed, _services.Sources, _services.Latest, _services.LastReadAt,
            _services.Running, _services.KnownAccounts, DateTimeOffset.UtcNow, _services.Redactor);
        SourcesDiagnostics.ItemsSource = _rows;
        DiagnosticsEmptyLine.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        Refresh();
        var text = DiagnosticsModel.CopyText(DateTimeOffset.UtcNow, _services.Installed, _services.Sources, _rows,
            _services.Settings.ResolveNames, _services.HostText, _services.Book.Root,
            _services.Book.Pending, _services.Book.Dropped, _services.Trail, _services.Redactor);

        try
        {
            Clipboard.SetText(text);
            DiagnosticsLine.Text = "Diagnostics copied to the clipboard.";
        }
        catch (Exception ex)
        {
            DiagnosticsLine.Text = $"Could not copy diagnostics: {ex.Message}";
        }
    }
}
