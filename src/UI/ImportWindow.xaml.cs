using System.Windows;
using System.Windows.Controls;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.Theming;
using static Labs626.UrScore.UI.TextLines;

namespace Labs626.UrScore.UI;

/// <summary>
/// The safety screen (spec §6.2, stats design §7.1), shown before anything a recipe describes runs:
/// every host it contacts and exactly what each receives, what the score book keeps, and the Stats
/// table. Input values are picked afterwards in Setup, where the search list helps. Holds no HTTP of its
/// own; the one read it can ask for goes through the callback it is given.
/// </summary>
public partial class ImportWindow : Window
{
    public sealed record HostItem(string Host, string SendsText);

    private readonly Recipe _recipe;
    private readonly ImportReviewResult _review;
    private readonly RecipeState _existing;

    public ImportWindow(
        Recipe recipe, ImportReviewResult review, UpdateComparison comparison, RecipeState? existing,
        IReadOnlyList<InstalledRecipe> installed, Func<IReadOnlyCollection<Guid>> accountIds,
        Func<string, string> ruleSentence, Func<Task<CounterLookup>>? readCounterNames)
    {
        InitializeComponent();
        ThemeService.Attach(this);
        _recipe = recipe;
        _review = review;
        _existing = existing ?? new RecipeState();

        Title = comparison.IsUpdate ? "Update recipe" : "Import recipe";
        NameLine.Text = recipe.Name;
        CreditLine.Text = recipe.Credit;
        AuthorLine.Text = recipe.Author is null
            ? "No author given."
            : $"Says it is from {recipe.Author}. This is not verified.";

        HostsList.ItemsSource = review.Hosts
            .Select(h => new HostItem(h.Host, ImportReview.SendsText(h)))
            .ToList();

        PollLine.Text = $"Asks every {recipe.EffectiveEverySeconds} seconds.";
        ShowLine(ReusedLine, string.Join(" ", review.ReusedKeys));
        ShowLine(ChangesLine, comparison.Changes.Count == 0
            ? ""
            : "What changed:" + string.Concat(comparison.Changes.Select(c => Environment.NewLine + "• " + c)));
        ShowLine(InputsNote, comparison.IsUpdate ? "" : ImportText.InputsNote(recipe));

        KeptNote.Text = ImportText.KeptNote(recipe);
        KeptList.ItemsSource = ImportText.Kept(recipe).Select(label => "• " + label).ToList();

        if (recipe.IsGroupList)
        {
            StatsSection.Visibility = Visibility.Collapsed;
        }
        else
        {
            // An update also reviews legacy sends; loading them alone never enables reporting.
            var fresh = !comparison.IsUpdate;
            StatsTable.Load(recipe, _existing, installed, accountIds, ruleSentence,
                readCounterNames is null ? null : _ => readCounterNames(),
                ImportText.ShowEveryStat, review.Refusals,
                fresh ? StatsTableModel.Suggested(recipe) : _existing.ChoicesForUpdate);
            ShowLine(SuggestedLine, fresh ? ImportText.SuggestedNote(recipe) : "");
            StatsTable.Changed += (_, _) => Refresh();
        }

        ImportButton.Content = comparison.IsUpdate ? "Update" : "Import";
        Refresh();
    }

    /// <summary>The choices to save. Set when Import is accepted.</summary>
    public IReadOnlyDictionary<string, StatChoice> Stats { get; private set; } = new Dictionary<string, StatChoice>();

    /// <summary>The counter names to keep in the recipe's state, read here or saved before.</summary>
    public IReadOnlyList<string> CounterNames => _recipe.IsGroupList ? _existing.SavedCounterNames : StatsTable.CounterNames;

    private void Refresh()
    {
        // With the table hidden, the review's own refusals need a line of their own.
        ShowLine(ImportRefusalLine, _recipe.IsGroupList ? string.Join(Environment.NewLine, _review.Refusals) : "");
        ImportButton.IsEnabled = _review.CanImport && (_recipe.IsGroupList || StatsTable.AnyTicked);
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
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
}
