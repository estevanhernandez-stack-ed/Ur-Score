using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Grpc.Core;
using Labs626.UrScore.Core;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.Source;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

/// <summary>
/// Runs the active recipe: the dashboard (headline, leaderboard, your accounts) above, the alert
/// pipeline's diagnostics below. Part 2a shows a column per shown stat; part 2b redesigns the board.
/// </summary>
public partial class MainWindow : Window
{
    private const string PluginId = "626labs.ur-score";
    private const double DefaultThreshold = 100;
    private const int DefaultWindowMinutes = 10;

    /// <summary>
    /// One row of the accounts grid. Raises change notifications so rows update in place: calling
    /// <c>Items.Refresh()</c> throws while the Send checkbox is mid-edit (F10). Stat values are bound
    /// by column position (<c>Cells[0]</c>, <c>Cells[1]</c>…), because a stat key such as
    /// <c>counter:Huge Pets Opened</c> is not something a binding path can hold unescaped.
    /// </summary>
    public sealed class Row : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _send = true;
        private string _position = StatText.Dash;
        private IReadOnlyList<string> _cells = [];
        private string _ratePerMinute = StatText.Dash;
        private string _lastValue = StatText.Dash;
        private string _lastSent = StatText.Dash;
        private string _note = "";

        public bool Send { get => _send; set => SetField(ref _send, value); }

        public string DisplayName { get; set; } = "";

        public Guid AccountId { get; set; }

        public long RobloxUserId { get; set; }

        public string Position { get => _position; set => SetField(ref _position, value); }

        public IReadOnlyList<string> Cells { get => _cells; set => SetField(ref _cells, value); }

        public string RatePerMinute { get => _ratePerMinute; set => SetField(ref _ratePerMinute, value); }

        public string LastValue { get => _lastValue; set => SetField(ref _lastValue, value); }

        public string LastSent { get => _lastSent; set => SetField(ref _lastSent, value); }

        public string Note { get => _note; set => SetField(ref _note, value); }

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class LeaderboardRow
    {
        public string Position { get; set; } = "";
        public string Name { get; set; } = "";
        public IReadOnlyList<string> Cells { get; set; } = [];
        public string Yours { get; set; } = "";
    }

    /// <summary>One sent stat in the rule helper's list: label, then the metric id the rule matches.</summary>
    public sealed record RuleChoice(string MetricId, string Text);

    private readonly ObservableCollection<Row> _rows = [];
    private readonly ObservableCollection<LeaderboardRow> _leaderboardRows = [];
    private readonly HttpClient _http = new();
    private readonly HttpClient _recipeHttp = new(HttpRecipeTransport.CreateHandler());
    private readonly DispatcherTimer _timer = new();
    private readonly HostClient _host = new(PluginId);

    /// <summary>Its own connection, so the long-lived theme stream never shares a channel with reports.</summary>
    private readonly HostClient _themeHost = new(PluginId);
    private readonly CancellationTokenSource _closing = new();
    private readonly NameClient _nameClient;
    private readonly IconClient _icons = new(HttpRecipeTransport.CreateHandler(), IconClient.DefaultCacheDirectory, () => DateTimeOffset.UtcNow);
    private readonly RecipeStore _store = new(RecipeStore.DefaultDirectory);
    private readonly KeyStore _keys = new(KeyStore.DefaultPath);
    private readonly Redactor _redactor;
    private readonly List<string> _trail = [];

    /// <summary>This window's own last reads per account and stat, cleared when the context changes, never taken from a report.</summary>
    private readonly StatHistory _history = new();

    private readonly List<DataGridTextColumn> _accountStatColumns = [];
    private readonly List<DataGridTextColumn> _leaderboardStatColumns = [];

    /// <summary>The stat-wide misses last written to the trail, so a miss that repeats every cycle is written once.</summary>
    private readonly Dictionary<string, string> _statMissesInTrail = new(StringComparer.Ordinal);

    private IReadOnlyList<RecipeStat> _shownStats = [];
    private string? _iconText;
    private string? _lastDashboardContext;
    private IReadOnlyList<string> _storeProblems = [];
    private string? _recipeFileNote;
    private Settings _settings = Settings.Load();
    private InstalledRecipe? _active;
    private RecipeWatch? _watch;
    private bool _running;

    public MainWindow()
    {
        InitializeComponent();
        ThemeService.Attach(this);
        Loaded += (_, _) => _ = FollowThemeAsync(_closing.Token);
        AccountsGrid.ItemsSource = _rows;
        AccountsGrid.CellEditEnding += OnSendToggled;
        LeaderboardGrid.ItemsSource = _leaderboardRows;

        _nameClient = new NameClient(_http);
        _redactor = new Redactor(() => _keys.Values());
        _timer.Tick += async (_, _) => await CycleAsync();

        LoadActiveRecipe();
        RenderRecipe();
        RenderRule();
        RenderPolicy();
        StateLine.Text = "Not started.";
    }

