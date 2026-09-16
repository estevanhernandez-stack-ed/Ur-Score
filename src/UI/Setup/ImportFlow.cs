using System.IO;
using System.Windows;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Microsoft.Win32;

namespace Labs626.UrScore.UI;

/// <summary>
/// What an import did, and whether Setup should go on to that recipe's Clans page. <paramref name="IsProblem"/>
/// marks a message the page says as a refusal rather than as news: nothing was installed.
/// </summary>
public sealed record ImportOutcome(string Slug, string Message, bool ChooseSources, bool IsProblem = false);

/// <summary>
/// Import recipe…, moved from the retired main window with the same rules (spec §6.3, stats design §7.2):
/// an invalid file is refused, a clash of names is refused, an identical file asks nothing, an update with
/// no new contacts is listed not asked, and anything else goes through the import screen.
/// </summary>
public static class ImportFlow
{
    public const string DialogTitle = "Import a recipe";

    public const string AskingForAccounts = "Asking RoRoRo for your accounts…";

    /// <param name="status">Shows a line while the import waits on RoRoRo, and clears it after.</param>
    public static async Task<ImportOutcome?> RunAsync(Window owner, ISetupServices services, Action<string>? status = null)
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
            status?.Invoke(AskingForAccounts);
            try
            {
                await services.RefreshAccountsAsync(CancellationToken.None);
            }
            catch (Exception)
            {
                // The budget then counts the accounts already known.
            }
            finally
            {
                status?.Invoke("");
            }

            var rules = RulesFile.Read(services.RulesPath);
            var window = new ImportWindow(
                recipe, review, comparison, installed?.State, services.Installed,
                () => [.. services.KnownAccounts.Select(a => a.AccountId)],
                metricId => AlertCards.StatLine(rules, metricId),
                recipe.LastStep.Counters is null ? null : () => services.ReadCounterNamesAsync(recipe, CancellationToken.None))
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
            // Said on the page the click came from, where the eye already is, not in a stock box that has to be
            // dismissed before you can see what you were doing (owner rule, backlog V3-S.10).
            return Problem(services.Redactor.Redact($"Could not save that recipe: {ex.Message}"));
        }
    }

    /// <summary>An import that couldn't happen, for the page to say as a refusal. Nothing was installed.</summary>
    private static ImportOutcome Problem(string message) => new("", message, ChooseSources: false, IsProblem: true);

    /// <summary>A recipe with inputs and no sources goes on to its Clans page, where the search replaces a plain text box.</summary>
    private static ImportOutcome Outcome(ISetupServices services, Recipe recipe, string message)
    {
        var installed = services.Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, recipe.Slug, StringComparison.Ordinal));
        var choose = installed is not null
                     && SetupPages.HasClansPage(installed)
                     && services.Sources.All(s => !string.Equals(s.Recipe, recipe.Slug, StringComparison.Ordinal));
        return new ImportOutcome(recipe.Slug, message, choose);
    }

    /// <summary>
    /// The one stock box left in the import flow, held back on purpose (backlog V3-S.10, still open for this).
    /// tools/smoke/window-smoke.ps1 step 2 refuses an invalid recipe and reads this box's words through Win32 —
    /// <c>Get-MessageBoxText</c> wants a <c>Static</c> control and <c>Close-MessageBox</c> posts BM_CLICK to a
    /// control's window handle, neither of which a WPF window has. Saying this in place needs that script changed
    /// in the same commit. <c>MessageBoxFenceTests</c> holds it by name so no new one can join it.
    /// </summary>
    private static void Warn(Window owner, string text) =>
        MessageBox.Show(owner, text, "Ur Score", MessageBoxButton.OK, MessageBoxImage.Warning);
}
