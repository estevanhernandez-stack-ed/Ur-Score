using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Labs626.UrScore.Board;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

/// <summary>One source a race can draw, ticked or not.</summary>
public sealed class RaceChoice(string key, string label, bool picked)
{
    public string Key { get; } = key;

    public string Label { get; } = label;

    public bool Picked { get; set; } = picked;
}

/// <summary>
/// Adding a panel, and ⋯ settings (spec §9.4). The form's fields, choices, defaults and problems all come from
/// <see cref="PanelForms"/>; this window only shows them. Fields the form doesn't ask for keep their defaults.
/// </summary>
public partial class PanelSettingsWindow : Window
{
    private readonly PanelType _type;
    private readonly LiveBoard _live;
    private readonly PanelSettings? _current;
    private readonly IReadOnlyList<PanelField> _fields;
    private bool _filling;

    public PanelSettingsWindow(PanelType type, string title, LiveBoard live, PanelSettings? current, bool adding)
    {
        InitializeComponent();
        ThemeService.Attach(this);

        _type = type;
        _live = live;
        _current = current;
        _fields = PanelForms.Fields(type, adding);

        Title = adding ? $"Add {title}" : "Panel settings";
        FormTitle.Text = title;
        SaveSettingsButton.Content = adding ? "Add panel" : "Save";

        var (group, groups) = PanelForms.GroupWords(live);
        var sourceWord = type switch
        {
            PanelType.PromotionCheck => "from",
            PanelType.Top or PanelType.ProfileStat => "source",
            _ => group,
        };
        SourceLabel.Text = sourceWord.ToUpperInvariant();
        AutomationProperties.SetName(SourceBox, RecipeWords.Capital(sourceWord));
        SourcesLabel.Text = $"{groups.ToUpperInvariant()} TO RACE (2 TO {PanelModels.MaxRace})";

        SourceRow.Visibility = Shows(PanelField.Source);
        SourcesRow.Visibility = Shows(PanelField.Sources);
        ToSourceRow.Visibility = Shows(PanelField.ToSource);
        StatRow.Visibility = Shows(PanelField.Stat);
        AccountRow.Visibility = Shows(PanelField.Account);

        Fill(current is null ? PanelForms.Defaults(type, live) : PanelForms.From(current));
        Check();
    }

    /// <summary>The settings to save, once the form was saved with no problem.</summary>
    public PanelSettings? Result { get; private set; }

    private Visibility Shows(PanelField field) => _fields.Contains(field) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Refills every list from the values, keeping each pick while it is still offered. A field the form doesn't show falls back to its first choice.</summary>
    private void Fill(FormValues values)
    {
        _filling = true;
        try
        {
            Pick(SourceBox, PanelField.Source, PanelForms.SourceChoices(_type, PanelField.Source, _live, values), values.Source);
            var withSource = values with { Source = (SourceBox.SelectedItem as FormChoice)?.Key };

            Pick(ToSourceBox, PanelField.ToSource, PanelForms.SourceChoices(_type, PanelField.ToSource, _live, withSource), values.ToSource);
            Pick(StatBox, PanelField.Stat, PanelForms.StatChoices(_type, _live, withSource, _current), values.Stat);
            Pick(AccountBox, PanelField.Account, PanelForms.AccountChoices(_live), values.Account);

            // Profile stat's source list follows the stat just picked.
            if (_type == PanelType.ProfileStat)
            {
                var withStat = withSource with { Stat = (StatBox.SelectedItem as FormChoice)?.Key };
                Pick(SourceBox, PanelField.Source, PanelForms.SourceChoices(_type, PanelField.Source, _live, withStat), values.Source);
            }

            var picked = (values.Sources ?? []).ToHashSet(StringComparer.Ordinal);
            RaceSources.ItemsSource = PanelForms.SourceChoices(_type, PanelField.Sources, _live, values)
                .Select(c => new RaceChoice(c.Key, c.Label, picked.Contains(c.Key)))
                .ToList();
        }
        finally
        {
            _filling = false;
        }
    }

    private void Pick(ComboBox box, PanelField field, IReadOnlyList<FormChoice> choices, string? key)
    {
        box.ItemsSource = choices;
        box.SelectedItem = choices.FirstOrDefault(c => c.Key == key) ?? (_fields.Contains(field) ? null : choices.FirstOrDefault());
    }

    private FormValues Values() => new(
        Source: (SourceBox.SelectedItem as FormChoice)?.Key,
        Sources: RaceSources.Items.OfType<RaceChoice>().Where(c => c.Picked).Select(c => c.Key).ToList(),
        ToSource: (ToSourceBox.SelectedItem as FormChoice)?.Key,
        Stat: (StatBox.SelectedItem as FormChoice)?.Key,
        Account: (AccountBox.SelectedItem as FormChoice)?.Key);

    /// <summary>Shows what's wrong with the form as it stands, and returns it.</summary>
    private string? Check()
    {
        var problem = PanelForms.Problem(_type, PanelForms.Build(_type, Values(), _live), _live);
        SettingsProblemLine.Text = problem ?? "";
        SettingsProblemLine.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
        return problem;
    }

    private void OnPickChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling) return;

        Fill(Values());
        Check();
    }

    private void OnRaceChanged(object sender, RoutedEventArgs e)
    {
        if (_filling) return;

        // The tick is copied here too, so the check never runs ahead of the two-way binding.
        if (sender is CheckBox { DataContext: RaceChoice choice } box) choice.Picked = box.IsChecked == true;
        Check();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (Check() is not null) return;

        Result = PanelForms.Build(_type, Values(), _live);
        DialogResult = true;
    }
}
