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
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        // One import screen at a time, however long the accounts wait lasts.
        if (_importing) return;
        _importing = true;
        ImportRecipeButton.IsEnabled = false;

        try
        {
            var outcome = await ImportFlow.RunAsync(_window, _services);
            if (outcome is null) return;

            Show(RecipesLine, outcome.Message);
            if (outcome.ChooseSources) _window.ShowPage(SetupPages.ClansId(outcome.Slug));
        }
        finally
        {
            _importing = false;
            ImportRecipeButton.IsEnabled = true;
        }
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string slug }) return;
        if (_services.Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, slug, StringComparison.Ordinal))?.Recipe is not { } recipe) return;

        var answer = MessageBox.Show(_window, RecipesModel.ConfirmRemove(recipe), "Ur Score",
            MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;

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
