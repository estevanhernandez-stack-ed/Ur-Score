using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

/// <summary>
/// The safety screen (spec §6.2, stats design §7.1), shown before anything a recipe describes runs:
/// every host it contacts and exactly what each receives. Also the recipe's settings: inputs, and
/// which stats are shown and sent under which names. Holds no HTTP of its own; the one read it can
/// ask for goes through the callback the main window supplies.
/// </summary>
public partial class ImportWindow : Window
{
    public sealed record HostItem(string Host, string SendsText);

    /// <summary>What a "Look up stat names" read found, or why it found nothing.</summary>
    public sealed record CounterLookup(IReadOnlyList<string> Names, string? Problem);

    public sealed class InputItem
    {
        public required string Id { get; init; }

        public required string Label { get; init; }

        public string Value { get; set; } = "";
    }

    /// <summary>One row of the Stats table. Raises change notifications so the slot line and the Import button follow every tick.</summary>
    public sealed class StatItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _show;
        private bool _send;
        private string _metricId = "";

        public required string Key { get; init; }

        public required string Label { get; init; }

        public bool Show { get => _show; set => SetField(ref _show, value); }

        public bool Send { get => _send; set => SetField(ref _send, value); }

        public string MetricId { get => _metricId; set => SetField(ref _metricId, value); }

        public string ShowName => $"Show {Label}";

        public string SendName => $"Send {Label}";

        public string MetricIdName => $"Name RoRoRo uses for {Label}";

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private readonly Recipe _recipe;
    private readonly ImportReviewResult _review;
    private readonly RecipeState _existing;
    private readonly IReadOnlyList<InstalledRecipe> _installed;
    /// <summary>Asked each time the budget is worked out, so accounts a Look up seeds are counted.</summary>
    private readonly Func<IReadOnlyCollection<Guid>> _accountIds;
    private readonly Func<string, string> _ruleSentence;
    private readonly Func<IReadOnlyDictionary<string, string>, Task<CounterLookup>>? _lookUpCounters;
    private readonly List<InputItem> _inputs;
    private readonly ObservableCollection<StatItem> _stats = [];
    private List<string> _counterNames;

    /// <summary>Why the last Send tick was undone, kept until the next change.</summary>
    private string? _budgetRefusal;

    private bool _reverting;

    public ImportWindow(
        Recipe recipe, ImportReviewResult review, UpdateComparison comparison, RecipeState? existing,
        IReadOnlyList<InstalledRecipe> installed, Func<IReadOnlyCollection<Guid>> accountIds,
        Func<string, string> ruleSentence,
        Func<IReadOnlyDictionary<string, string>, Task<CounterLookup>>? lookUpCounters,
        bool settingsOnly = false)
    {
        InitializeComponent();
        ThemeService.Attach(this);
        _recipe = recipe;
        _review = review;
        _existing = existing ?? new RecipeState();
        _installed = installed;
        _accountIds = accountIds;
        _ruleSentence = ruleSentence;
        _lookUpCounters = lookUpCounters;

        Title = settingsOnly ? "Recipe settings" : comparison.IsUpdate ? "Update recipe" : "Import recipe";
        NameLine.Text = recipe.Name;
        CreditLine.Text = recipe.Credit;
        AuthorLine.Text = recipe.Author is null
            ? "No author given."
            : $"Says it is from {recipe.Author}. This is not verified.";

        HostsList.ItemsSource = review.Hosts
            .Select(h => new HostItem(h.Host, ImportReview.SendsText(h)))
            .ToList();

        PollLine.Text = $"Asks every {recipe.EffectiveEverySeconds} seconds.";
        Show(ReusedLine, string.Join(" ", review.ReusedKeys));
        Show(ChangesLine, comparison.Changes.Count == 0
            ? ""
            : "What changed:" + string.Concat(comparison.Changes.Select(c => Environment.NewLine + "• " + c)));

        _inputs =
        [
            .. recipe.Inputs.Select(i => new InputItem
            {
                Id = i.Id,
                Label = i.Label,
                Value = _existing.InputValues.GetValueOrDefault(i.Id) ?? "",
            }),
        ];
        InputsList.ItemsSource = _inputs;

        foreach (var stat in RecipeStats.Offered(recipe, _existing.StatChoices.Keys))
        {
            AddStat(stat);
        }

        StatsList.ItemsSource = _stats;

        _counterNames = [.. _existing.SavedCounterNames];
        CounterPanel.Visibility = recipe.LastStep.Counters is null ? Visibility.Collapsed : Visibility.Visible;
        LookUpButton.Visibility = lookUpCounters is null ? Visibility.Collapsed : Visibility.Visible;
        Show(LookUpLine, _counterNames.Count == 0
            ? "Look up stat names to search them."
            : $"{_counterNames.Count} statistic names from the last read.");

        ImportButton.Content = settingsOnly ? "Save" : comparison.IsUpdate ? "Update" : "Import";
        Refresh();
    }

    public IReadOnlyDictionary<string, string> Inputs =>
        _inputs.ToDictionary(i => i.Id, i => i.Value.Trim(), StringComparer.Ordinal);

    /// <summary>The choices to save. Set when Import or Save is accepted.</summary>
    public IReadOnlyDictionary<string, StatChoice> Stats { get; private set; } = new Dictionary<string, StatChoice>();

    /// <summary>The counter names to keep in the recipe's state, looked up here or saved before.</summary>
    public IReadOnlyList<string> CounterNames => _counterNames;