    /// <summary>How long to wait before asking RoRoRo for its theme again after it was not there.</summary>
    private static readonly TimeSpan ThemeRetry = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Follows RoRoRo's theme for as long as the window is open: the current palette on connect, then
    /// every switch. While RoRoRo is not running the window keeps the last palette it had (Brand at
    /// first) and asks again every <see cref="ThemeRetry"/>. A host too old to have the theme feed
    /// is asked once and left on the fallback.
    /// </summary>
    private async Task FollowThemeAsync(CancellationToken cancellationToken)
    {
        var following = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _themeHost.FollowThemeAsync(palette => Dispatcher.InvokeAsync(() =>
                {
                    ThemeService.Apply(ThemeService.Current.Merge(palette));
                    if (following) return;
                    following = true;
                    _trail.Add(Stamp("THEME: following RoRoRo's theme."));
                }), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Unimplemented)
            {
                _trail.Add(Stamp("THEME: this RoRoRo has no theme feed, so the window keeps RoRoRo's Brand colours."));
                return;
            }
            catch (Exception ex)
            {
                if (following)
                {
                    following = false;
                    _trail.Add(Stamp($"THEME: stopped following RoRoRo's theme ({ex.GetType().Name}); colours stay as they are."));
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

    protected override void OnClosed(EventArgs e)
    {
        _closing.Cancel();
        _themeHost.Dispose();
        base.OnClosed(e);
    }

    private string RawDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "626labs.ur-score", "last-response");

    private IReadOnlySet<string> TrackedStats() => _active?.State.TrackedStats(_active.Recipe) ?? new HashSet<string>();

    private IReadOnlyList<SentStat> SentStats() => _active?.State.SentStats(_active.Recipe) ?? [];

    private IReadOnlyCollection<Guid> AccountIds() => [.. _rows.Select(r => r.AccountId)];

    private HashSet<Guid> CurrentAllowedSubjects() => _rows.Where(r => r.Send).Select(r => r.AccountId).ToHashSet();

    /// <summary>The sent stat the rule helper has selected, or null when nothing is sent.</summary>
    private string? RuleMetricId => (RuleStatBox.SelectedItem as RuleChoice)?.MetricId;

    private IReadOnlyList<string> Dashes() => [.. Enumerable.Repeat(StatText.Dash, _shownStats.Count)];

    /// <summary>
    /// Place, cells, rate and note, back to dash or empty together. Three call sites once reset these
    /// one field at a time and drifted apart, leaving a rate or a note stale beside dashed cells.
    /// </summary>
    private void ClearDashboard(Row row)
    {
        row.Position = StatText.Dash;
        row.Cells = Dashes();
        row.RatePerMinute = StatText.Dash;
        row.Note = "";
    }

    private string Stamp(string text) => $"{DateTimeOffset.UtcNow:O} {_redactor.Redact(text)}";

    /// <summary>The recipe named in settings, else the first installed.</summary>
    private void LoadActiveRecipe()
    {
        var load = _store.LoadAll();
        _storeProblems = load.Problems;
        _active = load.Recipes.FirstOrDefault(r => r.Recipe.Slug == _settings.ActiveRecipe) ?? load.Recipes.FirstOrDefault();

        foreach (var problem in load.Problems)
        {
            _trail.Add(Stamp($"RECIPE FILE SKIPPED: {problem}"));
        }
    }

    /// <summary>Re-reads the active recipe and its state from disk; both files are hand-editable.</summary>
    private void ReloadActive()
    {
        if (_active is null) return;

        var fresh = _store.Find(_active.Recipe.Slug);
        if (fresh is null)
        {
            // Removed on disk mid-session. Keep running what was loaded, so a live read is not cut off, but say so:
            // quietly running a recipe that no longer exists on disk is the kind of silent divergence this window avoids.
            _recipeFileNote = $"The recipe file for {_active.Recipe.Name} is no longer in {RecipeStore.DefaultDirectory}. "
                + "Still running the copy loaded earlier; import it again to keep it.";
            _trail.Add(Stamp($"RECIPE FILE GONE: {_recipeFileNote}"));
            return;
        }

        ApplyActive(fresh);
    }

    /// <summary>
    /// The one place a recipe becomes the running one: the missing-file note clears, the Send
    /// checkboxes follow its state, the watch takes its recipe, inputs and tracked stats, and the
    /// heading and columns re-render. Both <see cref="ReloadActive"/> and <see cref="Activate"/> go
    /// through here, so a recipe swap cannot update the watch in one place and forget it in the other.
    /// </summary>
    private void ApplyActive(InstalledRecipe installed)
    {
        _active = installed;
        _recipeFileNote = null;

        var excluded = installed.State.Excluded;
        foreach (var row in _rows)
        {
            row.Send = !excluded.Contains(row.AccountId);
        }

        _watch?.UpdateRecipe(installed.Recipe, installed.State.InputValues, TrackedStats());
        _watch?.UpdatePolicy(SentStats(), CurrentAllowedSubjects());
        RenderRecipe();
    }

    private void RenderRecipe()
    {
        RebuildStatColumns();
        RenderRuleChoices();

        if (_active is null)
        {
            HeadingLine.Text = "No recipe yet";
            ClanLine.Text = "Import a recipe to start.";
            ClanDetailLine.Text = "A recipe says where numbers are. Ur Score reads them and hands the ones you choose to RoRoRo.";
            AttributionLine.Text = "";
            DetailLine.Text = _storeProblems.Count > 0
                ? _redactor.Redact("Some recipe files could not be read: " + string.Join(" | ", _storeProblems))
                : $"Recipes live in {RecipeStore.DefaultDirectory}.";
            return;
        }

        var recipe = _active.Recipe;
        var listForm = !recipe.LastStep.PerAccount;
        HeadingLine.Text = recipe.Name;
        ClanLine.Text = "Not started.";
        ClanDetailLine.Text = "";
        AttributionLine.Text = recipe.Credit;

        // A position among the rows, and a rate between polls, only mean something for a list.
        AccountsPlaceColumn.Header = recipe.PlaceLabel;
        AccountsPlaceColumn.Visibility = listForm ? Visibility.Visible : Visibility.Collapsed;
        AccountsRateColumn.Visibility = listForm ? Visibility.Visible : Visibility.Collapsed;
        RateDisclaimerLine.Visibility = listForm ? Visibility.Visible : Visibility.Collapsed;

        DetailLine.Text = TrackedStats().Count == 0 ? RecipeEngine.NothingTracked : $"Reads {recipe.Name} when started.";
    }

    /// <summary>
    /// One column per shown stat in both tables, rebuilt only when the shown stats change, so a
    /// cycle's values are not blanked by an unrelated re-render.
    /// </summary>
    private void RebuildStatColumns()
    {
        var shown = _active?.State.ShownStats(_active.Recipe) ?? [];
        if (shown.SequenceEqual(_shownStats) && _accountStatColumns.Count == shown.Count) return;

        foreach (var column in _accountStatColumns) AccountsGrid.Columns.Remove(column);
        foreach (var column in _leaderboardStatColumns) LeaderboardGrid.Columns.Remove(column);
        _accountStatColumns.Clear();
        _leaderboardStatColumns.Clear();
        _statMissesInTrail.Clear();
        _shownStats = shown;

        for (var index = 0; index < shown.Count; index++)
        {
            var accountColumn = StatColumn(shown[index], index);
            AccountsGrid.Columns.Insert(AccountsGrid.Columns.IndexOf(AccountsRateColumn), accountColumn);
            _accountStatColumns.Add(accountColumn);

            var boardColumn = StatColumn(shown[index], index);
            LeaderboardGrid.Columns.Insert(LeaderboardGrid.Columns.IndexOf(LeaderboardYoursColumn), boardColumn);
            _leaderboardStatColumns.Add(boardColumn);
        }

        _history.Clear();
        _leaderboardRows.Clear();
        foreach (var row in _rows)
        {
            ClearDashboard(row);
        }
    }

    private static DataGridTextColumn StatColumn(RecipeStat stat, int index) => new()
    {
        Header = stat.Label,
        Binding = new Binding($"Cells[{index}]"),
        Width = new DataGridLength(140),
        IsReadOnly = true,
    };

    /// <summary>A stat that missed for every row says so in its header, and its reason goes to the trail once.</summary>
    private void RenderStatMisses(IReadOnlyDictionary<string, string> statMisses)
    {
        for (var index = 0; index < _shownStats.Count; index++)
        {
            var stat = _shownStats[index];
            var header = statMisses.ContainsKey(stat.Key) ? $"{stat.Label} (can't read)" : stat.Label;
            _accountStatColumns[index].Header = header;
            _leaderboardStatColumns[index].Header = header;
        }

        foreach (var (key, miss) in statMisses)
        {
            if (_statMissesInTrail.TryGetValue(key, out var written) && written == miss) continue;

            _statMissesInTrail[key] = miss;
            var label = RecipeStats.Find(_active!.Recipe, key)?.Label ?? key;
            _trail.Add(Stamp($"STAT NOT READ: {label}: {miss}"));
        }

        foreach (var key in _statMissesInTrail.Keys.Where(key => !statMisses.ContainsKey(key)).ToList())
        {
            _statMissesInTrail.Remove(key);
        }
    }

    /// <summary>
    /// The ONE watch this window uses for a recipe (F2): a fresh watch per cycle would get a fresh
    /// serialization guard, and a timer tick and a Test now click could both report one observation.
    /// </summary>
    private RecipeWatch EnsureWatch(InstalledRecipe active)
    {
        if (_watch is not null) return _watch;

        var engine = new RecipeEngine(new HttpRecipeTransport(_recipeHttp, RawDirectory, _redactor), _keys);
        var policy = new ReportPolicy(SentStats(), CurrentAllowedSubjects());
        return _watch = new RecipeWatch(engine, _host, _keys, policy, active.Recipe, active.State.InputValues, TrackedStats());
    }

    /// <summary>
    /// Adds a row for every account RoRoRo has saved. Runs before EVERY cycle (F8), because the
    /// report policy's allow list comes from these rows. Catches its own host calls: a declined
    /// capability returns its message, and any other failure costs the seed, not the cycle.
    /// </summary>
    private async Task<string?> SeedRowsAsync()
    {
        if (!await _host.IsReachableAsync(CancellationToken.None)) return null;

        IReadOnlyList<HostAccount> accounts;
        try
        {
            accounts = await _host.GetAccountsAsync(CancellationToken.None);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.PermissionDenied)
        {
            return RecipeWatch.RejectedMessage("host.queries.accounts");
        }
        catch (Exception)
        {
            return null;
        }

        var excluded = _active?.State.Excluded ?? new HashSet<Guid>();

        foreach (var account in accounts)
        {
            var existing = _rows.FirstOrDefault(r => r.AccountId == account.AccountId);
            if (existing is not null)
            {
                existing.RobloxUserId = account.RobloxUserId;
                continue;
            }

            _rows.Add(new Row
            {
                AccountId = account.AccountId,
                DisplayName = account.DisplayName,
                RobloxUserId = account.RobloxUserId,
                Send = !excluded.Contains(account.AccountId),
                Cells = Dashes(),
            });
        }

        _watch?.UpdatePolicy(SentStats(), CurrentAllowedSubjects());
        RenderPolicy();
        return null;
    }

    private void OnSendToggled(object? sender, DataGridCellEditEndingEventArgs e)
    {
        var toggled = e.Row.Item as Row;

        Dispatcher.BeginInvoke(() =>
        {
            if (_active is null) return;

            try
            {
                // Stats design §5.3: an account's Send tick that would pass RoRoRo's history limit is undone.
                var ids = AccountIds();
                var sentStats = _active.State.SentStats(_active.Recipe).Count;
                var before = ids.Count(id => !_active.State.Excluded.Contains(id));
                var budget = HistoryBudget.Check(
                    HistoryBudget.Installed(_store.LoadAll().Recipes, ids, exceptSlug: _active.Recipe.Slug),
                    (before, sentStats), (_rows.Count(r => r.Send), sentStats), accountsKnown: true);

                if (!budget.Allowed && toggled is { Send: true })
                {
                    toggled.Send = false;
                    DetailLine.Text = budget.Line;
                    return;
                }

                var excluded = _rows.Where(r => !r.Send).Select(r => r.AccountId.ToString()).ToList();
                var state = _active.State with { ExcludedAccountIds = excluded };
                _store.SaveState(_active.Recipe, state);
                _active = _active with { State = state };

                _watch?.UpdatePolicy(SentStats(), CurrentAllowedSubjects());
                RenderPolicy();
            }
            catch (Exception ex)
            {
                DetailLine.Text = _redactor.Redact($"Could not save that change: {ex.Message}");
            }
        }, DispatcherPriority.Background);
    }

    private void OnStartStopClick(object sender, RoutedEventArgs e)
    {
        if (!_running && _active is null)
        {
            StateLine.Text = "No recipe to run.";
            DetailLine.Text = "Import a recipe first.";
            return;
        }

        _running = !_running;
        StartStopButton.Content = _running ? "Stop Score Watch" : "Start Score Watch";

        if (!_running)
        {
            _timer.Stop();
            StateLine.Text = "Stopped.";
            return;
        }

        _settings = Settings.Load();
        ReloadActive();

        _timer.Interval = TimeSpan.FromSeconds(_active!.Recipe.EffectiveEverySeconds);
        _timer.Start();

        _ = CycleAsync();
    }

    private async void OnTestNowClick(object sender, RoutedEventArgs e)
    {
        if (_active is null)
        {
            StateLine.Text = "No recipe to test.";
            DetailLine.Text = "Import a recipe first.";
            return;
        }

        TestNowButton.IsEnabled = false;
        try
        {
            await CycleAsync();
        }
        finally
        {
            TestNowButton.IsEnabled = true;
        }
    }

    private async Task CycleAsync()
    {
        var active = _active;
        if (active is null) return;

        try
        {
            var seedProblem = await SeedRowsAsync();

            var watch = EnsureWatch(_active ?? active);
            var readSlug = watch.Recipe.Slug;
            var snapshot = await watch.RunOnceAsync(CancellationToken.None);
            Render(snapshot);

            if (seedProblem is not null)
            {
                StateLine.Text = "RoRoRo refused this.";
                DetailLine.Text = seedProblem;
                _trail.Add(Stamp($"SEED REJECTED: {seedProblem}"));
            }

            _trail.Add(Stamp($"{snapshot.State}: {snapshot.Detail}"));

            // A recipe switched while this cycle read the old one: its icon and names belong to the old one.
            if (_active is not null && string.Equals(_active.Recipe.Slug, readSlug, StringComparison.Ordinal))
            {
                if (snapshot.IconText is { } iconText) _ = ApplyIconAsync(iconText, _active.Recipe);
                if (snapshot.CounterNames.Count > 0) SaveCounterNames(snapshot.CounterNames);
            }

            await RenderDashboardAsync(snapshot);
        }
        catch (Exception ex)
        {
            // The window must never die on a cycle.
            StateLine.Text = "Something unexpected went wrong.";
            DetailLine.Text = _redactor.Redact(ex.Message);
            _trail.Add(Stamp($"EXCEPTION: {ex}"));
        }
    }

    private void Render(RecipeSnapshot snapshot)
    {
        StateLine.Text = snapshot.State switch
        {
            WatchState.NeedsInput => "Waiting for a value to be set.",
            WatchState.SourceUnreachable => "Could not reach the data.",
            WatchState.InputNotFound => "Nothing matched what was entered.",
            WatchState.SourceIdle => "Nothing to read right now.",
            WatchState.ShapeNotUnderstood => "The response was not a shape Ur Score understands.",
            WatchState.NoMatches => "None of your accounts are in what came back.",
            WatchState.Reporting => "Reporting to RoRoRo.",
            WatchState.HostDown => "RoRoRo is not running.",
            WatchState.Rejected => "RoRoRo refused the report.",
            WatchState.RateLimited => "The source asked Ur Score to slow down.",
            WatchState.SignInRequired => "The source wants signing in, which recipes cannot do.",
            WatchState.KeyMissing => "A key is needed.",
            WatchState.KeyRejected => "The source rejected the key.",
            WatchState.Showing => "Reading. No stat is set to send to RoRoRo.",
            _ => snapshot.State.ToString(),
        };

        DetailLine.Text = _redactor.Redact(snapshot.Detail);

        if (snapshot.Unresolved.Count > 0)
        {
            DetailLine.Text += "  Not watched yet, no Roblox user id resolved: "
                + string.Join(", ", snapshot.Unresolved.Select(a => a.DisplayName));
        }

        if (_recipeFileNote is not null)
        {
            DetailLine.Text += "  " + _recipeFileNote;
        }

        var sent = SentStats();
        foreach (var line in snapshot.Accounts)
        {
            var row = _rows.FirstOrDefault(r => r.AccountId == line.AccountId);
            if (row is null) continue;

            row.LastValue = StatText.LastSent(sent, line.LastValues);
            row.LastSent = line.LastReportedUtc?.ToLocalTime().ToString("HH:mm:ss") ?? StatText.Dash;
        }

        RenderPolicy();
        RenderRule();
    }

    /// <summary>
    /// The values a recipe took, for people: "battle=ArcadeBattle2026" reads as "ArcadeBattle2026".
    /// The names stay in the trail and diagnostics, where the recipe's own words are what helps.
    /// </summary>
    private static string ContextText(string context) => string.Join(" · ",
        context.Split("; ").Select(part => part.IndexOf('=') is var at and >= 0 ? part[(at + 1)..] : part));

    private async Task RenderDashboardAsync(RecipeSnapshot snapshot)
    {
        ClanLine.Text = snapshot.State is WatchState.Reporting or WatchState.Showing or WatchState.NoMatches or WatchState.HostDown
            ? snapshot.Context is not null ? $"Live: {ContextText(snapshot.Context)}" : "Live."
            : _redactor.Redact(snapshot.Detail);

        // No fresh rows this cycle: leave the last drawing, beside a state line that says what happened.
        if (snapshot.Rows is null) return;

        if (_lastDashboardContext != snapshot.Context)
        {
            _history.Clear();
            _lastDashboardContext = snapshot.Context;
        }

        ClanDetailLine.Text = snapshot.Headline is { Count: > 0 } headline
            ? string.Join(" · ", headline.Select(h => $"{h.Label} {FormatNumber(h.Text)}"))
            : "";

        RenderStatMisses(snapshot.StatMisses);

        var mine = _rows.Where(r => r.RobloxUserId != 0).Select(r => r.RobloxUserId).ToHashSet();
        var ranked = Leaderboard.Rank(snapshot.Rows, mine, _shownStats.FirstOrDefault()?.Key ?? "");

        await RenderLeaderboardAsync(ranked);
        RenderAccountDashboardRows(snapshot, ranked, DateTimeOffset.UtcNow);
    }

    private static string FormatNumber(string? text) =>
        text is null ? "unknown"
        : double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? StatText.Number(number)
        : text;

    /// <summary>Names from Roblox in one batched call, only when resolveNames allows. Your own names are already known.</summary>
    private async Task RenderLeaderboardAsync(IReadOnlyList<RankedRow> ranked)
    {
        var mineNames = _rows.Where(r => r.RobloxUserId != 0)
            .GroupBy(r => r.RobloxUserId)
            .ToDictionary(g => g.Key, g => g.First().DisplayName);

        IReadOnlyDictionary<long, string> resolved = new Dictionary<long, string>();
        if (_settings.ResolveNames)
        {
            var others = ranked.Where(r => !r.IsMine).Select(r => r.UserId).Distinct().ToList();
            if (others.Count > 0)
            {
                resolved = await _nameClient.ResolveAsync(others, CancellationToken.None);
            }
        }

        _leaderboardRows.Clear();
        foreach (var r in ranked)
        {
            _leaderboardRows.Add(new LeaderboardRow
            {
                Position = r.Position.ToString(CultureInfo.CurrentCulture),
                Name = r.IsMine
                    ? mineNames.GetValueOrDefault(r.UserId, $"You ({r.UserId})")
                    : resolved.GetValueOrDefault(r.UserId, $"Member {r.UserId}"),
                Cells = [.. _shownStats.Select(stat => r.Values.TryGetValue(stat.Key, out var value) ? StatText.Number(value) : StatText.Dash)],
                Yours = r.IsMine ? "You" : "",
            });
        }
    }

    /// <summary>
    /// Place, each shown stat with its change since the last read, the rate for the first shown stat,
    /// and a note, per account, from what was read, not what was sent.
    /// </summary>
    private void RenderAccountDashboardRows(RecipeSnapshot snapshot, IReadOnlyList<RankedRow> ranked, DateTimeOffset observedAt)
    {
        var byUserId = ranked.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => g.First());

        foreach (var row in _rows)
        {
            var unavailable = row.RobloxUserId == 0 ? null : snapshot.Unavailable.GetValueOrDefault(row.RobloxUserId);

            if (row.RobloxUserId == 0 || !byUserId.TryGetValue(row.RobloxUserId, out var r))
            {
                ClearDashboard(row);
                row.Note = StatText.Note(unavailable, []);
                continue;
            }

            row.Position = $"#{r.Position}";
            row.RatePerMinute = StatText.Dash;

            var cells = new string[_shownStats.Count];
            var missed = new List<string>();
            for (var index = 0; index < _shownStats.Count; index++)
            {
                var stat = _shownStats[index];
                if (!r.Values.TryGetValue(stat.Key, out var value))
                {
                    cells[index] = StatText.Dash;
                    if (snapshot.CellMisses.ContainsKey((r.UserId, stat.Key))) missed.Add(stat.Label);
                    continue;
                }

                var previous = _history.Record(row.AccountId, stat.Key, value, observedAt);
                cells[index] = StatText.Cell(value, previous);

                if (index == 0)
                {
                    var rate = PointsRate.PerMinute(previous, new PointsSample(value, observedAt));
                    row.RatePerMinute = rate is double perMinute ? $"{perMinute:+0.#;-0.#;0}/min" : StatText.Dash;
                }
            }

            row.Cells = cells;
            row.Note = StatText.Note(unavailable, missed);
        }
    }

    /// <summary>Counter names from a successful read, kept in the recipe's state for the settings screen's search.</summary>
    private void SaveCounterNames(IReadOnlyList<string> names)
    {
        if (_active is null || _active.Recipe.LastStep.Counters is null) return;
        if (names.SequenceEqual(_active.State.SavedCounterNames, StringComparer.Ordinal)) return;

        try
        {
            var state = _active.State with { CounterNames = [.. names] };
            _store.SaveState(_active.Recipe, state);
            _active = _active with { State = state };
        }
        catch (Exception ex)
        {
            _trail.Add(Stamp($"COUNTER NAMES NOT SAVED: {ex.Message}"));
        }
    }

    /// <summary>
    /// The recipe's icon on the window, the taskbar and beside the heading, fetched once per icon text.
    /// Anything that fails keeps Ur Score's own icon (stats design §3.3).
    /// </summary>
    private async Task ApplyIconAsync(string iconText, Recipe recipe)
    {
        if (string.Equals(iconText, _iconText, StringComparison.Ordinal)) return;
        _iconText = iconText;

        string? file;
        try
        {
            file = await _icons.ResolveAsync(iconText, RecipeHosts.ContactedBy(recipe), _closing.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Switched recipes, or a newer icon text, while this one was fetching.
        if (!string.Equals(_active?.Recipe.Slug, recipe.Slug, StringComparison.Ordinal) || !string.Equals(_iconText, iconText, StringComparison.Ordinal)) return;

        if (file is null)
        {
            ResetIcon();
            _trail.Add(Stamp("ICON: the recipe's icon could not be fetched, so the window keeps Ur Score's."));
            return;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(file);
            image.EndInit();
            image.Freeze();

            Icon = image;
            HeadingIcon.Source = image;
            HeadingIcon.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            ResetIcon();
            _trail.Add(Stamp($"ICON: the picture did not decode ({ex.GetType().Name}), so the window keeps Ur Score's."));
        }
    }

    private void ResetIcon()
    {
        ClearValue(IconProperty);
        HeadingIcon.Source = null;
        HeadingIcon.Visibility = Visibility.Collapsed;
    }

    private void RenderPolicy()
    {
        if (_active is null)
        {
            PolicyLine.Text = "No recipe, so nothing is sent to RoRoRo.";
            PolicyCounts.Text = "";
            return;
        }

        var policy = _watch?.Policy ?? new ReportPolicy(SentStats(), CurrentAllowedSubjects());
        PolicyLine.Text = policy.Describe(_rows.Count, _settings.ResolveNames);
        PolicyCounts.Text = _watch is null ? "" : $"Sent {_watch.Policy.Sent}, dropped {_watch.Policy.Dropped}.";
    }

    /// <summary>The sent stats the rule helper can pick from, keeping the current pick when it is still sent.</summary>
    private void RenderRuleChoices()
    {
        var picked = RuleMetricId;
        var choices = SentStats().Select(stat => new RuleChoice(stat.MetricId, $"{stat.Label} ({stat.MetricId})")).ToList();

        RuleStatBox.ItemsSource = choices;
        RuleStatBox.SelectedItem = choices.FirstOrDefault(c => c.MetricId == picked) ?? choices.FirstOrDefault();
        RuleStatBox.IsEnabled = choices.Count > 0;
    }

    private void OnRuleStatChanged(object sender, SelectionChangedEventArgs e) => RenderRule();

    private void RenderRule()
    {
        if (_active is null)
        {
            RuleLine.Text = "Import a recipe first.";
            AddRuleButton.IsEnabled = false;
            RulePreview.Text = "";
            return;
        }

        if (RuleMetricId is not { } metricId)
        {
            RuleLine.Text = "No stat is set to send. Tick Send on a stat in Recipe settings, then add its rule here.";
            AddRuleButton.IsEnabled = false;
            RulePreview.Text = "";
            return;
        }

        (RuleLine.Text, AddRuleButton.IsEnabled) = RuleSentence(metricId);
        RulePreview.Text = AddRuleButton.IsEnabled
            ? $"Rate, below {DefaultThreshold} per minute over {DefaultWindowMinutes} minutes"
            : "";
    }

    /// <summary>What RoRoRo's rules file holds for a metric id, and whether the helper can add one.</summary>
    private static (string Text, bool CanAdd) RuleSentence(string metricId)
    {
        var status = RulesFile.Inspect(null, metricId);
        var recorded = RuleInventory.Recorded(metricId);

        return status.State switch
        {
            RuleState.NoFile =>
                ("RoRoRo has no rules file yet, so nothing can alert until a rule is added. Adding one creates the file.", true),
            RuleState.NoRuleForMetric =>
                ($"RoRoRo has rules, but none for {metricId} — so reports will land and never alert.", true),
            RuleState.OursIntact when recorded is not null && status.Threshold != recorded =>
                ($"Your rule for {metricId} has been changed since Ur Score added it "
                 + $"(now {status.Threshold}, was {recorded}). Left exactly as it is.", false),
            RuleState.OursIntact =>
                ($"Ready: RoRoRo has a rule for {metricId} at {status.Threshold}.", false),
            RuleState.UserOwned =>
                ($"You wrote the rule for {metricId} yourself (threshold {status.Threshold}). Ur Score will not touch it.", false),
            RuleState.OwnedByAnotherPlugin =>
                ($"A rule for {metricId} belongs to {status.Owner}. Left alone.", false),
            RuleState.Unreadable =>
                ("RoRoRo's rules file is not valid JSON. Ur Score will not overwrite it — check it by hand.", false),
            _ => (status.State.ToString(), false),
        };
    }

    private void OnAddRuleClick(object sender, RoutedEventArgs e)
    {
        if (_active is null || RuleMetricId is not { } metricId) return;

        var preview = RulesFile.Preview(metricId, DefaultThreshold, DefaultWindowMinutes);
        var answer = MessageBox.Show(this,
            $"Add this rule to RoRoRo's metric-rules.json?\n\n{preview}\n\n"
            + "Your existing rules are kept, and the file is backed up first.",
            "Ur Score", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);

        if (answer != MessageBoxResult.OK) return;

        try
        {
            if (RulesFile.AddRule(null, metricId, DefaultThreshold, DefaultWindowMinutes))
            {
                RuleInventory.Record(metricId, DefaultThreshold);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not add the rule: {ex.Message}", "Ur Score",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        RenderRule();
    }

    private void OnCopyDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        var inputs = _active is null
            ? "(none)"
            : string.Join(", ", _active.State.InputValues.Select(kv => $"{kv.Key}={kv.Value}"));

        var sent = string.Join(", ", SentStats().Select(stat => $"{stat.Key}->{stat.MetricId}"));
        var shown = string.Join(", ", _shownStats.Select(stat => stat.Key));

        var text = new StringBuilder()
            .AppendLine($"Ur Score diagnostics {DateTimeOffset.UtcNow:O}")
            .AppendLine($"recipe={_active?.Recipe.Slug ?? "(none)"} "
                + $"poll={_active?.Recipe.EffectiveEverySeconds}s resolveNames={_settings.ResolveNames}")
            .AppendLine($"sent={(sent.Length == 0 ? "(none)" : sent)} shown={(shown.Length == 0 ? "(none)" : shown)}")
            .AppendLine($"inputs={inputs}")
            .AppendLine($"host={_host.HostVersion ?? "(not connected)"} reject={_host.RejectReason ?? "(none)"}")
            .AppendLine($"user-agent={UrScoreIdentity.UserAgent}")
            .AppendLine($"raw responses kept in {RawDirectory}")
            .AppendLine()
            .AppendLine(string.Join(Environment.NewLine, _trail.TakeLast(40)))
            .ToString();

        try
        {
            // Redacted as a whole, last, so nothing added above can carry a key out.
            Clipboard.SetText(_redactor.Redact(text));
            DetailLine.Text = "Diagnostics copied to the clipboard.";
        }
        catch (Exception ex)
        {
            DetailLine.Text = $"Could not copy diagnostics: {ex.Message}";
        }
    }

    private void OnImportRecipeClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import a recipe",
            Filter = "Ur Score recipe (*.recipe.json)|*.recipe.json|JSON file (*.json)|*.json",
        };

        if (dialog.ShowDialog(this) != true) return;

        string text;
        try
        {
            text = File.ReadAllText(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not read that file: {ex.Message}", "Ur Score", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var parsed = RecipeParser.Parse(text);
        if (!parsed.Ok)
        {
            MessageBox.Show(this,
                "That recipe could not be imported:\n\n" + string.Join("\n", parsed.Problems.Select(p => "• " + p)),
                "Ur Score", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var recipe = parsed.Recipe!;
        var installed = _store.Find(recipe.Slug);

        if (installed is not null
            && (!string.Equals(installed.Recipe.Name, recipe.Name, StringComparison.Ordinal)
                || !string.Equals(installed.Recipe.Author, recipe.Author, StringComparison.Ordinal)))
        {
            MessageBox.Show(this,
                $"A different recipe, {installed.Recipe.Name}, is already installed under the same file name. "
                + "Rename one of them before importing.",
                "Ur Score", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (installed is not null && string.Equals(installed.Text, text, StringComparison.Ordinal))
        {
            // Spec §6.3: an identical file imports without asking.
            Activate(installed);
            DetailLine.Text = $"{recipe.Name} is already installed, and is the recipe this window runs.";
            return;
        }

        var review = ImportReview.Review(recipe, _keys);
        var comparison = ImportReview.CompareToInstalled(installed?.Recipe, recipe, _keys, installed?.State);

        try
        {
            if (installed is not null && review.CanImport && !comparison.AsksAgain)
            {
                // An update that contacts the same hosts with the same things: listed, not asked.
                _store.Save(recipe, text, installed.State);
                Activate(_store.Find(recipe.Slug)!);
                DetailLine.Text = $"Updated {recipe.Name}. {string.Join(" ", comparison.Changes)}".Trim();
                return;
            }

            var window = new ImportWindow(recipe, review, comparison, installed?.State, _store.LoadAll().Recipes, AccountIds(),
                metricId => RuleSentence(metricId).Text, CounterLookupFor(recipe))
            {
                Owner = this,
            };

            if (window.ShowDialog() != true) return;

            var state = (installed?.State ?? new RecipeState()) with
            {
                Inputs = window.Inputs,
                Stats = window.Stats,
                CounterNames = window.CounterNames,
            };

            _store.Save(recipe, text, state);
            Activate(_store.Find(recipe.Slug)!);
            DetailLine.Text = $"Imported {recipe.Name}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not save that recipe: {ex.Message}", "Ur Score", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnRecipeSettingsClick(object sender, RoutedEventArgs e)
    {
        if (_active is null)
        {
            DetailLine.Text = "Import a recipe first.";
            return;
        }

        var active = _active;
        var window = new ImportWindow(active.Recipe, ImportReview.Review(active.Recipe, _keys),
            new UpdateComparison(false, false, []), active.State, _store.LoadAll().Recipes, AccountIds(),
            metricId => RuleSentence(metricId).Text, CounterLookupFor(active.Recipe), settingsOnly: true)
        {
            Owner = this,
        };

        if (window.ShowDialog() != true) return;

        try
        {
            var state = active.State with { Inputs = window.Inputs, Stats = window.Stats, CounterNames = window.CounterNames };
            _store.SaveState(active.Recipe, state);
            Activate(active with { State = state });
            DetailLine.Text = $"Saved settings for {active.Recipe.Name}.";
        }
        catch (Exception ex)
        {
            DetailLine.Text = $"Could not save those settings: {ex.Message}";
        }
    }

    /// <summary>The settings screen's "Look up stat names" read, offered only for a recipe with counters.</summary>
    private Func<IReadOnlyDictionary<string, string>, Task<ImportWindow.CounterLookup>>? CounterLookupFor(Recipe recipe) =>
        recipe.LastStep.Counters is null ? null : inputs => LookUpCounterNamesAsync(recipe, inputs);

    /// <summary>
    /// One read with every recipe value asked for, so the response can offer its counter names. Its
    /// own engine and no report policy: nothing read here can reach RoRoRo.
    /// </summary>
    private async Task<ImportWindow.CounterLookup> LookUpCounterNamesAsync(Recipe recipe, IReadOnlyDictionary<string, string> inputs)
    {
        try
        {
            await SeedRowsAsync();
            var ids = _rows.Where(r => r.RobloxUserId != 0).Select(r => r.RobloxUserId).ToList();
            var everyValue = recipe.LastStep.Values.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
            var engine = new RecipeEngine(new HttpRecipeTransport(_recipeHttp, RawDirectory, _redactor), _keys);

            var reading = await engine.ReadAsync(recipe, inputs, ids, everyValue, CancellationToken.None);
            _trail.Add(Stamp($"LOOK UP STAT NAMES: {reading.Outcome}, {reading.CounterNames.Count} name(s). {reading.Detail}"));

            if (reading.CounterNames.Count > 0) return new ImportWindow.CounterLookup(reading.CounterNames, null);

            var problem = reading.Outcome != ReadingOutcome.Read ? reading.Detail
                : recipe.LastStep.PerAccount && ids.Count == 0 ? "RoRoRo hasn't shared any accounts yet, so there was nothing to read."
                : $"The source answered, but no {recipe.LastStep.Counters!.Label} came back.";
            return new ImportWindow.CounterLookup([], _redactor.Redact(problem));
        }
        catch (Exception ex)
        {
            return new ImportWindow.CounterLookup([], _redactor.Redact($"Could not look them up: {ex.Message}"));
        }
    }

    /// <summary>
    /// Makes a recipe the one this window runs. A different recipe clears what the dashboard drew
    /// for the old one, and its icon; <see cref="RecipeWatch.UpdateRecipe"/> clears remembered values
    /// when the recipe or its inputs changed.
    /// </summary>
    private void Activate(InstalledRecipe installed)
    {
        var switching = !string.Equals(_active?.Recipe.Slug, installed.Recipe.Slug, StringComparison.Ordinal);

        _settings = _settings with { ActiveRecipe = installed.Recipe.Slug };
        try
        {
            Settings.Save(_settings);
        }
        catch (Exception)
        {
            // Remembering which recipe was active is a convenience; failing to save it costs only that.
        }

        if (switching)
        {
            _history.Clear();
            _leaderboardRows.Clear();
            _iconText = null;
            ResetIcon();
            foreach (var row in _rows)
            {
                ClearDashboard(row);
            }
        }

        ApplyActive(installed);

        RenderRule();
        RenderPolicy();

        if (_running)
        {
            _timer.Interval = TimeSpan.FromSeconds(installed.Recipe.EffectiveEverySeconds);
            _ = CycleAsync();
        }
    }
}
