using System.Windows;
using System.Windows.Automation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Labs626.UrScore.Board;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

using Source = Labs626.UrScore.Core.Source;

/// <summary>
/// The starter board (spec §8): the top bar, the fixed panels in a 12-column grid, and the first-run states.
/// Everything it shows comes from <see cref="AppServices"/>; everything that is setup is in the Setup window.
/// </summary>
public partial class BoardWindow : Window
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(20) };
    private readonly List<(PanelSpec Spec, FrameworkElement View)> _panels = [];

    /// <summary>Other members' names for a Live leaderboard panel, in memory only, each id asked once.</summary>
    private readonly Dictionary<long, string> _names = [];
    private readonly HashSet<long> _askedNames = [];

    private SetupWindow? _setup;
    private StarterBoard? _board;
    private string? _boardKey;

    /// <summary>Start is asking RoRoRo for accounts before the loops begin.</summary>
    private bool _starting;

    /// <summary>A Test now read is in flight. Stop still works; see <see cref="BoardButtons"/>.</summary>
    private bool _testing;

    /// <summary>The empty state's Import recipe… is in flight.</summary>
    private bool _importing;

    public BoardWindow(AppServices services)
    {
        InitializeComponent();
        ThemeService.Attach(this);
        _services = services;
        _services.Changed += Render;
        _services.IconChanged += ApplyIcon;
        _clock.Tick += (_, _) => RenderLines();
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _clock.Stop();
            _services.Changed -= Render;
            _services.IconChanged -= ApplyIcon;
        };

        ApplyButtons();
        Render();
        StateLine.Text = "Reading your score book…";
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _services.StartFollowingTheme();
        _clock.Start();

        try
        {
            await _services.LoadBookAsync();
        }
        catch (Exception ex)
        {
            StateLine.Text = "Your score book could not be read.";
            DetailLine.Text = _services.Redactor.Redact(ex.Message);
            _services.AddTrail($"BOOK NOT LOADED: {ex}");
            return;
        }

        ApplyButtons();
        Render();

        // Spec §7.1: a recipe with inputs and no sources opens Setup on its Clans page.
        if (SetupPages.FirstRunPage(_services.Installed, _services.Sources) is { } page) OpenSetup(page);
    }

    /// <summary>The window must never die on a redraw: what fails goes to the trail, by type only, and the last drawing stays.</summary>
    private void Render()
    {
        try
        {
            RenderBoard();
        }
        catch (Exception ex)
        {
            _services.AddTrail($"BOARD NOT DRAWN: {ex.GetType().Name}");
        }
    }

    private void RenderBoard()
    {
        var board = StarterBoards.Build(_services.Installed, _services.Sources);
        _board = board;
        RenderEmpty(board);

        if (board.Key != _boardKey)
        {
            _boardKey = board.Key;
            BoardPanels.Children.Clear();
            _panels.Clear();

            var counts = new Dictionary<PanelType, int>();
            foreach (var spec in board.Panels)
            {
                counts[spec.Type] = counts.GetValueOrDefault(spec.Type) + 1;
                var view = PanelViews.Create(spec.Type);
                AutomationProperties.SetAutomationId(view, $"{spec.Type}Panel{counts[spec.Type]}");
                PanelGrid.SetSpan(view, spec.Span);
                BoardPanels.Children.Add(view);
                _panels.Add((spec, view));
            }
        }

        var live = _services.CurrentBoard();

        // The reader is filled on a worker thread until the book has loaded; nothing may read it before then.
        if (_services.ReaderLoaded)
        {
            foreach (var (spec, view) in _panels)
            {
                try
                {
                    PanelViews.Render(view, spec.Settings, live, _services.Reader, _names);
                }
                catch (Exception ex)
                {
                    // A panel never takes the window down. The type only: a message could name another player's id.
                    _services.AddTrail($"PANEL NOT DRAWN: {spec.Type}: {ex.GetType().Name}");
                }
            }
        }

        _ = ResolveNamesAsync(live);
        RenderLines(live);
    }

    private void RenderLines(LiveBoard? live = null)
    {
        ApplyButtons();

        try
        {
            RenderLinesCore(live ?? _services.CurrentBoard());
        }
        catch (Exception ex)
        {
            _services.AddTrail($"LINES NOT DRAWN: {ex.GetType().Name}");
        }
    }

    private void RenderLinesCore(LiveBoard live)
    {
        PeriodLine.Text = BoardText.TopLine(live, _board?.AnchorSourceId);
        LiveDot.Visibility = live.Running ? Visibility.Visible : Visibility.Collapsed;
        StartStopButton.Content = live.Running ? "Stop" : "Start";
        AttributionLine.Text = BoardText.Attribution(live);

        if (!_services.ReaderLoaded) return;
        StateLine.Text = BoardText.StateLine(live, _services.EverStarted);
        DetailLine.Text = BoardText.DetailLine(live, _services.BudgetWarning);
    }

    private void RenderEmpty(StarterBoard board)
    {
        var recipe = _services.Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, board.RecipeSlug, StringComparison.Ordinal))?.Recipe;
        var (line, detail, button) = BoardText.EmptyState(board.Empty, recipe);
        var empty = board.Empty != BoardEmpty.None;

        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        BoardScroll.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyStateLine.Text = line;
        EmptyStateDetail.Text = detail;
        EmptyStateButton.Content = button;
        AutomationProperties.SetName(EmptyStateButton, button);
    }

    /// <summary>Names for Live leaderboard panels only, and only when resolveNames allows; the stage 1 starter boards have none.</summary>
    private async Task ResolveNamesAsync(LiveBoard live)
    {
        if (!_services.Settings.ResolveNames) return;

        var mine = live.MyUserIds;
        var ids = _panels
            .Where(p => p.Spec.Type == PanelType.LiveLeaderboard)
            .Select(p => live.FindSource(p.Spec.Settings.SourceId))
            .OfType<Source>()
            .SelectMany(s => live.SnapshotOf(s.Id)?.Rows ?? [])
            .Select(r => r.UserId)
            .Where(id => !mine.Contains(id) && _askedNames.Add(id))
            .ToList();
        if (ids.Count == 0) return;

        var resolved = await _services.Names.ResolveAsync(ids, CancellationToken.None);
        foreach (var (id, name) in resolved) _names[id] = name;
        if (resolved.Count > 0) Render();
    }

    private async void OnStartStopClick(object sender, RoutedEventArgs e)
    {
        if (_services.Running)
        {
            // Never gated on a Test now read: Stop only ends the timed loops, and the read in flight finishes
            // on its own token, as a Test now pressed while stopped would.
            try
            {
                _services.Stop();
            }
            catch (Exception ex)
            {
                ShowFailure(ex);
            }

            RenderLines();
            return;
        }

        // The button is disabled for these; this only catches a press already on its way.
        if (!BoardButtons.For(_services.ReaderLoaded, running: false, _starting, _testing, _importing).StartStop) return;

        if (_services.Installed.Count == 0)
        {
            StateLine.Text = "No recipe to run.";
            DetailLine.Text = "Import a recipe first.";
            return;
        }

        _starting = true;
        ApplyButtons();

        try
        {
            await _services.StartAsync();
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
        finally
        {
            _starting = false;
            RenderLines();
        }
    }

    private async void OnTestNowClick(object sender, RoutedEventArgs e)
    {
        if (!BoardButtons.For(_services.ReaderLoaded, _services.Running, _starting, _testing, _importing).TestNow) return;
        if (_services.Installed.Count == 0)
        {
            StateLine.Text = "No recipe to test.";
            DetailLine.Text = "Import a recipe first.";
            return;
        }

        _testing = true;
        ApplyButtons();
        StateLine.Text = "Reading every source once…";

        try
        {
            await _services.TestNowAsync();
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
        finally
        {
            _testing = false;
            RenderLines();
        }
    }

    /// <summary>The one place the board's buttons are enabled or disabled, called after every change to what they depend on.</summary>
    private void ApplyButtons()
    {
        var states = BoardButtons.For(_services.ReaderLoaded, _services.Running, _starting, _testing, _importing);
        StartStopButton.IsEnabled = states.StartStop;
        TestNowButton.IsEnabled = states.TestNow;
        EmptyStateButton.IsEnabled = states.EmptyState;
    }

    private void OnSetupClick(object sender, RoutedEventArgs e) => OpenSetup(null);

    private async void OnEmptyStateClick(object sender, RoutedEventArgs e)
    {
        if (_board is null || _importing) return;

        if (_board.Empty == BoardEmpty.NoStats)
        {
            OpenSetup(SetupPages.Stats);
            return;
        }

        if (_board.Empty == BoardEmpty.NoSources && _board.RecipeSlug is { } slug)
        {
            OpenSetup(SetupPages.ClansId(slug));
            return;
        }

        if (_board.Empty != BoardEmpty.NoRecipes) return;

        _importing = true;
        ApplyButtons();
        try
        {
            var outcome = await ImportFlow.RunAsync(this, _services, text => DetailLine.Text = text);
            if (outcome is null) return;

            DetailLine.Text = outcome.Message;
            if (outcome.ChooseSources) OpenSetup(SetupPages.ClansId(outcome.Slug));
        }
        finally
        {
            _importing = false;
            ApplyButtons();
        }
    }

    private void OpenSetup(string? page)
    {
        if (_setup is not null)
        {
            if (page is not null) _setup.ShowPage(page);
            _setup.Activate();
            return;
        }

        _setup = new SetupWindow(_services, page) { Owner = this };
        _setup.Closed += (_, _) => _setup = null;
        _setup.Show();
    }

    private void ShowFailure(Exception ex)
    {
        // The window must never die on a cycle.
        StateLine.Text = "Something unexpected went wrong.";
        DetailLine.Text = _services.Redactor.Redact(ex.Message);
        _services.AddTrail($"EXCEPTION: {ex}");
    }

    /// <summary>The main source's icon on the window, the taskbar and the top bar; anything that fails keeps Ur Score's own.</summary>
    private void ApplyIcon(string? file)
    {
        if (file is null)
        {
            ResetIcon();
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
            BoardIcon.Source = image;
            BoardIcon.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            ResetIcon();
            _services.AddTrail($"ICON: the picture did not decode ({ex.GetType().Name}), so the window keeps Ur Score's.");
        }
    }

    private void ResetIcon()
    {
        ClearValue(IconProperty);
        BoardIcon.Source = null;
        BoardIcon.Visibility = Visibility.Collapsed;
    }
}
