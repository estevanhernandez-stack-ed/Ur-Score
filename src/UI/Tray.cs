using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;

namespace Labs626.UrScore.UI;

/// <summary>
/// The tray icon of a start in tray mode (RoRoRo 1.33's autostart; <see cref="Core.LaunchMode"/>), its menu and its tooltip.
/// Built in <c>App.OnStartup</c> once the board exists, only when Ur Score started in the tray, and disposed in <c>OnExit</c>
/// so no ghost icon outlives the app. No test builds one: it is a shell icon. Everything it shows and does is decided by
/// <see cref="TrayModel"/>; this only draws it and hands a click back. Ported from K0ii Score, cut to Open board, Pause or
/// Resume reading, and Quit.
/// <para>
/// <b>Threads.</b> Everything here runs on the UI thread: the menu, a click, and the app's calls on a redraw (the services'
/// <c>Changed</c> is raised there). <see cref="ShowPicture"/> alone may come from elsewhere and is posted.
/// </para>
/// </summary>
public sealed class Tray : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly Dispatcher _ui;
    private readonly Icon? _picture;

    /// <summary>The main clan's picture while the tray wears it (<see cref="ShowPicture"/>); null while it wears Ur Score's own.</summary>
    private Icon? _clanPicture;
    private readonly Func<TrayState> _state;
    private readonly TrayTargets _targets;
    private bool _disposed;

    /// <param name="state">What the menu and tooltip are drawn from, read each time either is.</param>
    /// <param name="targets">Where a chosen line goes; <see cref="TrayTargets.Open"/> is also a left click on the icon.</param>
    public Tray(Func<TrayState> state, TrayTargets targets)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(targets);
        _ui = Dispatcher.CurrentDispatcher;
        _state = state;
        _targets = targets;
        _picture = LoadPicture();
        _icon = new TaskbarIcon
        {
            ToolTipText = TrayModel.AppName,
            Icon = _picture,
            Visibility = Visibility.Visible,
            ContextMenu = new ContextMenu(),
        };

        // A left click opens the board (the owner's ask); the right click is the menu, the shell's default.
        _icon.TrayLeftMouseUp += (_, _) => targets.Open();

        // Built again each time it opens, so Pause or Resume is as of the right-click.
        _icon.PreviewTrayContextMenuOpen += (_, _) => BuildMenu();
        BuildMenu();
        Refresh();
    }

    /// <summary>
    /// The main clan's icon, the one the window and the taskbar wear (<c>AppServices.WindowIcon</c>): at start, and every time
    /// it changes. No file, or one that won't decode, puts Ur Score's own back. From any thread; drawn on the tray's own.
    /// </summary>
    public void ShowPicture(string? file)
    {
        if (!_ui.CheckAccess())
        {
            _ui.BeginInvoke(() => ShowPicture(file));
            return;
        }

        if (_disposed) return;

        var clan = TrayPicture.From(file);
        _icon.Icon = clan ?? _picture;
        _clanPicture?.Dispose();
        _clanPicture = clan;
    }

    /// <summary>The tooltip again, from the state now: on every redraw of the services, which a pause or a start is.</summary>
    public void Refresh()
    {
        if (_disposed) return;
        _icon.ToolTipText = TrayModel.Tooltip(_state().Chip);
    }

    /// <summary>A balloon from the icon: the first close to the tray says where Ur Score went.</summary>
    public void Show(string title, string body)
    {
        if (_disposed) return;
        _icon.ShowBalloonTip(title, body, BalloonIcon.Info);
    }

    private void BuildMenu()
    {
        if (_disposed || _icon.ContextMenu is not { } menu) return;

        var state = _state();
        menu.Items.Clear();
        foreach (var item in TrayModel.Menu(state.Running, state.CanPauseOrResume)) menu.Items.Add(Draw(item));
    }

    private object Draw(TrayItem item)
    {
        if (item.IsSeparator) return new Separator();

        var line = new MenuItem { Header = item.Header, IsEnabled = item.IsEnabled };
        System.Windows.Automation.AutomationProperties.SetName(line, item.Header);
        line.Click += (_, _) =>
        {
            TrayModel.Choose(item, _targets);
            Refresh();
        };
        return line;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.Dispose();
        _picture?.Dispose();
        _clanPicture?.Dispose();
    }

    /// <summary>
    /// The app's own icon (the EXE's), worn until the main clan's is known, or none (the shell then shows a blank slot) if the
    /// resource can't be read.
    /// </summary>
    private static Icon? LoadPicture()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/icon.ico", UriKind.Absolute));
            if (resource is null) return null;
            using var stream = resource.Stream;
            return new Icon(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
