using System.Windows;
using System.Windows.Controls;
using Labs626.UrScore.Composition;

namespace Labs626.UrScore.UI;

/// <summary>Setup › Recipes (spec §7.4): the installed list, Remove, and Import recipe….</summary>
public partial class RecipesPage : UserControl, ISetupPage
{
    private readonly ISetupServices _services;
    private readonly SetupWindow _window;
    private bool _importing;

    /// <summary>The box is being set from the saved settings, not by a click, so the handler doesn't write them back.</summary>
    private bool _settingBox;

    public RecipesPage(ISetupServices services, SetupWindow window)
    {
        InitializeComponent();
        _services = services;
        _window = window;
        Refresh();
    }

    public void Refresh()
    {
        RecipesList.ItemsSource = RecipesModel.Items(_services.Installed, _services.Sources, _services.IconFileFor);
        RecipesEmptyLine.Visibility = _services.Installed.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Show(RecipeProblemsLine, _services.RecipeProblems.Count == 0
            ? ""
            : "Some recipe files could not be read: " + string.Join(" | ", _services.RecipeProblems));

        _settingBox = true;
        StartOnOpenBox.IsChecked = _services.Settings.StartOnOpen;
        _settingBox = false;
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        // One import screen at a time, however long the accounts wait lasts.
        if (_importing) return;
        _importing = true;
        ImportRecipeButton.IsEnabled = false;

        try
        {
            Show(ImportProblemLine, "");
            var outcome = await ImportFlow.RunAsync(_window, _services, text => Show(RecipesLine, text));
            if (outcome is null) return;

            // An import that couldn't happen is said here, under the button that started it, not in a stock box.
            if (outcome.IsProblem)
            {
                Show(RecipesLine, "");
                Show(ImportProblemLine, outcome.Message);
                return;
            }

            Show(RecipesLine, outcome.Message);
            if (outcome.ChooseSources) _window.ShowPage(SetupPages.ClansId(outcome.Slug));
        }
        finally
        {
            _importing = false;
            ImportRecipeButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// The one app-wide setting with a control (plan A32). A write that fails is said here and the box goes back to
    /// what is saved, so it never shows something the file doesn't — and no message box, ever (Global Constraints).
    /// </summary>
    private void OnStartOnOpenChanged(object sender, RoutedEventArgs e)
    {
        if (_settingBox) return;

        try
        {
            _services.SaveSettings(_services.Settings with { StartOnOpen = StartOnOpenBox.IsChecked == true });
            Show(StartOnOpenProblemLine, "");
        }
        catch (Exception ex)
        {
            Show(StartOnOpenProblemLine, _services.Redactor.Redact($"Could not save that: {ex.Message}"));
            _settingBox = true;
            StartOnOpenBox.IsChecked = _services.Settings.StartOnOpen;
            _settingBox = false;
        }
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string slug }) return;
        if (_services.Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, slug, StringComparison.Ordinal))?.Recipe is not { } recipe) return;

        // Asked in Ur Score's own window, in the theme, never a stock Windows box (owner rule, backlog V3-S.10).
        if (!ConfirmWindow.Ask(_window, RecipesModel.RemoveQuestion(recipe))) return;

        try
        {
            _services.RemoveRecipe(slug);
            Show(RecipesLine, $"Removed {recipe.Name}. {RecipesModel.KeepsBook}");
        }
        catch (Exception ex)
        {
            Show(RecipesLine, _services.Redactor.Redact($"Could not remove it: {ex.Message}"));
        }
    }

    private static void Show(TextBlock line, string text)
    {
        line.Text = text;
        line.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
