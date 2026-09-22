using System.Windows.Controls;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

[Collection(WpfCollection.Name)]
public class LegacyRecipeUpdateTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "urscore-legacy-" + Guid.NewGuid().ToString("N"));

    private static string RecipeText => RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json");

    private static Recipe Recipe => RecipeParser.Parse(RecipeText).Recipe!;

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private InstalledRecipe Load(string stateText)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, Recipe.Slug + ".recipe.json"), RecipeText);
        File.WriteAllText(Path.Combine(_directory, Recipe.Slug + ".state.json"), stateText);
        return Assert.Single(new RecipeStore(_directory).LoadAll().Recipes);
    }

    [Theory]
    [InlineData("null", "clan.battle.points")]
    [InlineData("\" \"", "clan.battle.points")]
    [InlineData("\" my.points \"", "my.points")]
    public void LegacyChoicesUseTheInstalledNameAndRequireReviewEvenWithoutNewContacts(string legacy, string expected)
    {
        var text = "{\"metricIdOverride\":" + legacy + "}";
        var installed = Load(text);
        var incoming = installed.Recipe with { EverySeconds = 300 };

        var comparison = ImportReview.CompareToInstalled(installed.Recipe, incoming, new NoKeys(), installed.State);

        Assert.True(comparison.AsksAgain);
        Assert.Contains(comparison.Changes, change => change.StartsWith("Review which stats to show and send.", StringComparison.Ordinal));
        Assert.Equal(new StatChoice(true, true, expected), installed.State.ChoicesForUpdate["value"]);
        Assert.Empty(installed.State.SentStats(incoming));
        Assert.Equal(text, File.ReadAllText(Path.Combine(_directory, Recipe.Slug + ".state.json")));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"value\":{\"show\":false,\"send\":false,\"metricId\":\"current.points\"}}")]
    public void ModernStatsNeverRecoverLegacyTicks(string stats)
    {
        var installed = Load("{\"metricIdOverride\":\"old.points\",\"stats\":" + stats + "}");

        Assert.Null(installed.State.LegacyStatChoices);
        Assert.All(installed.State.ChoicesForUpdate.Values, choice => Assert.False(choice.Show || choice.Send));
        Assert.False(ImportReview.CompareToInstalled(installed.Recipe,
            installed.Recipe with { EverySeconds = 300 }, new NoKeys(), installed.State).AsksAgain);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"metricIdOverride\":123}")]
    public void MissingOrUnrecognizedLegacyStateDoesNotSuggestSending(string text)
    {
        var installed = Load(text);

        Assert.Null(installed.State.LegacyStatChoices);
        Assert.Empty(installed.State.ChoicesForUpdate);
    }

    [Fact]
    public void RemovingTheLegacyStatNamesTheSendThatWouldStop()
    {
        var installed = Load("{\"metricIdOverride\":\"my.points\"}");
        var incoming = installed.Recipe with
        {
            Steps = [installed.Recipe.Steps[0], installed.Recipe.LastStep with
            {
                Values = [installed.Recipe.LastStep.Values[0] with { Id = "replacement" }],
            }],
        };

        var comparison = ImportReview.CompareToInstalled(installed.Recipe, incoming, new NoKeys(), installed.State);
        var rows = StatsTableModel.Build(incoming, installed.State.ChoicesForUpdate, []);

        Assert.True(comparison.AsksAgain);
        Assert.Contains("Points will no longer be read, so RoRoRo stops getting my.points.", comparison.Changes);
        Assert.False(StatsTableModel.AnyTicked(rows));
    }

    [Fact]
    public void UpdateWindowReviewsAndSavesTheLegacySendWhileFirstImportDoesNotAdoptIt()
    {
        var installed = Load("{\"metricIdOverride\":\"my.points\"}");
        var incoming = installed.Recipe with
        {
            Steps = [installed.Recipe.Steps[0], installed.Recipe.LastStep with
            {
                Values = [installed.Recipe.LastStep.Values[0] with { MetricId = "new.points" }],
            }],
        };

        OnSta(() =>
        {
            var keys = new NoKeys();
            var comparison = ImportReview.CompareToInstalled(installed.Recipe, incoming, keys, installed.State);
            var window = new ImportWindow(incoming, ImportReview.Review(incoming, keys), comparison,
                installed.State, [installed], () => [], _ => "", null);
            try
            {
                var table = Assert.IsType<StatsTable>(window.FindName("StatsTable"));
                Assert.True(Assert.IsType<Button>(window.FindName("ImportButton")).IsEnabled);
                Assert.Equal(new StatChoice(true, true, "my.points"), table.Choices["value"]);
                Assert.Empty(installed.State.SentStats(incoming));

                var store = new RecipeStore(_directory);
                store.SaveState(incoming, installed.State with { Stats = table.Choices });
                var saved = store.Find(Recipe.Slug)!.State;
                Assert.Equal("my.points", Assert.Single(saved.SentStats(incoming)).MetricId);
                Assert.Null(saved.LegacyStatChoices);
                var savedText = File.ReadAllText(Path.Combine(_directory, Recipe.Slug + ".state.json"));
                Assert.DoesNotContain("legacyStatChoices", savedText);
                Assert.DoesNotContain("choicesForUpdate", savedText);
                Assert.DoesNotContain("metricIdOverride", savedText);
            }
            finally
            {
                window.Close();
            }

            var firstImport = new ImportWindow(installed.Recipe, ImportReview.Review(installed.Recipe, keys),
                ImportReview.CompareToInstalled(null, installed.Recipe, keys), installed.State,
                [], () => [], _ => "", null);
            try
            {
                var table = Assert.IsType<StatsTable>(firstImport.FindName("StatsTable"));
                Assert.All(table.Choices.Values, choice => Assert.False(choice.Send));
            }
            finally
            {
                firstImport.Close();
            }
        });
    }

    /// <summary>
    /// Once this file's own copy of "an App on an STA thread, shut down after": now <see cref="UiThread.RunInApp"/>,
    /// which builds the process's ONE application and runs every caller on it — WPF permits no second one, which
    /// this copy could not have known while it was the only caller (2026-09-22).
    /// </summary>
    private static void OnSta(Action action) => UiThread.RunInApp(action);
}