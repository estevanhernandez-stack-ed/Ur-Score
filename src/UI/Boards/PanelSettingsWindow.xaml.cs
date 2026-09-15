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
    private readonly FormValues? _saved;
    private readonly bool _adding;

    /// <summary>A saved value a shown list no longer offers, by field: checked as the form's value until you pick another (<see cref="PanelForms.Pick"/>).</summary>
    private readonly Dictionary<PanelField, string> _held = [];

    private IReadOnlyList<PanelField> _fields;
    private bool _filling;

    public PanelSettingsWindow(PanelType type, string title, LiveBoard live, PanelSettings? current, bool adding)
    {
        InitializeComponent();
        ThemeService.Attach(this);

        _type = type;
        _live = live;
        _current = current;
        _saved = current is null ? null : PanelForms.From(current);
        _adding = adding;
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

        Fill(_saved ?? PanelForms.Defaults(type, live));
        Check();
    }

    /// <summary>The settings to save, once the form was saved with no problem.</summary>
    public PanelSettings? Result { get; private set; }

    private Visibility Shows(PanelField field) => _fields.Contains(field) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Refills every list from the values, keeping each pick while it is still offered. A field the form doesn't show
    /// falls back to its first choice; a saved value a shown field no longer offers is held and checked, never blanked
    /// into a default (<see cref="PanelForms.Pick"/>).
    /// </summary>
    private void Fill(FormValues values)
    {
        _filling = true;
        try
        {
            var withSource = values;
            if (_type != PanelType.ProfileStat)
            {
                Pick(SourceBox, PanelField.Source, PanelForms.SourceChoices(_type, PanelField.Source, _live, values), values.Source);
                withSource = values with { Source = KeyOf(SourceBox, PanelField.Source) };
            }

            Pick(ToSourceBox, PanelField.ToSource, PanelForms.SourceChoices(_type, PanelField.ToSource, _live, withSource), values.ToSource);
            Pick(StatBox, PanelField.Stat, PanelForms.StatChoices(_type, _live, withSource, _current), values.Stat);
            Pick(AccountBox, PanelField.Account, PanelForms.AccountChoices(_live), values.Account);

            // Profile stat's source list follows the stat just picked, and always shows the source the panel reads: the
            // one picked while the stat's recipe has it, else the one it falls back to. Adding asks for it only when
            // that isn't the recipe's one source that is on, so what is saved is a source you saw.
            if (_type == PanelType.ProfileStat)
            {
                var withStat = values with { Stat = KeyOf(StatBox, PanelField.Stat) };
                _fields = PanelForms.Fields(_type, _adding, _live, withStat);
                SourceRow.Visibility = Shows(PanelField.Source);
                Pick(SourceBox, PanelField.Source, PanelForms.SourceChoices(_type, PanelField.Source, _live, withStat),
                    PanelForms.Build(_type, withStat, _live).SourceId);
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
        var pick = PanelForms.Pick(choices, key, _fields.Contains(field), SavedKey(field));
        box.ItemsSource = choices;
        box.SelectedItem = pick.Selected;

        if (pick.Held is { } held) _held[field] = held;
        else _held.Remove(field);
    }

    /// <summary>The field's value: its pick, else the saved value it holds.</summary>
    private string? KeyOf(ComboBox box, PanelField field) => (box.SelectedItem as FormChoice)?.Key ?? _held.GetValueOrDefault(field);

    private string? SavedKey(PanelField field) => field switch
    {
        PanelField.Source => _saved?.Source,
        PanelField.ToSource => _saved?.ToSource,
        PanelField.Stat => _saved?.Stat,
        PanelField.Account => _saved?.Account,
        _ => null,
    };

    private FormValues Values() => new(
        Source: KeyOf(SourceBox, PanelField.Source),
        Sources: RaceSources.Items.OfType<RaceChoice>().Where(c => c.Picked).Select(c => c.Key).ToList(),
        ToSource: KeyOf(ToSourceBox, PanelField.ToSource),
        Stat: KeyOf(StatBox, PanelField.Stat),
        Account: KeyOf(AccountBox, PanelField.Account));

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
