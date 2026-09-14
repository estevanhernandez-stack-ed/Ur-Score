using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
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
/// pipeline's diagnostics below. Part 1 runs one recipe; part 2 adds the recipe list.
/// </summary>
public partial class MainWindow : Window
{
    private const string PluginId = "626labs.ur-score";
    private const double DefaultThreshold = 100;
    private const int DefaultWindowMinutes = 10;

    /// <summary>
    /// One row of the accounts grid. Raises change notifications so rows update in place: calling
    /// <c>Items.Refresh()</c> throws while the Send checkbox is mid-edit (F10).
    /// </summary>
    public sealed class Row : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _send = true;
        private string _position = "—";
        private string _value = "—";
        private string _ratePerMinute = "—";
        private string _lastValue = "—";
        private string _lastSent = "—";

        public bool Send { get => _send; set => SetField(ref _send, value); }

        public string DisplayName { get; set; } = "";

        public Guid AccountId { get; set; }

        public long RobloxUserId { get; set; }

        public string Position { get => _position; set => SetField(ref _position, value); }

        public string Value { get => _value; set => SetField(ref _value, value); }

        public string RatePerMinute { get => _ratePerMinute; set => SetField(ref _ratePerMinute, value); }

        public string LastValue { get => _lastValue; set => SetField(ref _lastValue, value); }

