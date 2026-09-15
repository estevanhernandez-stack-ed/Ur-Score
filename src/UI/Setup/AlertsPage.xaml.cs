using System.Windows;
using System.Windows.Controls;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Core;

namespace Labs626.UrScore.UI;

/// <summary>Setup › Alerts (spec §7.5): the rule helper and the report policy, as the main window had them.</summary>
public partial class AlertsPage : UserControl, ISetupPage
{
    private readonly ISetupServices _services;
    private IReadOnlyList<RuleChoice> _choices = [];

    public AlertsPage(ISetupServices services)
    {
        InitializeComponent();
        _services = services;
        Refresh();
    }

    private string? RuleMetricId => (RuleStatBox.SelectedItem as RuleChoice)?.MetricId;

    public void Refresh()
    {
        var choices = AlertsModel.Choices(_services.Installed);
        if (!choices.SequenceEqual(_choices))
        {
            var picked = RuleMetricId;
            _choices = choices;
            RuleStatBox.ItemsSource = choices;
            RuleStatBox.SelectedItem = choices.FirstOrDefault(c => c.MetricId == picked) ?? choices.FirstOrDefault();
        }

        RuleStatBox.IsEnabled = choices.Count > 0;
        RenderRule();

        var policies = AlertsModel.Policies(_services.Installed, _services.KnownAccounts, _services.Sources, _services.Settings.ResolveNames, _services.PolicyCounts);
        PolicyList.ItemsSource = policies;
        PolicyEmptyLine.Visibility = policies.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRuleStatChanged(object sender, SelectionChangedEventArgs e) => RenderRule();

    private void RenderRule()
    {
        if (_services.Installed.Count == 0)
        {
            RuleLine.Text = AlertsModel.NoRecipe;
            AddRuleButton.IsEnabled = false;
            RulePreview.Text = "";
            return;
        }

        if (RuleMetricId is not { } metricId)
        {
            RuleLine.Text = AlertsModel.NoSentStat;
            AddRuleButton.IsEnabled = false;
            RulePreview.Text = "";
            return;
        }

        var (text, canAdd) = AlertsModel.RuleSentence(metricId);
        RuleLine.Text = text;
        AddRuleButton.IsEnabled = canAdd;
        RulePreview.Text = AlertsModel.Preview(canAdd);
    }

    private void OnAddRuleClick(object sender, RoutedEventArgs e)
    {
        if (RuleMetricId is not { } metricId) return;
        var owner = Window.GetWindow(this)!;

        var preview = RulesFile.Preview(metricId, AlertsModel.DefaultThreshold, AlertsModel.DefaultWindowMinutes);
        var answer = MessageBox.Show(owner,
            $"Add this rule to RoRoRo's metric-rules.json?\n\n{preview}\n\n"
            + "Your existing rules are kept, and the file is backed up first.",
            "Ur Score", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);

        if (answer != MessageBoxResult.OK) return;

        try
        {
            if (RulesFile.AddRule(null, metricId, AlertsModel.DefaultThreshold, AlertsModel.DefaultWindowMinutes))
            {
                RuleInventory.Record(metricId, AlertsModel.DefaultThreshold);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"Could not add the rule: {ex.Message}", "Ur Score", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        RenderRule();
    }
}
