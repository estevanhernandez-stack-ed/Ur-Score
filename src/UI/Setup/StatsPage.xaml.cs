using System.Windows;
using System.Windows.Controls;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

/// <summary>A recipe in the Stats page's list. Its text is its name.</summary>
public sealed record RecipeChoice(string Slug, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Setup › Stats (spec §7.3): one Stats table per recipe, saved to the recipe's state and applied to its watches.</summary>
public partial class StatsPage : UserControl, ISetupPage
{
    private readonly ISetupServices _services;
    private readonly CancellationTokenSource _closing = new();

    /// <summary>Null until the first <see cref="Refresh"/>, so an empty first run (no recipes yet) is never
    /// mistaken for "unchanged since last time" and skip the visibility this page needs to show.</summary>
    private IReadOnlyList<RecipeChoice>? _choices;

    private string? _slug;

    public StatsPage(ISetupServices services, string? recipeSlug = null)
    {
        InitializeComponent();
        _services = services;
        _slug = recipeSlug;
        StatsTable.Changed += (_, _) => SaveStatsButton.IsEnabled = StatsTable.AnyTicked;
        Unloaded += (_, _) => _closing.Cancel();
        Refresh();
    }

    public void Refresh()
    {
        var groupLists = _services.Installed.Where(i => i.Recipe.IsGroupList).Select(i => i.Recipe.Name).ToList();
        Show(StatsGroupListLine, groupLists.Count == 0 ? ""
            : $"{string.Join(", ", groupLists)} {(groupLists.Count == 1 ? "has" : "have")} no account stats to tick — its rows are "
              + "other people's clans. What it can send is in Clan and field, below.");

        // Outside the early return below: the clan-and-field section stands on its own, and it has to show even
        // when a clans list is the only recipe installed and the Stats table has nothing to draw.
        LoadField();

        var choices = _services.Installed
            .Where(i => !i.Recipe.IsGroupList)
            .Select(i => new RecipeChoice(i.Recipe.Slug, i.Recipe.Name))
            .ToList();

        // Rebuilt only when the recipe list changes, so a snapshot arriving never resets ticks being made.
        // _choices is null only before the very first render, so that render is never skipped even when
        // it has nothing to show (fix round 1: the empty state was silently skipped on a no-recipes open).
        if (_choices is not null && choices.SequenceEqual(_choices)) return;
        _choices = choices;

        var any = ShowsBody(choices.Count);
        RecipeRow.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        StatsBody.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        StatsEmptyLine.Visibility = any ? Visibility.Collapsed : Visibility.Visible;

        StatsRecipeBox.ItemsSource = choices;
        StatsRecipeBox.SelectedItem = choices.FirstOrDefault(c => c.Slug == _slug) ?? choices.FirstOrDefault();
    }

    private void OnRecipeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StatsRecipeBox.SelectedItem is not RecipeChoice choice) return;
        _slug = choice.Slug;
        Load();
    }

    /// <summary>Whether the recipe picker and Stats table show, versus the empty-state line. Pure and public so the
    /// fix for the skipped-first-render bug (fix round 1) has a test that needs no live window.</summary>
    public static bool ShowsBody(int recipeChoiceCount) => recipeChoiceCount > 0;

    private InstalledRecipe? Current =>
        _services.Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, _slug, StringComparison.Ordinal));

    private void Load()
    {
        if (Current is not { } installed) return;

        var recipe = installed.Recipe;
        var rules = RulesFile.Read(_services.RulesPath);
        StatsTable.Load(
            recipe, installed.State, _services.Installed,
            () => [.. _services.KnownAccounts.Select(a => a.AccountId)],
            metricId => AlertCards.StatLine(rules, metricId),
            recipe.LastStep.Counters is null ? null : ct => _services.ReadCounterNamesAsync(recipe, ct),
            "Read stat names again");

        Show(StatsSavedLine, "");
        SaveStatsButton.IsEnabled = StatsTable.AnyTicked;

        // Spec §7.3: no saved names and a recipe with counters means one read when the page opens.
        if (recipe.LastStep.Counters is not null && !StatsTable.HasSavedNames) _ = StatsTable.ReadNamesAsync(_closing.Token);
    }

    /// <summary>The clans list this section is about, or null when none is installed and the section stays hidden.</summary>
    private InstalledRecipe? ClansList =>
        FieldMetricsModel.ListFor(_services.Installed);

    private void LoadField()
    {
        if (ClansList is not { } list)
        {
            FieldSection.Visibility = Visibility.Collapsed;
            return;
        }

        FieldSection.Visibility = Visibility.Visible;
        FieldLine.Text = FieldMetricsModel.Line(list, _services.Sources, _services.Installed);
        FieldNumbers.ItemsSource = FieldMetricsModel.Items(list.State);
        Show(FieldSavedLine, "");
    }

    private void OnSaveFieldClick(object sender, RoutedEventArgs e)
    {
        if (ClansList is not { } list) return;

        try
        {
            var ticked = FieldMetricsModel.Ticked(FieldNumbers.ItemsSource.Cast<FieldMetricItem>());
            _services.SaveRecipeState(list.Recipe, list.State with { SentFieldMetrics = ticked });
            LoadField();
            Show(FieldSavedLine, ticked.Count == 0
                ? "Saved. No clan number is sent."
                : $"Saved. {ticked.Count} clan number(s) go to RoRoRo from the next read.");
        }
        catch (Exception ex)
        {
            Show(FieldSavedLine, _services.Redactor.Redact($"Could not save those numbers: {ex.Message}"));
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (Current is not { } installed) return;

        var problems = StatsTable.SaveProblems();
        if (problems.Count > 0)
        {
            StatsTable.ShowProblems(problems);
            return;
        }

        try
        {
            _services.SaveRecipeState(installed.Recipe, installed.State with
            {
                Stats = StatsTable.Choices,
                CounterNames = StatsTable.CounterNames,
            });
            Load();
            Show(StatsSavedLine, "Saved. The next read asks for these stats.");
        }
        catch (Exception ex)
        {
            Show(StatsSavedLine, _services.Redactor.Redact($"Could not save those stats: {ex.Message}"));
        }
    }

    private static void Show(TextBlock line, string text)
    {
        line.Text = text;
        line.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
