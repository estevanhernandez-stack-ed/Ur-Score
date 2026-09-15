using System.IO;
using System.Windows;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Recipes;
using Microsoft.Win32;

namespace Labs626.UrScore.UI;

/// <summary>What an import did, and whether Setup should go on to that recipe's Clans page.</summary>
public sealed record ImportOutcome(string Slug, string Message, bool ChooseSources);

/// <summary>
/// Import recipe…, moved from the retired main window with the same rules (spec §6.3, stats design §7.2):
/// an invalid file is refused, a clash of names is refused, an identical file asks nothing, an update with
/// no new contacts is listed not asked, and anything else goes through the import screen.
/// </summary>
public static class ImportFlow
{
    public const string DialogTitle = "Import a recipe";

    public static async Task<ImportOutcome?> RunAsync(Window owner, ISetupServices services)
    {
        var dialog = new OpenFileDialog
        {
            Title = DialogTitle,
            Filter = "Ur Score recipe (*.recipe.json)|*.recipe.json|JSON file (*.json)|*.json",
        };

        if (dialog.ShowDialog(owner) != true) return null;

        string text;
        try
        {
            text = File.ReadAllText(dialog.FileName);
        }
        catch (Exception ex)
        {
            Warn(owner, $"Could not read that file: {ex.Message}");
            return null;
        }

        var parsed = RecipeParser.Parse(text);
        if (!parsed.Ok)
        {
            Warn(owner, "That recipe could not be imported:\n\n" + string.Join("\n", parsed.Problems.Select(p => "• " + p)));
            return null;
        }

        var recipe = parsed.Recipe!;
        var installed = services.Store.Find(recipe.Slug);

        if (installed is not null
            && (!string.Equals(installed.Recipe.Name, recipe.Name, StringComparison.Ordinal)
                || !string.Equals(installed.Recipe.Author, recipe.Author, StringComparison.Ordinal)))
        {
            Warn(owner, $"A different recipe, {installed.Recipe.Name}, is already installed under the same file name. Rename one of them before importing.");
            return null;
        }

        if (installed is not null && string.Equals(installed.Text, text, StringComparison.Ordinal))
        {
            // Spec §6.3: an identical file imports without asking.
            return Outcome(services, recipe, $"{recipe.Name} is already installed.");
        }

        var review = ImportReview.Review(recipe, services.Keys);
        var comparison = ImportReview.CompareToInstalled(installed?.Recipe, recipe, services.Keys, installed?.State);

        try
        {
            if (installed is not null && review.CanImport && !comparison.AsksAgain)
            {
                // An update that contacts the same hosts with the same things: listed, not asked.
                services.Store.Save(recipe, text, installed.State);
                services.ReloadRecipes();
                return Outcome(services, recipe, $"Updated {recipe.Name}. {string.Join(" ", comparison.Changes)}".Trim());
            }

            // The history budget counts RoRoRo's accounts, so they are asked for before the screen that checks it.
            try
            {
                await services.RefreshAccountsAsync(CancellationToken.None);
            }
            catch (Exception)
            {
                // The budget then counts the accounts already known.
            }

            var window = new ImportWindow(
                recipe, review, comparison, installed?.State, services.Installed,
                () => [.. services.KnownAccounts.Select(a => a.AccountId)],
                metricId => AlertsModel.RuleSentence(metricId).Text,
                recipe.LastStep.Counters is null ? null : _ => services.ReadCounterNamesAsync(recipe, CancellationToken.None))
            {
                Owner = owner,
            };

            if (window.ShowDialog() != true) return null;

            var state = (installed?.State ?? new RecipeState()) with
            {
                Stats = window.Stats,
                CounterNames = window.CounterNames,
            };

            services.Store.Save(recipe, text, state);
            services.ReloadRecipes();
            return Outcome(services, recipe, $"Imported {recipe.Name}.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, services.Redactor.Redact($"Could not save that recipe: {ex.Message}"), "Ur Score",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }

    /// <summary>A recipe with inputs and no sources goes on to its Clans page, where the search replaces a plain text box.</summary>
    private static ImportOutcome Outcome(ISetupServices services, Recipe recipe, string message)
    {
        var installed = services.Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, recipe.Slug, StringComparison.Ordinal));
        var choose = installed is not null
                     && SetupPages.HasClansPage(installed)
                     && services.Sources.All(s => !string.Equals(s.Recipe, recipe.Slug, StringComparison.Ordinal));
        return new ImportOutcome(recipe.Slug, message, choose);
    }

    private static void Warn(Window owner, string text) =>
        MessageBox.Show(owner, text, "Ur Score", MessageBoxButton.OK, MessageBoxImage.Warning);
}
