using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using Grpc.Core;
using Labs626.UrScore.Board;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.Theming;
using Labs626.UrScore.UI;

namespace Labs626.UrScore.Composition;

using NameClient = Labs626.UrScore.Source.NameClient;
using IconClient = Labs626.UrScore.Source.IconClient;
using AvatarBook = Labs626.UrScore.Source.AvatarBook;
using SourceIcons = Labs626.UrScore.Source.SourceIcons;
using Source = Labs626.UrScore.Core.Source;

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

    /// <summary>
    /// The longest a caller waits for RoRoRo's accounts. Each host call is bounded at 5 s already; this also
    /// covers a watch's fetch holding the shared one. Past it, the saved list stands in.
    /// </summary>
    public static readonly TimeSpan AccountsWait = TimeSpan.FromSeconds(20);

    private const int TrailLimit = 400;

    /// <summary>How long to wait before asking RoRoRo for its theme again after it was not there.</summary>
    private static readonly TimeSpan ThemeRetry = TimeSpan.FromSeconds(15);

    private readonly Dispatcher _ui;
    private readonly TimeProvider _time = TimeProvider.System;
    private readonly HttpClient _recipeHttp = new(HttpRecipeTransport.CreateHandler());
    private readonly HttpClient _namesHttp = new();
    private readonly HostClient _host = new(PluginId);

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
    private readonly SourceStore _sourceStore = new(SourceStore.DefaultPath);
    private readonly BoardsFile _boardsFile = new(BoardsFile.DefaultPath, TimeProvider.System);

    /// <summary>What <c>boards.json</c> holds, or null while there is no file (R1) or it couldn't be read (R3).</summary>
    private IReadOnlyList<BoardDef>? _savedBoards;

    /// <summary><c>boards.json</c> was there at start and couldn't be read, for any reason: the first save keeps a copy whatever it holds by then (R3).</summary>
    private bool _boardsUnread;
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

    public AppServices(Dispatcher ui)
    {
        _ui = ui;
        Keys = new KeyStore(KeyStore.DefaultPath);
        var keys = Keys;
        Redactor = new Redactor(() => keys.Values());
        Store = new RecipeStore(RecipeStore.DefaultDirectory);
        Settings = Settings.Load();

        // A walk's scratch rules file is never silent: Diagnostics' trail says which file alerts use.
        if (!string.Equals(RulesPath, RulesFile.DefaultPath, StringComparison.OrdinalIgnoreCase))
        {
            AddTrail($"RULES: alerts use the file {RulesFile.PathVariable} names, not RoRoRo's.");
        }

        // No raw responses kept: a response body holds every row the source returned, other players' ids and values
        // included, and other players never reach disk.
        DeleteOldRawResponses();
        var transport = new SpacedTransport(new HttpRecipeTransport(_recipeHttp, rawDirectory: null, Redactor), _time, SpacedTransport.DefaultSpacing);
        _engine = new RecipeEngine(transport, Keys);
        _searchLists = new SearchLists(transport);

        AccountsCache = new AccountsCache(AccountsCache.DefaultPath);
        _savedAccounts = LoadSavedAccounts(AccountsCache);
        Accounts = new SharedAccounts(_host, AccountsCache, _time);
        Accounts.Listed += OnListed;
        _claims = new AccountClaims(_time);

        _book = new ScoreBook(BookFiles.DefaultRoot);
        Reader = new ScoreBookReader(BookFiles.DefaultRoot, _time);

        Names = new NameClient(_namesHttp);
        _icons = new IconClient(HttpRecipeTransport.CreateHandler(), IconClient.DefaultCacheDirectory, () => _time.GetUtcNow());
        _avatars = new AvatarBook(_icons);
        _sourceIcons = new SourceIcons(_icons, Path.Combine(IconClient.DefaultCacheDirectory, SourceIcons.FileName));

        Runner = new SourceHost(CreateWatch, IntervalFor);
        Runner.SnapshotReady += OnSnapshotReady;

        LoadAtStart();
        LoadBoards();

        // Once the sources are known, and before the window exists: each source's picture from last session, from disk alone,
        // so the window, the taskbar and every Standing panel open on them rather than waiting for a read (V3-S.6).
        _sourceIcons.Restore(IconHostsFor);
        _windowIcon = WindowIcon;
    }

    // ---- ISetupServices ----

    public IReadOnlyList<InstalledRecipe> Installed { get; private set; } = [];

    public IReadOnlyList<string> RecipeProblems { get; private set; } = [];

    public IReadOnlyList<Source> Sources { get; private set; } = [];

    public RecipeStore Store { get; }

    public IKeyStore Keys { get; }

    public Redactor Redactor { get; }

    public Settings Settings { get; private set; }

    public SharedAccounts Accounts { get; }

    public AccountsCache AccountsCache { get; }

    public IReadOnlyList<HostAccount> KnownAccounts => Accounts.Last?.Accounts is { Count: > 0 } listed ? listed : _savedAccounts;

    public IScoreBook Book => _book;

    public ScoreBookReader Reader { get; }

    public bool ReaderLoaded { get; private set; }

    public bool Running => Runner.Running;

    public IReadOnlyDictionary<string, RecipeSnapshot> Latest => _latest;

    public string? BudgetWarning { get; private set; }

    public string HostText => $"host={_host.HostVersion ?? "(not connected)"} reject={_host.RejectReason ?? "(none)"}";

    /// <summary>RoRoRo's rules file, or the full path <c>UR_SCORE_RULES_FILE</c> names: the Setup › Alerts walk's scratch copy (plan A1).</summary>
    public string RulesPath { get; } = RulesFile.ResolvePath(Environment.GetEnvironmentVariable(RulesFile.PathVariable));

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

    public (int Sent, int Dropped) PolicyCounts(string recipeSlug)
    {
        int sent = 0, dropped = 0;
        foreach (var source in Sources.Where(s => string.Equals(s.Recipe, recipeSlug, StringComparison.Ordinal)))
        {
            if (Runner.WatchFor(source.Id) is not { } watch) continue;
            sent += watch.Policy.Sent;
            dropped += watch.Policy.Dropped;
        }

        return (sent, dropped);
    }

    public string? IconFileFor(string recipeSlug) => IconChoice.ForRecipe(recipeSlug, Sources, Installed, _sourceIcons.FileFor);

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
        KnownAccounts, _time, Runner.Running, _avatars.Files, _remembered, _sourceIcons.Files);

    /// <summary>
    /// The boards on screen: the saved ones, with each tab that still follows a starter rebuilt from your sources and
    /// shown while it has panels; with nothing saved, every starter follows (D1–D4). Never empty.
    /// </summary>
    public IReadOnlyList<BoardDef> Boards => Following.Shown(_savedBoards, StarterBoards.All(Installed, Sources));

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
        var clean = BoardDefs.Sanitize(Following.ToSave(_savedBoards, StarterBoards.All(Installed, Sources), boards), LiveBoard.UserIdsOf(KnownAccounts));

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
        await Task.Run(() => reader.Load(BookFiles.Slugs(root)));
        RaiseChanged();
    }

    private async Task LoadBookOnceAsync()
    {
        var root = _book.Root;
        var reader = Reader;
        _finals = await Task.Run(() =>
        {
            var index = FinalsIndex.Load(root);
            reader.Load(BookFiles.Slugs(root));
            return index;
        });

        ReaderLoaded = true;
        _book.Written += OnWritten;
        Runner.Apply(Sources);
        AddTrail($"BOOK: loaded from {root}.");
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
    /// Writes <c>settings.json</c> and redraws. Nothing running changes: the only key a page writes is read when the
    /// board next opens (plan A33). Qualified as <c>Core.Settings</c> because the property beside it has that name.
    /// </summary>
    public void SaveSettings(Settings settings)
    {
        Core.Settings.Save(settings);
        Settings = settings;
        RaiseChanged();
    }

    /// <summary>
    /// After an import or update: recipes come from disk again, sources don't. Only a newly installed recipe
    /// with no inputs gets a source (<see cref="SourceRules.ForNewRecipes"/>), and the file is saved only then.
    /// </summary>
    public void ReloadRecipes()
    {
        var before = Installed;
        LoadInstalled();

        var sources = SourceRules.ForNewRecipes(Sources, before, Installed);
        if (!ReferenceEquals(sources, Sources))
        {
            Sources = sources;
            TrySaveSources(sources);
        }

        ApplySources();
    }

    public void SaveRecipeState(Recipe recipe, RecipeState state)
    {
        Store.SaveState(recipe, state);
        LoadInstalled();
        ApplySources();
    }

    public void RemoveRecipe(string slug)
    {
        // The recipe file and its state go; its score book stays (spec §5.1).
        Store.Remove(slug);
        LoadInstalled();
        var sources = SourceRules.ForgetRecipe(Sources, slug);
        if (sources.Count != Sources.Count)
        {
            if (_sourcesUnreadable) AddTrail($"SOURCES NOT SAVED: {SourcesNotWritten}");
            else _sourceStore.Save(sources);
        }

        Sources = sources;
        ApplySources();
    }

    public async Task<RecipeSnapshot?> ReadOnceAsync(string sourceId, CancellationToken cancellationToken)
    {
        if (!ReaderLoaded) return null;

        AskedReadAt = _time.GetUtcNow();
        await RefreshAccountsAsync(cancellationToken);
        if (Runner.WatchFor(sourceId) is not { } watch) return null;

        var snapshot = await watch.RunOnceAsync(cancellationToken, BookLine.TriggerManual);
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
        _host.Dispose();
        _recipeHttp.Dispose();
        _namesHttp.Dispose();
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
    private bool WriteLabel(FieldMetric metric, string label) =>
        LabelInPlace(RulesFile.ChangeLabel(RulesPath, metric.MetricId, AlertKind.Level, label));

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
    /// </summary>
    private IReadOnlySet<string> MyGroupNames() =>
        SourceRules.MyClanNames(Sources, Installed).ToHashSet(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>After RoRoRo lists accounts: only the allow lists change, so a held stop stays held.</summary>
    private void RefreshPolicies()
    {
        foreach (var source in Sources)
        {
            if (Runner.WatchFor(source.Id) is not { } watch || FindInstalled(source.Recipe) is not { } installed) continue;
            UpdatePolicy(watch, PolicyFor(installed, source));
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

    private void ApplySources()
    {
        if (ReaderLoaded)
        {
            Runner.Apply(Sources);
            RefreshWatches();
        }

        foreach (var gone in _latest.Keys.Where(id => Sources.All(s => s.Id != id)).ToList())
        {
            _latest.Remove(gone);
            _lastRead.Remove(gone);
        }

        foreach (var gone in _given.Keys.Where(id => Runner.WatchFor(id) is null).ToList()) _given.Remove(gone);

        // A source that is gone, or whose recipe was updated or removed so it names no icon, loses its picture now and at the
        // next start (S1-14.1). Every path that reloads recipes or saves sources comes through here. Not while sources.json
        // couldn't be read: there are no sources this session, and that is no reason to forget every clan's picture.
        if (!_sourcesUnreadable) _sourceIcons.Keep(IconChoice.SourcesWithIcons(Sources, Installed));

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
        _latest[sourceId] = snapshot;
        _lastRead[sourceId] = at;
        AddTrail($"{sourceId} {snapshot.State}: {snapshot.Detail}");
        TrailMisses(sourceId, snapshot);
        SaveCounterNames(sourceId, snapshot);
        RefreshPolicies();
        WarnPastBudget();
        _ = ApplyIconAsync(sourceId, snapshot);
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

    /// <summary>Counter names from a successful read, kept in the recipe's state for the Stats table.</summary>
    private void SaveCounterNames(string sourceId, RecipeSnapshot snapshot)
    {
        if (snapshot.CounterNames.Count == 0) return;
        if (FindInstalled(Sources.FirstOrDefault(s => s.Id == sourceId)?.Recipe) is not { Recipe.LastStep.Counters: not null } installed) return;
        if (snapshot.CounterNames.SequenceEqual(installed.State.SavedCounterNames, StringComparer.Ordinal)) return;

        try
        {
            var state = installed.State with { CounterNames = [.. snapshot.CounterNames] };
            Store.SaveState(installed.Recipe, state);
            Installed = [.. Installed.Select(i => ReferenceEquals(i, installed) ? i with { State = state } : i)];
        }
        catch (Exception ex)
        {
            AddTrail($"COUNTER NAMES NOT SAVED: {ex.Message}");
        }
    }

    /// <summary>Accounts that arrive with Send on were never checked against RoRoRo's history limit. Says so once per count.</summary>
    private void WarnPastBudget()
    {
        try
        {
            var ids = KnownAccounts.Select(a => a.AccountId).ToList();
            var over = ids.Count == 0 ? null : HistoryBudget.AfterSeed(Installed.Where(i => !i.Recipe.IsGroupList), ids);
            BudgetWarning = over?.Line;
            if (over is null || _budgetWarnedCount == over.Count) return;

            _budgetWarnedCount = over.Count;
            AddTrail($"BUDGET: {over.Line}");
        }
        catch (Exception ex)
        {
            AddTrail($"BUDGET NOT CHECKED: {ex.Message}");
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

        var load = _sourceStore.LoadResult();
        if (!load.Exists)
        {
            var migrated = SourceRules.Migrate(Installed, []);
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

        // Nothing counts as installed before, so every input-less recipe without a source is treated as new.
        var sources = SourceRules.ForNewRecipes(load.Sources, [], Installed);
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

    private void LoadInstalled()
    {
        var load = Store.LoadAll();
        Installed = load.Recipes;
        RecipeProblems = [.. load.Problems.Select(p => Redactor.Redact(p))];
        foreach (var problem in RecipeProblems) AddTrail($"RECIPE FILE SKIPPED: {problem}");
    }

    private InstalledRecipe? FindInstalled(string? slug) =>
        slug is null ? null : Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, slug, StringComparison.Ordinal));

    /// <summary>
    /// Earlier versions kept each call's last response under <c>last-response</c>, other players' rows and all.
    /// That folder goes on start. Best effort: a folder that can't be removed costs a trail line, never the start.
    /// </summary>
    private void DeleteOldRawResponses()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "626labs.ur-score", "last-response");
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
