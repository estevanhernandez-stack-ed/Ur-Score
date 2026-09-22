using Labs626.UrScore.Board;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace Labs626.UrScore.Composition;

/// <summary>
/// Everything a Setup page may use. <c>AppServices</c> (Task 14) implements it. Every member is used from
/// the UI thread, and <see cref="Changed"/> is raised on it.
/// </summary>
public interface ISetupServices
{
    /// <summary>Where every file lives on this machine.</summary>
    AppPaths Paths { get; }

    /// <summary>Installed recipes as last loaded, in file order.</summary>
    IReadOnlyList<InstalledRecipe> Installed { get; }

    /// <summary>Recipe files that could not be read, already redacted.</summary>
    IReadOnlyList<string> RecipeProblems { get; }

    IReadOnlyList<Source> Sources { get; }

    RecipeStore Store { get; }

    IKeyStore Keys { get; }

    Redactor Redactor { get; }

    Settings Settings { get; }

    SharedAccounts Accounts { get; }

    AccountsCache AccountsCache { get; }

    /// <summary>RoRoRo's last list this session, else the saved one from <c>accounts.json</c>. Your accounts only.</summary>
    IReadOnlyList<HostAccount> KnownAccounts { get; }

    IScoreBook Book { get; }

    /// <summary>What boards.json holds — following entries and your own boards — as last loaded; empty while there is no file.</summary>
    IReadOnlyList<BoardDef> SavedBoards { get; }

    /// <summary>The stores a setup import writes through (<see cref="Core.SetupMerge.Apply"/>).</summary>
    Core.ISetupWriter SetupWriter { get; }

    ScoreBookReader Reader { get; }

    bool ReaderLoaded { get; }

    /// <summary>Reads the score book again, after something outside a read has added to it (importing another PC's stats).</summary>
    Task ReloadBookAsync();

    /// <summary>
    /// Writes the score book to a stats file at <paramref name="path"/> for another PC to import (<see cref="Book.BookPack"/>),
    /// pending lines flushed first so the file holds every reading taken. Returns what the file's manifest says. Off the UI
    /// thread: it reads the whole book.
    /// </summary>
    Book.BookPackManifest ExportStats(string path);

    bool Running { get; }

    /// <summary>The newest snapshot per source id, from a timed read, Test now, or a Setup read-once.</summary>
    IReadOnlyDictionary<string, RecipeSnapshot> Latest { get; }

    DateTimeOffset? LastReadAt(string sourceId);

    /// <summary>Reports sent and dropped this session, summed over a recipe's sources.</summary>
    (int Sent, int Dropped, int Held) PolicyCounts(string recipeSlug);

    /// <summary>
    /// The picture for a recipe's row: its own main clan's, once a read has named one (or last session's is in the cache), else
    /// null. Never another clan's (backlog V3-S.7).
    /// </summary>
    string? IconFileFor(string recipeSlug);

    /// <summary>The cached picture for one of your own accounts, once it has been fetched, else null. Never another player's.</summary>
    string? AvatarFileFor(long userId);

    /// <summary>The history-budget warning once RoRoRo's accounts are known, else null.</summary>
    string? BudgetWarning { get; }

    /// <summary>"host=1.28.0.0 reject=(none)", for diagnostics.</summary>
    string HostText { get; }

    /// <summary>The metric rules file Setup › Alerts reads and writes: RoRoRo's, unless a walk names a scratch copy (plan A1).</summary>
    string RulesPath { get; }

    /// <summary>The newest trail lines, redacted, oldest first.</summary>
    IReadOnlyList<string> Trail { get; }

    event Action? Changed;

    /// <summary>Saves <c>sources.json</c>, applies it to the running watches at once, and raises <see cref="Changed"/>.</summary>
    void SaveSources(IReadOnlyList<Source> sources);

    /// <summary>Replaces the saved boards wholesale: sanitized to your own ids, written once, redrawn. The setup import's step 4.</summary>
    void SaveImportedBoards(IReadOnlyList<BoardDef> saved);

    /// <summary>
    /// Writes <c>settings.json</c> and raises <see cref="Changed"/>. Throws when it can't be written, and nothing
    /// changes then; no page edits that file itself.
    /// </summary>
    void SaveSettings(Settings settings);

    /// <summary>Reloads recipes from disk, migrates sources for any new recipe, applies, and raises <see cref="Changed"/>.</summary>
    void ReloadRecipes();

    /// <summary>Saves one recipe's state (stats, Send per account, counter names) and updates its watches.</summary>
    void SaveRecipeState(Recipe recipe, RecipeState state);

    /// <summary>Removes a recipe file and its sources. Never touches its score book.</summary>
    void RemoveRecipe(string slug);

    /// <summary>Reads one source once, right now, and records it like any read. Null when that source has no watch yet.</summary>
    Task<RecipeSnapshot?> ReadOnceAsync(string sourceId, CancellationToken cancellationToken);

    Task<SearchListResult> SearchListAsync(RecipeSearch search, CancellationToken cancellationToken);

    /// <summary>One read with every recipe value asked for, so the response can offer its counter names. Sends nothing.</summary>
    Task<CounterLookup> ReadCounterNamesAsync(Recipe recipe, CancellationToken cancellationToken);

    Task<AccountList> RefreshAccountsAsync(CancellationToken cancellationToken);

    void AddTrail(string text);
}
