using System.IO;
using System.Windows;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Microsoft.Win32;

namespace Labs626.UrScore.UI;

/// <summary>
/// What an import did, and whether Setup should go on to that recipe's Clans page. <paramref name="IsProblem"/>
/// marks a message the page says as a refusal rather than as news: nothing was installed, or it was saved and couldn't be
/// loaded, which needs a restart.
/// </summary>
public sealed record ImportOutcome(string Slug, string Message, bool ChooseSources, bool IsProblem = false);

/// <summary>The two lines an import speaks on: what it did (<paramref name="News"/>), and why it couldn't (<paramref name="Problem"/>).</summary>
public sealed record ImportLines(string News, string Problem);

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
            return Problem($"Could not read that file: {ex.Message}");
        }

        var parsed = RecipeParser.Parse(text);
        if (!parsed.Ok)
        {
            return Problem("That recipe could not be imported:\n\n" + string.Join("\n", parsed.Problems.Select(p => "• " + p)));
        }

        var recipe = parsed.Recipe!;
        var installed = services.Store.Find(recipe.Slug);

        if (installed is not null
            && (!string.Equals(installed.Recipe.Name, recipe.Name, StringComparison.Ordinal)
                || !string.Equals(installed.Recipe.Author, recipe.Author, StringComparison.Ordinal)))
        {
            return Problem($"A different recipe, {installed.Recipe.Name}, is already installed under the same file name. Rename one of them before importing.");
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
                return SaveAndLoad(services, recipe, text, installed.State, $"Updated {recipe.Name}. {string.Join(" ", comparison.Changes)}".Trim());
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

            return SaveAndLoad(services, recipe, text, state, $"Imported {recipe.Name}.");
        }
        catch (Exception ex)
        {
            // Said on the page the click came from, where the eye already is, not in a stock box that has to be
            // dismissed before you can see what you were doing (owner rule, backlog V3-S.10).
            return Problem(services.Redactor.Redact($"Could not save that recipe: {ex.Message}"));
        }
    }

    /// <summary>
    /// What an import leaves on the page that started it. A cancelled one (null) leaves both lines as they were before it
    /// started: the wait for RoRoRo writes over the news line, and a cancel must not leave it blank (backlog S1-14.10). One that
    /// happened or couldn't replaces both.
    /// </summary>
    public static ImportLines LinesAfter(ImportLines before, ImportOutcome? outcome) =>
        outcome is null ? before
        : outcome.IsProblem ? new ImportLines("", outcome.Message)
        : new ImportLines(outcome.Message, "");

    /// <summary>
    /// Saves the recipe, then loads every recipe again. Each step fails on its own words (backlog S1-12.12): a load that throws
    /// after a good save is about a recipe that is on disk, so it says it was saved and that a restart loads it, and the trail
    /// gets the exception's type.
    /// </summary>
    public static ImportOutcome SaveAndLoad(ISetupServices services, Recipe recipe, string text, RecipeState state, string message)
    {
        try
        {
            services.Store.Save(recipe, text, state);
        }
        catch (Exception ex)
        {
            return Problem(services.Redactor.Redact($"Could not save that recipe: {ex.Message}"));
        }

        try
        {
            services.ReloadRecipes();
        }
        catch (Exception ex)
        {
            services.AddTrail($"RECIPE NOT LOADED: {recipe.Slug} {ex.GetType().Name}");
            return new ImportOutcome(recipe.Slug, $"{recipe.Name} was saved, but Ur Score couldn't load it. Restart Ur Score to load it.",
                ChooseSources: false, IsProblem: true);
        }

        return Outcome(services, recipe, message);
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

}
