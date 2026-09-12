using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Grpc.Core;
using Labs626.UrScore.Core;
using Labs626.UrScore.Host;
using Labs626.UrScore.Source;

namespace Labs626.UrScore.UI;

/// <summary>
/// The clan battle dashboard (spec §6.1) with the alert-pipeline diagnostics beneath it.
/// <para>
/// The dashboard's numbers come from <see cref="WatchSnapshot.Contributions"/> and
/// <see cref="WatchSnapshot.Standing"/> — the SAME contributions fetch <see cref="ScoreWatch"/>
/// already makes each cycle to decide what to report, carried out on the result rather than
/// re-derived. Earlier this window instead re-read the raw response
/// <see cref="Source.ClanClient"/> separately saves to <see cref="RawDirectory"/> for diagnostics —
/// that file is explicitly best-effort (<c>SaveRaw</c> swallows its own write failures, and a
/// <see cref="ClanClient"/> can be built with no <see cref="RawDirectory"/> at all), so making the
/// dashboard depend on it meant a diagnostics failure could silently become a dashboard failure.
/// Reading the typed result instead makes that impossible by construction.
/// </para>
/// </summary>
public partial class MainWindow : Window
{
    private const string PluginId = "626labs.ur-score";
    private const double DefaultThreshold = 100;
    private const int DefaultWindowMinutes = 10;

    /// <summary>One row of the accounts grid: the dashboard figures (position, points, rate) and
    /// the diagnostic ones (last value actually sent, and when) side by side, because a toggled-off
    /// account keeps climbing on the left while the right two columns sit still — which is the
    /// point.
    /// <para>
    /// F10: implements <see cref="INotifyPropertyChanged"/> so mutating a row's properties in place
    /// (every cycle, for every row) updates its cells directly instead of the window calling
    /// <c>AccountsGrid.Items.Refresh()</c> after every render. <c>Refresh()</c> throws if a cell is
    /// mid-edit — exactly the state the Send checkbox is in for the moment between a click and the
    /// edit committing — so a poll tick landing in that instant turned the state line into
    /// "Something unexpected went wrong" for no reason a user could see or reproduce reliably.
    /// </para>
    /// </summary>
    public sealed class Row : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _send = true;
        private string _position = "—";
        private string _points = "—";
        private string _ratePerMinute = "—";
        private string _lastValue = "—";
        private string _lastSent = "—";

        public bool Send
        {
            get => _send;
            set => SetField(ref _send, value);
        }

        // Set once, at seed time, and never mutated afterward — no notification needed.
        public string DisplayName { get; set; } = "";
        public Guid AccountId { get; set; }
        public long RobloxUserId { get; set; }

        public string Position
        {
            get => _position;
            set => SetField(ref _position, value);
        }

        public string Points
        {
            get => _points;
            set => SetField(ref _points, value);
        }

        public string RatePerMinute
        {
            get => _ratePerMinute;
            set => SetField(ref _ratePerMinute, value);
        }

        public string LastValue
        {
            get => _lastValue;
            set => SetField(ref _lastValue, value);
        }

        public string LastSent
        {
            get => _lastSent;
            set => SetField(ref _lastSent, value);
        }

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>One row of the leaderboard grid.</summary>
    public sealed class LeaderboardRow
    {
        public string Position { get; set; } = "";
        public string Name { get; set; } = "";
        public string Points { get; set; } = "";
        public string Yours { get; set; } = "";
    }

    private readonly ObservableCollection<Row> _rows = [];
    private readonly ObservableCollection<LeaderboardRow> _leaderboardRows = [];
    private readonly HttpClient _http = new();
    private readonly DispatcherTimer _timer = new();
    private readonly HostClient _host = new(PluginId);
    private readonly NameClient _nameClient;
    private readonly List<string> _trail = [];