        public string LastSent { get => _lastSent; set => SetField(ref _lastSent, value); }

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
        public string Value { get; set; } = "";
        public string Yours { get; set; } = "";
    }

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
    private readonly RecipeStore _store = new(RecipeStore.DefaultDirectory);
    private readonly KeyStore _keys = new(KeyStore.DefaultPath);
    private readonly Redactor _redactor;
    private readonly List<string> _trail = [];

    /// <summary>This window's own rate samples, cleared when the context changes, never taken from a report.</summary>
    private readonly Dictionary<Guid, PointsSample> _previousSamples = [];

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

    /// <summary>Until the rule helper picks a sent stat (Task 13), it writes a rule for the first one.</summary>
    private string MetricId => SentStats().FirstOrDefault()?.MetricId ?? "";

    /// <summary>Until the window shows a column per stat (Task 13), it ranks and shows the recipe's first value.</summary>
    private string FirstStatKey => _active?.Recipe.LastStep.Values[0].Id ?? "";

    private IReadOnlySet<string> TrackedStats() => _active?.State.TrackedStats(_active.Recipe) ?? new HashSet<string>();

    private IReadOnlyList<SentStat> SentStats() => _active?.State.SentStats(_active.Recipe) ?? [];

    private IReadOnlyCollection<Guid> AccountIds() => [.. _rows.Select(r => r.AccountId)];

    private HashSet<Guid> CurrentAllowedSubjects() => _rows.Where(r => r.Send).Select(r => r.AccountId).ToHashSet();

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
    /// checkboxes follow its state, the watch takes its recipe and inputs, and the heading re-renders.
    /// Both <see cref="ReloadActive"/> and <see cref="Activate"/> go through here, so a recipe swap
    /// cannot update the watch in one place and forget it in the other.
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
        if (_active is null)
        {
            HeadingLine.Text = "No recipe yet";
            ClanLine.Text = "Import a recipe to start.";
            ClanDetailLine.Text = "A recipe says where a number is. Ur Score reads it and hands your accounts' values to RoRoRo.";
            AttributionLine.Text = "";
            DetailLine.Text = _storeProblems.Count > 0
                ? _redactor.Redact("Some recipe files could not be read: " + string.Join(" | ", _storeProblems))
                : $"Recipes live in {RecipeStore.DefaultDirectory}.";
            return;
        }

        var recipe = _active.Recipe;
        HeadingLine.Text = recipe.Name;
        ClanLine.Text = "Not started.";
        ClanDetailLine.Text = "";
        AttributionLine.Text = recipe.Credit;
        LeaderboardValueColumn.Header = recipe.LastStep.Values[0].Label;
        AccountsValueColumn.Header = recipe.LastStep.Values[0].Label;
        DetailLine.Text = $"Reads {recipe.Name} when started.";
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
            });
        }

        _watch?.UpdatePolicy(SentStats(), CurrentAllowedSubjects());
        RenderPolicy();
        return null;
    }

    private void OnSendToggled(object? sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_active is null) return;

            try
            {
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

            var snapshot = await EnsureWatch(_active ?? active).RunOnceAsync(CancellationToken.None);
            Render(snapshot);

            if (seedProblem is not null)
            {
                StateLine.Text = "RoRoRo refused this.";
                DetailLine.Text = seedProblem;
                _trail.Add(Stamp($"SEED REJECTED: {seedProblem}"));
            }

            _trail.Add(Stamp($"{snapshot.State}: {snapshot.Detail}"));
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

        foreach (var line in snapshot.Accounts)
        {
            var row = _rows.FirstOrDefault(r => r.AccountId == line.AccountId);
            if (row is null) continue;

            row.LastValue = line.LastValues.TryGetValue(FirstStatKey, out var last) ? last.ToString("0.##") : "—";
            row.LastSent = line.LastReportedUtc?.ToLocalTime().ToString("HH:mm:ss") ?? "—";
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
            _previousSamples.Clear();
            _lastDashboardContext = snapshot.Context;
        }

        ClanDetailLine.Text = snapshot.Headline is { Count: > 0 } headline
            ? string.Join(" · ", headline.Select(h => $"{h.Label} {FormatNumber(h.Text)}"))
            : "";

        var mine = _rows.Where(r => r.RobloxUserId != 0).Select(r => r.RobloxUserId).ToHashSet();
        var ranked = Leaderboard.Rank(snapshot.Rows, mine, FirstStatKey);

        await RenderLeaderboardAsync(ranked);
        RenderAccountDashboardRows(ranked, DateTimeOffset.UtcNow);
    }

    private static string FormatNumber(string? text) =>
        text is null ? "unknown"
        : double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number.ToString("N0")
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
                Value = r.Values.TryGetValue(FirstStatKey, out var value) ? value.ToString("N0") : "—",
                Yours = r.IsMine ? "You" : "",
            });
        }
    }

    /// <summary>Position, value and rate per account, from what was read, not what was sent.</summary>
    private void RenderAccountDashboardRows(IReadOnlyList<RankedRow> ranked, DateTimeOffset observedAt)
    {
        var byUserId = ranked.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => g.First());

        foreach (var row in _rows)
        {
            if (row.RobloxUserId == 0 || !byUserId.TryGetValue(row.RobloxUserId, out var r))
            {
                row.Position = "—";
                row.Value = "—";
                continue;
            }

            row.Position = $"#{r.Position}";
            if (!r.Values.TryGetValue(FirstStatKey, out var value))
            {
                row.Value = "—";
                continue;
            }

            row.Value = value.ToString("N0");

            var current = new PointsSample(value, observedAt);
            var rate = PointsRate.PerMinute(_previousSamples.GetValueOrDefault(row.AccountId), current);
            row.RatePerMinute = rate is double perMinute ? $"{perMinute:+0.#;-0.#;0}/min" : "—";
            _previousSamples[row.AccountId] = current;
        }
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

    private void RenderRule()
    {
        if (_active is null)
        {
            RuleLine.Text = "Import a recipe first.";
            AddRuleButton.IsEnabled = false;
            RulePreview.Text = "";
            return;
        }

        if (string.IsNullOrWhiteSpace(MetricId))
        {
            RuleLine.Text = "No stat is set to send. Tick Send on a stat in Recipe settings, then add its rule here.";
            AddRuleButton.IsEnabled = false;
            RulePreview.Text = "";
            return;
        }

        (RuleLine.Text, AddRuleButton.IsEnabled) = RuleSentence(MetricId);
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
        if (_active is null) return;
        if (string.IsNullOrWhiteSpace(MetricId)) return;

        var metricId = MetricId;
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

        var text = new StringBuilder()
            .AppendLine($"Ur Score diagnostics {DateTimeOffset.UtcNow:O}")
            .AppendLine($"recipe={_active?.Recipe.Slug ?? "(none)"} metric={MetricId} "
                + $"poll={_active?.Recipe.EffectiveEverySeconds}s resolveNames={_settings.ResolveNames}")
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
    /// for the old one; <see cref="RecipeWatch.UpdateRecipe"/> clears remembered values when the
    /// recipe or its inputs changed.
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
            _previousSamples.Clear();
            _leaderboardRows.Clear();
            foreach (var row in _rows)
            {
                row.Position = "—";
                row.Value = "—";
                row.RatePerMinute = "—";
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
