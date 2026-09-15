using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

/// <summary>
/// Pop-out panels (spec §9.3, R18–R20). A panel's saved <c>popout</c> is the truth, on every board, and edit mode
/// doesn't edit it (only removing the panel does): windows follow the saved boards, and Done carries them over
/// (<see cref="BoardEdits.Finish"/>).
/// </summary>
public partial class BoardWindow
{
    private readonly Dictionary<string, PanelPopOutWindow> _popOuts = new(StringComparer.Ordinal);

    /// <summary>Where each panel's window last sat this session, so the next ⧉ opens there (R18).</summary>
    private readonly Dictionary<string, PopOutRect> _lastPopOut = new(StringComparer.Ordinal);

    /// <summary>Where the windows sit is written once moving settles, never for every pixel of a drag.</summary>
    private readonly DispatcherTimer _popOutSave = new() { Interval = TimeSpan.FromMilliseconds(700) };

    /// <summary>Set as Ur Score closes, so the windows it closes keep their <c>popout</c> and reopen next start.</summary>
    private bool _closingApp;

    private void HookPopOuts()
    {
        PanelFrame.SetShowPopOut(BoardPanels, true);
        BoardPanels.AddHandler(PanelFrame.ToolEvent, new EventHandler<PanelToolEventArgs>(OnPopOutTool));

        _popOutSave.Tick += (_, _) =>
        {
            _popOutSave.Stop();
            SavePopOutPositions();
        };

        // After edit mode's own Closing handler has saved the draft: the positions go on top of it.
        Closing += (_, _) =>
        {
            _closingApp = true;
            _popOutSave.Stop();
            SavePopOutPositions();
        };
    }

    private void OnPopOutTool(object? sender, PanelToolEventArgs e)
    {
        if (e.Tool != PanelTool.PopOut || Editing || PanelAt(e.OriginalSource) is not { } def || def.PopOut is not null) return;

        e.Handled = true;
        var areas = WorkAreas();
        var rect = _lastPopOut.TryGetValue(def.Id, out var last)
            ? PopOutPlacement.Clamp(last, areas)
            : PopOutPlacement.Default(BoardRect(), _popOuts.Count, areas);

        ChangeBoard(board => BoardEdits.PopOut(board, def.Id, rect));

        // The ⧉ pressed went with the redraw; focus goes to the slot's Bring back, the keyboard path home.
        FocusToolLater(def.Id, PanelTool.PopOut);
    }

    /// <summary>
    /// One window for every panel that has a <c>popout</c> on any board (R20), less a panel the draft has removed;
    /// a window whose panel lost it, or went, closes without returning anything.
    /// </summary>
    private void SyncPopOuts()
    {
        // Nothing opens while Ur Score closes; the windows it closes then keep their popout.
        if (_closingApp) return;

        var boards = _services.Boards;
        var wanted = new Dictionary<string, PanelDef>(StringComparer.Ordinal);
        foreach (var panel in BoardEdits.PoppedOut(boards, _draft)) wanted.TryAdd(panel.Id, panel);

        foreach (var id in _popOuts.Keys.Where(id => !wanted.ContainsKey(id)).ToList())
        {
            var window = _popOuts[id];
            _popOuts.Remove(id);
            window.Close();
        }

        // A pop-out's panel keeps the id its slot has now, as panels of its type come and go around it; while editing
        // that is the draft's numbering, as the slot's is.
        var shown = _draft is { } draft ? BoardEdits.Replace(boards, draft) : boards;
        foreach (var (id, window) in _popOuts) AutomationProperties.SetAutomationId(window.View, AutomationIdIn(shown, id));

        IReadOnlyList<PopOutRect>? areas = null;
        var live = _services.CurrentBoard();
        foreach (var (id, def) in wanted)
        {
            if (_popOuts.ContainsKey(id) || def.PopOut is not { } rect) continue;

            var view = PanelViews.Create(def.Type);
            AutomationProperties.SetAutomationId(view, AutomationIdIn(shown, id));

            // Drawn with the board's panels, straight after this (RenderPopOuts).
            areas ??= WorkAreas();
            var window = new PanelPopOutWindow(id, view, PopOutPlacement.Clamp(rect, areas));
            window.ShowTitle(PanelGallery.TitleOf(def, live));
            window.Moved += OnPopOutMoved;
            window.Closed += OnPopOutClosed;
            _popOuts[id] = window;
            window.Show();
        }
    }

