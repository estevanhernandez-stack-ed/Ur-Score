using System.Windows;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

/// <summary>
/// The safety screen (spec §6.2), shown before anything a recipe describes runs: every host it
/// contacts and exactly what each receives. Also part 1's home for a recipe's inputs and metric id,
/// until part 2 moves them into the main window.
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
    private readonly List<InputItem> _inputs;

    public ImportWindow(
        Recipe recipe, ImportReviewResult review, UpdateComparison comparison, RecipeState? existing,
        string ruleSentence, bool settingsOnly = false)
    {
        InitializeComponent();
        ThemeService.Attach(this);
        _recipe = recipe;

        Title = settingsOnly ? "Recipe settings" : comparison.IsUpdate ? "Update recipe" : "Import recipe";
        NameLine.Text = recipe.Name;
        CreditLine.Text = recipe.Credit;
        AuthorLine.Text = recipe.Author is null
            ? "No author given."
            : $"Says it is from {recipe.Author}. This is not verified.";

        HostsList.ItemsSource = review.Hosts
            .Select(h => new HostItem(h.Host, $"Receives {string.Join(", ", h.Sends)}."))
            .ToList();

        PollLine.Text = $"Asks every {recipe.EffectiveEverySeconds} seconds.";
        Show(ReusedLine, string.Join(" ", review.ReusedKeys));
        Show(ChangesLine, comparison.Changes.Count == 0
            ? ""
            : "What changed:" + string.Concat(comparison.Changes.Select(c => Environment.NewLine + "• " + c)));

        MetricIdBox.Text = existing?.StatChoices.GetValueOrDefault(recipe.LastStep.Values[0].Id)?.MetricId ?? recipe.LastStep.Values[0].MetricId;
        RuleLine.Text = ruleSentence;

        _inputs =
        [
            .. recipe.Inputs.Select(i => new InputItem
            {
                Id = i.Id,
                Label = i.Label,
                Value = existing?.InputValues.GetValueOrDefault(i.Id) ?? "",
            }),
        ];
        InputsList.ItemsSource = _inputs;

        Show(RefusalLine, string.Join(Environment.NewLine, review.Refusals));
        ImportButton.IsEnabled = review.CanImport;
        ImportButton.Content = settingsOnly ? "Save" : comparison.IsUpdate ? "Update" : "Import";
    }

    public IReadOnlyDictionary<string, string> Inputs =>
        _inputs.ToDictionary(i => i.Id, i => i.Value.Trim(), StringComparer.Ordinal);

    /// <summary>
    /// The metric id the user accepted, pinned so a later recipe update cannot move reports to a
    /// metric with no rule (spec §2: a recipe never decides what happens with the number).
    /// </summary>
    public string? MetricIdOverride { get; private set; }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        // Every declared input must be filled before the recipe runs (spec §3.3).
        var missing = _inputs.FirstOrDefault(i => string.IsNullOrWhiteSpace(i.Value));
        if (missing is not null)
        {
            Show(RefusalLine, $"Set {missing.Label} first.");
            return;
        }

        var metricId = MetricIdBox.Text.Trim();
        if (metricId.Length == 0)
        {
            Show(RefusalLine, "The metric id cannot be empty. RoRoRo's rules find the number by it.");
            return;
        }

        MetricIdOverride = metricId;
        DialogResult = true;
    }

    /// <summary>An empty line takes no space, so the screen has no gaps where nothing applies.</summary>
    private static void Show(System.Windows.Controls.TextBlock line, string text)
    {
        line.Text = text;
        line.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
