using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Labs626.UrScore.Board;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Theming;
using Labs626.UrScore.Core;

namespace Labs626.UrScore.UI;

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

    /// <summary>How long the undo toast shows (spec §5.3: about six seconds).</summary>
    private readonly DispatcherTimer _toastClock = new() { Interval = TimeSpan.FromSeconds(6) };

    /// <summary>
    /// The board the undo toast speaks for, and whether it offers Undo. Its Undo shows only while that board is on
    /// screen, so a toast carried over a tab switch (Done by tab click) can't undo a different board (BC6).
    /// </summary>
    private string? _toastBoardId;
    private bool _toastCanUndo;

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

    /// <summary>
    /// Why the score book couldn't be read, in plain words, or null. It keeps the detail line, and the board's empty state
    /// offers Try again, until the book loads (S1-14.2).
    /// </summary>
    private string? _bookProblem;

    /// <summary>The score book is being read, at open or from Try again.</summary>
    private bool _readingBook;

    /// <summary>
    /// Start, Stop or Test now went wrong in a way nothing else names. The line says so until the next of those presses: it was
    /// written once and then drawn over by the same press's own redraw, so it was never on screen.
    /// </summary>
    private bool _failed;

    /// <summary>
    /// What the empty state's Import recipe… said: its wait for RoRoRo, then what it did or why it couldn't. Kept for the detail
    /// line until the next import or the next Start, Stop or Test now, instead of being drawn over by the next redraw (S1-12.4).
    /// </summary>
    private string? _importNote;

    private BoardEmpty _empty;

    /// <summary>The tabs are being set from the boards, not by a click.</summary>
    private bool _selectingTab;

    /// <summary>Start is asking RoRoRo for accounts before the loops begin.</summary>
    private bool _starting;

    /// <summary>A Test now read is in flight. Pausing still works; see <see cref="BoardButtons"/>.</summary>
    private bool _testing;

    /// <summary>The empty state's Import recipe… is in flight.</summary>
    private bool _importing;

    /// <summary>
    /// Whether any source was switched on at the last draw, so the first one to come on asks start-on-open again (§3.6).
    /// Null until opening has made its own start-on-open decision: the book's load redraws before that decision, and a
    /// redraw asking first would start reading ahead of it (and again from it, or past its first-run skip).
    /// </summary>
    private bool? _hadSources;

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
        // At open, from last session's pictures (V3-S.6): the icon doesn't wait for the first read.
        ApplyIcon(_services.WindowIcon);
        _clock.Tick += (_, _) => RenderLines();
        _toastClock.Tick += (_, _) =>
        {
            _toastClock.Stop();
            UndoToast.Visibility = Visibility.Collapsed;
        };
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _clock.Stop();
            _toastClock.Stop();
            _services.Changed -= Render;
            _services.IconChanged -= ApplyIcon;
        };

        ApplyButtons();
        Render();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _services.StartFollowingTheme();
        _clock.Start();
        await OpenOnTheBookAsync();
    }

    /// <summary>
    /// Reads the score book, then does what opening does once it is read. At open, and again from the empty state's Try again
    /// when it couldn't be read (S1-14.2): the window never finished opening, so a book read on the second try gets the same
    /// first-run page and start-on-open the first try would have.
    /// </summary>
    private async Task OpenOnTheBookAsync()
    {
        if (!await ReadBookAsync() || _popOutLifecycle.ClosingApp) return;

        // Spec §7.1: a recipe with inputs and no sources opens Setup on its Clans page.
        var firstRun = SetupPages.FirstRunPage(_services.Installed, _services.Sources);
        if (firstRun is not null) OpenSetup(firstRun);

        // Sources already on at open are not "the first one came on" (§3.6); RenderBoard asks again only on none-to-some.
        _hadSources = _services.Sources.Any(s => s.Enabled);

        // Plan A33: with "Start reading when Ur Score opens" ticked, the board starts reading itself — after
        // the book is read, and never while Setup has just opened on a recipe that has no source yet.
        if (!BoardButtons.StartsOnOpen(
                _services.Settings.StartOnOpen, _services.ReaderLoaded, _services.Running,
                _services.Installed.Count, _services.Sources.Any(s => s.Enabled), firstRun is not null))
        {
            return;
        }

        _services.AddTrail("START ON OPEN: reading started because Setup > Recipes has it ticked.");
        await StartReadingAsync();
    }

    /// <summary>
    /// Whether the score book was read. A book that couldn't be read says why in plain words on the detail line, and the board
    /// offers Try again; the exception itself goes to the trail (S1-14.2). <see cref="AppServices.LoadBookAsync"/> starts a new
    /// read after a failed one, and reading the book again replaces what a failed read left, so a retry is safe.
    /// </summary>
    private async Task<bool> ReadBookAsync()
    {
        _readingBook = true;
        _bookProblem = null;
        ApplyButtons();
        Render();

        try
        {
            await _services.LoadBookAsync();
            return true;
        }
        catch (Exception ex)
        {
            _bookProblem = _services.Redactor.Redact(BoardText.BookUnread(ex));
            _services.AddTrail($"BOOK NOT LOADED: {ex.GetType().Name}");
            return false;
        }
        finally
        {
            _readingBook = false;
            ApplyButtons();
            Render();
        }
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
        ShowToastUndo();

        RenderTabs(boards, board);
        RenderEmpty(board);

        var key = ViewKey(board);
        if (_services.ReaderLoaded && key != _renderedKey)
        {
            _renderedKey = key;
            BuildPanels(board);
        }

        // Only once shown: the constructor's first draw must not open windows ahead of the board.
        if (IsLoaded && _services.ReaderLoaded) SyncPopOuts();

        _anchorSourceId = BoardEdits.AnchorSourceId(board, _services.Sources, _services.Installed);
        var live = _services.CurrentBoard();

        // The reader is filled on a worker thread until the book has loaded; nothing may read it before then.
        if (_services.ReaderLoaded)
        {
            foreach (var (def, view, _) in _panels) RenderPanel(def, view, live, board.Id);
            RenderPopOuts(live);
        }

        // Not awaited, and not lost: a lookup failing used to vanish, since a discarded task's fault reaches no
        // handler; its type goes to the trail now (S1-14.6).
        Unawaited.TrailFailures(ResolveNamesAsync(live), _services.AddTrail, "NAMES NOT RESOLVED");
        RenderLines(live);

        // §3.6: the first source switched on asks start-on-open again, for a board that never started. Only once opening
        // has decided for itself (_hadSources is set there).
        if (_hadSources is { } had)
        {
            var hasSources = _services.Sources.Any(s => s.Enabled);
            // Recorded before starting, so a redraw from inside the start never sees none-to-some a second time.
            _hadSources = hasSources;
            if (hasSources && !had && _services.ReaderLoaded)
            {
                Unawaited.TrailFailures(StartIfOpenWouldHaveAsync("when the first clan was switched on"), _services.AddTrail, "START ON OPEN FAILED");
            }
        }
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

        BoardPanels.NoGrips = board.Panels.Select((p, i) => (p, i)).Where(x => x.p.PopOut is not null).Select(x => x.i).ToHashSet();
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
        PeriodLine.Lead = BoardText.TopLine(live, _anchorSourceId);
        PeriodLine.Ends = BoardText.TopEnds(live, _anchorSourceId);
        AttributionLine.Text = BoardText.Attribution(live);
        ArrangeLine.Text = ArrangeBannerText();

        var chip = StatusChip.StateOf(live.Running, _services.EverStarted, _starting, BoardText.InTrouble(live));
        RenderChip(chip);

        var boardsProblem = _boardsNote ?? _services.BoardsProblem;
        if (!_services.ReaderLoaded)
        {
            // Why the score book couldn't be read comes first (S1-14.2); else why your boards aren't showing, said from the first
            // draw (R3).
            StateLine.Text = BoardText.BookStateLine(unread: _bookProblem is not null);
            DetailLine.Text = _bookProblem ?? boardsProblem ?? _importNote ?? "";
            // Arranging keeps the banner in the lines' place (spec §4.2).
            StateLines.Visibility = Editing ? Visibility.Collapsed : Visibility.Visible;
            return;
        }

        // Worked out on every redraw from what the window is doing, never written once, so no redraw wipes it (S1-14.3, S1-14.5).
        var activity = new BoardActivity(_starting, _testing, _services.AskedReadAt, _services.StoppedAt, _failed);
        StateLine.Text = BoardText.StateLine(live, _services.EverStarted, activity);
        DetailLine.Text = BoardText.DetailLine(live, _services.BudgetWarning, boardsProblem, _failed ? BoardText.UnexpectedDetail : _importNote);

        // BC3: the pair shows only when it has something to say; the card always has the lot. While arranging the
        // banner holds their slot (spec §4.2).
        StateLines.Visibility = StatusChip.ShowsLines(true, chip, _failed, DetailLine.Text, live.OldestRemembered is not null) && !Editing
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (StatusCard.IsOpen) RenderCard(live);
    }

    /// <summary>The chip, its name and the window title, from one state (BC2). Enabled-ness is ApplyButtons'.</summary>
    private void RenderChip(ChipState chip)
    {
        ChipGlyph.Text = StatusChip.Glyph(chip);
        ChipWord.Text = StatusChip.Word(chip);
        if (StatusChip.BrushKey(chip) is { } key) ChipGlyph.SetResourceReference(TextBlock.ForegroundProperty, key);
        else ChipGlyph.ClearValue(TextBlock.ForegroundProperty);

        StartStopButton.Style = (Style)FindResource(chip == ChipState.StartReading ? "PrimaryButton" : "ChipButton");
        AutomationProperties.SetName(StartStopButton, StatusChip.Name(chip));
        StartStopButton.ToolTip = StatusChip.Name(chip);
        Title = StatusChip.Title(chip);
    }

    /// <summary>The status card (spec §3.3): the state line, a line per source, whether alerts go out, and Pause.</summary>
    private void RenderCard(LiveBoard live)
    {
        CardStateLine.Text = StateLine.Text;
        CardSources.ItemsSource = BoardText.CardRows(live);
        var hostDown = live.Snapshots.Values.Any(s => s.State == WatchState.HostDown);
        CardAlertsLine.Text = BoardText.AlertsLine(live.Running, BoardText.Sending(_services.Installed, _services.Sources), hostDown);
        CardDetailLine.Text = DetailLine.Text;
        CardDetailLine.Visibility = DetailLine.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        PauseResumeButton.Content = live.Running ? "Pause reading" : "Resume reading";
        AutomationProperties.SetName(PauseResumeButton, live.Running ? "Pause reading" : "Resume reading");
    }

    private void RenderEmpty(BoardDef board)
    {
        var starters = StarterBoards.All(_services.Installed, _services.Sources);
        // A draft is a board being shaped, not a tab following your sources: with no panels it says so and offers Add panel.
        // A score book that couldn't be read covers every board, with Try again (S1-14.2).
        _empty = BoardText.EmptyFor(starters, board, Editing, bookUnread: _bookProblem is not null && !_services.ReaderLoaded);
        _emptyRecipe = (StarterBoards.Named(starters, board.Follows) ?? StarterBoards.EmptyState(starters)).RecipeSlug;

        var recipe = _services.Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, _emptyRecipe, StringComparison.Ordinal))?.Recipe;
        var (line, detail, button) = BoardText.EmptyState(_empty, recipe, Editing);
        var empty = _empty != BoardEmpty.None;

        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        BoardScroll.Visibility = empty || !_services.ReaderLoaded ? Visibility.Collapsed : Visibility.Visible;
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

        // Spec §4.5: a tab click while arranging saves the draft the way Done does, then switches. A save that fails keeps
        // arranging on this board, and the redraw puts the tab selection back where it was.
        if (Editing)
        {
            FinishEditing();
            if (Editing)
            {
                Render();
                return;
            }
        }

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

        // Its history goes with it (spec §5.1); the delete itself isn't undoable, it asked first.
        _undo.Clear(board.Id);
        if (_toastBoardId == board.Id) HideToast();

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

    /// <summary>
    /// The tabs never take more than their share of what the rest of the bar leaves (<see cref="TabStripShare"/>). The period
    /// line is the star column and trims first; the chip and ⟳ are counted as fixed, so they never give way (spec §3.1).
    /// </summary>
    private void OnTopBarSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 40: the margins between those elements, as the plan works it out. Unmeasured (R1): checked in the walk session.
        var fixedWidth = BoardIcon.ActualWidth + BoardMenuButton.ActualWidth + AddBoardButton.ActualWidth
            + StartStopButton.ActualWidth + TestNowButton.ActualWidth + TopButtons.ActualWidth + 40;
        BoardTabs.MaxWidth = StatusChip.TabBudget(TopBar.ActualWidth, fixedWidth, TabStripShare);
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

    /// <summary>
    /// BC2: a click on the chip opens the status card and never pauses. "Start reading" is the exception, because a
    /// board that has never read has no status to show and starting is what that press means.
    /// </summary>
    private async void OnStatusChipClick(object sender, RoutedEventArgs e)
    {
        var chip = StatusChip.StateOf(_services.Running, _services.EverStarted, _starting, trouble: false);
        if (chip == ChipState.Starting) return;

        if (StatusChip.ClickStarts(chip))
        {
            if (ButtonStates().StartStop) await StartReadingAsync();
            return;
        }

        StatusCard.IsOpen = !StatusCard.IsOpen;
        if (!StatusCard.IsOpen) return;

        // R6c: a throw here must not escape this async void (which would crash the process); it goes to the trail
        // by type only, the way RenderLines already does for the lines under the bar.
        try
        {
            RenderCard(_services.CurrentBoard());
        }
        catch (Exception ex)
        {
            _services.AddTrail($"LINES NOT DRAWN: {ex.GetType().Name}");
        }

        // While the card is open a press on the chip only closes it (StaysOpen="False"): the chip takes no hit until the
        // card has closed, so that same press can't land on it and open the card again.
        StartStopButton.IsHitTestVisible = false;
        // R6d: Pause disabled (e.g. a Test now read in flight) leaves nothing in the card to take focus, so a
        // keyboard user tabbing to it would land back on the page behind the popup and Esc would never reach
        // OnStatusCardKeyDown. The card's own Border is Focusable for exactly that case.
        FocusLater(PauseResumeButton.IsEnabled ? PauseResumeButton : CardBorder);
    }

    /// <summary>Esc closes the card and nothing else: marked handled so Arrange's Cancel (IsCancel) never sees it (Review Focus 4).</summary>
    private void OnStatusCardKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        StatusCard.IsOpen = false;
        e.Handled = true;
    }

    /// <summary>Focus goes back to the chip (spec §3.3); the chip takes presses again once the press that closed the card is done.</summary>
    private void OnStatusCardClosed(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() => { StartStopButton.IsHitTestVisible = true; }, DispatcherPriority.Background);
        FocusLater(StartStopButton);
    }

    /// <summary>
    /// F5 is ⟳, through the same gate (spec §3.5). Pop-outs have no ⟳ and don't take it. Ctrl+Z is undo on the board
    /// on screen, toast or not (BC6, spec §5.3).
    /// </summary>
    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            UndoLast();
            return;
        }

        if (e.Key != Key.F5 || Keyboard.Modifiers != ModifierKeys.None) return;
        e.Handled = true;
        OnTestNowClick(TestNowButton, new RoutedEventArgs());
    }

    /// <summary>
    /// Shows the undo toast for the board on screen, replacing any other (spec §5.3); a live region raises its change
    /// for a screen reader. The clock restarts, so each toast gets its full six seconds.
    /// </summary>
    private void ShowToast(string text, bool canUndo)
    {
        _toastBoardId = _boardId;
        _toastCanUndo = canUndo;
        UndoToastText.Text = text;
        ShowToastUndo();
        UndoToast.Visibility = Visibility.Visible;
        var peer = UIElementAutomationPeer.FromElement(UndoToastText) ?? UIElementAutomationPeer.CreatePeerForElement(UndoToastText);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);

        _toastClock.Stop();
        _toastClock.Start();
    }

    private void HideToast()
    {
        _toastClock.Stop();
        _toastCanUndo = false;
        UndoToast.Visibility = Visibility.Collapsed;
    }

    /// <summary>The toast's Undo shows only while the board it speaks for is on screen (BC6).</summary>
    private void ShowToastUndo() =>
        UndoToastButton.Visibility = _toastCanUndo && _toastBoardId == _boardId ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The toast's Undo is Ctrl+Z, for the board it named; on any other board it does nothing (BC6).</summary>
    private void OnUndoToastClick(object sender, RoutedEventArgs e)
    {
        if (!_toastCanUndo || _toastBoardId != _boardId) return;
        UndoLast();

        // The pressed Undo collapses with the toast that follows; focus goes somewhere that stays.
        FocusLater(Editing ? DoneButton : EditBoardButton);
    }

    /// <summary>Pause or Resume, from the status card (BC2); Pause lasts until Ur Score closes (BC7).</summary>
    private async void OnPauseResumeClick(object sender, RoutedEventArgs e)
    {
        if (_services.Running)
        {
            // Never gated on a Test now read: Stop only ends the timed loops, and the read in flight finishes
            // on its own token, as a Test now pressed while stopped would.
            ClearPressNotes();
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
    /// Start on open, asked again once there is something to read (spec §3.6): the one gate is BoardButtons.StartsLater,
    /// so a board that was paused, or is already starting, is left alone. Never while Ur Score is closing: Setup is an
    /// owned window, so closing the board closes it too, and its Closed must not start reading on the way out.
    /// </summary>
    private async Task StartIfOpenWouldHaveAsync(string when)
    {
        if (_popOutLifecycle.ClosingApp) return;
        if (!BoardButtons.StartsLater(_services.Settings.StartOnOpen, _services.ReaderLoaded, _services.Running,
                _services.EverStarted, _starting, _testing, _services.Installed.Count, _services.Sources.Any(s => s.Enabled)))
        {
            return;
        }

        _services.AddTrail($"START ON OPEN: reading started {when}.");
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

        ClearPressNotes();
        _starting = true;
        // The line says Start is waiting on RoRoRo, for as long as it waits (S1-14.3).
        RenderLines();

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

        ClearPressNotes();
        _testing = true;
        // Drawn from _testing, so a read landing mid-way can't put "Not started." back while this one still reads.
        RenderLines();

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
        var chip = StatusChip.StateOf(_services.Running, _services.EverStarted, _starting, trouble: false);
        StartStopButton.IsEnabled = StatusChip.Enabled(chip, _services.ReaderLoaded, states.StartStop);
        PauseResumeButton.IsEnabled = states.StartStop;
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

        // A disabled IsCancel button ignores Esc, so Esc outside arranging does nothing here (BC5).
        CancelArrangeButton.IsEnabled = states.Cancel;
    }

    /// <summary>What every button, tab and tab menu item takes right now, for <see cref="ApplyButtons"/> and the press guards.</summary>
    /// <remarks>Try again holds the empty state's button for its read the way Import recipe… does for its import.</remarks>
    private BoardButtonStates ButtonStates() =>
        BoardButtons.For(_services.ReaderLoaded, _services.Running, _starting, _testing, _importing || _readingBook, _services.Boards.Count, Editing,
            ShownBoard(_services.Boards));

    private void OnSetupClick(object sender, RoutedEventArgs e) => OpenSetup(null);

    private async void OnEmptyStateClick(object sender, RoutedEventArgs e)
    {
        if (_importing || _readingBook) return;

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

        if (_empty == BoardEmpty.BookUnread)
        {
            await OpenOnTheBookAsync();
            return;
        }

        if (_empty != BoardEmpty.NoRecipes) return;

        _importing = true;
        ApplyButtons();
        var before = new ImportLines(_importNote ?? "", "");
        try
        {
            // Kept, and drawn from, so neither the wait for RoRoRo nor what the import did is drawn over by the next redraw (S1-12.4).
            var outcome = await ImportFlow.RunAsync(this, _services, text =>
            {
                _importNote = text.Length > 0 ? text : before.News.Length > 0 ? before.News : null;
                RenderLines();
            });

            var after = ImportFlow.LinesAfter(before, outcome);
            _importNote = after.Problem.Length > 0 ? after.Problem : after.News.Length > 0 ? after.News : null;
            RenderLines();
            if (outcome is { ChooseSources: true }) OpenSetup(SetupPages.ClansId(outcome.Slug), outcome.Message);
        }
        finally
        {
            _importing = false;
            ApplyButtons();
        }
    }

    /// <param name="note">What the page opens saying, such as an import's result on the Clans page it goes on to (S1-12.4).</param>
    private void OpenSetup(string? page, string? note = null)
    {
        if (_setup is not null)
        {
            if (page is not null) _setup.ShowPage(page, note);
            _setup.Activate();
            return;
        }

        _setup = new SetupWindow(_services, page, note) { Owner = this };
        _setup.Closed += async (_, _) =>
        {
            _setup = null;
            await StartIfOpenWouldHaveAsync("after Setup closed");
        };
        _setup.Show();
    }

    /// <summary>
    /// The window must never die on a cycle. Said on the line until the next Start, Stop or Test now, in plain words: the press's
    /// own redraw used to draw over it at once. The exception's type goes to the trail, which Diagnostics shows — the type
    /// only, as every trail line: the whole exception carries its message and a stack, and a message can carry a path with
    /// the user's name in it or an address with a key in it (S1-14.14).
    /// </summary>
    private void ShowFailure(Exception ex)
    {
        _failed = true;
        _services.AddTrail($"EXCEPTION: {ex.GetType().Name}");
        RenderLines();
    }

    /// <summary>A Start, Stop or Test now press is newer than whatever the last one, or the empty state's import, said.</summary>
    private void ClearPressNotes()
    {
        _failed = false;
        _importNote = null;
    }

    /// <summary>
    /// The main clan's icon on the window, the taskbar and the top bar (backlog V3-S.7), at open and whenever it changes. With no
    /// picture, or one that doesn't decode, the window keeps Ur Score's own, and the top bar keeps the picture's space while a
    /// main clan's picture belongs there (A26). A missing picture is a missing decoration: nothing is said about it.
    /// </summary>
    private void ApplyIcon(WindowIcon icon)
    {
        AutomationProperties.SetName(BoardIcon, icon.Name ?? "");

        var image = icon.File is { } file ? PictureFile.Decode(file) : null;
        if (image is null) ClearValue(IconProperty);
        else Icon = image;

        BoardIcon.Source = image;
        BoardIcon.Visibility = PictureFile.SlotVisibility(icon.HasSlot, image is not null);
    }

    /// <summary>
    /// Closing the board ends Ur Score. While something is being read and sent, that silences the phone alerts, so it
    /// asks first (BC8), and Keep running cancels the close before edit mode's or the pop-outs' own Closing handlers run;
    /// a paused board closes silencing nothing. Nothing sending, or Windows ending the session: it closes as it always has.
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!App.EndingSession
            && BoardText.AsksBeforeClose(_services.Running, BoardText.Sending(_services.Installed, _services.Sources))
            && !ConfirmWindow.Ask(this, BoardText.CloseWhileSending))
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
