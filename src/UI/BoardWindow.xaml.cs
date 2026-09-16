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
    /// <summary>The most of the top bar's left side the tabs take before they scroll, leaving room for + Board and the period line.</summary>
    private const double TabStripShare = 0.6;

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

    /// <summary>Why a board change couldn't be saved, only while its message box is open; the box is the notification.</summary>
    private string? _boardsNote;

    /// <summary>Why the score book couldn't be read at start, or null. It keeps the detail line until the book loads.</summary>
    private string? _bookProblem;

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

        // Panel tools raise one routed event; each concern handles its own tools (Tasks 6-8).
        PanelFrame.SetShowSettings(BoardPanels, true);
        BoardPanels.AddHandler(PanelFrame.ToolEvent, new EventHandler<PanelToolEventArgs>(OnSettingsTool));
        HookEditing();
        HookPopOuts();
        HookAccounts();

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
            _bookProblem = _services.Redactor.Redact(ex.Message);
            DetailLine.Text = _bookProblem;
            _services.AddTrail($"BOOK NOT LOADED: {ex}");
            return;
        }

        ApplyButtons();
        Render();

        // Spec §7.1: a recipe with inputs and no sources opens Setup on its Clans page.
        var firstRun = SetupPages.FirstRunPage(_services.Installed, _services.Sources);
        if (firstRun is not null) OpenSetup(firstRun);

        // Plan A33: with "Start reading as soon as Ur Score opens" ticked, the board does once what pressing Start
        // does — after the book is read, and never while Setup has just opened on a recipe that has no source yet.
        if (!BoardButtons.StartsOnOpen(
                _services.Settings.StartOnOpen, _services.ReaderLoaded, _services.Running,
                _services.Installed.Count, _services.Sources.Any(s => s.Enabled), firstRun is not null))
        {
            return;
        }

        _services.AddTrail("START ON OPEN: reading started because Setup > Recipes has it ticked.");
        await StartReadingAsync();
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

        // Only once shown: the constructor's first draw must not open windows ahead of the board.
        if (IsLoaded) SyncPopOuts();

        _anchorSourceId = BoardEdits.AnchorSourceId(board, _services.Sources);
        var live = _services.CurrentBoard();

        // The reader is filled on a worker thread until the book has loaded; nothing may read it before then.
        if (_services.ReaderLoaded)
        {
            foreach (var (def, view, _) in _panels) RenderPanel(def, view, live, board.Id);
            RenderPopOuts(live);
        }

        _ = ResolveNamesAsync(live);
        RenderLines(live);
    }

    /// <summary>The board on screen: the draft while editing, else the selected tab's, else the first (R12).</summary>
    private BoardDef ShownBoard(IReadOnlyList<BoardDef> boards) =>
        _draft ?? boards.FirstOrDefault(b => b.Id == _boardId) ?? boards[0];

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
            PanelFrame.SetCurrentSize(view, def.Size);
            BoardPanels.Children.Add(view);
            _panels.Add((def, view, ids[i]));
        }
    }

    /// <summary>The control for one panel on the board, named by its automation id; a popped-out panel's slot holds a placeholder (R19).</summary>
    private FrameworkElement CreatePanelView(PanelDef def, string automationId)
    {
        if (def.PopOut is not null) return PoppedOutPlaceholder(def, automationId);

        var view = PanelViews.Create(def.Type);
        AutomationProperties.SetAutomationId(view, automationId);
        return view;
    }

    private void RenderPanel(PanelDef def, FrameworkElement view, LiveBoard live, string boardId)
    {
        try
        {
            PanelViews.Render(view, def.Settings, live, _services.Reader, _names, SessionFor(boardId, def));
        }
        catch (Exception ex)
        {
            // A panel never takes the window down. The type only: a message could name another player's id.
            _services.AddTrail($"PANEL NOT DRAWN: {def.Type}: {ex.GetType().Name}");
        }
    }

    /// <summary>Every panel showing anywhere, on the board or popped out, for looking up Live leaderboard names.</summary>
    private IEnumerable<PanelDef> ShownPanels()
    {
        foreach (var (def, _, _) in _panels)
        {
            if (def.PopOut is null) yield return def;
        }

        foreach (var id in _popOuts.Keys.ToList())
        {
            if (BoardEdits.Find(_services.Boards, id) is { } found) yield return found.Panel;
        }
    }

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
                if (BoardTabs.SelectedItem is { } selected) BoardTabs.ScrollIntoView(selected);
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

        var boardsProblem = _boardsNote ?? _services.BoardsProblem;
        if (!_services.ReaderLoaded)
        {
            // Why your boards aren't showing is said from the first draw (R3), unless the score book's own failure is on the line.
            if (boardsProblem is not null && _bookProblem is null) DetailLine.Text = boardsProblem;
            return;
        }

        StateLine.Text = BoardText.StateLine(live, _services.EverStarted);
        DetailLine.Text = BoardText.DetailLine(live, _services.BudgetWarning, boardsProblem);
    }

    private void RenderEmpty(BoardDef board)
    {
        var starters = StarterBoards.All(_services.Installed, _services.Sources);
        // A draft is a board being shaped, not a tab following your sources: with no panels it says so and offers Add panel.
        _empty = BoardText.EmptyFor(starters, board, Editing);
        _emptyRecipe = (StarterBoards.Named(starters, board.Follows) ?? StarterBoards.EmptyState(starters)).RecipeSlug;

        var recipe = _services.Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, _emptyRecipe, StringComparison.Ordinal))?.Recipe;
        var (line, detail, button) = BoardText.EmptyState(_empty, recipe, Editing);
        var empty = _empty != BoardEmpty.None;

        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        BoardScroll.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyStateLine.Text = line;
        EmptyStateDetail.Text = detail;
        EmptyStateButton.Content = button;

        // Only BoardEmpty.None has no button.
        EmptyStateButton.Visibility = button.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(EmptyStateButton, button);
    }

    // ---- tabs ----

    private void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectingTab || !ButtonStates().Tabs || BoardTabs.SelectedItem is not BoardTabItem tab || tab.Id == _boardId) return;

        ShowBoard(tab.Id);
    }

    /// <summary>The menu opens on what is true now, e.g. a board deleted since the last redraw.</summary>
    private void OnTabMenuOpened(object sender, RoutedEventArgs e) => ApplyButtons();

    /// <summary>
    /// ⋯ beside the tabs (backlog V3-S.8: the owner saw no way to rename or delete a board). It opens the tab's own
    /// menu, not a copy of it, so the two ways in always offer the same three commands.
    /// </summary>
    private void OnBoardMenuClick(object sender, RoutedEventArgs e)
    {
        if (!ButtonStates().BoardMenu) return;

        BoardMenu.AimAt(TabMenu, BoardMenuButton);
        TabMenu.IsOpen = true;
    }

    /// <summary>
    /// The menu goes back to the mouse for the next right-click. Closed with nothing chosen, focus goes back to ⋯,
    /// which is where it came from; a command that ran has already put it on the board's tab.
    /// </summary>
    private void OnTabMenuClosed(object sender, RoutedEventArgs e)
    {
        if (BoardMenu.Release(TabMenu, BoardMenuButton)) BoardMenuButton.Focus();
    }

    // Each handler that opens a dialog reads the boards again once it closes, and edits that list: something
    // else (a pop-out coming back, later) may have changed them while it was open. While editing they are all
    // disabled (R8); each guard only catches a press already on its way.

    private void OnAddBoardClick(object sender, RoutedEventArgs e)
    {
        if (!ButtonStates().AddBoard) return;

        var dialog = new AddBoardWindow(_services.Installed, _services.Sources, BoardEdits.NextName(_services.Boards)) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } board) return;

        if (SaveBoards(BoardEdits.Add(_services.Boards, board))) ShowBoard(board.Id);
    }

    private void OnRenameBoardClick(object sender, RoutedEventArgs e)
    {
        if (!ButtonStates().RenameBoard) return;

        var board = ShownBoard(_services.Boards);
        var dialog = new BoardNameWindow(board.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        // The name it already has changes nothing, so nothing is written (R1).
        var boards = _services.Boards;
        var renamed = BoardEdits.Rename(boards, board.Id, dialog.BoardName);
        if (ReferenceEquals(renamed, boards) || !SaveBoards(renamed)) return;

        ShowBoard(board.Id);
        FocusShownTab();
    }

    private void OnDuplicateBoardClick(object sender, RoutedEventArgs e)
    {
        if (!ButtonStates().DuplicateBoard) return;

        var boards = _services.Boards;
        var duplicated = BoardEdits.Duplicate(boards, ShownBoard(boards).Id);
        if (ReferenceEquals(duplicated, boards)) return;

        var copy = duplicated.First(b => boards.All(old => old.Id != b.Id));
        if (!SaveBoards(duplicated)) return;

        ShowBoard(copy.Id);
        FocusShownTab();
    }

    private void OnDeleteBoardClick(object sender, RoutedEventArgs e)
    {
        var board = ShownBoard(_services.Boards);

        // The item is disabled for the last board and while editing; this only catches a press already on its way.
        if (!ButtonStates().DeleteBoard) return;

        // Asked in Ur Score's own window, in the theme, never a stock Windows box (owner rule, backlog V3-S.10).
        if (!ConfirmWindow.Ask(this, BoardText.DeleteBoardQuestion(board))) return;

        var boards = _services.Boards;
        var remaining = BoardEdits.Delete(boards, board.Id);
        if (ReferenceEquals(remaining, boards) || !SaveBoards(remaining)) return;

        ShowBoard(remaining[0].Id);
        FocusShownTab();
    }

    private void ShowBoard(string boardId)
    {
        _boardId = boardId;
        Render();
    }

    /// <summary>A tab menu action rebuilds the tabs, which drops keyboard focus; it goes back to the tab on screen.</summary>
    private void FocusShownTab()
    {
        BoardTabs.UpdateLayout();
        if (BoardTabs.SelectedItem is { } item && BoardTabs.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem tab) tab.Focus();
    }

    /// <summary>
    /// Saves through the services. A file that can't be written is never silent: the detail line says the change
    /// wasn't saved and why, in the theme, where the eye already is — the way Setup › Alerts says a failed write on
    /// the card, and not a stock box to dismiss (owner rule, backlog V3-S.10). The note takes that line over (R3)
    /// and stays until a save works, so a change that silently didn't happen can't be clicked away and forgotten.
    /// </summary>
    private bool SaveBoards(IReadOnlyList<BoardDef> boards)
    {
        try
        {
            _services.SaveBoards(boards);
            if (_boardsNote is not null)
            {
                // A save that worked takes the last failure's note off the line, so "RoRoRo is not running" or the
                // budget line isn't hidden behind an old one.
                _boardsNote = null;
                RenderLines();
            }

            return true;
        }
        catch (Exception ex)
        {
            // Any failure, not only IO: a save that silently did nothing is the one thing this must never be.
            _boardsNote = _services.Redactor.Redact(BoardText.BoardsNotSaved(ex));
            _services.AddTrail($"BOARDS NOT SAVED: {ex.GetType().Name}");
            RenderLines();
            return false;
        }
    }

    /// <summary>The tabs never take more than their share of what the top bar's buttons leave (<see cref="TabStripShare"/>).</summary>
    private void OnTopBarSizeChanged(object sender, SizeChangedEventArgs e) =>
        BoardTabs.MaxWidth = Math.Max(0, (TopBar.ActualWidth - TopButtons.ActualWidth) * TabStripShare);

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

        await StartReadingAsync();
    }

    /// <summary>
    /// Start, from the button or from opening (plan A33): the same gate above it, the same in-flight flag, the same
    /// failure line. One path, so "from open" can never become a second, subtly different way to start.
    /// </summary>
    private async Task StartReadingAsync()
    {
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
        var states = ButtonStates();
        StartStopButton.IsEnabled = states.StartStop;
        TestNowButton.IsEnabled = states.TestNow;
        EmptyStateButton.IsEnabled = states.EmptyState;
        BoardTabs.IsEnabled = states.Tabs;
        AddBoardButton.IsEnabled = states.AddBoard;
        RenameBoardItem.IsEnabled = states.RenameBoard;
        DuplicateBoardItem.IsEnabled = states.DuplicateBoard;
        DeleteBoardItem.IsEnabled = states.DeleteBoard;
        BoardMenuButton.IsEnabled = states.BoardMenu;
        EditBoardButton.IsEnabled = states.EditBoard;
        AddPanelButton.IsEnabled = states.AddPanel;
        DoneButton.IsEnabled = states.Done;
    }

    /// <summary>What every button, tab and tab menu item takes right now, for <see cref="ApplyButtons"/> and the press guards.</summary>
    private BoardButtonStates ButtonStates() =>
        BoardButtons.For(_services.ReaderLoaded, _services.Running, _starting, _testing, _importing, _services.Boards.Count, Editing);

    private void OnSetupClick(object sender, RoutedEventArgs e) => OpenSetup(null);

    private async void OnEmptyStateClick(object sender, RoutedEventArgs e)
    {
        if (_importing) return;

        if (_empty == BoardEmpty.NoPanels)
        {
            AddPanelFromGallery();
            return;
        }

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
