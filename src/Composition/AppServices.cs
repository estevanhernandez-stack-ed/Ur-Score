using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using Grpc.Core;
using Labs626.UrScore.Board;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Games;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.Theming;
using Labs626.UrScore.UI;

namespace Labs626.UrScore.Composition;

using NameClient = Labs626.UrScore.Fetch.NameClient;
using IconClient = Labs626.UrScore.Fetch.IconClient;
using AvatarBook = Labs626.UrScore.Fetch.AvatarBook;
using SourceIcons = Labs626.UrScore.Fetch.SourceIcons;

/// <summary>
/// Everything the app runs on, built once (spec §4.2, §5, §7). The one place a watch is constructed: each
/// source gets its watch from <see cref="CreateWatch"/>, called by <see cref="SourceHost"/> once per source
/// id, and updated in place after that (F2: a watch built per cycle has a fresh serialization guard, and
/// one observation could be reported twice).
/// </summary>
public sealed class AppServices : ISetupServices, IDisposable
{
    public const string PluginId = "626labs.ur-score";

    public const string SourcesNotWritten =
        "Your sources file couldn't be read when Ur Score started, so it isn't written over. Fix or remove sources.json, then restart Ur Score.";

    public const string SettingsNotWritten =
        "Your settings file couldn't be read when Ur Score started, so it isn't written over. Fix or remove settings.json, then restart Ur Score.";

    /// <summary>
    /// The longest a caller waits for RoRoRo's accounts. Each host call is bounded at 5 s already; this also
    /// covers a watch's fetch holding the shared one. Past it, the saved list stands in.
    /// </summary>
    public static readonly TimeSpan AccountsWait = TimeSpan.FromSeconds(20);

    private const int TrailLimit = 400;

    /// <summary>How long to wait before asking RoRoRo for its theme again after it was not there.</summary>
    private static readonly TimeSpan ThemeRetry = TimeSpan.FromSeconds(15);

    private readonly Dispatcher _ui;
    private readonly TimeProvider _time;
    private readonly AppPaths _paths;
    private readonly HttpClient _recipeHttp = new(HttpRecipeTransport.CreateHandler());
    private readonly HttpClient _namesHttp = new();

    /// <summary>RoRoRo, as the watches and the account list see it: the pipe in the app, a stub in a test (S1-14.9).</summary>
    private readonly IHostClient _host;

    /// <summary>The real pipe client behind <see cref="_host"/> when there is one; null in a test, where there is no pipe to speak of.</summary>
    private readonly HostClient? _pipe;

    /// <summary>Its own connection, so the long-lived theme stream never shares a channel with reports.</summary>
    private readonly HostClient _themeHost = new(PluginId);

    private readonly CancellationTokenSource _closing = new();
    private readonly RecipeEngine _engine;
    private readonly AccountClaims _claims;
    private readonly ScoreBook _book;
    private readonly IconClient _icons;
    private readonly AvatarBook _avatars;

    /// <summary>Each source's own picture, never one per recipe (backlog V3-S.7), and back from the cache at start (V3-S.6).</summary>
    private readonly SourceIcons _sourceIcons;
    private readonly SearchLists _searchLists;
    private readonly SourceStore _sourceStore;
    private readonly BoardsFile _boardsFile;

    /// <summary>What <c>boards.json</c> holds, or null while there is no file (R1) or it couldn't be read (R3).</summary>
    private IReadOnlyList<BoardDef>? _savedBoards;

    /// <summary><c>boards.json</c> was there at start and couldn't be read, for any reason: the first save keeps a copy whatever it holds by then (R3).</summary>
    private bool _boardsUnread;

    /// <summary>
    /// Held while a refresh reads what is installed and hands each watch its policy, and while a save replaces
    /// what is installed. Two refreshes race otherwise: RoRoRo lists accounts on a fetching thread and refreshes
    /// every policy from there (F8, before that read sends), while the user unticks a stat on the UI thread and
    /// refreshes from a newer state. The background one could read the OLD state, be overtaken, and then land
    /// its old policy on top of the new — and a stat the user had just excluded went out once more before the
    /// next refresh put it right (S1-14.13). Under one gate the two cannot interleave, so the last policy
    /// applied is always from the last state saved.
    /// </summary>
    private readonly object _refreshGate = new();
    private readonly List<string> _trail = [];
    private readonly Dictionary<string, RecipeSnapshot> _latest = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _lastRead = new(StringComparer.Ordinal);

    /// <summary>The last numbers the score book kept, per source, until that source is read this session (plan A38).</summary>
    private IReadOnlyDictionary<string, RecipeSnapshot> _remembered = new Dictionary<string, RecipeSnapshot>(StringComparer.Ordinal);

    private readonly HashSet<string> _missesInTrail = new(StringComparer.Ordinal);

    /// <summary>What each source's watch was last given to read, so a change elsewhere doesn't swap an unchanged recipe under a read in flight.</summary>
    private readonly Dictionary<string, WatchInputs> _given = new(StringComparer.Ordinal);

    private IReadOnlyList<HostAccount> _savedAccounts;
    private FinalsIndex? _finals;
    private readonly BookLoader _bookLoader = new();
    private int? _budgetWarnedCount;
    private bool _changePending;

    /// <summary>The icon the window was last given, so a change raises <see cref="IconChanged"/> once.</summary>
    private WindowIcon _windowIcon = WindowIcon.None;

    /// <summary><c>sources.json</c> was there at start but could not be read: this session never writes over it.</summary>
    private bool _sourcesUnreadable;

    /// <summary><c>settings.json</c> was there at start but could not be read: the defaults run in memory and this session never writes over it.</summary>
    private bool _settingsUnreadable;

    /// <summary>
    /// The slugs whose <c>{slug}.recipe.json</c> was on disk, when this start turned them into a modes map (A5); null on any
    /// other start. <see cref="LoadAtStart"/> reads it.
    /// </summary>
    private IReadOnlyList<string>? _upgradedFrom;

    private static int _ownCompositions;

    /// <summary>How many times the app's own composition (the user's real data folder) was built in this process; never in a test.</summary>
    internal static int OwnCompositions => Volatile.Read(ref _ownCompositions);

    /// <summary>
    /// The app's own composition: the user's data folder, RoRoRo's pipe, the network, the wall clock. In a test process it
    /// throws before building anything, since <see cref="AppPaths.Default"/> is refused there.
    /// </summary>
    public AppServices(Dispatcher ui)
        : this(ui, AppPaths.Default, host: null, transport: null, TimeProvider.System,
            RulesFile.ResolvePath(Environment.GetEnvironmentVariable(RulesFile.PathVariable)))
    {
        Interlocked.Increment(ref _ownCompositions);
    }

