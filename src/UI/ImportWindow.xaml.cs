using System.Windows;
using System.Windows.Controls;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

/// <summary>
/// The safety screen (spec §6.2, stats design §7.1), shown before anything a recipe describes runs:
/// every host it contacts and exactly what each receives, what the score book keeps, and the Stats
/// table. Holds no HTTP of its own; the one read it can ask for goes through the callback it is given.
/// </summary>
public partial class ImportWindow : Window
{
    public sealed record HostItem(string Host, string SendsText);

    public sealed class InputItem
    {
        public required string Id { get; init; }

        public required string Label { get; init; }

        public string Value { get; set; } = "";
    }

    private readonly Recipe _recipe;
    private readonly ImportReviewResult _review;
    private readonly RecipeState _existing;
    private readonly List<InputItem> _inputs;

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

        KeptNote.Text = ImportText.KeptNote(recipe);
        KeptList.ItemsSource = ImportText.Kept(recipe).Select(label => "• " + label).ToList();

        if (recipe.IsGroupList)
        {
            StatsSection.Visibility = Visibility.Collapsed;
        }
        else
        {
            StatsTable.Load(recipe, _existing, installed, accountIds, ruleSentence,
                lookUpCounters is null ? null : _ => lookUpCounters(Inputs),
                ImportText.ShowEveryStat, review.Refusals);
            StatsTable.Changed += (_, _) => Refresh();
        }

        ImportButton.Content = settingsOnly ? "Save" : comparison.IsUpdate ? "Update" : "Import";
        Refresh();
    }

    public IReadOnlyDictionary<string, string> Inputs =>
        _inputs.ToDictionary(i => i.Id, i => i.Value.Trim(), StringComparer.Ordinal);

    /// <summary>The choices to save. Set when Import or Save is accepted.</summary>
    public IReadOnlyDictionary<string, StatChoice> Stats { get; private set; } = new Dictionary<string, StatChoice>();

    /// <summary>The counter names to keep in the recipe's state, read here or saved before.</summary>
    public IReadOnlyList<string> CounterNames => _recipe.IsGroupList ? _existing.SavedCounterNames : StatsTable.CounterNames;

    private void Refresh()
    {
        // With the table hidden, the review's own refusals need a line of their own.
        Show(ImportRefusalLine, _recipe.IsGroupList ? string.Join(Environment.NewLine, _review.Refusals) : "");
        ImportButton.IsEnabled = _review.CanImport && (_recipe.IsGroupList || StatsTable.AnyTicked);
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        // Every declared input must be filled before the recipe runs (spec §3.3).
        var missing = _inputs.FirstOrDefault(i => string.IsNullOrWhiteSpace(i.Value));
        if (missing is not null)
        {
            Show(ImportRefusalLine, $"Set {missing.Label} first.");
            return;
        }

        if (_recipe.IsGroupList)
        {
            Stats = _existing.StatChoices;
            DialogResult = true;
            return;
        }

        var problems = StatsTable.SaveProblems();
        if (problems.Count > 0)
        {
            StatsTable.ShowProblems([.. _review.Refusals, .. problems]);
            return;
        }

        Stats = StatsTable.Choices;
        DialogResult = true;
    }

    /// <summary>An empty line takes no space, so the screen has no gaps where nothing applies.</summary>
    private static void Show(TextBlock line, string text)
    {
        line.Text = text;
        line.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
