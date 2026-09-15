using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Labs626.UrScore.Board;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

using Source = Labs626.UrScore.Core.Source;

/// <summary>One tab. Its text is the board's name, so a screen reader and UI Automation read the name.</summary>
public sealed record BoardTabItem(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// The board (spec §8, §9.2): tabs and the top bar, the selected board's panels in a 12-column grid, and the
/// empty states. Everything it shows comes from <see cref="AppServices"/>; every board change goes through
/// <see cref="BoardEdits"/> and <see cref="AppServices.SaveBoards"/>; everything that is setup is in the Setup window.
/// </summary>
public partial class BoardWindow : Window
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(20) };

    /// <summary>What is on the grid now, in order: each panel's definition, its view and its automation id.</summary>
    private readonly List<(PanelDef Def, FrameworkElement View, string AutomationId)> _panels = [];

    /// <summary>Other members' names for a Live leaderboard panel, in memory only, each id asked once.</summary>
    private readonly Dictionary<long, string> _names = [];
    private readonly HashSet<long> _askedNames = [];

    private SetupWindow? _setup;

    /// <summary>The board on screen; null until the first draw, and a board that is gone falls back to the first (R12).</summary>
    private string? _boardId;

    private string? _renderedKey;
    private string? _tabsKey;
    private string? _anchorSourceId;
    private string? _emptyRecipe;

    /// <summary>Why the last board change couldn't be saved, or null.</summary>
    private string? _boardsNote;

    private BoardEmpty _empty;

    /// <summary>The tabs are being set from the boards, not by a click.</summary>
    private bool _selectingTab;

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
        var boards = _services.Boards;
        var board = ShownBoard(boards);
        _boardId = board.Id;

        RenderTabs(boards, board);
        RenderEmpty(board);

        var key = ViewKey(board);
        if (key != _renderedKey)
        {
            _renderedKey = key;
            BuildPanels(board);
        }

        _anchorSourceId = BoardEdits.AnchorSourceId(board, _services.Sources);
        var live = _services.CurrentBoard();

        // The reader is filled on a worker thread until the book has loaded; nothing may read it before then.
        if (_services.ReaderLoaded)
        {
            foreach (var (def, view, _) in _panels) RenderPanel(def, view, live);
        }

        _ = ResolveNamesAsync(live);
        RenderLines(live);
    }

    /// <summary>The board on screen: the selected tab's, else the first (R12).</summary>
    private BoardDef ShownBoard(IReadOnlyList<BoardDef> boards) =>
        boards.FirstOrDefault(b => b.Id == _boardId) ?? boards[0];

    /// <summary>The panels are rebuilt only when this changes.</summary>
    private string ViewKey(BoardDef board) => BoardDefs.Key(board);

    private void BuildPanels(BoardDef board)
    {
        BoardPanels.Children.Clear();
        _panels.Clear();

        var ids = BoardEdits.AutomationIds(board);
        for (var i = 0; i < board.Panels.Count; i++)
        {
            var def = board.Panels[i];
            var view = CreatePanelView(def, ids[i]);
            PanelGrid.SetSpan(view, def.Size.Span);
            PanelGrid.SetTall(view, def.Size.Tall);
            BoardPanels.Children.Add(view);
            _panels.Add((def, view, ids[i]));
        }
    }

    /// <summary>The control for one panel on the board, named by its automation id.</summary>
    private FrameworkElement CreatePanelView(PanelDef def, string automationId)
    {
        var view = PanelViews.Create(def.Type);
        AutomationProperties.SetAutomationId(view, automationId);
        return view;
    }

    private void RenderPanel(PanelDef def, FrameworkElement view, LiveBoard live)
    {
        try
        {
            PanelViews.Render(view, def.Settings, live, _services.Reader, _names);
        }
        catch (Exception ex)
        {
            // A panel never takes the window down. The type only: a message could name another player's id.
            _services.AddTrail($"PANEL NOT DRAWN: {def.Type}: {ex.GetType().Name}");
        }
    }

    /// <summary>Every panel showing anywhere, for looking up Live leaderboard names.</summary>
    private IEnumerable<PanelDef> ShownPanels() => _panels.Select(p => p.Def);

    private void RenderTabs(IReadOnlyList<BoardDef> boards, BoardDef shown)
    {
        var key = string.Join("|", boards.Select(b => $"{b.Id}={b.Name}"));
        _selectingTab = true;
        try
        {
            if (key != _tabsKey)
            {
                _tabsKey = key;
                BoardTabs.ItemsSource = boards.Select(b => new BoardTabItem(b.Id, b.Name)).ToList();
            }

            if ((BoardTabs.SelectedItem as BoardTabItem)?.Id != shown.Id)
            {
                BoardTabs.SelectedItem = BoardTabs.Items.OfType<BoardTabItem>().FirstOrDefault(t => t.Id == shown.Id);
            }
        }
        finally
        {
            _selectingTab = false;
        }
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
        PeriodLine.Text = BoardText.TopLine(live, _anchorSourceId);
        LiveDot.Visibility = live.Running ? Visibility.Visible : Visibility.Collapsed;
        StartStopButton.Content = live.Running ? "Stop" : "Start";
        AttributionLine.Text = BoardText.Attribution(live);

        if (!_services.ReaderLoaded) return;
        StateLine.Text = BoardText.StateLine(live, _services.EverStarted);

        var detail = BoardText.DetailLine(live, _services.BudgetWarning);
        DetailLine.Text = detail.Length > 0 ? detail : _boardsNote ?? _services.BoardsProblem ?? "";
    }

    private void RenderEmpty(BoardDef board)
    {
        var starter = StarterBoards.Build(_services.Installed, _services.Sources);
        _empty = BoardText.EmptyFor(starter, _services.BoardsFollowStarter, board);
        _emptyRecipe = starter.RecipeSlug;

        var recipe = _services.Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, _emptyRecipe, StringComparison.Ordinal))?.Recipe;
        var (line, detail, button) = BoardText.EmptyState(_empty, recipe);
        var empty = _empty != BoardEmpty.None;

        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        BoardScroll.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyStateLine.Text = line;
        EmptyStateDetail.Text = detail;
        EmptyStateButton.Content = button;

        // No panels has no button until the gallery arrives (Task 6).
        EmptyStateButton.Visibility = button.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(EmptyStateButton, button);
    }

    // ---- tabs ----

    private void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectingTab || BoardTabs.SelectedItem is not BoardTabItem tab || tab.Id == _boardId) return;

        ShowBoard(tab.Id);
    }

    /// <summary>The menu opens on what is true now, e.g. a board deleted since the last redraw.</summary>
    private void OnTabMenuOpened(object sender, RoutedEventArgs e) => ApplyButtons();

    private void OnAddBoardClick(object sender, RoutedEventArgs e)
    {
        var boards = _services.Boards;
        var dialog = new AddBoardWindow(_services.Installed, _services.Sources, BoardEdits.NextName(boards)) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } board) return;

        if (SaveBoards(BoardEdits.Add(boards, board))) ShowBoard(board.Id);
    }

    private void OnRenameBoardClick(object sender, RoutedEventArgs e)
    {
        var boards = _services.Boards;
        var board = ShownBoard(boards);
        var dialog = new BoardNameWindow(board.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var renamed = BoardEdits.Rename(boards, board.Id, dialog.BoardName);
        if (!ReferenceEquals(renamed, boards)) SaveBoards(renamed);
    }

    private void OnDuplicateBoardClick(object sender, RoutedEventArgs e)
    {
        var boards = _services.Boards;
        var duplicated = BoardEdits.Duplicate(boards, ShownBoard(boards).Id);
        if (ReferenceEquals(duplicated, boards)) return;

        var copy = duplicated.First(b => boards.All(old => old.Id != b.Id));
        if (SaveBoards(duplicated)) ShowBoard(copy.Id);
    }

    private void OnDeleteBoardClick(object sender, RoutedEventArgs e)
    {
        var boards = _services.Boards;
        var board = ShownBoard(boards);

        // The item is disabled for the last board; this only catches a press already on its way.
        if (!BoardButtons.For(_services.ReaderLoaded, _services.Running, _starting, _testing, _importing, boards.Count).DeleteBoard) return;

        var answer = MessageBox.Show(this,
            $"Delete the {board.Name} board? Its panels go with it. Your score book isn't touched.",
            "Ur Score", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;

        var remaining = BoardEdits.Delete(boards, board.Id);
        if (!ReferenceEquals(remaining, boards) && SaveBoards(remaining)) ShowBoard(remaining[0].Id);
    }

    private void ShowBoard(string boardId)
    {
        _boardId = boardId;
        Render();
    }

    /// <summary>Saves through the services. A file that can't be written says so on the detail line, and the board stays as it was.</summary>
    private bool SaveBoards(IReadOnlyList<BoardDef> boards)
    {
        try
        {
            _services.SaveBoards(boards);
            _boardsNote = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _boardsNote = $"Your boards couldn't be saved: {_services.Redactor.Redact(ex.Message)}";
            _services.AddTrail($"BOARDS NOT SAVED: {ex.GetType().Name}");
            RenderLines();
            return false;
        }
    }

    // ---- names, the top bar, Setup ----

    /// <summary>Names for Live leaderboard panels only, and only when resolveNames allows. In memory only.</summary>
    private async Task ResolveNamesAsync(LiveBoard live)
    {
        if (!_services.Settings.ResolveNames) return;

        var mine = live.MyUserIds;
        var ids = ShownPanels()
            .Where(p => p.Type == PanelType.LiveLeaderboard)
            .Select(p => live.FindSource(p.Settings.SourceId))
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
        var states = BoardButtons.For(_services.ReaderLoaded, _services.Running, _starting, _testing, _importing, _services.Boards.Count);
        StartStopButton.IsEnabled = states.StartStop;
        TestNowButton.IsEnabled = states.TestNow;
        EmptyStateButton.IsEnabled = states.EmptyState;
        DeleteBoardItem.IsEnabled = states.DeleteBoard;
    }

    private void OnSetupClick(object sender, RoutedEventArgs e) => OpenSetup(null);

    private async void OnEmptyStateClick(object sender, RoutedEventArgs e)
    {
        if (_importing) return;

        if (_empty == BoardEmpty.NoStats)
        {
            OpenSetup(SetupPages.Stats);
            return;
        }

        if (_empty == BoardEmpty.NoSources && _emptyRecipe is { } slug)
        {
            OpenSetup(SetupPages.ClansId(slug));
            return;
        }

        if (_empty != BoardEmpty.NoRecipes) return;

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