    /// <summary>
    /// The composition with its seams open, for a test that composes the whole app against a folder of its own
    /// (S1-14.9). A null <paramref name="host"/> or <paramref name="transport"/> means the real one — RoRoRo's pipe,
    /// the HTTP transport — so a test that leaves either null has built the app for real and should know it: a
    /// dev build is indistinguishable from the installed plugin to RoRoRo's host (V3-S.43).
    /// </summary>
    /// <param name="paths">Where every file lives; <see cref="AppPaths.Default"/> in the app.</param>
    /// <param name="host">RoRoRo, for the watches and the account list. The theme stream keeps its own real client either way and is started only by the window.</param>
    /// <param name="transport">What recipes fetch through; null builds the HTTP transport, spaced, with the redactor.</param>
    /// <param name="time">The clock everything reads: the watches' timestamps, the reader's cutoff, the host's intervals.</param>
    /// <param name="rulesPath">RoRoRo's rules file, or the one <c>UR_SCORE_RULES_FILE</c> names.</param>
    public AppServices(Dispatcher ui, AppPaths paths, IHostClient? host, IRecipeTransport? transport, TimeProvider time, string rulesPath)
    {
        _ui = ui;
        _time = time;
        _paths = paths;

        // Before ANYTHING below writes to the folder (settings.json, sources.json on a fresh start): whether this is an
        // upgrade, and what it had installed, is a fact about the folder as the last version left it (A5).
        var dataFolderExisted = UpgradeModes.DataFolderExisted(paths);
        var installedOnDisk = UpgradeModes.InstalledSlugs(paths);
        _pipe = host is null ? new HostClient(PluginId) : null;
        _host = host ?? _pipe!;
        RulesPath = rulesPath;
        SetupWriter = new SetupImportWriter(this);
        _sourceStore = new SourceStore(paths.Sources);
        _boardsFile = new BoardsFile(paths.Boards, time);
        Keys = new KeyStore(paths.Keys);
        var keys = Keys;
        Redactor = new Redactor(() => keys.Values());
        Store = new RecipeStore(paths.Recipes);
        var settingsLoad = Settings.LoadResult(paths.Settings);
        _settings = settingsLoad.Settings;
        _settingsPath = paths.Settings;

        // A mode the manifest gets wrong is dropped with a trail line, never a crash at start (spec "GameCatalog").
        // A manifest that doesn't parse at all is left out of BuiltIn with its own line (review round 2); same trail, same list.
        (Catalog, var manifestProblems) = Readers.Sound(GameCatalog.BuiltIn, BuiltInRecipes.BySlug);
        _manifestProblems = [.. GameCatalog.BuiltInProblems.Concat(manifestProblems).Select(p => $"game manifest: {p}")];
        foreach (var problem in _manifestProblems) AddTrail($"MODE DROPPED: {problem}");
        _switches = new ModeSwitches(Catalog, _settings.Modes);
        DecideUpgradeModes(settingsLoad.File, dataFolderExisted, installedOnDisk);

        // A walk's scratch rules file is never silent: Diagnostics' trail says which file alerts use.
        if (!string.Equals(RulesPath, RulesFile.DefaultPathUnder(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)), StringComparison.OrdinalIgnoreCase))
        {
            AddTrail($"RULES: alerts use the file {RulesFile.PathVariable} names, not RoRoRo's.");
        }

        // No raw responses kept: a response body holds every row the source returned, other players' ids and values
        // included, and other players never reach disk.
        DeleteOldRawResponses(paths.LastResponse);
        transport ??= new SpacedTransport(new HttpRecipeTransport(_recipeHttp, rawDirectory: null, Redactor), _time, SpacedTransport.DefaultSpacing);
        _engine = new RecipeEngine(transport, Keys);
        _searchLists = new SearchLists(transport);

        AccountsCache = new AccountsCache(paths.Accounts);
        _savedAccounts = LoadSavedAccounts(AccountsCache);
        Accounts = new SharedAccounts(_host, AccountsCache, _time);
        Accounts.Listed += OnListed;
        _claims = new AccountClaims(_time);

        _book = new ScoreBook(paths.Book);
        Reader = new ScoreBookReader(paths.Book, _time);

        Names = new NameClient(_namesHttp);
        _icons = new IconClient(HttpRecipeTransport.CreateHandler(), paths.IconCache, () => _time.GetUtcNow());
        _avatars = new AvatarBook(_icons);
        _sourceIcons = new SourceIcons(_icons, Path.Combine(paths.IconCache, SourceIcons.FileName));

        Runner = new SourceHost(CreateWatch, IntervalFor, _time);
        Runner.SnapshotReady += OnSnapshotReady;

        LoadAtStart();
        LoadBoards();

