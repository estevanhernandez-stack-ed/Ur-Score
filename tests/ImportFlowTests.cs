using Labs626.UrScore.Board;
using Labs626.UrScore.Book;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

/// <summary>What an import leaves on the page that started it, and what it says when saving worked and loading didn't.</summary>
public class ImportFlowTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "urscore-import-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static readonly ImportLines Before = new("Removed Top groups. Its score book is kept.", "That recipe could not be imported:\n\n• Step 2 has no 'url'.");

    /// <summary>
    /// Backlog S1-14.10. The wait for RoRoRo's accounts wrote "Asking RoRoRo…" over the Recipes line and blanked it after, so
    /// cancelling the import screen left the line empty: what it said before the import was gone, and nothing replaced it.
    /// </summary>
    [Fact]
    public void ACancelledImportLeavesTheLinesAsTheyWereBeforeItStarted() =>
        Assert.Equal(Before, ImportFlow.LinesAfter(Before, outcome: null));

    [Fact]
    public void AnImportThatHappenedSaysSoAndTakesTheOldProblemAway() =>
        Assert.Equal(new ImportLines("Imported Clan.", ""),
            ImportFlow.LinesAfter(Before, new ImportOutcome("clan", "Imported Clan.", ChooseSources: false)));

    [Fact]
    public void AnImportThatCouldNotHappenSaysWhyAndTakesTheOldNewsAway() =>
        Assert.Equal(new ImportLines("", "Could not read that file."),
            ImportFlow.LinesAfter(Before, new ImportOutcome("", "Could not read that file.", ChooseSources: false, IsProblem: true)));

    /// <summary>
    /// Backlog S1-12.12. One catch covered saving the recipe and loading it again, so a load that threw after a good save said
    /// "Could not save that recipe" about a recipe that was on disk. It says it was saved, and what to do; the trail gets the
    /// exception's type.
    /// </summary>
    [Fact]
    public void ARecipeThatWasSavedButDidNotLoadSaysItWasSaved()
    {
        var services = new ImportServices(_folder) { ReloadFailure = new InvalidOperationException("Sequence contains no elements") };
        var recipe = BoardFixtures.Clan;

        var outcome = ImportFlow.SaveAndLoad(services, recipe, RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json"), new RecipeState(), $"Imported {recipe.Name}.");

        Assert.True(outcome.IsProblem);
        Assert.False(outcome.ChooseSources);
        Assert.Equal($"{recipe.Name} was saved, but Ur Score couldn't load it. Restart Ur Score to load it.", outcome.Message);
        Assert.NotNull(services.Store.Find(recipe.Slug));
        Assert.Contains(services.TrailLines, line => line == $"RECIPE NOT LOADED: {recipe.Slug} InvalidOperationException");
        Assert.DoesNotContain(services.TrailLines, line => line.Contains("Sequence contains", StringComparison.Ordinal));
    }

    [Fact]
    public void ARecipeThatCouldNotBeSavedSaysSoAndLoadsNothing()
    {
        // A file where the recipes folder should be: the save can't create it.
        Directory.CreateDirectory(_folder);
        var blocked = Path.Combine(_folder, "recipes");
        File.WriteAllText(blocked, "not a folder");
        var services = new ImportServices(blocked);
        var recipe = BoardFixtures.Clan;

        var outcome = ImportFlow.SaveAndLoad(services, recipe, "{}", new RecipeState(), $"Imported {recipe.Name}.");

        Assert.True(outcome.IsProblem);
        Assert.StartsWith("Could not save that recipe: ", outcome.Message, StringComparison.Ordinal);
        Assert.Equal(0, services.Reloads);
    }

    [Fact]
    public void ARecipeThatWasSavedAndLoadedSaysSoAndGoesOnToChooseItsSources()
    {
        var services = new ImportServices(_folder);
        var recipe = BoardFixtures.Clan;

        var outcome = ImportFlow.SaveAndLoad(services, recipe, RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json"), new RecipeState(), $"Imported {recipe.Name}.");

        Assert.Equal(new ImportOutcome(recipe.Slug, $"Imported {recipe.Name}.", ChooseSources: true), outcome);
        Assert.Equal(1, services.Reloads);
    }

    /// <summary>Only what an import's save and load touch; anything else would be a test reaching further than it says.</summary>
    private sealed class ImportServices(string recipesFolder) : ISetupServices
    {
        public Exception? ReloadFailure { get; init; }

        public int Reloads { get; private set; }

        public List<string> TrailLines { get; } = [];

        public AppPaths Paths => throw new NotSupportedException();

        public IReadOnlyList<BoardDef> SavedBoards => throw new NotSupportedException();

        public void SaveImportedBoards(IReadOnlyList<BoardDef> saved) => throw new NotSupportedException();

        public ISetupWriter SetupWriter => throw new NotSupportedException();

        public IReadOnlyList<InstalledRecipe> Installed { get; private set; } = [];

        public IReadOnlyList<string> RecipeProblems => [];

        public IReadOnlyList<Source> Sources => [];

        public RecipeStore Store { get; } = new(recipesFolder);

        public IKeyStore Keys { get; } = new NoKeys();

        public Redactor Redactor { get; } = new(() => []);

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public void ReloadRecipes()
        {
            Reloads++;
            if (ReloadFailure is not null) throw ReloadFailure;
            Installed = Store.LoadAll().Recipes;
        }

        public void AddTrail(string text) => TrailLines.Add(text);

        public Settings Settings => throw new NotSupportedException();

        public SharedAccounts Accounts => throw new NotSupportedException();

        public AccountsCache AccountsCache => throw new NotSupportedException();

        public IReadOnlyList<HostAccount> KnownAccounts => throw new NotSupportedException();

        public IScoreBook Book => throw new NotSupportedException();

        public ScoreBookReader Reader => throw new NotSupportedException();

        public bool ReaderLoaded => throw new NotSupportedException();

        public Task ReloadBookAsync() => throw new NotSupportedException();

        public BookExport ExportStats(string path) => throw new NotSupportedException();

        public bool Running => throw new NotSupportedException();

        public bool EverStarted => throw new NotSupportedException();

        public IReadOnlyDictionary<string, RecipeSnapshot> Latest => throw new NotSupportedException();

        public string? BudgetWarning => throw new NotSupportedException();

        public string HostText => throw new NotSupportedException();

        public string RulesPath => throw new NotSupportedException();

        public IReadOnlyList<string> Trail => TrailLines;

        public DateTimeOffset? LastReadAt(string sourceId) => throw new NotSupportedException();

        public (int Sent, int Dropped, int Held) PolicyCounts(string recipeSlug) => throw new NotSupportedException();

        public string? IconFileFor(string recipeSlug) => throw new NotSupportedException();

        public string? AvatarFileFor(long userId) => throw new NotSupportedException();

        public void SaveSources(IReadOnlyList<Source> sources) => throw new NotSupportedException();

        public void SaveSettings(Settings settings) => throw new NotSupportedException();

        public void SaveRecipeState(Recipe recipe, RecipeState state) => throw new NotSupportedException();

        public void RemoveRecipe(string slug) => throw new NotSupportedException();

        public Task<RecipeSnapshot?> ReadOnceAsync(string sourceId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SearchListResult> SearchListAsync(RecipeSearch search, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CounterLookup> ReadCounterNamesAsync(Recipe recipe, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AccountList> RefreshAccountsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
