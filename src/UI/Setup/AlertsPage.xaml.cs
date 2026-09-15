using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Core;

namespace Labs626.UrScore.UI;

/// <summary>
/// Setup › Alerts: a card per sent stat whose alerts read as sentences and are edited in place, then the report policy card.
/// Every decision is <see cref="AlertCards"/>'s. This page reads the rules file, writes through <see cref="RulesFile"/>, draws
/// the rows and moves keyboard focus where <see cref="AlertCards"/> says (plan A13 to A15). It opens no window.
/// </summary>
public partial class AlertsPage : UserControl, ISetupPage
{
    private readonly ISetupServices _services;
    private AlertsView _view = AlertCards.Empty;
    private AlertsUi _ui = AlertsUi.Closed;
    private bool _drawn;

    public AlertsPage(ISetupServices services)
    {
        InitializeComponent();
        _services = services;
        Refresh();
    }

    public void Refresh()
    {
        var policies = AlertsModel.Policies(_services.Installed, _services.KnownAccounts, _services.Sources, _services.Settings.ResolveNames, _services.PolicyCounts);
        PolicyList.ItemsSource = policies;
        PolicyEmptyLine.Visibility = policies.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // A14: a read or a book line never redraws the cards under an open editor, and a redraw that changes nothing is skipped.
        if (_ui.Mode != AlertEditMode.None) return;

        var view = Read();
        if (_drawn && AlertCards.Same(_view, view))
        {
            DrawLines();
            return;
        }

        _view = view;
        Draw("");
    }

    private AlertsView Read() => AlertCards.Build(_services.Installed, RulesFile.Read(_services.RulesPath));

    private void Draw(string focus)
    {
        _drawn = true;
        AlertCardList.ItemsSource = AlertCards.Rows(_view, _ui);
        DrawLines();
        if (focus.Length > 0) _ = Dispatcher.InvokeAsync(() => FocusNamed(AlertCardList, focus), DispatcherPriority.Loaded);
    }

    private void DrawLines()
    {
        Show(AlertsEmptyLine, AlertCards.EmptyLine(_services.Installed, _view));
        Show(AlertsResultLine, AlertCards.OrphanResult(_view, _ui));
        AlertsNextLine.Visibility = _view.ShowNext ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string metricId) return;

        _view = Read();
        _ui = AlertCards.OpenAdd(metricId);
        Draw(AlertCards.FocusName(_view, metricId, _ui, AlertKind.Rate));
    }

    private void OnKindClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AlertTarget target) return;

        _ui = AlertCards.ChooseKind(target);
        Draw(AlertCards.FocusName(_view, target.MetricId, _ui, target.Kind));
    }

    private void OnChangeClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AlertTarget target || AlertCards.Managed(_view, target) is not { } line) return;

        _ui = AlertCards.OpenChange(line);
        Draw(AlertCards.FocusName(_view, target.MetricId, _ui, target.Kind));
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Cancel();

    private void Cancel()
    {
        var open = _ui;
        _ui = AlertsUi.Closed;
        _view = Read();
        Draw(AlertCards.FocusAfterCancel(_view, open));
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => Confirm();

    private void Confirm()
    {
        if (_ui.MetricId is not { } metricId || _ui.Draft is not { } draft || AlertCards.CardFor(_view, metricId) is not { } card) return;

        var (spec, problem) = AlertCards.Check(_ui.Kind, draft, card.Stat.Label);
        if (spec is null)
        {
            _ui = _ui with { Problem = problem };
            Draw(AlertCards.NumberName(card.Stat.Label));
            return;
        }

        var outcome = _ui.Mode == AlertEditMode.Changing
            ? RulesFile.Change(_services.RulesPath, metricId, spec)
            : RulesFile.TurnOn(_services.RulesPath, metricId, spec);
        _ui = AlertCards.AfterWrite(_ui, outcome, spec);
        _view = Read();
        Draw(AlertCards.FocusName(_view, metricId, _ui, spec.Kind));
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AlertTarget target
            || AlertCards.CardFor(_view, target.MetricId) is not { } card
            || AlertCards.Managed(_view, target) is not { } line)
        {
            return;
        }

        var outcome = RulesFile.Remove(_services.RulesPath, target.MetricId, target.Kind);
        _ui = AlertCards.AfterRemove(target, line, card.Stat.Label, outcome);
        _view = Read();
        Draw(AlertCards.FocusName(_view, target.MetricId, _ui, target.Kind));
    }

    /// <summary>Enter in the sentence does Turn on or Save; Escape cancels (A15). An open drop-down handles both keys itself first.</summary>
    private void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Confirm();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Cancel();
        }
    }

    // The boxes' selections are bound one way and copied into the draft here, so a box that is being rebuilt can never
    // push an empty choice back into what you picked.
    private void OnMinutesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { DataContext: AlertCardRow { Draft: { } draft }, SelectedItem: string minutes }) draft.Minutes = minutes;
    }

    private void OnDirectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { DataContext: AlertCardRow { Draft: { } draft }, SelectedItem: string direction }) draft.Direction = direction;
    }

    /// <summary>Moves keyboard focus to the visible control with this accessible name, selecting a number box's text (A15).</summary>
    private static bool FocusNamed(DependencyObject parent, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Control { IsVisible: true, Focusable: true } control && AutomationProperties.GetName(control) == name)
            {
                control.Focus();
                if (control is TextBox box) box.SelectAll();
                return true;
            }

            if (FocusNamed(child, name)) return true;
        }

        return false;
    }

    private static void Show(TextBlock line, string text)
    {
        line.Text = text;
        line.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
