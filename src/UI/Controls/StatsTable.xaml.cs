using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

/// <summary>
/// The single searchable Stats table (spec §7.3), shared by the import screen and Setup › Stats. Holds no
/// HTTP: the one read it can ask for goes through the callback its host supplies.
/// </summary>
public partial class StatsTable : UserControl
{
    private Recipe? _recipe;
    private RecipeState _existing = new();
    private IReadOnlyList<InstalledRecipe> _installed = [];
    private Func<IReadOnlyCollection<Guid>> _accountIds = () => [];
    private Func<string, string> _ruleSentence = _ => "";
    private Func<CancellationToken, Task<CounterLookup>>? _readNames;
    private IReadOnlyList<string> _extraRefusals = [];
    private IReadOnlyList<StatRow> _rows = [];
    private List<string> _counterNames = [];

    /// <summary>Why the last Send tick was undone, kept until the next change.</summary>
    private string? _budgetRefusal;

    private bool _reverting;
    private bool _reading;

    public StatsTable() => InitializeComponent();

    /// <summary>Raised after any tick, name or refusal change, so the host can enable its button.</summary>
    public event EventHandler? Changed;

    public IReadOnlyDictionary<string, StatChoice> Choices => StatsTableModel.Choices(_existing.StatChoices, _rows);

    public IReadOnlyList<string> CounterNames => _counterNames;

    public bool AnyTicked => StatsTableModel.AnyTicked(_rows);

    public bool HasSavedNames => _counterNames.Count > 0;

    /// <param name="accountIds">Asked each time the budget is worked out, so accounts listed later are counted.</param>
    /// <param name="readNames">The one read of counter names, or null when this host offers none.</param>
    /// <param name="extraRefusals">The host's own refusals, shown first in the refusal line.</param>
    public void Load(
        Recipe recipe, RecipeState existing, IReadOnlyList<InstalledRecipe> installed,
        Func<IReadOnlyCollection<Guid>> accountIds, Func<string, string> ruleSentence,
        Func<CancellationToken, Task<CounterLookup>>? readNames, string readNamesLabel,
        IReadOnlyList<string>? extraRefusals = null)
    {
        _recipe = recipe;
        _existing = existing;
        _installed = installed;
        _accountIds = accountIds;
        _ruleSentence = ruleSentence;
        _readNames = readNames;
        _extraRefusals = extraRefusals ?? [];
        _budgetRefusal = null;
        _counterNames = [.. existing.SavedCounterNames];

        ReadNamesButton.Content = readNamesLabel;
        AutomationProperties.SetName(ReadNamesButton, readNamesLabel);
        CounterPanel.Visibility = recipe.LastStep.Counters is null || readNames is null ? Visibility.Collapsed : Visibility.Visible;
        Show(NamesLine, StatsTableModel.NamesLine(recipe, _counterNames.Count));

        StatsSearchBox.Text = "";
        Rebuild(existing.StatChoices);
    }

    /// <summary>Reads counter names once and rebuilds the rows, keeping every tick made so far.</summary>
    public async Task ReadNamesAsync(CancellationToken cancellationToken)
    {
        if (_recipe is null || _readNames is null || _reading) return;

        var recipe = _recipe;
        _reading = true;
        ReadNamesButton.IsEnabled = false;
        Show(NamesLine, StatsTableModel.ReadingNamesLine(recipe));

        try
        {
            var found = await _readNames(cancellationToken);
            if (!ReferenceEquals(recipe, _recipe)) return;

            if (found.Names.Count > 0)
            {
                _counterNames = [.. found.Names];
                Rebuild(Choices);
            }

            Show(NamesLine, found.Names.Count > 0
                ? StatsTableModel.FoundNamesLine(found.Names.Count)
                : found.Problem ?? "No statistic names came back.");
            Refresh();
        }
        catch (OperationCanceledException)
        {
            // The page closed; nothing to show.
        }
        catch (Exception ex)
        {
            Show(NamesLine, $"Could not read them ({ex.GetType().Name}).");
        }
        finally
        {
            _reading = false;
            ReadNamesButton.IsEnabled = true;
        }
    }

    public IReadOnlyList<string> SaveProblems() =>
        _recipe is null ? [] : StatsTableModel.SaveProblems(_recipe, _existing, _installed, _accountIds(), _rows);

    /// <summary>Stays on screen until the next change, so a collided name can be fixed right here.</summary>
    public void ShowProblems(IReadOnlyList<string> problems) => Show(RefusalLine, string.Join(Environment.NewLine, problems));

    private async void OnReadNamesClick(object sender, RoutedEventArgs e) => await ReadNamesAsync(CancellationToken.None);

    private void Rebuild(IReadOnlyDictionary<string, StatChoice> choices)
    {
        foreach (var row in _rows) row.PropertyChanged -= OnRowChanged;
        _rows = StatsTableModel.Build(_recipe!, choices, _counterNames);
        foreach (var row in _rows) row.PropertyChanged += OnRowChanged;

        ApplyFilter();
        Refresh();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_recipe is not null) ApplyFilter();
    }

    /// <summary>Filters only when the search changes, never on a tick, so a row never jumps out from under the pointer.</summary>
    private void ApplyFilter()
    {
        var visible = StatsTableModel.Visible(_rows, StatsSearchBox.Text);
        StatsRows.ItemsSource = visible;
        Show(ShowingLine, StatsTableModel.ShowingLine(visible.Count, _rows.Count, StatsSearchBox.Text));
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(StatRow.Show) or nameof(StatRow.Send) or nameof(StatRow.MetricId))) return;

        if (!_reverting)
        {
            _budgetRefusal = null;

            // Stats design §5.3: a Send tick that would pass RoRoRo's limit is undone, and says why.
            if (sender is StatRow { Send: true } row && e.PropertyName == nameof(StatRow.Send) && _recipe is not null
                && StatsTableModel.Budget(_recipe, _existing, _installed, _accountIds(), _rows) is { Allowed: false } refused)
            {
                _budgetRefusal = refused.Line;
                Dispatcher.BeginInvoke(() =>
                {
                    _reverting = true;
                    row.Send = false;
                    _reverting = false;
                    Refresh();
                });
            }
        }

        Refresh();
    }

    private void Refresh()
    {
        if (_recipe is null) return;

        Show(SlotLine, StatsTableModel.Budget(_recipe, _existing, _installed, _accountIds(), _rows).Line);
        Show(RuleLine, StatsTableModel.RuleLines(_rows, _ruleSentence));
        Show(RefusalLine, string.Join(Environment.NewLine, StatsTableModel.Refusals(_extraRefusals, _rows, _budgetRefusal)));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>An empty line takes no space.</summary>
    private static void Show(TextBlock line, string text)
    {
        line.Text = text;
        line.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