    /// <summary>Every open pop-out updates live, from the same builders as the board.</summary>
    private void RenderPopOuts(LiveBoard live)
    {
        foreach (var window in _popOuts.Values)
        {
            if (BoardEdits.Find(_services.Boards, window.PanelId) is not { } found) continue;

            RenderPanel(found.Panel, window.View, live);
            window.ShowTitle(PanelGallery.TitleOf(found.Panel, live));
        }
    }

    private void OnPopOutMoved(PanelPopOutWindow window)
    {
        _popOutSave.Stop();
        _popOutSave.Start();
    }

    private void OnPopOutClosed(object? sender, EventArgs e)
    {
        if (sender is not PanelPopOutWindow window) return;

        window.Moved -= OnPopOutMoved;
        window.Closed -= OnPopOutClosed;
        _lastPopOut[window.PanelId] = window.Rect;

        // A window the board closed itself (its panel came back, or went) is no longer tracked, and returns nothing.
        if (!_popOuts.TryGetValue(window.PanelId, out var current) || !ReferenceEquals(current, window)) return;

        _popOuts.Remove(window.PanelId);
        if (!_closingApp) ReturnPanel(window.PanelId);
    }

    /// <summary>
    /// The panel goes back to its board, whichever board that is (spec §9.3: "closing it returns the panel"), and
    /// the draft follows so its slot shows the panel again. A return that isn't saved has said so, and leaves the
    /// panel out: its window opens again.
    /// </summary>
    private void ReturnPanel(string panelId)
    {
        var boards = _services.Boards;
        if (BoardEdits.Find(boards, panelId) is { } found && found.Panel.PopOut is not null
            && !SaveBoards(BoardEdits.Replace(boards, BoardEdits.Return(found.Board, panelId))))
        {
            Render();
            return;
        }

        if (_draft is { } draft && draft.Panels.Any(p => p.Id == panelId && p.PopOut is not null)) _draft = BoardEdits.Return(draft, panelId);
        Render();
    }

    /// <summary>
    /// Every open pop-out's place, saved once moving settles and as Ur Score closes. Never a message box: this runs
    /// under a drag, so a place that can't be written goes to the trail, and the next settle tries again.
    /// </summary>
    private void SavePopOutPositions()
    {
        if (_popOuts.Count == 0) return;

        var boards = _services.Boards;
        var placed = BoardEdits.PlacePopOuts(boards, _popOuts.ToDictionary(p => p.Key, p => p.Value.Rect, StringComparer.Ordinal));
        if (ReferenceEquals(placed, boards)) return;

        try
        {
            _services.SaveBoards(placed);
        }
        catch (Exception ex)
        {
            _services.AddTrail($"POP-OUT PLACES NOT SAVED: {ex.GetType().Name}");
        }
    }

    /// <summary>A popped-out panel's slot on its board (R19): a short card with Bring back, and Remove in edit mode.</summary>
    private FrameworkElement PoppedOutPlaceholder(PanelDef def, string automationId)
    {
        var title = PanelGallery.TitleOf(def, _services.CurrentBoard());
        var slot = new PoppedOutSlot(title, () =>
        {
            ReturnPanel(def.Id);
            FocusToolLater(def.Id, Editing ? PanelTool.MoveEarlier : PanelTool.PopOut);
        });

        AutomationProperties.SetAutomationId(slot, automationId + "PoppedOut");
        return slot;
    }

    /// <summary>A pop-out's panel keeps its board automation id, "StandingPanel1", inside its own window.</summary>
    private static string AutomationIdIn(IReadOnlyList<BoardDef> boards, string panelId) =>
        BoardEdits.AutomationIdOf(boards, panelId) ?? "PopOutPanel";