    private void AddStat(RecipeStat stat)
    {
        var choice = _existing.StatChoices.GetValueOrDefault(stat.Key);
        var item = new StatItem
        {
            Key = stat.Key,
            Label = stat.Label,
            Show = choice?.Show ?? false,
            Send = choice?.Send ?? false,
            MetricId = choice?.MetricId ?? stat.SuggestedMetricId,
        };

        item.PropertyChanged += OnStatChanged;
        _stats.Add(item);
    }

    /// <summary>
    /// Every entry already saved, plus every row ticked now. An entry, once written, keeps its name
    /// through recipe updates (stats design §5.1), so a row that was never ticked writes nothing.
    /// </summary>
    private Dictionary<string, StatChoice> Choices()
    {
        var choices = new Dictionary<string, StatChoice>(_existing.StatChoices, StringComparer.Ordinal);
        foreach (var item in _stats.Where(s => choices.ContainsKey(s.Key) || s.Show || s.Send))
        {
            choices[item.Key] = new StatChoice(item.Show, item.Send, item.MetricId.Trim());
        }

        return choices;
    }

    private BudgetCheck Budget()
    {
        var accountIds = _accountIds();
        var sending = accountIds.Count(id => !_existing.Excluded.Contains(id));
        var others = HistoryBudget.Installed(_installed, accountIds, exceptSlug: _recipe.Slug);
        var before = (sending, _existing.SentStats(_recipe).Count);
        var after = (sending, new RecipeState(Stats: Choices()).SentStats(_recipe).Count);
        return HistoryBudget.Check(others, before, after, accountsKnown: accountIds.Count > 0);
    }

    private void OnStatChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_reverting)
        {
            _budgetRefusal = null;

            // Stats design §5.3: a Send tick that would pass RoRoRo's limit is undone, and says why.
            if (sender is StatItem { Send: true } item && e.PropertyName == nameof(StatItem.Send) && Budget() is { Allowed: false } refused)
            {
                _budgetRefusal = refused.Line;
                Dispatcher.BeginInvoke(() =>
                {
                    _reverting = true;
                    item.Send = false;
                    _reverting = false;
                });
            }
        }

        Refresh();
    }

    private void Refresh()
    {
        var ticked = _stats.Any(s => s.Show || s.Send);

        Show(SlotLine, Budget().Line);
        Show(RuleLine, string.Join(Environment.NewLine, _stats
            .Where(s => s.Send && !string.IsNullOrWhiteSpace(s.MetricId))
            .Select(s => $"{s.Label}: {_ruleSentence(s.MetricId.Trim())}")));

        var refusals = _review.Refusals.ToList();
        if (!ticked) refusals.Add(StatRules.TickOne);
        if (_budgetRefusal is not null) refusals.Add(_budgetRefusal);
        Show(RefusalLine, string.Join(Environment.NewLine, refusals));

        ImportButton.IsEnabled = _review.CanImport && ticked;
    }

    private async void OnLookUpClick(object sender, RoutedEventArgs e)
    {
        if (_lookUpCounters is null) return;

        LookUpButton.IsEnabled = false;
        Show(LookUpLine, "Reading once…");
        try
        {
            var found = await _lookUpCounters(Inputs);
            if (found.Names.Count > 0) _counterNames = [.. found.Names];

            Show(LookUpLine, found.Names.Count > 0
                ? $"Found {found.Names.Count} statistic names. Type to search them."
                : found.Problem ?? "No statistic names came back.");
            UpdateMatches();

            // The read seeds RoRoRo's accounts, so the slot line may count more of them now.
            Refresh();
        }
        catch (Exception ex)
        {
            Show(LookUpLine, $"Could not look them up ({ex.GetType().Name}).");
        }
        finally
        {
            LookUpButton.IsEnabled = true;
        }
    }

    private void OnCounterSearchChanged(object sender, TextChangedEventArgs e) => UpdateMatches();

    private void UpdateMatches()
    {
        CounterMatches.ItemsSource = RecipeStats.MatchCounterNames(_counterNames, CounterSearchBox.Text, _stats.Select(s => s.Key));
        AddCounterButton.IsEnabled = false;
    }

    private void OnCounterMatchSelected(object sender, SelectionChangedEventArgs e) =>
        AddCounterButton.IsEnabled = CounterMatches.SelectedItem is string;

    private void OnAddCounterClick(object sender, RoutedEventArgs e)
    {
        if (CounterMatches.SelectedItem is not string name) return;
        if (RecipeStats.Find(_recipe, RecipeStats.CounterKey(name)) is not { } stat) return;
        if (_stats.Any(s => s.Key == stat.Key)) return;

        AddStat(stat);
        CounterSearchBox.Text = "";
        UpdateMatches();
        Refresh();
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        // Every declared input must be filled before the recipe runs (spec §3.3).
        var missing = _inputs.FirstOrDefault(i => string.IsNullOrWhiteSpace(i.Value));
        if (missing is not null)
        {
            Show(RefusalLine, $"Set {missing.Label} first.");
            return;
        }

        var choices = Choices();
        var problems = StatRules.Problems(_recipe, choices, _installed).ToList();
        if (Budget() is { Allowed: false } refused) problems.Add(refused.Line);

        if (problems.Count > 0)
        {
            // Stays open, so the name that collided can be changed right here.
            Show(RefusalLine, string.Join(Environment.NewLine, problems));
            return;
        }

        Stats = choices;
        DialogResult = true;
    }

    /// <summary>An empty line takes no space, so the screen has no gaps where nothing applies.</summary>
    private static void Show(TextBlock line, string text)
    {
        line.Text = text;
        line.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