    /// <summary>The rate shown per account is computed from this plugin's own last two polls
    /// (spec §6.1, "The rate shown here is not the rate RoRoRo judges") — never from a report,
    /// since a toggled-off account still climbs on screen. Cleared whenever the battle changes,
    /// the same discipline <see cref="ScoreWatch"/> applies to its own remembered lines, so a
    /// finished battle's last value never becomes the "previous" side of a new battle's first
    /// rate.</summary>
    private readonly Dictionary<Guid, PointsSample> _previousSamples = [];

    private string? _lastDashboardBattle;

    private Settings _settings = Settings.Load();
    private ScoreWatch? _watch;
    private bool _running;

    public MainWindow()
    {
        InitializeComponent();
        AccountsGrid.ItemsSource = _rows;
        AccountsGrid.CellEditEnding += OnSendToggled;
        LeaderboardGrid.ItemsSource = _leaderboardRows;

        // The same HttpClient the poll uses. NameClient never throws and is only ever CALLED when
        // Settings.ResolveNames allows it (see RenderLeaderboardAsync) — constructing it here costs
        // nothing on its own.
        _nameClient = new NameClient(_http);

        _timer.Tick += async (_, _) => await CycleAsync();
        RenderRule();
        RenderPolicy();

        AttributionLine.Text = ClanClient.Attribution;

        ClanLine.Text = "Not started.";
        ClanDetailLine.Text = "Your clan's place and points will appear here once Score Watch runs.";

        StateLine.Text = "Not started.";
        DetailLine.Text = string.IsNullOrWhiteSpace(_settings.ClanName)
            ? $"Set a clan name in {Settings.DefaultPath}, then press Start."
            : $"Watching {_settings.ClanName} when started.";
    }