        // Once the sources are known, and before the window exists: each source's picture from last session, from disk alone,
        // so the window, the taskbar and every Standing panel open on them rather than waiting for a read (V3-S.6).
        _sourceIcons.Restore(IconHostsFor);
        _windowIcon = WindowIcon;
    }

    // ---- ISetupServices ----

    public AppPaths Paths => _paths;

    public IReadOnlyList<InstalledRecipe> Installed { get; private set; } = [];

    public IReadOnlyList<string> RecipeProblems { get; private set; } = [];

    public IReadOnlyList<Source> Sources { get; private set; } = [];

    /// <summary>The games and modes this version knows, less any mode its manifest got wrong (dropped at start with a trail line).</summary>
    public GameCatalog Catalog { get; }

    /// <summary>Which modes are on, from <see cref="Settings"/>; rebuilt whenever the settings change, so the two can't disagree.</summary>
    public ModeSwitches Switches => _switches;

    /// <summary>Data-folder recipes no mode names: kept on disk, listed in Diagnostics, never read (spec "Readers").</summary>
    public IReadOnlyList<InstalledRecipe> Orphans { get; private set; } = [];

    /// <summary>
    /// The sources the runner may read: those whose reader's mode is on. Mode-off is a gate above <see cref="Source.Enabled"/>
    /// (spec decision 3), applied here and never by flipping sources, so a clan's own on/off survives any number of toggles.
    /// An orphan's sources are never active.
    /// </summary>
    public IReadOnlyList<Source> ActiveSources
    {
        get
        {
            var switches = _switches;
            return [.. Sources.Where(s => switches.IsReaderOn(s.Recipe))];
        }
    }

    public RecipeStore Store { get; }

    public IKeyStore Keys { get; }

    public Redactor Redactor { get; }

    public Settings Settings => _settings;

    private Settings _settings;

    private ModeSwitches _switches;

    /// <summary>What the startup check of the game manifest found, already in the trail; part of <see cref="RecipeProblems"/>.</summary>
    private readonly IReadOnlyList<string> _manifestProblems;

    public SharedAccounts Accounts { get; }

    public AccountsCache AccountsCache { get; }

    public IReadOnlyList<HostAccount> KnownAccounts => Accounts.Last?.Accounts is { Count: > 0 } listed ? listed : _savedAccounts;

    public IScoreBook Book => _book;

    /// <summary>What boards.json holds — following entries and your own boards — as last loaded; empty while there is no file.</summary>
    public IReadOnlyList<BoardDef> SavedBoards => _savedBoards ?? [];

    /// <summary>The stores a setup import writes through, built once over this instance.</summary>
    public Core.ISetupWriter SetupWriter { get; }

    /// <summary>
    /// Writes the book, with this machine's setup alongside it (<see cref="Core.SetupPack.FromHere"/>) so the other PC's
    /// import has recipes, clans, boards and the two settings to offer, not stats alone. A setup with nothing in it
    /// (<see cref="Core.SetupPack.IsEmpty"/>) is not attached at all: the manifest then says <c>setup: false</c> and the
    /// other side takes the plain stats merge. Hands back the pack it wrote, so the page's line counts the file's own
    /// contents rather than building a second pack that could differ.
    /// </summary>
    public BookExport ExportStats(string path)
    {
        _book.Flush();
        var version = typeof(AppServices).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var built = SetupPack.FromHere(Installed, Sources, SavedBoards, Settings, KnownAccounts, Catalog);
        var setup = built.IsEmpty ? null : built;
        return new BookExport(BookPack.Write(_book.Root, path, version, _time.GetUtcNow(), setup), setup);
    }

    public ScoreBookReader Reader { get; }

    public bool ReaderLoaded { get; private set; }

    public bool Running => Runner.Running;

    public IReadOnlyDictionary<string, RecipeSnapshot> Latest => _latest;

    public string? BudgetWarning { get; private set; }

    public string HostText => _pipe is null
        ? "host=(not RoRoRo's pipe: a client the composition was given)"
        : $"host={_pipe.HostVersion ?? "(not connected)"} reject={_pipe.RejectReason ?? "(none)"}";

    /// <summary>RoRoRo's rules file, or the full path <c>UR_SCORE_RULES_FILE</c> names: the Setup › Alerts walk's scratch copy (plan A1).</summary>
    public string RulesPath { get; }

    private readonly string _settingsPath;

    public IReadOnlyList<string> Trail
    {
        get
        {
            lock (_trail) return [.. _trail];
        }
    }

    public event Action? Changed;

    // ---- the board ----

    public SourceHost Runner { get; }

    public NameClient Names { get; }

    /// <summary>Stopped after at least one Start this session, as opposed to never started.</summary>
    public bool EverStarted { get; private set; }

    /// <summary>When reading last stopped, or null if it never ran. The board's stopped line says only reads asked for since (S1-14.5).</summary>
    public DateTimeOffset? StoppedAt { get; private set; }

    /// <summary>When a read was last asked for by hand: Test now, or Setup reading a source once. The board says what it found (S1-14.5).</summary>
    public DateTimeOffset? AskedReadAt { get; private set; }

    /// <summary>The window's icon changed: its picture, or none for Ur Score's own, and whose it is. Raised on the UI thread.</summary>
    public event Action<WindowIcon>? IconChanged;

    public DateTimeOffset? LastReadAt(string sourceId) => _lastRead.TryGetValue(sourceId, out var at) ? at : null;

    public (int Sent, int Dropped, int Held) PolicyCounts(string recipeSlug)
    {
        int sent = 0, dropped = 0, held = 0;
        foreach (var source in Sources.Where(s => string.Equals(s.Recipe, recipeSlug, StringComparison.Ordinal)))
        {
            if (Runner.WatchFor(source.Id) is not { } watch) continue;
            sent += watch.Policy.Sent;
            dropped += watch.Policy.Dropped;
            held += watch.Policy.Held;
        }

        return (sent, dropped, held);
    }

    /// <summary>
    /// The window's icon: the main clan's picture and never another clan's, whichever was read last (backlog V3-S.7). With no
    /// main, Ur Score's own.
    /// </summary>
    public WindowIcon WindowIcon => IconChoice.ForWindow(Sources, Installed, _sourceIcons.FileFor);

    /// <summary>The picture for one of your own accounts (plan A22): an id RoRoRo isn't listing as yours has none.</summary>
    public string? AvatarFileFor(long userId) =>
        LiveBoard.UserIdsOf(KnownAccounts).Contains(userId) ? _avatars.FileFor(userId) : null;

    public LiveBoard CurrentBoard() => new(
        Sources, Installed,
        new Dictionary<string, RecipeSnapshot>(_latest, StringComparer.Ordinal),
        new Dictionary<string, DateTimeOffset>(_lastRead, StringComparer.Ordinal),
        KnownAccounts, _time, Runner.Running, _avatars.Files, _remembered, _sourceIcons.Files, OffReaders(), ReaderLabels());

    /// <summary>
    /// The readers a panel can't draw: those of an off mode (with the switch that turns it back on) and the orphans, which
    /// belong to no mode. A panel built on one of them says so in place of numbers it isn't reading.
    /// </summary>
    private Dictionary<string, string> ReaderLabels() =>
        Installed.Concat(Orphans).ToDictionary(i => i.Recipe.Slug, i => ReaderNames.For(i.Recipe.Slug, Catalog, Installed), StringComparer.Ordinal);

    private Dictionary<string, ReaderOff> OffReaders()
    {
        var off = new Dictionary<string, ReaderOff>(StringComparer.Ordinal);
        foreach (var installed in Installed)
        {
            if (Catalog.ModeOf(installed.Recipe.Slug) is { } mode && !_switches.IsOn(mode.Key)) off[installed.Recipe.Slug] = new ReaderOff(mode.Key, mode.Name);
        }

        foreach (var orphan in Orphans) off[orphan.Recipe.Slug] = new ReaderOff(null);
        return off;
    }

    /// <summary>
    /// The boards on screen: the saved ones, with each tab that still follows a starter rebuilt from your sources and
    /// shown while it has panels; with nothing saved, every starter follows (D1–D4). Never empty.
    /// </summary>
    public IReadOnlyList<BoardDef> Boards => Following.Shown(_savedBoards, Starters());

    /// <summary>
    /// The starters, each built with its mode's state: an off mode's starter is still built (A2), empty and
    /// <see cref="BoardEmpty.ModeOff"/>, so a following tab hides rather than being forgotten by the next save.
    /// </summary>
    private IReadOnlyList<StarterBoard> Starters() => StarterBoards.All(Installed, Sources, OffModeName);

    /// <summary>The name of the mode a starter board belongs to when that mode is off, else null (a starter no mode names is never off).</summary>
    public string? OffModeName(string starterKey) =>
        Catalog.Modes.FirstOrDefault(m => string.Equals(m.Board, starterKey, StringComparison.Ordinal)) is { } mode && !_switches.IsOn(mode.Key)
            ? mode.Name
            : null;

    /// <summary>Why the saved boards aren't showing, or null.</summary>
    public string? BoardsProblem { get; private set; }

    /// <summary>
    /// Writes <c>boards.json</c> with only your own account ids (R17) and redraws. A following tab you didn't change stays
    /// a following entry, and one you changed is written as it is (D2, <see cref="Following.ToSave"/>). The old file is
    /// kept beside it when it doesn't parse, and on the first save after it couldn't be read at start (R3). Throws when
    /// the file can't be written; nothing changes then.
    /// </summary>
    public void SaveBoards(IReadOnlyList<BoardDef> boards)
    {
        var clean = BoardDefs.Sanitize(Following.ToSave(_savedBoards, Starters(), boards), LiveBoard.UserIdsOf(KnownAccounts));

        var kept = _boardsFile.Save(clean, keepExisting: _boardsUnread);
        _boardsUnread = false;

        _savedBoards = clean.Count > 0 ? clean : null;
        BoardsProblem = null;
        if (kept is not null) AddTrail($"BOARDS: the unreadable boards file was kept as {Path.GetFileName(kept)}.");

        RaiseChanged();
    }

    /// <summary>
    /// Replaces the saved boards wholesale: every following tab stays, shown or hidden — an import deletes nothing
    /// (spec §2, <see cref="Board.Following.KeepFollowing"/>) — sanitized to your own ids (R17), written once, redrawn.
    /// Mirrors <see cref="SaveBoards"/>'s own handling of an old file that couldn't be read at start (R3) and the
    /// trail line it leaves. The setup import's step 4.
    /// </summary>
    public void SaveImportedBoards(IReadOnlyList<BoardDef> saved)
    {
        var clean = BoardDefs.Sanitize(Following.KeepFollowing(_savedBoards, Starters(), saved), LiveBoard.UserIdsOf(KnownAccounts));

        var kept = _boardsFile.Save(clean, keepExisting: _boardsUnread);
        _boardsUnread = false;

        _savedBoards = clean.Count > 0 ? clean : null;
        BoardsProblem = null;
        if (kept is not null) AddTrail($"BOARDS: the unreadable boards file was kept as {Path.GetFileName(kept)}.");

        RaiseChanged();
    }

    /// <summary>
    /// Reads the finals index and the book on a worker thread, before any watch exists, then applies the
    /// sources. Nothing else touches the reader until this returns; after it, only the UI thread does.
    /// A second call while it runs (or after it finished) gets the same load, so the book is read once.
    /// </summary>
    public Task LoadBookAsync() => _bookLoader.LoadAsync(LoadBookOnceAsync);

    /// <summary>
    /// Reads the book again after something outside a read wrote to it — bringing in another PC's book. The reader
    /// replaces a slug's data on load, so this is safe to call over a book already read, and the panels redraw from it.
    /// </summary>
    public async Task ReloadBookAsync()
    {
        var root = _book.Root;
        var reader = Reader;
        var skipped = await Task.Run(() => reader.Load(BookFiles.Slugs(root)));
        if (skipped > 0) AddTrail(SkippedLines(skipped));
        RaiseChanged();
    }

    /// <summary>
    /// Said in the trail whenever a load left lines behind. A count and nothing else — a corrupt line's text
    /// could hold anything, and Diagnostics is copied and pasted (S1-F.10).
    /// </summary>
    internal static string SkippedLines(int count) =>
        $"BOOK: {count} line(s) could not be read and were skipped; the rest of the book loaded.";

    private async Task LoadBookOnceAsync()
    {
        var root = _book.Root;
        var reader = Reader;
        int skipped;
        // One walk of the files for both (S1-F.2). The index counts the same unreadable lines the reader does.
        (_finals, skipped) = await Task.Run(() =>
        {
            var index = new FinalsIndex();
            var unread = reader.Load(BookFiles.Slugs(root), index);
            return (index, Math.Max(index.Skipped, unread));
        });

        ReaderLoaded = true;
        _book.Written += OnWritten;
        ApplyRunner();
        AddTrail($"BOOK: loaded from {root}.");
        // The reader and the index count the same bad lines from the same pass; one count, the larger.
        if (skipped > 0) AddTrail(SkippedLines(skipped));
        AskForAvatars();
        RaiseChanged();

        // Not awaited: BoardWindow awaits LoadBookAsync before the window is usable, and asking RoRoRo must never
        // hold that up.
        _ = OpenOnLastNumbersAsync();
    }

    /// <summary>
    /// Fills the remembered map for the first time, once RoRoRo has been asked who your accounts are (review I2).
    /// <para>
    /// Which ids are yours decides which of the book's rows may come back (A42), and on window open nothing has asked
    /// RoRoRo yet — <see cref="RefreshAccountsAsync"/> runs from Start, Test now, Setup and the import flow, all of
    /// them later than this. Filling from Ur Score's own cache alone would put an account RoRoRo dropped between
    /// sessions back on the board with its last number, so the map stays empty until the answer lands: a beat of
    /// "waiting for the first read" beats a number for an account you no longer have.
    /// </para>
    /// <para>
    /// The ask is bounded and never fails for RoRoRo's sake; when RoRoRo doesn't answer, the saved list stands in, as
    /// it does everywhere else, and the numbers are marked <c>remembered</c> either way. Every later listing
    /// re-filters through <see cref="OnListed"/>.
    /// </para>
    /// </summary>
    private async Task OpenOnLastNumbersAsync()
    {
        try
        {
            await RefreshAccountsAsync(_closing.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // The type only: a message can carry anything. The board opens on the saved list's ids either way.
            AddTrail($"ACCOUNTS NOT LISTED AT START: {ex.GetType().Name}.");
        }

        // Explicit rather than relying on OnListed: a fetch that threw before it raised Listed must still leave the
        // window with something real in it.
        RememberLastNumbers();
        AddTrail($"OPENED ON: {_remembered.Count} source(s) drawing their last kept numbers.");
        RaiseChanged();
    }

    public async Task StartAsync()
    {
        if (!ReaderLoaded || Runner.Running) return;

        await RefreshAccountsAsync(_closing.Token);
        Runner.Start();
        EverStarted = true;
        AddTrail("STARTED");
        RaiseChanged();
    }

    public void Stop()
    {
        Runner.Stop();
        StoppedAt = _time.GetUtcNow();
        AddTrail("STOPPED");
        RaiseChanged();
    }

    public async Task TestNowAsync()
    {
        if (!ReaderLoaded) return;

        AskedReadAt = _time.GetUtcNow();
        await RefreshAccountsAsync(_closing.Token);
        await Runner.RunAllNowAsync(BookLine.TriggerManual, _closing.Token);
    }

    public void SaveSources(IReadOnlyList<Source> sources)
    {
        // Refused before anything changes, so the page says why and the running sources stay what the file would hold.
        if (_sourcesUnreadable) throw new InvalidOperationException(SourcesNotWritten);

        _sourceStore.Save(sources);
        Sources = sources;
        ApplySources();
    }

    /// <summary>
    /// Writes <c>settings.json</c> and redraws. The switches are derived from the settings here, so whatever writes them
    /// (a toggle, an imported setup, A7) turns modes on and off live: reading follows at once. Otherwise nothing running
    /// changes: the other keys are read when the board next opens (plan A33). Qualified as <c>Core.Settings</c> because
    /// the property beside it has that name.
    /// </summary>
    public void SaveSettings(Settings settings)
    {
        // Refused before anything changes, as sources.json is: the page says why, and the file stays what the player can fix.
        if (_settingsUnreadable) throw new InvalidOperationException(SettingsNotWritten);

        Core.Settings.Save(settings, _settingsPath);
        var before = _switches;
        _settings = settings;
        _switches = new ModeSwitches(Catalog, settings.Modes);

        if (Catalog.Modes.Any(m => before.IsOn(m.Key) != _switches.IsOn(m.Key)))
        {
            ModesChanged(before);
            return;
        }

        RaiseChanged();
    }

    /// <summary>
    /// Turns a game or a mode on or off (spec "Composition"): saved, applied to the runner at once, redrawn. Off cancels
    /// the watch of every source the mode reads, exactly as a disabled source's; nothing about the sources, their ticks or
    /// their book changes, so on brings back the same reading. Throws when settings.json can't be written, and nothing
    /// changes then.
    /// </summary>
    public void SetSwitch(string key, bool on) => SaveSettings(_settings with { Modes = _switches.With(key, on) });

    /// <summary>
    /// After the switches changed: a reader newly on that has no inputs and no source gets its one source, as an import
    /// did (<see cref="SourceRules.ForNewRecipes"/>); a Battle-only upgrader who turns Profile on would otherwise have no
    /// screen that can make one. Then the runner follows, with a trail line naming what changed.
    /// </summary>
    private void ModesChanged(ModeSwitches before)
    {
        var sources = SourceRules.ForNewRecipes(Sources, ReadersOn(before), ReadersOn(_switches));
        if (!ReferenceEquals(sources, Sources))
        {
            Sources = sources;
            TrySaveSources(sources);
        }

        foreach (var mode in Catalog.Modes.Where(m => before.IsOn(m.Key) != _switches.IsOn(m.Key)))
        {
            AddTrail($"MODES: {mode.Name} is {(_switches.IsOn(mode.Key) ? "on" : "off")}.");
        }

        ApplySources();
    }

    public IReadOnlyList<InstalledRecipe> ActiveReaders => ReadersOn(_switches);

    public string? OffModeOf(string slug) =>
        Catalog.ModeOf(slug) is { } mode && !_switches.IsOn(mode.Key) ? mode.Name : null;

    /// <summary>The readers a set of switches lets read.</summary>
    private IReadOnlyList<InstalledRecipe> ReadersOn(ModeSwitches switches) =>
        [.. Installed.Where(i => switches.IsReaderOn(i.Recipe.Slug))];

    /// <summary>
    /// The first start of 0.7.0 over a 0.6.3 folder writes an explicit modes map, each mode on iff one of its readers was
    /// installed (A5), so the upgrade reads exactly what 0.6.3 read, and marks the file version 3 so it is decided once.
    /// <para>
    /// Gated on the FILE, not on the folder (review round 2): only a settings.json that was read and is below version 3
    /// is an upgrade. A missing one is a fresh start, or a file the player deleted, and both get the defaults (every mode
    /// at its manifest default) written as version 3; the old rule read a deleted file in a used folder as "an install that
    /// had nothing" and switched every mode off. An unreadable one runs on the defaults in memory and is never written
    /// this session, as sources.json is not. A write that fails costs a trail line; the next start decides the same way
    /// from the same file, never from an absence the failure left behind.
    /// </para>
    /// </summary>
    private void DecideUpgradeModes(SettingsFile file, bool dataFolderExisted, IReadOnlyList<string> installedOnDisk)
    {
        switch (file)
        {
            case SettingsFile.Unreadable:
                _settingsUnreadable = true;
                AddTrail($"SETTINGS NOT READ: {SettingsNotWritten}");
                return;
            case SettingsFile.Missing:
                // An empty map says "decided, all defaults" (ModeSwitches reads a missing key as the default).
                TrySaveSettings(Core.Settings.Defaults with { Modes = new Dictionary<string, bool>() });
                return;
        }

        if (_settings.SettingsVersion >= Core.Settings.CurrentVersion) return;

        // A file below 3 that already holds a map (only a pre-release 0.7.0 build wrote one) keeps it; so does a file in a
        // folder nothing else used, which is a fresh start's.
        if (UpgradeModes.FirstRun(Catalog, dataFolderExisted, installedOnDisk, _settings.Modes) is not { } modes)
        {
            TrySaveSettings(_settings with { Modes = _settings.Modes ?? new Dictionary<string, bool>(), SettingsVersion = Core.Settings.CurrentVersion });
            return;
        }

        TrySaveSettings(_settings with { Modes = modes, SettingsVersion = Core.Settings.CurrentVersion });
        _upgradedFrom = installedOnDisk;
        var on = Catalog.Modes.Where(m => _switches.IsOn(m.Key)).Select(m => m.Name).ToList();
        AddTrail($"MODES: set from what was installed before modes. On: {(on.Count == 0 ? "none" : string.Join(", ", on))}.");
    }

    /// <summary>
    /// The start's own settings write: applied this session whether or not it reaches disk, and a failure is a trail line,
    /// never a failed start. The decision is simply made again next start.
    /// </summary>
    private void TrySaveSettings(Settings settings)
    {
        _settings = settings;
        _switches = new ModeSwitches(Catalog, settings.Modes);
        if (_settingsUnreadable)
        {
            AddTrail($"SETTINGS NOT SAVED: {SettingsNotWritten}");
            return;
        }

        try
        {
            Core.Settings.Save(settings, _settingsPath);
        }
        catch (Exception ex)
        {
            AddTrail($"MODES NOT SAVED: {ex.GetType().Name}; this session reads with them anyway.");
        }
    }

    public void SaveRecipeState(Recipe recipe, RecipeState state)
    {
        Store.SaveState(recipe, state);
        LoadInstalled();
        ApplySources();
    }

    public async Task<RecipeSnapshot?> ReadOnceAsync(string sourceId, CancellationToken cancellationToken)
    {
        if (!ReaderLoaded) return null;

        AskedReadAt = _time.GetUtcNow();
        await RefreshAccountsAsync(cancellationToken);

        // Under the source's own token too: its mode switched off (or the source removed) mid-read cancels the read before
        // it writes a line or sends, and that is no failure of the caller's, so it is no snapshot rather than a throw.
        RecipeSnapshot? snapshot;
        try
        {
            snapshot = await Runner.ReadNowAsync(sourceId, BookLine.TriggerManual, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        if (snapshot is null) return null;
        Record(sourceId, snapshot, _time.GetUtcNow());
        return snapshot;
    }

    public Task<SearchListResult> SearchListAsync(RecipeSearch search, CancellationToken cancellationToken) =>
        _searchLists.GetAsync(search, cancellationToken);

    /// <summary>
    /// One read with every recipe value asked for, so the response can offer its counter names. Its reading
    /// goes to no report policy and no book: nothing read here can reach RoRoRo or disk.
    /// </summary>
    public async Task<CounterLookup> ReadCounterNamesAsync(Recipe recipe, CancellationToken cancellationToken)
    {
        try
        {
            await RefreshAccountsAsync(cancellationToken);
            var ids = KnownAccounts.Where(a => a.RobloxUserId != 0).Select(a => a.RobloxUserId).Distinct().ToList();
            var everyValue = recipe.LastStep.Values.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
            var inputs = Sources.FirstOrDefault(s => string.Equals(s.Recipe, recipe.Slug, StringComparison.Ordinal))?.Inputs
                         ?? new Dictionary<string, string>();

            var reading = await _engine.ReadAsync(recipe, inputs, ids, everyValue, cancellationToken);
            AddTrail($"READ STAT NAMES: {reading.Outcome}, {reading.CounterNames.Count} name(s). {reading.Detail}");

            if (reading.CounterNames.Count > 0) return new CounterLookup(reading.CounterNames, null);

            var problem = reading.Outcome != ReadingOutcome.Read ? reading.Detail
                : recipe.LastStep.PerAccount && ids.Count == 0 ? "RoRoRo hasn't shared any accounts yet, so there was nothing to read."
                : $"The source answered, but no {recipe.LastStep.Counters?.Label ?? "statistic names"} came back.";
            return new CounterLookup([], Redactor.Redact(problem));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new CounterLookup([], Redactor.Redact($"Could not read them: {ex.Message}"));
        }
    }

    /// <summary>
    /// Bounded by <see cref="AccountsWait"/>, and it never fails for RoRoRo's sake: a slow or broken answer
    /// gives the saved list, so Start, Test now and Setup always go on. Only the caller's own cancellation throws.
    /// </summary>
    public async Task<AccountList> RefreshAccountsAsync(CancellationToken cancellationToken)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closing.Token);
        bounded.CancelAfter(AccountsWait);

        AccountList list;
        try
        {
            list = await Accounts.GetAsync(bounded.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The type only: an exception's message is RoRoRo's words, not ours to keep.
            AddTrail($"ACCOUNTS NOT LISTED: {ex.GetType().Name}; using the saved list.");
            list = new AccountList(_savedAccounts, HostUp: false, FromCache: true, Denied: false, ListedAt: Accounts.Last?.ListedAt ?? AccountsCache.SavedAt());
        }

        if (list.Accounts.Count > 0) _savedAccounts = list.Accounts;
        if (list.Denied) AddTrail($"ACCOUNTS REFUSED: {RecipeWatch.RejectedMessage("host.queries.accounts")}");

        RefreshPolicies();
        WarnPastBudget();
        AskForAvatars();
        RaiseChanged();
        return list;
    }

    public void AddTrail(string text)
    {
        var line = $"{_time.GetUtcNow():O} {Redactor.Redact(text)}";
        lock (_trail)
        {
            _trail.Add(line);
            if (_trail.Count > TrailLimit) _trail.RemoveRange(0, _trail.Count - TrailLimit);
        }
    }

    /// <summary>Follows RoRoRo's theme for as long as the app runs, asking again every 15 s while RoRoRo is away.</summary>
    public void StartFollowingTheme() => _ = FollowThemeAsync(_closing.Token);

    public void Dispose()
    {
        _closing.Cancel();
        Accounts.Listed -= OnListed;
        Runner.SnapshotReady -= OnSnapshotReady;
        Runner.Stop();
        Runner.Dispose();

        // Pending lines are flushed on exit (spec §5.7).
        _book.Flush();
        _book.Dispose();

        _themeHost.Dispose();
        _pipe?.Dispose();
        _recipeHttp.Dispose();
        _namesHttp.Dispose();
        _icons.Dispose();

        // _closing is cancelled above and deliberately NOT disposed: its token has been handed to work that may
        // still be unwinding, and a disposed source throws from .Token, so a cancelled-but-undisposed source is
        // the safer end for a token that lives as long as the process (S1-14.12).
    }

    // ---- setup import ----

    /// <summary>
    /// <see cref="Core.SetupMerge.Apply"/>'s seam onto this instance: the same stores every other page writes
    /// through, named once so the apply is testable against a fake elsewhere and never grows a second writer of its
    /// own (setup-transfer design 2026-09-22, §4). Named <c>SetupImportWriter</c> rather than <c>SetupWriter</c> —
    /// the obvious name — because <see cref="ISetupServices.SetupWriter"/> already claims it for the property beside
    /// it, and a nested type cannot share a name with a member of its enclosing class (CS0102).
    /// </summary>
    private sealed class SetupImportWriter(AppServices owner) : Core.ISetupWriter
    {
        public string DataRoot => owner._paths.Root;

        public Core.SetupHere Here => new(owner.Installed, owner.Sources, owner.SavedBoards, owner.KnownAccounts, owner.Settings, owner.Catalog);

        public void SaveState(string slug, RecipeState state) =>
            owner.SaveRecipeState(owner.FindInstalled(slug)?.Recipe ?? throw new InvalidOperationException($"no reader {slug} here"), state);

        public void SaveSources(IReadOnlyList<Source> sources) => owner.SaveSources(sources);

        public void SaveImportedBoards(IReadOnlyList<BoardDef> saved) => owner.SaveImportedBoards(saved);

        public void SaveSettings(Settings settings) => owner.SaveSettings(settings);
    }

    // ---- watches ----

    /// <summary>A watch's recipe text, exact inputs and tracked stats, compared by value.</summary>
    private sealed record WatchInputs(string Text, string Inputs, string Tracked)
    {
        public static WatchInputs Of(InstalledRecipe installed, Source source, IReadOnlySet<string> tracked) => new(
            installed.Text,
            string.Join('\u001F', source.Inputs.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}")),
            string.Join('\u001F', tracked.Order(StringComparer.Ordinal)));
    }

    private RecipeWatch? CreateWatch(Source source)
    {
        if (FindInstalled(source.Recipe) is not { } installed) return null;

        var tracked = TrackedFor(installed);
        _given[source.Id] = WatchInputs.Of(installed, source, tracked);

        return new RecipeWatch(
            _engine, _host, Keys, PolicyFor(installed, source), installed.Recipe, source.Inputs, tracked,
            _book, source, Accounts, installed.Text, _claims, _finals, _time, MyGroupNames, WriteLabel);
    }

    /// <summary>
    /// The <see cref="RecipeWatch"/> <c>writeLabel</c> seam (task 8): puts the rival clan's name on Ur Score's own
    /// Level rule for the metric, through the one place that write can happen, and answers whether the number
    /// behind it may go out by <see cref="LabelInPlace"/>.
    /// <para>
    /// <see cref="AlertKind.Level"/>, not <see cref="AlertKind.Rate"/>: a threat number is a level (how far
    /// behind, how many hours), and Ur Score's own Level rule for the metric is the one <c>ChangeLabel</c> edits
    /// (see <see cref="RulesRead.OursFor"/>). The plan draft named a nonexistent <c>AlertKind.Below</c> — the
    /// enum is Rate, Level, Event, and direction is the separate <see cref="AlertRule.AlertWhenBelow"/> bool.
    /// </para>
    /// </summary>
    private bool WriteLabel(FieldMetric metric, string label) => WriteLabel(RulesPath, metric, label);

    /// <summary>
    /// The composition above with the rules path passed in, so a test can reach it. The instance method is the
    /// seam <see cref="CreateWatch"/> hands to <see cref="RecipeWatch"/>; this is the same single line with
    /// nothing captured.
    /// <para>
    /// Split out by the final review of this branch, 2026-09-20: it was the one composition on the branch no
    /// test could call. <see cref="LabelInPlace"/> is table-tested and <c>RecipeWatchBookTests</c> passes stub
    /// lambdas, so the line binding <see cref="FieldMetric.MetricId"/> and <see cref="AlertKind.Level"/> to
    /// <see cref="RulesFile.ChangeLabel"/> had no coverage at all. Bind <see cref="FieldMetric.Key"/> there
    /// instead and nothing fails: <c>GuardId</c> only rejects blank, <c>OursFor</c> finds no rule under
    /// "threat-gap", NotThere maps to true, and every threat number ships under whatever stale label is on disk,
    /// for good, with the suite green.
    /// </para>
    /// </summary>
    internal static bool WriteLabel(string rulesPath, FieldMetric metric, string label) =>
        LabelInPlace(RulesFile.ChangeLabel(rulesPath, metric.MetricId, AlertKind.Level, label));

    /// <summary>
    /// Maps <see cref="RulesFile.ChangeLabel"/>'s outcome to whether <see cref="RecipeWatch"/> may report the
    /// number that label names (controller ruling, task 8). A named method rather than an inline lambda, because
    /// this mapping is the entire remaining shape of the no-label-no-send half of the ordering invariant (design
    /// §1): nothing else on the branch can test that a real refusal stops a send until this exists.
    /// </summary>
    internal static bool LabelInPlace(RuleWrite write) => write switch
    {
        // The label says exactly what it should, whether this call just wrote it or ChangeLabel found it
        // already said so and made no write (also reported as Done — see RulesFile.ChangeLabel). Either way the
        // rule on disk names the right clan, so the number behind it may go out.
        RuleWrite.Done => true,

        // True, but NOT because "the send is refused by the policy anyway" (the plan's stated reason for this
        // case — FALSE: ReportPolicy.EvaluateField consults only the tick, SentFieldMetrics, and never reads
        // rules.json, so a ticked metric with no rule sends perfectly well). The real reason: no rule means no
        // alert can ever fire for this metric, so there is no stale name a phone could receive under. Nothing to
        // keep current, and nothing to guard against.
        RuleWrite.NotThere => true,

        // ChangeLabel never returns this today: Done already covers "the label already says this" with no write
        // (RulesFile.cs), and AlreadyThere is returned only by TurnOn's Add. Mapped true anyway, defensively —
        // an already-correct label IS a correct label, a success by the same test Done is. If ChangeLabel is
        // ever "tidied" to return the semantically obvious AlreadyThere for that case instead, a mapping that
        // omitted it would turn every cycle after the first into a silent refusal: threat alerts going quiet
        // forever, with nothing here to catch it.
        RuleWrite.AlreadyThere => true,

        // CantWrite, CantOpen, NotJson, NotAList: whatever the reason, the name on disk is not the name we
        // intend to send under. A threat number under a stale or unconfirmed name is a confident false statement
        // on somebody's phone mid-battle, which the owner's ruling of 2026-09-20 says is worse than silence.
        _ => false,
    };

    /// <summary>
    /// The clans you set up, by name, asked for at each read rather than captured: a clans list uses them to say where
    /// you stand in the field and what the place above you holds (<see cref="FieldSummary"/>). Only your own sources'
    /// own inputs — a group list has none of its own.
    /// <para>
    /// Handed over IN THE ORDER <see cref="SourceRules.MyClanNames"/> returns, not folded into a set here: the watch
    /// needs the first of yours for a threat label and <c>AlertsPage.Clan()</c> takes the first of this same call for
    /// the same purpose, so the two can only be the same clan if both read the same ordered answer. This used to
    /// hand over a <c>HashSet</c>, whose enumeration order is not contractual (final review of this branch,
    /// 2026-09-20). The watch folds its own case-insensitive set for every membership question, exactly as this
    /// line did.
    /// </para>
    /// </summary>
    private IReadOnlyList<string> MyGroupNames() => SourceRules.MyClanNames(Sources, Installed);

    private int IntervalFor(Source source) =>
        FindInstalled(source.Recipe)?.Recipe.EffectiveEverySeconds ?? Recipe.MinimumEverySeconds;

    /// <summary>A group list has no ticks (it records and sends nothing), so it asks for every value it offers.</summary>
    private static IReadOnlySet<string> TrackedFor(InstalledRecipe installed) =>
        installed.Recipe.IsGroupList
            ? installed.Recipe.LastStep.Values.Select(v => v.Id).ToHashSet(StringComparer.Ordinal)
            : installed.State.TrackedStats(installed.Recipe);

    /// <summary>
    /// A watched source, a group list, and a recipe whose sources are all watched send nothing, whatever the
    /// recipe's ticks say (spec §4.1, §3.5). Otherwise the allow list is <see cref="ReportPolicies.Allowed"/>,
    /// the one Setup › Alerts describes, so the card and what is actually sent agree.
    /// </summary>
    private ReportPolicy PolicyFor(InstalledRecipe installed, Source source)
    {
        var sources = Sources;

        // A clans list's ACCOUNT gate stays shut, whatever is ticked: its rows are other people's clans, and the
        // rules above are about accounts. Its clan-and-field numbers ride beside that gate, not through it —
        // no subject, no clan but yours, and its own ticks (FieldMetrics).
        IReadOnlyList<FieldMetric> field = installed.Recipe.IsGroupList
            ? FieldMetrics.Offered(installed.State.FieldMetricKeys)
            : [];

        if (source.Role == SourceRole.Watch || !ReportPolicies.SendsByRole(installed, sources)) return new ReportPolicy([], new HashSet<Guid>(), field);

        return new ReportPolicy(installed.State.SentStats(installed.Recipe), ReportPolicies.Allowed(installed, KnownAccounts, sources), field);
    }

    /// <summary>
    /// RoRoRo just listed accounts, inside a watch's fetch or a caller's refresh, before that read sends: every
    /// running watch's allow list takes them now (F8). On the fetching thread; the lists it reads are immutable
    /// references, and <see cref="RecipeWatch.UpdatePolicy"/> takes the watch's lock.
    /// </summary>
    private void OnListed(AccountList list)
    {
        if (list.Accounts.Count > 0) _savedAccounts = list.Accounts;
        RefreshPolicies();
        RememberLastNumbers();
    }

    /// <summary>
    /// After a change to sources, recipes or ticks: each kept watch takes its source and policy, and its
    /// recipe, inputs and stats when one of those changed. An unchanged recipe is not swapped in again,
    /// since a swap turns a read in flight into "the recipe changed" and releases a held stop.
    /// </summary>
    private void RefreshWatches()
    {
        lock (_refreshGate)
        {
            foreach (var source in Sources)
            {
                if (Runner.WatchFor(source.Id) is not { } watch || FindInstalled(source.Recipe) is not { } installed) continue;

                watch.UpdateSource(source);

                var tracked = TrackedFor(installed);
                var given = WatchInputs.Of(installed, source, tracked);
                if (_given.GetValueOrDefault(source.Id) != given)
                {
                    watch.UpdateRecipe(installed.Recipe, source.Inputs, tracked, installed.Text);
                    _given[source.Id] = given;
                }

                UpdatePolicy(watch, PolicyFor(installed, source));
            }
        }
    }

    /// <summary>After RoRoRo lists accounts: only the allow lists change, so a held stop stays held.</summary>
    private void RefreshPolicies()
    {
        lock (_refreshGate)
        {
            foreach (var source in Sources)
            {
                if (Runner.WatchFor(source.Id) is not { } watch || FindInstalled(source.Recipe) is not { } installed) continue;
                UpdatePolicy(watch, PolicyFor(installed, source));
            }
        }
    }

    /// <summary>Only a real change replaces the policy, so a send in flight isn't counted on one that was just replaced.</summary>
    private static void UpdatePolicy(RecipeWatch watch, ReportPolicy wanted)
    {
        var current = watch.Policy;
        if (current.SentStats.SequenceEqual(wanted.SentStats) && current.AllowedSubjects.SetEquals(wanted.AllowedSubjects)
            && current.SentFieldMetrics.SequenceEqual(wanted.SentFieldMetrics)) return;

        watch.UpdatePolicy(wanted.SentStats, wanted.AllowedSubjects, wanted.SentFieldMetrics);
    }

    /// <summary>
    /// The one place the runner is told what to read (A3): the active sources only, so an enabled source of an off mode
    /// is dropped exactly like a disabled one (its watch cancelled). The book's first load and every later change both
    /// come through here; a second call site with all of <see cref="Sources"/> would read an off mode at start.
    /// </summary>
    private void ApplyRunner() => Runner.Apply(ActiveSources);

    private void ApplySources()
    {
        if (ReaderLoaded)
        {
            ApplyRunner();
            RefreshWatches();
        }

        // Against the active sources: an off mode's last reading goes with its watch, so nothing it read before the switch
        // is still shown as live (A3). The book keeps it.
        var active = ActiveSources;
        foreach (var gone in _latest.Keys.Where(id => active.All(s => s.Id != id)).ToList())
        {
            _latest.Remove(gone);
            _lastRead.Remove(gone);
        }

        foreach (var gone in _given.Keys.Where(id => Runner.WatchFor(id) is null).ToList()) _given.Remove(gone);

        // A source that is gone, or whose recipe was updated or removed so it names no icon, loses its picture now and at the
        // next start (S1-14.1). Every path that reloads recipes or saves sources comes through here. Not while sources.json
        // couldn't be read: there are no sources this session, and that is no reason to forget every clan's picture.
        // Over ALL sources, orphans' included, never just the active ones (A3): turning a mode off must not forget its
        // clans' pictures, and an orphan is kept, not removed.
        if (!_sourcesUnreadable) _sourceIcons.Keep(IconChoice.SourcesWithIcons(Sources, [.. Installed, .. Orphans]));

        RememberLastNumbers();
        WarnPastBudget();
        RaiseIconIfChanged();
        RaiseChanged();
    }

    // ---- reads arriving ----

    private void OnSnapshotReady(string sourceId, RecipeSnapshot snapshot)
    {
        var at = _time.GetUtcNow();
        _ui.BeginInvoke(() =>
        {
            try
            {
                Record(sourceId, snapshot, at);
            }
            catch (Exception ex)
            {
                // A read that can't be shown must never take the app down. The type only: a message can carry anything.
                AddTrail($"READ NOT SHOWN: {sourceId} {ex.GetType().Name}");
            }
        });
    }

    private void OnWritten(BookLine line) => _ui.BeginInvoke(() =>
    {
        try
        {
            Reader.Apply(line);
            RaiseChanged();
        }
        catch (Exception ex)
        {
            AddTrail($"BOOK LINE NOT SHOWN: {ex.GetType().Name}");
        }
    });

    private void Record(string sourceId, RecipeSnapshot snapshot, DateTimeOffset at)
    {
        // A snapshot queued for this thread before its mode went off (or its source went) arrives after the prune in
        // ApplySources, and would put an off mode back among the live readings (review round 2). The book already has
        // what the read kept while it was on.
        if (!ActiveSources.Any(s => string.Equals(s.Id, sourceId, StringComparison.Ordinal))) return;

        _latest[sourceId] = snapshot;
        _lastRead[sourceId] = at;
        AddTrail($"{sourceId} {snapshot.State}: {snapshot.Detail}");
        TrailMisses(sourceId, snapshot);
        SaveCounterNames(sourceId, snapshot);
        RefreshPolicies();
        WarnPastBudget();
        // Not awaited, and not lost: the icon fetch never throws past a stop by contract, and if that contract
        // ever breaks the break is a trail line rather than a task nobody heard from (S1-14.6).
        Unawaited.TrailFailures(ApplyIconAsync(sourceId, snapshot), AddTrail, "ICON NOT APPLIED");
        RaiseChanged();
    }

    /// <summary>Each miss goes to the trail once (by source and text). Only your own accounts are ever named.</summary>
    private void TrailMisses(string sourceId, RecipeSnapshot snapshot)
    {
        var recipe = FindInstalled(Sources.FirstOrDefault(s => s.Id == sourceId)?.Recipe)?.Recipe;
        var misses = DiagnosticsModel.Misses(recipe, snapshot, KnownAccounts);
        if (misses.Length == 0) return;

        if (_missesInTrail.Count > 1000) _missesInTrail.Clear();
        foreach (var line in misses.Split(Environment.NewLine))
        {
            if (_missesInTrail.Add($"{sourceId}|{line}")) AddTrail($"NOT READ: {sourceId} {line}");
        }
    }

    /// <summary>
    /// Counter names from a successful read, kept in the recipe's state for the Stats table. A seeded state is written
    /// without its stats (review round 2): the read is not the person choosing ticks, and writing the seed would freeze it
    /// into the file. The seeded ticks stay in memory, and the next start seeds again from the file's empty stats.
    /// </summary>
    private void SaveCounterNames(string sourceId, RecipeSnapshot snapshot)
    {
        if (snapshot.CounterNames.Count == 0) return;
        if (FindInstalled(Sources.FirstOrDefault(s => s.Id == sourceId)?.Recipe) is not { Recipe.LastStep.Counters: not null } installed) return;
        if (snapshot.CounterNames.SequenceEqual(installed.State.SavedCounterNames, StringComparer.Ordinal)) return;

        try
        {
            var state = installed.State with { CounterNames = [.. snapshot.CounterNames] };
            Store.SaveState(installed.Recipe, installed.Seeded ? state with { Stats = null } : state);
            Installed = [.. Installed.Select(i => ReferenceEquals(i, installed) ? i with { State = state } : i)];
        }
        catch (Exception ex)
        {
            AddTrail($"COUNTER NAMES NOT SAVED: {ex.GetType().Name}");
        }
    }

    /// <summary>Accounts that arrive with Send on were never checked against RoRoRo's history limit. Says so once per count.</summary>
    private void WarnPastBudget()
    {
        try
        {
            var ids = KnownAccounts.Select(a => a.AccountId).ToList();
            // Lists included: their clan numbers count now (V3-S.28). They were left out when they counted as nothing.
            // Only readers that read (A3): an off mode sends nothing, so it holds no history slot.
            var over = ids.Count == 0 ? null : HistoryBudget.AfterSeed(ReadersOn(_switches), ids);
            BudgetWarning = over?.Line;
            if (over is null || _budgetWarnedCount == over.Count) return;

            _budgetWarnedCount = over.Count;
            AddTrail($"BUDGET: {over.Line}");
        }
        catch (Exception ex)
        {
            AddTrail($"BUDGET NOT CHECKED: {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// The icon a read of <paramref name="sourceId"/> named becomes THAT source's picture (backlog V3-S.7; stats design §3.3),
    /// fetched once per icon text. The only text ever looked up is the one this read brought back. Anything that fails costs that
    /// source's picture and nothing else, and says nothing: a missing decoration is not news.
    /// </summary>
    private async Task ApplyIconAsync(string sourceId, RecipeSnapshot snapshot)
    {
        if (snapshot.IconText is not { } iconText) return;
        if (IconHostsFor(sourceId) is not { } hosts) return;

        try
        {
            if (!await _sourceIcons.ApplyAsync(sourceId, iconText, hosts, _closing.Token)) return;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        RaiseIconIfChanged();
        RaiseChanged();
    }

    /// <summary>The hosts a source's recipe contacts, or null when the source is gone or its recipe names no icon.</summary>
    private IReadOnlySet<string>? IconHostsFor(string sourceId) =>
        Sources.FirstOrDefault(s => string.Equals(s.Id, sourceId, StringComparison.Ordinal)) is { } source
        && FindInstalled(source.Recipe)?.Recipe is { Icon: not null } recipe
            ? RecipeHosts.ContactedBy(recipe)
            : null;

    /// <summary>The window's icon follows the main clan, so a new main, a picture landing or a removed recipe moves it too.</summary>
    private void RaiseIconIfChanged()
    {
        var icon = WindowIcon;
        if (icon == _windowIcon) return;

        _windowIcon = icon;
        IconChanged?.Invoke(icon);
    }

    /// <summary>
    /// The pictures beside your own accounts, after the numbers (plan A23): the ids RoRoRo lists as yours, once each per
    /// session, off the UI thread. Anything that fails costs the pictures and nothing else.
    /// </summary>
    private void AskForAvatars()
    {
        var yours = LiveBoard.UserIdsOf(KnownAccounts);

        // Unconditional, and before the early return: an account that stops being yours is forgotten even when it was the
        // last one RoRoRo listed (review Minor 5).
        _avatars.Keep(yours);
        if (yours.Count == 0) return;

        _ = AskForAvatarsAsync(yours);
    }

    /// <summary>
    /// The last numbers each source kept, so the window has something real in it before the first read lands (plan
    /// A37). Read from the loaded book, never from disk again: after the book loads, when the sources change, and
    /// when RoRoRo's account list changes, since which ids are yours decides which rows come back. A reading from
    /// this session always wins (<see cref="LiveBoard.SnapshotOf"/>), so nothing here needs clearing.
    /// </summary>
    private void RememberLastNumbers()
    {
        if (!ReaderLoaded) return;

        _remembered = Remembered.ForSources(Reader, Sources, Installed, LiveBoard.UserIdsOf(KnownAccounts));
    }

    private async Task AskForAvatarsAsync(IReadOnlySet<long> yours)
    {
        try
        {
            if (await _avatars.AskAsync(yours, _closing.Token)) RaiseChanged();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // The type only: a message can carry anything. The rows keep their names either way.
            AddTrail($"AVATARS: your accounts' pictures could not be fetched ({ex.GetType().Name}).");
        }
    }

    // ---- loading ----

    /// <summary>
    /// Recipes, then sources. Part 2a's saved inputs become sources only on the first start, when there is no
    /// <c>sources.json</c> yet (spec §4.1); after that the file is the truth, so a clan source the user removed
    /// stays removed. An installed recipe with no inputs and no source (placed in the folder while Ur Score was
    /// closed) gets its one source, since no screen can make one for it. A file that exists but can't be read
    /// leaves no sources this session and is never written over.
    /// </summary>
    private void LoadAtStart()
    {
        LoadInstalled();

        // Only readers of on modes (A5): a Battle-only upgrader does not wake up with a profile source. On the upgrade's own
        // start, only readers 0.6.3 had installed too (review round 2): Battle is on for a player who imported the
        // clan-battle recipe alone, and the clans list they never installed gets its source when they next turn Battle on,
        // not unasked.
        var upgradedFrom = _upgradedFrom;
        var readers = upgradedFrom is null
            ? ReadersOn(_switches)
            : [.. ReadersOn(_switches).Where(i => upgradedFrom.Contains(i.Recipe.Slug, StringComparer.Ordinal))];

        var load = _sourceStore.LoadResult();
        if (!load.Exists)
        {
            var migrated = SourceRules.Migrate(readers, []);
            Sources = migrated;
            TrySaveSources(migrated);
            return;
        }

        if (!load.Readable)
        {
            _sourcesUnreadable = true;
            AddTrail($"SOURCES NOT READ: {SourcesNotWritten}");
            Sources = [];
            return;
        }

        // Nothing counts as installed before, so every input-less reader without a source is treated as new.
        var sources = SourceRules.ForNewRecipes(load.Sources, [], readers);
        Sources = sources;
        if (!ReferenceEquals(sources, load.Sources)) TrySaveSources(sources);
    }

    /// <summary>
    /// No file, or an empty list, leaves every starter following your sources (R1, D3). A file that can't be read shows
    /// the starters too, and says why until the next save keeps a copy of it (R3).
    /// </summary>
    private void LoadBoards()
    {
        var load = _boardsFile.Load();
        if (!load.Readable)
        {
            _boardsUnread = true;
            BoardsProblem = "Your boards file couldn't be read, so your starter tabs are showing. The next change to a board keeps a copy of the old file beside the new one.";
            AddTrail("BOARDS NOT READ: showing the starter tabs.");
            return;
        }

        if (load.Boards.Count > 0) _savedBoards = load.Boards;
    }

    /// <summary>Saves what changed without an import or reload failing over it; a file that couldn't be read is left alone.</summary>
    private void TrySaveSources(IReadOnlyList<Source> sources)
    {
        if (_sourcesUnreadable)
        {
            AddTrail($"SOURCES NOT SAVED: {SourcesNotWritten}");
            return;
        }

        try
        {
            _sourceStore.Save(sources);
        }
        catch (Exception ex)
        {
            AddTrail($"SOURCES NOT SAVED: {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// The readers come from the app (<see cref="Readers.Compose"/>): the embedded text, with each one's saved choices or
    /// its seed. A data-folder recipe no mode names is an orphan, listed and never read. Seeding writes nothing.
    /// </summary>
    private void LoadInstalled()
    {
        var load = Readers.Compose(Catalog, Readers.BuiltIn, Store);
        // Under the refresh gate, so a refresh already reading the old list finishes applying it before the
        // new one is seen, and the refresh that follows this load is the one that lands last (S1-14.13).
        lock (_refreshGate) Installed = load.Installed;
        Orphans = load.Orphans;
        RecipeProblems = [.. _manifestProblems.Concat(load.Problems).Select(p => Redactor.Redact(p))];
        foreach (var problem in load.Problems) AddTrail($"READER SKIPPED: {Redactor.Redact(problem)}");
    }

    private InstalledRecipe? FindInstalled(string? slug) =>
        slug is null ? null : Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, slug, StringComparison.Ordinal));

    /// <summary>
    /// Earlier versions kept each call's last response under <c>last-response</c>, other players' rows and all.
    /// That folder goes on start. Best effort: a folder that can't be removed costs a trail line, never the start.
    /// </summary>
    private void DeleteOldRawResponses(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex)
        {
            AddTrail($"OLD RESPONSES NOT REMOVED: {ex.GetType().Name}");
        }
    }

    private static IReadOnlyList<HostAccount> LoadSavedAccounts(AccountsCache cache)
    {
        try
        {
            return cache.Load();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Coalesced: many reads and lines in one dispatcher pass redraw once.</summary>
    private void RaiseChanged()
    {
        if (_changePending) return;
        _changePending = true;
        _ui.BeginInvoke(() =>
        {
            _changePending = false;
            if (Changed is not { } changed) return;

            // Each subscriber on its own, so a page that fails to redraw doesn't stop the board redrawing.
            foreach (var handler in changed.GetInvocationList().Cast<Action>())
            {
                try
                {
                    handler();
                }
                catch (Exception ex)
                {
                    AddTrail($"NOT REDRAWN: {ex.GetType().Name}");
                }
            }
        }, DispatcherPriority.Background);
    }

    private async Task FollowThemeAsync(CancellationToken cancellationToken)
    {
        var following = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _themeHost.FollowThemeAsync(palette => _ui.InvokeAsync(() =>
                {
                    ThemeService.Apply(ThemeService.Current.Merge(palette));
                    if (following) return;
                    following = true;
                    AddTrail("THEME: following RoRoRo's theme.");
                }), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Unimplemented)
            {
                AddTrail("THEME: this RoRoRo has no theme feed, so the window keeps RoRoRo's Brand colours.");
                return;
            }
            catch (Exception ex)
            {
                if (following)
                {
                    following = false;
                    AddTrail($"THEME: stopped following RoRoRo's theme ({ex.GetType().Name}); colours stay as they are.");
                }
            }

            try
            {
                await Task.Delay(ThemeRetry, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