    private static PopOutRect VirtualScreen() => new(
        SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
        SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

    /// <summary>
    /// Every monitor's work area, the main one first, in device-independent pixels at the board's DPI (R18: a pop-out
    /// must land on a screen, not in a gap between screens of different sizes). Should Windows not say, the whole
    /// virtual screen stands in, and the trail says why.
    /// </summary>
    private IReadOnlyList<PopOutRect> WorkAreas()
    {
        var areas = new List<PopOutRect>();
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (!GetMonitorInfo(monitor, ref info)) return true;

                var work = info.Work;
                var area = new PopOutRect(
                    work.Left / dpi.DpiScaleX, work.Top / dpi.DpiScaleY,
                    (work.Right - work.Left) / dpi.DpiScaleX, (work.Bottom - work.Top) / dpi.DpiScaleY);
                if ((info.Flags & MonitorInfoPrimary) != 0) areas.Insert(0, area);
                else areas.Add(area);
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            _services.AddTrail($"SCREENS NOT LISTED: {ex.GetType().Name}; using the whole virtual screen.");
            areas.Clear();
        }

        return areas.Count > 0 ? areas : [VirtualScreen()];
    }

    private const int MonitorInfoPrimary = 1;

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr clip, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public int Flags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    /// <summary>Where the board is on screen. Maximized, Left and Top are where it goes back to, so its corner comes from its content.</summary>
    private PopOutRect BoardRect()
    {
        if (WindowState != WindowState.Normal && PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
        {
            var corner = target.TransformFromDevice.Transform(PointToScreen(new Point(0, 0)));
            return new PopOutRect(corner.X, corner.Y, ActualWidth, ActualHeight);
        }

        return new PopOutRect(Left, Top, ActualWidth, ActualHeight);
    }

    /// <summary>
    /// What a popped-out panel's slot shows: "… is popped out." with Bring back, and in edit mode Remove, which raises
    /// <see cref="PanelTool.Remove"/> like a panel's own ✕ (R19, R20). Remove follows the inherited
    /// <see cref="PanelFrame.ShowEditToolsProperty"/>, so the slot needn't be rebuilt as edit mode starts and ends.
    /// </summary>
    private sealed class PoppedOutSlot : UserControl
    {
        private readonly Button _bringBack;
        private readonly Button _remove;

        public PoppedOutSlot(string title, Action bringBack)
        {
            var line = new TextBlock { Text = $"{title} is popped out.", VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            line.SetResourceReference(FrameworkElement.StyleProperty, "Muted");

            _bringBack = new Button { Content = "Bring back", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            _bringBack.SetResourceReference(FrameworkElement.StyleProperty, "FlatButton");
            AutomationProperties.SetName(_bringBack, $"Bring back {title}");
            _bringBack.Click += (_, _) => bringBack();

            _remove = new Button { Content = "Remove", Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            _remove.SetResourceReference(FrameworkElement.StyleProperty, "FlatButton");
            AutomationProperties.SetAutomationId(_remove, "RemovePanelButton");
            AutomationProperties.SetName(_remove, $"Remove {title}");
            _remove.SetBinding(VisibilityProperty, new Binding
            {
                RelativeSource = RelativeSource.Self,
                Path = new PropertyPath(PanelFrame.ShowEditToolsProperty),
                Converter = new BooleanToVisibilityConverter(),
            });
            _remove.Click += (_, _) => _remove.RaiseEvent(new PanelToolEventArgs(PanelFrame.ToolEvent, PanelTool.Remove));

            var row = new DockPanel();
            DockPanel.SetDock(_remove, Dock.Right);
            DockPanel.SetDock(_bringBack, Dock.Right);
            row.Children.Add(_remove);
            row.Children.Add(_bringBack);
            row.Children.Add(line);

            var card = new Border { Child = row };
            card.SetResourceReference(FrameworkElement.StyleProperty, "PanelCard");

            // A Border has no automation peer; this UserControl is what UI Automation finds by id.
            Content = card;
        }

        /// <summary>After a redraw under a press: Remove while editing, else Bring back.</summary>
        public void FocusButton(bool editing) => (editing ? _remove : _bringBack).Focus();
    }
}