    private string RawDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "626labs.ur-score", "last-response");

    private HashSet<Guid> CurrentAllowedSubjects() =>
        _rows.Where(r => r.Send).Select(r => r.AccountId).ToHashSet();

    /// <summary>
    /// Builds the ONE <see cref="ScoreWatch"/> this window ever uses, the first time a cycle needs
    /// it, and returns that same instance forever after.
    /// <para>
    /// F2: this used to be called fresh at the top of every cycle. A freshly constructed
    /// <c>ScoreWatch</c> gets a freshly constructed serialization semaphore, which serializes
    /// nothing against the instance it replaced — so the timer's tick and a concurrent "Test now"
    /// click each got their own guard and could both reach <c>ReportMetric</c> for the same
    /// observation. Holding one instance is what makes that guard (and the per-battle state it
    /// protects, and the running Sent/Dropped counts) actually mean something across cycles. When
    /// the send list or the settings change afterward, update THIS instance
    /// (<see cref="ScoreWatch.UpdatePolicy"/>, <see cref="ScoreWatch.UpdateSettings"/>) rather than
    /// building a new one.
    /// </para>
    /// </summary>
    private ScoreWatch EnsureWatch()
    {
        if (_watch is not null) return _watch;

        var policy = new ReportPolicy(_settings.MetricId, CurrentAllowedSubjects());
        var source = new ClanClient(_http, RawDirectory);
        return _watch = new ScoreWatch(source, _host, policy, _settings);
    }

    /// <summary>
    /// Adds a row for every account RoRoRo has saved, defaulting to on unless the user has
    /// previously switched it off. Returns a message when the capability that lists accounts has
    /// been declined, so <see cref="CycleAsync"/> can show it; null otherwise, including on any
    /// OTHER failure — this method never throws.
    /// <para>
    /// THIS MUST RUN BEFORE THE FIRST CYCLE, and the reason is a deadlock the pre-flight scan
    /// caught. The report policy's allow list comes from these rows. An earlier draft populated
    /// them only from accounts that had been reported — which the policy would never allow,
    /// because the list they came from was empty. Nothing would ever be reported, no row would
    /// ever appear, and every unit test would still pass.
    /// </para>
    /// <para>
    /// F8: runs at the top of EVERY cycle, not just the first. It used to run only from Start and
    /// Test now — <see cref="ScoreWatch"/> re-fetches accounts every cycle regardless, so an
    /// account saved in RoRoRo mid-session mapped, reached the report gate, and was silently
    /// dropped for want of a row and an allow-list entry, with nothing anywhere explaining why.
    /// </para>
    /// <para>
    /// ROUND 3 (F4/F8 — the same root cause, found twice): guarding this with the CALLER's
    /// try/catch was not enough. This is the FIRST gated call of every cycle — <see cref="CycleAsync"/>
    /// awaits it before <c>ScoreWatch.RunOnceAsync</c> is ever called — so a live test injecting
    /// <c>PermissionDenied</c> here got the generic "Something unexpected went wrong" with a raw
    /// gRPC status, never reaching <c>ScoreWatch</c>'s own identical catch. Worse, letting the
    /// exception propagate aborted the WHOLE cycle before the clan poll ran at all: no dashboard,
    /// no leaderboard, and the graceful <c>HostDown</c> path — the README's promise that Ur Score
    /// "keeps polling the clan" — bypassed for a crash. A seed failure must cost the seed, not the
    /// dashboard, so this method now catches its own two host calls: <c>PermissionDenied</c>
    /// becomes the named capability message (the SAME text <c>ScoreWatch.RejectedMessage</c>
    /// produces, so the two can never drift from each other), and any OTHER failure — most
    /// plausibly F3's own named race, RoRoRo quitting between the reachability probe and the
    /// accounts call — leaves the rows exactly as they were and returns null. Either way,
    /// <see cref="CycleAsync"/> proceeds to the clan poll, whose own fresh
    /// <c>IHostClient.IsReachableAsync</c> check will discover the same outage on its own and land
    /// on <c>HostDown</c>, rather than this cycle producing nothing at all.
    /// </para>
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
            return ScoreWatch.RejectedMessage("host.queries.accounts");
        }
        catch (Exception)
        {
            return null;
        }

        var excluded = _settings.Excluded;

        foreach (var account in accounts)
        {
            var existing = _rows.FirstOrDefault(r => r.AccountId == account.AccountId);
            if (existing is not null)
            {
                // A resolved id arriving after the row already existed (§7): keep the row, but
                // let it start being ranked and rated once it has an id to be found under.
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

        // Push the (possibly grown) allow list into the persistent watch, if one exists yet — a
        // freshly seeded account must be reportable THIS cycle, not the one after. When no watch
        // exists yet, EnsureWatch() picks up the current rows when it constructs one. BEFORE
        // RenderPolicy: it now reads _watch.Policy.AllowedSubjects (via ReportPolicy.Describe), so
        // calling it first would show yesterday's count next to today's total.
        _watch?.UpdatePolicy(_settings.MetricId, CurrentAllowedSubjects());

        RenderPolicy();
        return null;
    }

    /// <summary>
    /// Writes the unticked accounts back to settings, so a choice survives a restart. Called when
    /// the grid's checkbox column commits an edit.
    /// </summary>
    private void OnSendToggled(object? sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var excluded = _rows.Where(r => !r.Send).Select(r => r.AccountId.ToString()).ToList();
                _settings = _settings with { ExcludedAccountIds = excluded };
                Settings.Save(_settings);

                // The checkbox just changed the allow list; the persistent watch's policy must
                // agree by the NEXT cycle, not the one after (see EnsureWatch's doc for why it is
                // updated in place rather than rebuilt). BEFORE RenderPolicy, for the same reason
                // as SeedRowsAsync.
                _watch?.UpdatePolicy(_settings.MetricId, CurrentAllowedSubjects());

                RenderPolicy();
            }
            catch (Exception ex)
            {
                // Round 3: the last unguarded IO reachable from a UI callback, on the file the
                // README invites people to keep open while Ur Score runs. No dispatcher exception
                // handler is registered anywhere, so an escaping exception here would have killed
                // the whole window over a single checkbox failing to save.
                DetailLine.Text = $"Could not save that change: {ex.Message}";
            }
        }, DispatcherPriority.Background);
    }

    private void OnStartStopClick(object sender, RoutedEventArgs e)
    {
        _running = !_running;
        StartStopButton.Content = _running ? "Stop Score Watch" : "Start Score Watch";

        if (!_running)
        {
            _timer.Stop();
            StateLine.Text = "Stopped.";
            return;
        }

        // F9: reload from disk. settings.json is the file we tell people to hand-edit, and this
        // window otherwise reads it exactly once, at construction — a checkbox toggled hours into
        // a session would then save that startup snapshot back over whatever was edited in the
        // meantime. Reloading on Start closes most of that window; push the fresh values into an
        // already-existing watch rather than rebuilding it (see EnsureWatch's doc).
        _settings = Settings.Load();

        // Round 3, F9's missed key: reloading _settings alone left every EXISTING row's Send
        // exactly as it was — SeedRowsAsync only ever sets Send on a NEW row. A hand-edited
        // excludedAccountIds was therefore ignored by an already-open window, and the very next
        // checkbox commit would have saved the stale in-memory state straight back over it. Every
        // row's Send is re-synced from the freshly loaded exclusions before anything reads it.
        var excluded = _settings.Excluded;
        foreach (var row in _rows)
        {
            row.Send = !excluded.Contains(row.AccountId);
        }

        _watch?.UpdateSettings(_settings);
        _watch?.UpdatePolicy(_settings.MetricId, CurrentAllowedSubjects());

        // Interval from settings, floored at the vendor's cache — polling faster returns the same
        // bytes and is simply rude.
        _timer.Interval = TimeSpan.FromSeconds(_settings.EffectivePollSeconds);
        _timer.Start();

        _ = CycleAsync();   // do not make the user wait three minutes to see whether it works
    }

    private async void OnTestNowClick(object sender, RoutedEventArgs e)
    {
        // One cycle, narrated, without starting the timer. The first thing anyone reaches for when
        // this does not work. Disabled while it runs — RunOnceAsync now serialises itself against
        // an overlapping timer tick and WAITS rather than skipping, so a second click here would
        // otherwise sit queued behind the first with no sign anything was happening.
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
        try
        {
            // F8: every cycle, not just the first — see SeedRowsAsync's doc. Round 3: SeedRowsAsync
            // now guards its OWN two host calls and never throws — this try/catch remains as the
            // backstop for anything else in the cycle, not as the seed's only protection, which
            // round 3 proved insufficient (a seed-level exception here used to abort everything
            // below, including the clan poll and its graceful HostDown path).
            var seedProblem = await SeedRowsAsync();

            var watch = EnsureWatch();
            var snapshot = await watch.RunOnceAsync(CancellationToken.None);
            Render(snapshot);

            if (seedProblem is not null)
            {
                // Takes priority over whatever the poll cycle concluded: a declined capability is
                // the more urgent fact, and the poll's own state (idle, no battle, whatever it is)
                // says nothing about it at all. The clan poll still ran and the dashboard below
                // still reflects it — only the diagnostics headline is overridden.
                StateLine.Text = "RoRoRo refused this.";
                DetailLine.Text = seedProblem;
                _trail.Add($"{DateTimeOffset.UtcNow:O} SEED REJECTED: {seedProblem}");
            }

            _trail.Add($"{DateTimeOffset.UtcNow:O} {snapshot.State}: {snapshot.Detail}");

            // The dashboard reads the SAME raw response the diagnostics above already caused
            // ClanClient to save this cycle. Kept separate from Render/RenderPolicy/RenderRule
            // (which report on the watch loop's own state) and guarded on its own, so a problem
            // reading that file costs the dashboard, never the diagnostics that just rendered fine.
            await RenderDashboardAsync(snapshot);
        }
        catch (Exception ex)
        {
            // The window must never die on a cycle. Whatever happened is a sentence, not a crash.
            StateLine.Text = "Something unexpected went wrong.";
            DetailLine.Text = ex.Message;
            _trail.Add($"{DateTimeOffset.UtcNow:O} EXCEPTION: {ex}");
        }
    }

    private void Render(WatchSnapshot snapshot)
    {
        StateLine.Text = snapshot.State switch
        {
            WatchState.Idle => "Idle — no clan name set.",
            WatchState.SourceUnreachable => "Could not reach the clan data.",
            WatchState.ClanNotFound => "Clan not found.",
            WatchState.NoBattle => "No clan battle running.",
            WatchState.ShapeNotUnderstood => "The response was not a shape Ur Score understands.",
            WatchState.NoMatches => "None of your accounts are in this battle's contributions.",
            WatchState.Reporting => "Reporting to RoRoRo.",
            WatchState.HostDown => "RoRoRo is not running.",
            WatchState.Rejected => "RoRoRo refused the report.",
            _ => snapshot.State.ToString(),
        };

        DetailLine.Text = snapshot.Detail ?? "";

        if (snapshot.Unresolved.Count > 0)
        {
            // Appended rather than replacing the detail: an unresolved account coexists with
            // reporting, and hiding the reporting to mention it would be the wrong trade.
            DetailLine.Text += $"  Not watched yet, no Roblox user id resolved: "
                + string.Join(", ", snapshot.Unresolved.Select(a => a.DisplayName));
        }

        foreach (var line in snapshot.Accounts)
        {
            // Rows come from SeedRowsAsync, never from reports — populating them here would be the
            // deadlock described on SeedRowsAsync. A line with no row is an account the host added
            // between the seed and now, and it will get its row on the next seed.
            var row = _rows.FirstOrDefault(r => r.AccountId == line.AccountId);
            if (row is null) continue;

            row.LastValue = line.LastValue?.ToString("0.##") ?? "—";
            row.LastSent = line.LastReportedUtc?.ToLocalTime().ToString("HH:mm:ss") ?? "—";
        }

        RenderPolicy();
        RenderRule();
    }

    private async Task RenderDashboardAsync(WatchSnapshot snapshot)
    {
        ClanLine.Text = snapshot.State switch
        {
            WatchState.Idle => "No clan name set.",
            WatchState.SourceUnreachable => "Could not reach the clan data.",
            WatchState.ClanNotFound => "Clan not found.",
            WatchState.ShapeNotUnderstood => "The response was not a shape Ur Score understands yet.",
            WatchState.NoBattle => "No clan battle is running right now.",
            _ => snapshot.Battle is not null ? $"Battle: {snapshot.Battle}" : "Waiting for a battle.",
        };

        if (snapshot.Contributions is null)
        {
            // No fresh clan data landed this cycle — read plainly off the snapshot rather than
            // re-deriving "was there a fetch" from the state enum. Leave whatever the last
            // successful cycle drew: a frozen dashboard beside a state sentence that plainly says
            // what happened (above) reads as "here is the last we knew," never as a lie about what
            // is live right now.
            return;
        }

        if (_lastDashboardBattle != snapshot.Battle)
        {
            // A new battle. Yesterday's rate has no business being the "previous" half of today's
            // first sample — the same reason ScoreWatch clears its own remembered lines.
            _previousSamples.Clear();
            _lastDashboardBattle = snapshot.Battle;
        }

        ClanDetailLine.Text = $"Place {FormatPlace(snapshot.Standing?.Place)} · "
            + $"{FormatPoints(snapshot.Standing?.Points)} points";

        var mine = _rows.Where(r => r.RobloxUserId != 0).Select(r => r.RobloxUserId).ToHashSet();
        var ranked = ClanStanding.Rank(snapshot.Contributions, mine);

        await RenderLeaderboardAsync(ranked);
        RenderAccountDashboardRows(ranked, DateTimeOffset.UtcNow);
    }

    private static string FormatPlace(long? place) => place is long p ? $"#{p:N0}" : "unknown";

    private static string FormatPoints(double? points) => points is double v ? v.ToString("N0") : "unknown";

    /// <summary>
    /// Names come from Roblox in one batched call, gated on <see cref="Settings.ResolveNames"/>
    /// (spec §6.1, "How this squares with the report policy"). The user's own accounts are never
    /// part of that call — their names are already known locally, from RoRoRo — so turning
    /// resolution off costs only strangers' names, never the user's own.
    /// </summary>
    private async Task RenderLeaderboardAsync(IReadOnlyList<RankedContribution> ranked)
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
            var name = r.IsMine
                ? mineNames.GetValueOrDefault(r.UserId, $"You ({r.UserId})")
                : resolved.GetValueOrDefault(r.UserId, $"Member {r.UserId}");

            _leaderboardRows.Add(new LeaderboardRow
            {
                Position = r.Position.ToString(),
                Name = name,
                Points = r.Points.ToString("N0"),
                Yours = r.IsMine ? "You" : "",
            });
        }

        // No Refresh() needed: LeaderboardGrid is read-only (no cell can be mid-edit, so nothing
        // here can throw the way AccountsGrid's could) and _leaderboardRows is an
        // ObservableCollection whose Clear/Add above already raise the change notifications the
        // grid is bound to.
    }

    /// <summary>
    /// Position, points and a rate for every one of the user's accounts — sourced from the
    /// LEADERBOARD, not from what was actually reported. A toggled-off account still climbs on the
    /// clan's own scoreboard; only the "Last sent" columns care whether the report policy actually
    /// let it through (see <see cref="Render"/>).
    /// </summary>
    private void RenderAccountDashboardRows(IReadOnlyList<RankedContribution> ranked, DateTimeOffset observedAt)
    {
        var byUserId = ranked
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var row in _rows)
        {
            if (row.RobloxUserId == 0 || !byUserId.TryGetValue(row.RobloxUserId, out var r))
            {
                // No Roblox id yet (§7), or simply not a contributor in this battle. Leave the
                // rate history alone — it belongs to a value this account actually had, not to a
                // gap in the clan's own data.
                row.Position = "—";
                row.Points = "—";
                continue;
            }

            row.Position = $"#{r.Position}";
            row.Points = r.Points.ToString("N0");

            var current = new PointsSample(r.Points, observedAt);
            var previous = _previousSamples.GetValueOrDefault(row.AccountId);
            var rate = PointsRate.PerMinute(previous, current);
            row.RatePerMinute = rate is double perMinute ? $"{perMinute:+0.#;-0.#;0}/min" : "—";
            _previousSamples[row.AccountId] = current;
        }
    }

    /// <summary>
    /// Residual from the F6 fix round: this used to build the policy sentence by hand, right down
    /// to <c>Math.Max(_rows.Count, allowed)</c> — the exact plausible-looking-wrong-number clamp
    /// Task 5's review rejected in favour of dropping the comparison. Calling
    /// <see cref="ReportPolicy.Describe"/> instead means the sentence shown on screen is the same
    /// one <c>ReportPolicyTests</c> guards, rather than a second, independently maintained copy
    /// that can drift from it (which is exactly what happened: <c>Describe</c> still said "leaves
    /// this plugin" after this method had already been fixed to say something true).
    /// </summary>
    private void RenderPolicy()
    {
        // No clamp: the real count, however it compares to the allow list. Describe itself decides
        // whether "X of your Y accounts" is sayable or whether to fall back to "X accounts".
        var policy = _watch?.Policy ?? new ReportPolicy(_settings.MetricId, CurrentAllowedSubjects());
        PolicyLine.Text = policy.Describe(_rows.Count, _settings.ResolveNames);

        PolicyCounts.Text = _watch is null
            ? ""
            : $"Sent {_watch.Policy.Sent}, dropped {_watch.Policy.Dropped}.";
    }

    private void RenderRule()
    {
        var status = RulesFile.Inspect(null, _settings.MetricId);
        var recorded = RuleInventory.Recorded(_settings.MetricId);

        (RuleLine.Text, AddRuleButton.IsEnabled) = status.State switch
        {
            RuleState.NoFile =>
                ($"RoRoRo has no rules file yet, so nothing can alert. Adding the rule below creates one.", true),
            RuleState.NoRuleForMetric =>
                ($"RoRoRo has rules, but none for {_settings.MetricId} — so reports will land and never alert.", true),
            RuleState.OursIntact when recorded is not null && status.Threshold != recorded =>
                ($"Your rule for {_settings.MetricId} has been changed since Ur Score added it "
                 + $"(now {status.Threshold}, was {recorded}). Left exactly as it is.", false),
            RuleState.OursIntact =>
                ($"Ready: RoRoRo has a rule for {_settings.MetricId} at {status.Threshold}.", false),
            RuleState.UserOwned =>
                ($"You wrote the rule for {_settings.MetricId} yourself (threshold {status.Threshold}). "
                 + "Ur Score will not touch it.", false),
            RuleState.OwnedByAnotherPlugin =>
                ($"A rule for {_settings.MetricId} belongs to {status.Owner}. Left alone.", false),
            RuleState.Unreadable =>
                ("RoRoRo's rules file is not valid JSON. Ur Score will not overwrite it — "
                 + "check it by hand.", false),
            _ => (status.State.ToString(), false),
        };

        RulePreview.Text = AddRuleButton.IsEnabled
            ? $"Rate, below {DefaultThreshold} per minute over {DefaultWindowMinutes} minutes"
            : "";
    }

    private void OnAddRuleClick(object sender, RoutedEventArgs e)
    {
        // The preview IS the consent, so show the exact JSON before writing a byte of it.
        var preview = RulesFile.Preview(_settings.MetricId, DefaultThreshold, DefaultWindowMinutes);
        var answer = MessageBox.Show(this,
            $"Add this rule to RoRoRo's metric-rules.json?\n\n{preview}\n\n"
            + "Your existing rules are kept, and the file is backed up first.",
            "Ur Score", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);

        if (answer != MessageBoxResult.OK) return;

        try
        {
            // F10: unguarded before this — RulesFile.AddRule does real file IO (a copy for the
            // backup, then a write) with no try/catch of its own, unlike RuleInventory.Record,
            // which already protects itself. A locked file or a denied permission mid-click must
            // not crash the window that exists to make this easy.
            if (RulesFile.AddRule(null, _settings.MetricId, DefaultThreshold, DefaultWindowMinutes))
            {
                RuleInventory.Record(_settings.MetricId, DefaultThreshold);
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
        // Everything someone would otherwise ask for one message at a time.
        var text = new StringBuilder()
            .AppendLine($"Ur Score diagnostics {DateTimeOffset.UtcNow:O}")
            .AppendLine($"clan={_settings.ClanName} metric={_settings.MetricId} poll={_settings.EffectivePollSeconds}s "
                + $"resolveNames={_settings.ResolveNames}")
            .AppendLine($"host={_host.HostVersion ?? "(not connected)"} reject={_host.RejectReason ?? "(none)"}")
            .AppendLine($"user-agent={ClanClient.UserAgent}")
            .AppendLine($"raw responses kept in {RawDirectory}")
            .AppendLine()
            .AppendLine(string.Join(Environment.NewLine, _trail.TakeLast(40)))
            .ToString();

        try
        {
            // F10: the one button someone reaches for when everything else has already failed
            // must not itself throw — Clipboard.SetText can (another app holding the clipboard
            // mid-copy is a common, transient Windows condition, not a bug in either program).
            Clipboard.SetText(text);
            DetailLine.Text = "Diagnostics copied to the clipboard.";
        }
        catch (Exception ex)
        {
            DetailLine.Text = $"Could not copy diagnostics: {ex.Message}";
        }
    }
}
